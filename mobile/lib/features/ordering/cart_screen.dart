import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../l10n/generated/app_localizations.dart';
import '../../shared/theme.dart';
import 'menu_screen.dart';
import 'order_providers.dart';

/// Review the order, say who it's for, and send it to the cashier.
class CartScreen extends ConsumerStatefulWidget {
  const CartScreen({super.key});

  @override
  ConsumerState<CartScreen> createState() => _CartScreenState();
}

class _CartScreenState extends ConsumerState<CartScreen> {
  late final TextEditingController _label = TextEditingController(text: ref.read(cartProvider).label);
  bool _showLabelError = false;
  bool _sending = false;

  @override
  void dispose() {
    _label.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final cart = ref.watch(cartProvider);
    final controller = ref.read(cartProvider.notifier);

    return Scaffold(
      appBar: AppBar(title: Text(l10n.yourOrder)),
      body: cart.lines.isEmpty
          ? Center(child: Text(l10n.cartEmpty, style: const TextStyle(color: AppColors.textMuted)))
          : ListView(padding: const EdgeInsets.fromLTRB(16, 8, 16, 24), children: [
              TextField(
                key: const Key('order-label'),
                controller: _label,
                textCapitalization: TextCapitalization.sentences,
                decoration: InputDecoration(
                  labelText: l10n.orderLabel,
                  hintText: l10n.orderLabelHint,
                  errorText: _showLabelError && _label.text.trim().isEmpty ? l10n.orderLabelRequired : null,
                ),
                onChanged: controller.setLabel,
              ),
              const SizedBox(height: 16),
              for (final line in cart.lines)
                Card(
                  margin: const EdgeInsets.only(bottom: 8),
                  child: Padding(
                    padding: const EdgeInsets.fromLTRB(12, 4, 4, 4),
                    child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                      Row(children: [
                        Expanded(child: Text(line.item.name, style: const TextStyle(fontWeight: FontWeight.w600))),
                        IconButton(onPressed: () => controller.remove(line.item), icon: const Icon(Icons.remove_circle_outline)),
                        Text('${line.quantity}', style: const TextStyle(fontSize: 16, fontWeight: FontWeight.w700)),
                        IconButton(onPressed: () => controller.add(line.item), icon: const Icon(Icons.add_circle, color: AppColors.accent)),
                      ]),
                      Row(children: [
                        Expanded(
                          child: TextButton.icon(
                            style: TextButton.styleFrom(alignment: Alignment.centerLeft, padding: EdgeInsets.zero),
                            onPressed: () => _editNote(line),
                            icon: const Icon(Icons.edit_note, size: 20),
                            label: Text(
                              (line.note ?? '').isEmpty ? l10n.addNote : line.note!,
                              overflow: TextOverflow.ellipsis,
                              style: TextStyle(color: (line.note ?? '').isEmpty ? AppColors.textMuted : AppColors.warning),
                            ),
                          ),
                        ),
                        Padding(
                          padding: const EdgeInsets.only(right: 12),
                          child: Text(formatMoney(line.lineTotal), style: const TextStyle(fontWeight: FontWeight.w600)),
                        ),
                      ]),
                    ]),
                  ),
                ),
              const SizedBox(height: 8),
              Row(children: [
                Text(l10n.total, style: const TextStyle(fontSize: 18, fontWeight: FontWeight.w700)),
                const Spacer(),
                Text(formatMoney(cart.total), style: const TextStyle(fontSize: 20, fontWeight: FontWeight.w800)),
              ]),
              const SizedBox(height: 4),
              Text(l10n.estimatedTotalHint, style: const TextStyle(color: AppColors.textMuted, fontSize: 12)),
            ]),
      bottomNavigationBar: cart.lines.isEmpty
          ? null
          : SafeArea(
              child: Padding(
                padding: const EdgeInsets.fromLTRB(16, 8, 16, 12),
                child: FilledButton.icon(
                  key: const Key('send-order'),
                  style: FilledButton.styleFrom(minimumSize: const Size.fromHeight(56)),
                  onPressed: _sending ? null : _send,
                  icon: const Icon(Icons.send),
                  label: Text(l10n.sendToCashier),
                ),
              ),
            ),
    );
  }

  Future<void> _editNote(CartLine line) async {
    final l10n = AppLocalizations.of(context);
    final text = TextEditingController(text: line.note);
    final note = await showDialog<String>(
      context: context,
      builder: (context) => AlertDialog(
        title: Text(line.item.name),
        content: TextField(controller: text, autofocus: true, decoration: InputDecoration(labelText: l10n.itemNote), maxLength: 200),
        actions: [
          TextButton(onPressed: () => Navigator.pop(context), child: Text(l10n.cancel)),
          FilledButton(onPressed: () => Navigator.pop(context, text.text.trim()), child: Text(l10n.ok)),
        ],
      ),
    );
    text.dispose();
    if (note != null) ref.read(cartProvider.notifier).setNote(line.item.productId, note);
  }

  Future<void> _send() async {
    final l10n = AppLocalizations.of(context);
    if (_label.text.trim().isEmpty) {
      setState(() => _showLabelError = true);
      return;
    }

    setState(() => _sending = true);
    final result = await ref.read(cartProvider.notifier).submit();
    if (!mounted) return;
    setState(() => _sending = false);

    switch (result) {
      case OrderQueued():
        ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(l10n.orderQueued)));
        context.go('/orders');
      case OrderBlockedOffline():
        ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(l10n.offlineCantOrder)));
    }
  }
}
