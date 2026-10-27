import 'dart:convert';
import 'dart:io';

import 'package:drift/drift.dart';
import 'package:drift/native.dart';
import 'package:path/path.dart' as p;
import 'package:path_provider/path_provider.dart';
import 'package:sqlite3/sqlite3.dart' show Database;

part 'app_database.g.dart';

/// One orderable item as shown on the phone's menu at a branch.
class MenuEntry {
  const MenuEntry({
    required this.productId,
    required this.name,
    required this.shortName,
    required this.description,
    required this.categoryId,
    required this.categoryName,
    required this.uomCode,
    required this.decimalPlaces,
    required this.price,
    this.imageAttachmentId,
  });

  final String productId;
  final String name;
  final String? shortName;
  final String? description;
  final String? categoryId;
  final String? categoryName;
  final String uomCode;
  final int decimalPlaces;

  /// Null when no price list has one: shown, but can't be ordered.
  final double? price;

  /// Null shows the placeholder tile.
  final String? imageAttachmentId;
}

/// Local copy of pulled entities plus the offline outbox (section 14). Every screen reads
/// from here; the sync engine is the only thing that talks to the server about this data.

/// tenancy.TenantSetting rows (pull entity "settings").
class LocalSettings extends Table {
  TextColumn get key => text()();
  TextColumn get value => text()();

  @override
  Set<Column> get primaryKey => {key};
}

/// tenancy.TenantFeature rows (pull entity "features").
class LocalFeatures extends Table {
  TextColumn get key => text()();
  BoolColumn get isEnabled => boolean()();

  @override
  Set<Column> get primaryKey => {key};
}

/// This user's permission grants (pull entity "permissions"). branchId '' = all branches.
class LocalPermissions extends Table {
  TextColumn get code => text()();
  TextColumn get branchId => text()();

  @override
  Set<Column> get primaryKey => {code, branchId};
}

/// Last token received per pull entity (section 12).
class SyncTokens extends Table {
  TextColumn get entity => text()();
  TextColumn get token => text()();

  @override
  Set<Column> get primaryKey => {entity};
}

/// Small key/value facts about this phone's sync state (last success, offline limit...).
class SyncMeta extends Table {
  TextColumn get key => text()();
  TextColumn get value => text()();

  @override
  Set<Column> get primaryKey => {key};
}

/// Branches the user works at (pull entity "branches").
@DataClassName('LocalBranch')
class LocalBranches extends Table {
  TextColumn get id => text()();
  TextColumn get code => text()();
  TextColumn get name => text()();
  BoolColumn get isActive => boolean()();

  @override
  Set<Column> get primaryKey => {id};
}

/// catalog.UnitOfMeasure (pull entity "uoms").
class LocalUoms extends Table {
  TextColumn get id => text()();
  TextColumn get code => text()();
  TextColumn get name => text()();
  IntColumn get decimalPlaces => integer()();
  BoolColumn get isActive => boolean()();

  @override
  Set<Column> get primaryKey => {id};
}

/// catalog.Category (pull entity "categories").
@DataClassName('LocalCategory')
class LocalCategories extends Table {
  TextColumn get id => text()();
  TextColumn get parentId => text().nullable()();
  TextColumn get name => text()();
  IntColumn get sortOrder => integer()();
  BoolColumn get isActive => boolean()();

  @override
  Set<Column> get primaryKey => {id};
}

/// catalog.Product (pull entity "products"): shared items plus branch-only items of the user's branches.
class LocalProducts extends Table {
  TextColumn get id => text()();
  TextColumn get sku => text()();
  TextColumn get name => text()();
  TextColumn get shortName => text().nullable()();
  TextColumn get description => text().nullable()();
  TextColumn get categoryId => text().nullable()();
  /// ProductType.Behaviour: Stocked, NonStock, Service, Kit, Recipe.
  TextColumn get behaviour => text()();
  TextColumn get baseUomId => text()();
  TextColumn get salesUomId => text().nullable()();
  TextColumn get ownerBranchId => text().nullable()();
  /// The product's picture (v3); its file is in ProductImageCache.
  TextColumn get imageAttachmentId => text().nullable()();
  BoolColumn get isActive => boolean()();

