import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:traza_autos/app/app.dart';
import 'package:traza_autos/core/errores/error_api.dart';
import 'package:traza_autos/core/tema/tema.dart';
import 'package:traza_autos/features/auth/presentation/auth_controller.dart';
import 'package:traza_autos/features/auth/presentation/cambiar_pin_page.dart';
import 'package:traza_autos/features/auth/presentation/login_page.dart';
import 'package:traza_autos/features/catalogos/providers.dart';
import 'package:traza_autos/features/viajes/presentation/mis_viajes_page.dart';

import '../support/arnes.dart';
import '../support/dobles_catalogos.dart';

/// Con un PIN temporal el servidor responde 403 a todo lo demás, así que esta pantalla es la única salida hacia
/// adelante: si deja pasar sin cambiarlo, la app queda inservible y la persona no sabe por qué.

Future<ProviderContainer> montar(WidgetTester tester, Arnes arnes) async {
  await tester.pumpWidget(
    ProviderScope(
      overrides: arnes.overrides,
      child: MaterialApp(theme: crearTema(), home: const CambiarPinPage()),
    ),
  );
  await tester.pump();
  return ProviderScope.containerOf(tester.element(find.byType(CambiarPinPage)));
}

Finder get botonGuardar => find.widgetWithText(FilledButton, 'Guardar PIN');
bool habilitado(WidgetTester tester) => tester.widget<FilledButton>(botonGuardar).onPressed != null;

Future<void> llenar(WidgetTester tester, {String actual = '482915', String nuevo = '730194', String? repetido}) async {
  await tester.enterText(find.widgetWithText(TextField, 'PIN actual'), actual);
  await tester.enterText(find.widgetWithText(TextField, 'PIN nuevo (4 a 8 dígitos)'), nuevo);
  await tester.enterText(find.widgetWithText(TextField, 'Repite el PIN nuevo'), repetido ?? nuevo);
  await tester.pump();
}

/// Varios fotogramas en vez de pumpAndSettle: la app muestra indicadores que no paran solos. Son bastantes
/// porque hay que dejar terminar la animación de la transición: hasta que acaba, la página que sale sigue en el
/// árbol y `find` la encuentra.
Future<void> avanzar(WidgetTester tester) async {
  for (var i = 0; i < 16; i++) {
    await tester.pump(const Duration(milliseconds: 50));
  }
}

Future<void> abrirApp(WidgetTester tester, Arnes arnes) async {
  await tester.pumpWidget(
    ProviderScope(
      retry: (_, _) => null,
      overrides: [
        ...arnes.overrides,
        catalogosRepositorioProvider.overrideWithValue(CatalogosRepositorioFalso()..guardado = catalogosCon('Golpe')),
      ],
      child: const TrazaApp(),
    ),
  );
  await avanzar(tester);
}

