import 'dart:io' show Platform;

/// Build-time config (section 17.1): the CI workflow passes these with `--dart-define`, from
/// the repository variable API_BASE_URL and the chosen flavor — never hardcoded in the repo.
class AppConfig {
  static const String _apiBaseUrl = String.fromEnvironment('API_BASE_URL');

  /// `dev` (debug-signed, may use plain HTTP to a LAN server) or `prod` (HTTPS only).
  static const String flavor = String.fromEnvironment('FLAVOR', defaultValue: 'dev');

  /// Sent as X-App-Version so the server can enforce its minimum version (section 9).
  static const String appVersion = String.fromEnvironment('APP_VERSION', defaultValue: '1.0.0');

  /// Empty when the build had no API_BASE_URL; the login screen says so instead of failing
  /// with an unreadable network error.
  static String get apiBaseUrl => _apiBaseUrl.endsWith('/') ? _apiBaseUrl.substring(0, _apiBaseUrl.length - 1) : _apiBaseUrl;

  static bool get isServerConfigured => apiBaseUrl.isNotEmpty;

  static String get platform => Platform.isIOS ? 'iOS' : 'Android';
}
