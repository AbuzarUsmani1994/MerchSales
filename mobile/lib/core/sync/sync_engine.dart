import 'dart:convert';

import '../db/app_database.dart';
import 'sync_api.dart';

/// What one sync run concluded; the session controller acts on revoked / update-required.
sealed class SyncRunResult {
  const SyncRunResult();
}

final class SyncSucceeded extends SyncRunResult {
  const SyncSucceeded({required this.pushed, required this.rejected});
  final int pushed;
  final int rejected;
}

/// Offline or the server didn't answer: nothing lost, everything pending stays pending.
final class SyncFailed extends SyncRunResult {
  const SyncFailed(this.message);
  final String message;
}

final class SyncDeviceRevoked extends SyncRunResult {
  const SyncDeviceRevoked({required this.wipeRequested});
  final bool wipeRequested;
}

final class SyncUpdateRequired extends SyncRunResult {
  const SyncUpdateRequired({this.storeUrl});
  final String? storeUrl;
}

/// Section 12/14 sync: check status, push the outbox in clientSequence order, then pull until
/// no entity has more. Only one run at a time; a run requested while one is going joins it.
class SyncEngine {
  SyncEngine({
    required SyncApi api,
    required AppDatabase db,
    required Future<String?> Function() deviceId,
    required String appVersion,
    DateTime Function()? clock,
  })  : _api = api,
        _db = db,
        _deviceId = deviceId,
        _appVersion = appVersion,
        _clock = clock ?? (() => DateTime.now().toUtc());

  static const pushBatchSize = 100;
  static const maxPullPages = 50;

  final SyncApi _api;
  final AppDatabase _db;
  final Future<String?> Function() _deviceId;
  final String _appVersion;
  final DateTime Function() _clock;

  Future<SyncRunResult>? _inFlight;

  bool get isRunning => _inFlight != null;

  Future<SyncRunResult> run() => _inFlight ??= _run().whenComplete(() => _inFlight = null);

  Future<SyncRunResult> _run() async {
    try {
      final status = await _api.status();
      if (status.deviceRevoked) return SyncDeviceRevoked(wipeRequested: status.wipeRequested);
      if (status.minVersion != null && isVersionBelow(_appVersion, status.minVersion!)) {
        return SyncUpdateRequired(storeUrl: status.storeUrl);
      }
      await _db.writeMeta(SyncMetaKeys.maxOfflineHours, '${status.maxOfflineHours}');

      final (pushed, rejected) = await _push();
      await _pull();

      await _db.writeMeta(SyncMetaKeys.lastSuccessfulSyncAtUtc, _clock().toIso8601String());
      await _db.writeMeta(SyncMetaKeys.lastSyncError, null);
      return SyncSucceeded(pushed: pushed, rejected: rejected);
    } on SyncTransportException catch (e) {
      await _db.writeMeta(SyncMetaKeys.lastSyncError, e.message);
      return SyncFailed(e.message);
    }
  }

  /// Pushes before pulling, so what the server sends back already reflects this phone's work.
  Future<(int, int)> _push() async {
    final deviceId = await _deviceId();
    if (deviceId == null) return (0, 0);

    var pushed = 0;
    var rejected = 0;
    final pending = await _db.pendingCommands();

    for (var start = 0; start < pending.length; start += pushBatchSize) {
      final batch = pending.skip(start).take(pushBatchSize).toList();
      final results = await _api.push(
        deviceId,
        batch
            .map((c) => PushCommand(
                  id: c.id,
                  type: c.type,
                  clientSequence: c.clientSequence,
                  createdOnDeviceAtUtc: c.createdOnDeviceAtUtc,
                  payload: jsonDecode(c.payloadJson) as Map<String, dynamic>,
                ))
            .toList(),
      );

      final byId = {for (final c in batch) c.id: c};
      for (final result in results) {
        final command = byId[result.id];
        switch (result.status) {
          case 'Applied' || 'AppliedWithChanges':
            await _db.markApplied(result.id, result.result == null ? null : jsonEncode(result.result));
            if (command != null) await _recordOrderOutcome(command, result);
            pushed++;
          case 'Rejected':
            await _db.markRejected(result.id, result.errorCode, result.message);
            if (command != null) await _recordOrderOutcome(command, result);
            rejected++;
          default:
            // Failed: the server applied nothing and kept no record; retried next run with the same id.
            await _db.markAttemptFailed(result.id, result.errorCode, result.message);
        }
      }
    }

    return (pushed, rejected);
  }

  /// Shows the outcome of a phone order straight away, before the next pull brings the server's copy.
  Future<void> _recordOrderOutcome(OutboxCommand command, PushResult result) async {
    if (command.type != 'CreateSalesOrder') return;
    final orderId = (jsonDecode(command.payloadJson) as Map<String, dynamic>)['id'] as String?;
    if (orderId == null) return;

    if (result.status == 'Rejected') {
      await _db.recordOrderOutcome(orderId, error: result.message ?? result.errorCode);
      return;
    }

    final body = result.result is Map<String, dynamic> ? result.result as Map<String, dynamic> : const <String, dynamic>{};
    final serverIds = body['serverIds'] is Map<String, dynamic> ? body['serverIds'] as Map<String, dynamic> : const <String, dynamic>{};
    await _db.recordOrderOutcome(
      orderId,
      serverId: serverIds['salesOrder'] as String?,
      documentNo: body['documentNo'] as String?,
      status: body['status'] as String? ?? 'Confirmed',
    );
  }

  Future<void> _pull() async {
    for (var page = 0; page < maxPullPages; page++) {
      final tokens = await _db.readTokens();
      final response = await _api.pull(tokens);

      List<({String code, String? branchId})>? permissions;
      final permissionsToken = response.tokens['permissions'];
      if (permissionsToken != null && permissionsToken != tokens['permissions']) {
        // The server sends the whole set whenever it changed (its token is the security stamp).
        permissions = (response.changes['permissions'] ?? const [])
            .map((g) => (code: g['code'] as String, branchId: g['branchId'] as String?))
            .toList();
      }

      await _db.applyPull(
        tokens: response.tokens,
        settings: (response.changes['settings'] ?? const [])
            .map((s) => (key: s['key'] as String, value: s['value'] as String))
            .toList(),
        features: (response.changes['features'] ?? const [])
            .map((f) => (key: f['key'] as String, isEnabled: f['isEnabled'] as bool))
            .toList(),
        permissions: permissions,
        rows: response.changes,
        tombstones: response.tombstones,
      );

      if (!response.anyHasMore) return;
    }
  }
}

/// Dotted numeric versions ("1.2.10" > "1.2.9"); a version that can't be read is never "below".
bool isVersionBelow(String current, String minimum) {
  List<int>? parse(String v) {
    final parts = v.split('-').first.split('.');
    final numbers = parts.map(int.tryParse).toList();
    return numbers.contains(null) ? null : numbers.cast<int>();
  }

  final a = parse(current);
  final b = parse(minimum);
  if (a == null || b == null) return false;

  for (var i = 0; i < a.length || i < b.length; i++) {
    final x = i < a.length ? a[i] : 0;
    final y = i < b.length ? b[i] : 0;
    if (x != y) return x < y;
  }
  return false;
}
