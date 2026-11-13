import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:intl/date_symbol_data_local.dart';
import 'package:traza_autos/core/errores/error_api.dart';
import 'package:traza_autos/core/dispositivo/servicios.dart';
import 'package:traza_autos/core/tema/tema.dart';
import 'package:traza_autos/features/auth/domain/sesion.dart';
import 'package:traza_autos/features/catalogos/providers.dart';
import 'package:traza_autos/features/vehiculos/data/vehiculos_repositorio.dart';
import 'package:traza_autos/features/vehiculos/domain/inventario.dart';
import 'package:traza_autos/features/vehiculos/presentation/vin_page.dart';
import 'package:traza_autos/features/vehiculos/providers.dart';
import 'package:traza_autos/features/viajes/domain/permiso_sin_inspector.dart';
import 'package:traza_autos/features/viajes/providers.dart';

import '../support/arnes.dart';
import '../support/dobles_catalogos.dart';
import '../support/dobles_viajes.dart';

/// S7-HU-06.2 (#352): donde el punto de cargue tiene inspectores, la excepción «No hay inspector» no se activa:
/// se pide, se espera con el reloj a la vista, y concedida se sigue sin repetir nada.

class _InventarioFalso implements VehiculosRepositorio {
  _InventarioFalso(this.respuesta);

  final Map<String, dynamic> respuesta;

  @override
  Future<Inventario> inventario(String vin) async => Inventario.fromJson(respuesta);

  @override
  Future<List<VehiculoBusqueda>> buscar(String termino) async => const [];
}

const _conductor = Sesion(
  token: 'jwt',
  usuario: Usuario(id: 'conductor-1', nombre: 'Luis Gómez', email: '', rol: 'conductor'),
);

Map<String, dynamic> _inventario() => {
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
          'con_personal_apoyo': true,
          'inspecciones': <dynamic>[],
        },
      ],
    };

/// Desmonta el árbol: la espera tiene un segundero vivo y la prueba no puede terminar con él corriendo.
Future<void> cerrar(WidgetTester tester) async {
  await tester.pumpWidget(const SizedBox());
  await tester.pump();
}

Future<void> avanzar(WidgetTester tester) async {
  for (var i = 0; i < 12; i++) {
    await tester.pump(const Duration(milliseconds: 50));
  }
}

typedef Montaje = ({Arnes arnes, PermisosRepositorioFalso permisos, List<Uri> capturas});

