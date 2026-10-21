import 'dart:convert';

import 'package:dio/dio.dart';
import 'package:drift/native.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/core/db/app_database.dart';
import 'package:traza_autos/core/db/cache_lecturas.dart';
import 'package:traza_autos/core/http/cliente_api.dart';
import 'package:traza_autos/features/catalogos/data/catalogos_repositorio.dart';
import 'package:traza_autos/features/catalogos/domain/catalogos.dart';

import '../support/dio_falso.dart';

ResponseBody cuerpo(String version) => ResponseBody.fromString(
      jsonEncode({
        'version': version,
        'catalogos': {
          Catalogo.clasesDano: [
            {'id': 'i1', 'codigo': 'golpe', 'nombre': 'Golpe', 'orden': 1, 'activo': true},
          ],
        },
      }),
      200,
      headers: {
        Headers.contentTypeHeader: ['application/json'],
        'etag': ['"$version"'],
      },
    );

ResponseBody get noModificado => ResponseBody.fromString('', 304, headers: {
      'etag': ['"v1"'],
    });

void main() {
  late AppDatabase db;
  late CacheLecturasRepo cache;

  setUp(() {
    db = AppDatabase(NativeDatabase.memory());
    cache = CacheLecturasRepo(db);
  });

  tearDown(() => db.close());

  CatalogosRepositorio repositorio(AdaptadorFalso adaptador) {
    final dio = Dio()..httpClientAdapter = adaptador;
    return CatalogosRepositorio(
      ClienteApi(baseUrl: 'https://api.test', token: () async => 'jwt', dio: dio),
      cache,
    );
  }

  String? enviado(AdaptadorFalso adaptador, int indice) =>
      adaptador.peticiones[indice].headers['If-None-Match'] as String?;

  test('la primera descarga no condiciona nada y deja la copia guardada', () async {
    final adaptador = AdaptadorFalso((_) => cuerpo('v1'));

    final catalogos = await repositorio(adaptador).sincronizar('u1');

    expect(enviado(adaptador, 0), isNull);
    expect(catalogos.version, 'v1');
    expect(catalogos.etiqueta(Catalogo.clasesDano, 'golpe'), 'Golpe');
    expect(await cache.leer(CatalogosRepositorio.clave), isNotNull);
  });

  test('con copia propia revalida con la versión entre comillas y un 304 conserva lo guardado', () async {
    final adaptador = AdaptadorFalso((opciones) => opciones.headers.containsKey('If-None-Match') ? noModificado : cuerpo('v1'));
    final repo = repositorio(adaptador);
    await repo.sincronizar('u1');

    final segunda = await repo.sincronizar('u1');

    expect(enviado(adaptador, 1), '"v1"');
    expect(segunda.version, 'v1');
    expect(segunda.etiqueta(Catalogo.clasesDano, 'golpe'), 'Golpe');
  });

  test('una caché ilegible se trata como ausente: se descarga completa, sin condicionar', () async {
    await cache.escribir(CatalogosRepositorio.clave, 'esto no es json');
    final adaptador = AdaptadorFalso((_) => cuerpo('v2'));

    final catalogos = await repositorio(adaptador).sincronizar('u1');

    expect(enviado(adaptador, 0), isNull, reason: 'mandar un ETag sin copia utilizable deja la app en 304 eterno');
    expect(catalogos.version, 'v2');
  });

  test('una copia de otro usuario se descarga completa y la fila queda sellada con el usuario actual', () async {
    final adaptador = AdaptadorFalso((_) => cuerpo('v1'));
    await repositorio(adaptador).sincronizar('u1');

    final deU2 = await repositorio(adaptador).sincronizar('u2');

    expect(enviado(adaptador, 1), isNull, reason: 'con 304 nunca recibiría los catálogos filtrados para u2');
    expect(deU2.usuarioId, 'u2');
    final guardado = jsonDecode((await cache.leer(CatalogosRepositorio.clave))!) as Map<String, dynamic>;
    expect(guardado['usuarioId'], 'u2');
  });

  test('un 304 sin copia que conservar se reintenta sin la condición', () async {
    var primera = true;
    final adaptador = AdaptadorFalso((_) {
      if (primera) {
        primera = false;
        return noModificado;
      }
      return cuerpo('v3');
    });

    final catalogos = await repositorio(adaptador).sincronizar('u1');

    expect(adaptador.peticiones.length, 2);
    expect(catalogos.version, 'v3');
  });

  test('un cuerpo con forma inesperada no pisa la copia anterior', () async {
    final buena = AdaptadorFalso((_) => cuerpo('v1'));
    await repositorio(buena).sincronizar('u1');
    final mala = AdaptadorFalso((_) => ResponseBody.fromString('[]', 200, headers: {
          Headers.contentTypeHeader: ['application/json'],
        }));

    await expectLater(repositorio(mala).sincronizar('u9'), throwsA(isA<FormatException>()));

    final conservada = await repositorio(buena).leerCache('u1');
    expect(conservada!.version, 'v1');
  });

  test('sin señal no hay descarga, pero la copia guardada sigue disponible', () async {
    final adaptador = AdaptadorFalso((_) => cuerpo('v1'));
    await repositorio(adaptador).sincronizar('u1');

    // La misma base, un repositorio nuevo y la red caída: es lo que ocurre al abrir la app en el patio.
    final sinRed = AdaptadorFalso((opciones) => throw DioException.connectionError(requestOptions: opciones, reason: 'sin red'));
    final guardada = await repositorio(sinRed).leerCache('u1');

    expect(guardada, isNotNull);
    expect(guardada!.etiqueta(Catalogo.clasesDano, 'golpe'), 'Golpe');
  });
}
