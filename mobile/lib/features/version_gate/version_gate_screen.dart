import 'package:flutter/material.dart';

/// Section 9, 14: shown when the server returns APP_VERSION_UNSUPPORTED (426) — a blocking
/// screen with a store link, no way to dismiss it back into the app.
class VersionGateScreen extends StatelessWidget {
  const VersionGateScreen({super.key, this.storeUrl});

  final String? storeUrl;

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      body: Center(
        child: Padding(
          padding: const EdgeInsets.all(24),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              const Icon(Icons.system_update, size: 48),
              const SizedBox(height: 16),
              const Text(
                'This app version is no longer supported.\nPlease update to continue.',
                textAlign: TextAlign.center,
              ),
              const SizedBox(height: 24),
              if (storeUrl != null)
                FilledButton(
                  onPressed: () {
                    // Phase 1: launch storeUrl via url_launcher once that dependency is added.
                  },
                  child: const Text('Update now'),
                ),
            ],
          ),
        ),
      ),
    );
  }
}
