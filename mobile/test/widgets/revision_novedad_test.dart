import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/core/tema/tema.dart';
import 'package:traza_autos/features/auth/presentation/auth_controller.dart';
import 'package:traza_autos/features/catalogos/providers.dart';
import 'package:traza_autos/features/dibujo_dano/presentation/dibujo_dano_controller.dart';
import 'package:traza_autos/features/dibujo_dano/presentation/pintor_regiones.dart';
import 'package:traza_autos/features/inspecciones/domain/novedad_pendiente.dart';
import 'package:traza_autos/features/inspecciones/presentation/revision_novedad_page.dart';

import '../support/arnes.dart';
import '../support/dobles_catalogos.dart';
import '../support/dobles_dibujo.dart';

/// S8-HU-08.2 CA-6: un 403 o un 409 al calificar se muestra con el texto del servidor sin perder lo elegido.

final dibujo = dibujoDeAssets();

const _novedad = NovedadPendiente(
  id: 'h1',
  vin: 'VIN00000000000001',
  proceso: 'descargue',
  momento: 'llegada',
  clase: 'golpe',
  tamano: 'mas_20cm',
  foto: FotoNovedad(tomadaEn: null, sinUbicacion: true),
);

Future<void> avanzar(WidgetTester tester) async {
  for (var i = 0; i < 12; i++) {
    await tester.pump(const Duration(milliseconds: 50));
  }
}

/// Abre la pantalla desde otra, como lo hacen la lista y la ficha, y devuelve lo que ella responde al salir.
Future<({Arnes arnes, List<bool?> respuestas})> abrir(WidgetTester tester) async {
  // Ancha a propósito: a 1600 px el primer toque elige la zona sin acercar.
  await tester.binding.setSurfaceSize(const Size(1600, 2400));
  addTearDown(() => tester.binding.setSurfaceSize(null));
  final arnes = Arnes();
  final respuestas = <bool?>[];
  await tester.pumpWidget(
    ProviderScope(
      retry: (_, _) => null,
      overrides: [
        ...arnes.overrides,
        catalogosRepositorioProvider.overrideWithValue(CatalogosRepositorioFalso()..guardado = catalogosCon('Golpe')),
        dibujoDanoProvider.overrideWith((ref) => dibujo),
      ],
      child: MaterialApp(
        theme: crearTema(),
        home: Consumer(
          builder: (context, ref, _) => ref.watch(authControllerProvider.select((e) => e.fase)) != FaseAuth.autenticado
              ? const SizedBox()
              : Scaffold(
                  body: TextButton(
                    onPressed: () async => respuestas.add(
                      await Navigator.of(context).push<bool>(
                        MaterialPageRoute(builder: (_) => const RevisionNovedadPage(novedad: _novedad)),
                      ),
                    ),
                    child: const Text('abrir'),
                  ),
                ),
        ),
      ),
    ),
  );
  await avanzar(tester);
  await tester.tap(find.text('abrir'));
  await avanzar(tester);
  return (arnes: arnes, respuestas: respuestas);
}

final _lienzo = find.byWidgetPredicate((w) => w is CustomPaint && w.painter is PintorRegiones);

/// Toca la zona y abre la hoja con «Registrar daño». El dibujo de esta pantalla es chico: el primer toque puede
/// solo acercar, y entonces se toca otra vez, con la vista ya acercada (`getRect` la trae transformada).
Future<void> tocarZona(WidgetTester tester, String clave) async {
  await tester.ensureVisible(_lienzo);
  await avanzar(tester);
  final registrar = find.widgetWithText(FilledButton, 'Registrar daño');
  for (var intento = 0; intento < 2 && registrar.evaluate().isEmpty; intento++) {
    final panel = (tester.widget<CustomPaint>(_lienzo).painter! as PintorRegiones).panel;
    final punto = dibujo.regionesDe(panel.codigo).singleWhere((r) => r.clave == clave).puntoEtiqueta;
    final caja = tester.getRect(_lienzo);
    await tester.tapAt(caja.topLeft + Offset(punto.x / panel.anchoRef * caja.width, punto.y / panel.altoRef * caja.height));
    await avanzar(tester);
  }
  await tester.ensureVisible(registrar);
  await tester.tap(find.widgetWithText(FilledButton, 'Registrar daño'));
  await avanzar(tester);
}

