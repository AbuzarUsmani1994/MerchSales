import 'dart:async';

import 'package:flutter/widgets.dart';

/// When to sync (section 14): on start, when the app comes back to the foreground, 5 seconds
/// after the last new command, every 5 minutes while open, and on pull-to-refresh / "Sync
/// now". (Reacting to connectivity changes directly would need a package the spec doesn't
/// list; the resume and periodic triggers cover regained connections within minutes.)
class SyncScheduler with WidgetsBindingObserver {
  SyncScheduler(this._runSync, {this.period = const Duration(minutes: 5), this.debounce = const Duration(seconds: 5)});

  final Future<void> Function() _runSync;
  final Duration period;
  final Duration debounce;

  Timer? _periodic;
  Timer? _debounced;
  bool _started = false;

  void start() {
    if (_started) return;
    _started = true;
    WidgetsBinding.instance.addObserver(this);
    _periodic = Timer.periodic(period, (_) => _runSync());
    _runSync();
  }

  void stop() {
    if (!_started) return;
    _started = false;
    WidgetsBinding.instance.removeObserver(this);
    _periodic?.cancel();
    _debounced?.cancel();
  }

  /// A command was just queued: sync soon, but let a burst of commands go in one push.
  void commandQueued() {
    if (!_started) return;
    _debounced?.cancel();
    _debounced = Timer(debounce, _runSync);
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed) _runSync();
  }
}
