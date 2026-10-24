import 'dart:io';

import 'package:drift/drift.dart';
import 'package:drift/native.dart';
import 'package:path/path.dart' as p;
import 'package:path_provider/path_provider.dart';
import 'package:sqlite3/sqlite3.dart' show Database;

part 'app_database.g.dart';

/// Local copy of pulled entities plus the offline outbox (section 14). Every screen reads
/// from here; the sync engine is the only thing that talks to the server about this data.

/// tenancy.TenantSetting rows (pull entity "settings").
class LocalSettings extends Table {
  TextColumn get key => text()();
  TextColumn get value => text()();

  @override
  Set<Column> get primaryKey => {key};
}

/// tenancy.TenantFeature rows (pull entity "features").
class LocalFeatures extends Table {
  TextColumn get key => text()();
  BoolColumn get isEnabled => boolean()();

  @override
  Set<Column> get primaryKey => {key};
}

/// This user's permission grants (pull entity "permissions"). branchId '' = all branches.
class LocalPermissions extends Table {
  TextColumn get code => text()();
  TextColumn get branchId => text()();

  @override
  Set<Column> get primaryKey => {code, branchId};
}

/// Last token received per pull entity (section 12).
class SyncTokens extends Table {
  TextColumn get entity => text()();
  TextColumn get token => text()();

  @override
  Set<Column> get primaryKey => {entity};
}

/// Small key/value facts about this phone's sync state (last success, offline limit...).
class SyncMeta extends Table {
  TextColumn get key => text()();
  TextColumn get value => text()();

  @override
  Set<Column> get primaryKey => {key};
}

/// Commands created offline, pushed in clientSequence order (section 12).
class OutboxCommands extends Table {
  TextColumn get id => text()(); // client-generated command GUID, kept across retries
  TextColumn get type => text()();
  TextColumn get payloadJson => text()();
  IntColumn get clientSequence => integer()();
  TextColumn get status => text().withDefault(const Constant(OutboxStatus.pending))();
  TextColumn get errorCode => text().nullable()();
  TextColumn get lastError => text().nullable()();
  IntColumn get attempts => integer().withDefault(const Constant(0))();
  DateTimeColumn get createdOnDeviceAtUtc => dateTime()();
  TextColumn get resultJson => text().nullable()();

  @override
  Set<Column> get primaryKey => {id};
}

abstract final class OutboxStatus {
  /// Not yet accepted by the server (never sent, or the server failed and it will retry).
  static const pending = 'Pending';
  static const applied = 'Applied';

  /// Final: the server refused it. Shown on the sync status screen until dismissed.
  static const rejected = 'Rejected';
}

abstract final class SyncMetaKeys {
  static const lastSuccessfulSyncAtUtc = 'lastSuccessfulSyncAtUtc';
  static const lastSyncError = 'lastSyncError';
  static const maxOfflineHours = 'maxOfflineHours';
  static const nextClientSequence = 'nextClientSequence';
  static const ownerUserId = 'ownerUserId';
}

@DriftDatabase(tables: [LocalSettings, LocalFeatures, LocalPermissions, SyncTokens, SyncMeta, OutboxCommands])
class AppDatabase extends _$AppDatabase {
  AppDatabase(super.executor);

  /// The phone's database file, encrypted with SQLCipher using [hexKey] (32 random bytes kept
  /// in secure storage). Throws if the bundled SQLite isn't SQLCipher, rather than silently
  /// writing business data unencrypted.
  static Future<AppDatabase> openEncrypted(String hexKey) async {
    return AppDatabase(NativeDatabase.createInBackground(await databaseFile(), setup: (raw) => applyKey(raw, hexKey)));
  }

  /// Shared with tests, which open a temp file the same way.
  static void applyKey(Database raw, String hexKey) {
    raw.execute("PRAGMA key = \"x'$hexKey'\";");
    if (raw.select('PRAGMA cipher_version;').isEmpty) {
      throw StateError('SQLCipher is not available; refusing to open an unencrypted database.');
    }
  }

  static Future<File> databaseFile() async {
    final dir = await getApplicationDocumentsDirectory();
    return File(p.join(dir.path, 'stockorder.sqlite'));
  }

  /// Remote wipe (section 10, 14): the file and its journal files go; the caller deletes the key.
  static Future<void> deleteDatabaseFiles() async {
    final file = await databaseFile();
    for (final path in [file.path, '${file.path}-wal', '${file.path}-shm', '${file.path}-journal']) {
      final f = File(path);
      if (await f.exists()) await f.delete();
    }
  }

  @override
  int get schemaVersion => 1;

  // ---------- Outbox ----------

  /// Saves a new command with the next clientSequence in one local transaction, so the order
  /// commands were created in is the order the server applies them (section 12).
  Future<void> enqueueCommand({
    required String id,
    required String type,
    required String payloadJson,
    required DateTime createdOnDeviceAtUtc,
  }) {
    return transaction(() async {
      final sequence = await _takeNextClientSequence();
      await into(outboxCommands).insert(OutboxCommandsCompanion.insert(
        id: id,
        type: type,
        payloadJson: payloadJson,
        clientSequence: sequence,
        createdOnDeviceAtUtc: createdOnDeviceAtUtc,
      ));
    });
  }

  Future<int> _takeNextClientSequence() async {
    final current = int.tryParse(await readMeta(SyncMetaKeys.nextClientSequence) ?? '') ?? 1;
    await writeMeta(SyncMetaKeys.nextClientSequence, '${current + 1}');
    return current;
  }

