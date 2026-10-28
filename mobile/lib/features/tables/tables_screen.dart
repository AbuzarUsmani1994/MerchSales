import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/db/app_database.dart';
import '../../core/providers.dart';
import '../../l10n/generated/app_localizations.dart';
import '../../shared/theme.dart';
import '../ordering/menu_screen.dart';
import '../ordering/order_providers.dart';
import 'table_api.dart';

/// The table map, live from the server (it can't be offline: two waiters must never open the
/// same table, and the kitchen must get rounds now). Refreshed every 10 seconds while open.
final tableMapProvider = FutureProvider.autoDispose<List<TableMapArea>>((ref) async {
  final branch = ref.watch(orderBranchProvider);
  if (branch == null) return const [];
  return ref.watch(tableApiProvider).tableMap(branch.id);
});

final _canServeProvider = StreamProvider<bool>((ref) => ref.watch(databaseProvider).watchHasPermission('Restaurant.Kitchen.Serve'));

const _refreshEvery = Duration(seconds: 10);

/// Waiters (section 14, Phase 2 "waiter table map"): free tables to seat guests at, taken ones
/// with their order, "bill asked", and a bell when the kitchen has food ready for the table.
class TablesScreen extends ConsumerStatefulWidget {
  const TablesScreen({super.key});

  @override
  ConsumerState<TablesScreen> createState() => _TablesScreenState();
}

class _TablesScreenState extends ConsumerState<TablesScreen> {
  Timer? _timer;

  @override
  void initState() {
    super.initState();
    _timer = Timer.periodic(_refreshEvery, (_) => ref.invalidate(tableMapProvider));
  }

  @override
  void dispose() {
    _timer?.cancel();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final branches = ref.watch(orderBranchesProvider).value ?? const <LocalBranch>[];
    final branch = ref.watch(orderBranchProvider);
    final map = ref.watch(tableMapProvider);
    final areas = map.value ?? const <TableMapArea>[];

    return Scaffold(
      appBar: AppBar(
        title: Text(branch == null ? l10n.tablesTitle : '${l10n.tablesTitle} · ${branch.name}'),
        actions: [
          if (branches.length > 1)
            PopupMenuButton<String>(
              tooltip: l10n.chooseBranch,
              icon: const Icon(Icons.storefront_outlined),
              onSelected: (id) => ref.read(databaseProvider).writeMeta(SyncMetaKeys.orderBranchId, id),
              itemBuilder: (context) => [
                for (final b in branches) CheckedPopupMenuItem(value: b.id, checked: b.id == branch?.id, child: Text(b.name)),
              ],
            ),
        ],
      ),
      body: RefreshIndicator(
        onRefresh: () => ref.refresh(tableMapProvider.future),
        child: ListView(
          padding: const EdgeInsets.fromLTRB(16, 8, 16, 24),
          children: [
            if (map.hasError)
              _Banner(icon: Icons.wifi_off, text: l10n.tablesNeedInternet, color: AppColors.danger, background: AppColors.dangerSoft),
            if (map.isLoading && !map.hasValue)
              const Padding(
                padding: EdgeInsets.all(32),
                child: Center(child: CircularProgressIndicator()),
              ),
            if (map.hasValue && areas.isEmpty)
              Padding(
                padding: const EdgeInsets.all(32),
                child: Text(
                  l10n.tablesEmpty,
                  textAlign: TextAlign.center,
                  style: const TextStyle(color: AppColors.textMuted),
                ),
              ),
            for (final area in areas) ...[
              Padding(
                padding: const EdgeInsets.fromLTRB(4, 12, 4, 8),
                child: Text(
                  area.name,
                  style: TextStyle(fontSize: 16, fontWeight: FontWeight.w800, color: context.palette.ink),
                ),
              ),
              GridView.extent(
                shrinkWrap: true,
                physics: const NeverScrollableScrollPhysics(),
                maxCrossAxisExtent: 120,
                mainAxisSpacing: 10,
                crossAxisSpacing: 10,
                childAspectRatio: 0.95,
                children: [for (final t in area.tables) _TableTile(table: t, onTap: () => _open(t))],
              ),
            ],
          ],
        ),
      ),
    );
  }

