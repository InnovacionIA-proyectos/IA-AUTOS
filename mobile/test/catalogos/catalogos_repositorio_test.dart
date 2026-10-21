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

  test('el validador se devuelve tal como lo mandó el servidor, no reconstruido desde la versión', () async {
    // Un proxy puede reescribir el ETag (por ejemplo añadiendo -gzip). Armarlo a mano desde `version` nunca
    // casaría y se bajaría el catálogo completo en cada revalidación.
    final adaptador = AdaptadorFalso(
      (opciones) => opciones.headers.containsKey('If-None-Match')
          ? noModificado
          : ResponseBody.fromString(
              jsonEncode({
                'version': 'v1',
                'catalogos': {
                  Catalogo.clasesDano: [
                    {'id': 'i1', 'codigo': 'golpe', 'nombre': 'Golpe', 'orden': 1, 'activo': true},
                  ],
                },
              }),
              200,
              headers: {
                Headers.contentTypeHeader: ['application/json'],
                'etag': ['"v1-gzip"'],
              },
            ),
    );
    final repo = repositorio(adaptador);
    await repo.sincronizar('u1');

    await repo.sincronizar('u1');

    expect(enviado(adaptador, 1), '"v1-gzip"');
  });

  test('una copia guardada por una versión anterior, sin etag, se sigue revalidando bien', () async {
    // Formato de 8b680d2: {version, usuarioId, catalogos}. Al no haber etag se arma desde la versión, que es
    // exactamente lo que compara el backend.
    await cache.escribir(
      CatalogosRepositorio.clave,
      jsonEncode({
        'version': 'v1',
        'usuarioId': 'u1',
        'catalogos': {
          Catalogo.clasesDano: [
            {'id': 'i1', 'codigo': 'golpe', 'nombre': 'Golpe', 'orden': 1, 'activo': true},
          ],
        },
      }),
    );
    final adaptador = AdaptadorFalso((opciones) => opciones.headers.containsKey('If-None-Match') ? noModificado : cuerpo('v2'));

    final catalogos = await repositorio(adaptador).sincronizar('u1');

    expect(enviado(adaptador, 0), '"v1"', reason: 'la copia vieja debe seguir sirviendo para revalidar');
    expect(catalogos.version, 'v1', reason: 'el 304 la conserva, no se descarga de nuevo');
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

  group('una respuesta 200 enganosa no pisa la copia anterior', () {
    // Un portal cautivo o un balanceador responden 200 con un sobre JSON propio. redEstable() solo mira el tipo de
    // conexion, asi que la peticion sale igual y el cuerpo llega con pinta de valido.
    final enganos = <String, String>{
      'una lista en vez de un objeto': '[]',
      'un sobre de error del proxy': '{"message":"Authentication required"}',
      'un objeto sin la clave catalogos': '{"version":"v9"}',
      'catalogos vacio': '{"version":"v9","catalogos":{}}',
      'catalogos que no es un objeto': '{"version":"v9","catalogos":[]}',
      // Un backend a medio desplegar o una base recién migrada: hay claves, pero ningún valor. Si se guardara,
      // su ETag haría que la siguiente revalidación respondiera 304 y la copia inservible quedaría clavada.
      'catalogos con todas las listas vacías': '{"version":"v9","catalogos":{"clases-dano":[],"tamanos-dano":[]}}',
    };

    for (final engano in enganos.entries) {
      test(engano.key, () async {
        final buena = AdaptadorFalso((_) => cuerpo('v1'));
        await repositorio(buena).sincronizar('u1');
        final mala = AdaptadorFalso((_) => ResponseBody.fromString(engano.value, 200, headers: {
              Headers.contentTypeHeader: ['application/json'],
            }));

        await expectLater(repositorio(mala).sincronizar('u1'), throwsA(isA<FormatException>()));

        final conservada = await repositorio(buena).leerCache('u1');
        expect(conservada, isNotNull, reason: 'la copia buena debe seguir ahi');
        expect(conservada!.version, 'v1');
        expect(conservada.etiqueta(Catalogo.clasesDano, 'golpe'), 'Golpe');
      });
    }
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
