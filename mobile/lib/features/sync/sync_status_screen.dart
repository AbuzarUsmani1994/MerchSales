import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/config/app_config.dart';
import '../../core/db/app_database.dart';
import '../../core/providers.dart';
import '../../l10n/generated/app_localizations.dart';
import '../../shared/theme.dart';

final _commandsProvider = StreamProvider.family<List<OutboxCommand>, String>(
  (ref, status) => ref.watch(databaseProvider).watchCommands(status),
);
final _deviceIdProvider = FutureProvider<String?>((ref) => ref.watch(tokenStorageProvider).readDeviceId());

/// Section 14 "sync status": pending commands, errors with retry, and the facts support
/// needs when a phone misbehaves (device id, app version).
class SyncStatusScreen extends ConsumerWidget {
  const SyncStatusScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final l10n = AppLocalizations.of(context);
    final pending = ref.watch(_commandsProvider(OutboxStatus.pending)).value ?? const [];
    final rejected = ref.watch(_commandsProvider(OutboxStatus.rejected)).value ?? const [];
    final syncing = ref.watch(syncControllerProvider).running;

    return Scaffold(
      appBar: AppBar(title: Text(l10n.syncStatusTitle)),
      body: RefreshIndicator(
        onRefresh: () => ref.read(syncControllerProvider.notifier).syncNow(),
        child: ListView(padding: const EdgeInsets.all(16), children: [
          FilledButton.icon(
            onPressed: syncing ? null : () => ref.read(syncControllerProvider.notifier).syncNow(),
            icon: const Icon(Icons.sync),
            label: Text(syncing ? l10n.syncing : l10n.syncNow),
          ),
          const SizedBox(height: 20),
          _SectionTitle(l10n.pendingSection),
          if (pending.isEmpty)
            Padding(padding: const EdgeInsets.symmetric(vertical: 8), child: Text(l10n.nothingPending, style: const TextStyle(color: AppColors.textMuted)))
          else
            ...pending.map((c) => _CommandTile(command: c, subtitle: [
                  l10n.attempts(c.attempts),
                  if (c.lastError != null) c.lastError!,
                ].join(' · '))),
          if (rejected.isNotEmpty) ...[
            const SizedBox(height: 20),
            _SectionTitle(l10n.rejectedSection),
            ...rejected.map((c) => _CommandTile(
                  command: c,
                  subtitle: [if (c.errorCode != null) c.errorCode!, if (c.lastError != null) c.lastError!].join(' · '),
                  isError: true,
                  trailing: TextButton(
                    onPressed: () => ref.read(databaseProvider).dismissRejected(c.id),
                    child: Text(l10n.dismiss),
                  ),
                )),
          ],
          const SizedBox(height: 28),
          _Fact(label: l10n.appVersion, value: '${AppConfig.appVersion} (${AppConfig.flavor})'),
          _Fact(label: l10n.deviceId, value: ref.watch(_deviceIdProvider).value ?? '—'),
        ]),
      ),
    );
  }
}

class _SectionTitle extends StatelessWidget {
  const _SectionTitle(this.text);
  final String text;

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.only(bottom: 8),
        child: Text(text.toUpperCase(),
            style: const TextStyle(fontSize: 12, fontWeight: FontWeight.w700, letterSpacing: 0.6, color: AppColors.textMuted)),
      );
}

class _CommandTile extends StatelessWidget {
  const _CommandTile({required this.command, required this.subtitle, this.isError = false, this.trailing});

  final OutboxCommand command;
  final String subtitle;
  final bool isError;
  final Widget? trailing;

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.only(bottom: 8),
        child: Card(
          child: ListTile(
            leading: Icon(isError ? Icons.error_outline : Icons.schedule, color: isError ? AppColors.danger : AppColors.accent),
            title: Text(command.type, style: const TextStyle(fontWeight: FontWeight.w600)),
            subtitle: Text(subtitle),
            trailing: trailing,
          ),
        ),
      );
}

class _Fact extends StatelessWidget {
  const _Fact({required this.label, required this.value});
  final String label;
  final String value;

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.only(bottom: 6),
        child: Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
          SizedBox(width: 110, child: Text(label, style: const TextStyle(color: AppColors.textMuted))),
          Expanded(child: SelectableText(value, style: const TextStyle(fontFamily: 'monospace', fontSize: 13))),
        ]),
      );
}
