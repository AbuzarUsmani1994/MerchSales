import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';

import '../shared/theme.dart';
import 'api/api_client.dart';
import 'auth/auth_repository.dart';
import 'auth/biometric_service.dart';
import 'auth/token_storage.dart';
import 'config/app_config.dart';
import 'db/app_database.dart';
import 'images/product_image_cache.dart';
import 'session/session_controller.dart';
import 'sync/sync_api.dart';
import 'sync/sync_engine.dart';

final secureStorageProvider = Provider<FlutterSecureStorage>((ref) => const FlutterSecureStorage());

final tokenStorageProvider = Provider<TokenStorage>((ref) => TokenStorage(ref.watch(secureStorageProvider)));

final apiClientProvider = Provider<ApiClient>((ref) => ApiClient(ref.watch(tokenStorageProvider)));

final authRepositoryProvider = Provider<AuthRepository>(
  (ref) => AuthRepository(ref.watch(apiClientProvider), ref.watch(tokenStorageProvider)),
);

/// Whether this build knows its server (API_BASE_URL); a provider so tests can say yes.
final serverConfiguredProvider = Provider<bool>((ref) => AppConfig.isServerConfigured);

final biometricServiceProvider = Provider<BiometricService>((ref) => BiometricService());

/// The database opened in main() (it needs the key from secure storage first).
final initialDatabaseProvider = Provider<AppDatabase>((ref) => throw UnimplementedError('Overridden in main()'));

/// The current local database. Replaced by a fresh, empty one after a remote wipe.
final databaseProvider = NotifierProvider<DatabaseController, AppDatabase>(DatabaseController.new);

class DatabaseController extends Notifier<AppDatabase> {
  @override
  AppDatabase build() => ref.read(initialDatabaseProvider);

  /// Remote wipe (section 10, 14): close, delete the file and its key, start an empty one
  /// with a new key.
  Future<void> wipe() async {
    await state.close();
    await AppDatabase.deleteDatabaseFiles();
    final storage = ref.read(tokenStorageProvider);
    await storage.deleteDatabaseKey();
    await ref.read(productImageCacheProvider).clear();
    state = await AppDatabase.openEncrypted(await storage.readOrCreateDatabaseKey());
  }
}

/// Menu pictures stored on the phone (downloaded after each sync).
final productImageCacheProvider = Provider<ProductImageCache>((ref) => ProductImageCache(ref.watch(apiClientProvider).dio));

final syncApiProvider = Provider<SyncApi>((ref) => DioSyncApi(ref.watch(apiClientProvider)));

final syncEngineProvider = Provider<SyncEngine>((ref) {
  final storage = ref.watch(tokenStorageProvider);
  return SyncEngine(
    api: ref.watch(syncApiProvider),
    db: ref.watch(databaseProvider),
    deviceId: storage.readDeviceId,
    appVersion: AppConfig.appVersion,
  );
});

/// The industry read from secure storage at start-up (main), before the first frame.
final initialIndustryProvider = Provider<AppIndustry>((ref) => AppIndustry.standard);

/// The company's industry: the app's theme. Set at sign-in and refreshed by every sync.
final industryProvider = NotifierProvider<IndustryController, AppIndustry>(IndustryController.new);

class IndustryController extends Notifier<AppIndustry> {
  @override
  AppIndustry build() => ref.read(initialIndustryProvider);

  Future<void> set(String? serverIndustry) async {
    if (serverIndustry == null) return;
    state = AppIndustry.fromServer(serverIndustry);
    await ref.read(tokenStorageProvider).saveIndustry(serverIndustry);
  }
}

/// The language saved on the phone, read at start-up (main) before the first frame.
final initialLanguageProvider = Provider<String?>((ref) => null);

/// The app's language: "en", "ur", or null to follow the phone's language.
final languageProvider = NotifierProvider<LanguageController, String?>(LanguageController.new);

class LanguageController extends Notifier<String?> {
  @override
  String? build() => ref.read(initialLanguageProvider);

  Future<void> set(String code) async {
    state = code;
    await ref.read(tokenStorageProvider).saveLanguage(code);
  }
}

final sessionControllerProvider = NotifierProvider<SessionController, SessionState>(SessionController.new);

final syncControllerProvider = NotifierProvider<SyncController, SyncUiState>(SyncController.new);
