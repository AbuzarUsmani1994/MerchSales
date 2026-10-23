import 'dart:async';

import 'package:dio/dio.dart';
import 'package:uuid/uuid.dart';

import '../auth/token_storage.dart';
import '../config/app_config.dart';

/// Section 9: mobile requests send X-App-Version and X-Device-Id; the API enforces the
/// minimum version and device revocation via these headers. Section 10: silent refresh on
/// 401, with a single in-flight refresh shared across concurrent requests.
class ApiClient {
  ApiClient(this._tokenStorage) {
    _dio = Dio(BaseOptions(baseUrl: AppConfig.apiBaseUrl));
    _dio.interceptors.add(InterceptorsWrapper(onRequest: _onRequest, onError: _onError));
  }

  final TokenStorage _tokenStorage;
  late final Dio _dio;
  Completer<String?>? _refreshCompleter;

  final _deviceRevokedController = StreamController<bool>.broadcast();

  Dio get dio => _dio;

  /// Emits `wipeRequested` (section 10, 14) when the API reports DEVICE_REVOKED, for the
  /// app shell to sign out — and wipe the local database, if requested — in one place.
  Stream<bool> get onDeviceRevoked => _deviceRevokedController.stream;

  Future<void> _onRequest(RequestOptions options, RequestInterceptorHandler handler) async {
    final accessToken = await _tokenStorage.readAccessToken();
    final deviceId = await _tokenStorage.readDeviceId();

    if (accessToken != null) {
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
    final code = (response?.data is Map) ? (response!.data as Map)['code'] as String? : null;

    if (code == 'DEVICE_REVOKED') {
      final wipeRequested = (response!.data as Map)['wipeRequested'] as bool? ?? false;
      await _tokenStorage.clear();
      _deviceRevokedController.add(wipeRequested);
      handler.next(error);
      return;
    }

    if (response?.statusCode != 401) {
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

      final response = await Dio(BaseOptions(baseUrl: AppConfig.apiBaseUrl))
          .post('/api/v1/auth/refresh', data: {'refreshToken': refreshToken});

      final newAccessToken = response.data['accessToken'] as String;
      final newRefreshToken = response.data['refreshToken'] as String;
      await _tokenStorage.saveTokens(accessToken: newAccessToken, refreshToken: newRefreshToken);

      completer.complete(newAccessToken);
      return newAccessToken;
    } catch (_) {
      await _tokenStorage.clear();
      completer.complete(null);
      return null;
    } finally {
      _refreshCompleter = null;
    }
  }
}