  @override
  Set<Column> get primaryKey => {id};
}

/// catalog.PriceList (pull entity "priceLists"): the default list and the user's branch lists.
class LocalPriceLists extends Table {
  TextColumn get id => text()();
  TextColumn get name => text()();
  BoolColumn get isDefault => boolean()();
  TextColumn get branchId => text().nullable()();
  BoolColumn get isActive => boolean()();

  @override
  Set<Column> get primaryKey => {id};
}

/// catalog.PriceListItem (pull entity "priceListItems"). For display only: the server prices every order.
class LocalPriceListItems extends Table {
  TextColumn get id => text()();
  TextColumn get priceListId => text()();
  TextColumn get productId => text()();
  TextColumn get uomId => text()();
  RealColumn get minQuantity => real()();
  RealColumn get unitPrice => real()();
  /// yyyy-MM-dd, so they compare as text.
  TextColumn get validFrom => text().nullable()();
  TextColumn get validTo => text().nullable()();

  @override
  Set<Column> get primaryKey => {id};
}

/// catalog.ProductBranch (pull entity "productBranches"): an item switched off at a branch.
@DataClassName('LocalProductBranch')
class LocalProductBranches extends Table {
  TextColumn get id => text()();
  TextColumn get productId => text()();
  TextColumn get branchId => text()();
  BoolColumn get isAvailable => boolean()();

  @override
  Set<Column> get primaryKey => {id};
}

/// Orders of the user's branches: pulled ones (entity "openOrders") and ones taken on this
/// phone that the server hasn't confirmed yet. Keyed by the phone's own order id when there
/// is one, so the pulled copy replaces the local one.
class LocalOrders extends Table {
  TextColumn get key => text()();
  TextColumn get serverId => text().nullable()();
  TextColumn get documentNo => text().nullable()();
  /// A server status (Confirmed, Closed, Cancelled...) or [LocalOrderStatus] while local.
  TextColumn get status => text()();
  TextColumn get branchId => text()();
  TextColumn get notes => text().nullable()();
  RealColumn get grandTotal => real()();
  DateTimeColumn get orderDateUtc => dateTime()();
  TextColumn get takenByUserId => text().nullable()();
  /// [{productId, productName, quantity, unitPrice, lineTotal, notes}]
  TextColumn get linesJson => text()();
  TextColumn get errorMessage => text().nullable()();

  @override
  Set<Column> get primaryKey => {key};
}

abstract final class LocalOrderStatus {
  /// Taken on this phone, waiting for the next sync.
  static const sending = 'Sending';

  /// The server refused it (e.g. an item became unavailable). Final.
  static const rejected = 'Rejected';
}

/// Commands created offline, pushed in clientSequence order (section 12).
class OutboxCommands extends Table {
  TextColumn get id => text()(); // client-generated command GUID, kept across retries
  TextColumn get type => text()();
  TextColumn get payloadJson => text()();
  IntColumn get clientSequence => integer()();
  TextColumn get status => text().withDefault(const Constant(OutboxStatus.pending))();
  TextColumn get errorCode => text().nullable()();
  TextColumn get lastError => text().nullable()();
  IntColumn get attempts => integer().withDefault(const Constant(0))();
  DateTimeColumn get createdOnDeviceAtUtc => dateTime()();
  TextColumn get resultJson => text().nullable()();

  @override
  Set<Column> get primaryKey => {id};
}

abstract final class OutboxStatus {
  /// Not yet accepted by the server (never sent, or the server failed and it will retry).
  static const pending = 'Pending';
  static const applied = 'Applied';

  /// Final: the server refused it. Shown on the sync status screen until dismissed.
  static const rejected = 'Rejected';
}

