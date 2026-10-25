import 'dart:io';

import 'package:dio/dio.dart';
import 'package:path/path.dart' as p;
import 'package:path_provider/path_provider.dart';

/// Menu pictures on the phone's own storage, so the menu shows them offline too. Each file is
/// named by the picture's id, which changes whenever a picture is replaced, so a stored file is
/// never stale. Downloaded with the signed-in user's token (the API serves pictures only to
/// users who can see the item); files live in the app's private folder and go with a sign-out
/// to another user or a remote wipe.
class ProductImageCache {
  ProductImageCache(this._dio, {Future<Directory> Function()? rootDirectory})
      : _rootDirectory = rootDirectory ?? getApplicationSupportDirectory;

  final Dio _dio;
  final Future<Directory> Function() _rootDirectory;
  final _inFlight = <String, Future<File?>>{};

  Future<Directory> _folder() async => Directory(p.join((await _rootDirectory()).path, 'product_images'));

  Future<File> _fileFor(String attachmentId) async => File(p.join((await _folder()).path, '$attachmentId.img'));

  /// The stored picture, downloading it first if needed. Null when offline or it can't be fetched;
  /// callers then show the placeholder.
  Future<File?> get(String productId, String attachmentId) =>
      // A block body on purpose: `() => _inFlight.remove(id)` would return that same Future,
      // and whenComplete waits for whatever its callback returns: the download would wait on itself.
      _inFlight[attachmentId] ??= _get(productId, attachmentId).whenComplete(() {
        _inFlight.remove(attachmentId);
      });

  Future<File?> _get(String productId, String attachmentId) async {
    final file = await _fileFor(attachmentId);
    if (await file.exists()) return file;

    try {
      final response = await _dio.get<List<int>>(
        '/api/v1/catalog/products/$productId/image',
        queryParameters: {'v': attachmentId},
        options: Options(responseType: ResponseType.bytes),
      );
      final bytes = response.data;
      if (bytes == null || bytes.isEmpty) return null;

      await file.parent.create(recursive: true);
      // Write then rename, so a half-written file is never mistaken for a picture.
      final partial = File('${file.path}.part');
      await partial.writeAsBytes(bytes, flush: true);
      return await partial.rename(file.path);
    } on DioException {
      return null;
    } on FileSystemException {
      return null;
    }
  }

  /// After a sync: fetch pictures the menu needs, one at a time, and delete ones no longer used.
  Future<void> syncWith(List<({String productId, String attachmentId})> wanted) async {
    final keep = {for (final w in wanted) '${w.attachmentId}.img'};
    final folder = await _folder();
    if (await folder.exists()) {
      await for (final entry in folder.list()) {
        if (entry is File && !keep.contains(p.basename(entry.path))) {
          await entry.delete();
        }
      }
    }

    for (final w in wanted) {
      await get(w.productId, w.attachmentId);
    }
  }

  /// A different user signs in, or the phone is wiped.
  Future<void> clear() async {
    final folder = await _folder();
    if (await folder.exists()) await folder.delete(recursive: true);
  }
}
