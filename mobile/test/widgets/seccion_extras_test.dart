import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/core/config/entorno.dart';
import 'package:traza_autos/core/tema/tema.dart';
import 'package:traza_autos/features/catalogos/domain/catalogos.dart';
import 'package:traza_autos/features/inspecciones/presentation/seccion_extras.dart';

import '../support/arnes.dart';
import '../support/dobles_viajes.dart';
import 'captura_page_test.dart' show abrir;

ValorCatalogo tipo(String codigo, String nombre) =>
    ValorCatalogo({'id': codigo, 'codigo': codigo, 'nombre': nombre, 'orden': 1, 'activo': true});

/// Abre la hoja del extra con un botón y devuelve lo que entregue al cerrarse.
Future<DatosExtra? Function()> abrirHoja(WidgetTester tester, List<ValorCatalogo> tipos) async {
  DatosExtra? resultado;
  await tester.pumpWidget(MaterialApp(
    theme: crearTema(),
    home: Scaffold(
      body: Builder(
        builder: (context) => TextButton(
          onPressed: () async => resultado = await pedirExtra(context, tipos: tipos),
          child: const Text('abrir'),
        ),
      ),
    ),
  ));
  await tester.tap(find.text('abrir'));
  await tester.pumpAndSettle();
  return () => resultado;
}

void main() {
  group('la hoja del extra', () {
    testWidgets('ofrece los tipos del catálogo y siempre «Otro», sin repetirlo', (tester) async {
      await abrirHoja(tester, [tipo('platon', 'Platón'), tipo('otro', 'Otro')]);

      expect(find.widgetWithText(ChoiceChip, 'Platón'), findsOneWidget);
      expect(find.widgetWithText(ChoiceChip, 'Otro'), findsOneWidget);
    });

    // Sin el catálogo publicado, lo que no está configurado se reporta igual.
    testWidgets('sin catálogo se puede reportar como «Otro»', (tester) async {
      await abrirHoja(tester, const []);

      expect(find.byType(ChoiceChip), findsOneWidget);
      expect(find.widgetWithText(ChoiceChip, 'Otro'), findsOneWidget);
    });

    testWidgets('«Otro» exige decir qué es', (tester) async {
      final resultado = await abrirHoja(tester, const []);

      await tester.tap(find.widgetWithText(ChoiceChip, 'Otro'));
      await tester.pumpAndSettle();
      expect(tester.widget<FilledButton>(find.widgetWithText(FilledButton, 'Agregar')).onPressed, isNull);

      await tester.enterText(find.widgetWithText(TextField, '¿Qué es?'), 'portabicicletas');
      await tester.pump();
      await tester.tap(find.widgetWithText(FilledButton, 'Agregar'));
      await tester.pumpAndSettle();

      expect(resultado()?.tipo, 'otro');
      expect(resultado()?.nombre, 'Otro: portabicicletas');
      expect(resultado()?.textoOtro, 'portabicicletas');
      expect(resultado()?.observacion, isNull);
    });

    testWidgets('un tipo del catálogo lleva su nombre y la observación', (tester) async {
      final resultado = await abrirHoja(tester, [tipo('platon', 'Platón')]);

      await tester.tap(find.widgetWithText(ChoiceChip, 'Platón'));
      await tester.enterText(find.widgetWithText(TextField, 'Observación (opcional)'), ' rayón en la compuerta ');
      await tester.tap(find.widgetWithText(FilledButton, 'Agregar'));
      await tester.pumpAndSettle();

      expect((resultado()?.tipo, resultado()?.nombre, resultado()?.observacion), ('platon', 'Platón', 'rayón en la compuerta'));
      expect(resultado()?.textoOtro, isNull);
    });
  });

  // El servidor todavía no recibe extras: con la sección visible se capturaría algo que nunca llega.
  group('la sección en la captura', () {
    testWidgets('con el interruptor apagado no aparece', (tester) async {
      await abrir(tester, ViajesRepositorioFalso()..viajeDeEtapa = viaje('v1'));

      expect(find.byType(SeccionExtras), findsNothing);
    });

    testWidgets('con el interruptor prendido aparece, sin avisos de que no se envíe', (tester) async {
      await abrir(
        tester,
        ViajesRepositorioFalso()..viajeDeEtapa = viaje('v1'),
        arnes: Arnes(entorno: const Entorno(flavor: Flavor.dev, apiUrl: 'https://api.test', extrasHabilitados: true)),
      );

      expect(find.byType(SeccionExtras), findsOneWidget);
      expect(find.textContaining('no se envían'), findsNothing, reason: 'los extras ya salen con el informe');
      expect(find.widgetWithText(OutlinedButton, 'Agregar extra'), findsOneWidget);
    });
  });
}
