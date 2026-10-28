import 'dart:convert';

import 'package:dio/dio.dart';
import 'package:drift/native.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:stockorder_mobile/core/auth/token_storage.dart';
import 'package:stockorder_mobile/core/db/app_database.dart';
import 'package:stockorder_mobile/core/providers.dart';
import 'package:stockorder_mobile/core/session/session_controller.dart';
import 'package:stockorder_mobile/features/ordering/cart_screen.dart';
import 'package:stockorder_mobile/features/ordering/menu_screen.dart';
import 'package:stockorder_mobile/features/ordering/orders_screen.dart';
import 'package:stockorder_mobile/features/tables/table_api.dart';
import 'package:stockorder_mobile/features/tables/tables_screen.dart';
import 'package:stockorder_mobile/l10n/generated/app_localizations.dart';
import 'package:stockorder_mobile/shared/theme.dart';

/// Tables (016_RestaurantPack): the waiter's table map, sending a table's round straight to the
/// kitchen, and takeaway for orders taken outside the map.
void main() {
  late AppDatabase db;
  late _FakeTableApi api;
  const glb = 'branch-glb';

  setUp(() async {
    FlutterSecureStorage.setMockInitialValues({});
    api = _FakeTableApi();
    db = AppDatabase(NativeDatabase.memory());
    await db.applyPull(
      tokens: const {},
      settings: const [],
      features: const [(key: 'Restaurant.Tables', isEnabled: true)],
      permissions: const [(code: 'Sales.Order.Create', branchId: glb), (code: 'Restaurant.Kitchen.Serve', branchId: glb)],
      rows: {
        'branches': [
          {'id': glb, 'code': 'GLB', 'name': 'Gulberg', 'isActive': true},
        ],
        'uoms': [
          {'id': 'portion', 'code': 'portion', 'name': 'Portion', 'decimalPlaces': 0, 'isActive': true},
        ],
        'priceLists': [
          {'id': 'menu', 'name': 'Menu', 'isDefault': true, 'branchId': null, 'isActive': true},
        ],
        'products': [
          {
            'id': 'tikka', 'sku': 'ITEM-0001', 'name': 'Tikka Pizza', 'shortName': null, 'description': null, 'categoryId': null,
            'behaviour': 'Recipe', 'baseUomId': 'portion', 'salesUomId': null, 'ownerBranchId': null, 'isActive': true,
          },
        ],
        'priceListItems': [
          {'id': 'p1', 'priceListId': 'menu', 'productId': 'tikka', 'uomId': 'portion', 'minQuantity': 1, 'unitPrice': 1200, 'validFrom': null, 'validTo': null},
        ],
      },
    );
    await db.writeMeta(SyncMetaKeys.maxOfflineHours, '24');
    await db.writeMeta(SyncMetaKeys.lastSuccessfulSyncAtUtc, DateTime.now().toUtc().toIso8601String());
  });

  tearDown(() => db.close());

  Widget app({String initial = '/tables'}) {
    final router = GoRouter(initialLocation: initial, routes: [
      GoRoute(path: '/', builder: (context, state) => const Scaffold(body: Text('HOME')), routes: [
        GoRoute(path: 'tables', builder: (context, state) => const TablesScreen()),
        GoRoute(
          path: 'order',
          builder: (context, state) => const MenuScreen(),
          routes: [GoRoute(path: 'cart', builder: (context, state) => const CartScreen())],
        ),
        GoRoute(path: 'orders', builder: (context, state) => const OrdersScreen()),
      ]),
    ]);
    return ProviderScope(
      overrides: [
        initialDatabaseProvider.overrideWithValue(db),
        sessionControllerProvider.overrideWith(_SignedInSession.new),
        syncControllerProvider.overrideWith(_NoSync.new),
        tableApiProvider.overrideWithValue(api),
      ],
      child: MaterialApp.router(
        theme: buildAppTheme(AppIndustry.restaurant),
        routerConfig: router,
        localizationsDelegates: AppLocalizations.localizationsDelegates,
        supportedLocales: AppLocalizations.supportedLocales,
      ),
    );
  }

  Future<void> settle(WidgetTester tester) async {
    for (var i = 0; i < 5; i++) {
      await tester.runAsync(() => Future<void>.delayed(const Duration(milliseconds: 20)));
      await tester.pump();
    }
    await tester.pump(const Duration(milliseconds: 500));
  }

  Future<T> real<T>(WidgetTester tester, Future<T> Function() work) async => (await tester.runAsync(work)) as T;

  Future<void> unmount(WidgetTester tester) async {
    await tester.pumpWidget(const SizedBox());
    await settle(tester);
  }

  Future<void> addTikkaAndOpenCart(WidgetTester tester) async {
    await tester.tap(find.byKey(const Key('add-tikka')));
    await settle(tester);
    await tester.tap(find.textContaining('View order'));
    await settle(tester);
  }

  testWidgets('the map shows free, taken and bill-asked tables, with a bell when food is ready', (tester) async {
    await tester.pumpWidget(app());
    await settle(tester);

    expect(find.text('Main hall'), findsOneWidget);
    expect(find.text('Free'), findsOneWidget);
    expect(find.text('Taken'), findsOneWidget);
    expect(find.text('Bill asked'), findsOneWidget);
    expect(find.byIcon(Icons.notifications_active), findsOneWidget, reason: 'table 2 has a ready round');
    await unmount(tester);
  });

  testWidgets('a free table: guests, items, a note, and the round goes straight to the kitchen', (tester) async {
    await tester.pumpWidget(app());
    await settle(tester);

    await tester.tap(find.byKey(const Key('table-1')));
    await settle(tester);
    expect(find.text('How many guests?'), findsOneWidget);
    await tester.tap(find.byKey(const Key('guests-4')));
    await settle(tester);

    expect(find.text('Table 1'), findsOneWidget, reason: 'the menu is for the table');
    await addTikkaAndOpenCart(tester);
    expect(find.byKey(const Key('order-label')), findsNothing, reason: 'a table order needs no name');
    await tester.enterText(find.byKey(const Key('round-note')), 'no onions');
    await tester.tap(find.byKey(const Key('send-order')));
    await settle(tester);

    expect(api.opened.single, (tableId: 't1', covers: 4, note: 'no onions', lines: 1));
    expect(find.text('Sent to the kitchen · order #23'), findsOneWidget);
    expect(find.text('Main hall'), findsOneWidget, reason: 'back on the table map');
    expect(await real(tester, db.pendingCommands), isEmpty, reason: 'tables never go through the offline queue');
    await unmount(tester);
  });

  testWidgets('no connection: the round is not sent, says so in red, and the items stay', (tester) async {
    api.failNext = true;
    await tester.pumpWidget(app());
    await settle(tester);

    await tester.tap(find.byKey(const Key('table-1')));
    await settle(tester);
    await tester.tap(find.byKey(const Key('guests-2')));
    await settle(tester);
    await addTikkaAndOpenCart(tester);
    await tester.tap(find.byKey(const Key('send-order')));
    await settle(tester);

    expect(find.byKey(const Key('not-sent')), findsOneWidget);
    expect(find.textContaining('Not sent to the kitchen'), findsOneWidget);
    expect(find.text('Tikka Pizza'), findsOneWidget, reason: 'the cart is kept for another try');

    await tester.tap(find.byKey(const Key('send-order')));
    await settle(tester);
    expect(api.keys.toSet(), hasLength(1), reason: 'the retry reuses the Idempotency-Key, so a lost answer never sends twice');
    expect(find.text('Sent to the kitchen · order #23'), findsOneWidget);
    await unmount(tester);
  });

  testWidgets('a taken table: add a round; if another waiter added one first, it retries on the new version', (tester) async {
    api.staleOnce = true;
    await tester.pumpWidget(app());
    await settle(tester);

    await tester.tap(find.byKey(const Key('table-2')));
    await settle(tester);
    expect(find.text('Food ready'), findsOneWidget);
    expect(find.byKey(const Key('table-served')), findsOneWidget);
    await tester.tap(find.byKey(const Key('table-add')));
    await settle(tester);
    await addTikkaAndOpenCart(tester);
    await tester.tap(find.byKey(const Key('send-order')));
    await settle(tester);

    expect(api.roundEtags, ['etag-old', 'etag-new']);
    expect(find.text('Sent to the kitchen · order #22'), findsOneWidget);
    await unmount(tester);
  });

  testWidgets('with tables on, an order taken from New order is takeaway', (tester) async {
    await tester.pumpWidget(app(initial: '/order'));
    await settle(tester);
    await addTikkaAndOpenCart(tester);
    expect(find.text('Customer name (takeaway)'), findsOneWidget);
    await tester.enterText(find.byKey(const Key('order-label')), 'Ali');
    await tester.tap(find.byKey(const Key('send-order')));
    await settle(tester);

    final payload = jsonDecode((await real(tester, db.pendingCommands)).single.payloadJson) as Map<String, dynamic>;
    expect(payload['channel'], 'Takeaway');
    await unmount(tester);
  });
}

