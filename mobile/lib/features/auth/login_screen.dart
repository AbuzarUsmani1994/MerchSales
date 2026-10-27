import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../core/providers.dart';
import '../../core/session/session_controller.dart';
import '../../l10n/generated/app_localizations.dart';
import '../../shared/language_switch.dart';
import '../../shared/theme.dart';

class LoginScreen extends ConsumerStatefulWidget {
  const LoginScreen({super.key});

  @override
  ConsumerState<LoginScreen> createState() => _LoginScreenState();
}

class _LoginScreenState extends ConsumerState<LoginScreen> {
  final _formKey = GlobalKey<FormState>();
  final _tenantCodeController = TextEditingController();
  final _usernameController = TextEditingController();
  final _passwordController = TextEditingController();
  final _totpController = TextEditingController();

  bool _submitting = false;
  bool _mfaRequired = false;
  bool _obscurePassword = true;
  String? _errorMessage;

  @override
  void dispose() {
    _tenantCodeController.dispose();
    _usernameController.dispose();
    _passwordController.dispose();
    _totpController.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    final l10n = AppLocalizations.of(context);
    if (!_formKey.currentState!.validate()) return;

    setState(() {
      _submitting = true;
      _errorMessage = null;
    });

    try {
      await ref.read(sessionControllerProvider.notifier).login(
            tenantCode: _tenantCodeController.text.trim(),
            username: _usernameController.text.trim(),
            password: _passwordController.text,
            totpCode: _totpController.text.trim().isEmpty ? null : _totpController.text.trim(),
          );
    } on DioException catch (e) {
      setState(() {
        final code = problemCode(e);
        if (code == 'MFA_REQUIRED') {
          _mfaRequired = true;
          _errorMessage = l10n.enterAuthenticatorCode;
        } else if (e.response == null) {
          _errorMessage = l10n.networkError;
        } else {
          // The app's own text for the usual refusals, so they read in the app's language.
          _errorMessage = switch (code) {
            'INVALID_CREDENTIALS' when !_mfaRequired => l10n.errorInvalidCredentials,
            'LOCKED_OUT' => l10n.errorLockedOut,
            'TENANT_SUSPENDED' => l10n.errorTenantSuspended,
            'PERMISSION_DENIED' => l10n.errorNotAllowedOnMobile,
            _ => problemDetail(e) ?? l10n.signInFailed,
          };
        }
      });
    } catch (e) {
      // Anything else (storage, the local database) used to end the spinner with no message.
      setState(() => _errorMessage = l10n.errorUnexpected('$e'));
    } finally {
      if (mounted) setState(() => _submitting = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final reason = ref.watch(sessionControllerProvider).signedOutReason;
    final serverConfigured = ref.watch(serverConfiguredProvider);
    final notice = switch (reason) {
      SignedOutReason.deviceRevoked => l10n.deviceRevokedMessage,
      SignedOutReason.deviceWiped => l10n.deviceWipedMessage,
      SignedOutReason.sessionExpired => l10n.sessionExpiredMessage,
      null => null,
    };

    return Scaffold(
      body: SafeArea(
        child: Center(
          child: SingleChildScrollView(
            padding: const EdgeInsets.all(24),
            child: ConstrainedBox(
              constraints: const BoxConstraints(maxWidth: 420),
              child: Form(
                key: _formKey,
                child: AutofillGroup(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      Row(children: [
                        const BrandMark(size: 40),
                        const SizedBox(width: 12),
                        Text(l10n.appTitle, style: TextStyle(fontSize: 20, fontWeight: FontWeight.w700, color: context.palette.ink)),
                        const Spacer(),
                        const LanguageButton(),
                      ]),
                      const SizedBox(height: 40),
                      Text(l10n.loginTitle, style: TextStyle(fontSize: 30, fontWeight: FontWeight.w700, color: context.palette.ink)),
                      const SizedBox(height: 6),
                      Text(l10n.loginSubtitle, style: const TextStyle(color: AppColors.textMuted, fontSize: 15)),
                      const SizedBox(height: 28),
                      if (notice != null) ...[
                        _Banner(text: notice, color: AppColors.warning, background: AppColors.warningSoft),
                        const SizedBox(height: 16),
                      ],
                      if (!serverConfigured) ...[
                        _Banner(text: l10n.serverNotConfigured, color: AppColors.danger, background: AppColors.dangerSoft),
                        const SizedBox(height: 16),
                      ],
                      TextFormField(
                        controller: _tenantCodeController,
                        decoration: InputDecoration(labelText: l10n.tenantCode, prefixIcon: const Icon(Icons.business_outlined)),
                        textCapitalization: TextCapitalization.characters,
                        textInputAction: TextInputAction.next,
                        autofillHints: const [AutofillHints.organizationName],
                        validator: (v) => (v ?? '').trim().isEmpty ? l10n.fieldRequired : null,
                      ),
                      const SizedBox(height: 14),
                      TextFormField(
                        controller: _usernameController,
                        decoration: InputDecoration(labelText: l10n.username, prefixIcon: const Icon(Icons.person_outline)),
                        textInputAction: TextInputAction.next,
                        autocorrect: false,
                        autofillHints: const [AutofillHints.username],
                        validator: (v) => (v ?? '').trim().isEmpty ? l10n.fieldRequired : null,
                      ),
                      const SizedBox(height: 14),
                      TextFormField(
                        controller: _passwordController,
                        decoration: InputDecoration(
                          labelText: l10n.password,
                          prefixIcon: const Icon(Icons.lock_outline),
                          suffixIcon: IconButton(
                            icon: Icon(_obscurePassword ? Icons.visibility_outlined : Icons.visibility_off_outlined),
                            onPressed: () => setState(() => _obscurePassword = !_obscurePassword),
                          ),
                        ),
                        obscureText: _obscurePassword,
                        textInputAction: _mfaRequired ? TextInputAction.next : TextInputAction.done,
                        autofillHints: const [AutofillHints.password],
                        onFieldSubmitted: (_) => _mfaRequired ? null : _submit(),
                        validator: (v) => (v ?? '').isEmpty ? l10n.fieldRequired : null,
                      ),
                      if (_mfaRequired) ...[
                        const SizedBox(height: 14),
                        TextFormField(
                          controller: _totpController,
                          decoration: InputDecoration(labelText: l10n.authenticatorCode, prefixIcon: const Icon(Icons.pin_outlined)),
                          keyboardType: TextInputType.number,
                          autofillHints: const [AutofillHints.oneTimeCode],
                          onFieldSubmitted: (_) => _submit(),
                        ),
                      ],
                      if (_errorMessage != null) ...[
                        const SizedBox(height: 14),
                        Text(_errorMessage!, key: const Key('login-error'), style: const TextStyle(color: AppColors.danger)),
                      ],
                      const SizedBox(height: 24),
                      FilledButton(
                        onPressed: _submitting || !serverConfigured ? null : _submit,
                        child: _submitting
                            ? const SizedBox(height: 20, width: 20, child: CircularProgressIndicator(strokeWidth: 2, color: Colors.white))
                            : Text(l10n.signIn),
                      ),
                    ],
                  ),
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}

class _Banner extends StatelessWidget {
  const _Banner({required this.text, required this.color, required this.background});

  final String text;
  final Color color;
  final Color background;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.all(14),
      decoration: BoxDecoration(color: background, borderRadius: BorderRadius.circular(12)),
      child: Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
        Icon(Icons.info_outline, color: color, size: 20),
        const SizedBox(width: 10),
        Expanded(child: Text(text, style: TextStyle(color: color))),
      ]),
    );
  }
}
