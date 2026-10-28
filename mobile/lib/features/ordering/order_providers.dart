import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:uuid/uuid.dart';

import '../../core/db/app_database.dart';
import '../../core/providers.dart';
import '../../core/sync/offline_policy.dart';
import '../tables/table_api.dart';
import 'phone_numbers.dart';

/// Restaurants with tables switched on: waiters seat guests at tables, and "New order" is takeaway.
final tablesEnabledProvider = StreamProvider<bool>((ref) => ref.watch(databaseProvider).watchFeature('Restaurant.Tables'));

/// Branches this user takes orders at.
final orderBranchesProvider = StreamProvider<List<LocalBranch>>((ref) => ref.watch(databaseProvider).watchOrderBranches());

final _savedBranchProvider = StreamProvider<String?>((ref) => ref.watch(databaseProvider).watchMeta(SyncMetaKeys.orderBranchId));

/// The branch the phone takes orders for: the one last picked, else the first allowed.
final orderBranchProvider = Provider<LocalBranch?>((ref) {
  final branches = ref.watch(orderBranchesProvider).value ?? const [];
  if (branches.isEmpty) return null;
  final saved = ref.watch(_savedBranchProvider).value;
  return branches.where((b) => b.id == saved).firstOrNull ?? branches.first;
});

/// Business "today" for price validity; the server decides the real business date.
String _today() {
  final now = DateTime.now();
  return '${now.year.toString().padLeft(4, '0')}-${now.month.toString().padLeft(2, '0')}-${now.day.toString().padLeft(2, '0')}';
}

final menuProvider = StreamProvider<List<MenuEntry>>((ref) {
  final branch = ref.watch(orderBranchProvider);
  if (branch == null) return Stream.value(const []);
  return ref.watch(databaseProvider).watchMenu(branch.id, _today());
});

final branchOrdersProvider = StreamProvider<List<LocalOrder>>((ref) {
  final branch = ref.watch(orderBranchProvider);
  if (branch == null) return Stream.value(const []);
  return ref.watch(databaseProvider).watchOrders(branch.id);
});

class CartLine {
  const CartLine({required this.item, required this.quantity, this.note});

  final MenuEntry item;
  final int quantity;
  final String? note;

  double get lineTotal => (item.price ?? 0) * quantity;

  CartLine copyWith({int? quantity, String? note}) => CartLine(item: item, quantity: quantity ?? this.quantity, note: note ?? this.note);
}

/// A table order the cart is for: a new order at a free table, or the next round of an open one.
class TableTarget {
  const TableTarget({required this.tableId, required this.tableName, this.covers, this.orderId, this.etag, this.orderNo});

  final String tableId;
  final String tableName;
  final int? covers;

  /// Set when adding a round to the table's open order.
  final String? orderId;
  final String? etag;
  final int? orderNo;

  bool get isRound => orderId != null;
}

class Cart {
  const Cart({this.lines = const [], this.label = '', this.phone = '', this.table});

  final List<CartLine> lines;

  /// Set while ordering for a table: the cart then goes straight to the kitchen, not the offline queue.
  final TableTarget? table;

  /// A visit-reward member's phone, for the cashier (only where the programme runs).
  final String phone;

  /// Who the order is for ("Table 4"); goes to the order's Notes.
  final String label;

  int get itemCount => lines.fold(0, (sum, l) => sum + l.quantity);
  double get total => lines.fold(0.0, (sum, l) => sum + l.lineTotal);
  int quantityOf(String productId) => lines.where((l) => l.item.productId == productId).firstOrNull?.quantity ?? 0;
}

sealed class SubmitOrderResult {
  const SubmitOrderResult();
}

final class OrderQueued extends SubmitOrderResult {
  const OrderQueued();
}

final class OrderBlockedOffline extends SubmitOrderResult {
  const OrderBlockedOffline();
}

/// The order being built. Whole units only: menu items are sold per portion / piece.
class CartController extends Notifier<Cart> {
  @override
  Cart build() {
    // A different branch has a different menu and prices: start over.
    ref.watch(orderBranchProvider.select((b) => b?.id));
    return const Cart();
  }

  void add(MenuEntry item) => _setQuantity(item, state.quantityOf(item.productId) + 1);

  void remove(MenuEntry item) => _setQuantity(item, state.quantityOf(item.productId) - 1);

  void setPhone(String phone) => state = Cart(lines: state.lines, label: state.label, phone: phone, table: state.table);

  /// Start ordering for a table (a fresh cart).
  void startTable(TableTarget table) {
    _sendKey = null;
    state = Cart(table: table);
  }

  void setNote(String productId, String note) => state = Cart(
        label: state.label,
        phone: state.phone,
        table: state.table,
        lines: [for (final l in state.lines) l.item.productId == productId ? CartLine(item: l.item, quantity: l.quantity, note: note) : l],
      );

  void setLabel(String label) => state = Cart(lines: state.lines, label: label, phone: state.phone, table: state.table);

  void clear() {
    _sendKey = null;
    state = const Cart();
  }

  void _setQuantity(MenuEntry item, int quantity) {
    final lines = [...state.lines];
    final index = lines.indexWhere((l) => l.item.productId == item.productId);
    if (quantity <= 0) {
      if (index >= 0) lines.removeAt(index);
    } else if (index >= 0) {
      lines[index] = lines[index].copyWith(quantity: quantity);
    } else {
      lines.add(CartLine(item: item, quantity: quantity));
    }
    _sendKey = null;
    state = Cart(lines: lines, label: state.label, phone: state.phone, table: state.table);
  }