Future<Montaje> abrir(
  WidgetTester tester, {
  required bool cargueConInspector,
  PermisoSinInspector? permiso,
  Object? errorAlSolicitar,
}) async {
  await tester.binding.setSurfaceSize(const Size(600, 2400));
  addTearDown(() => tester.binding.setSurfaceSize(null));
  final arnes = Arnes(sesion: _conductor);
  final capturas = <Uri>[];
  final permisos = PermisosRepositorioFalso()
    ..permiso = permiso
    ..errorAlSolicitar = errorAlSolicitar;
  final viajes = ViajesRepositorioFalso()
    ..viajeDeEtapa = viaje('viaje-1', cargueTieneInspector: cargueConInspector);
  final router = GoRouter(
    initialLocation: '/vin',
    routes: [
      GoRoute(path: '/vin', builder: (_, _) => const VinPage(vin: 'VIN00000000000001')),
      GoRoute(
        path: '/inspecciones/vin/:vin/captura/:etapaId',
        builder: (_, state) {
          capturas.add(state.uri);
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
        vehiculosRepositorioProvider.overrideWithValue(_InventarioFalso(_inventario())),
        viajesRepositorioProvider.overrideWithValue(viajes),
        permisosRepositorioProvider.overrideWithValue(permisos),
      ],
      child: MaterialApp.router(theme: crearTema(), routerConfig: router),
    ),
  );
  await avanzar(tester);
  return (arnes: arnes, permisos: permisos, capturas: capturas);
}

/// Elige «El inspector no llegó», que no pide escribir nada, y continúa.
Future<void> elegirMotivo(WidgetTester tester) async {
  await tester.tap(find.text('El inspector no llegó'));
  await tester.pump();
  await tester.tap(find.widgetWithText(FilledButton, 'Continuar con la salida del cargue'));
  await avanzar(tester);
}

void main() {
  setUpAll(() => initializeDateFormatting('es_CO'));

  group('el botón dice qué va a pasar', () {
    // CA-2: donde el servidor dice que el punto no tiene inspectores, todo sigue como estaba.
    testWidgets('sin inspectores en el punto, la excepción se activa en el momento', (tester) async {
      final m = await abrir(tester, cargueConInspector: false);

      expect(find.widgetWithText(OutlinedButton, 'No hay inspector'), findsOneWidget);
      expect(m.permisos.consultas, 0, reason: 'ese permiso no existe: no hay por qué preguntarlo');
    });

    // CA-1: con inspectores en el punto no se ofrece la activación directa, que el servidor rechazaría.
    testWidgets('con inspectores en el punto se pide permiso', (tester) async {
      await abrir(tester, cargueConInspector: true);

      expect(find.widgetWithText(OutlinedButton, 'Pedir permiso'), findsOneWidget);
      expect(find.widgetWithText(OutlinedButton, 'No hay inspector'), findsNothing);
    });

    testWidgets('con la solicitud ya puesta, el botón lleva a mirar cómo va', (tester) async {
      await abrir(tester, cargueConInspector: true, permiso: permisoDe());

      expect(find.widgetWithText(OutlinedButton, 'Ver el permiso'), findsOneWidget);
    });

    testWidgets('concedido, el botón es para seguir', (tester) async {
      await abrir(tester, cargueConInspector: true, permiso: permisoDe(concedido: true));

      expect(find.widgetWithText(OutlinedButton, 'Continuar el cargue'), findsOneWidget);
    });
  });

  group('pedir el permiso', () {
    testWidgets('manda el motivo y dónde se está, y deja esperando', (tester) async {
      final m = await abrir(tester, cargueConInspector: true);
      m.arnes.ubicacion.posicion = const Posicion(6.2442, -75.5812, precision: 5);

      await tester.tap(find.widgetWithText(OutlinedButton, 'Pedir permiso'));
      await avanzar(tester);
      await elegirMotivo(tester);

      expect(m.permisos.solicitudes.single, {
        'viajeId': 'viaje-1',
        'motivo': 'inspector_no_llego',
        'texto': null,
        'lat': 6.2442,
        'lng': -75.5812,
      });
      expect(find.text('Esperando respuesta'), findsOneWidget);
      expect(m.capturas, isEmpty, reason: 'todavía no se captura nada: falta que alguien conceda');
    });

    // CA-6: el punto dejó de tener inspectores entre abrir la ficha y pedir. Reintentar daría el mismo 409.
    testWidgets('si el punto ya no tiene inspectores, se dice y se sigue con el motivo ya elegido', (tester) async {
      final m = await abrir(
        tester,
        cargueConInspector: true,
        errorAlSolicitar: const ErrorApi(
          code: 'CONFLICT',
          status: 409,
          message: 'El punto de cargue no tiene inspectores asignados: la excepción «No hay inspector» se activa '
              'directamente, sin permiso',
        ),
      );

      await tester.tap(find.widgetWithText(OutlinedButton, 'Pedir permiso'));
      await avanzar(tester);
      await elegirMotivo(tester);

      expect(find.text('Aquí ya no hace falta permiso'), findsOneWidget);
      await tester.tap(find.widgetWithText(TextButton, 'Entendido'));
      await avanzar(tester);

      expect(m.capturas.single.queryParameters['motivo'], 'inspector_no_llego');
    });
  });

  group('la espera', () {
    // CA-3: cuánto falta y por dónde puede llegar. Lo segundo importa tanto como lo primero: sin ello, el
    // conductor se queda mirando un reloj sin saber que alguien puede destrabarlo antes.
    testWidgets('dice cuánto falta y las dos maneras en que puede llegar', (tester) async {
      await abrir(tester, cargueConInspector: true, permiso: permisoDe(falta: const Duration(minutes: 7, seconds: 30)));

      await tester.tap(find.widgetWithText(OutlinedButton, 'Ver el permiso'));
      await avanzar(tester);

      expect(find.textContaining('Faltan 7:'), findsOneWidget, reason: 'la cuenta atrás, ya corriendo');
      expect(find.textContaining('lo conceda un coordinador'), findsOneWidget);
      expect(find.textContaining('lo conceda el tiempo'), findsOneWidget);
      await cerrar(tester);
    });

    testWidgets('no depende de esta pantalla, y lo dice', (tester) async {
      await abrir(tester, cargueConInspector: true, permiso: permisoDe());

      await tester.tap(find.widgetWithText(OutlinedButton, 'Ver el permiso'));
      await avanzar(tester);

      expect(find.textContaining('la espera corre en el servidor'), findsOneWidget);
      await cerrar(tester);
    });

    testWidgets('cumplido el plazo deja de prometer minutos que ya pasaron', (tester) async {
      await abrir(tester, cargueConInspector: true, permiso: permisoDe(falta: Duration.zero));

      await tester.tap(find.widgetWithText(OutlinedButton, 'Ver el permiso'));
      await avanzar(tester);

      expect(find.textContaining('Ya se cumplió el plazo'), findsOneWidget);
      await cerrar(tester);
    });
  });

  group('concedido', () {
    // CA-4: seguir sin repetir nada. El motivo ya se eligió al pedir el permiso.
    testWidgets('se sigue a la captura con el motivo del permiso, sin preguntarlo otra vez', (tester) async {
      final m = await abrir(tester, cargueConInspector: true, permiso: permisoDe(concedido: true));

      await tester.tap(find.widgetWithText(OutlinedButton, 'Continuar el cargue'));
      await avanzar(tester);

      expect(find.text('El inspector no llegó'), findsNothing, reason: 'no se vuelve a preguntar el motivo');
      final consulta = m.capturas.single.queryParameters;
      expect(consulta['motivo'], 'otro');
      expect(consulta['motivoTexto'], 'El inspector se enfermó');
      expect(consulta['momento'], 'salida');
      expect(consulta['latMotivo'], '6.2442');
    });

    // Lo normal es estar esperando cuando llega: la pantalla cambia sola al volver a preguntar.
    testWidgets('esperando, al concederse por tiempo la pantalla lo dice y ofrece seguir', (tester) async {
      final m = await abrir(tester, cargueConInspector: true, permiso: permisoDe());

      await tester.tap(find.widgetWithText(OutlinedButton, 'Ver el permiso'));
      await avanzar(tester);
      expect(find.text('Esperando respuesta'), findsOneWidget);

      m.permisos.permiso = permisoDe(concedido: true, porTiempo: true);
      await tester.tap(find.widgetWithText(OutlinedButton, 'Actualizar'));
      await avanzar(tester);

      expect(find.text('Permiso concedido'), findsOneWidget);
      expect(find.textContaining('lo concedió el tiempo'), findsOneWidget);
      expect(find.widgetWithText(FilledButton, 'Continuar el cargue'), findsOneWidget);
      await cerrar(tester);
    });
  });
}
