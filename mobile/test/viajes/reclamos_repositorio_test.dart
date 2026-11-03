import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/core/errores/error_api.dart';
import 'package:traza_autos/core/http/cliente_api.dart';
import 'package:traza_autos/features/viajes/data/reclamos_repositorio.dart';
import 'package:traza_autos/features/viajes/domain/pendiente.dart';

import '../support/dio_falso.dart';

(ReclamosRepositorio, AdaptadorFalso) repositorio(ResponseBody Function(RequestOptions) responder) {
  final adaptador = AdaptadorFalso(responder);
  final api = ClienteApi(baseUrl: 'https://api.test', token: () async => 'jwt', dio: Dio()..httpClientAdapter = adaptador);
  return (ReclamosRepositorio(api), adaptador);
}

Map<String, Object?> fila({String estado = 'disponible', String proceso = 'cargue', Object? por}) => {
      'viaje_id': 'v1',
      'codigo_mtrack': 'MTK-1',
      'placa': 'ABC123',
      'proceso': proceso,
      'lugar_id': 'l1',
      'lugar': 'CEDI Envigado',
      'fecha_programada': '2026-10-29T11:00:00Z',
      'estado': estado,
      'reclamado_por': por == null ? null : 'u2',
      'reclamado_por_nombre': por,
      'reclamado_at': null,
      'unidades': 3,
    };

void main() {
  group('lo que el servidor dice que está pendiente', () {
    test('lee los tres estados con quién tiene cada camión', () async {
      final (repo, adaptador) = repositorio((_) => json(200, [
            fila(),
            fila(estado: 'en_revision', por: 'Andrés'),
            fila(estado: 'mio', proceso: 'entrega'),
          ]));

      final lista = await repo.pendientes();

      expect(adaptador.peticiones.single.uri.path, '/api/viajes/pendientes');
      expect(lista.map((p) => p.estado), [EstadoPendiente.disponible, EstadoPendiente.enRevision, EstadoPendiente.mio]);
      expect(lista[1].reclamadoPorNombre, 'Andrés');
      expect(lista[2].esEntrega, isTrue);
      expect((lista[0].placa, lista[0].lugar, lista[0].unidades), ('ABC123', 'CEDI Envigado', 3));
      expect(lista[0].fechaProgramada, DateTime.utc(2026, 10, 29, 11));
    });

    test('un inspector sin puntos recibe una lista vacía', () async {
      final (repo, _) = repositorio((_) => json(200, <Object>[]));
      expect(await repo.pendientes(), isEmpty);
    });

    test('un estado que no conoce se trata como disponible: el servidor decide al reclamar', () {
      expect(EstadoPendiente.desde('algo_nuevo'), EstadoPendiente.disponible);
      expect(EstadoPendiente.desde(null), EstadoPendiente.disponible);
    });
  });

  group('reclamar y liberar', () {
    test('reclamar manda el proceso al viaje', () async {
      final (repo, adaptador) = repositorio((_) => json(201, {'id': 'r1'}));

      await repo.reclamar('v 1', 'cargue');

      final peticion = adaptador.peticiones.single;
      expect((peticion.method, peticion.uri.path), ('POST', '/api/viajes/v%201/reclamos'));
      expect(peticion.data, {'proceso': 'cargue'});
    });

    test('un 409 llega con el mensaje del servidor, tal cual', () async {
      final (repo, _) = repositorio(
        (_) => json(409, {'error': 'Ya fue tomado por otro inspector', 'code': 'CONFLICT', 'message': 'Ya fue tomado por otro inspector'}),
      );

      await expectLater(
        repo.reclamar('v1', 'cargue'),
        throwsA(isA<ErrorApi>().having((e) => (e.status, e.message), 'estado y mensaje', (409, 'Ya fue tomado por otro inspector'))),
      );
    });

    test('liberar borra el reclamo de ese proceso', () async {
      final (repo, adaptador) = repositorio((_) => json(200, {'id': 'r1', 'activo': false}));

      await repo.liberar('v1', 'entrega');

      final peticion = adaptador.peticiones.single;
      expect((peticion.method, peticion.uri.path), ('DELETE', '/api/viajes/v1/reclamos/entrega'));
    });

    test('el viaje de un pendiente se pide suelto, no de la copia de Mis viajes', () async {
      final (repo, adaptador) = repositorio((_) => json(200, {
            'id': 'v1',
            'codigo_mtrack': 'MTK-1',
            'placa': 'ABC123',
            'estado': 'programado',
            'vehiculos': [
              {'vin': 'VIN1', 'marca': 'Renault'},
            ],
          }));

      final viaje = await repo.viaje('v1');

      expect(adaptador.peticiones.single.uri.path, '/api/viajes/v1');
      expect(viaje.vehiculos.single.vin, 'VIN1');
    });
  });
}