abstract final class SyncMetaKeys {
  static const lastSuccessfulSyncAtUtc = 'lastSuccessfulSyncAtUtc';
  static const lastSyncError = 'lastSyncError';
  static const maxOfflineHours = 'maxOfflineHours';
  static const nextClientSequence = 'nextClientSequence';
  static const ownerUserId = 'ownerUserId';
  static const orderBranchId = 'orderBranchId';
  static const industry = 'industry';
}

@DriftDatabase(tables: [
  LocalSettings,
  LocalFeatures,
  LocalPermissions,
  SyncTokens,
  SyncMeta,
  OutboxCommands,
  LocalBranches,
  LocalUoms,
  LocalCategories,
  LocalProducts,
  LocalPriceLists,
  LocalPriceListItems,
  LocalProductBranches,
  LocalOrders,
])
class AppDatabase extends _$AppDatabase {
  AppDatabase(super.executor);

  /// The phone's database file, encrypted with SQLCipher using [hexKey] (32 random bytes kept
  /// in secure storage). Throws if the bundled SQLite isn't SQLCipher, rather than silently
  /// writing business data unencrypted.
  static Future<AppDatabase> openEncrypted(String hexKey) async {
    return AppDatabase(NativeDatabase.createInBackground(await databaseFile(), setup: (raw) => applyKey(raw, hexKey)));
  }

  /// Shared with tests, which open a temp file the same way.
  static void applyKey(Database raw, String hexKey) {
    raw.execute("PRAGMA key = \"x'$hexKey'\";");
    if (raw.select('PRAGMA cipher_version;').isEmpty) {
      throw StateError('SQLCipher is not available; refusing to open an unencrypted database.');
    }
  }

  static Future<File> databaseFile() async {
    final dir = await getApplicationDocumentsDirectory();
    return File(p.join(dir.path, 'stockorder.sqlite'));
  }

  /// Remote wipe (section 10, 14): the file and its journal files go; the caller deletes the key.
  static Future<void> deleteDatabaseFiles() async {
    final file = await databaseFile();
    for (final path in [file.path, '${file.path}-wal', '${file.path}-shm', '${file.path}-journal']) {
      final f = File(path);
      if (await f.exists()) await f.delete();
    }
  }

  @override
  int get schemaVersion => 3;

  @override
  MigrationStrategy get migration => MigrationStrategy(
        onUpgrade: (m, from, to) async {
          if (from < 2) {
            // v2: the menu and orders. Their pull tokens start empty, so the next sync sends everything.
            for (final TableInfo table in [localBranches, localUoms, localCategories, localProducts, localPriceLists,
                localPriceListItems, localProductBranches, localOrders]) {
              await m.createTable(table);
            }
          } else if (from < 3) {
            // v3: product pictures. Forget the products token so the next sync resends every product with its picture.
            await m.addColumn(localProducts, localProducts.imageAttachmentId);
            await customStatement("DELETE FROM sync_tokens WHERE entity = 'products'");
          }
        },
      );

  // ---------- Outbox ----------

  /// Saves a new command with the next clientSequence in one local transaction, so the order
  /// commands were created in is the order the server applies them (section 12).
  Future<void> enqueueCommand({
    required String id,
    required String type,
    required String payloadJson,
    required DateTime createdOnDeviceAtUtc,
  }) {
    return transaction(() async {
      final sequence = await _takeNextClientSequence();
      await into(outboxCommands).insert(OutboxCommandsCompanion.insert(
        id: id,
        type: type,
        payloadJson: payloadJson,
        clientSequence: sequence,
        createdOnDeviceAtUtc: createdOnDeviceAtUtc,
      ));
    });
  }

  Future<int> _takeNextClientSequence() async {
    final current = int.tryParse(await readMeta(SyncMetaKeys.nextClientSequence) ?? '') ?? 1;
    await writeMeta(SyncMetaKeys.nextClientSequence, '${current + 1}');
    return current;
  }

  Future<List<OutboxCommand>> pendingCommands() => (select(outboxCommands)
        ..where((c) => c.status.equals(OutboxStatus.pending))
        ..orderBy([(c) => OrderingTerm.asc(c.clientSequence)]))
      .get();

