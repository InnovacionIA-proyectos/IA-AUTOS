import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/core/tema/tema.dart';
import 'package:traza_autos/features/dibujo_dano/domain/dibujo_dano.dart';
import 'package:traza_autos/features/dibujo_dano/domain/geometria.dart';
import 'package:traza_autos/features/dibujo_dano/domain/toque.dart';
import 'package:traza_autos/features/dibujo_dano/presentation/dibujo_dano_controller.dart';
import 'package:traza_autos/features/dibujo_dano/presentation/pintor_regiones.dart';
import 'package:traza_autos/features/dibujo_dano/presentation/selector_zona_dano.dart';

import '../support/dobles_dibujo.dart';

/// El comportamiento del prototipo, sobre los datos reales. La superficie es ancha a propósito: a 1600 px el
/// costado izquierdo (la primera pestaña) no necesita acercar, y así cada toque elige directamente.

final dibujo = dibujoDeAssets();

Future<void> montar(
  WidgetTester tester, {
  Set<String> conDano = const {},
  void Function(ZonaDano zona)? alRegistrar,
  void Function(ZonaDano zona)? alAbrirDano,
}) async {
  await tester.binding.setSurfaceSize(const Size(1600, 2400));
  addTearDown(() => tester.binding.setSurfaceSize(null));
  await tester.pumpWidget(
    ProviderScope(
      retry: (_, _) => null,
      overrides: [dibujoDanoProvider.overrideWith((ref) => dibujo)],
      child: MaterialApp(
        theme: crearTema(),
        home: Scaffold(
          body: SingleChildScrollView(
            child: SelectorZonaDano(
              contexto: 'prueba',
              conDano: conDano,
              alRegistrar: alRegistrar ?? (_) {},
              alAbrirDano: alAbrirDano,
              alturaMaxima: 2000,
            ),
          ),
        ),
      ),
    ),
  );
  await tester.pump();
}

final _lienzo = find.byWidgetPredicate((w) => w is CustomPaint && w.painter is PintorRegiones);

PanelDano panelVisible(WidgetTester tester) => (tester.widget<CustomPaint>(_lienzo).painter! as PintorRegiones).panel;

/// Un punto en píxeles de referencia del panel visible, en coordenadas de pantalla.
Offset enPantalla(WidgetTester tester, Punto ref) {
  final caja = tester.getRect(_lienzo);
  final panel = panelVisible(tester);
  return caja.topLeft + Offset(ref.x / panel.anchoRef * caja.width, ref.y / panel.altoRef * caja.height);
}

Future<void> tocarZona(WidgetTester tester, String clave) async {
  final region = dibujo.regionesDe(panelVisible(tester).codigo).singleWhere((r) => r.clave == clave);
  await tester.tapAt(enPantalla(tester, region.puntoEtiqueta));
  await tester.pump();
}

