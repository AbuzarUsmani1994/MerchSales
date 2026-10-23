import 'package:flutter_secure_storage/flutter_secure_storage.dart';

/// Tokens live in secure storage, never in plain SharedPreferences (section 14).
class TokenStorage {
  TokenStorage(this._storage);

  final FlutterSecureStorage _storage;

  static const _accessTokenKey = 'stockorder.accessToken';
  static const _refreshTokenKey = 'stockorder.refreshToken';
  static const _deviceIdKey = 'stockorder.deviceId';

  Future<String?> readAccessToken() => _storage.read(key: _accessTokenKey);
  Future<String?> readRefreshToken() => _storage.read(key: _refreshTokenKey);
  Future<String?> readDeviceId() => _storage.read(key: _deviceIdKey);

  Future<void> saveTokens({required String accessToken, required String refreshToken}) async {
    await _storage.write(key: _accessTokenKey, value: accessToken);
    await _storage.write(key: _refreshTokenKey, value: refreshToken);
  }

  Future<void> saveDeviceId(String deviceId) => _storage.write(key: _deviceIdKey, value: deviceId);

  Future<void> clear() async {
    await _storage.delete(key: _accessTokenKey);
    await _storage.delete(key: _refreshTokenKey);
    // Device id is intentionally kept — the device identity survives a sign-out.
  }
}
