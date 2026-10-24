import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../core/providers.dart';
import '../../l10n/generated/app_localizations.dart';
import '../../shared/theme.dart';

/// Section 10: users created by an admin get a one-time password and must replace it before
/// anything else. The rules mirror the server's for quick feedback; the server re-checks.
class ChangePasswordScreen extends ConsumerStatefulWidget {
  const ChangePasswordScreen({super.key});

  @override
  ConsumerState<ChangePasswordScreen> createState() => _ChangePasswordScreenState();
}

class _ChangePasswordScreenState extends ConsumerState<ChangePasswordScreen> {
  final _formKey = GlobalKey<FormState>();
  final _current = TextEditingController();
  final _new = TextEditingController();
  final _confirm = TextEditingController();
  bool _submitting = false;
  String? _error;

  @override
  void dispose() {
    _current.dispose();
    _new.dispose();
    _confirm.dispose();
    super.dispose();
  }

  String? _validateNew(String? value, AppLocalizations l10n, String username) {
    final v = value ?? '';
    if (v.length < 8) return l10n.passwordTooShort;
    if (RegExp(r'^\d+$').hasMatch(v)) return l10n.passwordAllDigits;
    if (v.toLowerCase() == username.toLowerCase()) return l10n.passwordSameAsUsername;
    return null;
  }

  Future<void> _submit() async {
    if (!_formKey.currentState!.validate()) return;
    final l10n = AppLocalizations.of(context);
    setState(() {
      _submitting = true;
      _error = null;
    });

    try {
      await ref.read(authRepositoryProvider).changePassword(currentPassword: _current.text, newPassword: _new.text);
      await ref.read(sessionControllerProvider.notifier).passwordChanged();
    } on DioException catch (e) {
      setState(() => _error = e.response == null ? l10n.networkError : problemDetail(e) ?? l10n.signInFailed);
    } finally {
      if (mounted) setState(() => _submitting = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context);
    final username = ref.watch(sessionControllerProvider).profile?.username ?? '';

    return Scaffold(
      appBar: AppBar(
        actions: [
          TextButton(
            onPressed: () => ref.read(sessionControllerProvider.notifier).signOut(),
            child: Text(l10n.signOut),
          ),
        ],
      ),
      body: SafeArea(
        child: SingleChildScrollView(
          padding: const EdgeInsets.all(24),
          child: Form(
            key: _formKey,
            child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
              Container(
                width: 48,
                height: 48,
                alignment: Alignment.center,
                decoration: BoxDecoration(color: AppColors.accentSoft, borderRadius: BorderRadius.circular(14)),
                child: const Icon(Icons.lock_reset, color: AppColors.accent),
              ),
              const SizedBox(height: 16),
              Text(l10n.changePasswordTitle, style: const TextStyle(fontSize: 26, fontWeight: FontWeight.w700, color: AppColors.navy)),
              const SizedBox(height: 6),
              Text(l10n.changePasswordSubtitle, style: const TextStyle(color: AppColors.textMuted)),
              const SizedBox(height: 24),
              TextFormField(
                controller: _current,
                obscureText: true,
                decoration: InputDecoration(labelText: l10n.currentPassword),
                validator: (v) => (v ?? '').isEmpty ? l10n.fieldRequired : null,
              ),
              const SizedBox(height: 14),
              TextFormField(
                controller: _new,
                obscureText: true,
                decoration: InputDecoration(labelText: l10n.newPassword),
                autofillHints: const [AutofillHints.newPassword],
                validator: (v) => _validateNew(v, l10n, username),
              ),
              const SizedBox(height: 14),
              TextFormField(
                controller: _confirm,
                obscureText: true,
                decoration: InputDecoration(labelText: l10n.confirmPassword),
                validator: (v) => v != _new.text ? l10n.passwordsDontMatch : null,
                onFieldSubmitted: (_) => _submit(),
              ),
              if (_error != null) ...[
                const SizedBox(height: 14),
                Text(_error!, style: const TextStyle(color: AppColors.danger)),
              ],
              const SizedBox(height: 24),
              FilledButton(
                onPressed: _submitting ? null : _submit,
                child: _submitting
                    ? const SizedBox(height: 20, width: 20, child: CircularProgressIndicator(strokeWidth: 2, color: Colors.white))
                    : Text(l10n.changePassword),
              ),
            ]),
          ),
        ),
      ),
    );
  }
}
