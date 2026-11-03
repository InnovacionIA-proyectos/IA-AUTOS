import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:intl/date_symbol_data_local.dart';
import 'package:traza_autos/core/dispositivo/servicios.dart';
import 'package:traza_autos/core/tema/tema.dart';
import 'package:traza_autos/features/auth/domain/sesion.dart';
import 'package:traza_autos/features/catalogos/providers.dart';
import 'package:traza_autos/features/custodia/presentation/lienzo_firma.dart';
import 'package:traza_autos/features/vehiculos/data/vehiculos_repositorio.dart';
import 'package:traza_autos/features/vehiculos/domain/inventario.dart';
import 'package:traza_autos/features/vehiculos/presentation/vin_page.dart';
import 'package:traza_autos/features/vehiculos/providers.dart';
import 'package:traza_autos/features/viajes/providers.dart';

import '../support/arnes.dart';
import '../support/dobles_catalogos.dart';
import '../support/dobles_viajes.dart';

/// S6-HU-02.18: «No hay inspector» en el cargue, «Firmar recibo sin entrega» y la novedad de proceso en la ficha.

class _InventarioFalso implements VehiculosRepositorio {
  _InventarioFalso(this.respuesta);

  final Map<String, dynamic> respuesta;

  @override
  Future<Inventario> inventario(String vin) async => Inventario.fromJson(respuesta);
}

const _conductor = Sesion(
  token: 'jwt',
  usuario: Usuario(id: 'conductor-1', nombre: 'Luis Gómez', email: '', rol: 'conductor'),
);

Map<String, dynamic> _salidaSinInspector({String? tipoCierre, bool cerrada = true}) => {
      'id': 'i1',
      'momento': 'salida',
      'momento_nombre': 'Inspección de salida',
      'fecha': '2026-10-23T08:30:00Z',
      'cerrada_at': cerrada ? '2026-10-23T09:00:00Z' : null,
      'estado': cerrada ? 'cerrada' : 'borrador',
      'usuario_id': 'conductor-1',
      'hallazgos': [],
      'evidencias': [],
      'transferencia': cerrada
          ? {
              'entrega_usuario_id': tipoCierre == null ? null : '',
              'recibe_usuario_id': 'conductor-1',
              'tipo_cierre': tipoCierre,
            }
          : null,
      'motivo_sin_inspector': {'codigo': 'otro', 'nombre': 'Otro', 'texto': 'El inspector se enfermó'},
      'novedades_proceso': [
        {'tipo': 'cargue_sin_inspector', 'motivo_codigo': 'otro', 'motivo': 'Otro', 'texto': 'El inspector se enfermó', 'fecha': '2026-10-23T08:30:00Z'},
      ],
    };

Map<String, dynamic> _inventario({bool conApoyo = true, List<Map<String, dynamic>> inspecciones = const []}) => {
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
          'con_personal_apoyo': conApoyo,
          'inspecciones': inspecciones,
        },
      ],
    };

Future<Arnes> abrir(WidgetTester tester, {Sesion? sesion, Map<String, dynamic>? inventario, List<Uri>? capturas}) async {
  await tester.binding.setSurfaceSize(const Size(600, 2400));
  addTearDown(() => tester.binding.setSurfaceSize(null));
  final arnes = Arnes(sesion: sesion ?? _conductor);
  final router = GoRouter(
    initialLocation: '/vin',
    routes: [
      GoRoute(path: '/vin', builder: (_, _) => const VinPage(vin: 'VIN00000000000001')),
      GoRoute(
        path: '/inspecciones/vin/:vin/captura/:etapaId',
        builder: (_, state) {
          capturas?.add(state.uri);
          return const Scaffold(body: Text('captura'));
        },
      ),
    ],
  );
  await tester.pumpWidget(
    ProviderScope(
      retry: (_, _) => null,
      overrides: [
        ...arnes.overrides,
        catalogosRepositorioProvider.overrideWithValue(
          CatalogosRepositorioFalso()..guardado = catalogosCon('Golpe', motivos: motivosDePrueba),
        ),
        vehiculosRepositorioProvider.overrideWithValue(_InventarioFalso(inventario ?? _inventario())),
        viajesRepositorioProvider.overrideWithValue(ViajesRepositorioFalso()),
      ],
      child: MaterialApp.router(theme: crearTema(), routerConfig: router),
    ),
  );
  await avanzar(tester);
  return arnes;
}

