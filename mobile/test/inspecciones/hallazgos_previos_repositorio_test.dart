import 'package:dio/dio.dart';
import 'package:drift/native.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/core/db/app_database.dart';
import 'package:traza_autos/core/db/cache_lecturas.dart';
import 'package:traza_autos/core/http/cliente_api.dart';
import 'package:traza_autos/features/inspecciones/data/hallazgos_previos_repositorio.dart';

import '../support/dio_falso.dart';

/// Un hallazgo previo como lo manda `/api/vehiculos/{vin}/hallazgos-previos`.
Map<String, dynamic> previo(String id, {String viaje = 'MTK-900'}) => {
      'id': id,
      'zona': 'Izquierdo 7 · Espejo retrovisor',
      'cuadrante_numero': 7,
      'lado': 'izquierdo',
      'clase_dano': 'golpe',
      'tamano_dano': '1_5cm',
      'proceso': 'cargue',
      'momento': 'llegada',
      'viaje_id': 'vj-1',
      'codigo_mtrack': viaje,
      'cerrada_at': '2026-10-01T09:00:00Z',
      'evidencias': [
        {'id': 'ev-1', 'url': '/api/uploads/dano.jpg'},
      ],
    };

void main() {
  const vin = 'VIN00000000000001';
  late AppDatabase db;
  late CacheLecturasRepo cache;

  setUp(() {
    db = AppDatabase(NativeDatabase.memory());
    cache = CacheLecturasRepo(db);
  });
  tearDown(() => db.close());

  HallazgosPreviosRepositorio repositorio(AdaptadorFalso adaptador) => HallazgosPreviosRepositorio(
        ClienteApi(baseUrl: 'https://api.test', token: () async => 'jwt', dio: Dio()..httpClientAdapter = adaptador),
        cache,
      );

  test('con señal los baja de la ruta del VIN y los guarda', () async {
    final adaptador = AdaptadorFalso((_) => json(200, [previo('h1')]));

    final previos = await repositorio(adaptador).de(vin);

    expect(adaptador.peticiones.single.uri.path, '/api/vehiculos/$vin/hallazgos-previos');
    expect(previos.single.id, 'h1');
    expect(previos.single.fotoUrl, '/api/uploads/dano.jpg');
    expect(await cache.leer('hallazgos-previos:$vin'), isNotNull);
  });

  test('trae los de viajes anteriores, que es para lo que se cambió de ruta', () async {
    final adaptador = AdaptadorFalso((_) => json(200, [previo('h-otro-viaje', viaje: 'MTK-800'), previo('h1')]));

    final previos = await repositorio(adaptador).de(vin);

    expect([for (final p in previos) p.id], ['h-otro-viaje', 'h1']);
  });

  test('sin señal devuelve la última copia guardada', () async {
    await repositorio(AdaptadorFalso((_) => json(200, [previo('h1')]))).de(vin);

    final previos = await repositorio(AdaptadorFalso((_) => throw DioException.connectionError(
          requestOptions: RequestOptions(),
          reason: 'sin red',
        ))).de(vin);

    expect(previos.single.id, 'h1', reason: 'la propuesta de preexistente tiene que funcionar en el patio');
    expect(previos.single.zona, 'Izquierdo 7 · Espejo retrovisor');
    expect(previos.single.clave, '7I');
  });

  test('sin señal y sin copia no propone nada, pero tampoco rompe la captura', () async {
    final repo = repositorio(AdaptadorFalso((_) => throw DioException.connectionError(
          requestOptions: RequestOptions(),
          reason: 'sin red',
        )));

    expect(await repo.de(vin), isEmpty);
  });

  test('al conductor la API le responde 403 y se queda sin previos, sin romper nada', () async {
    final repo = repositorio(AdaptadorFalso((_) => json(403, {'detail': 'No es para el conductor'})));

    expect(await repo.de(vin), isEmpty);
  });
}
