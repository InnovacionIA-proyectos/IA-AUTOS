import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/core/http/cliente_api.dart';
import 'package:traza_autos/features/inspecciones/domain/reglas_informe.dart';
import 'package:traza_autos/features/sync/data/gateway_informes_api.dart';
import 'package:traza_autos/features/sync/domain/job.dart';
import 'package:traza_autos/features/sync/domain/procesador_cola.dart';

import '../support/arnes.dart';
import '../support/dio_falso.dart';
import '../support/dobles_sync.dart';

/// S6-HU-02.18: el motivo «No hay inspector» viaja con el informe, y el recibo sin entrega sale por la cola.

ReciboSinEntregaJob recibo({bool fallido = false}) => ReciboSinEntregaJob(
      id: 'r1',
      etiqueta: 'Recibo sin entrega',
      creadoEn: 1,
      usuarioId: usuarioPrueba,
      inspeccionId: 'insp-9',
      firma: 'M0 0L10 10',
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

  group('el motivo sin inspector en el informe', () {
    test('se guarda con el informe y los informes viejos se leen sin él', () {
      final job = informe(motivoSinInspector: 'otro', motivoSinInspectorTexto: 'El inspector se enfermó');
      final de = InformeJob.fromJson(job.toJson());
      expect((de.motivoSinInspector, de.motivoSinInspectorTexto), ('otro', 'El inspector se enfermó'));

      final viejo = job.toJson()
        ..remove('motivo_sin_inspector')
        ..remove('motivo_sin_inspector_texto');
      expect(InformeJob.fromJson(viejo).motivoSinInspector, isNull);
    });

    test('sobrevive a contar un intento o marcar progreso', () {
      final job = informe(motivoSinInspector: 'inspector_no_llego');
      expect(job.conIntento(intentos: 1, error: null, fallido: false).motivoSinInspector, 'inspector_no_llego');
      expect(job.conProgreso(cerrada: true).motivoSinInspector, 'inspector_no_llego');
    });

    test('lo arma el informe con el momento en que se eligió el motivo', () {
      final job = construirInformeJob(
        id: 'j',
        creadoEn: 1,
        usuarioId: 'u1',
        momento: 'salida',
        vin: 'VIN1',
        proceso: 'Cargue',
        etapaId: 'e1',
        fecha: DateTime.utc(2026, 10, 23, 13, 30),
        lat: 6.2442,
        lng: -75.5812,
        fotos: const [],
        hallazgos: const [],
        motivoSinInspector: 'otro',
        motivoSinInspectorTexto: 'El inspector se enfermó',
      );
      expect((job.fecha, job.lat, job.lng, job.motivoSinInspector), ('2026-10-23T13:30:00.000Z', 6.2442, -75.5812, 'otro'));
    });

    test('al enviar, el motivo viaja al crear la inspección y las normales no llevan ninguno', () async {
      await cola.agregar(informe(id: 'a', motivoSinInspector: 'otro', motivoSinInspectorTexto: 'El inspector se enfermó'));
      await cola.agregar(informe(id: 'b'));

      await procesador.procesar(usuarioPrueba);

      expect(gateway.motivosEnviados, [('otro', 'El inspector se enfermó'), (null, null)]);
    });

    test('un 400 con el motivo deja el informe fallido, con su mensaje, sin reintentar', () async {
      gateway.errores['crearInspeccion'] = [errorApi(400, 'El motivo no es válido')];
      await cola.agregar(informe(motivoSinInspector: 'otro'));

      final r = await procesador.procesar(usuarioPrueba);

      expect(r.fallidos, 1);
      expect(cola.jobs.single.error, contains('El motivo no es válido'));
    });

    test('un 409 porque el inspector ya creó la salida se muestra y no se reintenta', () async {
      gateway.errores['crearInspeccion'] = [errorApi(409, 'La etapa ya tiene su inspección de salida')];
      await cola.agregar(informe(motivoSinInspector: 'otro'));

      final r = await procesador.procesar(usuarioPrueba);

      expect(r.fallidos, 1);
      expect(cola.jobs.single.fallido, isTrue);
      expect(gateway.llamadas.where((l) => l == 'crearInspeccion'), hasLength(1), reason: 'no se vuelve a intentar');
    });
  });

  group('el recibo sin entrega por la cola', () {
    test('se guarda y se lee igual', () {
      final de = ReciboSinEntregaJob.fromJson(recibo().toJson());
      expect((de.inspeccionId, de.firma, de.usuarioId), ('insp-9', 'M0 0L10 10', usuarioPrueba));
      expect(Job.fromJson(recibo().toJson()), isA<ReciboSinEntregaJob>());
      expect(recibo().conUsuario('u2').usuarioId, 'u2');
    });

    test('sale a nombre de su dueño y saca el job de la cola', () async {
      await cola.agregar(recibo());

      final r = await procesador.procesar(usuarioPrueba);

      expect(gateway.recibosSinEntrega, [('insp-9', 'M0 0L10 10')]);
      expect((r.enviados, r.fallidos), (1, 0));
      expect(cola.jobs, isEmpty);
    });

    test('el de otra persona no sale desde esta sesión', () async {
      await cola.agregar(recibo().conUsuario('otra-persona'));

      await procesador.procesar(usuarioPrueba);

      expect(gateway.recibosSinEntrega, isEmpty);
      expect(cola.jobs, hasLength(1));
    });

    test('un 409 (ya tiene tipo de cierre) queda fallido con el motivo del servidor', () async {
      gateway.errores['firmarReciboSinEntrega'] = [errorApi(409, 'La transferencia ya tiene tipo de cierre')];
      await cola.agregar(recibo());

      final r = await procesador.procesar(usuarioPrueba);

      expect(r.fallidos, 1);
      expect(cola.jobs.single.error, contains('ya tiene tipo de cierre'));
    });
  });

  group('hacia la API', () {
    (GatewayInformesApi, AdaptadorFalso) gatewayApi() {
      final adaptador = AdaptadorFalso((_) => json(201, {'id': 'insp-1'}));
      final api = ClienteApi(baseUrl: 'https://api.test', token: () async => 'jwt', dio: Dio()..httpClientAdapter = adaptador);
      return (GatewayInformesApi(api), adaptador);
    }

    test('la inspección lleva el motivo y su texto solo en la excepción', () async {
      final (api, adaptador) = gatewayApi();

      await api.crearInspeccion(
        etapaId: 'e1',
        momento: 'salida',
        fecha: '2026-10-23T13:30:00Z',
        lat: 6.2,
        lng: -75.5,
        motivoSinInspector: 'otro',
        motivoSinInspectorTexto: 'El inspector se enfermó',
      );
      await api.crearInspeccion(etapaId: 'e1', momento: 'salida', fecha: '2026-10-23T13:30:00Z', lat: 6.2, lng: -75.5);

      final conMotivo = adaptador.peticiones.first.data as Map<String, dynamic>;
      expect((conMotivo['motivo_sin_inspector'], conMotivo['motivo_sin_inspector_texto']), ('otro', 'El inspector se enfermó'));
      // El servidor responde 400 «Campo no admitido» si llegan en cualquier otra inspección, aunque sea en null.
      final normal = adaptador.peticiones.last.data as Map<String, dynamic>;
      expect(normal.keys, isNot(contains('motivo_sin_inspector')));
      expect(normal.keys, isNot(contains('motivo_sin_inspector_texto')));
    });

    test('un motivo que no pide texto no manda el campo', () async {
      final (api, adaptador) = gatewayApi();

      await api.crearInspeccion(
        etapaId: 'e1',
        momento: 'salida',
        fecha: '2026-10-23T13:30:00Z',
        lat: 6.2,
        lng: -75.5,
        motivoSinInspector: 'inspector_no_llego',
      );

      final cuerpo = adaptador.peticiones.single.data as Map<String, dynamic>;
      expect(cuerpo['motivo_sin_inspector'], 'inspector_no_llego');
      expect(cuerpo.keys, isNot(contains('motivo_sin_inspector_texto')));
    });

    test('el recibo sin entrega va a la transferencia de esa inspección, solo con la firma', () async {
      final (api, adaptador) = gatewayApi();

      await api.firmarReciboSinEntrega('insp 9', firma: 'M0 0L5 5');

      final peticion = adaptador.peticiones.single;
      expect((peticion.method, peticion.uri.path), ('POST', '/api/inspecciones/insp%209/transferencia/sin-entrega'));
      expect(peticion.data, {'firma': 'M0 0L5 5'});
    });
  });
}