void main() {
  // En un teléfono las piezas son más chicas que un dedo: el primer toque acerca y el siguiente tiene que elegir.
  testWidgets('en un teléfono, el primer toque acerca y el segundo elige la zona', (tester) async {
    await tester.binding.setSurfaceSize(const Size(393, 852));
    addTearDown(() => tester.binding.setSurfaceSize(null));
    await tester.pumpWidget(
      ProviderScope(
        retry: (_, _) => null,
        overrides: [dibujoDanoProvider.overrideWith((ref) => dibujo)],
        child: MaterialApp(
          theme: crearTema(),
          home: Scaffold(
            body: ListView(
              padding: const EdgeInsets.all(16),
              children: [SelectorZonaDano(contexto: 'telefono', conDano: const {}, alRegistrar: (_) {})],
            ),
          ),
        ),
      ),
    );
    await tester.pump();
    final region = dibujo.regionesDe('costado_izquierdo').singleWhere((r) => r.clave == '13I');

    await tester.tapAt(enPantalla(tester, region.puntoEtiqueta));
    await tester.pump();
    expect(find.text('Ver todo'), findsOneWidget, reason: 'el primer toque acerca');
    expect(find.textContaining('Cuadrante 13'), findsNothing);

    await tester.tapAt(enPantalla(tester, region.puntoEtiqueta));
    await tester.pump();
    expect(find.textContaining('Cuadrante 13, lado izquierdo'), findsOneWidget);
  });

  // Lo que pasó en el Redmi: acercar en la llanta delantera y tocarla elegía el rin trasero y movía la vista.
  testWidgets('en un teléfono, acercar en una pieza y tocarla elige esa pieza sin mover la vista', (tester) async {
    await tester.binding.setSurfaceSize(const Size(393, 852));
    addTearDown(() => tester.binding.setSurfaceSize(null));
    await tester.pumpWidget(
      ProviderScope(
        retry: (_, _) => null,
        overrides: [dibujoDanoProvider.overrideWith((ref) => dibujo)],
        child: MaterialApp(
          theme: crearTema(),
          home: Scaffold(
            body: ListView(
              padding: const EdgeInsets.all(16),
              children: [SelectorZonaDano(contexto: 'telefono-llanta', conDano: const {}, alRegistrar: (_) {})],
            ),
          ),
        ),
      ),
    );
    await tester.pump();
    final llanta = dibujo.regionesDe('costado_izquierdo').singleWhere((r) => r.clave == '6I');

    await tester.tapAt(enPantalla(tester, llanta.puntoEtiqueta));
    await tester.pump();
    final vista = tester.getRect(_lienzo);

    await tester.tapAt(enPantalla(tester, llanta.puntoEtiqueta));
    await tester.pump();
    expect(find.text('Llanta delantera izquierda'), findsOneWidget);
    expect(tester.getRect(_lienzo), vista, reason: 'elegir no mueve la vista');
  });

  testWidgets('tocar una zona la elige y muestra su nombre completo; tocarla otra vez la suelta', (tester) async {
    await montar(tester);
    expect(panelVisible(tester).codigo, 'costado_izquierdo');

    await tocarZona(tester, '6I');
    expect(find.text('Llanta delantera izquierda'), findsOneWidget);
    expect(find.textContaining('Cuadrante 6, lado izquierdo'), findsOneWidget);

    await tocarZona(tester, '6I');
    expect(find.text('Llanta delantera izquierda'), findsNothing);
  });

  testWidgets('«Deseleccionar» suelta la zona', (tester) async {
    await montar(tester);
    await tocarZona(tester, '6I');

    await tester.tap(find.widgetWithText(OutlinedButton, 'Deseleccionar'));
    await tester.pump();

    expect(find.text('Llanta delantera izquierda'), findsNothing);
  });

  testWidgets('«Registrar daño» entrega la zona con su número y su lado', (tester) async {
    final registradas = <ZonaDano>[];
    await montar(tester, alRegistrar: registradas.add);
    await tocarZona(tester, '34I');

    await tester.tap(find.widgetWithText(FilledButton, 'Registrar daño'));
    await tester.pump();

    expect((registradas.single.numero, registradas.single.lado), (34, LadoZona.izquierdo));
  });

  testWidgets('un toque en el borde entre dos zonas pregunta cuál', (tester) async {
    await montar(tester);
    final panel = panelVisible(tester);
    final regiones = dibujo.regionesDe(panel.codigo);
    final tolerancia = MedidasToque.tolerancia / (tester.getRect(_lienzo).width / panel.anchoRef);
    final (punto, claves) = [
      for (final r in regiones)
        for (final v in r.puntosRef) (v, {for (final o in regiones) if (distanciaAPoligono(v, o.puntosRef) <= tolerancia) o.clave}),
    ].firstWhere((c) => c.$2.length == 2);

    await tester.tapAt(enPantalla(tester, punto));
    await tester.pump();

    expect(find.text('¿Cuál cuadrante tocaste?'), findsOneWidget);
    for (final clave in claves) {
      expect(find.text(dibujo.zona(clave)!.nombreCompleto), findsOneWidget, reason: clave);
    }

    final elegida = dibujo.zona(claves.first)!;
    await tester.tap(find.text(elegida.nombreCompleto));
    await tester.pump();
    expect(find.text('¿Cuál cuadrante tocaste?'), findsNothing);
    expect(find.text(elegida.nombreCompleto), findsOneWidget, reason: 'queda elegida y se ve su ficha');
  });

  testWidgets('un daño por zona: tocar una zona que ya tiene daño abre ese registro', (tester) async {
    final abiertas = <String>[];
    await montar(tester, conDano: {'6I'}, alAbrirDano: (z) => abiertas.add(z.clave));

    await tocarZona(tester, '6I');

    expect(abiertas, ['6I']);
    expect(find.widgetWithText(FilledButton, 'Registrar daño'), findsNothing);
    expect(find.widgetWithText(FilledButton, 'Ver daño'), findsOneWidget);
  });

  testWidgets('una zona con daño se cuenta en cada pestaña donde se ve', (tester) async {
    await montar(tester, conDano: {'26D'});

    final conNumero = find.byWidgetPredicate((w) => w is Badge && w.isLabelVisible);
    expect(conNumero, findsNWidgets(dibujo.panelesDondeSeVe('26D').length));
  });

  testWidgets('con una zona elegida, las pestañas donde también se ve llevan el punto amarillo', (tester) async {
    await montar(tester);
    await tocarZona(tester, '6I');

    final puntos = find.byWidgetPredicate((w) => w is Semantics && w.properties.label == 'El cuadrante elegido se ve aquí');
    expect(puntos, findsNWidgets(dibujo.panelesDondeSeVe('6I').length));
  });

  testWidgets('en solo lectura no se ofrece registrar', (tester) async {
    await tester.binding.setSurfaceSize(const Size(1600, 2400));
    addTearDown(() => tester.binding.setSurfaceSize(null));
    await tester.pumpWidget(
      ProviderScope(
        retry: (_, _) => null,
        overrides: [dibujoDanoProvider.overrideWith((ref) => dibujo)],
        child: MaterialApp(
          theme: crearTema(),
          home: const Scaffold(
            body: SingleChildScrollView(child: SelectorZonaDano(contexto: 'lectura', conDano: {'6I'}, alturaMaxima: 2000)),
          ),
        ),
      ),
    );
    await tester.pump();
    await tocarZona(tester, '34I');

    expect(find.text('Rin delantero izquierdo'), findsOneWidget);
    expect(find.byType(FilledButton), findsNothing);
  });
}
