import 'dart:convert';

import 'package:drift/native.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:stockorder_mobile/core/db/app_database.dart';
import 'package:stockorder_mobile/core/sync/offline_policy.dart';
import 'package:stockorder_mobile/core/sync/sync_api.dart';
import 'package:stockorder_mobile/core/sync/sync_engine.dart';

/// Section 19 (Flutter): sync engine ordering, retries, idempotency, dependency rejection;
/// outbox; offline limit.
void main() {
  late AppDatabase db;
  late FakeSyncApi api;
  late SyncEngine engine;
  final now = DateTime.utc(2026, 10, 24, 12);

  setUp(() {
    db = AppDatabase(NativeDatabase.memory());
    api = FakeSyncApi();
    engine = SyncEngine(api: api, db: db, deviceId: () async => 'device-1', appVersion: '1.0.0', clock: () => now);
  });

  tearDown(() => db.close());

  Future<void> enqueue(String id, {Map<String, dynamic> payload = const {}}) => db.enqueueCommand(
        id: id,
        type: 'UpdatePushToken',
        payloadJson: jsonEncode(payload),
        createdOnDeviceAtUtc: now,
      );

  group('outbox', () {
    test('assigns increasing clientSequence in creation order', () async {
      await enqueue('a');
      await enqueue('b');
      await enqueue('c');

      final pending = await db.pendingCommands();
      expect(pending.map((c) => c.id), ['a', 'b', 'c']);
      expect(pending.map((c) => c.clientSequence), [1, 2, 3]);
    });

    test('sequence keeps counting up after commands leave the outbox', () async {
      await enqueue('a');
      await db.discardPending();
      await enqueue('b');

      expect((await db.pendingCommands()).single.clientSequence, 2);
    });
  });

  group('push', () {
    test('sends pending commands in clientSequence order, before pulling', () async {
      await enqueue('first');
      await enqueue('second');

      await engine.run();

      expect(api.calls, ['status', 'push', 'pull']);
      expect(api.pushed.single.map((c) => c.id), ['first', 'second']);
      expect(api.pushed.single.map((c) => c.clientSequence), [1, 2]);
    });

    test('applied commands leave the pending list; rejected ones are kept with their error', () async {
      await enqueue('ok');
      await enqueue('bad');
      api.pushResults = {
        'ok': const PushResult(id: 'ok', status: 'Applied'),
        'bad': const PushResult(id: 'bad', status: 'Rejected', errorCode: 'UNKNOWN_COMMAND_TYPE', message: 'nope'),
      };

      final result = await engine.run();

      expect(result, isA<SyncSucceeded>().having((r) => r.pushed, 'pushed', 1).having((r) => r.rejected, 'rejected', 1));
      expect(await db.pendingCommands(), isEmpty);
      final rejected = await db.watchCommands(OutboxStatus.rejected).first;
      expect(rejected.single.id, 'bad');
      expect(rejected.single.errorCode, 'UNKNOWN_COMMAND_TYPE');
    });

    test('a dependency rejected by the server is shown as rejected, not retried', () async {
      await enqueue('parent');
      await enqueue('child', payload: {'parentId': 'parent'});
      api.pushResults = {
        'parent': const PushResult(id: 'parent', status: 'Rejected', errorCode: 'INVALID_PAYLOAD'),
        'child': const PushResult(id: 'child', status: 'Rejected', errorCode: 'DEPENDENCY_REJECTED'),
      };

      await engine.run();

      final rejected = await db.watchCommands(OutboxStatus.rejected).first;
      expect(rejected.map((c) => c.errorCode), ['INVALID_PAYLOAD', 'DEPENDENCY_REJECTED']);
    });

    test('a server-side failure stays pending and is retried with the same id next run', () async {
      await enqueue('flaky');
      api.pushResults = {'flaky': const PushResult(id: 'flaky', status: 'Failed', errorCode: 'COMMAND_FAILED')};

      await engine.run();
      var pending = await db.pendingCommands();
      expect(pending.single.attempts, 1);
      expect(pending.single.errorCode, 'COMMAND_FAILED');

      api.pushResults = {'flaky': const PushResult(id: 'flaky', status: 'Applied')};
      await engine.run();

      expect(api.pushed.map((batch) => batch.single.id), ['flaky', 'flaky'], reason: 'same id both times');
      expect(await db.pendingCommands(), isEmpty);
    });

    test('offline: nothing is marked, nothing is pulled, the error is recorded', () async {
      await enqueue('a');
      api.failWith = SyncTransportException('No connection');

      final result = await engine.run();

      expect(result, isA<SyncFailed>());
      expect((await db.pendingCommands()).single.attempts, 0);
      expect(await db.readMeta(SyncMetaKeys.lastSyncError), 'No connection');
      expect(await db.readMeta(SyncMetaKeys.lastSuccessfulSyncAtUtc), isNull);
    });

    test('concurrent run requests share one run', () async {
      await enqueue('a');
      await Future.wait([engine.run(), engine.run()]);

      expect(api.calls.where((c) => c == 'push'), hasLength(1));
    });
  });

  group('pull', () {
    test('keeps pulling while any entity has more, and stores data and tokens', () async {
      api.pullPages = [
        PullResponse(
          tokens: const {'settings': '0A', 'features': '0B', 'permissions': 'stamp1'},
          hasMore: const {'settings': true, 'features': false, 'permissions': false},
          changes: const {
            'settings': [
              {'key': 'Mobile.MaxOfflineHours', 'value': '24'},
            ],
            'features': [
              {'key': 'Restaurant.Tables', 'isEnabled': true},
            ],
            'permissions': [
              {'code': 'Mobile.Use', 'branchId': 'b1'},
              {'code': 'Sales.Order.Create', 'branchId': 'b1'},
            ],
          },
        ),
        PullResponse(
          tokens: const {'settings': '0C', 'features': '0B', 'permissions': 'stamp1'},
          hasMore: const {'settings': false, 'features': false, 'permissions': false},
          changes: const {
            'settings': [
              {'key': 'Sales.MaxDiscountPercent', 'value': '10'},
            ],
            'features': [],
            'permissions': [],
          },
        ),
      ];

      await engine.run();

      expect(api.pullTokens, [<String, String>{}, {'settings': '0A', 'features': '0B', 'permissions': 'stamp1'}]);
      expect(await db.readSetting('Mobile.MaxOfflineHours'), '24');
      expect(await db.readSetting('Sales.MaxDiscountPercent'), '10');
      expect(await db.watchPermissionCount().first, 2, reason: 'unchanged permission token keeps the set');
      expect(await db.readTokens(), {'settings': '0C', 'features': '0B', 'permissions': 'stamp1'});
      expect(await db.readMeta(SyncMetaKeys.lastSuccessfulSyncAtUtc), now.toIso8601String());
    });

    test('a new permission token replaces the whole set, even with an empty one', () async {
      api.pullPages = [
        PullResponse(
          tokens: const {'permissions': 'stamp1'},
          hasMore: const {'permissions': false},
          changes: const {
            'permissions': [
              {'code': 'Mobile.Use', 'branchId': 'b1'},
            ],
          },
        ),
      ];
      await engine.run();
      expect(await db.watchPermissionCount().first, 1);

      api.pullPages = [
        PullResponse(tokens: const {'permissions': 'stamp2'}, hasMore: const {'permissions': false}, changes: const {'permissions': []}),
      ];
      await engine.run();

      expect(await db.watchPermissionCount().first, 0, reason: 'all roles were removed from this user');
    });
  });

  group('status', () {
    test('revoked device stops the run before anything is pushed', () async {
      await enqueue('a');
      api.statusResponse = _status(deviceRevoked: true, wipeRequested: true);

      final result = await engine.run();

      expect(result, isA<SyncDeviceRevoked>().having((r) => r.wipeRequested, 'wipe', isTrue));
      expect(api.calls, ['status']);
    });

    test('app below the minimum version must update', () async {
      api.statusResponse = _status(minVersion: '1.2.0', storeUrl: 'https://example.test/app.apk');

      final result = await engine.run();

      expect(result, isA<SyncUpdateRequired>().having((r) => r.storeUrl, 'url', 'https://example.test/app.apk'));
    });
  });

  group('version comparison', () {
    test('compares dotted versions numerically', () {
      expect(isVersionBelow('1.2.9', '1.2.10'), isTrue);
      expect(isVersionBelow('1.10.0', '1.9.9'), isFalse);
      expect(isVersionBelow('1.0', '1.0.0'), isFalse);
      expect(isVersionBelow('1.0.0-dev', '1.0.1'), isTrue);
      expect(isVersionBelow('garbage', '2.0.0'), isFalse, reason: 'never lock users out on an unreadable version');
    });
  });

  group('offline limit', () {
    test('blocks new transactions after maxOfflineHours, and before the first sync', () {
      final lastSync = DateTime.utc(2026, 10, 24, 8);
      final policy = OfflinePolicy(lastSuccessfulSyncAtUtc: lastSync, maxOfflineHours: 4);

      expect(policy.allowsNewTransactions(lastSync.add(const Duration(hours: 4))), isTrue);
      expect(policy.allowsNewTransactions(lastSync.add(const Duration(hours: 4, minutes: 1))), isFalse);
      expect(const OfflinePolicy(lastSuccessfulSyncAtUtc: null, maxOfflineHours: 24).allowsNewTransactions(now), isFalse);
    });
  });
}

