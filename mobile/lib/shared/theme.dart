import 'package:flutter/material.dart';

/// Same palette as the back-office web app: navy chrome, indigo accent, cool-grey surfaces.
abstract final class AppColors {
  static const navy = Color(0xFF1A1F36);
  static const navyDeep = Color(0xFF0D1021);
  static const accent = Color(0xFF4F46E5);
  static const accentSoft = Color(0xFFEEF2FF);
  static const background = Color(0xFFF5F6FA);
  static const border = Color(0xFFE6E8EF);
  static const textMuted = Color(0xFF6B7280);
  static const success = Color(0xFF047857);
  static const successSoft = Color(0xFFECFDF5);
  static const warning = Color(0xFFB45309);
  static const warningSoft = Color(0xFFFFFBEB);
  static const danger = Color(0xFFB91C1C);
  static const dangerSoft = Color(0xFFFEF2F2);
}

ThemeData buildAppTheme() {
  final scheme = ColorScheme.fromSeed(seedColor: AppColors.accent).copyWith(
    primary: AppColors.accent,
    onPrimary: Colors.white,
    surface: Colors.white,
    error: AppColors.danger,
  );

  final rounded = RoundedRectangleBorder(borderRadius: BorderRadius.circular(12));

  return ThemeData(
    useMaterial3: true,
    colorScheme: scheme,
    scaffoldBackgroundColor: AppColors.background,
    appBarTheme: const AppBarTheme(
      backgroundColor: AppColors.background,
      foregroundColor: AppColors.navy,
      elevation: 0,
      scrolledUnderElevation: 0,
      centerTitle: false,
      titleTextStyle: TextStyle(color: AppColors.navy, fontSize: 20, fontWeight: FontWeight.w700),
    ),
    cardTheme: CardThemeData(
      color: Colors.white,
      elevation: 0,
      margin: EdgeInsets.zero,
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(16), side: const BorderSide(color: AppColors.border)),
    ),
    inputDecorationTheme: InputDecorationTheme(
      filled: true,
      fillColor: Colors.white,
      border: OutlineInputBorder(borderRadius: BorderRadius.circular(12), borderSide: const BorderSide(color: AppColors.border)),
      enabledBorder: OutlineInputBorder(borderRadius: BorderRadius.circular(12), borderSide: const BorderSide(color: AppColors.border)),
      focusedBorder: OutlineInputBorder(borderRadius: BorderRadius.circular(12), borderSide: const BorderSide(color: AppColors.accent, width: 2)),
      contentPadding: const EdgeInsets.symmetric(horizontal: 16, vertical: 16),
    ),
    filledButtonTheme: FilledButtonThemeData(
      style: FilledButton.styleFrom(
        minimumSize: const Size.fromHeight(52),
        shape: rounded,
        textStyle: const TextStyle(fontSize: 16, fontWeight: FontWeight.w600),
      ),
    ),
    outlinedButtonTheme: OutlinedButtonThemeData(
      style: OutlinedButton.styleFrom(minimumSize: const Size.fromHeight(48), shape: rounded),
    ),
    textButtonTheme: TextButtonThemeData(style: TextButton.styleFrom(shape: rounded)),
  );
}

/// The "S" mark used on the web login page, so both apps read as one product.
class BrandMark extends StatelessWidget {
  const BrandMark({super.key, this.size = 44});

  final double size;

  @override
  Widget build(BuildContext context) {
    return Container(
      width: size,
      height: size,
      alignment: Alignment.center,
      decoration: BoxDecoration(
        borderRadius: BorderRadius.circular(size * 0.28),
        gradient: const LinearGradient(colors: [Color(0xFF6366F1), AppColors.accent]),
        boxShadow: const [BoxShadow(color: Color(0x664F46E5), blurRadius: 16, offset: Offset(0, 6))],
      ),
      child: Text('S', style: TextStyle(color: Colors.white, fontWeight: FontWeight.w700, fontSize: size * 0.45)),
    );
  }
}