  Stream<List<OutboxCommand>> watchCommands(String status) => (select(outboxCommands)
        ..where((c) => c.status.equals(status))
        ..orderBy([(c) => OrderingTerm.asc(c.clientSequence)]))
      .watch();

  Stream<int> watchPendingCount() {
    final count = outboxCommands.id.count();
    final query = selectOnly(outboxCommands)
      ..addColumns([count])
      ..where(outboxCommands.status.equals(OutboxStatus.pending));
    return query.map((row) => row.read(count) ?? 0).watchSingle();
  }

  Future<void> markApplied(String id, String? resultJson) => (update(outboxCommands)..where((c) => c.id.equals(id))).write(
        OutboxCommandsCompanion(
          status: const Value(OutboxStatus.applied),
          resultJson: Value(resultJson),
          errorCode: const Value(null),
          lastError: const Value(null),
        ),
      );

  Future<void> markRejected(String id, String? errorCode, String? message) =>
      (update(outboxCommands)..where((c) => c.id.equals(id))).write(
        OutboxCommandsCompanion(
          status: const Value(OutboxStatus.rejected),
          errorCode: Value(errorCode),
          lastError: Value(message),
        ),
      );

  /// The server couldn't apply it this time; it stays pending and is retried with the same id.
  Future<void> markAttemptFailed(String id, String? errorCode, String? message) => customUpdate(
        'UPDATE outbox_commands SET attempts = attempts + 1, error_code = ?, last_error = ? WHERE id = ?',
        variables: [Variable(errorCode), Variable(message), Variable(id)],
        updates: {outboxCommands},
      );

  Future<void> dismissRejected(String id) =>
      (delete(outboxCommands)..where((c) => c.id.equals(id) & c.status.equals(OutboxStatus.rejected))).go();

  Future<void> discardPending() => (delete(outboxCommands)..where((c) => c.status.equals(OutboxStatus.pending))).go();

  // ---------- Pulled data ----------

  Future<Map<String, String>> readTokens() async {
    final rows = await select(syncTokens).get();
    return {for (final row in rows) row.entity: row.token};
  }

  /// Applies one pull page atomically: upserts, the permission set (replaced whenever its
  /// token moved, even to an empty set), and the new tokens.
  Future<void> applyPull({
    required Map<String, String> tokens,
    required List<({String key, String value})> settings,
    required List<({String key, bool isEnabled})> features,
    required List<({String code, String? branchId})>? permissions,
    Map<String, List<Map<String, dynamic>>> rows = const {},
    List<({String entity, String id})> tombstones = const [],
  }) {
    return transaction(() async {
      await _applyRows(rows);
      for (final t in tombstones) {
        await _deleteTombstoned(t.entity, t.id);
      }

      await batch((b) {
        b.insertAllOnConflictUpdate(
          localSettings,
          settings.map((s) => LocalSettingsCompanion.insert(key: s.key, value: s.value)),
        );
        b.insertAllOnConflictUpdate(
          localFeatures,
          features.map((f) => LocalFeaturesCompanion.insert(key: f.key, isEnabled: f.isEnabled)),
        );
      });

      if (permissions != null) {
        await delete(localPermissions).go();
        await batch((b) => b.insertAll(
              localPermissions,
              permissions.map((g) => LocalPermissionsCompanion.insert(code: g.code, branchId: g.branchId ?? '')),
              mode: InsertMode.insertOrIgnore,
            ));
      }

      await batch((b) => b.insertAllOnConflictUpdate(
            syncTokens,
            tokens.entries.map((e) => SyncTokensCompanion.insert(entity: e.key, token: e.value)),
          ));
    });
  }

