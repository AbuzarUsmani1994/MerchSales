import 'package:uuid/uuid.dart';

import '../api/api_client.dart';
import '../config/app_config.dart';
import 'token_storage.dart';

class LoginResult {
  LoginResult({required this.mustChangePassword, required this.mfaRequired});

  final bool mustChangePassword;
  final bool mfaRequired;
}

/// Login with device registration (section 10, 14): the device row is upserted in the same
/// transaction as the login, and the refresh token references it (30-day mobile lifetime).
class AuthRepository {
  AuthRepository(this._apiClient, this._tokenStorage);

  final ApiClient _apiClient;
  final TokenStorage _tokenStorage;

  Future<LoginResult> login({
    required String tenantCode,
    required String username,
    required String password,
    String? totpCode,
  }) async {
    var deviceId = await _tokenStorage.readDeviceId();
    deviceId ??= const Uuid().v7();
    await _tokenStorage.saveDeviceId(deviceId);

    final response = await _apiClient.dio.post('/api/v1/auth/login', data: {
      'tenantCode': tenantCode,
      'username': username,
      'password': password,
      'totpCode': totpCode,
      'device': {
        'id': deviceId,
        'platform': AppConfig.platform,
        'name': null,
        'appVersion': AppConfig.appVersion,
        'osVersion': null,
        'pushToken': null,
      },
    });

    final data = response.data as Map<String, dynamic>;
    await _tokenStorage.saveTokens(
      accessToken: data['accessToken'] as String,
      refreshToken: data['refreshToken'] as String,
    );

    return LoginResult(
      mustChangePassword: data['mustChangePassword'] as bool,
      mfaRequired: data['mfaRequired'] as bool,
    );
  }

  Future<bool> isSignedIn() async => (await _tokenStorage.readAccessToken()) != null;

  Future<void> signOut() => _tokenStorage.clear();
}
