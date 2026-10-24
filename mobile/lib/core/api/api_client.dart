import 'dart:async';

import 'package:dio/dio.dart';
import 'package:uuid/uuid.dart';

import '../auth/token_storage.dart';
import '../config/app_config.dart';

/// Something the server said that the whole app must react to, wherever it happened.
sealed class ApiEvent {
  const ApiEvent();
}

/// DEVICE_REVOKED (section 9): sign out, and erase local data when [wipeRequested].
final class DeviceRevokedEvent extends ApiEvent {
  const DeviceRevokedEvent({required this.wipeRequested});
  final bool wipeRequested;
}

/// APP_VERSION_UNSUPPORTED (426): block the app until it's updated.
final class AppVersionUnsupportedEvent extends ApiEvent {
  const AppVersionUnsupportedEvent({this.storeUrl});
  final String? storeUrl;
}

/// The refresh token was rejected: the session is over and the user must sign in again.
final class SessionExpiredEvent extends ApiEvent {
  const SessionExpiredEvent();
}

/// Section 9: mobile requests send X-App-Version and X-Device-Id; the API enforces the
/// minimum version and device revocation via these headers. Section 10: silent refresh on
/// 401, with a single in-flight refresh shared across concurrent requests.
class ApiClient {
  ApiClient(this._tokenStorage, {Dio? dio, Dio? refreshDio}) {
    _dio = dio ?? Dio(BaseOptions(baseUrl: AppConfig.apiBaseUrl, connectTimeout: _timeout, receiveTimeout: _timeout));
    _refreshDio = refreshDio ?? Dio(BaseOptions(baseUrl: AppConfig.apiBaseUrl, connectTimeout: _timeout, receiveTimeout: _timeout));
    _dio.interceptors.add(InterceptorsWrapper(onRequest: _onRequest, onError: _onError));
  }

  static const _timeout = Duration(seconds: 20);
  static const _noRefreshPaths = {'/api/v1/auth/login', '/api/v1/auth/refresh', '/api/v1/auth/logout'};

  final TokenStorage _tokenStorage;
  late final Dio _dio;
  late final Dio _refreshDio;
  Completer<String?>? _refreshCompleter;

  final _events = StreamController<ApiEvent>.broadcast();

  Dio get dio => _dio;

  Stream<ApiEvent> get events => _events.stream;

  Future<void> _onRequest(RequestOptions options, RequestInterceptorHandler handler) async {
    final accessToken = await _tokenStorage.readAccessToken();
    final deviceId = await _tokenStorage.readDeviceId();

    if (accessToken != null && !options.headers.containsKey('Authorization')) {
      options.headers['Authorization'] = 'Bearer $accessToken';
    }
    if (deviceId != null) {
      options.headers['X-Device-Id'] = deviceId;
    }
    options.headers['X-App-Version'] = AppConfig.appVersion;
    options.headers['X-Correlation-Id'] = const Uuid().v7();

    handler.next(options);
  }

  Future<void> _onError(DioException error, ErrorInterceptorHandler handler) async {
    final response = error.response;
    final data = response?.data;
    final code = data is Map ? data['code'] as String? : null;

    if (code == 'DEVICE_REVOKED') {
      _events.add(DeviceRevokedEvent(wipeRequested: (data as Map)['wipeRequested'] as bool? ?? false));
      handler.next(error);
      return;
    }

    if (code == 'APP_VERSION_UNSUPPORTED' || response?.statusCode == 426) {
      _events.add(const AppVersionUnsupportedEvent());
      handler.next(error);
      return;
    }

    if (response?.statusCode != 401 || _noRefreshPaths.contains(error.requestOptions.path)) {
      handler.next(error);
      return;
    }

    final newToken = await _refreshAccessToken();
    if (newToken == null) {
      handler.next(error);
      return;
    }

    final retryRequest = error.requestOptions;
    retryRequest.headers['Authorization'] = 'Bearer $newToken';
    try {
      final retryResponse = await _dio.fetch(retryRequest);
      handler.resolve(retryResponse);
    } on DioException catch (retryError) {
      handler.next(retryError);
    }
  }

  Future<String?> _refreshAccessToken() async {
    if (_refreshCompleter != null) {
      return _refreshCompleter!.future;
    }

    final completer = Completer<String?>();
    _refreshCompleter = completer;

    try {
      final refreshToken = await _tokenStorage.readRefreshToken();
      if (refreshToken == null) {
        completer.complete(null);
        return null;
      }

      final response = await _refreshDio.post('/api/v1/auth/refresh', data: {'refreshToken': refreshToken});

      final newAccessToken = response.data['accessToken'] as String;
      final newRefreshToken = response.data['refreshToken'] as String;
      await _tokenStorage.saveTokens(accessToken: newAccessToken, refreshToken: newRefreshToken);

      completer.complete(newAccessToken);
      return newAccessToken;
    } on DioException catch (e) {
      // Only a definite "no" from the server ends the session; being offline must not.
      if (e.response != null) {
        await _tokenStorage.clearSession();
        _events.add(const SessionExpiredEvent());
      }
      completer.complete(null);
      return null;
    } finally {
      _refreshCompleter = null;
    }
  }
}

/// A short, user-safe message from a failed request: the server's ProblemDetails `detail`,
/// or null when there was no response at all (offline, timeout, DNS).
String? problemDetail(DioException e) {
  final data = e.response?.data;
  return data is Map ? data['detail'] as String? : null;
}

String? problemCode(DioException e) {
  final data = e.response?.data;
  return data is Map ? data['code'] as String? : null;
}
