import 'dart:convert';

import 'package:drift/native.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:stockorder_mobile/core/auth/token_storage.dart';
import 'package:stockorder_mobile/core/db/app_database.dart';
import 'package:stockorder_mobile/core/providers.dart';
import 'package:stockorder_mobile/core/session/session_controller.dart';
import 'package:stockorder_mobile/features/ordering/cart_screen.dart';
import 'package:stockorder_mobile/features/ordering/menu_screen.dart';
import 'package:stockorder_mobile/features/ordering/orders_screen.dart';
import 'package:stockorder_mobile/l10n/generated/app_localizations.dart';

/// Section 19 (Flutter): widget test for taking a sales order.
void main() {
  late AppDatabase db;
  const lkc = 'branch-lkc';

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
        'uoms': [
          {'id': 'portion', 'code': 'portion', 'name': 'Portion', 'decimalPlaces': 0, 'isActive': true},
        ],
        'priceLists': [
          {'id': 'menu', 'name': 'Menu', 'isDefault': true, 'branchId': null, 'isActive': true},
        ],
        'products': [
          {
            'id': 'tikka', 'sku': 'ITEM-0001', 'name': 'Tikka Pizza', 'shortName': null, 'description': null, 'categoryId': null,
            'behaviour': 'Recipe', 'baseUomId': 'portion', 'salesUomId': null, 'ownerBranchId': null, 'isActive': true,
          },
        ],
        'priceListItems': [
          {'id': 'p1', 'priceListId': 'menu', 'productId': 'tikka', 'uomId': 'portion', 'minQuantity': 1, 'unitPrice': 1200, 'validFrom': null, 'validTo': null},
        ],
      },
    );
    await db.writeMeta(SyncMetaKeys.maxOfflineHours, '24');
  });

  tearDown(() => db.close());

  Widget app() {
    // Same nesting as the app's router: the work screens sit under Home.
    final router = GoRouter(initialLocation: '/order', routes: [
      GoRoute(path: '/', builder: (context, state) => const Scaffold(body: Text('HOME')), routes: [
        GoRoute(
          path: 'order',
          builder: (context, state) => const MenuScreen(),
          routes: [GoRoute(path: 'cart', builder: (context, state) => const CartScreen())],
        ),
        GoRoute(path: 'orders', builder: (context, state) => const OrdersScreen()),
      ]),
    ]);
    return ProviderScope(
      overrides: [
        initialDatabaseProvider.overrideWithValue(db),
        sessionControllerProvider.overrideWith(_SignedInSession.new),
      ],
      child: MaterialApp.router(
        routerConfig: router,
        localizationsDelegates: AppLocalizations.localizationsDelegates,
        supportedLocales: AppLocalizations.supportedLocales,
      ),
    );
  }

  /// Drift does real async work that the widget test's fake clock never completes on its own.
  Future<void> settle(WidgetTester tester) async {
    for (var i = 0; i < 5; i++) {
      await tester.runAsync(() => Future<void>.delayed(const Duration(milliseconds: 20)));
      await tester.pump();
    }
    await tester.pumpAndSettle();
  }

  Future<T> real<T>(WidgetTester tester, Future<T> Function() work) async => (await tester.runAsync(work)) as T;

  Future<void> unmount(WidgetTester tester) async {
    await tester.pumpWidget(const SizedBox());
    await settle(tester);
  }

  testWidgets('waiter adds items, must say who it is for, and the order is queued for the cashier', (tester) async {
    await real(tester, () => db.writeMeta(SyncMetaKeys.lastSuccessfulSyncAtUtc, DateTime.now().toUtc().toIso8601String()));
    await tester.pumpWidget(app());
    await settle(tester);

    expect(find.text('Menu · Lake City'), findsOneWidget);
    await tester.tap(find.byKey(const Key('add-tikka')));
    await settle(tester);
    await tester.tap(find.byIcon(Icons.add_circle));
    await settle(tester);
    expect(find.text('2 items'), findsOneWidget);

    await tester.tap(find.textContaining('View order'));
    await settle(tester);
    await tester.tap(find.byKey(const Key('send-order')));
    await settle(tester);
    expect(find.text('Say who the order is for, so the cashier can find it.'), findsOneWidget);
    expect(await real(tester, db.pendingCommands), isEmpty);

    await tester.enterText(find.byKey(const Key('order-label')), 'Table 4');
    await tester.tap(find.byKey(const Key('send-order')));
    await settle(tester);

    final command = (await real(tester, db.pendingCommands)).single;
    final payload = jsonDecode(command.payloadJson) as Map<String, dynamic>;
    expect(command.type, 'CreateSalesOrder');
    expect(payload['notes'], 'Table 4');
    expect(payload['lines'], [
      {'productId': 'tikka', 'quantity': 2, 'notes': null},
    ]);
    expect(payload.containsKey('unitPrice'), isFalse, reason: 'the server prices orders, never the phone');
    expect(find.text('Table 4'), findsOneWidget, reason: 'lands on the orders list');
    expect(find.text('Sending'), findsOneWidget);

    // The bug the waiter hit: Orders after sending had no way back.
    expect(find.byType(BackButton), findsOneWidget);
    await tester.tap(find.byKey(const Key('orders-new-order')));
    await settle(tester);
    expect(find.text('Menu · Lake City'), findsOneWidget, reason: 'New order opens the menu for the next order');
    await tester.tap(find.byType(BackButton));
    await settle(tester);
    expect(find.text('HOME'), findsOneWidget, reason: 'back from the menu returns home');
    await unmount(tester);
  });

  testWidgets('a phone offline for too long cannot take new orders', (tester) async {
    await real(tester, () => db.writeMeta(
        SyncMetaKeys.lastSuccessfulSyncAtUtc, DateTime.now().toUtc().subtract(const Duration(hours: 30)).toIso8601String()));
    await tester.pumpWidget(app());
    await settle(tester);

    await tester.tap(find.byKey(const Key('add-tikka')));
    await settle(tester);
    await tester.tap(find.textContaining('View order'));
    await settle(tester);
    await tester.enterText(find.byKey(const Key('order-label')), 'Table 9');
    await tester.tap(find.byKey(const Key('send-order')));
    await settle(tester);

    expect(find.textContaining('offline too long'), findsOneWidget);
    expect(await real(tester, db.pendingCommands), isEmpty);
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