class _FakeTableApi extends TableApi {
  _FakeTableApi() : super(Dio());

  bool failNext = false;
  bool staleOnce = false;
  final opened = <({String tableId, int? covers, String? note, int lines})>[];
  final roundEtags = <String>[];
  final keys = <String>[];

  static TableOrderSummary _summary(String id, int no, {int ready = 0}) => TableOrderSummary(
        orderId: id, orderNo: no, covers: 3, serverName: 'Waiter', openedAtUtc: DateTime.utc(2026, 10, 28, 18), grandTotal: 2400,
        etag: 'etag-old', readyRounds: ready, inKitchenRounds: 0,
      );

  static TableOrder _order(String id, int no, String etag) =>
      TableOrder(id: id, orderNo: no, etag: etag, grandTotal: 2400, covers: 3, lines: const [TableOrderLine(name: 'Tikka Pizza', quantity: 2, lineTotal: 2400)]);

  @override
  Future<List<TableMapArea>> tableMap(String branchId) async => [
        TableMapArea(id: 'a1', name: 'Main hall', tables: [
          const TableMapTable(id: 't1', name: '1', seats: 4, status: 'Free'),
          TableMapTable(id: 't2', name: '2', seats: 4, status: 'Occupied', order: _summary('o2', 22, ready: 1)),
          TableMapTable(id: 't3', name: '3', seats: 6, status: 'BillRequested', order: _summary('o3', 21)),
        ]),
      ];