  Future<void> _applyRows(Map<String, List<Map<String, dynamic>>> rows) async {
    String? str(Object? v) => v as String?;
    bool flag(Object? v) => v as bool? ?? false;
    double num0(Object? v) => (v as num?)?.toDouble() ?? 0;

    await batch((b) {
      b.insertAllOnConflictUpdate(localBranches, (rows['branches'] ?? const []).map((r) => LocalBranchesCompanion.insert(
            id: r['id'] as String, code: r['code'] as String, name: r['name'] as String, isActive: flag(r['isActive']))));
      b.insertAllOnConflictUpdate(localUoms, (rows['uoms'] ?? const []).map((r) => LocalUomsCompanion.insert(
            id: r['id'] as String,
            code: r['code'] as String,
            name: r['name'] as String,
            decimalPlaces: (r['decimalPlaces'] as num).toInt(),
            isActive: flag(r['isActive']))));
      b.insertAllOnConflictUpdate(localCategories, (rows['categories'] ?? const []).map((r) => LocalCategoriesCompanion.insert(
            id: r['id'] as String,
            parentId: Value(str(r['parentId'])),
            name: r['name'] as String,
            sortOrder: (r['sortOrder'] as num).toInt(),
            isActive: flag(r['isActive']))));
      b.insertAllOnConflictUpdate(localProducts, (rows['products'] ?? const []).map((r) => LocalProductsCompanion.insert(
            id: r['id'] as String,
            sku: r['sku'] as String,
            name: r['name'] as String,
            shortName: Value(str(r['shortName'])),
            description: Value(str(r['description'])),
            categoryId: Value(str(r['categoryId'])),
            behaviour: r['behaviour'] as String,
            baseUomId: r['baseUomId'] as String,
            salesUomId: Value(str(r['salesUomId'])),
            ownerBranchId: Value(str(r['ownerBranchId'])),
            imageAttachmentId: Value(str(r['imageAttachmentId'])),
            isActive: flag(r['isActive']))));
      b.insertAllOnConflictUpdate(localPriceLists, (rows['priceLists'] ?? const []).map((r) => LocalPriceListsCompanion.insert(
            id: r['id'] as String,
            name: r['name'] as String,
            isDefault: flag(r['isDefault']),
            branchId: Value(str(r['branchId'])),
            isActive: flag(r['isActive']))));
      b.insertAllOnConflictUpdate(localPriceListItems, (rows['priceListItems'] ?? const []).map((r) => LocalPriceListItemsCompanion.insert(
            id: r['id'] as String,
            priceListId: r['priceListId'] as String,
            productId: r['productId'] as String,
            uomId: r['uomId'] as String,
            minQuantity: num0(r['minQuantity']),
            unitPrice: num0(r['unitPrice']),
            validFrom: Value(str(r['validFrom'])),
            validTo: Value(str(r['validTo'])))));
      b.insertAllOnConflictUpdate(localProductBranches, (rows['productBranches'] ?? const []).map((r) => LocalProductBranchesCompanion.insert(
            id: r['id'] as String, productId: r['productId'] as String, branchId: r['branchId'] as String, isAvailable: flag(r['isAvailable']))));
    });

    for (final r in rows['openOrders'] ?? const <Map<String, dynamic>>[]) {
      // The server's copy wins over the phone's own "Sending" row (same key: the phone's order id).
      await into(localOrders).insertOnConflictUpdate(LocalOrdersCompanion.insert(
        key: str(r['clientDocumentId']) ?? r['id'] as String,
        serverId: Value(r['id'] as String),
        documentNo: Value(str(r['documentNo'])),
        status: r['status'] as String,
        branchId: r['branchId'] as String,
        notes: Value(str(r['notes'])),
        grandTotal: num0(r['grandTotal']),
        orderDateUtc: DateTime.parse(r['orderDate'] as String).toUtc(),
        takenByUserId: Value(str(r['takenByUserId'])),
        linesJson: jsonEncode(r['lines'] ?? const []),
        errorMessage: const Value(null),
      ));
    }
  }

  Future<void> _deleteTombstoned(String entity, String id) async {
    final TableInfo? table = switch (entity) {
      'branches' => localBranches,
      'uoms' => localUoms,
      'categories' => localCategories,
      'products' => localProducts,
      'priceLists' => localPriceLists,
      'priceListItems' => localPriceListItems,
      'productBranches' => localProductBranches,
      _ => null,
    };
    if (table != null) {
      await customStatement('DELETE FROM ${table.actualTableName} WHERE id = ?', [id]);
    }
  }

