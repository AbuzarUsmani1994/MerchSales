import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../core/providers.dart';
import '../l10n/generated/app_localizations.dart';

/// The app's two languages. Switching is instant and remembered on the phone.
bool isUrdu(BuildContext context) => Localizations.localeOf(context).languageCode == 'ur';

void switchLanguage(BuildContext context, WidgetRef ref) =>
    ref.read(languageProvider.notifier).set(isUrdu(context) ? 'en' : 'ur');

/// "اردو" in English, "English" in Urdu: always names the language you'd switch to, in that language.
String otherLanguageName(BuildContext context) {
  final l10n = AppLocalizations.of(context);
  return isUrdu(context) ? l10n.languageEnglish : l10n.languageUrdu;
}

class LanguageButton extends ConsumerWidget {
  const LanguageButton({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) => TextButton.icon(
        key: const Key('language-switch'),
        onPressed: () => switchLanguage(context, ref),
        icon: const Icon(Icons.translate, size: 20),
        label: Text(otherLanguageName(context)),
      );
}
