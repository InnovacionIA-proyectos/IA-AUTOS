import 'dart:convert';

import 'package:dio/dio.dart';
import 'package:drift/native.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/core/db/app_database.dart';
import 'package:traza_autos/core/db/cache_lecturas.dart';
import 'package:traza_autos/core/http/cliente_api.dart';
import 'package:traza_autos/features/viajes/data/viajes_repositorio.dart';

import '../support/dio_falso.dart';

Map<String, dynamic> viajeJson(String id, {String placa = 'ABC123', String? conductor = 'Ana Pérez'}) => {
      'id': id,
      'codigo_mtrack': 'MTK-$id',
      'placa': placa,
      'estado': 'en_curso',
      'unidad': 'Niñera',
      'trailer': 'T-9',
      'conductor_nombre': conductor,
      'inspector_nombre': 'Luis',
      'origen': 'CEDI 1',
      'destino': 'Concesionario',
      'vehiculos': [
        {'vin': 'VIN00000000000001', 'marca': 'Renault', 'modelo': 'Kwid', 'color': 'Blanco', 'operacion': 'nacional'},
      ],
    };

void main() {
  late AppDatabase db;
  late CacheLecturasRepo cache;

  setUp(() {
    db = AppDatabase(NativeDatabase.memory());
    cache = CacheLecturasRepo(db);
  });

  tearDown(() => db.close());

  ViajesRepositorio repositorio(AdaptadorFalso adaptador) {
    final dio = Dio()..httpClientAdapter = adaptador;
    return ViajesRepositorio(ClienteApi(baseUrl: 'https://api.test', token: () async => 'jwt', dio: dio), cache);
  }

  test('listarRemoto pide mis viajes y deja la copia guardada', () async {
    final adaptador = AdaptadorFalso((_) => json(200, [viajeJson('v1')]));

    final viajes = await repositorio(adaptador).listarRemoto('u1');

    expect(adaptador.peticiones.single.uri.path, '/api/viajes/mios');
    expect(viajes.single.placa, 'ABC123');
    expect(viajes.single.unidad, 'Niñera');
    expect(viajes.single.vehiculos.single.vin, 'VIN00000000000001');
    expect(await cache.leer(ViajesRepositorio.clave('u1')), isNotNull);
  });

  test('la copia es por usuario: nadie lee los viajes de otra persona en un equipo compartido', () async {
    await repositorio(AdaptadorFalso((_) => json(200, [viajeJson('v1')]))).listarRemoto('u1');

    expect(await repositorio(AdaptadorFalso((_) => json(200, []))).leerCache('u2'), isNull);
    expect(await repositorio(AdaptadorFalso((_) => json(200, []))).leerCache('u1'), isNotNull);
  });

  test('una caché ilegible se trata como ausente', () async {
    await cache.escribir(ViajesRepositorio.clave('u1'), 'esto no es json');

    expect(await repositorio(AdaptadorFalso((_) => json(200, []))).leerCache('u1'), isNull);
  });

  group('deLaEtapa', () {
    test('sale de la copia guardada, sin tocar la red: es lo que hace que la captura funcione sin señal', () async {
      await repositorio(AdaptadorFalso((_) => json(200, [viajeJson('v1')]))).listarRemoto('u1');
      final sinRed = AdaptadorFalso((o) => throw DioException.connectionError(requestOptions: o, reason: 'sin red'));

      final viaje = await repositorio(sinRed).deLaEtapa('u1', 'v1');

      expect(viaje!.placa, 'ABC123');
      expect(viaje.conductorNombre, 'Ana Pérez');
      expect(sinRed.peticiones, isEmpty);
    });

    test('si no está entre mis viajes se pide suelto (un administrador no es conductor ni inspector)', () async {
      final adaptador = AdaptadorFalso((o) => o.path.endsWith('/v9') ? json(200, viajeJson('v9', placa: 'XYZ789')) : json(200, []));

      final viaje = await repositorio(adaptador).deLaEtapa('u1', 'v9');

      expect(viaje!.placa, 'XYZ789');
      expect(adaptador.peticiones.last.uri.path, '/api/viajes/v9');
    });

    test('sin copia y sin red devuelve null, y la captura sigue siendo posible', () async {
      final sinRed = AdaptadorFalso((o) => throw DioException.connectionError(requestOptions: o, reason: 'sin red'));

      expect(await repositorio(sinRed).deLaEtapa('u1', 'v1'), isNull);
    });
  });

  test('ida y vuelta por la caché conserva los campos que muestra la captura', () async {
    await repositorio(AdaptadorFalso((_) => json(200, [viajeJson('v1')]))).listarRemoto('u1');

    final guardado = jsonDecode((await cache.leer(ViajesRepositorio.clave('u1')))!) as List<dynamic>;
    final viaje = (await repositorio(AdaptadorFalso((_) => json(200, []))).leerCache('u1'))!.single;

    expect(guardado.length, 1);
    expect((viaje.placa, viaje.trailer, viaje.unidad, viaje.conductorNombre), ('ABC123', 'T-9', 'Niñera', 'Ana Pérez'));
  });
}