  // ---------- Menu and orders ----------

  /// Branches the user can take orders at (a Sales.Order.Create grant there, or for all branches).
  Stream<List<LocalBranch>> watchOrderBranches() => customSelect(
        """
        SELECT b.* FROM local_branches b
        WHERE b.is_active = 1 AND EXISTS (
          SELECT 1 FROM local_permissions p WHERE p.code = 'Sales.Order.Create' AND (p.branch_id = '' OR p.branch_id = b.id))
        ORDER BY b.name
        """,
        readsFrom: {localBranches, localPermissions},
      ).map((row) => localBranches.map(row.data)).watch();

  /// The menu as sold at [branchId] today: branch price over base price, items switched off
  /// there hidden, stock-tracked items left out (they can't be ordered until stock receiving exists).
  Stream<List<MenuEntry>> watchMenu(String branchId, String today) => customSelect(
        """
        SELECT p.id, p.name, p.short_name, p.description, p.category_id, c.name AS category_name, p.image_attachment_id,
               COALESCE(u.code, '') AS uom_code, COALESCE(u.decimal_places, 0) AS decimal_places,
               (SELECT i.unit_price FROM local_price_list_items i
                  JOIN local_price_lists l ON l.id = i.price_list_id
                 WHERE i.product_id = p.id AND i.uom_id = COALESCE(p.sales_uom_id, p.base_uom_id) AND i.min_quantity <= 1
                   AND (i.valid_from IS NULL OR i.valid_from <= ?2) AND (i.valid_to IS NULL OR i.valid_to >= ?2)
                   AND l.is_active = 1 AND (l.branch_id = ?1 OR l.is_default = 1)
                 ORDER BY CASE WHEN l.branch_id = ?1 THEN 0 ELSE 1 END, i.min_quantity DESC
                 LIMIT 1) AS price
        FROM local_products p
        LEFT JOIN local_categories c ON c.id = p.category_id
        LEFT JOIN local_uoms u ON u.id = COALESCE(p.sales_uom_id, p.base_uom_id)
        WHERE p.is_active = 1
          AND (p.owner_branch_id IS NULL OR p.owner_branch_id = ?1)
          AND p.behaviour NOT IN ('Stocked', 'Kit')
          AND (c.id IS NULL OR c.is_active = 1)
          AND NOT EXISTS (SELECT 1 FROM local_product_branches pb
                          WHERE pb.product_id = p.id AND pb.branch_id = ?1 AND pb.is_available = 0)
        ORDER BY CASE WHEN c.id IS NULL THEN 1 ELSE 0 END, c.sort_order, c.name, p.name
        """,
        variables: [Variable(branchId), Variable(today)],
        readsFrom: {localProducts, localCategories, localUoms, localPriceLists, localPriceListItems, localProductBranches},
      )
          .map((row) => MenuEntry(
                productId: row.read<String>('id'),
                name: row.read<String>('name'),
                shortName: row.readNullable<String>('short_name'),
                description: row.readNullable<String>('description'),
                categoryId: row.readNullable<String>('category_id'),
                categoryName: row.readNullable<String>('category_name'),
                uomCode: row.read<String>('uom_code'),
                decimalPlaces: row.read<int>('decimal_places'),
                price: row.readNullable<double>('price'),
                imageAttachmentId: row.readNullable<String>('image_attachment_id'),
              ))
          .watch();

  /// Pictures the menu uses, for ProductImageCache to keep on the phone.
  Future<List<({String productId, String attachmentId})>> productImages() async {
    final rows = await (select(localProducts)..where((p) => p.isActive.equals(true) & p.imageAttachmentId.isNotNull())).get();
    return [for (final r in rows) (productId: r.id, attachmentId: r.imageAttachmentId!)];
  }

