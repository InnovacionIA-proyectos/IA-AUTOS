import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intl/date_symbol_data_local.dart';
import 'package:traza_autos/core/dispositivo/servicios.dart';
import 'package:traza_autos/core/errores/error_api.dart';
import 'package:traza_autos/core/tema/tema.dart';
import 'package:traza_autos/features/auth/domain/sesion.dart';
import 'package:traza_autos/features/auth/presentation/auth_controller.dart';
import 'package:traza_autos/features/catalogos/domain/catalogos.dart';
import 'package:traza_autos/features/catalogos/providers.dart';
import 'package:traza_autos/features/sync/domain/job.dart';
import 'package:traza_autos/features/vehiculos/data/vehiculos_repositorio.dart';
import 'package:traza_autos/features/vehiculos/domain/inventario.dart';
import 'package:traza_autos/features/vehiculos/providers.dart';
import 'package:traza_autos/features/viajes/domain/novedad_viaje.dart';
import 'package:traza_autos/features/viajes/domain/pendiente.dart';
import 'package:traza_autos/features/viajes/domain/viaje.dart';
import 'package:traza_autos/features/viajes/presentation/mis_viajes_page.dart';
import 'package:traza_autos/features/viajes/presentation/novedad_viaje.dart';
import 'package:traza_autos/features/viajes/providers.dart';

import '../support/arnes.dart';
import '../support/dobles_catalogos.dart';
import '../support/dobles_viajes.dart';

/// S8-HU-07.3 CA-1 a CA-3 y CA-10: «Novedad del viaje». El conductor la ve en la tarjeta de su viaje y el inspector en
/// la del camión que reclamó; la hoja ofrece los tipos de su rol, pide el texto y una foto opcional, y la novedad va
/// por la cola con la hora y el GPS del momento.

const _conductor = Sesion(
  token: 'jwt',
  usuario: Usuario(id: 'conductor-1', nombre: 'Luis Gómez', email: '', rol: 'conductor'),
);

const _admin = Sesion(
  token: 'jwt',
  usuario: Usuario(id: 'admin-1', nombre: 'Marta Ruiz', email: '', rol: 'admin'),
);

class _SinInventario implements VehiculosRepositorio {
  @override
  Future<List<VehiculoBusqueda>> buscar(String termino) async => const [];

  @override
  Future<Inventario> inventario(String vin) async => throw const ErrorApi.red();
}

class _Arranque extends ConsumerWidget {
  const _Arranque({this.hijo = const MisViajesPage()});

  final Widget hijo;

  @override
  Widget build(BuildContext context, WidgetRef ref) =>
      ref.watch(authControllerProvider.select((e) => e.fase)) == FaseAuth.autenticado ? Scaffold(body: hijo) : const SizedBox();
}

Future<void> avanzar(WidgetTester tester) async {
  for (var i = 0; i < 12; i++) {
    await tester.pump(const Duration(milliseconds: 50));
  }
}

Future<Arnes> abrir(
  WidgetTester tester, {
  Sesion sesion = _conductor,
  List<Viaje> viajes = const [],
  List<Pendiente> pendientes = const [],
  List<Map<String, dynamic>> tipos = tiposNovedadViajeDePrueba,
  Arnes? arnes,
  Widget? hijo,
}) async {
  await tester.binding.setSurfaceSize(const Size(600, 3000));
  addTearDown(() => tester.binding.setSurfaceSize(null));
  final usado = arnes ?? Arnes(sesion: sesion);
  await tester.pumpWidget(
    ProviderScope(
      retry: (_, _) => null,
      overrides: [
        ...usado.overrides,
        viajesRepositorioProvider.overrideWithValue(ViajesRepositorioFalso()..remotos = viajes),
        vehiculosRepositorioProvider.overrideWithValue(_SinInventario()),
        reclamosRepositorioProvider.overrideWithValue(ReclamosRepositorioFalso()..pendientesActuales = pendientes),
        catalogosRepositorioProvider.overrideWithValue(
          CatalogosRepositorioFalso()..guardado = catalogosCon('Golpe', tiposNovedadViaje: tipos),
        ),
      ],
      child: MaterialApp(theme: crearTema(), home: _Arranque(hijo: hijo ?? const MisViajesPage())),
    ),
  );
  await avanzar(tester);
  return usado;
}

Finder get _boton => find.widgetWithText(OutlinedButton, 'Novedad del viaje');

/// Elige el tipo, escribe qué pasó y guarda.
Future<void> llenarYGuardar(WidgetTester tester, {String tipo = 'Bloqueo de vía', String texto = 'Paro en el peaje'}) async {
  await tester.tap(find.text(tipo));
  await tester.pump();
  await tester.enterText(find.byType(TextField), texto);
  await tester.pump();
  await tester.tap(find.widgetWithText(FilledButton, 'Guardar novedad'));
  await avanzar(tester);
}

