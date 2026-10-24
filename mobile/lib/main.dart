import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';

import 'app.dart';
import 'core/auth/token_storage.dart';
import 'core/db/app_database.dart';
import 'core/providers.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();

  // The encrypted database needs its key from secure storage before anything reads it.
  final key = await TokenStorage(const FlutterSecureStorage()).readOrCreateDatabaseKey();
  final database = await AppDatabase.openEncrypted(key);

  // Firebase (push, Crashlytics) is intentionally not initialised yet: section 17.1 requires
  // the app to build and run without google-services.json. It is wired up with push in Phase 1.
  runApp(ProviderScope(
    overrides: [initialDatabaseProvider.overrideWithValue(database)],
    child: const StockOrderApp(),
  ));
}
