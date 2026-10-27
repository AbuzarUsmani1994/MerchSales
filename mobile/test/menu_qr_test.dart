import 'package:drift/native.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:qr_flutter/qr_flutter.dart';
import 'package:stockorder_mobile/core/auth/biometric_service.dart';
import 'package:stockorder_mobile/core/auth/token_storage.dart';
import 'package:stockorder_mobile/core/db/app_database.dart';
import 'package:stockorder_mobile/core/providers.dart';
import 'package:stockorder_mobile/core/session/session_controller.dart';
import 'package:stockorder_mobile/features/home/home_screen.dart';
import 'package:stockorder_mobile/features/menu_qr/menu_qr_screen.dart';
import 'package:stockorder_mobile/l10n/generated/app_localizations.dart';
import 'package:stockorder_mobile/shared/theme.dart';

/// The customer menu QR a waiter shows guests (restaurants only).
void main() {
  late AppDatabase db;
  const lkc = 'branch-lkc';
  const code = 'abcdefghjkmnpq23';

  setUp(() async {
    FlutterSecureStorage.setMockInitialValues({});
    db = AppDatabase(NativeDatabase.memory());
    await db.applyPull(
      tokens: const {},
      settings: const [],
      features: const [],
      permissions: const [(code: 'Sales.Order.Create', branchId: lkc)],
      rows: {
        'branches': [
          {'id': lkc, 'code': 'LKC', 'name': 'Lake City', 'isActive': true},
        ],
      },
    );
  });

  tearDown(() => db.close());

  Widget app(Widget screen, {AppIndustry industry = AppIndustry.restaurant}) => ProviderScope(
        overrides: [
          initialDatabaseProvider.overrideWithValue(db),
          initialIndustryProvider.overrideWithValue(industry),
          sessionControllerProvider.overrideWith(_SignedInSession.new),
          biometricServiceProvider.overrideWithValue(_NoBiometrics()),
        ],
        child: MaterialApp(
          theme: buildAppTheme(industry),
          home: screen,
          localizationsDelegates: AppLocalizations.localizationsDelegates,
          supportedLocales: AppLocalizations.supportedLocales,
        ),
      );

  Future<void> settle(WidgetTester tester) async {
    for (var i = 0; i < 5; i++) {
      await tester.runAsync(() => Future<void>.delayed(const Duration(milliseconds: 20)));
      await tester.pump();
    }
  }

  Future<void> setCode(WidgetTester tester, String value) => tester.runAsync(() => db.applyPull(
        tokens: const {},
        settings: [(key: publicMenuSettingKey(lkc), value: value)],
        features: const [],
        permissions: null,
      ));

  // Drift closes its query streams on a zero-length timer: let it fire before the test ends.
  Future<void> unmount(WidgetTester tester) async {
    await tester.pumpWidget(const SizedBox());
    await settle(tester);
    await tester.pump(const Duration(milliseconds: 1));
  }

  testWidgets('shows the branch QR that opens its customer menu', (tester) async {
    await setCode(tester, code);
    await tester.pumpWidget(app(const MenuQrScreen()));
    await settle(tester);

    final qr = tester.widget<QrImageView>(find.byKey(const Key('menu-qr')));
    expect(qr, isNotNull);
    expect(find.text('Lake City'), findsOneWidget);
    expect(find.text('Scan with your phone camera to see our menu'), findsOneWidget);
    expect(find.text(publicMenuUrl(code)), findsOneWidget);
    expect(publicMenuUrl(code), endsWith('/m/$code'));
    await unmount(tester);
  });

  testWidgets('switched off (or never switched on): says so, no QR', (tester) async {
    await setCode(tester, '');
    await tester.pumpWidget(app(const MenuQrScreen()));
    await settle(tester);

    expect(find.byKey(const Key('menu-qr')), findsNothing);
    expect(find.text('The customer menu is off'), findsOneWidget);
    expect(find.textContaining('Lake City'), findsOneWidget);
    await unmount(tester);
  });

  testWidgets('home offers the menu QR to restaurants only', (tester) async {
    await tester.pumpWidget(app(const HomeScreen()));
    await settle(tester);
    expect(find.byKey(const Key('home-menu-qr')), findsOneWidget);
    await unmount(tester);

    await tester.pumpWidget(app(const HomeScreen(), industry: AppIndustry.pharma));
    await settle(tester);
    expect(find.byKey(const Key('home-menu-qr')), findsNothing);
    expect(find.byKey(const Key('home-new-order')), findsOneWidget, reason: 'the rest of home is unchanged');
    await unmount(tester);
  });
}

class _SignedInSession extends SessionController {
  @override
  SessionState build() => const SessionState(
        SessionStatus.signedIn,
        profile: UserProfile(
          userId: 'waiter-1', username: 'waiter', displayName: 'Waiter', tenantId: 't', tenantCode: 'CHZ', tenantName: 'Cheezious',
          mustChangePassword: false,
        ),
      );
}

class _NoBiometrics extends BiometricService {
  @override
  Future<bool> isAvailable() async => false;
}
