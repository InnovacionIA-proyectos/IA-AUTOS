import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/core/tema/tema.dart';
import 'package:traza_autos/features/catalogos/providers.dart';
import 'package:traza_autos/features/inspecciones/presentation/captura_page.dart';
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
Future<void> abrir(WidgetTester tester, ViajesRepositorioFalso viajes, {String? viajeId = 'v1'}) async {
  await tester.binding.setSurfaceSize(const Size(600, 2400));
  addTearDown(() => tester.binding.setSurfaceSize(null));
  final arnes = Arnes();
  await tester.pumpWidget(
    ProviderScope(
      retry: (_, _) => null,
      overrides: [
        ...arnes.overrides,
        viajesRepositorioProvider.overrideWithValue(viajes),
        catalogosRepositorioProvider.overrideWithValue(CatalogosRepositorioFalso()..remoto = catalogosCon('Golpe')),
      ],
      child: MaterialApp(
        theme: crearTema(),
        home: CapturaPage(vin: 'VIN00000000000001', etapaId: 'e1', proceso: 'Cargue', viajeId: viajeId),
      ),
    ),
  );
  for (var i = 0; i < 5; i++) {
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
}
