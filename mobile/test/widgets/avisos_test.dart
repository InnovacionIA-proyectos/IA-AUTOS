import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intl/date_symbol_data_local.dart';
import 'package:traza_autos/core/errores/error_api.dart';
import 'package:traza_autos/core/http/cliente_api.dart';
import 'package:traza_autos/core/tema/tema.dart';
import 'package:traza_autos/features/auth/domain/sesion.dart';
import 'package:traza_autos/features/auth/presentation/auth_controller.dart';
import 'package:traza_autos/features/avisos/data/avisos_repositorio.dart';
import 'package:traza_autos/features/avisos/domain/aviso.dart';
import 'package:traza_autos/features/avisos/presentation/avisos_page.dart';
import 'package:traza_autos/features/avisos/providers.dart';

import '../support/arnes.dart';
import '../support/dio_falso.dart';

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
}

class _SinSenalAlLeer extends AvisosRepositorioFalso {
  @override
  Future<Aviso> marcarLeido(String id) async => throw const ErrorApi.red();
}
