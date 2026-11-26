import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_riverpod/misc.dart' show Override;
import 'package:flutter_test/flutter_test.dart';
import 'package:intl/date_symbol_data_local.dart';
import 'package:traza_autos/core/tema/tema.dart';
import 'package:traza_autos/features/inspecciones/domain/inspeccion_resumen.dart';
import 'package:traza_autos/features/inspecciones/presentation/lista_inspecciones_page.dart';
import 'package:traza_autos/features/catalogos/providers.dart';
import 'package:traza_autos/features/sync/presentation/sync_controller.dart';

import '../support/arnes.dart';
import '../support/dobles_catalogos.dart';

/// La pestaña de Inspecciones vive dentro del shell: una vez construida no se vuelve a construir al volver a
/// ella, así que lo que muestra se queda como estaba cuando se abrió por primera vez.
Future<Arnes> abrir(WidgetTester tester, {Arnes? conArnes, List<Override> extra = const []}) async {
  final arnes = conArnes ?? Arnes();
  await tester.pumpWidget(
    ProviderScope(
      retry: (_, _) => null,
      overrides: [...arnes.overrides, ...extra],
      child: MaterialApp(theme: crearTema(), home: const ListaInspeccionesPage()),
    ),
  );
  for (var i = 0; i < 6; i++) {
    await tester.pump(const Duration(milliseconds: 50));
  }
  return arnes;
}

void main() {
  setUpAll(() => initializeDateFormatting('es_CO'));

  // El defecto visto en el celular: se cierra un informe, se entra a Inspecciones y sigue apareciendo como
  // estaba antes; había que arrastrar la pantalla hacia abajo para verlo cerrado.
  testWidgets('al terminar de trabajar la cola, la lista se vuelve a cargar sola', (tester) async {
    final arnes = await abrir(tester);
    final antes = arnes.inspecciones.llamadasRemotas;
    expect(antes, greaterThan(0), reason: 'la primera carga es la de entrada a la pestaña');

    final contenedor = ProviderScope.containerOf(tester.element(find.byType(ListaInspeccionesPage)));
    await contenedor.read(syncControllerProvider.notifier).sincronizar();
    for (var i = 0; i < 6; i++) {
      await tester.pump(const Duration(milliseconds: 50));
    }

    expect(arnes.inspecciones.llamadasRemotas, greaterThan(antes));
  });

  /// S7-HU-21: la misma pestaña, dos lecturas. El inspector ve las suyas; el coordinador de calidad, las de su
  /// cliente, que no son suyas y no caben en una pantalla.
  group('el coordinador de calidad', () {
    Arnes conCoordinadora() => Arnes(sesion: sesionCoordinador);

    testWidgets('la pestaña no le habla de «mis» inspecciones y le ofrece buscar', (tester) async {
      final arnes = conCoordinadora()..inspecciones.remoto = [_resumen('1', 'VIN00000000000001')];
      await abrir(tester, conArnes: arnes);

      expect(find.text('Inspecciones'), findsOneWidget);
      expect(find.text('Mis inspecciones'), findsNothing);
      expect(find.widgetWithText(TextField, 'Buscar por VIN'), findsOneWidget);
    });

    testWidgets('sin ninguna, no lo manda a capturar: no captura ninguna', (tester) async {
      await abrir(tester, conArnes: conCoordinadora());

      expect(find.text('Aún no hay inspecciones'), findsOneWidget);
      expect(find.widgetWithText(FilledButton, 'Ir a Mis viajes'), findsNothing);
    });

    testWidgets('una lista larga ofrece traer la siguiente página', (tester) async {
      final arnes = conCoordinadora()
        ..inspecciones.paginas = {
          1: [_resumen('1', 'VIN00000000000001')],
          2: [_resumen('2', 'VIN00000000000002')],
        }
        ..inspecciones.totalRemoto = 120;
      await abrir(tester, conArnes: arnes);

      expect(find.textContaining('Cargar más'), findsOneWidget);
      await tester.tap(find.textContaining('Cargar más'));
      for (var i = 0; i < 6; i++) {
        await tester.pump(const Duration(milliseconds: 50));
      }

      expect(find.text('VIN00000000000002'), findsOneWidget, reason: 'la página siguiente se agrega al final');
      expect(find.textContaining('Cargar más'), findsOneWidget, reason: 'con 2 de 120 todavía falta');
    });
  });

  // #394: la coordinadora ve los informes de todo el mundo. Sin decir de qué viaje es cada uno, la lista es una
  // pila de VINes repetidos. El servidor ya manda estos tres datos; solo había que pintarlos.
  group('cada fila dice de qué viaje es', () {
    // El proceso llega como código («cargue») y se enseña con el nombre del catálogo («Cargue»).
    Arnes conEtapas() => Arnes(sesion: sesionCoordinador);

    List<Override> catalogos() => [
          catalogosRepositorioProvider.overrideWithValue(
            CatalogosRepositorioFalso()..guardado = catalogosCon('Golpe', etapas: etapasDePrueba),
          ),
        ];

    testWidgets('el proceso con su nombre, el código MTrack y el lugar', (tester) async {
      await abrir(
        tester,
        conArnes: conEtapas()
          ..inspecciones.remoto = [
            _resumen('1', 'VIN00000000000001', proceso: 'cargue', codigo: 'MTK-2026-001', lugar: 'CEDI Envigado'),
          ],
        extra: catalogos(),
      );

      expect(find.text('Cargue · MTK-2026-001 · CEDI Envigado'), findsOneWidget);
    });

    // Una inspección suelta no tiene viaje: antes que un renglón vacío o separadores sueltos, nada.
    testWidgets('un informe sin viaje no pinta el renglón', (tester) async {
      await abrir(
        tester,
        conArnes: conEtapas()..inspecciones.remoto = [_resumen('1', 'VIN00000000000001')],
        extra: catalogos(),
      );

      expect(find.textContaining(' · '), findsOneWidget, reason: 'solo el del momento y la fecha');
    });
  });

  testWidgets('al inspector no le cambia nada: su pestaña sigue siendo la suya y sin buscador', (tester) async {
    await abrir(tester, conArnes: Arnes()..inspecciones.remoto = [_resumen('1', 'VIN00000000000001')]);

    expect(find.text('Mis inspecciones'), findsOneWidget);
    expect(find.byType(TextField), findsNothing);
  });
}

InspeccionResumen _resumen(String id, String vin, {String? proceso, String? codigo, String? lugar}) =>
    InspeccionResumen(
      id: id,
      vin: vin,
      momento: 'salida',
      momentoNombre: 'Inspección de salida',
      fecha: '2026-01-0$id' 'T10:00:00Z',
      estado: 'cerrada',
      proceso: proceso,
      codigoMtrack: codigo,
      lugar: lugar,
    );
