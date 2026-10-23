import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/providers.dart';

class LoginScreen extends ConsumerStatefulWidget {
  const LoginScreen({super.key});

  @override
  ConsumerState<LoginScreen> createState() => _LoginScreenState();
}

class _LoginScreenState extends ConsumerState<LoginScreen> {
  final _tenantCodeController = TextEditingController();
  final _usernameController = TextEditingController();
  final _passwordController = TextEditingController();
  final _totpController = TextEditingController();

  bool _submitting = false;
  bool _mfaRequired = false;
  String? _errorMessage;

  Future<void> _submit() async {
    setState(() {
      _submitting = true;
      _errorMessage = null;
    });

    try {
      final result = await ref.read(authRepositoryProvider).login(
            tenantCode: _tenantCodeController.text.trim(),
            username: _usernameController.text.trim(),
            password: _passwordController.text,
            totpCode: _totpController.text.isEmpty ? null : _totpController.text,
          );

      if (!mounted) return;
      if (result.mustChangePassword) {
        context.go('/change-password');
      } else {
        context.go('/');
      }
    } on DioException catch (e) {
      final code = (e.response?.data as Map?)?['code'] as String?;
      setState(() {
        if (code == 'MFA_REQUIRED') {
          _mfaRequired = true;
          _errorMessage = 'Enter your authenticator code to continue.';
        } else {
          _errorMessage = (e.response?.data as Map?)?['detail'] as String? ?? 'Sign in failed.';
        }
      });
    } finally {
      if (mounted) setState(() => _submitting = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      body: Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 360),
          child: Padding(
            padding: const EdgeInsets.all(24),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                const Text('StockOrder', style: TextStyle(fontSize: 24, fontWeight: FontWeight.bold)),
                const SizedBox(height: 24),
                TextField(
                  controller: _tenantCodeController,
                  decoration: const InputDecoration(labelText: 'Tenant code'),
                ),
                TextField(controller: _usernameController, decoration: const InputDecoration(labelText: 'Username')),
                TextField(
                  controller: _passwordController,
                  decoration: const InputDecoration(labelText: 'Password'),
                  obscureText: true,
                ),
                if (_mfaRequired)
                  TextField(
                    controller: _totpController,
                    decoration: const InputDecoration(labelText: 'Authenticator code'),
                  ),
                if (_errorMessage != null)
                  Padding(
                    padding: const EdgeInsets.only(top: 8),
                    child: Text(_errorMessage!, style: const TextStyle(color: Colors.red)),
                  ),
                const SizedBox(height: 16),
                FilledButton(
                  onPressed: _submitting ? null : _submit,
                  child: _submitting
                      ? const SizedBox(height: 16, width: 16, child: CircularProgressIndicator(strokeWidth: 2))
                      : const Text('Sign in'),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}
