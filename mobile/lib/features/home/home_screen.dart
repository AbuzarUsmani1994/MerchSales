import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/db/app_database.dart';
import '../../core/providers.dart';
import '../../core/sync/offline_policy.dart';
import '../../core/sync/sync_engine.dart';
import '../../l10n/generated/app_localizations.dart';
import '../../shared/theme.dart';

final _metaProvider = StreamProvider.family<String?, String>((ref, key) => ref.watch(databaseProvider).watchMeta(key));
final pendingCountProvider = StreamProvider<int>((ref) => ref.watch(databaseProvider).watchPendingCount());
final _permissionCountProvider = StreamProvider<int>((ref) => ref.watch(databaseProvider).watchPermissionCount());
final _featureCountProvider = StreamProvider<int>((ref) => ref.watch(databaseProvider).watchEnabledFeatureCount());
final _biometricOfferProvider = FutureProvider<bool>((ref) async {
  final choice = await ref.watch(tokenStorageProvider).readBiometricChoice();
  return choice == null && await ref.watch(biometricServiceProvider).isAvailable();
});

/// Home dashboard (section 14). Phase 0 shows the phone's sync health and access; the daily
/// work screens (orders, receiving, counts, counter sale) come with Phase 1.
class HomeScreen extends ConsumerWidget {
  const HomeScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context);
    final profile = ref.watch(sessionControllerProvider).profile;
    final pending = ref.watch(pendingCountProvider).value ?? 0;

    return Scaffold(
      appBar: AppBar(
        title: Text(profile?.tenantName ?? l10n.appTitle),
        actions: [
          IconButton(
            tooltip: l10n.syncStatusTitle,
            onPressed: () => context.push('/sync'),
            icon: Badge(isLabelVisible: pending > 0, label: Text('$pending'), child: const Icon(Icons.sync)),
          ),
          PopupMenuButton<String>(
            onSelected: (value) => switch (value) {
              'biometric' => _toggleBiometric(context, ref),
              'signOut' => _signOut(context, ref, pending),
              _ => null,
            },
            itemBuilder: (context) => [
              PopupMenuItem(value: 'biometric', child: Text(l10n.biometricUnlock)),
              PopupMenuItem(value: 'signOut', child: Text(l10n.signOut)),
            ],
          ),
        ],
      ),
      body: RefreshIndicator(
        onRefresh: () => ref.read(syncControllerProvider.notifier).syncNow(),
        child: ListView(
          padding: const EdgeInsets.fromLTRB(16, 8, 16, 32),
          children: [
            Text(l10n.homeGreeting(profile?.displayName ?? ''),
                style: const TextStyle(fontSize: 26, fontWeight: FontWeight.w700, color: AppColors.navy)),
            const SizedBox(height: 4),
            Text('${profile?.username ?? ''} · ${profile?.tenantCode ?? ''}', style: const TextStyle(color: AppColors.textMuted)),
            const SizedBox(height: 20),
            const _OfflineBanner(),
            const _SyncCard(),
            const SizedBox(height: 12),
            const _BiometricOffer(),
            const _AccessCard(),
            const SizedBox(height: 12),
            _InfoCard(icon: Icons.construction_outlined, title: l10n.comingSoonTitle, body: l10n.comingSoonBody),
          ],
        ),
      ),
    );
  }

  Future<void> _toggleBiometric(BuildContext context, WidgetRef ref) async {
    final storage = ref.read(tokenStorageProvider);
    final enabled = await storage.readBiometricChoice() ?? false;
    await ref.read(sessionControllerProvider.notifier).setBiometric(!enabled);
    ref.invalidate(_biometricOfferProvider);
    if (context.mounted) {
      final l10n = AppLocalizations.of(context);
      ScaffoldMessenger.of(context)
          .showSnackBar(SnackBar(content: Text(!enabled ? l10n.biometricTurnedOn : l10n.biometricTurnedOff)));
    }
  }

  Future<void> _signOut(BuildContext context, WidgetRef ref, int pending) async {
    final session = ref.read(sessionControllerProvider.notifier);
    if (pending == 0) {
      await session.signOut();
      return;
    }

    final l10n = AppLocalizations.of(context);
    final choice = await showDialog<String>(
      context: context,
      builder: (context) => AlertDialog(
        title: Text(l10n.signOutPendingTitle),
        content: Text(l10n.signOutPendingBody(pending)),
        actions: [
          TextButton(onPressed: () => Navigator.pop(context), child: Text(l10n.cancel)),
          TextButton(onPressed: () => Navigator.pop(context, 'discard'), child: Text(l10n.discardAndSignOut)),
          FilledButton(onPressed: () => Navigator.pop(context, 'sync'), child: Text(l10n.syncNow)),
        ],
      ),
    );

    if (choice == 'sync') await ref.read(syncControllerProvider.notifier).syncNow();
    if (choice == 'discard') await session.signOut(discardPending: true);
  }
}

class _OfflineBanner extends ConsumerWidget {
  const _OfflineBanner();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final last = DateTime.tryParse(ref.watch(_metaProvider(SyncMetaKeys.lastSuccessfulSyncAtUtc)).value ?? '');
    final hours = int.tryParse(ref.watch(_metaProvider(SyncMetaKeys.maxOfflineHours)).value ?? '') ?? 24;
    final syncing = ref.watch(syncControllerProvider).running;

    // While the very first sync is still running there's nothing to warn about yet.
    if (OfflinePolicy(lastSuccessfulSyncAtUtc: last, maxOfflineHours: hours).allowsNewTransactions(DateTime.now().toUtc()) ||
        (last == null && syncing)) {
      return const SizedBox.shrink();
    }

