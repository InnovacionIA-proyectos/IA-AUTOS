import 'package:dio/dio.dart';
import 'package:drift/native.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/core/db/app_database.dart';
import 'package:traza_autos/core/db/cache_lecturas.dart';
import 'package:traza_autos/core/errores/error_api.dart';
import 'package:traza_autos/core/http/cliente_api.dart';
import 'package:traza_autos/features/vehiculos/data/vehiculos_repositorio.dart';

import '../support/dio_falso.dart';

/// S6-HU-02.3 CA-4: el conductor baja el inventario de cada unidad con señal y lo mira en lectura aunque después no
/// haya. La copia es por usuario: lo que ve cada rol cambia.

Map<String, dynamic> inventarioJson() => {
      'id': 'v1',
      'vin': 'VIN00000000000001',
      'marca': 'Renault',
      'modelo': 'Kwid',
      'operacion': 'nacional',
      'etapas': [],
    };

void main() {
  late AppDatabase db;
  late CacheLecturasRepo cache;
  var usuario = 'conductor-1';

  setUp(() {
    db = AppDatabase(NativeDatabase.memory());
    cache = CacheLecturasRepo(db);
    usuario = 'conductor-1';
  });

  tearDown(() => db.close());

  VehiculosRepositorio repositorio(ResponseBody Function(RequestOptions) responder) {
    final dio = Dio()..httpClientAdapter = AdaptadorFalso(responder);
    return VehiculosRepositorio(
      ClienteApi(baseUrl: 'https://api.test', token: () async => 'jwt', dio: dio),
      cache: cache,
      usuarioId: () => usuario,
    );
  }

  ResponseBody sinRed(RequestOptions _) => throw DioException(requestOptions: RequestOptions(path: '/'), type: DioExceptionType.connectionError);

  test('con señal devuelve el inventario y deja la copia guardada', () async {
    final repo = repositorio((_) => json(200, inventarioJson()));

    final inventario = await repo.inventario('VIN00000000000001');

    expect(inventario.marca, 'Renault');
    expect(await cache.leer('inventario.conductor-1.VIN00000000000001'), isNotNull);
  });

  test('sin señal devuelve la última copia', () async {
    await repositorio((_) => json(200, inventarioJson())).inventario('VIN00000000000001');

    final inventario = await repositorio(sinRed).inventario('VIN00000000000001');

    expect(inventario.vin, 'VIN00000000000001');
  });

  test('sin señal y sin copia, el error de red sale tal cual', () async {
    await expectLater(repositorio(sinRed).inventario('VIN00000000000001'), throwsA(isA<ErrorApi>().having((e) => e.status, 'estado', 0)));
  });

  test('un error del servidor nunca se tapa con una copia vieja', () async {
    await repositorio((_) => json(200, inventarioJson())).inventario('VIN00000000000001');

    await expectLater(
      repositorio((_) => json(403, {'error': 'No es de tus viajes', 'code': 'FORBIDDEN', 'message': 'No es de tus viajes'})).inventario('VIN00000000000001'),
      throwsA(isA<ErrorApi>().having((e) => e.status, 'estado', 403)),
    );
  });

  test('la copia es de quien la bajó: otra persona en el mismo equipo no la lee', () async {
    await repositorio((_) => json(200, inventarioJson())).inventario('VIN00000000000001');
    usuario = 'otra-persona';

    await expectLater(repositorio(sinRed).inventario('VIN00000000000001'), throwsA(isA<ErrorApi>()));
  });

  test('sin sesión no guarda nada', () async {
    final repo = VehiculosRepositorio(
      ClienteApi(baseUrl: 'https://api.test', token: () async => 'jwt', dio: Dio()..httpClientAdapter = AdaptadorFalso((_) => json(200, inventarioJson()))),
      cache: cache,
      usuarioId: () => null,
    );

    await repo.inventario('VIN00000000000001');

    expect(await cache.leer('inventario.null.VIN00000000000001'), isNull);
  });
}