void main() {
  setUpAll(() => initializeDateFormatting('es_CO'));

  group('CA-1: quién la ve', () {
    testWidgets('el conductor la ve en la tarjeta de su viaje mientras el viaje admita trabajo', (tester) async {
      await abrir(tester, viajes: [viaje('v1', vines: const ['VIN00000000000001'], conductorId: 'conductor-1')]);

      expect(_boton, findsOneWidget, reason: 'una por viaje, no una por carro');
    });

    testWidgets('en un viaje programado también', (tester) async {
      await abrir(tester, viajes: [viaje('v1', vines: const ['VIN00000000000001'], estado: 'programado')]);

      expect(_boton, findsOneWidget);
    });

    testWidgets('en un viaje terminado no', (tester) async {
      await abrir(tester, viajes: [viaje('v1', vines: const ['VIN00000000000001'], estado: 'finalizado')]);

      expect(_boton, findsNothing);
    });

    testWidgets('el inspector del viaje la ve en su tarjeta, para el cargue', (tester) async {
      final arnes = await abrir(
        tester,
        sesion: sesionPrueba,
        viajes: [viaje('v1', vines: const ['VIN00000000000001'], inspectorId: 'u1')],
      );

      await tester.tap(_boton);
      await avanzar(tester);
      await llenarYGuardar(tester, tipo: 'Carro no llegó al patio', texto: 'El carro del puesto 9 no está en el patio');

      final enviada = arnes.gateway.novedadesViajeEnviadas.single;
      expect((enviada.proceso, enviada.lugarId), ('cargue', null));
    });

    testWidgets('el inspector que tomó una entrega la manda con el proceso y la parada', (tester) async {
      final arnes = await abrir(
        tester,
        sesion: sesionPrueba,
        pendientes: [
          pendienteDe('v-ent', proceso: procesoEntrega, estado: EstadoPendiente.mio, lugar: 'Puerto de Cartagena', lugarId: 'parada-1'),
        ],
      );

      await tester.tap(_boton);
      await avanzar(tester);
      await llenarYGuardar(tester, tipo: 'Otro', texto: 'Llegó 3 horas después de la cita');

      final enviada = arnes.gateway.novedadesViajeEnviadas.single;
      expect((enviada.viajeId, enviada.proceso, enviada.lugarId), ('v-ent', 'entrega', 'parada-1'));
    });

    testWidgets('en la pantalla del camión que reclamó también la ve', (tester) async {
      await abrir(
        tester,
        sesion: sesionPrueba,
        pendientes: [
          pendienteDe('v-ent', proceso: procesoEntrega, estado: EstadoPendiente.mio, lugar: 'Puerto de Cartagena', lugarId: 'parada-1'),
        ],
      );

      await tester.tap(find.widgetWithText(FilledButton, 'Ver unidades'));
      await avanzar(tester);

      expect(find.text('Entrega por revisar'), findsOneWidget);
      expect(_boton, findsOneWidget);
    });

    for (final (quien, sesion) in [('el coordinador', sesionCoordinador), ('el administrador', _admin)]) {
      testWidgets('$quien no la ve: el servidor le responde 403', (tester) async {
        await abrir(tester, sesion: sesion, hijo: const BotonNovedadViaje(viajeId: 'v1'));

        expect(_boton, findsNothing);
      });
    }
  });

  group('CA-2: la hoja', () {
    testWidgets('ofrece solo los tipos activos del rol del conductor, por su nombre', (tester) async {
      await abrir(tester, viajes: [viaje('v1', vines: const ['VIN00000000000001'])]);

      await tester.tap(_boton);
      await avanzar(tester);

      for (final suyo in ['Falla del camión', 'Bloqueo de vía', 'Otro']) {
        expect(find.widgetWithText(ChoiceChip, suyo), findsOneWidget, reason: suyo);
      }
      for (final ajeno in ['Carro no llegó al patio', 'Faltan documentos', 'Accidente']) {
        expect(find.widgetWithText(ChoiceChip, ajeno), findsNothing, reason: ajeno);
      }
    });

    testWidgets('al inspector, los suyos', (tester) async {
      await abrir(tester, sesion: sesionPrueba, viajes: [viaje('v1', vines: const ['VIN00000000000001'], inspectorId: 'u1')]);

      await tester.tap(_boton);
      await avanzar(tester);

      expect(find.widgetWithText(ChoiceChip, 'Faltan documentos'), findsOneWidget);
      expect(find.widgetWithText(ChoiceChip, 'Falla del camión'), findsNothing);
    });

    testWidgets('el texto es obligatorio, con su contador de 300', (tester) async {
      await abrir(tester, viajes: [viaje('v1', vines: const ['VIN00000000000001'])]);
      await tester.tap(_boton);
      await avanzar(tester);

      Finder guardar() => find.widgetWithText(FilledButton, 'Guardar novedad');
      expect(find.text('0/300'), findsOneWidget);
      expect(tester.widget<FilledButton>(guardar()).onPressed, isNull);

      await tester.tap(find.text('Falla del camión'));
      await tester.pump();
      expect(tester.widget<FilledButton>(guardar()).onPressed, isNull, reason: 'falta qué pasó');

      await tester.enterText(find.byType(TextField), '   ');
      await tester.pump();
      expect(tester.widget<FilledButton>(guardar()).onPressed, isNull, reason: 'espacios no dicen nada');

      await tester.enterText(find.byType(TextField), 'Se reventó una llanta en la vía');
      await tester.pump();
      expect(tester.widget<FilledButton>(guardar()).onPressed, isNotNull);
      expect(find.widgetWithText(OutlinedButton, 'Agregar foto (opcional)'), findsOneWidget);
    });

    testWidgets('sin el catálogo descargado lo dice y no abre la hoja', (tester) async {
      await abrir(tester, tipos: const [], viajes: [viaje('v1', vines: const ['VIN00000000000001'])]);

      await tester.tap(_boton);
      await avanzar(tester);

      expect(find.text('Los catálogos aún no se han descargado. Conéctate e inténtalo de nuevo.'), findsOneWidget);
      expect(find.text('¿Qué pasó?'), findsNothing);
    });

    // C14 (ESC-13): sin GPS se envía igual, y la hoja lo dice antes de guardar.
    testWidgets('sin GPS dice «Se registrará sin ubicación» y se envía sin ubicación', (tester) async {
      final arnes = Arnes(sesion: _conductor)..ubicacion.error = const UbicacionNoDisponible('GPS apagado');
      await abrir(tester, arnes: arnes, viajes: [viaje('v1', vines: const ['VIN00000000000001'])]);

      await tester.tap(_boton);
      await avanzar(tester);
      expect(find.text('Se registrará sin ubicación'), findsOneWidget);
      await llenarYGuardar(tester, tipo: 'Otro', texto: 'Intentaron abrir la niñera en la parada');

      final enviada = arnes.gateway.novedadesViajeEnviadas.single;
      expect((enviada.lat, enviada.lng), (null, null));
      expect(find.text('Novedad del viaje registrada'), findsOneWidget);
    });

    testWidgets('con GPS no lo dice', (tester) async {
      await abrir(tester, viajes: [viaje('v1', vines: const ['VIN00000000000001'])]);

      await tester.tap(_boton);
      await avanzar(tester);

      expect(find.text('Se registrará sin ubicación'), findsNothing);
    });

    // Revisión de S8-HU-07.3: la lectura de la hoja puede ser de horas antes; al guardar vale la de ese momento.
    testWidgets('una lectura vieja del GPS no se usa: al guardar se toma la de ese momento', (tester) async {
      final arnes = Arnes(sesion: _conductor)
        ..ubicacion.posicion = Posicion(6.2442, -75.5812, tomadaEn: DateTime.now().subtract(const Duration(hours: 6)));
      await abrir(tester, arnes: arnes, viajes: [viaje('v1', vines: const ['VIN00000000000001'])]);

      await tester.tap(_boton);
      await avanzar(tester);
      // El camión ya está en otro sitio: la lectura que tiene la hoja es la de la mañana.
      arnes.ubicacion.posicion = Posicion(4.7, -74.2, tomadaEn: DateTime.now());
      await llenarYGuardar(tester);

      final enviada = arnes.gateway.novedadesViajeEnviadas.single;
      expect((enviada.lat, enviada.lng), (4.7, -74.2));
    });

    testWidgets('si al guardar no llega la lectura del momento, lo dice y el siguiente toque la guarda sin ubicación', (tester) async {
      final arnes = Arnes(sesion: _conductor)
        ..ubicacion.posicion = Posicion(6.2442, -75.5812, tomadaEn: DateTime.now().subtract(const Duration(hours: 6)));
      await abrir(tester, arnes: arnes, viajes: [viaje('v1', vines: const ['VIN00000000000001'])]);

      await tester.tap(_boton);
      await avanzar(tester);
      expect(find.text('Se registrará sin ubicación'), findsNothing, reason: 'la hoja todavía tiene una lectura');
      arnes.ubicacion.error = const UbicacionNoDisponible('GPS apagado');
      await llenarYGuardar(tester);

      expect(find.text('Se registrará sin ubicación'), findsOneWidget);
      expect(arnes.cola.jobs, isEmpty, reason: 'no se guardó sin decirlo');
      expect(arnes.gateway.novedadesViajeEnviadas, isEmpty);

      await tester.tap(find.widgetWithText(FilledButton, 'Guardar sin ubicación'));
      await avanzar(tester);

      final enviada = arnes.gateway.novedadesViajeEnviadas.single;
      expect((enviada.lat, enviada.lng), (null, null), reason: 'nunca la de la mañana');
    });
  });

  group('CA-3: por la cola', () {
    testWidgets('con señal sale con la hora y el GPS de cuando se guardó, y el id del envío', (tester) async {
      final arnes = Arnes(sesion: _conductor)..ubicacion.posicion = Posicion(4.7, -74.2, tomadaEn: DateTime.now());
      await abrir(tester, arnes: arnes, viajes: [viaje('v1', vines: const ['VIN00000000000001'])]);
      final antes = DateTime.now().toUtc();

      await tester.tap(_boton);
      await avanzar(tester);
      await llenarYGuardar(tester);

      final enviada = arnes.gateway.novedadesViajeEnviadas.single;
      expect((enviada.viajeId, enviada.tipo, enviada.texto), ('v1', 'bloqueo_via', 'Paro en el peaje'));
      expect((enviada.lat, enviada.lng, enviada.proceso, enviada.lugarId), (4.7, -74.2, null, null));
      expect(DateTime.parse(enviada.fecha).isBefore(antes.subtract(const Duration(seconds: 1))), isFalse);
      expect(enviada.idEnvio, isNotNull);
      expect(arnes.cola.jobs, isEmpty);
      expect(find.text('Novedad del viaje registrada'), findsOneWidget);
    });

    // ESC-05: sin señal queda en la cola, a nombre de quien la reportó, con la hora del momento.
    testWidgets('sin señal queda en la cola con la hora del momento y lo dice', (tester) async {
      final arnes = Arnes(sesion: _conductor)..red.estable = false;
      await abrir(tester, arnes: arnes, viajes: [viaje('v1', vines: const ['VIN00000000000001'])]);

      await tester.tap(_boton);
      await avanzar(tester);
      await llenarYGuardar(tester);

      final job = arnes.cola.jobs.single as NovedadViajeJob;
      expect((job.usuarioId, job.viajeId, job.tipoNovedad, job.texto), ('conductor-1', 'v1', 'bloqueo_via', 'Paro en el peaje'));
      expect(DateTime.tryParse(job.fecha), isNotNull);
      expect(arnes.gateway.novedadesViajeEnviadas, isEmpty);
      expect(find.text('En cola: se enviará cuando haya señal'), findsOneWidget);
    });

    testWidgets('un rechazo se dice con el mensaje del servidor, nunca con un código', (tester) async {
      final arnes = Arnes(sesion: _conductor);
      arnes.gateway.errores['registrarNovedadViaje'] = [
        const ErrorApi(
          message: 'Datos inválidos',
          code: 'VALIDATION_ERROR',
          status: 400,
          details: {
            'fieldErrors': {
              'tipo': ['Ese tipo de novedad no es para tu rol'],
            },
          },
        ),
      ];
      await abrir(tester, arnes: arnes, viajes: [viaje('v1', vines: const ['VIN00000000000001'])]);

      await tester.tap(_boton);
      await avanzar(tester);
      await llenarYGuardar(tester);

      expect(find.textContaining('Ese tipo de novedad no es para tu rol'), findsOneWidget);
      expect(find.textContaining('VALIDATION_ERROR'), findsNothing);
      expect(arnes.cola.jobs.single.fallido, isTrue, reason: 'queda en «Cola»');
    });
  });

  test('los tipos del rol: el suyo y «todos», activos y en su orden', () {
    final catalogos = catalogosCon('Golpe', tiposNovedadViaje: tiposNovedadViajeDePrueba);

    expect([for (final t in tiposNovedadViajePara(catalogos, 'conductor')) t.codigo], ['falla_camion', 'bloqueo_via', 'otro']);
    expect(
      [for (final t in tiposNovedadViajePara(catalogos, 'inspector')) t.codigo],
      ['carro_no_llego_patio', 'faltan_documentos', 'otro'],
    );
    expect(tiposNovedadViajePara(Catalogos.vacio, 'conductor'), isEmpty);
    expect(reportaNovedadViaje('coordinador_calidad'), isFalse);
  });
}