void main() {
  group('la pantalla', () {
    testWidgets('dice de quién es el PIN que hay que cambiar', (tester) async {
      await montar(tester, Arnes(sesion: sesionConPinTemporal));

      expect(find.text('Cambia tu PIN'), findsOneWidget);
      expect(find.textContaining('Luis Gómez'), findsOneWidget);
    });

    testWidgets('el PIN nuevo va de 4 a 8 dígitos', (tester) async {
      await montar(tester, Arnes(sesion: sesionConPinTemporal));

      await llenar(tester, nuevo: '123');
      expect(habilitado(tester), isFalse, reason: 'tres dígitos no alcanzan');

      await llenar(tester, nuevo: '1234');
      expect(habilitado(tester), isTrue);
    });

    testWidgets('avisa si los dos no coinciden, y no antes de escribir', (tester) async {
      await montar(tester, Arnes(sesion: sesionConPinTemporal));
      expect(find.text('Los PIN no coinciden.'), findsNothing, reason: 'no se regaña por un campo vacío');

      await llenar(tester, nuevo: '730194', repetido: '730195');

      expect(find.text('Los PIN no coinciden.'), findsOneWidget);
      expect(habilitado(tester), isFalse);
    });

    testWidgets('los tres campos van ocultos y solo admiten dígitos', (tester) async {
      await montar(tester, Arnes(sesion: sesionConPinTemporal));

      final campos = tester.widgetList<TextField>(find.byType(TextField));
      expect(campos, hasLength(3));
      for (final campo in campos) {
        expect(campo.obscureText, isTrue);
        expect(campo.maxLength, largoMaximoPin);
      }
    });

    testWidgets('manda el PIN actual y el nuevo', (tester) async {
      final arnes = Arnes(sesion: sesionConPinTemporal);
      await montar(tester, arnes);

      await llenar(tester);
      await tester.tap(botonGuardar);
      await tester.pump();
      await tester.pump();

      expect(arnes.auth.cambiosDePin.single, ('482915', '730194'));
    });

    testWidgets('un PIN actual errado se muestra y deja reintentar', (tester) async {
      final arnes = Arnes(sesion: sesionConPinTemporal)
        ..auth.errorCambioPin = const ErrorApi(
          message: 'Datos inválidos',
          code: 'VALIDATION_ERROR',
          status: 400,
          details: {
            'fieldErrors': {
              'pin_actual': ['El PIN actual no es correcto'],
            },
          },
        );
      await montar(tester, arnes);

      await llenar(tester);
      await tester.tap(botonGuardar);
      await tester.pump();
      await tester.pump();

      expect(find.text('El PIN actual no es correcto'), findsOneWidget);
      expect(habilitado(tester), isTrue);
    });

    // Es la única salida hacia atrás: sin ella, alguien a quien le reiniciaron el PIN y no lo recuerda queda
    // encerrado en esta pantalla sin poder ni siquiera entrar con otro usuario.
    testWidgets('se puede cerrar sesión desde aquí', (tester) async {
      final arnes = Arnes(sesion: sesionConPinTemporal);
      final contenedor = await montar(tester, arnes);

      await tester.tap(find.widgetWithText(TextButton, 'Cerrar sesión'));
      await tester.pump();
      await tester.pump();

      expect(contenedor.read(authControllerProvider).fase, FaseAuth.noAutenticado);
      expect(arnes.almacen.sesion, isNull);
    });
  });

  group('el router con un PIN temporal', () {
    testWidgets('lleva ahí al entrar, en vez de a las pestañas', (tester) async {
      await abrirApp(tester, Arnes(sesion: sesionConPinTemporal));

      expect(find.byType(CambiarPinPage), findsOneWidget);
      expect(find.byType(MisViajesPage), findsNothing);
      expect(find.byType(NavigationBar), findsNothing, reason: 'no hay nada que navegar todavía');
    });

    testWidgets('al cambiarlo sale solo a su primera pestaña, sin volver a entrar', (tester) async {
      final arnes = Arnes(sesion: sesionConPinTemporal);
      await abrirApp(tester, arnes);

      await llenar(tester);
      await tester.tap(botonGuardar);
      await avanzar(tester);

      expect(find.byType(CambiarPinPage), findsNothing);
      expect(find.byType(MisViajesPage), findsOneWidget);
      expect(find.byType(LoginPage), findsNothing, reason: 'el mismo token sigue sirviendo');
    });

    testWidgets('al cerrar sesión desde ahí se va al login', (tester) async {
      await abrirApp(tester, Arnes(sesion: sesionConPinTemporal));

      await tester.tap(find.widgetWithText(TextButton, 'Cerrar sesión'));
      await avanzar(tester);

      expect(find.byType(LoginPage), findsOneWidget);
    });

    testWidgets('sin PIN temporal no se puede entrar a esa pantalla', (tester) async {
      await abrirApp(tester, Arnes());

      tester.element(find.byType(MisViajesPage)).go('/cambiar-pin');
      await avanzar(tester);

      expect(find.byType(CambiarPinPage), findsNothing);
      expect(find.byType(MisViajesPage), findsOneWidget);
    });
  });
}
