import 'dart:io';

import 'package:drift/drift.dart';
import 'package:drift/native.dart';
import 'package:path/path.dart' as p;
import 'package:path_provider/path_provider.dart';

part 'app_database.g.dart';

/// Local tables mirroring pulled entities, plus the offline outbox (section 14). Phase 0
/// only needs enough schema for the sync engine skeleton (settings/features/permissions
/// pull, one sample outbox command); Phase 1 adds the full set of synced entity tables.

class Settings extends Table {
  TextColumn get key => text()();
  TextColumn get value => text()();

  @override
  Set<Column> get primaryKey => {key};
}

class OutboxCommands extends Table {
  TextColumn get id => text()(); // client-generated command GUID
  TextColumn get type => text()();
  TextColumn get payloadJson => text()();
  IntColumn get clientSequence => integer()();
  TextColumn get status => text().withDefault(const Constant('Pending'))();
  TextColumn get lastError => text().nullable()();
  DateTimeColumn get createdOnDeviceAtUtc => dateTime()();

  @override
  Set<Column> get primaryKey => {id};
}

@DriftDatabase(tables: [Settings, OutboxCommands])
class AppDatabase extends _$AppDatabase {
  AppDatabase() : super(_openConnection());

  @override
  int get schemaVersion => 1;

  static LazyDatabase _openConnection() {
    return LazyDatabase(() async {
      final dir = await getApplicationDocumentsDirectory();
      final file = File(p.join(dir.path, 'stockorder.sqlite'));

      // TODO (Phase 1): open with SQLCipher encryption, per-install key in secure
      // storage (section 14) — see the pubspec.yaml note on the current drift
      // encrypted-NativeDatabase setup before wiring this up for real.
      return NativeDatabase.createInBackground(file);
    });
  }
}
