import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:intl/date_symbol_data_local.dart';
import 'package:traza_autos/core/errores/error_api.dart';
import 'package:traza_autos/core/http/cliente_api.dart';
import 'package:traza_autos/core/tema/tema.dart';
import 'package:traza_autos/features/auth/domain/sesion.dart';
import 'package:traza_autos/features/auth/presentation/auth_controller.dart';
import 'package:traza_autos/features/avisos/data/avisos_repositorio.dart';
import 'package:traza_autos/features/avisos/domain/aviso.dart';
import 'package:traza_autos/features/avisos/domain/destino_aviso.dart';
import 'package:traza_autos/features/avisos/presentation/avisos_page.dart';
import 'package:traza_autos/features/avisos/providers.dart';
import 'package:traza_autos/features/viajes/domain/viaje.dart';
import 'package:traza_autos/features/viajes/presentation/mis_viajes_page.dart';
import 'package:traza_autos/features/viajes/providers.dart';

import '../support/arnes.dart';
import '../support/dio_falso.dart';
import '../support/dobles_viajes.dart';

/// S6-HU-02.11 (sin push): la bandeja «Avisos» con su urgencia, marcar leído al abrir, y la respuesta del coordinador.

Map<String, Object?> avisoJson(
  String id, {
  String tipo = 'novedad_conductor',
  String urgencia = 'alta',
  String titulo = 'Novedad en ruta',
  String? leida,
  Map<String, Object?>? respuesta,
  String? vin = 'VIN00000000000001',
}) =>
    {
      'id': id,
      'tipo': tipo,
      'urgencia': urgencia,
      'titulo': titulo,
      'mensaje': 'Luis registró una novedad de tipo golpe',
      'viaje_id': 'v1',
      'codigo_mtrack': 'MTK-1',
      'vin': vin,
      'leida_at': leida,
      'respuesta': respuesta,
      'created_at': '2026-10-21T13:31:00Z',
    };

class AvisosRepositorioFalso implements AvisosRepositorio {
  PaginaAvisos pagina0 = const PaginaAvisos(avisos: [], total: 0, noLeidos: 0);
  Object? error;
  Object? errorResponder;
  final List<String> leidos = [];
  final List<(String, String, String)> respuestas = [];
  int consultas = 0;

  @override
  Future<PaginaAvisos> pagina({int pagina = 1, int tamano = 50}) async {
    consultas++;
    if (error != null) throw error!;
    return pagina0;
  }

  @override
  Future<Aviso> marcarLeido(String id) async {
    leidos.add(id);
    return pagina0.avisos.firstWhere((a) => a.id == id).copiar(leidoEn: DateTime.utc(2026, 10, 21, 14));
  }

  @override
  Future<Aviso> responder(String id, {required String texto, required String medio}) async {
    respuestas.add((id, texto, medio));
    if (errorResponder != null) throw errorResponder!;
    return pagina0.avisos.firstWhere((a) => a.id == id).copiar(
          respuesta: RespuestaAviso(texto: texto, medio: medio, usuarioNombre: 'Ana Ruiz'),
        );
  }
}

PaginaAvisos paginaCon(List<Map<String, Object?>> items, {int noLeidos = 0}) =>
    PaginaAvisos.fromJson({'items': items, 'total': items.length, 'pagina': 1, 'tamano': 50, 'no_leidas': noLeidos});

const _coordinadora = Sesion(
  token: 'jwt',
  usuario: Usuario(id: 'c1', nombre: 'Ana Ruiz', email: '', rol: 'coordinador_calidad'),
);

Future<void> avanzar(WidgetTester tester) async {
  for (var i = 0; i < 12; i++) {
    await tester.pump(const Duration(milliseconds: 50));
  }
}

/// Pinta [child] cuando la sesión ya cargó, como la app.
class _ConSesion extends ConsumerWidget {
  const _ConSesion({required this.child});

  final Widget child;

  @override
  Widget build(BuildContext context, WidgetRef ref) =>
      ref.watch(authControllerProvider.select((e) => e.fase)) == FaseAuth.autenticado ? child : const SizedBox();
}

class _Arranque extends ConsumerWidget {
  const _Arranque();

  @override
  Widget build(BuildContext context, WidgetRef ref) =>
      ref.watch(authControllerProvider.select((e) => e.fase)) == FaseAuth.autenticado ? const AvisosPage() : const SizedBox();
}

