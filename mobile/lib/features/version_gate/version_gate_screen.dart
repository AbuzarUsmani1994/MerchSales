import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/providers.dart';
import '../../l10n/generated/app_localizations.dart';
import '../../shared/theme.dart';

/// Sections 9, 14: shown when the server says this app version is below its minimum. No way
/// back into the app until the version is fine. The link is shown and copyable (opening it
/// directly would need url_launcher, which the spec doesn't list).
class VersionGateScreen extends ConsumerWidget {
  const VersionGateScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context);
    final storeUrl = ref.watch(sessionControllerProvider).storeUrl;

    return PopScope(
      canPop: false,
      child: Scaffold(
        body: SafeArea(
          child: Padding(
            padding: const EdgeInsets.all(24),
            child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
              const Spacer(),
              const Icon(Icons.system_update, size: 56, color: AppColors.accent),
              const SizedBox(height: 20),
              Text(l10n.updateTitle,
                  textAlign: TextAlign.center, style: const TextStyle(fontSize: 24, fontWeight: FontWeight.w700, color: AppColors.navy)),
              const SizedBox(height: 8),
              Text(l10n.updateBody, textAlign: TextAlign.center, style: const TextStyle(color: AppColors.textMuted)),
              const SizedBox(height: 24),
              if (storeUrl != null && storeUrl.isNotEmpty)
                Card(
                  child: ListTile(
                    title: Text(l10n.updateLink),
                    subtitle: SelectableText(storeUrl),
                    trailing: IconButton(
                      tooltip: l10n.copyLink,
                      icon: const Icon(Icons.copy),
                      onPressed: () async {
                        await Clipboard.setData(ClipboardData(text: storeUrl));
                        if (context.mounted) {
                          ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(l10n.copied)));
                        }
                      },
                    ),
                  ),
                )
              else
                Text(l10n.askAdminForUpdate, textAlign: TextAlign.center),
              const Spacer(),
              OutlinedButton(
                onPressed: () => ref.read(sessionControllerProvider.notifier).retryAfterUpdateGate(),
                child: Text(l10n.checkAgain),
              ),
            ]),
          ),
        ),
      ),
    );
  }
}
