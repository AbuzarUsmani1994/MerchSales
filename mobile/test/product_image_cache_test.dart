import 'dart:io';
import 'dart:typed_data';

import 'package:dio/dio.dart';
import 'package:drift/native.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:stockorder_mobile/core/db/app_database.dart';
import 'package:stockorder_mobile/core/images/product_image_cache.dart';

/// Menu pictures kept on the phone: fetched once, shown offline, cleaned up when unused.
void main() {
  late Directory root;
  late _FakeServer server;
  late ProductImageCache cache;

  setUp(() async {
    root = await Directory.systemTemp.createTemp('images_test');
    server = _FakeServer();
    final dio = Dio(BaseOptions(baseUrl: 'http://server'))..httpClientAdapter = server;
    cache = ProductImageCache(dio, rootDirectory: () async => root);
  });

  tearDown(() => root.delete(recursive: true));

  test('downloads a picture once, then serves it from the phone (offline too)', () async {
    final first = await cache.get('p1', 'img-1');
    expect(await first!.readAsBytes(), server.bytes);
    expect(server.requests, ['/api/v1/catalog/products/p1/image?v=img-1']);

    server.offline = true;
    final again = await cache.get('p1', 'img-1');
    expect(again!.path, first.path);
    expect(server.requests, hasLength(1));
  });

  test('no connection and nothing stored: null, so the placeholder shows', () async {
    server.offline = true;
    expect(await cache.get('p1', 'img-1'), isNull);
  });

  test('after a sync, unused pictures are deleted and missing ones fetched', () async {
    await cache.get('p1', 'old');
    await cache.syncWith([(productId: 'p1', attachmentId: 'new'), (productId: 'p2', attachmentId: 'other')]);

    final names = root.listSync(recursive: true).whereType<File>().map((f) => f.uri.pathSegments.last).toSet();
    expect(names, {'new.img', 'other.img'});
  });

  test('clear removes everything (new user on the phone, remote wipe)', () async {
    await cache.get('p1', 'img-1');
    await cache.clear();
    expect(root.listSync(recursive: true).whereType<File>(), isEmpty);
  });

  test('a v2 phone database gains the picture column and re-pulls products', () async {
    final db = AppDatabase(NativeDatabase.memory());
    await db.customSelect('SELECT 1').get();
    await db.customStatement('ALTER TABLE local_products DROP COLUMN image_attachment_id');
    await db.customStatement("INSERT INTO sync_tokens (entity, token) VALUES ('products', '00000000000000FF'), ('uoms', '0000000000000010')");

    await db.migration.onUpgrade(db.createMigrator(), 2, 3);

    final columns = await db.customSelect('PRAGMA table_info(local_products)').get();
    expect(columns.map((c) => c.read<String>('name')), contains('image_attachment_id'));
    expect(await db.readTokens(), {'uoms': '0000000000000010'}, reason: 'only the products token is forgotten');
    await db.close();
  });
}

class _FakeServer implements HttpClientAdapter {
  final bytes = Uint8List.fromList([0xFF, 0xD8, 0xFF, 1, 2, 3]);
  final requests = <String>[];
  bool offline = false;

  @override
  Future<ResponseBody> fetch(RequestOptions options, Stream<Uint8List>? requestStream, Future<void>? cancelFuture) async {
    if (offline) {
      throw DioException.connectionError(requestOptions: options, reason: 'offline');
    }
    requests.add('${options.uri.path}?${options.uri.query}');
    return ResponseBody.fromBytes(bytes, 200, headers: {
      Headers.contentTypeHeader: ['image/jpeg'],
    });
  }

  @override
  void close({bool force = false}) {}
}
