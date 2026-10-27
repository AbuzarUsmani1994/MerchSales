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
import 'features/menu_qr/menu_qr_screen.dart';
import 'features/ordering/cart_screen.dart';
import 'features/ordering/menu_screen.dart';
import 'features/ordering/orders_screen.dart';
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

/// Every screen sits on the industry background (screens' own Scaffolds are transparent).
Page<void> _page(GoRouterState state, Widget screen) => MaterialPage<void>(key: state.pageKey, child: AppBackground(child: screen));

final routerProvider = Provider<GoRouter>((ref) {
  final refresh = ValueNotifier<SessionStatus>(ref.read(sessionControllerProvider).status);
  ref.listen(sessionControllerProvider, (_, next) => refresh.value = next.status);
  ref.onDispose(refresh.dispose);

  return GoRouter(
    initialLocation: '/starting',
    refreshListenable: refresh,
    redirect: (context, state) => _redirectFor(ref.read(sessionControllerProvider).status, state.matchedLocation),
    routes: [
      GoRoute(path: '/starting', pageBuilder: (context, state) => _page(state, const _StartingScreen())),
      GoRoute(path: '/login', pageBuilder: (context, state) => _page(state, const LoginScreen())),
      GoRoute(path: '/unlock', pageBuilder: (context, state) => _page(state, const UnlockScreen())),
      GoRoute(path: '/change-password', pageBuilder: (context, state) => _page(state, const ChangePasswordScreen())),
      GoRoute(path: '/update', pageBuilder: (context, state) => _page(state, const VersionGateScreen())),
      // Work screens sit under Home, so going straight to one (e.g. Orders after sending an
      // order) still leaves Home underneath: a back arrow, and Android back returns home.
      GoRoute(
        path: '/',
        pageBuilder: (context, state) => _page(state, const HomeScreen()),
        routes: [
          GoRoute(path: 'sync', pageBuilder: (context, state) => _page(state, const SyncStatusScreen())),
          GoRoute(
            path: 'order',
            pageBuilder: (context, state) => _page(state, const MenuScreen()),
            routes: [GoRoute(path: 'cart', pageBuilder: (context, state) => _page(state, const CartScreen()))],
          ),
          GoRoute(path: 'orders', pageBuilder: (context, state) => _page(state, const OrdersScreen())),
          GoRoute(path: 'menu-qr', pageBuilder: (context, state) => _page(state, const MenuQrScreen())),
        ],
      ),
    ],
  );
});

class StockOrderApp extends ConsumerWidget {
  const StockOrderApp({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    return MaterialApp.router(
      onGenerateTitle: (context) => AppLocalizations.of(context).appTitle,
      theme: buildAppTheme(ref.watch(industryProvider)),
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