  Future<List<OutboxCommand>> pendingCommands() => (select(outboxCommands)
        ..where((c) => c.status.equals(OutboxStatus.pending))
        ..orderBy([(c) => OrderingTerm.asc(c.clientSequence)]))
      .get();

  Stream<List<OutboxCommand>> watchCommands(String status) => (select(outboxCommands)
        ..where((c) => c.status.equals(status))
        ..orderBy([(c) => OrderingTerm.asc(c.clientSequence)]))
      .watch();

  Stream<int> watchPendingCount() {
    final count = outboxCommands.id.count();
    final query = selectOnly(outboxCommands)
      ..addColumns([count])
      ..where(outboxCommands.status.equals(OutboxStatus.pending));
    return query.map((row) => row.read(count) ?? 0).watchSingle();
  }

  Future<void> markApplied(String id, String? resultJson) => (update(outboxCommands)..where((c) => c.id.equals(id))).write(
        OutboxCommandsCompanion(
          status: const Value(OutboxStatus.applied),
          resultJson: Value(resultJson),
          errorCode: const Value(null),
          lastError: const Value(null),
        ),
      );

  Future<void> markRejected(String id, String? errorCode, String? message) =>
      (update(outboxCommands)..where((c) => c.id.equals(id))).write(
        OutboxCommandsCompanion(
          status: const Value(OutboxStatus.rejected),
          errorCode: Value(errorCode),
          lastError: Value(message),
        ),
      );

  /// The server couldn't apply it this time; it stays pending and is retried with the same id.
  Future<void> markAttemptFailed(String id, String? errorCode, String? message) => customUpdate(
        'UPDATE outbox_commands SET attempts = attempts + 1, error_code = ?, last_error = ? WHERE id = ?',
        variables: [Variable(errorCode), Variable(message), Variable(id)],
        updates: {outboxCommands},
      );

  Future<void> dismissRejected(String id) =>
      (delete(outboxCommands)..where((c) => c.id.equals(id) & c.status.equals(OutboxStatus.rejected))).go();

  Future<void> discardPending() => (delete(outboxCommands)..where((c) => c.status.equals(OutboxStatus.pending))).go();

  // ---------- Pulled data ----------

  Future<Map<String, String>> readTokens() async {
    final rows = await select(syncTokens).get();
    return {for (final row in rows) row.entity: row.token};
  }

  /// Applies one pull page atomically: upserts, the permission set (replaced whenever its
  /// token moved, even to an empty set), and the new tokens.
  Future<void> applyPull({
    required Map<String, String> tokens,
    required List<({String key, String value})> settings,
    required List<({String key, bool isEnabled})> features,
    required List<({String code, String? branchId})>? permissions,
  }) {
    return transaction(() async {
      await batch((b) {
        b.insertAllOnConflictUpdate(
          localSettings,
          settings.map((s) => LocalSettingsCompanion.insert(key: s.key, value: s.value)),
        );
        b.insertAllOnConflictUpdate(
          localFeatures,
          features.map((f) => LocalFeaturesCompanion.insert(key: f.key, isEnabled: f.isEnabled)),
        );
      });

      if (permissions != null) {
        await delete(localPermissions).go();
        await batch((b) => b.insertAll(
              localPermissions,
              permissions.map((g) => LocalPermissionsCompanion.insert(code: g.code, branchId: g.branchId ?? '')),
              mode: InsertMode.insertOrIgnore,
            ));
      }

      await batch((b) => b.insertAllOnConflictUpdate(
            syncTokens,
            tokens.entries.map((e) => SyncTokensCompanion.insert(entity: e.key, token: e.value)),
          ));
    });
  }

  Future<String?> readSetting(String key) async =>
      (await (select(localSettings)..where((s) => s.key.equals(key))).getSingleOrNull())?.value;

  Stream<int> watchPermissionCount() {
    final count = localPermissions.code.count(distinct: true);
    return (selectOnly(localPermissions)..addColumns([count])).map((row) => row.read(count) ?? 0).watchSingle();
  }

  Stream<int> watchEnabledFeatureCount() {
    final count = localFeatures.key.count();
    final query = selectOnly(localFeatures)
      ..addColumns([count])
      ..where(localFeatures.isEnabled.equals(true));
    return query.map((row) => row.read(count) ?? 0).watchSingle();
  }

  /// A different user signed in on this phone: their data must not mix with the last one's.
  /// Pending commands are discarded by the sign-out flow before this point, never pushed
  /// under someone else's account.
  Future<void> clearForNewUser(String userId) => transaction(() async {
        for (final TableInfo table in [localSettings, localFeatures, localPermissions, syncTokens, outboxCommands]) {
          await delete(table).go();
        }
        await (delete(syncMeta)..where((m) => m.key.isNotValue(SyncMetaKeys.nextClientSequence))).go();
        await writeMeta(SyncMetaKeys.ownerUserId, userId);
      });

  // ---------- Meta ----------

  Future<String?> readMeta(String key) async =>
      (await (select(syncMeta)..where((m) => m.key.equals(key))).getSingleOrNull())?.value;

  Stream<String?> watchMeta(String key) =>
      (select(syncMeta)..where((m) => m.key.equals(key))).watchSingleOrNull().map((row) => row?.value);

  Future<void> writeMeta(String key, String? value) async {
    if (value == null) {
      await (delete(syncMeta)..where((m) => m.key.equals(key))).go();
    } else {
      await into(syncMeta).insertOnConflictUpdate(SyncMetaCompanion.insert(key: key, value: value));
    }
  }
}
