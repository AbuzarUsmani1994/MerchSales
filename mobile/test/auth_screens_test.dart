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
import 'package:stockorder_mobile/features/auth/change_password_screen.dart';
import 'package:stockorder_mobile/features/auth/login_screen.dart';
import 'package:stockorder_mobile/l10n/generated/app_localizations.dart';

/// Section 19 (Flutter): widget tests for login.
void main() {
  late AppDatabase db;
  late FakeAuthRepository auth;

  setUp(() {
    FlutterSecureStorage.setMockInitialValues({});
    db = AppDatabase(NativeDatabase.memory());
    auth = FakeAuthRepository();
  });

  tearDown(() => db.close());

  Widget app(Widget home) => ProviderScope(
        overrides: [
          initialDatabaseProvider.overrideWithValue(db),
          authRepositoryProvider.overrideWithValue(auth),
          serverConfiguredProvider.overrideWithValue(true),
        ],
        child: MaterialApp(
          localizationsDelegates: AppLocalizations.localizationsDelegates,
          supportedLocales: AppLocalizations.supportedLocales,
          home: home,
        ),
      );

  testWidgets('login validates required fields before calling the server', (tester) async {
    await tester.pumpWidget(app(const LoginScreen()));
    await tester.pumpAndSettle();

    await tester.tap(find.text('Sign in').last);
    await tester.pumpAndSettle();

    expect(find.text('Required'), findsNWidgets(3));
    expect(auth.loginCalls, 0);
  });

  testWidgets('login shows the server message and asks for the authenticator code when required', (tester) async {
    await tester.pumpWidget(app(const LoginScreen()));
    await tester.pumpAndSettle();

    await tester.enterText(find.widgetWithText(TextFormField, 'Company code'), 'CHEEZ');
    await tester.enterText(find.widgetWithText(TextFormField, 'Username'), 'owner');
    await tester.enterText(find.widgetWithText(TextFormField, 'Password'), 'secret');

    auth.nextError = _problem(409, 'MFA_REQUIRED', 'A TOTP code is required for this account.');
    await tester.tap(find.text('Sign in').last);
    await tester.pumpAndSettle();

    expect(find.widgetWithText(TextFormField, 'Authenticator code'), findsOneWidget);
    expect(find.text('Enter the code from your authenticator app.'), findsOneWidget);

    auth.nextError = _problem(401, 'INVALID_CREDENTIALS', 'Invalid tenant code, username or password.');
    await tester.tap(find.text('Sign in').last);
    await tester.pumpAndSettle();

    expect(find.text('Invalid tenant code, username or password.'), findsOneWidget);
    expect(auth.loginCalls, 2);
  });

  testWidgets('login without a response says the phone is offline', (tester) async {
    await tester.pumpWidget(app(const LoginScreen()));
    await tester.pumpAndSettle();

    await tester.enterText(find.widgetWithText(TextFormField, 'Company code'), 'CHEEZ');
    await tester.enterText(find.widgetWithText(TextFormField, 'Username'), 'owner');
    await tester.enterText(find.widgetWithText(TextFormField, 'Password'), 'secret');
    auth.nextError = DioException(requestOptions: RequestOptions(path: '/api/v1/auth/login'), type: DioExceptionType.connectionError);

    await tester.tap(find.text('Sign in').last);
    await tester.pumpAndSettle();

    expect(find.text("Can't reach the server. Check your internet connection."), findsOneWidget);
  });

  testWidgets('change password applies the same rules as the server', (tester) async {
    await tester.pumpWidget(app(const ChangePasswordScreen()));
    await tester.pumpAndSettle();

    await tester.enterText(find.widgetWithText(TextFormField, 'One-time or current password'), 'Temp1234');
    await tester.enterText(find.widgetWithText(TextFormField, 'New password'), '12345678');
    await tester.enterText(find.widgetWithText(TextFormField, 'Confirm new password'), '1234567');
    await tester.tap(find.widgetWithText(FilledButton, 'Change password'));
    await tester.pumpAndSettle();

    expect(find.text("Can't be only numbers"), findsOneWidget);
    expect(find.text("Passwords don't match"), findsOneWidget);
    expect(auth.changePasswordCalls, 0);
  });
}

DioException _problem(int status, String code, String detail) {
  final options = RequestOptions(path: '/api/v1/auth/login');
  return DioException(
    requestOptions: options,
    response: Response(requestOptions: options, statusCode: status, data: {'code': code, 'detail': detail}),
    type: DioExceptionType.badResponse,
  );
}

class FakeAuthRepository implements AuthRepository {
  int loginCalls = 0;
  int changePasswordCalls = 0;
  DioException? nextError;

  @override
  Future<UserProfile> login({required String tenantCode, required String username, required String password, String? totpCode}) async {
    loginCalls++;
    if (nextError != null) throw nextError!;
    return UserProfile(
      userId: 'u1',
      username: username,
      displayName: 'Test',
      tenantId: 't1',
      tenantCode: tenantCode,
      tenantName: 'Test',
      mustChangePassword: false,
    );
  }

  @override
  Future<void> changePassword({required String currentPassword, required String newPassword}) async => changePasswordCalls++;

  @override
  Future<void> signOut() async {}
}