  Future<void> _open(TableMapTable table) async {
    if (table.isFree) {
      final covers = await showDialog<int>(
        context: context,
        builder: (_) => _GuestsDialog(seats: table.seats),
      );
      if (covers == null || !mounted) return;
      ref.read(cartProvider.notifier).startTable(TableTarget(tableId: table.id, tableName: table.name, covers: covers));
      await context.push('/order');
    } else {
      await showModalBottomSheet<void>(
        context: context,
        isScrollControlled: true,
        showDragHandle: true,
        builder: (_) => _TableOrderSheet(table: table),
      );
    }
    ref.invalidate(tableMapProvider);
  }
}

class _TableTile extends StatelessWidget {
  const _TableTile({required this.table, required this.onTap});

  final TableMapTable table;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final palette = context.palette;
    final order = table.order;
    final ready = (order?.readyRounds ?? 0) > 0;
    final (background, foreground, label) = table.isFree
        ? (Colors.white, palette.ink, l10n.tableFree)
        : table.billRequested
        ? (AppColors.danger, Colors.white, l10n.tableBillAsked)
        : (palette.primary, Colors.white, l10n.tableTaken);

    return Material(
      color: background,
      borderRadius: BorderRadius.circular(16),
      child: InkWell(
        key: Key('table-${table.name}'),
        borderRadius: BorderRadius.circular(16),
        onTap: onTap,
        child: Container(
          decoration: BoxDecoration(
            borderRadius: BorderRadius.circular(16),
            border: Border.all(color: table.isFree ? palette.primary.withValues(alpha: 0.35) : Colors.transparent, width: 1.5),
          ),
          padding: const EdgeInsets.all(8),
          child: Stack(
            children: [
              // Scales down rather than overflowing: Urdu's taller lines, or a large text size on the phone.
              Center(
                child: FittedBox(
                  fit: BoxFit.scaleDown,
                  child: Column(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      FittedBox(
                        child: Text(
                          table.name,
                          style: TextStyle(fontSize: 26, fontWeight: FontWeight.w800, color: foreground),
                        ),
                      ),
                      Text(
                        label,
                        style: TextStyle(fontSize: 12, fontWeight: FontWeight.w600, color: foreground.withValues(alpha: 0.85)),
                      ),
                      if (order != null)
                        Row(
                          mainAxisSize: MainAxisSize.min,
                          children: [
                            if (order.orderNo != null)
                              Text('#${order.orderNo}  ', style: TextStyle(fontSize: 12, color: foreground.withValues(alpha: 0.85))),
                            if (order.covers != null) ...[
                              Icon(Icons.person, size: 12, color: foreground.withValues(alpha: 0.85)),
                              Text('${order.covers}', style: TextStyle(fontSize: 12, color: foreground.withValues(alpha: 0.85))),
                            ],
                          ],
                        ),
                    ],
                  ),
                ),
              ),
              if (ready)
                PositionedDirectional(
                  top: 0,
                  end: 0,
                  child: Container(
                    padding: const EdgeInsets.all(4),
                    decoration: const BoxDecoration(color: AppColors.success, shape: BoxShape.circle),
                    child: const Icon(Icons.notifications_active, size: 16, color: Colors.white),
                  ),
                ),
            ],
          ),
        ),
      ),
    );
  }
}

class _GuestsDialog extends StatelessWidget {
  const _GuestsDialog({required this.seats});

