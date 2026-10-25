import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:intl/intl.dart';

import '../../core/db/app_database.dart';
import '../../core/providers.dart';
import '../../l10n/generated/app_localizations.dart';
import '../../shared/theme.dart';
import 'menu_screen.dart';
import 'order_providers.dart';

/// Orders of the branch, newest first: this phone's own while they're sending, then what the
/// cashier did with them (pulled as "openOrders").
class OrdersScreen extends ConsumerWidget {
  const OrdersScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context);
    final orders = ref.watch(branchOrdersProvider).value ?? const <LocalOrder>[];
    final userId = ref.watch(sessionControllerProvider).profile?.userId;

    return Scaffold(
      appBar: AppBar(title: Text(l10n.ordersTitle)),
      floatingActionButton: FloatingActionButton.extended(
        key: const Key('orders-new-order'),
        // Home -> Menu, so back from the menu returns home rather than to this list.
        onPressed: () => context.go('/order'),
        icon: const Icon(Icons.add_shopping_cart),
        label: Text(l10n.newOrder),
      ),
      body: RefreshIndicator(
        onRefresh: () => ref.read(syncControllerProvider.notifier).syncNow(),
        child: orders.isEmpty
            ? ListView(children: [
                const SizedBox(height: 120),
                Center(child: Text(l10n.noOrdersYet, style: const TextStyle(color: AppColors.textMuted))),
              ])
            : ListView.separated(
                // Room at the bottom so the New order button never covers the last order.
                padding: const EdgeInsets.fromLTRB(16, 16, 16, 96),
                itemCount: orders.length,
                separatorBuilder: (context, index) => const SizedBox(height: 8),
                itemBuilder: (context, index) => _OrderCard(order: orders[index], mine: orders[index].takenByUserId == userId),
              ),
      ),
    );
  }
}

class _OrderCard extends StatelessWidget {
  const _OrderCard({required this.order, required this.mine});

  final LocalOrder order;
  final bool mine;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final lines = (jsonDecode(order.linesJson) as List).cast<Map<String, dynamic>>();
    final (label, color, soft) = switch (order.status) {
      LocalOrderStatus.sending => (l10n.statusSending, AppColors.textMuted, context.palette.background),
      LocalOrderStatus.rejected => (l10n.statusRejected, AppColors.danger, AppColors.dangerSoft),
      'Closed' || 'Paid' => (l10n.statusPaid, AppColors.success, AppColors.successSoft),
      'Cancelled' => (l10n.statusCancelled, AppColors.danger, AppColors.dangerSoft),
      _ => (l10n.statusToPay, AppColors.warning, AppColors.warningSoft),
    };

    return Card(
      child: Padding(
        padding: const EdgeInsets.all(14),
        child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
            Expanded(
              child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                Text(order.notes ?? l10n.walkIn, style: const TextStyle(fontSize: 17, fontWeight: FontWeight.w700)),
                const SizedBox(height: 2),
                Text(
                  [order.documentNo ?? '…', DateFormat.jm().format(order.orderDateUtc.toLocal()), if (mine) l10n.takenByYou].join(' · '),
                  style: const TextStyle(color: AppColors.textMuted, fontSize: 12),
                ),
              ]),
            ),
            Container(
              padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
              decoration: BoxDecoration(color: soft, borderRadius: BorderRadius.circular(999)),
              child: Text(label, style: TextStyle(color: color, fontWeight: FontWeight.w600, fontSize: 12)),
            ),
          ]),
          const SizedBox(height: 8),
          for (final line in lines)
            Padding(
              padding: const EdgeInsets.only(bottom: 2),
              child: Text('${(line['quantity'] as num).toStringAsFixed(0)} × ${line['productName']}'
                  '${(line['notes'] as String?)?.isNotEmpty == true ? ' (${line['notes']})' : ''}'),
            ),
          if (order.errorMessage != null) ...[
            const SizedBox(height: 6),
            Text(order.errorMessage!, style: const TextStyle(color: AppColors.danger)),
          ],
          const Divider(height: 20),
          Row(children: [
            Text(l10n.total, style: const TextStyle(color: AppColors.textMuted)),
            const Spacer(),
            Text(formatMoney(order.grandTotal), style: const TextStyle(fontWeight: FontWeight.w700)),
          ]),
        ]),
      ),
    );
  }
}
