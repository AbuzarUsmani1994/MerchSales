import 'dart:io' show Platform;

import 'package:dio/dio.dart';
import 'package:uuid/uuid.dart';

import '../api/api_client.dart';
import '../config/app_config.dart';
import 'token_storage.dart';

/// Server calls for signing in and out (section 10, 14). Holds no state of its own; the
/// session controller decides what the app shows.
class AuthRepository {
  AuthRepository(this._apiClient, this._tokenStorage);

  final ApiClient _apiClient;
  final TokenStorage _tokenStorage;

  /// Logs in and registers this phone as a device in the same server transaction; the
  /// refresh token references it (30-day mobile lifetime).
  Future<UserProfile> login({
    required String tenantCode,
    required String username,
    required String password,
    String? totpCode,
  }) async {
    var deviceId = await _tokenStorage.readDeviceId();
    if (deviceId == null) {
      deviceId = const Uuid().v7();
      await _tokenStorage.saveDeviceId(deviceId);
    }

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
        'osVersion': _osVersion(),
        'pushToken': null,
      },
    });

    final data = response.data as Map<String, dynamic>;
    await _tokenStorage.saveTokens(
      accessToken: data['accessToken'] as String,
      refreshToken: data['refreshToken'] as String,
    );

    final profile = UserProfile(
      userId: data['userId'] as String,
      username: data['username'] as String,
      displayName: data['displayName'] as String,
      tenantId: data['tenantId'] as String,
      tenantCode: data['tenantCode'] as String,
      tenantName: data['tenantName'] as String,
      mustChangePassword: data['mustChangePassword'] as bool,
    );
    await _tokenStorage.saveProfile(profile);
    return profile;
  }

  Future<void> changePassword({required String currentPassword, required String newPassword}) async {
    await _apiClient.dio.post('/api/v1/auth/change-password', data: {
      'currentPassword': currentPassword,
      'newPassword': newPassword,
    });
  }

  /// Revokes the refresh token on the server, then forgets the session locally. A failed
  /// server call (offline) still signs out locally; the token then simply expires.
  Future<void> signOut() async {
    final refreshToken = await _tokenStorage.readRefreshToken();
    if (refreshToken != null) {
      try {
        await _apiClient.dio.post('/api/v1/auth/logout', data: {'refreshToken': refreshToken});
      } on DioException {
        // Signed out locally regardless.
      }
    }
    await _tokenStorage.clearSession();
  }

  static String? _osVersion() {
    final version = Platform.operatingSystemVersion;
    return version.length > 40 ? version.substring(0, 40) : version;
  }
}
