import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'app.dart';

void main() {
  // Firebase.initializeApp() is intentionally not called here yet: section 17.1 requires
  // the app to still build and run with push disabled when GOOGLE_SERVICES_JSON_BASE64
  // isn't configured. Phase 1 wraps this in a try/catch that disables FCM on failure.
  runApp(const ProviderScope(child: StockOrderApp()));
}
