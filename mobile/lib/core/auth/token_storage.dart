import 'dart:convert';
import 'dart:math';

import 'package:flutter_secure_storage/flutter_secure_storage.dart';

/// Everything secret or identity-related lives in secure storage (Android Keystore / iOS
/// Keychain), never in plain files (section 14).
class TokenStorage {
  TokenStorage(this._storage);

  final FlutterSecureStorage _storage;

  static const _accessTokenKey = 'stockorder.accessToken';
  static const _refreshTokenKey = 'stockorder.refreshToken';
  static const _deviceIdKey = 'stockorder.deviceId';
  static const _databaseKeyKey = 'stockorder.databaseKey';
  static const _profileKey = 'stockorder.profile';
  static const _biometricKey = 'stockorder.biometric';

  Future<String?> readAccessToken() => _storage.read(key: _accessTokenKey);
  Future<String?> readRefreshToken() => _storage.read(key: _refreshTokenKey);
  Future<String?> readDeviceId() => _storage.read(key: _deviceIdKey);

  Future<void> saveTokens({required String accessToken, required String refreshToken}) async {
    await _storage.write(key: _accessTokenKey, value: accessToken);
    await _storage.write(key: _refreshTokenKey, value: refreshToken);
  }

  Future<void> saveDeviceId(String deviceId) => _storage.write(key: _deviceIdKey, value: deviceId);

  /// After the administrator revokes this phone the server refuses its device id for good,
  /// so the next sign-in registers it as a new device (visible again on the Devices page).
  Future<void> forgetDeviceId() => _storage.delete(key: _deviceIdKey);

  /// A random 256-bit key per install for the SQLCipher database (section 14), created on first use.
  Future<String> readOrCreateDatabaseKey() async {
    final existing = await _storage.read(key: _databaseKeyKey);
    if (existing != null) return existing;

    final random = Random.secure();
    final key = List.generate(32, (_) => random.nextInt(256)).map((b) => b.toRadixString(16).padLeft(2, '0')).join();
    await _storage.write(key: _databaseKeyKey, value: key);
    return key;
  }

  Future<void> deleteDatabaseKey() => _storage.delete(key: _databaseKeyKey);

  Future<UserProfile?> readProfile() async {
    final raw = await _storage.read(key: _profileKey);
    return raw == null ? null : UserProfile.fromJson(jsonDecode(raw) as Map<String, dynamic>);
  }

  Future<void> saveProfile(UserProfile profile) => _storage.write(key: _profileKey, value: jsonEncode(profile.toJson()));

  Future<bool?> readBiometricChoice() async {
    final raw = await _storage.read(key: _biometricKey);
    return raw == null ? null : raw == 'true';
  }

  Future<void> saveBiometricChoice(bool enabled) => _storage.write(key: _biometricKey, value: '$enabled');

  /// Signs out: tokens and the "must change password" state go; the device id, database key,
  /// last profile (to detect a different user next time) and biometric choice stay.
  Future<void> clearSession() async {
    await _storage.delete(key: _accessTokenKey);
    await _storage.delete(key: _refreshTokenKey);
  }
}

/// Who is signed in, kept across app restarts so the app opens without a network round trip.
class UserProfile {
  const UserProfile({
    required this.userId,
    required this.username,
    required this.displayName,
    required this.tenantId,
    required this.tenantCode,
    required this.tenantName,
    required this.mustChangePassword,
  });

  final String userId;
  final String username;
  final String displayName;
  final String tenantId;
  final String tenantCode;
  final String tenantName;
  final bool mustChangePassword;

  UserProfile copyWith({bool? mustChangePassword}) => UserProfile(
        userId: userId,
        username: username,
        displayName: displayName,
        tenantId: tenantId,
        tenantCode: tenantCode,
        tenantName: tenantName,
        mustChangePassword: mustChangePassword ?? this.mustChangePassword,
      );

  factory UserProfile.fromJson(Map<String, dynamic> json) => UserProfile(
        userId: json['userId'] as String,
        username: json['username'] as String,
        displayName: json['displayName'] as String,
        tenantId: json['tenantId'] as String,
        tenantCode: json['tenantCode'] as String,
        tenantName: json['tenantName'] as String,
        mustChangePassword: json['mustChangePassword'] as bool,
      );

  Map<String, dynamic> toJson() => {
        'userId': userId,
        'username': username,
        'displayName': displayName,
        'tenantId': tenantId,
        'tenantCode': tenantCode,
        'tenantName': tenantName,
        'mustChangePassword': mustChangePassword,
      };
}
