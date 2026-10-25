import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:intl/intl.dart';

import '../../core/db/app_database.dart';
import '../../core/providers.dart';
import '../../l10n/generated/app_localizations.dart';
import '../../shared/theme.dart';
import 'order_providers.dart';
import 'product_image.dart';

final _money = NumberFormat.decimalPattern();

String formatMoney(double value) => _money.format(value);

/// New order (section 14 "sales staff"): the branch's menu from the local database, grouped by
/// category, with a running cart. Works offline; prices are what the phone last synced.
class MenuScreen extends ConsumerStatefulWidget {
  const MenuScreen({super.key});

  @override
  ConsumerState<MenuScreen> createState() => _MenuScreenState();
}

class _MenuScreenState extends ConsumerState<MenuScreen> {
  String _search = '';
  String? _category;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final branches = ref.watch(orderBranchesProvider).value ?? const <LocalBranch>[];
    final branch = ref.watch(orderBranchProvider);
    final menu = ref.watch(menuProvider).value ?? const <MenuEntry>[];
    final cart = ref.watch(cartProvider);

    final categories = <String?, String>{};
    for (final item in menu) {
      categories.putIfAbsent(item.categoryId, () => item.categoryName ?? l10n.otherCategory);
    }
    final term = _search.trim().toLowerCase();
    final visible = menu
        .where(
          (i) =>
              (_category == null || i.categoryId == _category) &&
              (term.isEmpty || i.name.toLowerCase().contains(term) || (i.shortName?.toLowerCase().contains(term) ?? false)),
        )
        .toList();

    return Scaffold(
      appBar: AppBar(
        title: Text(branch == null ? l10n.menuTitle : '${l10n.menuTitle} · ${branch.name}'),
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
      body: branch == null
          ? _Message(icon: Icons.storefront_outlined, title: l10n.noOrderBranch)
          : RefreshIndicator(
              onRefresh: () => ref.read(syncControllerProvider.notifier).syncNow(),
              child: CustomScrollView(
                slivers: [
                  SliverToBoxAdapter(
                    child: Padding(
                      padding: const EdgeInsets.fromLTRB(16, 8, 16, 4),
                      child: TextField(
                        decoration: InputDecoration(prefixIcon: const Icon(Icons.search), hintText: l10n.searchMenu, isDense: true),
                        onChanged: (v) => setState(() => _search = v),
                      ),
                    ),
                  ),
                  if (categories.length > 1)
                    SliverToBoxAdapter(
                      child: SizedBox(
                        height: 48,
                        child: ListView(
                          scrollDirection: Axis.horizontal,
                          padding: const EdgeInsets.symmetric(horizontal: 12),
                          children: [
                            _CategoryChip(
                              label: l10n.allCategories,
                              selected: _category == null,
                              onTap: () => setState(() => _category = null),
                            ),
                            for (final entry in categories.entries)
                              _CategoryChip(
                                label: entry.value,
                                selected: _category == entry.key && entry.key != null,
                                onTap: () => setState(() => _category = entry.key),
                              ),
                          ],
                        ),
                      ),
                    ),
                  if (menu.isEmpty)
                    SliverFillRemaining(
                      hasScrollBody: false,
                      child: _Message(icon: Icons.restaurant_menu, title: l10n.menuEmptyTitle, body: l10n.menuEmptyBody),
                    )
                  else if (context.palette.menuAsGrid)
                    // Restaurants: a picture grid, two columns on a phone, more on a tablet.
                    SliverPadding(
                      padding: const EdgeInsets.fromLTRB(16, 4, 16, 0),
                      sliver: SliverGrid.builder(
                        gridDelegate: const SliverGridDelegateWithMaxCrossAxisExtent(
                          maxCrossAxisExtent: 220,
                          mainAxisSpacing: 12,
                          crossAxisSpacing: 12,
                          childAspectRatio: 0.72,
                        ),
                        itemCount: visible.length,
                        itemBuilder: (context, index) =>
                            _MenuCard(item: visible[index], quantity: cart.quantityOf(visible[index].productId)),
                      ),
                    )
                  else
                    SliverList.separated(
                      itemCount: visible.length,
                      separatorBuilder: (context, index) => const Divider(height: 1, indent: 16, endIndent: 16),
                      itemBuilder: (context, index) => _MenuTile(item: visible[index], quantity: cart.quantityOf(visible[index].productId)),
                    ),
                  const SliverToBoxAdapter(child: SizedBox(height: 96)),
                ],
              ),
            ),
      bottomNavigationBar: cart.itemCount == 0
          ? null
          : SafeArea(
              child: Padding(
                padding: const EdgeInsets.fromLTRB(16, 8, 16, 12),
                child: FilledButton(
                  style: FilledButton.styleFrom(minimumSize: const Size.fromHeight(56)),
                  onPressed: () => context.push('/order/cart'),
                  child: Row(
                    children: [
                      Text(l10n.cartItems(cart.itemCount)),
                      const Spacer(),
                      Text('${l10n.viewOrder} · ${formatMoney(cart.total)}', style: const TextStyle(fontWeight: FontWeight.w700)),
                    ],
                  ),
                ),
              ),
            ),
    );
  }
}

class _CategoryChip extends StatelessWidget {
  const _CategoryChip({required this.label, required this.selected, required this.onTap});