  final int seats;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final options = [for (var i = 1; i <= (seats < 8 ? 8 : seats).clamp(1, 16); i++) i];
    return AlertDialog(
      title: Text(l10n.guestsQuestion),
      content: Wrap(
        spacing: 8,
        runSpacing: 8,
        children: [
          for (final n in options)
            SizedBox(
              width: 52,
              height: 48,
              child: OutlinedButton(
                key: Key('guests-$n'),
                style: OutlinedButton.styleFrom(padding: EdgeInsets.zero),
                onPressed: () => Navigator.pop(context, n),
                child: Text('$n', style: const TextStyle(fontSize: 18, fontWeight: FontWeight.w700)),
              ),
            ),
        ],
      ),
      actions: [TextButton(onPressed: () => Navigator.pop(context), child: Text(l10n.cancel))],
    );
  }
}

/// A taken table: what it has ordered, and the waiter's actions on it.
class _TableOrderSheet extends ConsumerStatefulWidget {
  const _TableOrderSheet({required this.table});

  final TableMapTable table;

  @override
  ConsumerState<_TableOrderSheet> createState() => _TableOrderSheetState();
}

class _TableOrderSheetState extends ConsumerState<_TableOrderSheet> {
  TableOrder? _order;
  String? _error;
  bool _busy = false;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    try {
      final order = await ref.read(tableApiProvider).order(widget.table.order!.orderId);
      if (mounted) setState(() => _order = order);
    } on DioException {
      if (mounted) setState(() => _error = AppLocalizations.of(context).tablesNeedInternet);
    }
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final table = widget.table;
    final summary = table.order!;
    final order = _order;
    final canServe = ref.watch(_canServeProvider).value ?? false;

