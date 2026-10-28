import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../l10n/generated/app_localizations.dart';
import '../../shared/theme.dart';
import 'menu_screen.dart';
import 'order_providers.dart';
import 'phone_numbers.dart';
import 'product_image.dart';

/// Review the order and send it: a table's round straight to the kitchen (online only), anything
/// else to the cashier through the offline queue.
class CartScreen extends ConsumerStatefulWidget {
  const CartScreen({super.key});

  @override
  ConsumerState<CartScreen> createState() => _CartScreenState();
}

class _CartScreenState extends ConsumerState<CartScreen> {
  // Created up front: a lazily created controller would first be touched in dispose(), where ref can't be read.
  late final TextEditingController _label;
  late final TextEditingController _phone;
  late final TextEditingController _note;
  bool _showLabelError = false;
  bool _sending = false;

  /// Why the last send to the kitchen failed; shown in red until the next try.
  String? _notSent;

  @override
  void initState() {
    super.initState();
    final cart = ref.read(cartProvider);
    _label = TextEditingController(text: cart.label);
    _phone = TextEditingController(text: cart.phone);
    _note = TextEditingController();
  }

  @override
  void dispose() {
    _label.dispose();
    _phone.dispose();
    _note.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final cart = ref.watch(cartProvider);
    final controller = ref.read(cartProvider.notifier);
    final table = cart.table;
    final takeaway = table == null && (ref.watch(tablesEnabledProvider).value ?? false);

    return Scaffold(
      appBar: AppBar(title: Text(table == null ? l10n.yourOrder : l10n.tableName(table.tableName))),
      body: cart.lines.isEmpty
          ? Center(child: Text(l10n.cartEmpty, style: const TextStyle(color: AppColors.textMuted)))
          : ListView(padding: const EdgeInsets.fromLTRB(16, 8, 16, 24), children: [
              if (_notSent != null)
                Container(
                  key: const Key('not-sent'),
                  margin: const EdgeInsets.only(bottom: 12),
                  padding: const EdgeInsets.all(12),
                  decoration: BoxDecoration(color: AppColors.dangerSoft, borderRadius: BorderRadius.circular(12)),
                  child: Row(children: [
                    const Icon(Icons.wifi_off, color: AppColors.danger),
                    const SizedBox(width: 10),
                    Expanded(child: Text(_notSent!, style: const TextStyle(color: AppColors.danger, fontWeight: FontWeight.w600))),
                  ]),
                ),
              if (table != null)
                TextField(
                  key: const Key('round-note'),
                  controller: _note,
                  textCapitalization: TextCapitalization.sentences,
                  maxLength: 200,
                  decoration: InputDecoration(labelText: l10n.roundNote, prefixIcon: const Icon(Icons.sticky_note_2_outlined)),
                )
              else
                TextField(
                  key: const Key('order-label'),
                  controller: _label,
                  textCapitalization: TextCapitalization.sentences,
                  decoration: InputDecoration(
                    labelText: takeaway ? l10n.takeawayLabel : l10n.orderLabel,
                    hintText: takeaway ? l10n.takeawayLabelHint : l10n.orderLabelHint,
                    errorText: _showLabelError && _label.text.trim().isEmpty ? l10n.orderLabelRequired : null,
                  ),
                  onChanged: controller.setLabel,
                ),
              if (table == null && (ref.watch(loyaltyEnabledProvider).value ?? false)) ...[
                const SizedBox(height: 12),
                TextField(
                  key: const Key('order-phone'),
                  controller: _phone,
                  keyboardType: TextInputType.phone,
                  decoration: InputDecoration(
                    labelText: l10n.customerPhone,
                    hintText: l10n.customerPhoneHint,
                    prefixIcon: const Icon(Icons.loyalty_outlined),
                    errorText: _phoneInvalid ? l10n.customerPhoneInvalid : null,
                  ),
                  onChanged: (value) {
                    controller.setPhone(value);
                    if (_phoneInvalid) setState(() {});
                  },
                ),
              ],
              const SizedBox(height: 16),
              for (final line in cart.lines)
                Card(
                  margin: const EdgeInsets.only(bottom: 8),
                  child: Padding(
                    padding: const EdgeInsets.fromLTRB(12, 4, 4, 4),
                    child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                      Row(children: [
                        ProductImage(productId: line.item.productId, attachmentId: line.item.imageAttachmentId, name: line.item.name, size: 40),
                        const SizedBox(width: 10),
                        Expanded(child: Text(line.item.name, style: const TextStyle(fontWeight: FontWeight.w600))),
                        IconButton(onPressed: () => controller.remove(line.item), icon: const Icon(Icons.remove_circle_outline)),
                        Text('${line.quantity}', style: const TextStyle(fontSize: 16, fontWeight: FontWeight.w700)),
                        IconButton(onPressed: () => controller.add(line.item), icon: Icon(Icons.add_circle, color: context.palette.primary)),
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
                  onPressed: _sending ? null : (table == null ? _send : _sendToKitchen),
                  icon: Icon(table == null ? Icons.send : Icons.soup_kitchen),
                  label: Text(table == null ? l10n.sendToCashier : l10n.sendToKitchen),
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

  Future<void> _sendToKitchen() async {
    final l10n = AppLocalizations.of(context);
    setState(() {
      _sending = true;
      _notSent = null;
    });
    final result = await ref.read(cartProvider.notifier).sendToKitchen(_note.text);
    if (!mounted) return;
    setState(() => _sending = false);

    switch (result) {
      case TableSent(:final orderNo):
        ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(orderNo == null ? l10n.orderQueued : l10n.sentToKitchen(orderNo))));
        context.go('/tables');
      case TableChanged():
        setState(() => _notSent = l10n.tableChanged);
      case TableNotSent(:final detail):
        setState(() => _notSent = detail ?? l10n.notSentToKitchen);
    }
  }

  bool _showPhoneError = false;

  /// A typed phone that isn't a usable mobile number (an empty one is fine: it's optional).
  bool get _phoneInvalid => _showPhoneError && _phone.text.trim().isNotEmpty && normalizePhone(_phone.text) == null;

  Future<void> _send() async {
    final l10n = AppLocalizations.of(context);
    if (_label.text.trim().isEmpty) {
      setState(() => _showLabelError = true);
      return;
    }

    final loyaltyOn = ref.read(loyaltyEnabledProvider).value ?? false;
    if (loyaltyOn && _phone.text.trim().isNotEmpty && normalizePhone(_phone.text) == null) {
      setState(() => _showPhoneError = true);
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
