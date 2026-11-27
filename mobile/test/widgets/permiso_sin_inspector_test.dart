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

/// [conMarca] en falso es la respuesta de hoy (contrato 3.0 en adelante), sin `con_personal_apoyo`: ahí la llegada del
/// cargue del conductor también va con la excepción (S8-HU-07.3 CA-7).
Map<String, dynamic> _inventario({bool conMarca = true}) => {
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
          if (conMarca) 'con_personal_apoyo': true,
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
  // S8-HU-07.3 CA-6: el permiso solo existe donde el lugar de cargue admite cargue sin inspector.
  bool admite = true,
  bool conMarca = true,
}) async {
  await tester.binding.setSurfaceSize(const Size(600, 2400));
  addTearDown(() => tester.binding.setSurfaceSize(null));
  final arnes = Arnes(sesion: _conductor);
  final capturas = <Uri>[];
  final permisos = PermisosRepositorioFalso()
    ..permiso = permiso
    ..errorAlSolicitar = errorAlSolicitar;
  final viajes = ViajesRepositorioFalso()
    ..viajeDeEtapa = viaje('viaje-1', cargueTieneInspector: cargueConInspector, admiteSinInspector: admite);
  final router = GoRouter(
    initialLocation: '/vin',
    routes: [
      GoRoute(path: '/vin', builder: (_, _) => const VinPage(vin: 'VIN00000000000001')),
      GoRoute(
        path: '/inspecciones/vin/:vin/captura/:etapaId',
        builder: (_, state) {
          capturas.add(state.uri);
          // Con barra, para que la prueba pueda volver atrás como se vuelve en el equipo.
          return Scaffold(appBar: AppBar(title: const Text('captura')), body: const SizedBox());
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
        vehiculosRepositorioProvider.overrideWithValue(_InventarioFalso(_inventario(conMarca: conMarca))),
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

    // S8-HU-09.2 CA-2: en cero se sigue sin esperar a ver el permiso concedido, porque el servidor lo concede en la
    // misma petición (ESC-03). La cuenta atrás corre con el reloj de verdad: por eso se deja pasar tiempo real.
    testWidgets('con la cuenta atrás en cero, «Continuar el cargue» queda disponible sin ver el permiso concedido',
        (tester) async {
      final m = await abrir(tester, cargueConInspector: true, permiso: permisoDe(falta: const Duration(seconds: 3)));
      await tester.tap(find.widgetWithText(OutlinedButton, 'Ver el permiso'));
      await avanzar(tester);
      expect(find.widgetWithText(FilledButton, 'Continuar el cargue'), findsNothing, reason: 'todavía falta');

      await tester.runAsync(() => Future<void>.delayed(const Duration(milliseconds: 3200)));
      await tester.pump(const Duration(seconds: 1));

      expect(find.textContaining('Ya se cumplió el plazo'), findsOneWidget);
      expect(find.text('Permiso concedido'), findsNothing, reason: 'el servidor aún no lo dice, y no hace falta');
      expect(m.permisos.permiso!.concedido, isFalse);
      await tester.tap(find.widgetWithText(FilledButton, 'Continuar el cargue'));
      await avanzar(tester);

      final consulta = m.capturas.single.queryParameters;
      expect((consulta['motivo'], consulta['momento']), ('otro', 'salida'));
      expect(find.text('Permiso para el cargue'), findsNothing, reason: 'la espera se cierra al seguir');
      await cerrar(tester);
    });

    testWidgets('en cero, el botón de la ficha ya es para seguir y lleva a la captura con el motivo pedido',
        (tester) async {
      final m = await abrir(tester, cargueConInspector: true, permiso: permisoDe(falta: Duration.zero));

      expect(find.widgetWithText(OutlinedButton, 'Ver el permiso'), findsNothing);
      await tester.tap(find.widgetWithText(OutlinedButton, 'Continuar el cargue'));
      await avanzar(tester);

      expect(m.capturas.single.queryParameters['motivo'], 'otro');
      expect(find.text('El inspector no llegó'), findsNothing, reason: 'no se vuelve a preguntar el motivo');
    });
  });

  group('la etiqueta del botón', () {
    final ahora = DateTime.utc(2026, 11, 10, 8, 10, 20);

    /// Pedido diez minutos antes de [ahora] y con [falta] todavía por delante.
    PermisoSinInspector pendiente(Duration falta) =>
        permisoDe(ahora: ahora.subtract(const Duration(minutes: 10)), falta: const Duration(minutes: 10) + falta);

    test('pendiente con plazo por cumplir: mirar cómo va; cumplido o concedido: seguir', () {
      expect(etiquetaExcepcion(cargueConInspector: true, permiso: pendiente(const Duration(seconds: 30)), ahora: ahora),
          'Ver el permiso');
      expect(etiquetaExcepcion(cargueConInspector: true, permiso: pendiente(Duration.zero), ahora: ahora),
          'Continuar el cargue');
      expect(etiquetaExcepcion(cargueConInspector: true, permiso: permisoDe(concedido: true), ahora: ahora),
          'Continuar el cargue');
      expect(etiquetaExcepcion(cargueConInspector: true, ahora: ahora), 'Pedir permiso');
    });

    test('vencido es solo el pendiente con la cuenta en cero', () {
      expect(pendiente(const Duration(minutes: 2)).vencido(ahora), isFalse);
      expect(pendiente(const Duration(minutes: 2)).vencido(ahora.add(const Duration(minutes: 2))), isTrue);
      expect(permisoDe(concedido: true, falta: Duration.zero).vencido(ahora), isFalse, reason: 'concedido no está «vencido»');
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

    // El defecto visto en el equipo: tras cerrar el cargue se volvía a la espera, que seguía diciendo
    // «Continuar el cargue» y mandaba a capturar otra vez un informe ya cerrado.
    testWidgets('al seguir desde la espera, la espera se cierra y no se vuelve a ella', (tester) async {
      final m = await abrir(tester, cargueConInspector: true, permiso: permisoDe(concedido: true));

      await tester.tap(find.widgetWithText(OutlinedButton, 'Continuar el cargue'));
      await avanzar(tester);
      expect(m.capturas, hasLength(1), reason: 'el botón de la ficha va directo, sin pasar por la espera');

      // Y por el otro camino, el de quien estaba esperando: abre la espera y sigue desde ella.
      final n = await abrir(tester, cargueConInspector: true, permiso: permisoDe());
      await tester.tap(find.widgetWithText(OutlinedButton, 'Ver el permiso'));
      await avanzar(tester);
      n.permisos.permiso = permisoDe(concedido: true);
      await tester.tap(find.widgetWithText(OutlinedButton, 'Actualizar'));
      await avanzar(tester);

      await tester.tap(find.widgetWithText(FilledButton, 'Continuar el cargue'));
      await avanzar(tester);
      expect(n.capturas, hasLength(1));

      // Lo que de verdad falla si la espera se queda debajo: al terminar la captura se vuelve a ella.
      await tester.pageBack();
      await avanzar(tester);

      expect(find.text('Permiso para el cargue'), findsNothing, reason: 'se vuelve a la ficha, no a la espera');
      expect(find.text('Esperando respuesta'), findsNothing);
      await cerrar(tester);
    });
  });

  // S8-HU-07.3 CA-6 (C1): la excepción y el permiso solo donde el viaje dice que el lugar de cargue los admite.
  group('el lugar decide', () {
    testWidgets('donde el lugar no admite cargue sin inspector, ni la excepción ni el permiso: lo dice la ficha', (tester) async {
      final m = await abrir(tester, cargueConInspector: true, admite: false, conMarca: false);

      expect(find.widgetWithText(OutlinedButton, 'Pedir permiso'), findsNothing);
      expect(find.widgetWithText(OutlinedButton, 'No hay inspector'), findsNothing);
      expect(find.widgetWithText(OutlinedButton, 'Capturar'), findsNothing, reason: 'el cargue lo hace el inspector');
      expect(find.text('El cargue lo hace el inspector'), findsOneWidget);
      expect(m.permisos.consultas, 0, reason: 'ese permiso no existe allí: no hay por qué preguntarlo');
    });

    testWidgets('un 409 lugar_no_admite_cargue_sin_inspector al pedir el permiso se dice con el mensaje del servidor', (tester) async {
      final m = await abrir(
        tester,
        cargueConInspector: true,
        errorAlSolicitar: const ErrorApi(
          code: 'CONFLICT',
          status: 409,
          message: 'Este lugar de cargue no admite cargue sin inspector: el cargue lo hace el inspector',
          details: {'motivo': 'lugar_no_admite_cargue_sin_inspector'},
        ),
      );

      await tester.tap(find.widgetWithText(OutlinedButton, 'Pedir permiso'));
      await avanzar(tester);
      await elegirMotivo(tester);

      expect(
        find.text('Este lugar de cargue no admite cargue sin inspector: el cargue lo hace el inspector'),
        findsOneWidget,
      );
      expect(find.textContaining('lugar_no_admite'), findsNothing, reason: 'nunca el código crudo');
      expect(m.capturas, isEmpty);
    });
  });

  // S8-HU-07.3 CA-7 (hueco 7): la llegada del cargue del conductor tampoco se captura sin la excepción.
  group('la llegada del cargue del conductor', () {
    Future<void> elegirMotivoDeLaLlegada(WidgetTester tester) async {
      expect(find.text('Elige por qué: la llegada del cargue la haces tú.'), findsOneWidget);
      await tester.tap(find.text('El inspector no llegó'));
      await tester.pump();
      await tester.tap(find.widgetWithText(FilledButton, 'Continuar con la llegada del cargue'));
      await avanzar(tester);
    }

    testWidgets('sin inspectores en el punto, «No hay inspector» activa la excepción y sigue a la llegada con el motivo', (tester) async {
      final m = await abrir(tester, cargueConInspector: false, conMarca: false);

      expect(find.widgetWithText(OutlinedButton, 'No hay inspector'), findsNWidgets(2), reason: 'la salida y la llegada');
      expect(find.widgetWithText(OutlinedButton, 'Capturar'), findsNothing);

      await tester.tap(find.widgetWithText(OutlinedButton, 'No hay inspector').last);
      await avanzar(tester);
      await elegirMotivoDeLaLlegada(tester);

      final consulta = m.capturas.single.queryParameters;
      expect((consulta['momento'], consulta['motivo']), ('llegada', 'inspector_no_llego'));
      expect(m.permisos.solicitudes, isEmpty, reason: 'sin inspectores no hay permiso que pedir');
    });

    testWidgets('con inspectores en el punto, «No hay inspector» lleva a pedir el permiso', (tester) async {
      final m = await abrir(tester, cargueConInspector: true, conMarca: false);

      expect(find.widgetWithText(OutlinedButton, 'Pedir permiso'), findsOneWidget, reason: 'la salida');
      expect(find.widgetWithText(OutlinedButton, 'No hay inspector'), findsOneWidget, reason: 'la llegada');
      expect(find.widgetWithText(OutlinedButton, 'Capturar'), findsNothing, reason: 'sin el permiso concedido');

      await tester.tap(find.widgetWithText(OutlinedButton, 'No hay inspector'));
      await avanzar(tester);
      await elegirMotivoDeLaLlegada(tester);

      expect(m.permisos.solicitudes.single['motivo'], 'inspector_no_llego');
      expect(find.text('Esperando respuesta'), findsOneWidget);
      expect(m.capturas, isEmpty);
      await cerrar(tester);
    });

    testWidgets('con el permiso concedido ofrece «Capturar» y va a la llegada con el motivo del permiso', (tester) async {
      final m = await abrir(tester, cargueConInspector: true, conMarca: false, permiso: permisoDe(concedido: true));

      expect(find.widgetWithText(OutlinedButton, 'Continuar el cargue'), findsOneWidget, reason: 'la salida');
      await tester.tap(find.widgetWithText(OutlinedButton, 'Capturar'));
      await avanzar(tester);

      final consulta = m.capturas.single.queryParameters;
      expect((consulta['momento'], consulta['motivo'], consulta['motivoTexto']), ('llegada', 'otro', 'El inspector se enfermó'));
      expect(find.text('El inspector no llegó'), findsNothing, reason: 'no se vuelve a preguntar el motivo');
    });

    testWidgets('con el permiso pendiente, la llegada lleva a mirar cómo va', (tester) async {
      await abrir(tester, cargueConInspector: true, conMarca: false, permiso: permisoDe());

      expect(find.widgetWithText(OutlinedButton, 'Ver el permiso'), findsNWidgets(2));
      expect(find.widgetWithText(OutlinedButton, 'Capturar'), findsNothing);
    });

    // Revisión de S8-HU-07.3: la espera abierta desde la llegada habla de la llegada, como la ficha.
    testWidgets('la espera abierta desde la llegada sigue con «Capturar» a la captura de la llegada', (tester) async {
      final m = await abrir(tester, cargueConInspector: true, conMarca: false, permiso: permisoDe());

      await tester.tap(find.widgetWithText(OutlinedButton, 'Ver el permiso').last);
      await avanzar(tester);
      m.permisos.permiso = permisoDe(concedido: true);
      await tester.tap(find.widgetWithText(OutlinedButton, 'Actualizar'));
      await avanzar(tester);

      expect(find.widgetWithText(FilledButton, 'Continuar el cargue'), findsNothing);
      await tester.tap(find.widgetWithText(FilledButton, 'Capturar'));
      await avanzar(tester);

      expect(m.capturas.single.queryParameters['momento'], 'llegada');
      await cerrar(tester);
    });
  });

  // S8-HU-09.2 CA-2: la captura sabe si viene de un permiso pedido. Solo entonces un 403 de espera es esperar.
  group('la captura sabe si sigue un permiso pedido', () {
    testWidgets('siguiendo el permiso, la captura lo lleva', (tester) async {
      final m = await abrir(tester, cargueConInspector: true, permiso: permisoDe(concedido: true));

      await tester.tap(find.widgetWithText(OutlinedButton, 'Continuar el cargue'));
      await avanzar(tester);

      expect(m.capturas.single.queryParameters['permiso'], '1');
    });

    testWidgets('con la excepción directa (sin permiso que esperar), no', (tester) async {
      final m = await abrir(tester, cargueConInspector: false);

      await tester.tap(find.widgetWithText(OutlinedButton, 'No hay inspector'));
      await avanzar(tester);
      await elegirMotivo(tester);

      expect(m.capturas.single.queryParameters['motivo'], 'inspector_no_llego');
      expect(m.capturas.single.queryParameters.containsKey('permiso'), isFalse);
    });
  });

  // S8-HU-09.2 CA-2: el botón de la ficha cambia solo cuando la cuenta llega a cero, sin tocar la pantalla.
  testWidgets('en la ficha, «Ver el permiso» pasa a «Continuar el cargue» al llegar la cuenta a cero', (tester) async {
    await abrir(tester, cargueConInspector: true, permiso: permisoDe(falta: const Duration(seconds: 3)));
    expect(find.widgetWithText(OutlinedButton, 'Ver el permiso'), findsOneWidget);

    await tester.runAsync(() => Future<void>.delayed(const Duration(milliseconds: 3200)));
    await tester.pump(const Duration(seconds: 4));
    await avanzar(tester);

    expect(find.widgetWithText(OutlinedButton, 'Continuar el cargue'), findsOneWidget);
    expect(find.widgetWithText(OutlinedButton, 'Ver el permiso'), findsNothing);
  });
}