  /// One key per round until it's sent: a retry after a lost answer replays, it doesn't send twice.
  String? _sendKey;

  /// A table's round goes straight to the server (and so the kitchen); never queued offline,
  /// where it could reach the kitchen hours late. The cart is kept when it fails.
  Future<TableSendResult> sendToKitchen(String? note) async {
    final branch = ref.read(orderBranchProvider);
    final table = state.table;
    if (branch == null || table == null || state.lines.isEmpty) return const TableNotSent(null);

    final lines = [
      for (final l in state.lines) (productId: l.item.productId, quantity: l.quantity, notes: (l.note ?? '').trim().isEmpty ? null : l.note!.trim()),
    ];
    final roundNote = (note ?? '').trim().isEmpty ? null : note!.trim();
    final key = _sendKey ??= const Uuid().v4();
    final api = ref.read(tableApiProvider);
    Future<TableOrder> send(String? etag) => table.isRound
        ? api.addRound(orderId: table.orderId!, etag: etag!, lines: lines, note: roundNote, idempotencyKey: key)
        : api.openTable(branchId: branch.id, tableId: table.tableId, covers: table.covers, lines: lines, note: roundNote, idempotencyKey: key);
    try {
      TableOrder order;
      try {
        order = await send(table.etag);
      } on DioException catch (e) {
        // Another waiter added to this table a moment ago: adding is safe to repeat on the new version.
        if (!table.isRound || e.response?.statusCode != 412) rethrow;
        order = await send((await api.order(table.orderId!)).etag);
      }
      _sendKey = null;
      state = const Cart();
      // The phone's order list catches up at the next sync; start one now.
      ref.read(syncControllerProvider.notifier).syncNow();
      return TableSent(order.orderNo);
    } on DioException catch (e) {
      final status = e.response?.statusCode;
      // 409: someone opened this table first, or the table was paid meanwhile. The items stay in the cart.
      if (status == 409) {
        _sendKey = null;
        return const TableChanged();
      }
      final data = e.response?.data;
      return TableNotSent(data is Map ? data['detail'] as String? : null);
    }
  }

  /// Saves the order and its CreateSalesOrder command locally (section 14) and asks for a sync.
  Future<SubmitOrderResult> submit() async {
    final db = ref.read(databaseProvider);
    final branch = ref.read(orderBranchProvider);
    final profile = ref.read(sessionControllerProvider).profile;
    if (branch == null || profile == null || state.lines.isEmpty) return const OrderBlockedOffline();

    final last = DateTime.tryParse(await db.readMeta(SyncMetaKeys.lastSuccessfulSyncAtUtc) ?? '');
    final hours = int.tryParse(await db.readMeta(SyncMetaKeys.maxOfflineHours) ?? '') ?? 24;
    if (!OfflinePolicy(lastSuccessfulSyncAtUtc: last, maxOfflineHours: hours).allowsNewTransactions(DateTime.now().toUtc())) {
      return const OrderBlockedOffline();
    }

    const uuid = Uuid();
    final orderId = uuid.v4();
    final label = state.label.trim();
    final lines = state.lines;
    await db.createLocalOrder(
      orderId: orderId,
      commandId: uuid.v4(),
      branchId: branch.id,
      notes: label.isEmpty ? null : label,
      estimatedTotal: state.total,
      takenByUserId: profile.userId,
      lines: [
        for (final l in lines)
          {
            'productId': l.item.productId,
            'productName': l.item.name,
            'quantity': l.quantity,
            'unitPrice': l.item.price,
            'lineTotal': l.lineTotal,
            'notes': l.note,
          },
      ],
      payload: {
        'id': orderId,
        'branchId': branch.id,
        // With tables on, dine-in goes through the table map; an order taken here is takeaway.
        'channel': await db.readFeature('Restaurant.Tables') ? 'Takeaway' : 'DineIn',
        'notes': label.isEmpty ? null : label,
        if (await loyaltyEnabledAt(db, branch.id) && normalizePhone(state.phone) != null) 'customerPhone': state.phone.trim(),
        'lines': [
          for (final l in lines) {'productId': l.item.productId, 'quantity': l.quantity, 'notes': (l.note ?? '').trim().isEmpty ? null : l.note!.trim()},
        ],
      },
      nowUtc: DateTime.now().toUtc(),
    );
    ref.read(sessionControllerProvider.notifier).commandQueued();
    state = const Cart();
    return const OrderQueued();
  }
}

final cartProvider = NotifierProvider<CartController, Cart>(CartController.new);

sealed class TableSendResult {
  const TableSendResult();
}

final class TableSent extends TableSendResult {
  const TableSent(this.orderNo);
  final int? orderNo;
}

/// Someone else changed the table first; the waiter looks again and resends.
final class TableChanged extends TableSendResult {
  const TableChanged();
}

/// No connection, or the server refused it ([detail] says why when it answered).
final class TableNotSent extends TableSendResult {
  const TableNotSent(this.detail);
  final String? detail;
}

Future<bool> loyaltyEnabledAt(AppDatabase db, String branchId) async => await db.readSetting(loyaltyEnabledKey(branchId)) == 'true';

/// Whether visit rewards run at the phone's branch: the order screen then asks for the customer's phone.
final loyaltyEnabledProvider = StreamProvider<bool>((ref) {
  final branch = ref.watch(orderBranchProvider);
  if (branch == null) return Stream.value(false);
  return ref.watch(databaseProvider).watchSetting(loyaltyEnabledKey(branch.id)).map((v) => v == 'true');
});
