import 'dart:convert';

import 'package:dio/dio.dart';
import 'package:drift/native.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/core/db/app_database.dart';
import 'package:traza_autos/core/db/cache_lecturas.dart';
import 'package:traza_autos/core/errores/error_api.dart';
import 'package:traza_autos/core/http/cliente_api.dart';
import 'package:traza_autos/features/sync/data/gateway_informes_api.dart';
import 'package:traza_autos/features/sync/domain/job.dart';
import 'package:traza_autos/features/sync/domain/procesador_cola.dart';
import 'package:traza_autos/features/viajes/data/entregas_repositorio.dart';
import 'package:traza_autos/features/viajes/domain/entrega_punto.dart';

import '../support/dio_falso.dart';
import '../support/dobles_sync.dart';

/// S8-HU-05.2: «No hay inspector» en la parada va por la cola con la hora y el GPS del celular, y reenviarla no crea
/// otra espera; lo que el servidor rechaza después queda con su motivo para explicarlo; la parada guarda su copia sin
/// señal para que el reloj siga contando.

SinInspectorParadaJob activacion({bool fallido = false, String? codigo}) => SinInspectorParadaJob(
      id: 'a1',
      etiqueta: 'No hay inspector · Puerto de Cartagena',
      creadoEn: 1,
      usuarioId: usuarioPrueba,
      viajeId: 'v1',
      lugarId: 'l1',
      motivo: 'otro',
      motivoTexto: 'El inspector se enfermó',
      fechaMomento: '2026-11-26T08:00:00-05:00',
      lat: 10.39,
      lng: -75.51,
      precisionM: 8,
      avisoMinutos: 15,
      planBMinutos: 30,
      fallido: fallido,
      codigoError: codigo,
    );

ErrorApi rechazo(int status, String motivo, String mensaje) =>
    ErrorApi(message: mensaje, code: status == 403 ? 'FORBIDDEN' : 'CONFLICT', status: status, details: {'motivo': motivo});

EntregaPuntoJob planB() => const EntregaPuntoJob(
      id: 'e1',
      etiqueta: 'Entrega Puerto de Cartagena',
      creadoEn: 1,
      usuarioId: usuarioPrueba,
      viajeId: 'v1',
      lugarId: 'l1',
      firma: 'M0 0L10 10',
      fechaMomento: '2026-11-26T09:20:00-05:00',
      lat: 10.39,
      lng: -75.51,
      precisionM: 8,
      recibeNombre: 'Andrés Pardo',
      recibeCedula: '79123456',
      firmaRecibe: 'M1 1L9 9',
      rechazos: [RechazoCarro(vin: 'VIN2', hallazgoId: 'h1')],
      reservas: [ReservaCarro(vin: 'VIN1', observaciones: 'Sin manual')],
    );

