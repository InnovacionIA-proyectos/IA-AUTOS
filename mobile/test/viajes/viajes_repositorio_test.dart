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

  // Un viaje cancelado deja de llegar en la respuesta. Si la copia solo creciera, seguiría en la lista del
  // patio y alguien intentaría trabajarlo.
  test('un viaje que deja de llegar se va de la copia', () async {
    final repo = repositorio(AdaptadorFalso((_) => json(200, [viajeJson('v1'), viajeJson('v2')])));
    await repo.listarRemoto('u1');
    expect((await repo.leerCache('u1'))!, hasLength(2));

    await repositorio(AdaptadorFalso((_) => json(200, [viajeJson('v1')]))).listarRemoto('u1');

    final guardados = await repositorio(AdaptadorFalso((_) => json(200, []))).leerCache('u1');
    expect([for (final v in guardados!) v.id], ['v1']);
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

  group('terminados', () {
    test('se piden con estado=finalizado y no tocan la copia de los activos', () async {
      await repositorio(AdaptadorFalso((_) => json(200, [viajeJson('v1')]))).listarRemoto('u1');
      final guardadoAntes = await cache.leer(ViajesRepositorio.clave('u1'));

      final adaptador = AdaptadorFalso((_) => json(200, [viajeJson('v9')]));
      final terminados = await repositorio(adaptador).terminados();

      expect(adaptador.peticiones.single.uri.path, '/api/viajes/mios');
      expect(adaptador.peticiones.single.uri.queryParameters['estado'], 'finalizado');
      expect(terminados.single.codigoMtrack, 'MTK-v9');
      // Si se guardaran, quien está en el patio vería viajes que ya no puede trabajar.
      expect(await cache.leer(ViajesRepositorio.clave('u1')), guardadoAntes);
    });
  });

  group('marcarNoSale', () {
    test('manda el motivo, el hallazgo y el reemplazo, y devuelve el viaje', () async {
      final conNoSale = viajeJson('v1');
      conNoSale['vehiculos'] = <Map<String, dynamic>>[
        {
          'vin': 'VIN00000000000001',
          'marca': 'Renault',
          'modelo': 'Kwid',
          'estado_unidad': 'no_sale',
          'no_sale': {
            'motivo': 'Golpe profundo en el capó',
            'hallazgo_id': 'h1',
            'marcado_por_nombre': 'Ana Pérez',
            'fecha': '2026-10-28T15:00:00Z',
            'vin_reemplazo': 'VIN00000000000002',
          },
        },
      ];
      final adaptador = AdaptadorFalso((_) => json(200, conNoSale));

      final viaje = await repositorio(adaptador).marcarNoSale(
        'v1',
        'VIN00000000000001',
        motivo: 'Golpe profundo en el capó',
        hallazgoId: 'h1',
        vinReemplazo: 'VIN00000000000002',
      );

      final peticion = adaptador.peticiones.single;
      expect(peticion.uri.path, '/api/viajes/v1/vehiculos/VIN00000000000001/no-sale');
      expect(peticion.method, 'POST');
      final cuerpo = peticion.data as Map<String, dynamic>;
      expect((cuerpo['motivo'], cuerpo['hallazgo_id'], cuerpo['vin_reemplazo']),
          ('Golpe profundo en el capó', 'h1', 'VIN00000000000002'));

      final unidad = viaje.vehiculos.single;
      expect(unidad.sale, isFalse);
      expect(unidad.noSale!.motivo, 'Golpe profundo en el capó');
      expect(unidad.noSale!.vinReemplazo, 'VIN00000000000002');
    });

    test('sin reemplazo la clave viaja igual, en nulo: el servidor la exige presente', () async {
      final adaptador = AdaptadorFalso((_) => json(200, viajeJson('v1')));

      await repositorio(adaptador).marcarNoSale('v1', 'VIN00000000000001', motivo: 'Rechazado', hallazgoId: 'h1');

      final cuerpo = adaptador.peticiones.single.data as Map<String, dynamic>;
      expect(cuerpo.containsKey('vin_reemplazo'), isTrue);
      expect(cuerpo['vin_reemplazo'], isNull);
    });
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
