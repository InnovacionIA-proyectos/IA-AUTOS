import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/core/tema/tema.dart';
import 'package:traza_autos/features/catalogos/providers.dart';
import 'package:traza_autos/features/inspecciones/presentation/captura_page.dart';
import 'package:traza_autos/features/vehiculos/domain/inventario.dart';
import 'package:traza_autos/features/viajes/providers.dart';

import '../support/arnes.dart';
import '../support/dobles_catalogos.dart';
import '../support/dobles_viajes.dart';

/// El criterio de la S5-HU-22.9: «la captura toma placa, tráiler, unidad y conductor del viaje; se retiran los
/// campos de texto libre». Es lo único que prueba que la decisión llegue a la pantalla y no solo al repositorio.

/// Varios fotogramas en vez de pumpAndSettle: la página busca el GPS y muestra indicadores que no paran solos.
///
/// La superficie es alta a propósito: el `ListView` de la captura es perezoso y en una pantalla de teléfono no
/// construye el campo de notas ni el botón de cierre, que son justo lo que hay que mirar.
Future<Arnes> abrir(
  WidgetTester tester,
  ViajesRepositorioFalso viajes, {
  String? viajeId = 'v1',
  Arnes? arnes,
  List<Map<String, dynamic>> vistas = const [],
  String proceso = 'Cargue',
  String procesoCodigo = 'cargue',
}) async {
  await tester.binding.setSurfaceSize(const Size(600, 2400));
  addTearDown(() => tester.binding.setSurfaceSize(null));
  final usado = arnes ?? Arnes();
  await tester.pumpWidget(
    ProviderScope(
      retry: (_, _) => null,
      overrides: [
        ...usado.overrides,
        viajesRepositorioProvider.overrideWithValue(viajes),
        // Con copia guardada además de la remota: así los catálogos están listos también sin señal, como queda
        // el celular tras el primer ingreso. Sin ellos el cierre se bloquea y no se llega al desenlace.
        catalogosRepositorioProvider.overrideWithValue(
          CatalogosRepositorioFalso()
            ..guardado = catalogosCon('Golpe', vistas: vistas)
            ..remoto = catalogosCon('Golpe', vistas: vistas),
        ),
      ],
      child: MaterialApp(
        theme: crearTema(),
        home: CapturaPage(
          vin: 'VIN00000000000001',
          etapaId: 'e1',
          proceso: procesoCodigo,
          momento: Momento.salida,
          viajeId: viajeId,
        ),
      ),
    ),
  );
  for (var i = 0; i < 5; i++) {
    await tester.pump(const Duration(milliseconds: 50));
  }
  return usado;
}

/// Cierra el informe y espera a que salga el diálogo del desenlace.
Future<void> cerrarInforme(WidgetTester tester) async {
  await tester.tap(find.widgetWithText(FilledButton, 'Cerrar informe'));
  for (var i = 0; i < 8; i++) {
    await tester.pump(const Duration(milliseconds: 50));
  }
}

/// El valor que acompaña a una etiqueta de la tarjeta del viaje («Placa» → «ABC123»).
Finder dato(String etiqueta, String valor) => find.ancestor(
      of: find.text(etiqueta),
      matching: find.widgetWithText(Row, valor),
    );