  @override
  Future<TableOrder> order(String orderId) async => _order(orderId, orderId == 'o2' ? 22 : 21, roundEtags.isEmpty ? 'etag-old' : 'etag-new');

  @override
  Future<TableOrder> openTable({
    required String branchId,
    required String tableId,
    required int? covers,
    required List<TableLine> lines,
    required String? note,
    required String idempotencyKey,
  }) async {
    keys.add(idempotencyKey);
    if (failNext) {
      failNext = false;
      throw DioException(requestOptions: RequestOptions(path: '/api/v1/sales/orders'), type: DioExceptionType.connectionError);
    }
    opened.add((tableId: tableId, covers: covers, note: note, lines: lines.length));
    return _order('o1', 23, 'etag-1');
  }

  @override
  Future<TableOrder> addRound({
    required String orderId,
    required String etag,
    required List<TableLine> lines,
    required String? note,
    required String idempotencyKey,
  }) async {
    roundEtags.add(etag);
    if (staleOnce) {
      staleOnce = false;
      final request = RequestOptions(path: '/api/v1/sales/orders/$orderId/rounds');
      throw DioException(requestOptions: request, response: Response(requestOptions: request, statusCode: 412));
    }
    return _order(orderId, 22, 'etag-2');
  }
}

class _NoSync extends SyncController {
  @override
  Future<void> syncNow() async {}
}

class _SignedInSession extends SessionController {
  @override
  SessionState build() => const SessionState(
        SessionStatus.signedIn,
        profile: UserProfile(
          userId: 'waiter-1', username: 'waiter', displayName: 'Waiter', tenantId: 't', tenantCode: 'QRDEMO', tenantName: 'Lahori Grill',
          mustChangePassword: false,
        ),
      );
}
