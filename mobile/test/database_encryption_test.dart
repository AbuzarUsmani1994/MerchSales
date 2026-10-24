import 'dart:io';

import 'package:drift/drift.dart' show Variable;
import 'package:drift/native.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:sqlite3/sqlite3.dart';
import 'package:stockorder_mobile/core/db/app_database.dart';

/// Section 14: the local database is encrypted with SQLCipher and a per-install key.
void main() {
  late Directory dir;
  late File file;
  const key = '00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff';

  setUp(() {
    dir = Directory.systemTemp.createTempSync('stockorder_db_test');
    file = File('${dir.path}/test.sqlite');
  });

  tearDown(() => dir.deleteSync(recursive: true));

  test('data written with the key can be read back with it', () async {
    final db = AppDatabase(NativeDatabase(file, setup: (raw) => AppDatabase.applyKey(raw, key)));
    await db.writeMeta('probe', 'secret-value');
    await db.close();

    final reopened = AppDatabase(NativeDatabase(file, setup: (raw) => AppDatabase.applyKey(raw, key)));
    expect(await reopened.readMeta('probe'), 'secret-value');
    await reopened.close();
  });

  test('the file is unreadable without the key or with a wrong key', () async {
    final db = AppDatabase(NativeDatabase(file, setup: (raw) => AppDatabase.applyKey(raw, key)));
    await db.writeMeta('probe', 'secret-value');
    await db.close();

    final noKey = sqlite3.open(file.path);
    expect(() => noKey.select('SELECT count(*) FROM sync_meta'), throwsA(isA<SqliteException>()));
    noKey.close();

    const wrongKey = 'ffeeddccbbaa99887766554433221100ffeeddccbbaa99887766554433221100';
    final wrong = AppDatabase(NativeDatabase(file, setup: (raw) => AppDatabase.applyKey(raw, wrongKey)));
    await expectLater(wrong.customSelect('SELECT count(*) FROM sync_meta', variables: <Variable>[]).get(), throwsA(anything));
    await wrong.close();

    expect(file.readAsBytesSync().take(16), isNot(equals('SQLite format 3\u0000'.codeUnits)),
        reason: 'an unencrypted SQLite file starts with this header');
  });
}
