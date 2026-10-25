import 'package:flutter/material.dart';

/// Status colours: the same in every theme, so "paid", "warning" and "error" always look alike.
abstract final class AppColors {
  static const border = Color(0xFFE6E8EF);
  static const textMuted = Color(0xFF6B7280);
  static const success = Color(0xFF047857);
  static const successSoft = Color(0xFFECFDF5);
  static const warning = Color(0xFFB45309);
  static const warningSoft = Color(0xFFFFFBEB);
  static const danger = Color(0xFFB91C1C);
  static const dangerSoft = Color(0xFFFEF2F2);
}

/// The company's industry (tenant template) decides the app's look: restaurants warm and
/// food-like, pharma distributors clean and clinical.
enum AppIndustry {
  restaurant,
  pharma,
  standard;

  /// From the server's tenant industry ("Restaurant", "Pharma", ...); anything else is standard.
  static AppIndustry fromServer(String? value) => switch (value?.toLowerCase()) {
        'restaurant' => AppIndustry.restaurant,
        'pharma' => AppIndustry.pharma,
        _ => AppIndustry.standard,
      };
}

/// The colours and touches that differ per industry. Screens read it with `context.palette`.
@immutable
class AppPalette extends ThemeExtension<AppPalette> {
  const AppPalette({
    required this.primary,
    required this.primarySoft,
    required this.ink,
    required this.background,
    required this.hero,
    required this.icon,
    required this.menuAsGrid,
  });

  final Color primary;
  final Color primarySoft;

  /// Headings.
  final Color ink;
  final Color background;

  /// The header gradient (home banner, brand mark).
  final List<Color> hero;

  /// The industry's symbol on the home banner.
  final IconData icon;

  /// Restaurants browse a picture grid; pharma reads a dense list of many similar items.
  final bool menuAsGrid;

  static const restaurant = AppPalette(
    primary: Color(0xFFC2410C),
    primarySoft: Color(0xFFFFEDD5),
    ink: Color(0xFF431407),
    background: Color(0xFFFFF7ED),
    hero: [Color(0xFF7C2D12), Color(0xFFC2410C), Color(0xFFF59E0B)],
    icon: Icons.restaurant_menu,
    menuAsGrid: true,
  );

  static const pharma = AppPalette(
    primary: Color(0xFF0F766E),
    primarySoft: Color(0xFFCCFBF1),
    ink: Color(0xFF134E4A),
    background: Color(0xFFF0FDFA),
    hero: [Color(0xFF134E4A), Color(0xFF0F766E), Color(0xFF0891B2)],
    icon: Icons.local_pharmacy_outlined,
    menuAsGrid: false,
  );

  /// The back office's own indigo, before the phone knows its company.
  static const standard = AppPalette(
    primary: Color(0xFF4F46E5),
    primarySoft: Color(0xFFEEF2FF),
    ink: Color(0xFF1A1F36),
    background: Color(0xFFF5F6FA),
    hero: [Color(0xFF1E1B4B), Color(0xFF4F46E5), Color(0xFF7C3AED)],
    icon: Icons.inventory_2_outlined,
    menuAsGrid: false,
  );

  static AppPalette of(AppIndustry industry) => switch (industry) {
        AppIndustry.restaurant => restaurant,
        AppIndustry.pharma => pharma,
        AppIndustry.standard => standard,
      };

  @override
  AppPalette copyWith({Color? primary, Color? primarySoft, Color? ink, Color? background, List<Color>? hero, IconData? icon, bool? menuAsGrid}) =>
      AppPalette(
        primary: primary ?? this.primary,
        primarySoft: primarySoft ?? this.primarySoft,
        ink: ink ?? this.ink,
        background: background ?? this.background,
        hero: hero ?? this.hero,
        icon: icon ?? this.icon,
        menuAsGrid: menuAsGrid ?? this.menuAsGrid,
      );

  @override
  AppPalette lerp(AppPalette? other, double t) {
    if (other == null) return this;
    return AppPalette(
      primary: Color.lerp(primary, other.primary, t)!,
      primarySoft: Color.lerp(primarySoft, other.primarySoft, t)!,
      ink: Color.lerp(ink, other.ink, t)!,
      background: Color.lerp(background, other.background, t)!,
      hero: [for (var i = 0; i < hero.length; i++) Color.lerp(hero[i], other.hero[i % other.hero.length], t)!],
      icon: t < 0.5 ? icon : other.icon,
      menuAsGrid: t < 0.5 ? menuAsGrid : other.menuAsGrid,
    );
  }
}

