import 'package:flutter/material.dart';

/// Tokens del sistema de diseño de Botero Soto (los mismos de la web y de la app original).
/// Modo claro siempre (legibilidad bajo el sol). El verde de marca nunca lleva texto blanco encima.
abstract final class Colores {
  static const primario = Color(0xFF005AAB);
  static const primarioHover = Color(0xFF004886);
  static const secundario = Color(0xFF1E7ACB);
  static const acento = Color(0xFF009A3C);

  static const fondo = Color(0xFFFFFFFF);
  static const superficie = Color(0xFFF4F7FB);
  static const borde = Color(0xFFD6E0EC);

  static const textoPrimario = Color(0xFF0F2347);
  static const textoSecundario = Color(0xFF5A7299);

  static const exito = Color(0xFF0A7A34);
  static const advertencia = Color(0xFFE68A00);
  static const peligro = Color(0xFFD32F2F);
  static const info = Color(0xFF1E7ACB);
}

/// Espaciado en múltiplos de 4. Tamaño táctil mínimo de 48 px (campo, a la intemperie, con guantes).
abstract final class Espacio {
  static const xs = 4.0;
  static const sm = 8.0;
  static const md = 12.0;
  static const lg = 16.0;
  static const xl = 24.0;
  static const xxl = 32.0;
}

const alturaTactil = 48.0;

ThemeData crearTema() {
  const esquema = ColorScheme.light(
    primary: Colores.primario,
    onPrimary: Colors.white,
    secondary: Colores.secundario,
    onSecondary: Colors.white,
    surface: Colores.fondo,
    onSurface: Colores.textoPrimario,
    surfaceContainerHighest: Colores.superficie,
    outline: Colores.borde,
    error: Colores.peligro,
    onError: Colors.white,
  );

  final borde = OutlineInputBorder(
    borderRadius: BorderRadius.circular(10),
    borderSide: const BorderSide(color: Colores.borde),
  );

  return ThemeData(
    useMaterial3: true,
    colorScheme: esquema,
    scaffoldBackgroundColor: Colores.fondo,
    appBarTheme: const AppBarTheme(
      backgroundColor: Colores.fondo,
      foregroundColor: Colores.textoPrimario,
      elevation: 0,
      scrolledUnderElevation: 0,
      centerTitle: false,
    ),
    textTheme: const TextTheme(
      headlineMedium: TextStyle(fontSize: 24, height: 30 / 24, fontWeight: FontWeight.w600, color: Colores.textoPrimario),
      titleLarge: TextStyle(fontSize: 20, height: 26 / 20, fontWeight: FontWeight.w600, color: Colores.textoPrimario),
      titleMedium: TextStyle(fontSize: 17, height: 24 / 17, fontWeight: FontWeight.w600, color: Colores.textoPrimario),
      bodyLarge: TextStyle(fontSize: 15, height: 22 / 15, color: Colores.textoPrimario),
      bodyMedium: TextStyle(fontSize: 15, height: 22 / 15, color: Colores.textoPrimario),
      bodySmall: TextStyle(fontSize: 13, height: 18 / 13, color: Colores.textoSecundario),
      labelLarge: TextStyle(fontSize: 15, fontWeight: FontWeight.w600),
    ),
    inputDecorationTheme: InputDecorationTheme(
      border: borde,
      enabledBorder: borde,
      focusedBorder: borde.copyWith(borderSide: const BorderSide(color: Colores.secundario, width: 2)),
      errorBorder: borde.copyWith(borderSide: const BorderSide(color: Colores.peligro)),
      contentPadding: const EdgeInsets.symmetric(horizontal: Espacio.lg, vertical: Espacio.md),
      isDense: false,
    ),
    filledButtonTheme: FilledButtonThemeData(
      style: FilledButton.styleFrom(
        minimumSize: const Size.fromHeight(alturaTactil),
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(10)),
        textStyle: const TextStyle(fontSize: 15, fontWeight: FontWeight.w600),
      ),
    ),
    outlinedButtonTheme: OutlinedButtonThemeData(
      style: OutlinedButton.styleFrom(
        minimumSize: const Size.fromHeight(alturaTactil),
        side: const BorderSide(color: Colores.borde),
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(10)),
      ),
    ),
    navigationBarTheme: const NavigationBarThemeData(
      backgroundColor: Colores.fondo,
      indicatorColor: Color(0x1A005AAB),
    ),
    dividerColor: Colores.borde,
  );
}