  Stream<List<LocalOrder>> watchOrders(String branchId) => (select(localOrders)
        ..where((o) => o.branchId.equals(branchId))
        ..orderBy([(o) => OrderingTerm.desc(o.orderDateUtc)]))
      .watch();

  /// Section 14: the local order and its outbox command are written in one local transaction.
  Future<void> createLocalOrder({
    required String orderId,
    required String commandId,
    required String branchId,
    required String? notes,
    required double estimatedTotal,
    required String takenByUserId,
    required List<Map<String, dynamic>> lines,
    required Map<String, dynamic> payload,
    required DateTime nowUtc,
  }) =>
      transaction(() async {
        await into(localOrders).insert(LocalOrdersCompanion.insert(
          key: orderId,
          status: LocalOrderStatus.sending,
          branchId: branchId,
          notes: Value(notes),
          grandTotal: estimatedTotal,
          orderDateUtc: nowUtc,
          takenByUserId: Value(takenByUserId),
          linesJson: jsonEncode(lines),
        ));
        await enqueueCommand(id: commandId, type: 'CreateSalesOrder', payloadJson: jsonEncode(payload), createdOnDeviceAtUtc: nowUtc);
      });

  /// After a push: the server's number and status for an order taken here, or why it refused it.
  Future<void> recordOrderOutcome(String orderId, {String? serverId, String? documentNo, String? status, String? error}) =>
      (update(localOrders)..where((o) => o.key.equals(orderId) & o.serverId.isNull())).write(LocalOrdersCompanion(
        serverId: serverId == null ? const Value.absent() : Value(serverId),
        documentNo: documentNo == null ? const Value.absent() : Value(documentNo),
        status: Value(status ?? LocalOrderStatus.rejected),
        errorMessage: Value(error),
      ));

  Future<String?> readSetting(String key) async =>
      (await (select(localSettings)..where((s) => s.key.equals(key))).getSingleOrNull())?.value;

  Stream<String?> watchSetting(String key) =>
      (select(localSettings)..where((s) => s.key.equals(key))).watchSingleOrNull().map((row) => row?.value);

  Stream<int> watchPermissionCount() {
    final count = localPermissions.code.count(distinct: true);
    return (selectOnly(localPermissions)..addColumns([count])).map((row) => row.read(count) ?? 0).watchSingle();
  }

  Stream<int> watchEnabledFeatureCount() {
    final count = localFeatures.key.count();
    final query = selectOnly(localFeatures)
      ..addColumns([count])
      ..where(localFeatures.isEnabled.equals(true));
    return query.map((row) => row.read(count) ?? 0).watchSingle();
  }

  /// A different user signed in on this phone: their data must not mix with the last one's.
  /// Pending commands are discarded by the sign-out flow before this point, never pushed
  /// under someone else's account.
  Future<void> clearForNewUser(String userId) => transaction(() async {
        for (final TableInfo table in [
          localSettings, localFeatures, localPermissions, syncTokens, outboxCommands, localBranches, localUoms,
          localCategories, localProducts, localPriceLists, localPriceListItems, localProductBranches, localOrders,
        ]) {
          await delete(table).go();
        }
        await (delete(syncMeta)..where((m) => m.key.isNotValue(SyncMetaKeys.nextClientSequence))).go();
        await writeMeta(SyncMetaKeys.ownerUserId, userId);
      });

  // ---------- Meta ----------

  Future<String?> readMeta(String key) async =>
      (await (select(syncMeta)..where((m) => m.key.equals(key))).getSingleOrNull())?.value;

  Stream<String?> watchMeta(String key) =>
      (select(syncMeta)..where((m) => m.key.equals(key))).watchSingleOrNull().map((row) => row?.value);

  Future<void> writeMeta(String key, String? value) async {
    if (value == null) {
      await (delete(syncMeta)..where((m) => m.key.equals(key))).go();
    } else {
      await into(syncMeta).insertOnConflictUpdate(SyncMetaCompanion.insert(key: key, value: value));
    }
  }
}