void main() {
  testWidgets('muestra placa, tráiler, unidad y conductor tomados del viaje', (tester) async {
    final viajes = ViajesRepositorioFalso()
      ..viajeDeEtapa = viaje('v1', placa: 'XKL482', trailer: 'T-9', unidad: 'Niñera', conductor: 'Luis Gómez');

    await abrir(tester, viajes);

    expect(viajes.etapasConsultadas, ['v1']);
    expect(dato('Placa', 'XKL482'), findsOneWidget);
    expect(dato('Tráiler', 'T-9'), findsOneWidget);
    expect(dato('Unidad', 'Niñera'), findsOneWidget);
    expect(dato('Conductor', 'Luis Gómez'), findsOneWidget);
  });

  // La otra mitad del criterio, y la que se rompe sin ruido: alguien vuelve a poner un campo escribible y el
  // informe vuelve a llevar datos de transporte tecleados, que el servidor ya no acepta.
  testWidgets('no quedan campos de texto libre del transporte: solo se escriben las notas', (tester) async {
    await abrir(tester, ViajesRepositorioFalso()..viajeDeEtapa = viaje('v1'));

    final campos = tester.widgetList<TextField>(find.byType(TextField));
    expect(campos, hasLength(1));
    expect((campos.single.decoration!.labelText)!, contains('Notas'));
  });

  testWidgets('los cuatro datos no son editables', (tester) async {
    await abrir(tester, ViajesRepositorioFalso()..viajeDeEtapa = viaje('v1', conductor: 'Luis Gómez'));

    for (final valor in ['ABC123', 'Niñera', 'Luis Gómez']) {
      expect(find.widgetWithText(TextField, valor), findsNothing, reason: '$valor se muestra, no se escribe');
    }
  });

  testWidgets('un viaje sin tráiler ni conductor los marca en vez de dejarlos en blanco', (tester) async {
    await abrir(tester, ViajesRepositorioFalso()..viajeDeEtapa = viaje('v1', trailer: null, conductor: null));

    expect(dato('Tráiler', '—'), findsOneWidget);
    expect(dato('Conductor', '—'), findsOneWidget);
  });

  testWidgets('una etapa sin viaje lo dice y no pregunta al servidor', (tester) async {
    final viajes = ViajesRepositorioFalso();

    await abrir(tester, viajes, viajeId: null);

    expect(find.text('Esta etapa no tiene viaje asociado.'), findsOneWidget);
    expect(viajes.etapasConsultadas, isEmpty);
  });

  // Que no se puedan traer los datos no debe dejar a nadie sin poder registrar lo que está viendo en el patio: el
  // servidor toma la placa y el conductor del viaje al recibir el informe, no de aquí.
  testWidgets('si el viaje no se puede traer avisa, pero deja cerrar el informe igual', (tester) async {
    await abrir(tester, ViajesRepositorioFalso()..errorDeLaEtapa = Exception('sin red'));

    expect(find.text('No se pudieron cargar los datos del viaje.'), findsOneWidget);
    final boton = tester.widget<FilledButton>(find.widgetWithText(FilledButton, 'Cerrar informe'));
    expect(boton.onPressed, isNotNull);
  });

  // Lo que el informe le dice a quien está en el patio tiene que ser verdad: es su única señal de si el trabajo
  // quedó hecho. El mensaje se decidía con el contador de pendientes de TODA la cola, que excluye los fallidos,
  // así que un rechazo del servidor se anunciaba como «enviado».
  group('el desenlace que se anuncia al cerrar', () {
    testWidgets('enviado: el job salió de la cola', (tester) async {
      final arnes = await abrir(tester, ViajesRepositorioFalso()..viajeDeEtapa = viaje('v1'));

      await cerrarInforme(tester);

      expect(find.text('Informe enviado'), findsOneWidget);
      expect(arnes.cola.jobs, isEmpty);
    });

    testWidgets('en cola: sin señal se queda pendiente y se enviará solo', (tester) async {
      final arnes = Arnes()..red.estable = false;
      await abrir(tester, ViajesRepositorioFalso()..viajeDeEtapa = viaje('v1'), arnes: arnes);

      await cerrarInforme(tester);

      expect(find.text('Informe guardado'), findsOneWidget);
      expect(find.textContaining('se enviará automáticamente'), findsOneWidget);
      expect(arnes.cola.jobs.single.fallido, isFalse);
    });

    testWidgets('rechazado: NO dice enviado, dice qué pasó y dónde quedó', (tester) async {
      final arnes = Arnes()
        ..gateway.errores['crearInspeccion'] = [errorApi(400, 'Momento inválido: use salida o llegada')];
      await abrir(tester, ViajesRepositorioFalso()..viajeDeEtapa = viaje('v1'), arnes: arnes);

      await cerrarInforme(tester);

      expect(arnes.cola.jobs.single.fallido, isTrue, reason: 'un 400 no se arregla reintentando');
      expect(find.text('Informe enviado'), findsNothing);
      expect(find.text('No se pudo enviar'), findsOneWidget);
      expect(find.textContaining('Momento inválido'), findsOneWidget, reason: 'el motivo del servidor, tal cual');
      expect(find.textContaining('Cola'), findsOneWidget, reason: 'dónde recuperarlo');
    });

  });

  group('el checklist de vistas', () {
    testWidgets('lista las vistas del catálogo, con su condición', (tester) async {
      await abrir(tester, ViajesRepositorioFalso()..viajeDeEtapa = viaje('v1'), vistas: vistasDePrueba);

      expect(find.text('Fotos de la inspección'), findsOneWidget);
      expect(find.text('Frontal'), findsOneWidget);
      expect(find.text('Piezas en el baúl'), findsOneWidget);
      expect(find.text('Solo si hay faltante o daño'), findsOneWidget,
          reason: 'la adicional dice cuándo se toma; la obligatoria no lo necesita');
      expect(find.widgetWithText(TextButton, 'Tomar'), findsNWidgets(2));
    });

    // Sin catálogo no se inventa un checklist: pedir vistas que nadie configuró sería peor que no pedir ninguna.
    testWidgets('sin vistas descargadas no se inventan', (tester) async {
      await abrir(tester, ViajesRepositorioFalso()..viajeDeEtapa = viaje('v1'));

      expect(find.text('Fotos de la inspección'), findsNothing);
      expect(find.widgetWithText(TextButton, 'Tomar'), findsNothing);
    });

    // En Transporte la salida y la llegada son el registro del conductor: lo que se exige es el GPS, no 7 fotos.
    testWidgets('en Transporte no hay checklist y se dice por qué', (tester) async {
      await abrir(
        tester,
        ViajesRepositorioFalso()..viajeDeEtapa = viaje('v1'),
        vistas: vistasDePrueba,
        procesoCodigo: 'transporte',
      );

      expect(find.text('Fotos de la inspección'), findsNothing);
      expect(find.textContaining('no pide fotos por vista'), findsOneWidget);
    });

    testWidgets('el título dice qué momento se está capturando', (tester) async {
      await abrir(tester, ViajesRepositorioFalso()..viajeDeEtapa = viaje('v1'), vistas: vistasDePrueba);

      expect(find.text('Inspección de salida · cargue'), findsOneWidget);
    });
  });

  // Pautas del Entendimiento (S5-HU-24.12): la placa es un código que se lee carácter a carácter contra la
  // unidad que se tiene delante, igual que el VIN.
  testWidgets('la placa va en monoespaciada, como el VIN', (tester) async {
    await abrir(tester, ViajesRepositorioFalso()..viajeDeEtapa = viaje('v1', placa: 'XKL482'));

    final placa = tester.widget<Text>(find.text('XKL482'));
    expect(placa.style?.fontFamily, 'monospace');
  });
}