SyncStatus _status({bool deviceRevoked = false, bool wipeRequested = false, String? minVersion, String? storeUrl}) => SyncStatus(
      minVersion: minVersion,
      storeUrl: storeUrl,
      deviceRevoked: deviceRevoked,
      wipeRequested: wipeRequested,
      maxOfflineHours: 24,
      serverTimeUtc: DateTime.utc(2026, 10, 24, 12),
    );

class FakeSyncApi implements SyncApi {
  final calls = <String>[];
  final pushed = <List<PushCommand>>[];
  final pullTokens = <Map<String, String>>[];
  Map<String, PushResult> pushResults = {};
  List<PullResponse> pullPages = [];
  SyncStatus statusResponse = _status();
  SyncTransportException? failWith;

  @override
  Future<SyncStatus> status() async {
    calls.add('status');
    if (failWith != null) throw failWith!;
    return statusResponse;
  }

  @override
  Future<List<PushResult>> push(String deviceId, List<PushCommand> commands) async {
    calls.add('push');
    pushed.add(commands);
    return commands.map((c) => pushResults[c.id] ?? PushResult(id: c.id, status: 'Applied')).toList();
  }

  @override
  Future<PullResponse> pull(Map<String, String> tokens, {int limit = 1000}) async {
    calls.add('pull');
    pullTokens.add(Map.of(tokens));
    if (pullPages.isEmpty) {
      return PullResponse(tokens: tokens, hasMore: const {}, changes: const {});
    }
    return pullPages.removeAt(0);
  }
}
