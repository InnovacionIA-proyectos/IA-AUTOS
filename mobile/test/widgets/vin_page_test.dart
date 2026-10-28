import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:intl/date_symbol_data_local.dart';
import 'package:traza_autos/core/tema/tema.dart';
import 'package:traza_autos/core/ui/widgets.dart';
import 'package:traza_autos/features/auth/domain/sesion.dart';
import 'package:traza_autos/features/catalogos/providers.dart';
import 'package:traza_autos/features/dibujo_dano/presentation/selector_zona_dano.dart';
import 'package:traza_autos/features/vehiculos/data/vehiculos_repositorio.dart';
import 'package:traza_autos/features/vehiculos/domain/inventario.dart';
import 'package:traza_autos/features/vehiculos/presentation/vin_page.dart';
import 'package:traza_autos/features/vehiculos/providers.dart';
import 'package:traza_autos/features/viajes/domain/viaje.dart';
import 'package:traza_autos/features/viajes/providers.dart';

import '../support/arnes.dart';
import '../support/dobles_catalogos.dart';
import '../support/dobles_viajes.dart';

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
              // Lo registró el inspector del viaje, no quien mira.
              'usuario_id': 'inspector-1',
              'notas': 'Llovía al cargar',
              'hallazgos': [
                {'id': 'h1', 'parte_vehiculo': 'Capot', 'clase_dano': 'golpe', 'tamano_dano': 'mas_20cm'},
              ],
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

/// Quien está en sesión. Por defecto la inspectora del arnés; las pruebas del recibo entran como conductor.
const conductor = Sesion(
  token: 'jwt',
  usuario: Usuario(id: 'conductor-1', nombre: 'Luis Gómez', email: '', rol: 'conductor'),
);

Future<void> abrir(
  WidgetTester tester, {
  Map<String, dynamic>? inventario,
  Sesion? sesion,
  ViajesRepositorioFalso? viajes,
}) async {
  await tester.binding.setSurfaceSize(const Size(600, 2400));
  addTearDown(() => tester.binding.setSurfaceSize(null));
  await tester.pumpWidget(
    ProviderScope(
      retry: (_, _) => null,
      overrides: [
        ...Arnes(sesion: sesion ?? sesionPrueba).overrides,
        catalogosRepositorioProvider.overrideWithValue(CatalogosRepositorioFalso()..guardado = catalogosCon('Golpe')),
        vehiculosRepositorioProvider.overrideWithValue(VehiculosRepositorioFalso(inventario ?? inventarioJson())),
        viajesRepositorioProvider.overrideWithValue(viajes ?? ViajesRepositorioFalso()),
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

  // S5-HU-25.4: el conductor recibe el vehículo y firma su recibo. Si antes ve lo que marcó el inspector, su
  // recibo deja de decir lo que él encontró y pasa a confirmar lo del otro.
  group('la tarjeta del informe cuando mira un conductor', () {
    testWidgets('no muestra los hallazgos del inspector ni su dibujo de daños', (tester) async {
      await abrir(tester, sesion: conductor);

      expect(find.textContaining('Capot'), findsNothing);
      expect(find.byType(SelectorZonaDano), findsNothing);
      expect(find.text('Llovía al cargar'), findsNothing);
      expect(find.textContaining('queda en tu firma'), findsOneWidget, reason: 'se dice por qué, no se calla');
    });

    testWidgets('sí muestra el informe que registró él mismo', (tester) async {
      final mio = inventarioJson();
      (((mio['etapas'] as List<dynamic>).first as Map<String, dynamic>)['inspecciones'] as List<dynamic>)
          .cast<Map<String, dynamic>>()
          .first['usuario_id'] = conductor.usuario.id;

      await abrir(tester, inventario: mio, sesion: conductor);

      expect(find.textContaining('Capot'), findsOneWidget);
      expect(find.text('Llovía al cargar'), findsOneWidget);
    });

    testWidgets('quien inspecciona lo sigue viendo entero', (tester) async {
      await abrir(tester);

      expect(find.textContaining('Capot'), findsOneWidget);
      expect(find.byType(SelectorZonaDano), findsOneWidget);
    });
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

  group('la unidad no sale', () {
    /// Inventario con un V1+ en una inspección cerrada del viaje: la única forma de que el servidor lo acepte.
    Map<String, dynamic> conV1Plus() {
      final json = inventarioJson();
      final etapa = (json['etapas'] as List).first as Map<String, dynamic>;
      final inspeccion = (etapa['inspecciones'] as List).first as Map<String, dynamic>;
      inspeccion['hallazgos'] = <Map<String, dynamic>>[
        {'id': 'h1', 'zona': 'Izquierdo 7 · Espejo retrovisor', 'cuadrante_numero': 7, 'lado': 'izquierdo',
         'clase_dano': 'golpe', 'tamano_dano': 'mas_20cm', 'nivel_severidad': 'v1_plus'},
      ];
      return json;
    }

    ViajesRepositorioFalso delInspector(String inspectorId, {bool noSale = false}) => ViajesRepositorioFalso()
      ..viajeDeEtapa = Viaje(
        id: 'viaje-1',
        codigoMtrack: 'MTK-1',
        placa: 'ABC123',
        estado: 'en_curso',
        inspectorId: inspectorId,
        vehiculos: [
          ViajeVehiculo(
            vin: 'VIN00000000000001',
            estadoUnidad: noSale ? 'no_sale' : estadoUnidadActiva,
            noSale: noSale ? const NoSale(motivo: 'Golpe profundo en el capó', vinReemplazo: 'VIN00000000000002') : null,
          ),
        ],
      );

    testWidgets('se le ofrece al inspector del viaje con un V1+ cerrado', (tester) async {
      await abrir(tester, inventario: conV1Plus(), viajes: delInspector('u1'));

      expect(find.text('La unidad no sale'), findsOneWidget);
    });

    testWidgets('a un inspector que no es el del viaje, no', (tester) async {
      await abrir(tester, inventario: conV1Plus(), viajes: delInspector('otro-inspector'));

      expect(find.text('La unidad no sale'), findsNothing);
    });

    testWidgets('al conductor, nunca', (tester) async {
      await abrir(tester, inventario: conV1Plus(), sesion: conductor, viajes: delInspector('conductor-1'));

      expect(find.text('La unidad no sale'), findsNothing);
    });

    testWidgets('sin un V1+ no hay nada que marcar', (tester) async {
      await abrir(tester, viajes: delInspector('u1'));

      expect(find.text('La unidad no sale'), findsNothing);
    });

    testWidgets('una unidad ya marcada dice por qué, en vez del botón', (tester) async {
      await abrir(tester, inventario: conV1Plus(), viajes: delInspector('u1', noSale: true));

      expect(find.text('La unidad no sale'), findsNothing);
      expect(find.text('Esta unidad no sale'), findsOneWidget);
      expect(find.text('Golpe profundo en el capó'), findsOneWidget);
      expect(find.text('La reemplaza VIN00000000000002'), findsOneWidget);
    });
  });
}
