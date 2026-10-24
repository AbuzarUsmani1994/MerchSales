import 'dart:convert';

import 'package:drift/drift.dart' show TableInfo;
import 'package:drift/native.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:stockorder_mobile/core/db/app_database.dart';
import 'package:stockorder_mobile/core/sync/sync_api.dart';
import 'package:stockorder_mobile/core/sync/sync_engine.dart';

/// The phone's menu and orders: what the waiter may order at their branch, the local order
/// plus its outbox command, and how the server's answer shows up.
void main() {
  late AppDatabase db;
  final now = DateTime.utc(2026, 10, 24, 12);
  const lkc = 'branch-lkc';
  const edn = 'branch-edn';

  setUp(() => db = AppDatabase(NativeDatabase.memory()));
  tearDown(() => db.close());

  Map<String, dynamic> product(String id, String name, {String behaviour = 'Recipe', String? owner, String? category, bool active = true}) => {
        'id': id, 'sku': id, 'name': name, 'shortName': null, 'description': null, 'categoryId': category, 'productTypeId': 't',
        'behaviour': behaviour, 'baseUomId': 'portion', 'salesUomId': null, 'ownerBranchId': owner, 'isActive': active,
      };

  Map<String, dynamic> price(String id, String list, String productId, num unitPrice) => {
        'id': id, 'priceListId': list, 'productId': productId, 'uomId': 'portion', 'minQuantity': 1, 'unitPrice': unitPrice,
        'validFrom': null, 'validTo': null,
      };

  Future<void> pullMenu([Map<String, List<Map<String, dynamic>>> extra = const {}]) => db.applyPull(
        tokens: const {},
        settings: const [],
        features: const [],
        permissions: null,
        rows: {
          'uoms': [
            {'id': 'portion', 'code': 'portion', 'name': 'Portion', 'decimalPlaces': 0, 'isActive': true},
          ],
          'categories': [
            {'id': 'pizza', 'parentId': null, 'name': 'Pizzas', 'sortOrder': 1, 'isActive': true},
            {'id': 'old', 'parentId': null, 'name': 'Retired', 'sortOrder': 2, 'isActive': false},
          ],
          'priceLists': [
            {'id': 'menu', 'name': 'Menu', 'isDefault': true, 'branchId': null, 'isActive': true},
            {'id': 'lkc-list', 'name': 'LKC prices', 'isDefault': false, 'branchId': lkc, 'isActive': true},
          ],
          'products': [
            product('tikka', 'Tikka Pizza', category: 'pizza'),
            product('special', 'Lake City Special', owner: lkc),
            product('eden', 'Eden Special', owner: edn),
            product('cola', 'Cola', behaviour: 'Stocked'),
            product('off', 'Off at LKC'),
            product('gone', 'Retired dish', category: 'old'),
            product('free', 'No price yet'),
          ],
          'priceListItems': [
            price('p1', 'menu', 'tikka', 1200),
            price('p2', 'lkc-list', 'tikka', 1250),
            price('p3', 'lkc-list', 'special', 900),
            price('p4', 'menu', 'off', 500),
            price('p5', 'menu', 'cola', 150),
            price('p6', 'menu', 'gone', 100),
          ],
          'productBranches': [
            {'id': 'pb1', 'productId': 'off', 'branchId': lkc, 'isAvailable': false},
          ],
          ...extra,
        },
      );

  group('menu at a branch', () {
    test('branch price wins, branch-only items stay at their branch, switched-off and stock items are hidden', () async {
      await pullMenu();
      final lakeCity = await db.watchMenu(lkc, '2026-10-24').first;
      final eden = await db.watchMenu(edn, '2026-10-24').first;

      expect({for (final i in lakeCity) i.name: i.price}, {
        'Tikka Pizza': 1250,
        'Lake City Special': 900,
        'No price yet': null,
      });
      expect({for (final i in eden) i.name: i.price}, {'Tikka Pizza': 1200, 'Eden Special': null, 'Off at LKC': 500, 'No price yet': null});
    });

    test('a cleared branch price (tombstone) falls back to the base price', () async {
      await pullMenu();
      await db.applyPull(
        tokens: const {}, settings: const [], features: const [], permissions: null,
        tombstones: const [(entity: 'priceListItems', id: 'p2')],
      );

      final menu = await db.watchMenu(lkc, '2026-10-24').first;
      expect(menu.firstWhere((i) => i.productId == 'tikka').price, 1200);
    });

    test('only branches with Sales.Order.Create can take orders', () async {
      await db.applyPull(
        tokens: const {}, settings: const [], features: const [],
        permissions: const [(code: 'Sales.Order.Create', branchId: lkc), (code: 'Sales.View', branchId: edn)],
        rows: {
          'branches': [
            {'id': lkc, 'code': 'LKC', 'name': 'Lake City', 'isActive': true},
            {'id': edn, 'code': 'EDN', 'name': 'Eden', 'isActive': true},
          ],
        },
      );

      expect((await db.watchOrderBranches().first).map((b) => b.name), ['Lake City']);
    });
  });

  group('orders taken on the phone', () {
    Future<void> takeOrder(String orderId, String commandId) => db.createLocalOrder(
          orderId: orderId,
          commandId: commandId,
          branchId: lkc,
          notes: 'Table 4',
          estimatedTotal: 2500,
          takenByUserId: 'waiter',
          lines: [
            {'productId': 'tikka', 'productName': 'Tikka Pizza', 'quantity': 2, 'unitPrice': 1250, 'lineTotal': 2500, 'notes': null},
          ],
          payload: {'id': orderId, 'branchId': lkc, 'notes': 'Table 4', 'lines': [{'productId': 'tikka', 'quantity': 2}]},
          nowUtc: now,
        );

    test('the order and its CreateSalesOrder command are saved together', () async {
      await takeOrder('o1', 'c1');

      final orders = await db.watchOrders(lkc).first;
      expect(orders.single.status, LocalOrderStatus.sending);
      final command = (await db.pendingCommands()).single;
      expect(command.type, 'CreateSalesOrder');
      expect((jsonDecode(command.payloadJson) as Map)['id'], 'o1');
    });

    test("the server's number shows after push, and the pulled copy replaces the local row", () async {
      await takeOrder('o1', 'c1');
      final api = _FakeApi()
        ..pushResults['c1'] = const PushResult(
          id: 'c1', status: 'Applied', result: {'serverIds': {'salesOrder': 's1'}, 'documentNo': 'SO-000001', 'status': 'Confirmed'})
        ..pullPage = PullResponse(tokens: const {'openOrders': 'A'}, hasMore: const {'openOrders': false}, changes: {
          'openOrders': [
            {
              'id': 's1', 'documentNo': 'SO-000001', 'clientDocumentId': 'o1', 'status': 'Closed', 'channel': 'DineIn', 'branchId': lkc,
              'notes': 'Table 4', 'grandTotal': 2500, 'paidAmount': 2500, 'orderDate': '2026-10-24T12:00:00.000Z', 'takenByUserId': 'waiter',
              'lines': [{'productId': 'tikka', 'productName': 'Tikka Pizza', 'quantity': 2, 'unitPrice': 1250, 'lineTotal': 2500, 'notes': null}],
            },
          ],
        });
      final engine = SyncEngine(api: api, db: db, deviceId: () async => 'd1', appVersion: '1.0.0', clock: () => now);

      api.skipPull = true;
      await engine.run();
      var order = (await db.watchOrders(lkc).first).single;
      expect((order.documentNo, order.status, order.serverId), ('SO-000001', 'Confirmed', 's1'));

      api.skipPull = false;
      await engine.run();
      order = (await db.watchOrders(lkc).first).single;
      expect((order.key, order.status), ('o1', 'Closed'));
    });

    test('a refused order says why', () async {
      await takeOrder('o1', 'c1');
      final api = _FakeApi()
        ..pushResults['c1'] = const PushResult(
            id: 'c1', status: 'Rejected', errorCode: 'VALIDATION_FAILED', message: 'Tikka Pizza is unavailable at this branch right now.');
      await SyncEngine(api: api, db: db, deviceId: () async => 'd1', appVersion: '1.0.0', clock: () => now).run();

      final order = (await db.watchOrders(lkc).first).single;
      expect(order.status, LocalOrderStatus.rejected);
      expect(order.errorMessage, contains('unavailable'));
    });
  });

  test('a phone on the v1 database gets the menu and order tables on upgrade', () async {
    // Make a v1 database: today's schema minus the v2 tables, user_version 1.
    final v1 = AppDatabase(NativeDatabase.memory(setup: (raw) {}));
    await v1.customSelect('SELECT 1').get();
    for (final TableInfo t in [v1.localBranches, v1.localUoms, v1.localCategories, v1.localProducts, v1.localPriceLists,
        v1.localPriceListItems, v1.localProductBranches, v1.localOrders]) {
      await v1.customStatement('DROP TABLE ${t.actualTableName}');
    }
    await v1.customStatement('PRAGMA user_version = 1');
    final migrator = v1.createMigrator();
    await v1.migration.onUpgrade(migrator, 1, 2);

    await v1.createLocalOrder(
      orderId: 'o1', commandId: 'c1', branchId: lkc, notes: null, estimatedTotal: 0, takenByUserId: 'u',
      lines: const [], payload: const {'id': 'o1'}, nowUtc: now,
    );
    expect((await v1.watchOrders(lkc).first).length, 1);
    await v1.close();
  });
}

class _FakeApi implements SyncApi {
  final pushResults = <String, PushResult>{};
  PullResponse? pullPage;
  bool skipPull = false;

  @override
  Future<SyncStatus> status() async => SyncStatus(
      minVersion: null, storeUrl: null, deviceRevoked: false, wipeRequested: false, maxOfflineHours: 24, serverTimeUtc: DateTime.utc(2026));

  @override
  Future<List<PushResult>> push(String deviceId, List<PushCommand> commands) async =>
      commands.map((c) => pushResults[c.id] ?? PushResult(id: c.id, status: 'Applied')).toList();

  @override
  Future<PullResponse> pull(Map<String, String> tokens, {int limit = 1000}) async =>
      (skipPull ? null : pullPage) ?? PullResponse(tokens: tokens, hasMore: const {}, changes: const {});
}