/// Califica la novedad: la clase y el tamaño vienen del conductor; quien revisa pone el nivel.
Future<void> calificarConV1(WidgetTester tester) async {
  await tocarZona(tester, '34I');
  await tester.tap(find.text('V1'));
  await tester.pump();
  await tester.tap(find.widgetWithText(FilledButton, 'Guardar cambios'));
  await avanzar(tester);
}

Finder get _enviarDeNuevo => find.widgetWithText(FilledButton, 'Enviar de nuevo');

void main() {
  testWidgets('un 409 deja la pantalla abierta con el texto del servidor y lo elegido', (tester) async {
    final (:arnes, :respuestas) = await abrir(tester);
    arnes.novedades.errorAlRevisar = errorApi(409, 'El hallazgo ya fue calificado');

    await calificarConV1(tester);

    expect(find.byType(RevisionNovedadPage), findsOneWidget, reason: 'no se cierra con el rechazo');
    expect(respuestas, isEmpty);
    expect(find.text('No se registró la calificación'), findsOneWidget);
    expect(find.text('El hallazgo ya fue calificado'), findsOneWidget);
    expect(find.textContaining('Golpe · V1'), findsOneWidget, reason: 'lo elegido sigue a la vista');
    expect(_enviarDeNuevo, findsOneWidget);
  });

  testWidgets('«Enviar de nuevo» manda lo mismo y, si pasa, vuelve con la lista por recargar', (tester) async {
    final (:arnes, :respuestas) = await abrir(tester);
    arnes.novedades.errorAlRevisar = errorApi(403, 'El VIN del hallazgo no pertenece a ninguno de sus viajes');
    await calificarConV1(tester);
    expect(find.text('El VIN del hallazgo no pertenece a ninguno de sus viajes'), findsOneWidget);

    arnes.novedades.errorAlRevisar = null;
    await tester.ensureVisible(_enviarDeNuevo);
    await tester.tap(_enviarDeNuevo);
    await avanzar(tester);

    expect(arnes.novedades.revisadas, hasLength(2));
    expect(arnes.novedades.revisadas.last, arnes.novedades.revisadas.first, reason: 'sin volver a elegir nada');
    expect(arnes.novedades.revisadas.last['nivel'], 'v1');
    expect(find.byType(RevisionNovedadPage), findsNothing);
    expect(respuestas, [true]);
  });

  testWidgets('tras el rechazo, la hoja se vuelve a abrir con el nivel ya elegido', (tester) async {
    final (:arnes, :respuestas) = await abrir(tester);
    arnes.novedades.errorAlRevisar = errorApi(409, 'El hallazgo ya fue calificado');
    await calificarConV1(tester);

    await tocarZona(tester, '34I');

    final guardar = tester.widget<FilledButton>(find.widgetWithText(FilledButton, 'Guardar cambios'));
    expect(guardar.onPressed, isNotNull, reason: 'clase, tamaño y nivel ya están: no hay que elegir de nuevo');
  });

  testWidgets('salir después de un rechazo le pide a quien la abrió que recargue', (tester) async {
    final (:arnes, :respuestas) = await abrir(tester);
    arnes.novedades.errorAlRevisar = errorApi(409, 'El hallazgo ya fue calificado');
    await calificarConV1(tester);

    await tester.pageBack();
    await avanzar(tester);

    expect(find.byType(RevisionNovedadPage), findsNothing);
    expect(respuestas, [true]);
  });

  testWidgets('sin señal lo dice con su propio texto y también deja reenviar', (tester) async {
    final (:arnes, :respuestas) = await abrir(tester);
    arnes.novedades.errorAlRevisar = Exception('sin red');

    await calificarConV1(tester);

    expect(find.textContaining('Revisa la señal'), findsOneWidget);
    expect(_enviarDeNuevo, findsOneWidget);
    expect(respuestas, isEmpty);
  });

  testWidgets('sin rechazo, salir no pide recargar', (tester) async {
    final (:arnes, :respuestas) = await abrir(tester);

    await tester.pageBack();
    await avanzar(tester);

    expect(arnes.novedades.revisadas, isEmpty);
    expect(respuestas, [null]);
  });
}
