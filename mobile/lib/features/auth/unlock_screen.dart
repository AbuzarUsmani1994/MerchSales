import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/providers.dart';
import '../../l10n/generated/app_localizations.dart';
import '../../shared/theme.dart';

/// Section 14: biometric unlock after the first password sign-in.
class UnlockScreen extends ConsumerStatefulWidget {
  const UnlockScreen({super.key});

  @override
  ConsumerState<UnlockScreen> createState() => _UnlockScreenState();
}

class _UnlockScreenState extends ConsumerState<UnlockScreen> {
  bool _prompted = false;

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    if (!_prompted) {
      _prompted = true;
      WidgetsBinding.instance.addPostFrameCallback((_) => _unlock());
    }
  }

  Future<void> _unlock() async {
    final reason = AppLocalizations.of(context).unlockReason;
    await ref.read(sessionControllerProvider.notifier).unlock(reason);
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final name = ref.watch(sessionControllerProvider).profile?.displayName ?? '';

    return Scaffold(
      body: SafeArea(
        child: Padding(
          padding: const EdgeInsets.all(24),
          child: Column(children: [
            const Spacer(),
            const BrandMark(size: 64),
            const SizedBox(height: 24),
            Text(l10n.unlockTitle, style: const TextStyle(fontSize: 24, fontWeight: FontWeight.w700, color: AppColors.navy)),
            const SizedBox(height: 6),
            Text(name, style: const TextStyle(color: AppColors.textMuted)),
            const Spacer(),
            FilledButton.icon(onPressed: _unlock, icon: const Icon(Icons.fingerprint), label: Text(l10n.unlock)),
            const SizedBox(height: 8),
            TextButton(
              onPressed: () => ref.read(sessionControllerProvider.notifier).signOut(),
              child: Text(l10n.usePasswordInstead),
            ),
          ]),
        ),
      ),
    );
  }
}