void main() {
  late ColaEnMemoria cola;
  late GatewayFalso gateway;
  late RedFalsa red;
  late ProcesadorCola procesador;

  setUp(() {
    cola = ColaEnMemoria();
    gateway = GatewayFalso();
    red = RedFalsa();
    procesador = ProcesadorCola(cola: cola, gateway: gateway, red: red, archivos: ArchivosFalsos(const []));
  });

  group('la activación de «No hay inspector»', () {
    test('sobrevive al reinicio de la app: se guarda y se lee igual, con los X e Y de la parada', () {
      final leida = Job.fromJson(jsonDecode(jsonEncode(activacion().toJson())) as Map<String, dynamic>);

      expect(leida, isA<SinInspectorParadaJob>());
      final job = leida as SinInspectorParadaJob;
      expect((job.viajeId, job.lugarId, job.motivo, job.motivoTexto), ('v1', 'l1', 'otro', 'El inspector se enfermó'));
      expect((job.fechaMomento, job.lat, job.lng, job.precisionM), ('2026-11-26T08:00:00-05:00', 10.39, -75.51, 8.0));
      expect((job.avisoMinutos, job.planBMinutos, job.usuarioId), (15, 30, usuarioPrueba));
    });

    // CA-2: sin señal se guarda y se envía sola, con la hora y el GPS del momento de tocarla.
    test('sin señal espera; con señal sale sola con la hora y el GPS del celular', () async {
      red.estable = false;
      await cola.agregar(activacion());

      await procesador.procesar(usuarioPrueba);
      expect(gateway.activacionesEnviadas, isEmpty);
      expect(cola.jobs, hasLength(1));

      red.estable = true;
      await procesador.procesar(usuarioPrueba);

      final enviada = gateway.activacionesEnviadas.single;
      expect((enviada.viajeId, enviada.lugarId, enviada.motivo, enviada.motivoTexto), ('v1', 'l1', 'otro', 'El inspector se enfermó'));
      expect(enviada.momento, (fecha: '2026-11-26T08:00:00-05:00', lat: 10.39, lng: -75.51, precision: 8.0));
      expect(cola.jobs, isEmpty);
    });

    // CA-2: reintentar no crea otra espera: lo que sale otra vez es la misma activación, con la misma hora.
    test('un fallo pasajero se reintenta con la misma activación, sin crear otra', () async {
      gateway.errores['activarSinInspector'] = [errorHttp(503, 'mantenimiento')];
      await cola.agregar(activacion());

      await procesador.procesar(usuarioPrueba);
      expect(cola.jobs.single.fallido, isFalse);
      await procesador.procesar(usuarioPrueba);

      expect(gateway.activacionesEnviadas, hasLength(2));
      expect(gateway.activacionesEnviadas.map((a) => a.momento.fecha).toSet(), {'2026-11-26T08:00:00-05:00'});
      expect(cola.jobs, isEmpty);
    });

    test('rechazada por la parada queda con su motivo, no con el código genérico', () async {
      gateway.errores['activarSinInspector'] = [
        rechazo(409, MotivoParada.paradaSinInspectores, 'La parada no tiene inspectores asignados'),
      ];
      await cola.agregar(activacion());

      await procesador.procesar(usuarioPrueba);

      final job = cola.jobs.single;
      expect((job.fallido, job.codigoError), (true, MotivoParada.paradaSinInspectores));
      expect(explicacionDeLaActivacionRechazada(job.codigoError), 'Esta parada no tiene inspectores: aquí recibe la persona del lugar');
    });

    // CA-6: agotar los intentos sin respuesta no es un rechazo del servidor.
    test('si agota los intentos sin respuesta queda «sin respuesta», no como un rechazo', () async {
      final procesadorCorto = ProcesadorCola(cola: cola, gateway: gateway, red: red, archivos: ArchivosFalsos(const []), maxIntentos: 1);
      gateway.errores['activarSinInspector'] = [errorHttp(503, 'mantenimiento')];
      gateway.errores['firmarEntregaPunto'] = [errorHttp(503, 'mantenimiento')];
      await cola.agregar(activacion());
      await cola.agregar(planB());

      await procesadorCorto.procesar(usuarioPrueba);

      expect(cola.jobs.map((j) => (j.fallido, j.codigoError)), [(true, codigoSinRespuesta), (true, codigoSinRespuesta)]);
      expect(explicacionDelRechazoTardio(codigoSinRespuesta), 'No hubo respuesta del servidor tras varios intentos: vuelve a intentarlo');
    });

    test('va a la ruta de la parada con el motivo, la hora y el GPS, y nada más', () async {
      final adaptador = AdaptadorFalso((_) => json(201, {'id': 'esp-1', 'estado': 'esperando'}));
      final api = GatewayInformesApi(
        ClienteApi(baseUrl: 'https://api.test', token: () async => 'jwt', dio: Dio()..httpClientAdapter = adaptador),
      );

      await api.activarSinInspector('v1', 'l 1', motivo: 'inspector_no_llego', fechaMomento: '2026-11-26T08:00:00-05:00');
      await api.activarSinInspector(
        'v1',
        'l1',
        motivo: 'otro',
        motivoTexto: 'El inspector se enfermó',
        fechaMomento: '2026-11-26T08:00:00-05:00',
        lat: 10.39,
        lng: -75.51,
        precisionM: 8,
      );

      final (sinGps, conGps) = (adaptador.peticiones.first, adaptador.peticiones.last);
      expect((sinGps.method, sinGps.uri.path), ('POST', '/api/viajes/v1/entregas/l%201/sin-inspector'));
      expect(sinGps.data, {'motivo_sin_inspector': 'inspector_no_llego', 'fecha_momento': '2026-11-26T08:00:00-05:00'});
      expect(conGps.data, {
        'motivo_sin_inspector': 'otro',
        'motivo_sin_inspector_texto': 'El inspector se enfermó',
        'fecha_momento': '2026-11-26T08:00:00-05:00',
        'lat': 10.39,
        'lng': -75.51,
        'precision_m': 8.0,
      });
    });
  });

  // CA-6: lo que el servidor rechaza después queda con su motivo para explicarlo en «Cola» y en la parada.
  group('el envío de la parada rechazado después', () {
    for (final (motivo, explicacion) in [
      (MotivoParada.entregaTomada, 'Mientras se enviaba, un inspector del punto tomó la entrega: firma con él. Tu firma se conserva'),
      (MotivoParada.planBNoHabilitado, 'Aún no se podía cerrar sin inspector del punto: espera y vuelve a intentarlo'),
      (MotivoParada.sinReceptor, 'Aquí firma el inspector del punto: pídele que tome la entrega en su app'),
      (MotivoParada.entregaCerradaSinInspector, 'Esta entrega ya se cerró sin inspector del punto'),
    ]) {
      test('$motivo queda como su motivo y se explica sin el texto del servidor', () async {
        gateway.errores['firmarEntregaPunto'] = [rechazo(409, motivo, 'texto crudo del servidor')];
        await cola.agregar(planB());

        await procesador.procesar(usuarioPrueba);

        final job = cola.jobs.single;
        expect((job.fallido, job.codigoError), (true, motivo));
        expect(explicacionDelRechazoTardio(job.codigoError), explicacion);
      });
    }

    test('un motivo desconocido se dice como «El servidor rechazó la entrega»', () async {
      gateway.errores['firmarEntregaPunto'] = [rechazo(409, 'motivo_nuevo', 'texto crudo')];
      await cola.agregar(planB());

      await procesador.procesar(usuarioPrueba);

      expect(explicacionDelRechazoTardio(cola.jobs.single.codigoError), 'El servidor rechazó la entrega');
    });

    // «Tu firma se conserva»: la del conductor sale sola para firmar con el inspector que tomó la entrega.
    test('con la entrega tomada, la firma se conserva y sale sola, sin la persona del lugar ni rechazos', () async {
      await cola.agregar(planB().conIntento(intentos: 1, error: 'x', codigo: MotivoParada.entregaTomada, fallido: true));

      await cola.guardar((cola.jobs.single as EntregaPuntoJob).soloMiFirma());
      await procesador.procesar(usuarioPrueba);

      final enviada = gateway.entregasEnviadas.single;
      expect((enviada.firma, enviada.motivoCodigo), ('M0 0L10 10', null));
      expect(enviada.rechazos, isEmpty);
      expect(enviada.reservas, isEmpty);
      expect(enviada.momento, (fecha: '2026-11-26T09:20:00-05:00', lat: 10.39, lng: -75.51, precision: 8.0));
      expect(gateway.externosEnviados.single, (null, null, null));
      expect(cola.jobs, isEmpty);
    });

    // CA-9: el 409 `llegada_del_inspector_del_punto` de la revisión del conductor se ve con el mensaje del servidor.
    test('la revisión de llegada rechazada por ser del inspector del punto dice lo que dijo el servidor', () async {
      const mensaje = 'La revisión de llegada en esta parada la hace el inspector del punto: podrás hacerla sin él en 5 minutos';
      gateway.errores['crearInspeccion'] = [rechazo(409, 'llegada_del_inspector_del_punto', mensaje)];
      await cola.agregar(informe(id: 'i1'));

      await procesador.procesar(usuarioPrueba);

      final job = cola.jobs.single;
      expect((job.fallido, job.error), (true, mensaje));
    });
  });

  // CA-3: la copia sin señal de la parada, con su espera y sus plazos.
  group('la copia sin señal de la parada', () {
    late AppDatabase db;
    late CacheLecturasRepo cache;

    setUp(() {
      db = AppDatabase(NativeDatabase.memory());
      cache = CacheLecturasRepo(db);
    });
    tearDown(() => db.close());

    final parada = {
      'viaje_id': 'v1',
      'lugar_id': 'l1',
      'lugar': 'Puerto de Cartagena',
      'lugar_tiene_inspector': true,
      'receptor': 'sin_receptor',
      'conductor_llego': true,
      'espera': {
        'id': 'esp-1',
        'estado': 'esperando',
        'activada_at': '2026-11-26T13:00:00Z',
        'aviso_desde': '2026-11-26T13:15:00Z',
        'aviso_at': null,
        'plan_b_desde': '2026-11-26T13:45:00Z',
        'plan_b_habilitado': false,
      },
      'espera_aviso_minutos': 15,
      'espera_plan_b_minutos': 30,
      'danos_por_calificar': <dynamic>[],
      'unidades': [
        {'vin': 'VIN1', 'estado': 'pendiente', 'inspeccion_id': 'ins-1'},
      ],
    };

    test('sin señal devuelve la última copia, con su espera, y dice que es una copia', () async {
      var conSenal = true;
      final adaptador = AdaptadorFalso((opciones) {
        if (!conSenal) throw DioException(requestOptions: opciones, type: DioExceptionType.connectionError);
        return json(200, parada);
      });
      final repo = EntregasRepositorio(
        ClienteApi(baseUrl: 'https://api.test', token: () async => 'jwt', dio: Dio()..httpClientAdapter = adaptador),
        copias: () => cache,
        usuarioActual: () => 'u9',
      );

      final delServidor = await repo.deParada('v1', 'l1');
      conSenal = false;
      final copia = await repo.deParada('v1', 'l1');

      expect(delServidor.copiaSinSenal, isFalse);
      expect(copia.copiaSinSenal, isTrue);
      expect(copia.espera?.activadaAt, DateTime.utc(2026, 11, 26, 13));
      expect(copia.espera?.planBDesde, DateTime.utc(2026, 11, 26, 13, 45));
      expect((copia.esperaAvisoMinutos, copia.esperaPlanBMinutos), (15, 30));
      expect(await cache.leer(EntregasRepositorio.clave('u9', 'v1', 'l1')), isNotNull, reason: 'la copia es por usuario');
      expect(await cache.leer(EntregasRepositorio.clave('otro', 'v1', 'l1')), isNull);
    });

    // CA-3: la activación queda anotada en la copia, y sin señal el reloj sigue con ella aunque ya no esté en la cola.
    test('la activación queda anotada en la copia sin señal de la parada', () async {
      var conSenal = true;
      final adaptador = AdaptadorFalso((opciones) {
        if (!conSenal) throw DioException(requestOptions: opciones, type: DioExceptionType.connectionError);
        return json(200, {...parada, 'espera': null});
      });
      final repo = EntregasRepositorio(
        ClienteApi(baseUrl: 'https://api.test', token: () async => 'jwt', dio: Dio()..httpClientAdapter = adaptador),
        copias: () => cache,
        usuarioActual: () => 'u9',
      );
      await repo.deParada('v1', 'l1');

      await repo.anotarActivacion(activacion());
      conSenal = false;
      final copia = await repo.deParada('v1', 'l1');

      final espera = copia.espera!;
      expect((espera.abierta, espera.deEsteTelefono), (true, true));
      expect((espera.activadaAt, espera.avisoDesde, espera.planBDesde),
          (DateTime.utc(2026, 11, 26, 13), DateTime.utc(2026, 11, 26, 13, 15), DateTime.utc(2026, 11, 26, 13, 45)));
    });

    test('un rechazo del servidor no se tapa con la copia', () async {
      var veces = 0;
      final adaptador = AdaptadorFalso((_) => veces++ == 0 ? json(200, parada) : json(403, {'message': 'No es tu viaje', 'code': 'FORBIDDEN'}));
      final repo = EntregasRepositorio(
        ClienteApi(baseUrl: 'https://api.test', token: () async => 'jwt', dio: Dio()..httpClientAdapter = adaptador),
        copias: () => cache,
        usuarioActual: () => 'u9',
      );

      await repo.deParada('v1', 'l1');

      await expectLater(repo.deParada('v1', 'l1'), throwsA(isA<ErrorApi>().having((e) => e.status, 'status', 403)));
    });

    test('sin señal y sin copia, se dice que no se pudo', () async {
      final adaptador = AdaptadorFalso(
        (opciones) => throw DioException(requestOptions: opciones, type: DioExceptionType.connectionError),
      );
      final repo = EntregasRepositorio(
        ClienteApi(baseUrl: 'https://api.test', token: () async => 'jwt', dio: Dio()..httpClientAdapter = adaptador),
        copias: () => cache,
        usuarioActual: () => 'u9',
      );

      await expectLater(repo.deParada('v1', 'l1'), throwsA(isA<ErrorApi>().having((e) => e.status, 'status', 0)));
    });
  });
}
