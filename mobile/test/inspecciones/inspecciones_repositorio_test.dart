import 'package:dio/dio.dart';
import 'package:drift/native.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/core/db/app_database.dart';
import 'package:traza_autos/core/db/cache_lecturas.dart';
import 'package:traza_autos/core/http/cliente_api.dart';
import 'package:traza_autos/features/inspecciones/data/inspecciones_repositorio.dart';

import '../support/dio_falso.dart';

/// S7-HU-21: qué se le pide de verdad a `GET /api/inspecciones`.
///
/// Se mira la petición que sale, no lo que devuelve un doble: el filtro de autor es justo lo que dejaba al
/// coordinador de calidad con la pestaña vacía —él revisa informes, no captura ninguno— y un doble que lo ignore
/// no lo habría visto nunca.
void main() {
  late AppDatabase db;
  late CacheLecturasRepo cache;

  setUp(() {
    db = AppDatabase(NativeDatabase.memory());
    cache = CacheLecturasRepo(db);
  });

  tearDown(() => db.close());

  const inspeccion = {
    'id': 'ins-1',
    'vin': 'VIN00000000000001',
    'momento': 'salida',
    'fecha': '2026-11-09T12:00:00.000Z',
    'estado': 'cerrada',
  };

  Map<String, dynamic> pagina(List<Map<String, dynamic>> items, {int? total, int numero = 1}) =>
      {'items': items, 'total': total ?? items.length, 'pagina': numero, 'tamano': 50};

  (InspeccionesRepositorio, AdaptadorFalso) repositorio([Object? cuerpo]) {
    final adaptador = AdaptadorFalso((_) => json(200, cuerpo ?? pagina(const [inspeccion])));
    final api = ClienteApi(baseUrl: 'https://api.test', token: () async => 'jwt', dio: Dio()..httpClientAdapter = adaptador);
    return (InspeccionesRepositorio(api, cache), adaptador);
  }

  test('el inspector pide solo los suyos: el filtro de autor viaja', () async {
    final (repo, adaptador) = repositorio();

    await repo.listarRemoto('u1');

    expect(adaptador.peticiones.single.queryParameters['usuario_id'], 'u1');
  });

  test('el coordinador de calidad no manda el filtro de autor: el servidor le da los de su cliente', () async {
    final (repo, adaptador) = repositorio();

    await repo.listarRemoto('coord-1', soloSuyas: false);

    expect(adaptador.peticiones.single.queryParameters.containsKey('usuario_id'), isFalse);
  });

  test('siempre pide una página, nunca la lista entera', () async {
    final (repo, adaptador) = repositorio();

    await repo.listarRemoto('u1', pagina: 3);

    final query = adaptador.peticiones.single.queryParameters;
    expect((query['pagina'], query['tamano']), ('3', '${InspeccionesRepositorio.tamanoPagina}'));
  });

  test('buscar un VIN se lo pregunta al servidor', () async {
    final (repo, adaptador) = repositorio();

    await repo.listarRemoto('coord-1', soloSuyas: false, vin: 'VIN00000000000001');

    expect(adaptador.peticiones.single.queryParameters['vin'], 'VIN00000000000001');
  });

  test('devuelve el total que declara la página, no lo que vino en ella', () async {
    final (repo, _) = repositorio(pagina(const [inspeccion], total: 120));

    final resultado = await repo.listarRemoto('u1');

    expect((resultado.items.length, resultado.total), (1, 120));
  });

  group('qué se guarda para mirar sin señal', () {
    test('la primera página de la lista completa', () async {
      final (repo, _) = repositorio();

      await repo.listarRemoto('u1');

      expect((await repo.leerCache('u1'))?.single.id, 'ins-1');
    });

    // Guardadas, dirían sin señal que esos son todos los informes de la persona: un pedazo de la mitad de la
    // lista, o lo que coincidió con un VIN, haciéndose pasar por la lista entera.
    test('una página de más adelante, no', () async {
      final (repo, _) = repositorio(pagina(const [inspeccion], numero: 2));

      await repo.listarRemoto('u1', pagina: 2);

      expect(await repo.leerCache('u1'), isNull);
    });

    test('el resultado de una búsqueda, tampoco', () async {
      final (repo, _) = repositorio();

      await repo.listarRemoto('u1', vin: 'VIN00000000000001');

      expect(await repo.leerCache('u1'), isNull);
    });
  });
}
