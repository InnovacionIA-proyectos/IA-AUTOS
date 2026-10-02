import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/core/config/entorno.dart';
import 'package:traza_autos/core/tema/tema.dart';
import 'package:traza_autos/features/auth/presentation/auth_controller.dart';
import 'package:traza_autos/features/auth/presentation/login_page.dart';

import '../support/arnes.dart';

Future<ProviderContainer> montar(WidgetTester tester, Arnes arnes) async {
  await tester.pumpWidget(
    ProviderScope(
      overrides: arnes.overrides,
      child: MaterialApp(theme: crearTema(), home: const LoginPage()),
    ),
  );
  await tester.pump();
  return ProviderScope.containerOf(tester.element(find.byType(LoginPage)));
}

Finder get botonIngresar => find.widgetWithText(FilledButton, 'Ingresar');
bool habilitado(WidgetTester tester) => tester.widget<FilledButton>(botonIngresar).onPressed != null;

void main() {
  testWidgets('el botón se habilita solo con un correo válido y una contraseña', (tester) async {
    await montar(tester, Arnes(sesion: null));
    expect(habilitado(tester), isFalse);

    await tester.enterText(find.widgetWithText(TextField, 'Correo'), 'sin-arroba');
    await tester.enterText(find.widgetWithText(TextField, 'Contraseña'), 'x');
    await tester.pump();
    expect(habilitado(tester), isFalse);

    await tester.enterText(find.widgetWithText(TextField, 'Correo'), 'ana@boterosoto.com.co');
    await tester.pump();
    expect(habilitado(tester), isTrue);
  });

  testWidgets('envía las credenciales escritas', (tester) async {
    final arnes = Arnes(sesion: null);
    await montar(tester, arnes);

    await tester.enterText(find.widgetWithText(TextField, 'Correo'), ' ana@boterosoto.com.co ');
    await tester.enterText(find.widgetWithText(TextField, 'Contraseña'), 'Clave-Segura-1');
    await tester.pump();
    await tester.tap(botonIngresar);
    await tester.pump();
    await tester.pump();

    expect(arnes.auth.intentos.single, ('ana@boterosoto.com.co', 'Clave-Segura-1'));
  });

  testWidgets('credenciales incorrectas muestran el mensaje y permiten reintentar', (tester) async {
    final arnes = Arnes(sesion: null)..auth.error = errorApi(401);
    await montar(tester, arnes);

    await tester.enterText(find.widgetWithText(TextField, 'Correo'), 'ana@boterosoto.com.co');
    await tester.enterText(find.widgetWithText(TextField, 'Contraseña'), 'mala');
    await tester.pump();
    await tester.tap(botonIngresar);
    await tester.pump();
    await tester.pump();

    expect(find.text('Correo o contraseña incorrectos.'), findsOneWidget);
    expect(habilitado(tester), isTrue);
  });

  testWidgets('la contraseña se oculta y se puede mostrar', (tester) async {
    await montar(tester, Arnes(sesion: null));
    TextField campo() => tester.widget<TextField>(find.widgetWithText(TextField, 'Contraseña'));
    expect(campo().obscureText, isTrue);

    await tester.tap(find.byTooltip('Mostrar contraseña'));
    await tester.pump();

    expect(campo().obscureText, isFalse);
  });

  testWidgets('sin API_URL configurada avisa y no deja ingresar', (tester) async {
    await montar(tester, Arnes(sesion: null, entorno: const Entorno(flavor: Flavor.dev, apiUrl: '')));

    expect(find.byKey(const Key('aviso-login')), findsOneWidget);
    expect(find.textContaining('API_URL'), findsOneWidget);

    await tester.enterText(find.widgetWithText(TextField, 'Correo'), 'ana@boterosoto.com.co');
    await tester.enterText(find.widgetWithText(TextField, 'Contraseña'), 'x');
    await tester.pump();
    expect(habilitado(tester), isFalse);
  });

  testWidgets('avisa cuando la sesión expiró', (tester) async {
    final contenedor = await montar(tester, Arnes());
    await tester.pump();

    contenedor.read(authControllerProvider.notifier).sesionExpirada();
    await tester.pump();

    expect(find.text('Tu sesión expiró. Inicia sesión de nuevo.'), findsOneWidget);
  });

  testWidgets('muestra el logo y el título', (tester) async {
    await montar(tester, Arnes(sesion: null));
    expect(find.text('Traza Autos 360°'), findsOneWidget);
    expect(find.bySemanticsLabel('Botero Soto'), findsOneWidget);
  });
}
