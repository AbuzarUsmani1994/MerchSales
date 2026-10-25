import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/providers.dart';

final _pictureProvider = FutureProvider.family<File?, ({String productId, String attachmentId})>(
  (ref, key) => ref.watch(productImageCacheProvider).get(key.productId, key.attachmentId),
);

/// A menu item's picture, or a coloured tile with its initial when it has none (or it isn't
/// on the phone yet and there's no connection). Same colours as the back office.
class ProductImage extends ConsumerWidget {
  const ProductImage({
    super.key,
    required this.productId,
    required this.attachmentId,
    required this.name,
    this.size = 56,
    this.square = true,
  });

  final String productId;
  final String? attachmentId;
  final String name;
  final double size;

  /// False fills the space it's given (a grid card's picture area) instead of a size x size square.
  final bool square;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final id = attachmentId;
    final file = id == null ? null : ref.watch(_pictureProvider((productId: productId, attachmentId: id))).value;
    final placeholder = _Placeholder(name: name, size: size);

    final picture = file == null
        ? placeholder
        : Image.file(
            file,
            fit: BoxFit.cover,
            cacheWidth: (size * MediaQuery.devicePixelRatioOf(context)).round(),
            errorBuilder: (context, error, stack) => placeholder,
          );

    return square
        ? ClipRRect(
            borderRadius: BorderRadius.circular(size * 0.18),
            child: SizedBox.square(dimension: size, child: picture),
          )
        : SizedBox.expand(child: picture);
  }
}

class _Placeholder extends StatelessWidget {
  const _Placeholder({required this.name, required this.size});

  final String name;
  final double size;

  /// A stable colour per name (the same hash as the back office's placeholder).
  Color get _tint {
    var hash = 0;
    for (final unit in name.codeUnits) {
      hash = (hash * 31 + unit).toSigned(32);
    }
    return HSLColor.fromAHSL(1, (hash.abs() % 360).toDouble(), 0.55, 0.52).toColor();
  }

  @override
  Widget build(BuildContext context) {
    final trimmed = name.trim();
    return ColoredBox(
      color: _tint,
      child: Center(
        child: Text(
          trimmed.isEmpty ? '?' : trimmed.characters.first.toUpperCase(),
          style: TextStyle(color: Colors.white, fontWeight: FontWeight.w700, fontSize: size * 0.42),
        ),
      ),
    );
  }
}
