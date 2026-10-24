import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import 'core/providers.dart';
import 'core/session/session_controller.dart';
import 'features/auth/change_password_screen.dart';
import 'features/auth/login_screen.dart';
import 'features/auth/unlock_screen.dart';
import 'features/home/home_screen.dart';
import 'features/sync/sync_status_screen.dart';
import 'features/version_gate/version_gate_screen.dart';
import 'l10n/generated/app_localizations.dart';
import 'shared/theme.dart';

/// Where each session state is allowed to be; everything else redirects there.
String? _redirectFor(SessionStatus status, String location) {
  final target = switch (status) {
    SessionStatus.starting => '/starting',
    SessionStatus.signedOut => '/login',
    SessionStatus.locked => '/unlock',
    SessionStatus.mustChangePassword => '/change-password',
    SessionStatus.updateRequired => '/update',
    SessionStatus.signedIn => null,
  };

  if (target != null) return location == target ? null : target;

  // Signed in: the app's own screens, never back to the gate screens.
  const gates = {'/starting', '/login', '/unlock', '/change-password', '/update'};
  return gates.contains(location) ? '/' : null;
}

final routerProvider = Provider<GoRouter>((ref) {
  final refresh = ValueNotifier<SessionStatus>(ref.read(sessionControllerProvider).status);
  ref.listen(sessionControllerProvider, (_, next) => refresh.value = next.status);
  ref.onDispose(refresh.dispose);

  return GoRouter(
    initialLocation: '/starting',
    refreshListenable: refresh,
    redirect: (context, state) => _redirectFor(ref.read(sessionControllerProvider).status, state.matchedLocation),
    routes: [
      GoRoute(path: '/starting', builder: (context, state) => const _StartingScreen()),
      GoRoute(path: '/login', builder: (context, state) => const LoginScreen()),
      GoRoute(path: '/unlock', builder: (context, state) => const UnlockScreen()),
      GoRoute(path: '/change-password', builder: (context, state) => const ChangePasswordScreen()),
      GoRoute(path: '/update', builder: (context, state) => const VersionGateScreen()),
      GoRoute(path: '/', builder: (context, state) => const HomeScreen()),
      GoRoute(path: '/sync', builder: (context, state) => const SyncStatusScreen()),
    ],
  );
});

class StockOrderApp extends ConsumerWidget {
  const StockOrderApp({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    return MaterialApp.router(
      onGenerateTitle: (context) => AppLocalizations.of(context).appTitle,
      theme: buildAppTheme(),
      localizationsDelegates: const [
        AppLocalizations.delegate,
        GlobalMaterialLocalizations.delegate,
        GlobalWidgetsLocalizations.delegate,
        GlobalCupertinoLocalizations.delegate,
      ],
      supportedLocales: AppLocalizations.supportedLocales,
      routerConfig: ref.watch(routerProvider),
    );
  }
}

class _StartingScreen extends StatelessWidget {
  const _StartingScreen();

  @override
  Widget build(BuildContext context) => const Scaffold(body: Center(child: BrandMark(size: 56)));
}