    final l10n = AppLocalizations.of(context);
    return Padding(
      padding: const EdgeInsets.only(bottom: 12),
      child: Container(
        padding: const EdgeInsets.all(16),
        decoration: BoxDecoration(color: AppColors.warningSoft, borderRadius: BorderRadius.circular(16)),
        child: Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
          const Icon(Icons.cloud_off_outlined, color: AppColors.warning),
          const SizedBox(width: 12),
          Expanded(
            child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
              Text(l10n.offlineBlockedTitle, style: const TextStyle(fontWeight: FontWeight.w700, color: AppColors.warning)),
              const SizedBox(height: 4),
              Text(l10n.offlineBlockedBody(hours), style: const TextStyle(color: AppColors.warning)),
            ]),
          ),
        ]),
      ),
    );
  }
}

class _SyncCard extends ConsumerWidget {
  const _SyncCard();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context);
    final sync = ref.watch(syncControllerProvider);
    final last = DateTime.tryParse(ref.watch(_metaProvider(SyncMetaKeys.lastSuccessfulSyncAtUtc)).value ?? '');
    final error = ref.watch(_metaProvider(SyncMetaKeys.lastSyncError)).value;
    final pending = ref.watch(pendingCountProvider).value ?? 0;

    final healthy = error == null && last != null;
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          Row(children: [
            Container(
              width: 40,
              height: 40,
              decoration: BoxDecoration(
                color: healthy ? AppColors.successSoft : AppColors.accentSoft,
                borderRadius: BorderRadius.circular(12),
              ),
              child: Icon(healthy ? Icons.cloud_done_outlined : Icons.cloud_sync_outlined,
                  color: healthy ? AppColors.success : AppColors.accent),
            ),
            const SizedBox(width: 12),
            Expanded(
              child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                Text(l10n.syncCardTitle, style: const TextStyle(fontWeight: FontWeight.w700, fontSize: 16)),
                Text(last == null ? l10n.neverSynced : l10n.lastSynced(relativeTime(l10n, last)),
                    style: const TextStyle(color: AppColors.textMuted)),
              ]),
            ),
          ]),
          const SizedBox(height: 12),
          Text(l10n.pendingChanges(pending)),
          if (error != null && sync.lastResult is! SyncSucceeded) ...[
            const SizedBox(height: 6),
            Text(l10n.syncFailed(error), style: const TextStyle(color: AppColors.danger)),
          ],
          const SizedBox(height: 14),
          OutlinedButton.icon(
            onPressed: sync.running ? null : () => ref.read(syncControllerProvider.notifier).syncNow(),
            icon: sync.running
                ? const SizedBox(width: 16, height: 16, child: CircularProgressIndicator(strokeWidth: 2))
                : const Icon(Icons.sync),
            label: Text(sync.running ? l10n.syncing : l10n.syncNow),
          ),
        ]),
      ),
    );
  }
}

class _BiometricOffer extends ConsumerWidget {
  const _BiometricOffer();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    if (ref.watch(_biometricOfferProvider).value != true) return const SizedBox.shrink();
    final l10n = AppLocalizations.of(context);

    Future<void> choose(bool enabled) async {
      await ref.read(sessionControllerProvider.notifier).setBiometric(enabled);
      ref.invalidate(_biometricOfferProvider);
    }

    return Padding(
      padding: const EdgeInsets.only(bottom: 12),
      child: Card(
        color: AppColors.accentSoft,
        child: Padding(
          padding: const EdgeInsets.all(16),
          child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
            Row(children: [
              const Icon(Icons.fingerprint, color: AppColors.accent),
              const SizedBox(width: 10),
              Text(l10n.biometricOfferTitle, style: const TextStyle(fontWeight: FontWeight.w700, fontSize: 16)),
            ]),
            const SizedBox(height: 6),
            Text(l10n.biometricOfferBody),
            const SizedBox(height: 12),
            Row(mainAxisAlignment: MainAxisAlignment.end, children: [
              TextButton(onPressed: () => choose(false), child: Text(l10n.notNow)),
              const SizedBox(width: 8),
              FilledButton(
                style: FilledButton.styleFrom(minimumSize: const Size(0, 44)),
                onPressed: () => choose(true),
                child: Text(l10n.enable),
              ),
            ]),
          ]),
        ),
      ),
    );
  }
}

class _AccessCard extends ConsumerWidget {
  const _AccessCard();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context);
    final permissions = ref.watch(_permissionCountProvider).value ?? 0;
    final features = ref.watch(_featureCountProvider).value ?? 0;
    return _InfoCard(
      icon: Icons.verified_user_outlined,
      title: l10n.accessCardTitle,
      body: l10n.accessSummary(permissions, features),
    );
  }
}

class _InfoCard extends StatelessWidget {
  const _InfoCard({required this.icon, required this.title, required this.body});

  final IconData icon;
  final String title;
  final String body;

  @override
  Widget build(BuildContext context) {
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
          Container(
            width: 40,
            height: 40,
            decoration: BoxDecoration(color: AppColors.background, borderRadius: BorderRadius.circular(12)),
            child: Icon(icon, color: AppColors.textMuted),
          ),
          const SizedBox(width: 12),
          Expanded(
            child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
              Text(title, style: const TextStyle(fontWeight: FontWeight.w700, fontSize: 16)),
              const SizedBox(height: 4),
              Text(body, style: const TextStyle(color: AppColors.textMuted)),
            ]),
          ),
        ]),
      ),
    );
  }
}

String relativeTime(AppLocalizations l10n, DateTime utc) {
  final elapsed = DateTime.now().toUtc().difference(utc);
  if (elapsed.inMinutes < 1) return l10n.justNow;
  if (elapsed.inHours < 1) return l10n.minutesAgo(elapsed.inMinutes);
  return l10n.hoursAgo(elapsed.inHours);
}
