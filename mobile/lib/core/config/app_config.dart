/// Build-time config (section 17.1): the CI workflow passes the API host via
/// `--dart-define=API_BASE_URL=...` from the repository variable of the same name, never
/// hardcoded or committed to the repo.
class AppConfig {
  static const String apiBaseUrl = String.fromEnvironment(
    'API_BASE_URL',
    defaultValue: 'http://10.0.2.2:5280',
  );

  static const String appVersion = '1.0.0';
  static const String platform = 'Android';
}
