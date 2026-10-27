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
  static const _industryKey = 'stockorder.industry';
  static const _languageKey = 'stockorder.language';

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

  /// The last company's industry ("Restaurant", "Pharma"): the app's theme, kept across sign-outs
  /// so the sign-in screen already looks like the company's app.
  Future<String?> readIndustry() => _storage.read(key: _industryKey);

  Future<void> saveIndustry(String industry) => _storage.write(key: _industryKey, value: industry);

  /// The language the user picked ("en", "ur"); null = follow the phone. A preference of the
  /// phone's user, so it stays across sign-outs.
  Future<String?> readLanguage() => _storage.read(key: _languageKey);

  Future<void> saveLanguage(String? code) =>
      code == null ? _storage.delete(key: _languageKey) : _storage.write(key: _languageKey, value: code);

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
    this.industry,
  });

  final String userId;
  final String username;
  final String displayName;
  final String tenantId;
  final String tenantCode;
  final String tenantName;
  final bool mustChangePassword;

  /// The company's industry template; null in profiles saved before it was added.
  final String? industry;

  UserProfile copyWith({bool? mustChangePassword}) => UserProfile(
        userId: userId,
        username: username,
        displayName: displayName,
        tenantId: tenantId,
        tenantCode: tenantCode,
        tenantName: tenantName,
        mustChangePassword: mustChangePassword ?? this.mustChangePassword,
        industry: industry,
      );

  factory UserProfile.fromJson(Map<String, dynamic> json) => UserProfile(
        userId: json['userId'] as String,
        username: json['username'] as String,
        displayName: json['displayName'] as String,
        tenantId: json['tenantId'] as String,
        tenantCode: json['tenantCode'] as String,
        tenantName: json['tenantName'] as String,
        mustChangePassword: json['mustChangePassword'] as bool,
        industry: json['industry'] as String?,
      );

  Map<String, dynamic> toJson() => {
        'userId': userId,
        'username': username,
        'displayName': displayName,
        'tenantId': tenantId,
        'tenantCode': tenantCode,
        'tenantName': tenantName,
        'mustChangePassword': mustChangePassword,
        'industry': industry,
      };
}