Future<void> avanzar(WidgetTester tester) async {
  for (var i = 0; i < 12; i++) {
    await tester.pump(const Duration(milliseconds: 50));
  }
}

void main() {
  setUpAll(() => initializeDateFormatting('es_CO'));

  group('«No hay inspector» en la salida del cargue', () {
    testWidgets('el conductor ve «No hay inspector» en la salida y «Capturar» en la llegada', (tester) async {
      await abrir(tester);

      expect(find.widgetWithText(OutlinedButton, 'No hay inspector'), findsOneWidget);
      expect(find.widgetWithText(OutlinedButton, 'Capturar'), findsOneWidget, reason: 'solo la llegada');
    });

    testWidgets('sin la marca de apoyo en la respuesta (contrato 2.10) también', (tester) async {
      final json = _inventario();
      ((json['etapas'] as List<dynamic>).first as Map<String, dynamic>).remove('con_personal_apoyo');
      await abrir(tester, inventario: json);

      expect(find.widgetWithText(OutlinedButton, 'No hay inspector'), findsOneWidget);
    });

    testWidgets('el inspector sigue capturando: a él le toca la salida', (tester) async {
      await abrir(tester, sesion: sesionPrueba);

      expect(find.widgetWithText(OutlinedButton, 'No hay inspector'), findsNothing);
      expect(find.widgetWithText(OutlinedButton, 'Capturar'), findsNWidgets(2));
    });

    testWidgets('con la marca en falso el conductor captura como antes', (tester) async {
      await abrir(tester, inventario: _inventario(conApoyo: false));

      expect(find.widgetWithText(OutlinedButton, 'No hay inspector'), findsNothing);
      expect(find.widgetWithText(OutlinedButton, 'Capturar'), findsNWidgets(2));
    });

    testWidgets('sin elegir un motivo no se puede seguir, y se avisa que se registrará', (tester) async {
      await abrir(tester);

      await tester.tap(find.widgetWithText(OutlinedButton, 'No hay inspector'));
      await avanzar(tester);

      expect(find.text('El inspector no llegó'), findsOneWidget);
      expect(find.text('Otro'), findsOneWidget);
      expect(find.text('Se registrará y se avisará al coordinador.'), findsOneWidget);
      final boton = tester.widget<FilledButton>(find.widgetWithText(FilledButton, 'Continuar con la salida del cargue'));
      expect(boton.onPressed, isNull);
    });

    testWidgets('«Otro» exige escribir cuál; los demás motivos no piden texto', (tester) async {
      await abrir(tester);
      await tester.tap(find.widgetWithText(OutlinedButton, 'No hay inspector'));
      await avanzar(tester);

      await tester.tap(find.text('El inspector no llegó'));
      await tester.pump();
      expect(find.byType(TextField), findsNothing);

      await tester.tap(find.text('Otro'));
      await tester.pump();
      expect(find.byType(TextField), findsOneWidget);
      expect(tester.widget<FilledButton>(find.widgetWithText(FilledButton, 'Continuar con la salida del cargue')).onPressed, isNull);

      await tester.enterText(find.byType(TextField), '   ');
      await tester.pump();
      expect(tester.widget<FilledButton>(find.widgetWithText(FilledButton, 'Continuar con la salida del cargue')).onPressed, isNull,
          reason: 'espacios no son un motivo');

      await tester.enterText(find.byType(TextField), 'El inspector se enfermó');
      await tester.pump();
      expect(tester.widget<FilledButton>(find.widgetWithText(FilledButton, 'Continuar con la salida del cargue')).onPressed, isNotNull);
    });

    testWidgets('con el motivo sigue a la captura de la salida llevando el motivo, la fecha y el lugar del momento', (tester) async {
      final capturas = <Uri>[];
      final arnes = await abrir(tester, capturas: capturas);
      arnes.ubicacion.posicion = const Posicion(6.2442, -75.5812, precision: 5);

      await tester.tap(find.widgetWithText(OutlinedButton, 'No hay inspector'));
      await avanzar(tester);
      await tester.tap(find.text('Otro'));
      await tester.pump();
      await tester.enterText(find.byType(TextField), 'El inspector se enfermó');
      await tester.pump();
      await tester.tap(find.widgetWithText(FilledButton, 'Continuar con la salida del cargue'));
      await avanzar(tester);

      final ruta = capturas.single;
      expect(ruta.queryParameters['proceso'], 'cargue');
      expect(ruta.queryParameters['momento'], 'salida');
      expect(ruta.queryParameters['motivo'], 'otro');
      expect(ruta.queryParameters['motivoTexto'], 'El inspector se enfermó');
      expect(ruta.queryParameters['latMotivo'], '6.2442');
      expect(ruta.queryParameters['lngMotivo'], '-75.5812');
      expect(DateTime.tryParse(ruta.queryParameters['fechaMotivo']!), isNotNull);
    });
  });

  group('recibo sin entrega', () {
    testWidgets('con la salida hecha sin inspector, en vez de esperar la entrega se ofrece «Firmar recibo sin entrega»', (tester) async {
      await abrir(tester, inventario: _inventario(inspecciones: [_salidaSinInspector()]));

      expect(find.widgetWithText(FilledButton, 'Firmar recibo sin entrega'), findsOneWidget);
      expect(find.widgetWithText(FilledButton, 'Firmar transferencia'), findsNothing);
    });

    testWidgets('una salida normal sigue ofreciendo firmar la transferencia', (tester) async {
      final normal = {..._salidaSinInspector(), 'motivo_sin_inspector': null, 'novedades_proceso': []};
      await abrir(tester, inventario: _inventario(inspecciones: [normal]));

      expect(find.widgetWithText(FilledButton, 'Firmar recibo sin entrega'), findsNothing);
    });

    testWidgets('quien inspecciona no ve el recibo sin entrega: es del conductor', (tester) async {
      await abrir(tester, sesion: sesionPrueba, inventario: _inventario(inspecciones: [_salidaSinInspector()]));

      expect(find.widgetWithText(FilledButton, 'Firmar recibo sin entrega'), findsNothing);
    });

    testWidgets('firmar encola el recibo con su dueño y lo envía', (tester) async {
      final arnes = await abrir(tester, inventario: _inventario(inspecciones: [_salidaSinInspector()]));

      await tester.tap(find.widgetWithText(FilledButton, 'Firmar recibo sin entrega'));
      await avanzar(tester);
      final boton = tester.widget<FilledButton>(find.widgetWithText(FilledButton, 'Firmar recibo sin entrega'));
      expect(boton.onPressed, isNull, reason: 'sin trazo no hay nada que registrar');

      final lienzo = tester.getBottomLeft(find.byType(LienzoFirma)) + const Offset(60, -80);
      await tester.dragFrom(lienzo, const Offset(40, 30));
      await tester.pump();
      await tester.tap(find.widgetWithText(FilledButton, 'Firmar recibo sin entrega'));
      await avanzar(tester);

      expect(arnes.gateway.recibosSinEntrega.single.$1, 'i1');
      expect(arnes.gateway.recibosSinEntrega.single.$2, isNotEmpty);
      expect(find.text('Recibo sin entrega'), findsWidgets);
    });

    testWidgets('ya cerrada, la tarjeta dice «Recibo sin entrega» y no ofrece firmar', (tester) async {
      await abrir(
        tester,
        inventario: _inventario(inspecciones: [_salidaSinInspector(tipoCierre: 'recibo_sin_entrega')]),
      );

      expect(find.widgetWithText(FilledButton, 'Firmar recibo sin entrega'), findsNothing);
      expect(find.text('Recibo sin entrega'), findsOneWidget);
    });
  });

  testWidgets('la ficha de la unidad muestra la novedad de proceso con su motivo', (tester) async {
    await abrir(tester, inventario: _inventario(inspecciones: [_salidaSinInspector()]));

    expect(find.text('Cargue sin inspector · Otro: El inspector se enfermó'), findsOneWidget);
  });
}
