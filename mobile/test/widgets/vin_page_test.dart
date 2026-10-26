import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:intl/date_symbol_data_local.dart';
import 'package:traza_autos/core/tema/tema.dart';
import 'package:traza_autos/core/ui/widgets.dart';
import 'package:traza_autos/features/catalogos/providers.dart';
import 'package:traza_autos/features/vehiculos/data/vehiculos_repositorio.dart';
import 'package:traza_autos/features/vehiculos/domain/inventario.dart';
import 'package:traza_autos/features/vehiculos/presentation/vin_page.dart';
import 'package:traza_autos/features/vehiculos/providers.dart';

import '../support/arnes.dart';
import '../support/dobles_catalogos.dart';

/// La pantalla del VIN con su ruta de custodia. Es la que abre el inspector en el patio: si revienta, no hay
/// informe, y en una compilación de release revienta en blanco, sin decir nada.

class VehiculosRepositorioFalso implements VehiculosRepositorio {
  VehiculosRepositorioFalso(this.respuesta);

  final Map<String, dynamic> respuesta;

  @override
  Future<Inventario> inventario(String vin) async => Inventario.fromJson(respuesta);
}

/// El inventario tal como lo sirve la API: dos etapas, la primera con su salida cerrada.
Map<String, dynamic> inventarioJson() => {
      'id': 'v1',
      'vin': 'VIN00000000000001',
      'marca': 'Renault',
      'modelo': 'Kwid',
      'operacion': 'nacional',
      'etapas': [
        {
          'id': 'e1',
          'orden': 1,
          'proceso': 'cargue',
          'lugar': 'CEDI 1',
          'requiere_inspeccion': true,
          'viaje_id': 'viaje-1',
          'con_personal_apoyo': false,
          'inspecciones': [
            {
              'id': 'i1',
              'momento': 'salida',
              'momento_nombre': 'Inspección de salida',
              'fecha': '2026-01-02T10:00:00Z',
              'cerrada_at': '2026-01-02T11:00:00Z',
              'estado': 'cerrada',
              'notas': null,
              'hallazgos': [],
              'evidencias': [],
              'transferencia': null,
            },
          ],
        },
        {
          'id': 'e2',
          'orden': 2,
          'proceso': 'transporte',
          'lugar': null,
          'requiere_inspeccion': true,
          'viaje_id': 'viaje-1',
          'con_personal_apoyo': false,
          'inspecciones': [],
        },
      ],
    };

Future<void> abrir(WidgetTester tester, {Map<String, dynamic>? inventario}) async {
  await tester.binding.setSurfaceSize(const Size(600, 2400));
  addTearDown(() => tester.binding.setSurfaceSize(null));
  await tester.pumpWidget(
    ProviderScope(
      retry: (_, _) => null,
      overrides: [
        ...Arnes().overrides,
        catalogosRepositorioProvider.overrideWithValue(CatalogosRepositorioFalso()..guardado = catalogosCon('Golpe')),
        vehiculosRepositorioProvider.overrideWithValue(VehiculosRepositorioFalso(inventario ?? inventarioJson())),
      ],
      child: MaterialApp(theme: crearTema(), home: const VinPage(vin: 'VIN00000000000001')),
    ),
  );
  for (var i = 0; i < 10; i++) {
    await tester.pump(const Duration(milliseconds: 50));
  }
}

void main() {
  setUpAll(() => initializeDateFormatting('es_CO'));

  testWidgets('dibuja la ruta de custodia con sus dos momentos por etapa', (tester) async {
    await abrir(tester);

    expect(find.text('CEDI 1'), findsOneWidget);
    expect(find.text('Inspección de salida'), findsWidgets);
    expect(find.text('Inspección de llegada'), findsWidgets);
  });

  testWidgets('la salida de la etapa siguiente espera a la anterior', (tester) async {
    await abrir(tester);

    expect(find.textContaining('Falta cerrar la llegada de'), findsOneWidget);
  });

  // El defecto visto en el celular: la captura de un transporte pedía las 7 fotos obligatorias, que es lo que
  // ese proceso NO pide. La ruta llevaba la etiqueta del catálogo («Transporte») y `pideChecklist` compara
  // contra el código («transporte»), así que nunca coincidían.
  testWidgets('la captura recibe el código del proceso, no su etiqueta', (tester) async {
    final abiertas = <Uri>[];
    final router = GoRouter(
      initialLocation: '/vin',
      routes: [
        GoRoute(path: '/vin', builder: (_, _) => const VinPage(vin: 'VIN00000000000001')),
        GoRoute(
          path: '/inspecciones/vin/:vin/captura/:etapaId',
          builder: (_, state) {
            abiertas.add(state.uri);
            return const Scaffold(body: Text('captura'));
          },
        ),
      ],
    );
    await tester.binding.setSurfaceSize(const Size(600, 2400));
    addTearDown(() => tester.binding.setSurfaceSize(null));
    await tester.pumpWidget(
      ProviderScope(
        retry: (_, _) => null,
        overrides: [
          ...Arnes().overrides,
          catalogosRepositorioProvider.overrideWithValue(
            CatalogosRepositorioFalso()..guardado = catalogosCon('Golpe', etapas: etapasDePrueba),
          ),
          vehiculosRepositorioProvider.overrideWithValue(VehiculosRepositorioFalso(inventarioJson())),
        ],
        child: MaterialApp.router(theme: crearTema(), routerConfig: router),
      ),
    );
    for (var i = 0; i < 10; i++) {
      await tester.pump(const Duration(milliseconds: 50));
    }

    // La llegada del cargue: la única capturable de esa etapa, porque su salida ya está cerrada.
    await tester.tap(find.widgetWithText(BotonEnFila, 'Capturar').first);
    await tester.pumpAndSettle();

    expect(abiertas.single.queryParameters['proceso'], 'cargue', reason: 'el código, no «Cargue»');
  });
}