extension AppPaletteContext on BuildContext {
  AppPalette get palette => Theme.of(this).extension<AppPalette>() ?? AppPalette.standard;
}

ThemeData buildAppTheme([AppIndustry industry = AppIndustry.standard]) {
  final palette = AppPalette.of(industry);
  final scheme = ColorScheme.fromSeed(seedColor: palette.primary).copyWith(
    primary: palette.primary,
    onPrimary: Colors.white,
    secondaryContainer: palette.primarySoft,
    surface: Colors.white,
    error: AppColors.danger,
  );

  final rounded = RoundedRectangleBorder(borderRadius: BorderRadius.circular(12));

  return ThemeData(
    useMaterial3: true,
    colorScheme: scheme,
    extensions: [palette],
    // Transparent: the router puts every screen on AppBackground (the industry's tint).
    scaffoldBackgroundColor: Colors.transparent,
    appBarTheme: AppBarTheme(
      backgroundColor: Colors.transparent,
      foregroundColor: palette.ink,
      elevation: 0,
      scrolledUnderElevation: 0,
      centerTitle: false,
      titleTextStyle: TextStyle(color: palette.ink, fontSize: 20, fontWeight: FontWeight.w700),
    ),
    cardTheme: CardThemeData(
      color: Colors.white,
      elevation: 0,
      margin: EdgeInsets.zero,
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(16),
        side: BorderSide(color: Color.lerp(AppColors.border, palette.primary, 0.08)!),
      ),
    ),
    chipTheme: ChipThemeData(
      selectedColor: palette.primary,
      secondarySelectedColor: palette.primary,
      checkmarkColor: Colors.white,
      labelStyle: TextStyle(color: palette.ink),
      secondaryLabelStyle: const TextStyle(color: Colors.white),
      side: BorderSide(color: Color.lerp(AppColors.border, palette.primary, 0.15)!),
      shape: const StadiumBorder(),
    ),
    floatingActionButtonTheme: FloatingActionButtonThemeData(backgroundColor: palette.primary, foregroundColor: Colors.white),
    inputDecorationTheme: InputDecorationTheme(
      filled: true,
      fillColor: Colors.white,
      border: OutlineInputBorder(borderRadius: BorderRadius.circular(12), borderSide: const BorderSide(color: AppColors.border)),
      enabledBorder: OutlineInputBorder(borderRadius: BorderRadius.circular(12), borderSide: const BorderSide(color: AppColors.border)),
      focusedBorder: OutlineInputBorder(borderRadius: BorderRadius.circular(12), borderSide: BorderSide(color: palette.primary, width: 2)),
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

/// The screen background: the industry's soft tint at the top fading into its page colour,
/// with the industry's symbol watermarked faintly in the corner.
class AppBackground extends StatelessWidget {
  const AppBackground({super.key, required this.child});

  final Widget child;

  @override
  Widget build(BuildContext context) {
    final palette = context.palette;
    return DecoratedBox(
      decoration: BoxDecoration(
        gradient: LinearGradient(
          begin: Alignment.topCenter,
          end: Alignment.bottomCenter,
          stops: const [0, 0.45],
          colors: [palette.primarySoft, palette.background],
        ),
      ),
      child: Stack(children: [
        Positioned(
          top: -30,
          right: -40,
          child: IgnorePointer(child: Icon(palette.icon, size: 220, color: palette.primary.withValues(alpha: 0.05))),
        ),
        Positioned.fill(child: child),
      ]),
    );
  }
}

/// The "S" mark used on the web login page, in the company's industry colours.
class BrandMark extends StatelessWidget {
  const BrandMark({super.key, this.size = 44});

  final double size;

  @override
  Widget build(BuildContext context) {
    final palette = context.palette;
    return Container(
      width: size,
      height: size,
      alignment: Alignment.center,
      decoration: BoxDecoration(
        borderRadius: BorderRadius.circular(size * 0.28),
        gradient: LinearGradient(colors: [palette.hero[1], palette.hero.last], begin: Alignment.topLeft, end: Alignment.bottomRight),
        boxShadow: [BoxShadow(color: palette.primary.withValues(alpha: 0.4), blurRadius: 16, offset: const Offset(0, 6))],
      ),
      child: Text('S', style: TextStyle(color: Colors.white, fontWeight: FontWeight.w700, fontSize: size * 0.45)),
    );
  }
}
