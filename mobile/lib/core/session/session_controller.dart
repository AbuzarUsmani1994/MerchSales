import 'dart:async';
import 'dart:convert';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:uuid/uuid.dart';

import '../api/api_client.dart';
import '../auth/token_storage.dart';
import '../db/app_database.dart';
import '../providers.dart';
import '../sync/sync_engine.dart';
import '../sync/sync_scheduler.dart';

enum SessionStatus {
  /// Reading secure storage on app start.
  starting,
  signedOut,

  /// Signed in, waiting for fingerprint / face unlock.
  locked,

  /// Signed in with a one-time password; nothing else is reachable until it's changed.
  mustChangePassword,
  signedIn,

  /// Below the server's minimum app version: a blocking screen (section 9, 14).
  updateRequired,
}

/// Why the user landed on the sign-in screen without signing out themselves.
enum SignedOutReason { deviceRevoked, deviceWiped, sessionExpired }

class SessionState {
  const SessionState(this.status, {this.profile, this.signedOutReason, this.storeUrl});

  final SessionStatus status;
  final UserProfile? profile;
  final SignedOutReason? signedOutReason;
  final String? storeUrl;
}

class SyncUiState {
  const SyncUiState({this.running = false, this.lastResult});

  final bool running;
  final SyncRunResult? lastResult;
}

/// The single place that decides what the app shows (section 14 flows): first sign-in,
/// forced password change, biometric unlock, update gate, and revoke / remote wipe.
class SessionController extends Notifier<SessionState> {
  StreamSubscription<ApiEvent>? _events;
  SyncScheduler? _scheduler;

  @override
  SessionState build() {
    _events = ref.read(apiClientProvider).events.listen(_onApiEvent);
    ref.onDispose(() {
      _events?.cancel();
      _scheduler?.stop();
    });
    Future.microtask(_bootstrap);
    return const SessionState(SessionStatus.starting);
  }

  TokenStorage get _storage => ref.read(tokenStorageProvider);

  Future<void> _bootstrap() async {
    final profile = await _storage.readProfile();
    final hasToken = await _storage.readAccessToken() != null;
    if (!hasToken || profile == null) {
      state = const SessionState(SessionStatus.signedOut);
      return;
    }

    if (profile.mustChangePassword) {
      state = SessionState(SessionStatus.mustChangePassword, profile: profile);
    } else if (await _storage.readBiometricChoice() == true && await ref.read(biometricServiceProvider).isAvailable()) {
      state = SessionState(SessionStatus.locked, profile: profile);
    } else {
      _enterSignedIn(profile);
    }
  }

  Future<void> login({required String tenantCode, required String username, required String password, String? totpCode}) async {
    final profile = await ref.read(authRepositoryProvider).login(
          tenantCode: tenantCode,
          username: username,
          password: password,
          totpCode: totpCode,
        );

    final db = ref.read(databaseProvider);
    if (await db.readMeta(SyncMetaKeys.ownerUserId) != profile.userId) {
      await db.clearForNewUser(profile.userId);
    }

    // The section 12 sample command: tells the server which push token reaches this phone.
    // Null until Firebase is configured (push arrives with Phase 1), which is a valid payload.
    await db.enqueueCommand(
      id: const Uuid().v7(),
      type: 'UpdatePushToken',
      payloadJson: jsonEncode({'pushToken': null}),
      createdOnDeviceAtUtc: DateTime.now().toUtc(),
    );

    if (profile.mustChangePassword) {
      state = SessionState(SessionStatus.mustChangePassword, profile: profile);
    } else {
      _enterSignedIn(profile);
    }
  }

  Future<void> passwordChanged() async {
    final profile = state.profile!.copyWith(mustChangePassword: false);
    await _storage.saveProfile(profile);
    _enterSignedIn(profile);
  }

  Future<bool> unlock(String reason) async {
    final ok = await ref.read(biometricServiceProvider).authenticate(reason);
    if (ok && state.profile != null) _enterSignedIn(state.profile!);
    return ok;
  }

  Future<void> setBiometric(bool enabled) => _storage.saveBiometricChoice(enabled);

  /// Callers check for unsent commands first (sign-out dialog); [discardPending] drops them so
  /// they're never pushed under the next person's account.
  Future<void> signOut({bool discardPending = false}) async {
    _scheduler?.stop();
    _scheduler = null;
    if (discardPending) await ref.read(databaseProvider).discardPending();
    await ref.read(authRepositoryProvider).signOut();
    state = const SessionState(SessionStatus.signedOut);
  }

  void commandQueued() => _scheduler?.commandQueued();

  /// Called by the sync controller after every run.
  Future<void> onSyncResult(SyncRunResult result) async {
    switch (result) {
      case SyncDeviceRevoked(:final wipeRequested):
        await _revoked(wipeRequested);
      case SyncUpdateRequired(:final storeUrl):
        _updateRequired(storeUrl);
      case SyncSucceeded() || SyncFailed():
        break;
    }
  }

  Future<void> retryAfterUpdateGate() async {
    final profile = await _storage.readProfile();
    if (profile == null || await _storage.readAccessToken() == null) {
      state = const SessionState(SessionStatus.signedOut);
    } else {
      _enterSignedIn(profile);
    }
  }

  void _enterSignedIn(UserProfile profile) {
    state = SessionState(SessionStatus.signedIn, profile: profile);
    _scheduler ??= SyncScheduler(() => ref.read(syncControllerProvider.notifier).syncNow())..start();
  }

  void _onApiEvent(ApiEvent event) {
    switch (event) {
      case DeviceRevokedEvent(:final wipeRequested):
        unawaited(_revoked(wipeRequested));
      case AppVersionUnsupportedEvent(:final storeUrl):
        _updateRequired(storeUrl);
      case SessionExpiredEvent():
        _scheduler?.stop();
        _scheduler = null;
        state = const SessionState(SessionStatus.signedOut, signedOutReason: SignedOutReason.sessionExpired);
    }
  }

  void _updateRequired(String? storeUrl) {
    if (state.status == SessionStatus.updateRequired) return;
    _scheduler?.stop();
    _scheduler = null;
    state = SessionState(SessionStatus.updateRequired, profile: state.profile, storeUrl: storeUrl);
  }

  /// Section 10/14: sign out; on a wipe request also erase the database and its key. Either
  /// way the device id is dropped, because the server refuses a revoked id for good.
  Future<void> _revoked(bool wipeRequested) async {
    if (state.status == SessionStatus.signedOut && state.signedOutReason != null) return;
    _scheduler?.stop();
    _scheduler = null;

    await _storage.clearSession();
    await _storage.forgetDeviceId();
    if (wipeRequested) {
      await ref.read(databaseProvider.notifier).wipe();
    }

    state = SessionState(
      SessionStatus.signedOut,
      signedOutReason: wipeRequested ? SignedOutReason.deviceWiped : SignedOutReason.deviceRevoked,
    );
  }
}

/// Runs sync on demand and exposes progress to the UI; one run at a time (the engine joins
/// concurrent requests).
class SyncController extends Notifier<SyncUiState> {
  @override
  SyncUiState build() => const SyncUiState();

  Future<void> syncNow() async {
    if (state.running) return;
    state = SyncUiState(running: true, lastResult: state.lastResult);
    final result = await ref.read(syncEngineProvider).run();
    state = SyncUiState(lastResult: result);
    await ref.read(sessionControllerProvider.notifier).onSyncResult(result);
  }
}
