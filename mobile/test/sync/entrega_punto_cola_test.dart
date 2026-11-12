import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/core/http/cliente_api.dart';
import 'package:traza_autos/features/sync/data/gateway_informes_api.dart';
import 'package:traza_autos/features/sync/domain/job.dart';
import 'package:traza_autos/features/sync/domain/procesador_cola.dart';
import 'package:traza_autos/features/viajes/data/entregas_repositorio.dart';
import 'package:traza_autos/features/viajes/domain/entrega_punto.dart';

import '../support/arnes.dart';
import '../support/dio_falso.dart';
import '../support/dobles_sync.dart';

EntregaPuntoJob entrega({String? motivo, String? firma = 'M0 0L10 10', bool fallido = false}) => EntregaPuntoJob(
      id: 'e1',
      etiqueta: 'Entrega Autonorte',
      creadoEn: 1,
      usuarioId: usuarioPrueba,
      viajeId: 'v1',
      lugarId: 'l1',
      firma: firma,
      motivo: motivo,
      fallido: fallido,
    );

void main() {
  late ColaEnMemoria cola;
  late GatewayFalso gateway;
  late ProcesadorCola procesador;

  setUp(() {
    cola = ColaEnMemoria();
    gateway = GatewayFalso();
    procesador = ProcesadorCola(cola: cola, gateway: gateway, red: RedFalsa(), archivos: ArchivosFalsos(const []));
  });

  group('el job de la entrega por punto', () {
    test('se guarda y se lee igual, con o sin motivo', () {
      final de = EntregaPuntoJob.fromJson(entrega(motivo: 'no hay inspector en el punto').toJson());
      expect((de.viajeId, de.lugarId, de.firma, de.motivo, de.unaSolaFirma), ('v1', 'l1', 'M0 0L10 10', 'no hay inspector en el punto', true));
      expect(EntregaPuntoJob.fromJson(entrega().toJson()).unaSolaFirma, isFalse);
      expect(Job.fromJson(entrega().toJson()), isA<EntregaPuntoJob>());
    });

    test('conserva sus datos al contar un intento o cambiar de dueño', () {
      final intentado = entrega().conIntento(intentos: 2, error: 'sin red', fallido: false);
      expect((intentado.intentos, intentado.viajeId, intentado.firma), (2, 'v1', 'M0 0L10 10'));
      expect(entrega().conUsuario('u2').usuarioId, 'u2');
    });
  });

  group('envío por la cola', () {
    test('la firma de una parte sale una sola vez y saca el job de la cola', () async {
      await cola.agregar(entrega());

      final r = await procesador.procesar(usuarioPrueba);

      expect(gateway.entregasEnviadas, [('v1', 'l1', null, 'M0 0L10 10')]);
      expect(cola.jobs, isEmpty);
      expect((r.enviados, r.pendientes, r.fallidos), (1, 0, 0));
    });

    test('con motivo sale el cierre con una sola firma', () async {
      await cola.agregar(entrega(motivo: 'no hay inspector en el punto'));

      await procesador.procesar(usuarioPrueba);

      expect(gateway.entregasEnviadas, [('v1', 'l1', 'no hay inspector en el punto', 'M0 0L10 10')]);
    });

    test('sin señal se queda en la cola y al volver se envía sin duplicar', () async {
      gateway.errores['firmarEntregaPunto'] = [Exception('sin red')];
      await cola.agregar(entrega());

      await procesador.procesar(usuarioPrueba);
      expect(cola.jobs, hasLength(1));
      expect(cola.jobs.single.fallido, isFalse, reason: 'una caída de red se reintenta sola');

      await procesador.procesar(usuarioPrueba);
      expect(cola.jobs, isEmpty);
      expect(gateway.entregasEnviadas, hasLength(2), reason: 'dos intentos, un solo envío completo');
    });

    test('si el servidor ya no tiene unidades pendientes, la entrega se da por hecha', () async {
      gateway.errores['firmarEntregaPunto'] = [errorApi(409, 'No hay unidades pendientes de entrega en este punto')];
      await cola.agregar(entrega());

      final r = await procesador.procesar(usuarioPrueba);

      expect(cola.jobs, isEmpty);
      expect((r.enviados, r.fallidos), (1, 0));
    });

    test('otro 409 deja el envío fallido con el motivo del servidor', () async {
      gateway.errores['firmarEntregaPunto'] = [errorApi(409, 'Aún no tienen la inspección de llegada del descargue: VIN1')];
      await cola.agregar(entrega());

      final r = await procesador.procesar(usuarioPrueba);

      expect(r.fallidos, 1);
      expect(cola.jobs.single.fallido, isTrue);
      expect(cola.jobs.single.error, contains('inspección de llegada'));
    });
  });

  group('hacia la API', () {
    (GatewayInformesApi, AdaptadorFalso) gatewayApi() {
      final adaptador = AdaptadorFalso((_) => json(200, {'viaje_id': 'v1'}));
      final api = ClienteApi(baseUrl: 'https://api.test', token: () async => 'jwt', dio: Dio()..httpClientAdapter = adaptador);
      return (GatewayInformesApi(api), adaptador);
    }

    test('la firma va al punto del viaje, solo con el trazo', () async {
      final (api, adaptador) = gatewayApi();

      await api.firmarEntregaPunto('v 1', 'l1', firma: 'M0 0L5 5');

      final peticion = adaptador.peticiones.single;
      expect((peticion.method, peticion.uri.path), ('POST', '/api/viajes/v%201/entregas/l1/firma'));
      expect(peticion.data, {'firma': 'M0 0L5 5'});
    });

    test('el cierre con una sola firma lleva el motivo y la firma', () async {
      final (api, adaptador) = gatewayApi();

      await api.cerrarEntregaConUnaFirma('v1', 'l1', motivo: 'no hay inspector', firma: 'M0 0L5 5');

      final peticion = adaptador.peticiones.single;
      expect(peticion.uri.path, '/api/viajes/v1/entregas/l1/una-firma');
      expect(peticion.data, {'motivo': 'no hay inspector', 'firma': 'M0 0L5 5'});
    });

    test('lee los puntos del viaje con sus unidades', () async {
      final adaptador = AdaptadorFalso((_) => json(200, {
            'viaje_id': 'v1',
            'viaje_estado': 'en_curso',
            'unidades_sin_destino': 0,
            'puntos': [
              {
                'viaje_id': 'v1',
                'lugar_id': 'l1',
                'lugar': 'Concesionario Autonorte',
                'ciudad': 'Bogotá',
                'unidades_total': 2,
                'unidades_pendientes': 1,
                'entregada': false,
                'unidades': [
                  {'vin': 'VIN1', 'estado': 'pendiente', 'tipo_cierre': null, 'entrega_firmada_at': '2026-10-29T10:00:00Z', 'recibe_firmada_at': null},
                  {'vin': 'VIN2', 'estado': 'cerrada', 'tipo_cierre': 'firmada'},
                ],
              },
            ],
          }));
      final repo = EntregasRepositorio(ClienteApi(baseUrl: 'https://api.test', token: () async => 'jwt', dio: Dio()..httpClientAdapter = adaptador));

      final puntos = await repo.delViaje('v1');

      expect(adaptador.peticiones.single.uri.path, '/api/viajes/v1/entregas');
      final punto = puntos.single;
      expect((punto.lugar, punto.ciudad, punto.pendientes, punto.entregada), ('Concesionario Autonorte', 'Bogotá', 1, false));
      expect(punto.porEntregar.map((u) => u.vin), ['VIN1']);
      expect(punto.unidades.first.entregaFirmada, isTrue);
      expect(punto.unidades.first.recibeFirmada, isFalse);
      expect(punto.unidades.last.cerrada, isTrue);
    });

    test('una unidad sin la inspección de llegada bloquea la firma del punto', () {
      final punto = EntregaPunto.fromJson({
        'viaje_id': 'v1',
        'lugar_id': 'l1',
        'lugar': 'Cali',
        'unidades': [
          {'vin': 'VIN1', 'estado': 'pendiente'},
          {'vin': 'VIN2', 'estado': 'sin_inspeccion'},
        ],
      });
      expect(punto.sinInspeccion.map((u) => u.vin), ['VIN2']);
      expect(punto.pendientes, 2, reason: 'sin el dato del servidor se cuentan las que no están cerradas');
    });
  });
}
