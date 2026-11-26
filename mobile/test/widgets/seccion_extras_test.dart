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

/// Las tres listas del daño, con lo justo para elegir: una clase, un tamaño y un nivel.
final clasesDano = [tipo('golpe', 'Golpe'), tipo('rayon', 'Rayón')];
final tamanosDano = [tipo('1_5cm', 'De 1 a 5 cm')];
final nivelesDano = [tipo('v2', 'V2')];

/// Abre la hoja del extra con un botón y devuelve lo que entregue al cerrarse.
///
/// `puedeMarcarDano` en falso es el caso del conductor: no se le ofrece marcarlo como dañado, porque el servidor
/// exige el nivel para el daño de un extra y él no asigna niveles.
Future<DatosExtra? Function()> abrirHoja(
  WidgetTester tester,
  List<ValorCatalogo> tipos, {
  List<ValorCatalogo>? niveles,
  bool puedeMarcarDano = true,
}) async {
  DatosExtra? resultado;
  await tester.pumpWidget(MaterialApp(
    theme: crearTema(),
    home: Scaffold(
      body: Builder(
        builder: (context) => TextButton(
          onPressed: () async => resultado = await pedirExtra(
            context,
            tipos: tipos,
            clases: clasesDano,
            tamanos: tamanosDano,
            niveles: niveles ?? nivelesDano,
            puedeMarcarDano: puedeMarcarDano,
          ),
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

  /// S7-HU-04.2: el golpe de un platón es un golpe del carro. Se marca aquí mismo, con los tres datos de
  /// siempre, y cuenta en las estadísticas como cualquier otro.
  group('el extra dañado', () {
    Future<DatosExtra? Function()> conPlaton(WidgetTester tester, {bool puedeMarcarDano = true}) async {
      final resultado = await abrirHoja(tester, [tipo('platon', 'Platón')], puedeMarcarDano: puedeMarcarDano);
      await tester.tap(find.widgetWithText(ChoiceChip, 'Platón'));
      await tester.pumpAndSettle();
      return resultado;
    }

    Future<void> marcarDanado(WidgetTester tester) async {
      await tester.tap(find.byType(SwitchListTile));
      await tester.pumpAndSettle();
    }

    testWidgets('sin marcarlo como dañado se guarda como hasta ahora', (tester) async {
      final resultado = await conPlaton(tester);

      expect(find.text('Clase de daño'), findsNothing, reason: 'no se pregunta lo que no se va a usar');
      await tester.tap(find.widgetWithText(FilledButton, 'Agregar'));
      await tester.pumpAndSettle();

      expect(resultado()?.dano, isNull);
    });

    testWidgets('al marcarlo pide clase, tamaño y nivel, y los reclama de uno en uno', (tester) async {
      final resultado = await conPlaton(tester);
      await marcarDanado(tester);

      expect(find.text('Clase de daño'), findsOneWidget);
      expect(find.text('Tamaño'), findsOneWidget);
      expect(find.text('Nivel de severidad'), findsOneWidget);
      expect(find.text('Elige la clase del daño.'), findsOneWidget);
      expect(tester.widget<FilledButton>(find.widgetWithText(FilledButton, 'Agregar')).onPressed, isNull);

      await tester.tap(find.widgetWithText(ChoiceChip, 'Golpe'));
      await tester.pumpAndSettle();
      expect(find.text('Elige el tamaño del daño.'), findsOneWidget);

      await tester.tap(find.widgetWithText(ChoiceChip, 'De 1 a 5 cm'));
      await tester.pumpAndSettle();
      expect(find.text('Elige el nivel de severidad.'), findsOneWidget);
      expect(tester.widget<FilledButton>(find.widgetWithText(FilledButton, 'Agregar')).onPressed, isNull,
          reason: 'ESC-05: sin nivel no se guarda, ni siquiera a medias');

      // La hoja crecio con las tres listas: el nivel queda abajo del todo y hay que traerlo a la vista.
      await tester.ensureVisible(find.widgetWithText(ChoiceChip, 'V2'));
      await tester.pumpAndSettle();
      await tester.tap(find.widgetWithText(ChoiceChip, 'V2'));
      await tester.pumpAndSettle();
      await tester.tap(find.widgetWithText(FilledButton, 'Agregar'));
      await tester.pumpAndSettle();

      final dano = resultado()?.dano;
      expect((dano?.clase, dano?.tamano, dano?.nivel), ('golpe', '1_5cm', 'v2'));
    });

    // Al revés que en el daño de una zona: en un extra el servidor exige el nivel a todo el mundo (CONTRATO.md,
    // «los tres, con danado: true»). Al conductor, que no asigna niveles, no se le ofrece marcarlo: si se le
    // ofreciera, armaría un envío que vuelve con 400 y deja el informe a medias.
    testWidgets('al conductor no se le ofrece marcarlo como dañado, y se le dice qué hacer', (tester) async {
      final resultado = await conPlaton(tester, puedeMarcarDano: false);

      expect(find.byType(SwitchListTile), findsNothing);
      expect(find.textContaining('lo califica quien inspecciona'), findsOneWidget);

      await tester.tap(find.widgetWithText(FilledButton, 'Agregar'));
      await tester.pumpAndSettle();

      expect(resultado()?.dano, isNull, reason: 'nunca sale un extra dañado sin nivel');
    });

    testWidgets('apagar el interruptor deja el extra sin daño', (tester) async {
      final resultado = await conPlaton(tester);
      await marcarDanado(tester);
      await tester.tap(find.widgetWithText(ChoiceChip, 'Golpe'));
      await tester.pumpAndSettle();

      await marcarDanado(tester);
      await tester.tap(find.widgetWithText(FilledButton, 'Agregar'));
      await tester.pumpAndSettle();

      expect(resultado()?.dano, isNull);
      expect(resultado()?.tipo, 'platon');
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