  final String label;
  final bool selected;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.symmetric(horizontal: 4, vertical: 6),
    child: ChoiceChip(label: Text(label), selected: selected, onSelected: (_) => onTap()),
  );
}

class _MenuTile extends ConsumerWidget {
  const _MenuTile({required this.item, required this.quantity});

  final MenuEntry item;
  final int quantity;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context);
    final cart = ref.read(cartProvider.notifier);
    final orderable = item.price != null;

    return ListTile(
      contentPadding: const EdgeInsets.symmetric(horizontal: 16, vertical: 4),
      leading: ProductImage(productId: item.productId, attachmentId: item.imageAttachmentId, name: item.name),
      title: Text(item.name, style: const TextStyle(fontWeight: FontWeight.w600)),
      subtitle: Text(
        [if (item.description != null) item.description!, orderable ? formatMoney(item.price!) : l10n.noPrice].join('\n'),
        maxLines: 3,
        overflow: TextOverflow.ellipsis,
        style: TextStyle(color: orderable ? AppColors.textMuted : AppColors.danger),
      ),
      trailing: !orderable
          ? null
          : quantity == 0
          ? IconButton.filledTonal(key: Key('add-${item.productId}'), onPressed: () => cart.add(item), icon: const Icon(Icons.add))
          : _Stepper(quantity: quantity, onMinus: () => cart.remove(item), onPlus: () => cart.add(item)),
      onTap: orderable ? () => cart.add(item) : null,
    );
  }
}

/// A restaurant menu item: its picture large, name, price and the add / quantity control.
class _MenuCard extends ConsumerWidget {
  const _MenuCard({required this.item, required this.quantity});

  final MenuEntry item;
  final int quantity;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context);
    final cart = ref.read(cartProvider.notifier);
    final palette = context.palette;
    final orderable = item.price != null;

    return Card(
      clipBehavior: Clip.antiAlias,
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(18),
        side: BorderSide(
          color: quantity > 0 ? palette.primary : Color.lerp(AppColors.border, palette.primary, 0.08)!,
          width: quantity > 0 ? 2 : 1,
        ),
      ),
      child: InkWell(
        key: Key('card-${item.productId}'),
        onTap: orderable ? () => cart.add(item) : null,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Expanded(
              child: LayoutBuilder(
                builder: (context, box) => ProductImage(
                  productId: item.productId,
                  attachmentId: item.imageAttachmentId,
                  name: item.name,
                  size: box.maxWidth,
                  square: false,
                ),
              ),
            ),
            Padding(
              padding: const EdgeInsets.fromLTRB(12, 10, 6, 8),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    item.name,
                    maxLines: 2,
                    overflow: TextOverflow.ellipsis,
                    style: TextStyle(fontWeight: FontWeight.w700, color: palette.ink, height: 1.2),
                  ),
                  const SizedBox(height: 4),
                  Row(
                    children: [
                      Expanded(
                        child: Text(
                          orderable ? formatMoney(item.price!) : l10n.noPrice,
                          style: TextStyle(fontWeight: FontWeight.w800, color: orderable ? palette.primary : AppColors.danger),
                        ),
                      ),
                      if (orderable)
                        quantity == 0
                            ? IconButton.filled(
                                key: Key('add-${item.productId}'),
                                visualDensity: VisualDensity.compact,
                                onPressed: () => cart.add(item),
                                icon: const Icon(Icons.add),
                              )
                            : Row(
                                mainAxisSize: MainAxisSize.min,
                                children: [
                                  IconButton(
                                    visualDensity: VisualDensity.compact,
                                    onPressed: () => cart.remove(item),
                                    icon: const Icon(Icons.remove_circle_outline),
                                  ),
                                  Text('$quantity', style: const TextStyle(fontWeight: FontWeight.w800)),
                                  IconButton(
                                    visualDensity: VisualDensity.compact,
                                    onPressed: () => cart.add(item),
                                    icon: Icon(Icons.add_circle, color: palette.primary),
                                  ),
                                ],
                              ),
                    ],
                  ),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _Stepper extends StatelessWidget {
  const _Stepper({required this.quantity, required this.onMinus, required this.onPlus});

  final int quantity;
  final VoidCallback onMinus;
  final VoidCallback onPlus;

  @override
  Widget build(BuildContext context) => Row(
    mainAxisSize: MainAxisSize.min,
    children: [
      IconButton(onPressed: onMinus, icon: const Icon(Icons.remove_circle_outline)),
      Text('$quantity', style: const TextStyle(fontSize: 16, fontWeight: FontWeight.w700)),
      IconButton(
        onPressed: onPlus,
        icon: Icon(Icons.add_circle, color: context.palette.primary),
      ),
    ],
  );
}

class _Message extends StatelessWidget {
  const _Message({required this.icon, required this.title, this.body});

  final IconData icon;
  final String title;
  final String? body;

  @override
  Widget build(BuildContext context) => Center(
    child: Padding(
      padding: const EdgeInsets.all(32),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(icon, size: 48, color: AppColors.textMuted),
          const SizedBox(height: 12),
          Text(
            title,
            textAlign: TextAlign.center,
            style: const TextStyle(fontWeight: FontWeight.w700, fontSize: 16),
          ),
          if (body != null) ...[
            const SizedBox(height: 6),
            Text(
              body!,
              textAlign: TextAlign.center,
              style: const TextStyle(color: AppColors.textMuted),
            ),
          ],
        ],
      ),
    ),
  );
}
