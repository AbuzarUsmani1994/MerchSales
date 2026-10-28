import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:uuid/uuid.dart';

import '../../core/providers.dart';

/// A table on the waiter's map: free, taken, or waiting for its bill.
class TableMapTable {
  const TableMapTable({required this.id, required this.name, required this.seats, required this.status, this.order});

  factory TableMapTable.fromJson(Map<String, dynamic> j) => TableMapTable(
        id: j['id'] as String,
        name: j['name'] as String,
        seats: j['seats'] as int,
        status: j['status'] as String,
        order: j['order'] == null ? null : TableOrderSummary.fromJson(j['order'] as Map<String, dynamic>),
      );

  final String id;
  final String name;
  final int seats;

  /// Free, Occupied or BillRequested.
  final String status;
  final TableOrderSummary? order;

  bool get isFree => status == 'Free';
  bool get billRequested => status == 'BillRequested';
}

class TableOrderSummary {
  const TableOrderSummary({
    required this.orderId,
    required this.orderNo,
    required this.covers,
    required this.serverName,
    required this.openedAtUtc,
    required this.grandTotal,
    required this.etag,
    required this.readyRounds,
    required this.inKitchenRounds,
  });

  factory TableOrderSummary.fromJson(Map<String, dynamic> j) => TableOrderSummary(
        orderId: j['orderId'] as String,
        orderNo: j['orderNo'] as int?,
        covers: j['covers'] as int?,
        serverName: j['serverName'] as String?,
        openedAtUtc: DateTime.parse(j['openedAtUtc'] as String),
        grandTotal: (j['grandTotal'] as num).toDouble(),
        etag: j['etag'] as String,
        readyRounds: j['readyRounds'] as int,
        inKitchenRounds: j['inKitchenRounds'] as int,
      );

  final String orderId;
  final int? orderNo;
  final int? covers;
  final String? serverName;
  final DateTime openedAtUtc;
  final double grandTotal;
  final String etag;

  /// Rounds the kitchen marked ready that nobody took out yet.
  final int readyRounds;
  final int inKitchenRounds;
}

class TableMapArea {
  const TableMapArea({required this.id, required this.name, required this.tables});

  factory TableMapArea.fromJson(Map<String, dynamic> j) => TableMapArea(
        id: j['id'] as String,
        name: j['name'] as String,
        tables: [for (final t in j['tables'] as List) TableMapTable.fromJson(t as Map<String, dynamic>)],
      );

  final String id;
  final String name;
  final List<TableMapTable> tables;
}

class TableOrderLine {
  const TableOrderLine({required this.name, required this.quantity, required this.lineTotal, this.notes});

  final String name;
  final double quantity;
  final double lineTotal;
  final String? notes;
}

/// An open table order as the server has it now.
class TableOrder {
  const TableOrder({required this.id, required this.orderNo, required this.etag, required this.grandTotal, required this.lines, this.covers});

  factory TableOrder.fromJson(Map<String, dynamic> j) => TableOrder(
        id: j['id'] as String,
        orderNo: j['orderNo'] as int?,
        etag: j['etag'] as String,
        grandTotal: (j['grandTotal'] as num).toDouble(),
        covers: j['covers'] as int?,
        lines: [
          for (final l in j['lines'] as List)
            TableOrderLine(
              name: l['productName'] as String,
              quantity: (l['quantity'] as num).toDouble(),
              lineTotal: (l['lineTotal'] as num).toDouble(),
              notes: l['notes'] as String?,
            ),
        ],
      );

  final String id;
  final int? orderNo;
  final String etag;
  final double grandTotal;
  final int? covers;
  final List<TableOrderLine> lines;
}

typedef TableLine = ({String productId, int quantity, String? notes});

/// Tables talk to the server directly, not through the offline queue: the kitchen must get a
/// round now, and two waiters must never open the same table. Each send carries an
/// Idempotency-Key, so a retry after a lost answer doesn't send the round twice.
class TableApi {
  TableApi(this._dio);

  final Dio _dio;

  static Options _key(String idempotencyKey, {String? etag}) =>
      Options(headers: {'Idempotency-Key': idempotencyKey, if (etag != null) 'If-Match': '"$etag"'});

  Future<List<TableMapArea>> tableMap(String branchId) async {
    final response = await _dio.get<List<dynamic>>('/api/v1/restaurant/table-map', queryParameters: {'branchId': branchId});
    return [for (final a in response.data!) TableMapArea.fromJson(a as Map<String, dynamic>)];
  }

  Future<TableOrder> order(String orderId) async {
    final response = await _dio.get<Map<String, dynamic>>('/api/v1/sales/orders/$orderId');
    return TableOrder.fromJson(response.data!);
  }

  Future<TableOrder> openTable({
    required String branchId,
    required String tableId,
    required int? covers,
    required List<TableLine> lines,
    required String? note,
    required String idempotencyKey,
  }) async {
    final response = await _dio.post<Map<String, dynamic>>(
      '/api/v1/sales/orders',
      data: {
        'branchId': branchId,
        'channel': 'DineIn',
        'tableId': tableId,
        'covers': covers,
        'notes': note,
        'lines': [for (final l in lines) {'productId': l.productId, 'quantity': l.quantity, 'notes': l.notes}],
      },
      options: _key(idempotencyKey),
    );
    return TableOrder.fromJson(response.data!);
  }

  Future<TableOrder> addRound({
    required String orderId,
    required String etag,
    required List<TableLine> lines,
    required String? note,
    required String idempotencyKey,
  }) async {
    final response = await _dio.post<Map<String, dynamic>>(
      '/api/v1/sales/orders/$orderId/rounds',
      data: {
        'notes': note,
        'lines': [for (final l in lines) {'productId': l.productId, 'quantity': l.quantity, 'notes': l.notes}],
      },
      options: _key(idempotencyKey, etag: etag),
    );
    return TableOrder.fromJson(response.data!);
  }

  Future<void> requestBill(String orderId, String etag) =>
      _dio.post<void>('/api/v1/sales/orders/$orderId/request-bill', options: _key(const Uuid().v4(), etag: etag));

  /// Everything the kitchen marked ready for this order went out to the table.
  Future<void> serveReady(String orderId) =>
      _dio.post<void>('/api/v1/restaurant/kitchen/orders/$orderId/served', options: _key(const Uuid().v4()));
}

final tableApiProvider = Provider<TableApi>((ref) => TableApi(ref.watch(apiClientProvider).dio));
