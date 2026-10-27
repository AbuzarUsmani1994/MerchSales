import 'dart:convert';
import 'dart:io';

import 'package:dio/dio.dart';
import 'package:drift/native.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:stockorder_mobile/core/auth/auth_repository.dart';
import 'package:stockorder_mobile/core/auth/token_storage.dart';
import 'package:stockorder_mobile/core/db/app_database.dart';
import 'package:stockorder_mobile/core/providers.dart';
import 'package:stockorder_mobile/features/auth/login_screen.dart';
import 'package:stockorder_mobile/l10n/generated/app_localizations.dart';

/// The app in Urdu: every text translated, right-to-left layout, the choice kept on the phone.
void main() {
  test('every English text has an Urdu translation with the same placeholders', () {
    Map<String, dynamic> read(String file) => jsonDecode(File('lib/l10n/$file').readAsStringSync()) as Map<String, dynamic>;
    final en = read('app_en.arb');
    final ur = read('app_ur.arb');
    final placeholder = RegExp(r'\{(\w+)[,}]');

    for (final key in en.keys.where((k) => !k.startsWith('@'))) {
      expect(ur.containsKey(key), isTrue, reason: 'missing Urdu for "$key"');
      final enNames = placeholder.allMatches(en[key] as String).map((m) => m.group(1)).toSet();
      final urNames = placeholder.allMatches(ur[key] as String).map((m) => m.group(1)).toSet();
      expect(urNames, enNames, reason: 'placeholders of "$key"');
    }
  });

  group('sign-in screen', () {
    late AppDatabase db;
    late _FailingAuth auth;

    setUp(() {
      FlutterSecureStorage.setMockInitialValues({});
      db = AppDatabase(NativeDatabase.memory());
      auth = _FailingAuth();
    });

    tearDown(() => db.close());

    Widget app({String? language}) => ProviderScope(
          overrides: [
            initialDatabaseProvider.overrideWithValue(db),
            authRepositoryProvider.overrideWithValue(auth),
            serverConfiguredProvider.overrideWithValue(true),
            initialLanguageProvider.overrideWithValue(language),
          ],
          // Same locale wiring as the app.
          child: Consumer(
            builder: (context, ref, _) => MaterialApp(
              locale: switch (ref.watch(languageProvider)) { final code? => Locale(code), null => const Locale('en') },
              localizationsDelegates: AppLocalizations.localizationsDelegates,
              supportedLocales: AppLocalizations.supportedLocales,
              home: const LoginScreen(),
            ),
          ),
        );

    testWidgets('switches to Urdu, right to left, and remembers it', (tester) async {
      await tester.pumpWidget(app());
      await tester.pumpAndSettle();
      expect(find.text('Company code'), findsOneWidget);

      await tester.tap(find.byKey(const Key('language-switch')));
      await tester.pumpAndSettle();

      expect(find.text('کمپنی کوڈ'), findsOneWidget);
      expect(find.text('Company code'), findsNothing);
      expect(Directionality.of(tester.element(find.byType(LoginScreen))), TextDirection.rtl);
      expect(find.text('English'), findsOneWidget, reason: 'the button offers the way back');
      expect(await tester.runAsync(() => TokenStorage(const FlutterSecureStorage()).readLanguage()), 'ur');
    });

    testWidgets('in Urdu, the usual sign-in refusals read in Urdu', (tester) async {
      await tester.pumpWidget(app(language: 'ur'));
      await tester.pumpAndSettle();

      await tester.enterText(find.widgetWithText(TextFormField, 'کمپنی کوڈ'), 'CHEEZ');
      await tester.enterText(find.widgetWithText(TextFormField, 'یوزر نیم'), 'waiter');
      await tester.enterText(find.widgetWithText(TextFormField, 'پاس ورڈ'), 'wrong');
      await tester.tap(find.text('سائن اِن کریں'));
      await tester.pumpAndSettle();

      expect(find.text('کمپنی کوڈ، یوزر نیم یا پاس ورڈ غلط ہے۔'), findsOneWidget);
    });
  });
}

class _FailingAuth implements AuthRepository {
  @override
  Future<UserProfile> login({required String tenantCode, required String username, required String password, String? totpCode}) {
    final options = RequestOptions(path: '/api/v1/auth/login');
    throw DioException(
      requestOptions: options,
      response: Response(
        requestOptions: options,
        statusCode: 409,
        data: {'code': 'INVALID_CREDENTIALS', 'detail': 'Invalid tenant code, username or password.'},
      ),
      type: DioExceptionType.badResponse,
    );
  }

  @override
  Future<void> changePassword({required String currentPassword, required String newPassword}) async {}

  @override
  Future<void> signOut() async {}
}