    return SafeArea(
      child: Padding(
        padding: const EdgeInsets.fromLTRB(20, 0, 20, 16),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Row(
              children: [
                Text(
                  l10n.tableName(table.name),
                  style: TextStyle(fontSize: 22, fontWeight: FontWeight.w800, color: context.palette.ink),
                ),
                const SizedBox(width: 10),
                if (summary.orderNo != null) Text(l10n.orderNumber(summary.orderNo!), style: const TextStyle(color: AppColors.textMuted)),
                const Spacer(),
                if (summary.covers != null) Text(l10n.guestsCount(summary.covers!), style: const TextStyle(color: AppColors.textMuted)),
              ],
            ),
            const SizedBox(height: 6),
            Wrap(
              spacing: 8,
              children: [
                if (table.billRequested) _Chip(text: l10n.tableBillAsked, color: AppColors.danger),
                if (summary.readyRounds > 0) _Chip(text: l10n.tableFoodReady, color: AppColors.success),
                if (summary.inKitchenRounds > 0) _Chip(text: l10n.inKitchen(summary.inKitchenRounds), color: AppColors.warning),
              ],
            ),
            const SizedBox(height: 8),
            if (_error != null) _Banner(icon: Icons.wifi_off, text: _error!, color: AppColors.danger, background: AppColors.dangerSoft),
            if (order == null && _error == null)
              const Padding(
                padding: EdgeInsets.all(24),
                child: Center(child: CircularProgressIndicator()),
              ),
            if (order != null)
              ConstrainedBox(
                constraints: BoxConstraints(maxHeight: MediaQuery.sizeOf(context).height * 0.4),
                child: ListView(
                  shrinkWrap: true,
                  children: [
                    for (final line in order.lines)
                      Padding(
                        padding: const EdgeInsets.symmetric(vertical: 4),
                        child: Row(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            SizedBox(
                              width: 36,
                              child: Text('${line.quantity.toStringAsFixed(0)}×', style: const TextStyle(fontWeight: FontWeight.w700)),
                            ),
                            Expanded(
                              child: Column(
                                crossAxisAlignment: CrossAxisAlignment.start,
                                children: [
                                  Text(line.name),
                                  if (line.notes != null) Text(line.notes!, style: const TextStyle(fontSize: 12, color: AppColors.warning)),
                                ],
                              ),
                            ),
                            Text(formatMoney(line.lineTotal)),
                          ],
                        ),
                      ),
                    const Divider(),
                    Row(
                      children: [
                        Text(l10n.total, style: const TextStyle(fontWeight: FontWeight.w700)),
                        const Spacer(),
                        Text(formatMoney(order.grandTotal), style: const TextStyle(fontSize: 18, fontWeight: FontWeight.w800)),
                      ],
                    ),
                  ],
                ),
              ),
            const SizedBox(height: 12),
            if (canServe && summary.readyRounds > 0)
              Padding(
                padding: const EdgeInsets.only(bottom: 8),
                child: FilledButton.icon(
                  key: const Key('table-served'),
                  style: FilledButton.styleFrom(backgroundColor: AppColors.success, minimumSize: const Size.fromHeight(48)),
                  onPressed: _busy ? null : _served,
                  icon: const Icon(Icons.room_service),
                  label: Text(l10n.markServed),
                ),
              ),
            Row(
              children: [
                Expanded(
                  child: OutlinedButton.icon(
                    key: const Key('table-bill'),
                    style: OutlinedButton.styleFrom(minimumSize: const Size.fromHeight(48)),
                    onPressed: _busy || order == null || table.billRequested ? null : _askForBill,
                    icon: const Icon(Icons.receipt_long),
                    label: Text(l10n.askForBill),
                  ),
                ),
                const SizedBox(width: 12),
                Expanded(
                  child: FilledButton.icon(
                    key: const Key('table-add'),
                    style: FilledButton.styleFrom(minimumSize: const Size.fromHeight(48)),
                    onPressed: _busy || order == null ? null : _addItems,
                    icon: const Icon(Icons.add),
                    label: Text(l10n.addItems),
                  ),
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }

  void _addItems() {
    final order = _order!;
    final table = widget.table;
    ref
        .read(cartProvider.notifier)
        .startTable(
          TableTarget(
            tableId: table.id,
            tableName: table.name,
            covers: order.covers,
            orderId: order.id,
            etag: order.etag,
            orderNo: order.orderNo,
          ),
        );
    final router = GoRouter.of(context);
    Navigator.pop(context);
    router.push('/order');
  }

  Future<void> _askForBill() =>
      _act(() => ref.read(tableApiProvider).requestBill(_order!.id, _order!.etag), AppLocalizations.of(context).billAskedDone);

  Future<void> _served() => _act(() => ref.read(tableApiProvider).serveReady(_order!.id), null);

  Future<void> _act(Future<void> Function() action, String? done) async {
    final l10n = AppLocalizations.of(context);
    final messenger = ScaffoldMessenger.of(context);
    setState(() => _busy = true);
    try {
      await action();
      if (!mounted) return;
      Navigator.pop(context);
      if (done != null) messenger.showSnackBar(SnackBar(content: Text(done)));
    } on DioException catch (e) {
      if (!mounted) return;
      setState(() {
        _busy = false;
        _error = e.response?.statusCode == 412 ? l10n.tableChanged : l10n.tablesNeedInternet;
      });
      if (e.response?.statusCode == 412) await _load();
    }
  }
}

class _Chip extends StatelessWidget {
  const _Chip({required this.text, required this.color});

  final String text;
  final Color color;

  @override
  Widget build(BuildContext context) => Container(
    padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
    decoration: BoxDecoration(color: color.withValues(alpha: 0.12), borderRadius: BorderRadius.circular(999)),
    child: Text(
      text,
      style: TextStyle(color: color, fontWeight: FontWeight.w700, fontSize: 12),
    ),
  );
}

class _Banner extends StatelessWidget {
  const _Banner({required this.icon, required this.text, required this.color, required this.background});

  final IconData icon;
  final String text;
  final Color color;
  final Color background;

  @override
  Widget build(BuildContext context) => Container(
    margin: const EdgeInsets.only(bottom: 12),
    padding: const EdgeInsets.all(12),
    decoration: BoxDecoration(color: background, borderRadius: BorderRadius.circular(12)),
    child: Row(
      children: [
        Icon(icon, color: color),
        const SizedBox(width: 10),
        Expanded(
          child: Text(
            text,
            style: TextStyle(color: color, fontWeight: FontWeight.w600),
          ),
        ),
      ],
    ),
  );
}
