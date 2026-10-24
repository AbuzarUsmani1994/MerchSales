import 'dart:convert';

import 'package:dio/dio.dart';

import '../api/api_client.dart';

/// The server side of section 12, behind an interface so the engine can be tested offline.
abstract interface class SyncApi {
  Future<SyncStatus> status();

  Future<PullResponse> pull(Map<String, String> tokens, {int limit = 1000});

  Future<List<PushResult>> push(String deviceId, List<PushCommand> commands);
}

/// No answer from the server (offline, timeout, 5xx): nothing was decided, try again later.
class SyncTransportException implements Exception {
  SyncTransportException(this.message);
  final String message;

  @override
  String toString() => message;
}

class SyncStatus {
  const SyncStatus({
    required this.minVersion,
    required this.storeUrl,
    required this.deviceRevoked,
    required this.wipeRequested,
    required this.maxOfflineHours,
    required this.serverTimeUtc,
  });

  final String? minVersion;
  final String? storeUrl;
  final bool deviceRevoked;
  final bool wipeRequested;
  final int maxOfflineHours;
  final DateTime serverTimeUtc;

  factory SyncStatus.fromJson(Map<String, dynamic> json) => SyncStatus(
        minVersion: json['minVersion'] as String?,
        storeUrl: json['storeUrl'] as String?,
        deviceRevoked: json['deviceRevoked'] as bool,
        wipeRequested: json['wipeRequested'] as bool,
        maxOfflineHours: json['maxOfflineHours'] as int,
        serverTimeUtc: DateTime.parse(json['serverTimeUtc'] as String),
      );
}

class PullResponse {
  const PullResponse({required this.tokens, required this.hasMore, required this.changes});

  final Map<String, String> tokens;
  final Map<String, bool> hasMore;
  final Map<String, List<Map<String, dynamic>>> changes;

  bool get anyHasMore => hasMore.values.any((more) => more);

  factory PullResponse.fromJson(Map<String, dynamic> json) => PullResponse(
        tokens: (json['tokens'] as Map).map((k, v) => MapEntry(k as String, v as String)),
        hasMore: (json['hasMore'] as Map).map((k, v) => MapEntry(k as String, v as bool)),
        changes: (json['changes'] as Map).map(
          (k, v) => MapEntry(k as String, (v as List).cast<Map<String, dynamic>>()),
        ),
      );
}

class PushCommand {
  const PushCommand({
    required this.id,
    required this.type,
    required this.clientSequence,
    required this.createdOnDeviceAtUtc,
    required this.payload,
  });

  final String id;
  final String type;
  final int clientSequence;
  final DateTime createdOnDeviceAtUtc;
  final Map<String, dynamic> payload;

  Map<String, dynamic> toJson() => {
        'id': id,
        'type': type,
        'clientSequence': clientSequence,
        'createdOnDeviceAtUtc': createdOnDeviceAtUtc.toUtc().toIso8601String(),
        'payload': payload,
      };
}

class PushResult {
  const PushResult({required this.id, required this.status, this.errorCode, this.message, this.result});

  final String id;

  /// Applied | AppliedWithChanges | Rejected | Failed (section 12).
  final String status;
  final String? errorCode;
  final String? message;
  final Object? result;

  factory PushResult.fromJson(Map<String, dynamic> json) => PushResult(
        id: json['id'] as String,
        status: json['status'] as String,
        errorCode: json['errorCode'] as String?,
        message: json['message'] as String?,
        result: json['result'],
      );
}

class DioSyncApi implements SyncApi {
  DioSyncApi(this._apiClient);

  final ApiClient _apiClient;

  @override
  Future<SyncStatus> status() => _call(() async {
        final response = await _apiClient.dio.get('/api/v1/sync/status');
        return SyncStatus.fromJson(response.data as Map<String, dynamic>);
      });

  @override
  Future<PullResponse> pull(Map<String, String> tokens, {int limit = 1000}) => _call(() async {
        final response = await _apiClient.dio.get('/api/v1/sync/pull', queryParameters: {
          if (tokens.isNotEmpty) 'tokens': base64Encode(utf8.encode(jsonEncode(tokens))),
          'limit': limit,
        });
        return PullResponse.fromJson(response.data as Map<String, dynamic>);
      });

  @override
  Future<List<PushResult>> push(String deviceId, List<PushCommand> commands) => _call(() async {
        final response = await _apiClient.dio.post('/api/v1/sync/push', data: {
          'deviceId': deviceId,
          'commands': commands.map((c) => c.toJson()).toList(),
        });
        return ((response.data as Map<String, dynamic>)['results'] as List)
            .map((r) => PushResult.fromJson(r as Map<String, dynamic>))
            .toList();
      });

  static Future<T> _call<T>(Future<T> Function() request) async {
    try {
      return await request();
    } on DioException catch (e) {
      throw SyncTransportException(problemDetail(e) ?? e.message ?? 'Network error');
    }
  }
}
