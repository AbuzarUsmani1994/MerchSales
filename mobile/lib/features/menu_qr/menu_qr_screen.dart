import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:qr_flutter/qr_flutter.dart';

import '../../core/config/app_config.dart';
import '../../core/db/app_database.dart';
import '../../core/providers.dart';
import '../../l10n/generated/app_localizations.dart';
import '../../shared/theme.dart';
import '../ordering/order_providers.dart';

/// Tenant setting holding a branch's customer menu code (server: PublicMenuSettings). Empty = switched off.
String publicMenuSettingKey(String branchId) => 'PublicMenu.Code.$branchId';

/// The customer menu address a QR opens: the API's own site serves the page.
String publicMenuUrl(String code) => '${AppConfig.apiBaseUrl}/m/$code';

final _menuCodeProvider = StreamProvider.family<String?, String>(
  (ref, branchId) => ref.watch(databaseProvider).watchSetting(publicMenuSettingKey(branchId)),
);

/// The waiter shows this to a guest, who scans it with their own camera and gets this branch's
/// menu in their browser. The code arrives with the settings sync, so the QR shows offline too.
class MenuQrScreen extends ConsumerWidget {
  const MenuQrScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context);
    final branches = ref.watch(orderBranchesProvider).value ?? const <LocalBranch>[];
    final branch = ref.watch(orderBranchProvider);
    final code = branch == null ? null : ref.watch(_menuCodeProvider(branch.id)).value;

    return Scaffold(
      appBar: AppBar(
        title: Text(l10n.menuQrTitle),
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
      body: SafeArea(
        child: Center(
          child: SingleChildScrollView(
            padding: const EdgeInsets.all(24),
            child: ConstrainedBox(
              constraints: const BoxConstraints(maxWidth: 420),
              child: branch == null
                  ? Text(l10n.noOrderBranch, textAlign: TextAlign.center)
                  : code == null || code.isEmpty
                      ? _SwitchedOff(branchName: branch.name)
                      : _QrCard(url: publicMenuUrl(code), branchName: branch.name),
            ),
          ),
        ),
      ),
    );
  }
}

class _QrCard extends StatelessWidget {
  const _QrCard({required this.url, required this.branchName});

  final String url;
  final String branchName;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final palette = context.palette;
    return Column(
      children: [
        Container(
          padding: const EdgeInsets.fromLTRB(24, 24, 24, 20),
          decoration: BoxDecoration(
            color: Colors.white,
            borderRadius: BorderRadius.circular(28),
            boxShadow: [BoxShadow(color: palette.primary.withValues(alpha: 0.18), blurRadius: 32, offset: const Offset(0, 12))],
          ),
          child: Column(
            children: [
              Row(
                mainAxisAlignment: MainAxisAlignment.center,
                children: [
                  Icon(palette.icon, color: palette.primary),
                  const SizedBox(width: 8),
                  Flexible(
                    child: Text(
                      branchName,
                      style: TextStyle(fontSize: 20, fontWeight: FontWeight.w800, color: palette.ink),
                      overflow: TextOverflow.ellipsis,
                    ),
                  ),
                ],
              ),
              const SizedBox(height: 18),
              // Black on white with a quiet zone: what phone cameras read most reliably.
              QrImageView(
                key: const Key('menu-qr'),
                data: url,
                size: 260,
                padding: const EdgeInsets.all(8),
                backgroundColor: Colors.white,
                errorCorrectionLevel: QrErrorCorrectLevel.M,
                semanticsLabel: l10n.menuQrScanHint,
              ),
              const SizedBox(height: 16),
              Text(
                l10n.menuQrScanHint,
                textAlign: TextAlign.center,
                style: TextStyle(fontSize: 16, fontWeight: FontWeight.w600, color: palette.ink),
              ),
            ],
          ),
        ),
        const SizedBox(height: 16),
        SelectableText(url, textAlign: TextAlign.center, style: const TextStyle(color: AppColors.textMuted, fontSize: 12)),
      ],
    );
  }
}

class _SwitchedOff extends StatelessWidget {
  const _SwitchedOff({required this.branchName});

  final String branchName;

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(24),
        child: Column(
          children: [
            Icon(Icons.qr_code_2, size: 56, color: context.palette.primary.withValues(alpha: 0.5)),
            const SizedBox(height: 12),
            Text(l10n.menuQrOffTitle, style: TextStyle(fontSize: 18, fontWeight: FontWeight.w700, color: context.palette.ink)),
            const SizedBox(height: 8),
            Text(l10n.menuQrOffBody(branchName), textAlign: TextAlign.center),
            const SizedBox(height: 8),
            Text(l10n.menuQrSyncHint, textAlign: TextAlign.center, style: const TextStyle(color: AppColors.textMuted, fontSize: 13)),
          ],
        ),
      ),
    );
  }
}