Future<AvisosRepositorioFalso> abrir(WidgetTester tester, AvisosRepositorioFalso repo, {Sesion sesion = _coordinadora}) async {
  await tester.binding.setSurfaceSize(const Size(600, 2400));
  addTearDown(() => tester.binding.setSurfaceSize(null));
  await tester.pumpWidget(
    ProviderScope(
      retry: (_, _) => null,
      overrides: [...Arnes(sesion: sesion).overrides, avisosRepositorioProvider.overrideWithValue(repo)],
      child: MaterialApp(theme: crearTema(), home: const _Arranque()),
    ),
  );
  await avanzar(tester);
  return repo;
}

void main() {
  setUpAll(() => initializeDateFormatting('es_CO'));

  group('lo que trae la API', () {
    test('lee el aviso, su urgencia, si está leído y su respuesta', () {
      final aviso = Aviso.fromJson(avisoJson('a1', leida: '2026-10-21T14:00:00Z', respuesta: {
        'texto': 'Detente en el siguiente peaje',
        'medio': 'llamada',
        'usuario_nombre': 'Ana Ruiz',
        'fecha': '2026-10-21T14:12:00Z',
      }));
      expect((aviso.tipo, aviso.esAlta, aviso.leido, aviso.vin), ('novedad_conductor', true, true, 'VIN00000000000001'));
      expect((aviso.respuesta?.texto, aviso.respuesta?.medio, aviso.respuesta?.usuarioNombre), ('Detente en el siguiente peaje', 'llamada', 'Ana Ruiz'));
      expect(aviso.sePuedeResponder, isFalse, reason: 'ya tiene respuesta');
    });

    test('una novedad sin contestar es la única que se puede responder', () {
      expect(Aviso.fromJson(avisoJson('a1')).sePuedeResponder, isTrue);
      expect(Aviso.fromJson(avisoJson('a2', tipo: 'captura_incompleta')).sePuedeResponder, isFalse);
    });

    test('la página trae el total de sin leer', () {
      final pagina = paginaCon([avisoJson('a1'), avisoJson('a2')], noLeidos: 2);
      expect((pagina.avisos.length, pagina.noLeidos), (2, 2));
    });

    test('quién puede responder: el coordinador y los administradores', () {
      expect(puedeResponder('coordinador_calidad'), isTrue);
      expect(puedeResponder('admin'), isTrue);
      expect(puedeResponder('super_admin'), isTrue);
      expect(puedeResponder('inspector'), isFalse);
      expect(puedeResponder('conductor'), isFalse);
    });
  });

  group('hacia la API', () {
    (AvisosRepositorio, AdaptadorFalso) repositorio(Object cuerpo) {
      final adaptador = AdaptadorFalso((_) => json(200, cuerpo));
      final api = ClienteApi(baseUrl: 'https://api.test', token: () async => 'jwt', dio: Dio()..httpClientAdapter = adaptador);
      return (AvisosRepositorio(api), adaptador);
    }

    test('lista la primera página de la bandeja', () async {
      final (repo, adaptador) = repositorio({'items': [avisoJson('a1')], 'total': 1, 'pagina': 1, 'tamano': 50, 'no_leidas': 1});

      final pagina = await repo.pagina();

      expect(adaptador.peticiones.single.uri.path, '/api/notificaciones');
      expect(adaptador.peticiones.single.uri.queryParameters, {'pagina': '1', 'tamano': '50'});
      expect(pagina.noLeidos, 1);
    });

    test('marcar leído y responder van a su ruta con lo que pide el contrato', () async {
      final (repo, adaptador) = repositorio(avisoJson('a 1'));

      await repo.marcarLeido('a 1');
      await repo.responder('a 1', texto: 'Detente', medio: 'app');

      expect(adaptador.peticiones.first.uri.path, '/api/notificaciones/a%201/leida');
      expect(adaptador.peticiones.last.uri.path, '/api/notificaciones/a%201/respuesta');
      expect(adaptador.peticiones.last.data, {'texto': 'Detente', 'medio': 'app'});
    });
  });

  group('la pantalla', () {
    testWidgets('lista los avisos con su urgencia y cuántos faltan por leer', (tester) async {
      final repo = AvisosRepositorioFalso()
        ..pagina0 = paginaCon([
          avisoJson('a1', titulo: 'Novedad en ruta: VIN1'),
          avisoJson('a2', titulo: 'Camión sin actividad', urgencia: 'media', leida: '2026-10-21T14:00:00Z', tipo: 'reclamo_sin_actividad'),
        ], noLeidos: 1);
      await abrir(tester, repo);

      expect(find.text('Novedad en ruta: VIN1'), findsOneWidget);
      expect(find.text('Camión sin actividad'), findsOneWidget);
      expect(find.text('Urgente'), findsOneWidget);
      expect(find.text('Media'), findsOneWidget);
      expect(find.text('1 aviso sin leer'), findsOneWidget);
    });

    testWidgets('sin avisos lo dice', (tester) async {
      await abrir(tester, AvisosRepositorioFalso());

      expect(find.text('No tienes avisos'), findsOneWidget);
    });

    testWidgets('sin señal lo dice y se puede reintentar', (tester) async {
      final repo = AvisosRepositorioFalso()..error = const ErrorApi.red();
      await abrir(tester, repo);

      expect(find.text('No pudimos cargar tus avisos'), findsOneWidget);
      repo.error = null;
      repo.pagina0 = paginaCon([avisoJson('a1')], noLeidos: 1);
      await tester.tap(find.widgetWithText(FilledButton, 'Reintentar'));
      await avanzar(tester);

      expect(find.text('Novedad en ruta'), findsOneWidget);
    });

    testWidgets('abrir un aviso lo marca leído y muestra su mensaje completo', (tester) async {
      final repo = AvisosRepositorioFalso()..pagina0 = paginaCon([avisoJson('a1')], noLeidos: 1);
      await abrir(tester, repo);

      await tester.tap(find.text('Novedad en ruta'));
      await avanzar(tester);

      expect(repo.leidos, ['a1']);
      expect(find.text('Viaje MTK-1'), findsOneWidget);
    });

    // S8-HU-03.2 CA-6: el aviso del destino nuevo lleva a ese carro del viaje; un tipo que la app no conoce se pinta
    // igual con su título y su mensaje.
    testWidgets('el aviso de destino cambiado lleva al carro, y un tipo desconocido se pinta igual', (tester) async {
      final repo = AvisosRepositorioFalso()
        ..pagina0 = paginaCon([
          avisoJson('a1', tipo: 'cambio_destino_unidad', titulo: 'Cambió el destino de un carro', urgencia: 'media'),
          avisoJson('a2', tipo: 'algo_que_vendra', titulo: 'Un aviso nuevo', vin: null),
        ], noLeidos: 2);
      await abrir(tester, repo);

      expect(find.text('Un aviso nuevo'), findsOneWidget);
      await tester.tap(find.text('Cambió el destino de un carro'));
      await avanzar(tester);

      expect(find.widgetWithText(OutlinedButton, 'Ver la unidad'), findsOneWidget);
    });

    testWidgets('uno que ya estaba leído no se vuelve a marcar', (tester) async {
      final repo = AvisosRepositorioFalso()..pagina0 = paginaCon([avisoJson('a1', leida: '2026-10-21T14:00:00Z')]);
      await abrir(tester, repo);

      await tester.tap(find.text('Novedad en ruta'));
      await avanzar(tester);

      expect(repo.leidos, isEmpty);
    });

    testWidgets('si la marca de leído falla por falta de señal, el aviso se abre igual', (tester) async {
      final repo = _SinSenalAlLeer()..pagina0 = paginaCon([avisoJson('a1')], noLeidos: 1);
      await abrir(tester, repo);

      await tester.tap(find.text('Novedad en ruta'));
      await avanzar(tester);

      expect(find.text('Viaje MTK-1'), findsOneWidget);
    });
  });

  group('responder una novedad', () {
    Future<AvisosRepositorioFalso> abrirNovedad(WidgetTester tester, {Sesion sesion = _coordinadora, AvisosRepositorioFalso? repo}) async {
      final usado = repo ?? AvisosRepositorioFalso();
      usado.pagina0 = paginaCon([avisoJson('a1')], noLeidos: 1);
      await abrir(tester, usado, sesion: sesion);
      await tester.tap(find.text('Novedad en ruta'));
      await avanzar(tester);
      return usado;
    }

    testWidgets('el coordinador responde y queda la respuesta en el aviso', (tester) async {
      final repo = await abrirNovedad(tester);

      await tester.tap(find.widgetWithText(FilledButton, 'Responder'));
      await tester.pump();
      expect(tester.widget<FilledButton>(find.widgetWithText(FilledButton, 'Enviar respuesta')).onPressed, isNull, reason: 'sin texto no se envía');
      await tester.enterText(find.byType(TextField), 'Detente en el siguiente peaje');
      await tester.tap(find.text('Por llamada'));
      await tester.pump();
      await tester.tap(find.widgetWithText(FilledButton, 'Enviar respuesta'));
      await avanzar(tester);

      expect(repo.respuestas, [('a1', 'Detente en el siguiente peaje', 'llamada')]);
      expect(find.textContaining('Detente en el siguiente peaje'), findsOneWidget);
      expect(find.textContaining('por llamada'), findsOneWidget);
      expect(find.widgetWithText(FilledButton, 'Responder'), findsNothing, reason: 'ya está respondida');
    });

    testWidgets('si otra persona ya la respondió, lo dice con las palabras del servidor', (tester) async {
      final repo = AvisosRepositorioFalso()..errorResponder = const ErrorApi(message: 'Esta novedad ya fue respondida', code: 'CONFLICT', status: 409);
      await abrirNovedad(tester, repo: repo);

      await tester.tap(find.widgetWithText(FilledButton, 'Responder'));
      await tester.pump();
      await tester.enterText(find.byType(TextField), 'Detente');
      await tester.pump();
      await tester.tap(find.widgetWithText(FilledButton, 'Enviar respuesta'));
      await avanzar(tester);

      expect(find.text('Esta novedad ya fue respondida'), findsOneWidget);
    });

    testWidgets('el inspector no ve el botón de responder: no es suyo', (tester) async {
      await abrirNovedad(tester, sesion: sesionPrueba);

      expect(find.widgetWithText(FilledButton, 'Responder'), findsNothing);
    });

    testWidgets('un aviso que no es una novedad no se responde', (tester) async {
      final repo = AvisosRepositorioFalso()..pagina0 = paginaCon([avisoJson('a1', tipo: 'captura_incompleta', titulo: 'Captura incompleta')]);
      await abrir(tester, repo);

      await tester.tap(find.text('Captura incompleta'));
      await avanzar(tester);

      expect(find.widgetWithText(FilledButton, 'Responder'), findsNothing);
    });

    testWidgets('la respuesta que ya existe se muestra con quién y cómo la dio', (tester) async {
      final repo = AvisosRepositorioFalso()
        ..pagina0 = paginaCon([
          avisoJson('a1', respuesta: {'texto': 'Espera al inspector', 'medio': 'app', 'usuario_nombre': 'Ana Ruiz'}),
        ]);
      await abrir(tester, repo);

      await tester.tap(find.text('Novedad en ruta'));
      await avanzar(tester);

      expect(find.text('Respuesta de Ana Ruiz por la app'), findsOneWidget);
      expect(find.text('Espera al inspector'), findsOneWidget);
    });
  });

  // S8-HU-07.3 CA-9: la novedad del viaje y el aviso único «Cargue sin inspector» con sus carros.
  group('los avisos nuevos', () {
    Map<String, Object?> novedadDelViaje({String urgencia = 'alta'}) => {
          ...avisoJson('nv1', tipo: 'novedad_viaje', urgencia: urgencia, titulo: 'Novedad del viaje: Falla del camión (MT-2026-0417)', vin: null),
          'mensaje': 'Falla del camión: Se reventó una llanta en la vía',
          'datos': {
            'tipo_novedad': 'falla_camion',
            'tipo_novedad_nombre': 'Falla del camión',
            'rol': 'conductor',
            'proceso': null,
            'lugar': null,
            'usuario_nombre': 'Luis',
            'con_foto': true,
          },
        };

    Map<String, Object?> cargueSinInspector({Object? datos}) => {
          ...avisoJson('cs1', tipo: 'novedad_proceso', titulo: 'Cargue sin inspector (viaje MT-2026-0421)', vin: null),
          'mensaje': 'Luis hizo el cargue sin inspector',
          'datos': datos ??
              {
                'hecho': 'cargue_sin_inspector',
                'vins': ['9BWZZZ377VT004261', '9BWZZZ377VT004262', '9BWZZZ377VT004263'],
                'motivo': 'inspector_no_llego',
                'motivo_nombre': 'El inspector no llegó',
                'lugar': 'Patio de Prueba',
              },
        };

    test('el aviso lee sus datos; un datos que no es un objeto se ignora', () {
      final aviso = Aviso.fromJson(cargueSinInspector());
      expect(aviso.datos?['hecho'], 'cargue_sin_inspector');
      expect(aviso.vinsDelCargueSinInspector, hasLength(3));
      expect(Aviso.fromJson(novedadDelViaje()).etiquetaTipo, 'Novedad del viaje');

      final raro = Aviso.fromJson({...avisoJson('x1'), 'datos': 'no es un objeto'});
      expect((raro.datos, raro.vinsDelCargueSinInspector), (null, null));
      expect(Aviso.fromJson(cargueSinInspector(datos: {'hecho': 'cargue_sin_inspector', 'vins': 3})).vinsDelCargueSinInspector, isNull);
    });

    testWidgets('la novedad del viaje se ve con su etiqueta y la insignia de su urgencia', (tester) async {
      final repo = AvisosRepositorioFalso()
        ..pagina0 = paginaCon([novedadDelViaje(), {...novedadDelViaje(urgencia: 'media'), 'id': 'nv2'}], noLeidos: 2);
      await abrir(tester, repo);

      expect(find.text('Novedad del viaje'), findsNWidgets(2));
      expect(find.text('Urgente'), findsOneWidget);
      expect(find.text('Media'), findsOneWidget);
      expect(find.text('Novedad del viaje: Falla del camión (MT-2026-0417)'), findsNWidgets(2));
    });

    testWidgets('el aviso único «Cargue sin inspector» dice cuántos carros y cuáles', (tester) async {
      final repo = AvisosRepositorioFalso()..pagina0 = paginaCon([cargueSinInspector()], noLeidos: 1);
      await abrir(tester, repo);

      expect(find.text('Cargue sin inspector (viaje MT-2026-0421)'), findsOneWidget);
      expect(find.text('Carros: 3'), findsOneWidget);
      for (final vin in ['9BWZZZ377VT004261', '9BWZZZ377VT004262', '9BWZZZ377VT004263']) {
        expect(find.text(vin), findsOneWidget, reason: vin);
      }

      await tester.tap(find.text('Cargue sin inspector (viaje MT-2026-0421)'));
      await avanzar(tester);
      expect(find.text('Carros: 3'), findsNWidgets(2), reason: 'también en el aviso abierto');
    });

    testWidgets('un tipo o unos datos desconocidos se muestran con su título y su mensaje, sin romper la lista', (tester) async {
      final repo = AvisosRepositorioFalso()
        ..pagina0 = paginaCon([
          {...avisoJson('z1', tipo: 'tipo_del_futuro', titulo: 'Algo nuevo'), 'datos': {'cosa': 1}},
          cargueSinInspector(datos: ['no', 'es', 'un', 'objeto']),
          avisoJson('a1', titulo: 'Novedad en ruta: VIN1'),
        ], noLeidos: 3);
      await abrir(tester, repo);

      expect(find.text('Algo nuevo'), findsOneWidget);
      expect(find.text('Cargue sin inspector (viaje MT-2026-0421)'), findsOneWidget);
      expect(find.text('Novedad en ruta: VIN1'), findsOneWidget);
      expect(find.textContaining('Carros:'), findsNothing);
      expect(find.text('tipo_del_futuro'), findsNothing);
    });
  });

  // S8-HU-09.2 CA-6: cada aviso lleva a donde dicen su tipo y sus ids (del aviso o de `datos`), nunca su texto.
  group('a dónde lleva cada aviso', () {
    test('a la unidad, al hallazgo y al viaje por sus ids', () {
      expect(destinoDeAviso(tipo: 'cambio_destino_unidad', vin: 'VIN1', viajeId: 'v1')?.ruta, '/inspecciones/vin/VIN1?viajeId=v1');
      expect(
        destinoDeAviso(tipo: 'hallazgo_critico', vin: 'VIN1', viajeId: 'v1', hallazgoId: 'h1')?.ruta,
        '/inspecciones/vin/VIN1?viajeId=v1&hallazgo=h1',
      );
      expect(destinoDeAviso(tipo: 'viaje_sin_movimiento', datos: {'viaje_id': 'v7'})?.ruta, '/viajes/viaje/v7');
      expect(destinoDeAviso(tipo: 'novedad_conductor', vin: 'VIN1')?.ruta, '/inspecciones/vin/VIN1', reason: 'sin viaje');
    });

    test('«Fuera del rango» del reclamo lleva al viaje; el de una foto, a la unidad', () {
      final delReclamo = destinoDeAviso(
        tipo: 'fuera_de_rango',
        viajeId: 'v17',
        datos: {'hecho': 'reclamo_entrega', 'viaje_id': 'v17', 'lugar_id': 'l1'},
      );
      expect((delReclamo?.ruta, delReclamo?.etiqueta), ('/viajes/viaje/v17', 'Ver el viaje'));
      expect(destinoDeAviso(tipo: 'fuera_de_rango', viajeId: 'v17', vin: 'VIN1')?.etiqueta, 'Ver la unidad');
    });

    test('«Ubicación fijada» es del lugar: no hay pantalla, se lee en la bandeja', () {
      final destino = destinoDeAviso(tipo: 'ubicacion_fijada', datos: {'lugar_id': 'l1', 'lugar': 'Patio de Prueba'});
      expect(destino, isA<DestinoLugar>());
      expect((destino!.ruta, destino.etiqueta), (null, 'Patio de Prueba'));
    });

    test('un tipo desconocido, o uno sin ids, no lleva a ninguna parte', () {
      expect(destinoDeAviso(tipo: 'algo_que_vendra', vin: 'VIN1', viajeId: 'v1'), isNull);
      expect(destinoDeAviso(tipo: 'reporte_diario'), isNull);
    });

    test('el aviso lee hallazgo_id y datos de la API', () {
      final aviso = Aviso.fromJson({
        ...avisoJson('a1', tipo: 'hallazgo_critico'),
        'hallazgo_id': 'h9',
        'datos': {'lugar': 'Puerto'},
      });
      expect((aviso.hallazgoId, aviso.datos?['lugar']), ('h9', 'Puerto'));
      expect(aviso.copiar(leidoEn: DateTime.utc(2026)).hallazgoId, 'h9', reason: 'marcarlo leído no lo pierde');
      expect(aviso.destino, isA<DestinoHallazgo>());
    });

    test('los metros se escriben con el punto de miles', () {
      expect(metros(2300), '2.300 m');
      expect(metros(500), '500 m');
      expect(metros(12.4), '12 m');
      expect(metros(1234567), '1.234.567 m');
    });
  });

  group('los datos propios de «Ubicación fijada» y «Fuera del rango»', () {
    // ESC-07: «Patio de Prueba», quién la fijó, cuándo y «precisión 12 m».
    test('ubicación fijada: el lugar, quién, cuándo y la precisión', () {
      final lineas = lineasDeDatos(Aviso.fromJson({
        ...avisoJson('a1', tipo: 'ubicacion_fijada', vin: null),
        'datos': {
          'lugar_id': 'l1',
          'lugar': 'Patio de Prueba',
          'usuario_nombre': 'Ana Restrepo',
          'precision_m': 12,
          'fijada_at': '2026-11-20T13:05:00Z',
        },
      }));
      expect(lineas, hasLength(4));
      expect(lineas[0], 'Lugar: Patio de Prueba');
      expect(lineas[1], 'La fijó: Ana Restrepo');
      expect(lineas[2], startsWith('Cuándo: 20 nov 2026'));
      expect(lineas[3], 'GPS con precisión 12 m');
    });

    // ESC-09: «Puerto de Cartagena», «2.300 m» y «rango 500 m».
    test('fuera del rango del reclamo: el lugar, quién, la distancia, el rango y la precisión', () {
      final lineas = lineasDeDatos(Aviso.fromJson({
        ...avisoJson('a1', tipo: 'fuera_de_rango', vin: null),
        'datos': {
          'hecho': 'reclamo_entrega',
          'viaje_id': 'v1',
          'lugar_id': 'l1',
          'lugar': 'Puerto de Cartagena',
          'usuario_nombre': 'Pedro Gil',
          'distancia_m': 2300.4,
          'radio_m': 500,
          'precision_m': 10,
        },
      }));
      expect(lineas, [
        'Lugar: Puerto de Cartagena',
        'Tomó la entrega: Pedro Gil',
        'A 2.300 m del lugar · rango 500 m',
        'GPS con precisión 10 m',
      ]);
    });

    test('los demás avisos no tienen líneas de datos', () {
      expect(lineasDeDatos(Aviso.fromJson({...avisoJson('a1'), 'datos': {'lugar': 'X'}})), isEmpty);
    });
  });

  group('la bandeja enruta', () {
    Future<(AvisosRepositorioFalso, GoRouter)> abrirConRutas(WidgetTester tester, List<Map<String, Object?>> avisos) async {
      await tester.binding.setSurfaceSize(const Size(600, 2400));
      addTearDown(() => tester.binding.setSurfaceSize(null));
      final repo = AvisosRepositorioFalso()..pagina0 = paginaCon(avisos, noLeidos: avisos.length);
      final router = GoRouter(
        initialLocation: '/avisos',
        routes: [
          GoRoute(path: '/avisos', builder: (_, _) => const _Arranque()),
          GoRoute(path: '/viajes/viaje/:id', builder: (_, s) => Text('el viaje ${s.pathParameters['id']}')),
          GoRoute(
            path: '/inspecciones/vin/:vin',
            builder: (_, s) => Text('la unidad ${s.pathParameters['vin']} ${s.uri.queryParameters['hallazgo'] ?? ''}'),
          ),
        ],
      );
      await tester.pumpWidget(
        ProviderScope(
          retry: (_, _) => null,
          overrides: [...Arnes(sesion: _coordinadora).overrides, avisosRepositorioProvider.overrideWithValue(repo)],
          child: MaterialApp.router(theme: crearTema(), routerConfig: router),
        ),
      );
      await avanzar(tester);
      return (repo, router);
    }

    // ESC-11 (sin el push): tocar «Fuera del rango» lleva al viaje.
    testWidgets('«Fuera del rango» muestra sus datos y lleva al viaje', (tester) async {
      await abrirConRutas(tester, [
        {
          ...avisoJson('a1', tipo: 'fuera_de_rango', titulo: 'Fuera del rango: Puerto de Cartagena', vin: null),
          'viaje_id': 'v17',
          'datos': {
            'hecho': 'reclamo_entrega',
            'viaje_id': 'v17',
            'lugar_id': 'l1',
            'lugar': 'Puerto de Cartagena',
            'usuario_nombre': 'Pedro Gil',
            'distancia_m': 2300,
            'radio_m': 500,
            'precision_m': 10,
          },
        },
      ]);

      await tester.tap(find.text('Fuera del rango: Puerto de Cartagena'));
      await avanzar(tester);
      expect(find.text('A 2.300 m del lugar · rango 500 m'), findsOneWidget);
      expect(find.text('Tomó la entrega: Pedro Gil'), findsOneWidget);

      await tester.tap(find.widgetWithText(OutlinedButton, 'Ver el viaje'));
      await avanzar(tester);
      expect(find.text('el viaje v17'), findsOneWidget);
    });

    testWidgets('«Ubicación fijada» se lee en la bandeja, con sus datos y sin botón', (tester) async {
      await abrirConRutas(tester, [
        {
          ...avisoJson('a1', tipo: 'ubicacion_fijada', titulo: 'Ubicación fijada: Patio de Prueba', urgencia: 'media', vin: null),
          'viaje_id': null,
          'datos': {'lugar_id': 'l1', 'lugar': 'Patio de Prueba', 'usuario_nombre': 'Ana Restrepo', 'precision_m': 12},
        },
      ]);

      await tester.tap(find.text('Ubicación fijada: Patio de Prueba'));
      await avanzar(tester);

      expect(find.text('GPS con precisión 12 m'), findsOneWidget);
      expect(find.text('La fijó: Ana Restrepo'), findsOneWidget);
      expect(find.byType(OutlinedButton), findsNothing);
    });

    testWidgets('el hallazgo crítico lleva a su hallazgo en la ficha de la unidad', (tester) async {
      await abrirConRutas(tester, [
        {...avisoJson('a1', tipo: 'hallazgo_critico', titulo: 'Hallazgo crítico'), 'hallazgo_id': 'h9'},
      ]);

      await tester.tap(find.text('Hallazgo crítico'));
      await avanzar(tester);
      await tester.tap(find.widgetWithText(OutlinedButton, 'Ver el hallazgo'));
      await avanzar(tester);

      expect(find.text('la unidad VIN00000000000001 h9'), findsOneWidget);
    });

    testWidgets('un tipo desconocido se abre en la bandeja sin botón y sin romper', (tester) async {
      final (repo, _) = await abrirConRutas(tester, [avisoJson('a1', tipo: 'algo_que_vendra', titulo: 'Un aviso nuevo')]);

      await tester.tap(find.text('Un aviso nuevo'));
      await avanzar(tester);

      expect(repo.leidos, ['a1']);
      expect(find.text('Luis registró una novedad de tipo golpe'), findsWidgets);
      expect(find.byType(OutlinedButton), findsNothing);
      expect(tester.takeException(), isNull);
    });
  });

  // El viaje al que lleva un aviso se abre aunque no esté en «Mis viajes».
  group('la pantalla del viaje de un aviso', () {
    Future<void> abrirViaje(WidgetTester tester, ReclamosRepositorioFalso reclamos, {List<Viaje>? copia}) async {
      await tester.binding.setSurfaceSize(const Size(600, 2400));
      addTearDown(() => tester.binding.setSurfaceSize(null));
      await tester.pumpWidget(
        ProviderScope(
          retry: (_, _) => null,
          overrides: [
            ...Arnes(sesion: _coordinadora).overrides,
            reclamosRepositorioProvider.overrideWithValue(reclamos),
            viajesRepositorioProvider.overrideWithValue(ViajesRepositorioFalso()..guardados = copia),
          ],
          child: MaterialApp(
            theme: crearTema(),
            home: const _ConSesion(child: ViajeDelAvisoPage(viajeId: 'v17')),
          ),
        ),
      );
      await avanzar(tester);
    }

    testWidgets('muestra el viaje con sus unidades', (tester) async {
      await abrirViaje(
        tester,
        ReclamosRepositorioFalso()..viajeDelPendiente = viaje('v17', codigo: 'MT-2026-0417', vines: ['VINAAAAAAAAAAAAA1']),
      );

      expect(find.text('MT-2026-0417'), findsWidgets);
      expect(find.text('VINAAAAAAAAAAAAA1'), findsOneWidget);
    });

    testWidgets('un 403 dice que no es suyo, no que falte señal, y no ofrece reintentar', (tester) async {
      await abrirViaje(tester, ReclamosRepositorioFalso()..errorViaje = const ErrorApi(message: 'x', code: 'FORBIDDEN', status: 403));

      expect(find.text('No puedes ver este viaje'), findsOneWidget);
      expect(find.textContaining('señal'), findsNothing);
      expect(find.widgetWithText(FilledButton, 'Reintentar'), findsNothing);
    });

    testWidgets('un 404 dice que el viaje ya no existe', (tester) async {
      await abrirViaje(tester, ReclamosRepositorioFalso()..errorViaje = const ErrorApi(message: 'x', code: 'NOT_FOUND', status: 404));

      expect(find.text('Este viaje ya no existe'), findsOneWidget);
      expect(find.textContaining('señal'), findsNothing);
    });

    testWidgets('sin señal sale de la copia si el viaje está ahí; si no, dice que falta señal', (tester) async {
      await abrirViaje(
        tester,
        ReclamosRepositorioFalso()..errorViaje = const ErrorApi.red(),
        copia: [viaje('v17', codigo: 'MT-2026-0417', vines: ['VINAAAAAAAAAAAAA1'])],
      );
      expect(find.text('VINAAAAAAAAAAAAA1'), findsOneWidget);

      await abrirViaje(tester, ReclamosRepositorioFalso()..errorViaje = const ErrorApi.red(), copia: const []);
      expect(find.textContaining('El viaje se consulta con señal'), findsOneWidget);
      expect(find.widgetWithText(FilledButton, 'Reintentar'), findsOneWidget);
    });
  });
}

class _SinSenalAlLeer extends AvisosRepositorioFalso {
  @override
  Future<Aviso> marcarLeido(String id) async => throw const ErrorApi.red();
}
