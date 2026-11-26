import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/core/errores/error_api.dart';
import 'package:traza_autos/core/http/cliente_api.dart';
import 'package:traza_autos/features/inspecciones/domain/foto_local.dart';
import 'package:traza_autos/features/sync/data/gateway_informes_api.dart';
import 'package:traza_autos/features/sync/domain/desenlace_envio.dart';
import 'package:traza_autos/features/sync/domain/job.dart';
import 'package:traza_autos/features/sync/domain/procesador_cola.dart';

import '../support/dio_falso.dart';
import '../support/dobles_sync.dart';

/// S8-HU-07.3 CA-3: la novedad del viaje va por la cola con la hora y el GPS del momento en que se guardó, su foto si
/// la trae, y su `id` como `id_envio`. Y lo que la cola hace con los rechazos nuevos del cargue y del transporte
/// (CA-6, CA-7 y CA-8).

NovedadViajeJob novedadViaje({
  String id = 'nv1',
  double? lat = 4.7,
  double? lng = -74.2,
  FotoLocal? foto,
  String? proceso,
  String? lugarId,
}) =>
    NovedadViajeJob(
      id: id,
      etiqueta: 'Novedad del viaje: Bloqueo de vía',
      creadoEn: 1,
      usuarioId: usuarioPrueba,
      viajeId: 'v1',
      tipoNovedad: 'bloqueo_via',
      texto: 'Paro en el peaje',
      fecha: '2026-10-21T21:20:00.000Z',
      lat: lat,
      lng: lng,
      foto: foto,
      proceso: proceso,
      lugarId: lugarId,
    );

ErrorApi conCodigo(int status, String codigo, String mensaje, {Object? details}) =>
    ErrorApi(message: mensaje, code: codigo, status: status, details: details);

void main() {
  late ColaEnMemoria cola;
  late GatewayFalso gateway;
  late ArchivosFalsos archivos;
  late RedFalsa red;
  late ProcesadorCola procesador;

  setUp(() {
    cola = ColaEnMemoria();
    gateway = GatewayFalso();
    archivos = ArchivosFalsos(['viaje.jpg']);
    red = RedFalsa();
    procesador = ProcesadorCola(cola: cola, gateway: gateway, red: red, archivos: archivos);
  });

  group('el job', () {
    test('se guarda y se lee igual, con la hora, el GPS, la foto, el proceso y la parada', () {
      final job = novedadViaje(
        foto: const FotoLocal(ruta: 'viaje.jpg', selloFecha: '2026-10-21T21:20:00.000Z'),
        proceso: 'entrega',
        lugarId: 'parada-1',
      );
      final leido = Job.fromJson(job.toJson()) as NovedadViajeJob;

      expect((leido.viajeId, leido.tipoNovedad, leido.texto), ('v1', 'bloqueo_via', 'Paro en el peaje'));
      expect((leido.fecha, leido.lat, leido.lng), ('2026-10-21T21:20:00.000Z', 4.7, -74.2));
      expect((leido.foto?.ruta, leido.proceso, leido.lugarId, leido.usuarioId), ('viaje.jpg', 'entrega', 'parada-1', usuarioPrueba));
    });

    test('sin GPS ni foto también: quedan nulos', () {
      final leido = NovedadViajeJob.fromJson(novedadViaje(lat: null, lng: null).toJson());

      expect((leido.lat, leido.lng, leido.foto, leido.sinUbicacion), (null, null, null, true));
    });
  });

  group('el envío', () {
    test('sale con el id del job como id_envio y con la hora y el GPS del momento', () async {
      await cola.agregar(novedadViaje(id: 'envio-7'));

      final r = await procesador.procesar(usuarioPrueba);

      final enviada = gateway.novedadesViajeEnviadas.single;
      expect((enviada.viajeId, enviada.tipo, enviada.texto), ('v1', 'bloqueo_via', 'Paro en el peaje'));
      expect((enviada.fecha, enviada.lat, enviada.lng), ('2026-10-21T21:20:00.000Z', 4.7, -74.2));
      expect((enviada.idEnvio, enviada.storagePath, enviada.proceso, enviada.lugarId), ('envio-7', null, null, null));
      expect(gateway.llamadas, ['registrarNovedadViaje'], reason: 'sin foto no se sube nada');
      expect(r.enviados, 1);
      expect(cola.jobs, isEmpty);
    });

    // C14 (ESC-13): sin GPS se envía igual y el servidor la marca «sin ubicación».
    test('sin GPS se envía igual, sin lat ni lng', () async {
      await cola.agregar(novedadViaje(lat: null, lng: null));

      await procesador.procesar(usuarioPrueba);

      final enviada = gateway.novedadesViajeEnviadas.single;
      expect((enviada.lat, enviada.lng), (null, null));
      expect(cola.jobs, isEmpty);
    });

    // ESC-05: sin señal espera en la cola y sale con la hora de cuando se guardó, no la del envío.
    test('sin señal queda en la cola y, al volver, sale con la hora del momento', () async {
      red.estable = false;
      await cola.agregar(novedadViaje());

      final sinSenal = await procesador.procesar(usuarioPrueba);
      expect(sinSenal.pendientes, 1);
      expect(gateway.novedadesViajeEnviadas, isEmpty);
      expect(desenlaceDe(cola.jobs, 'nv1', pausa: sinSenal.pausa).fase, FaseEnvio.enCola);

      red.estable = true;
      await procesador.procesar(usuarioPrueba);

      expect(gateway.novedadesViajeEnviadas.single.fecha, '2026-10-21T21:20:00.000Z');
      expect(desenlaceDe(cola.jobs, 'nv1').fase, FaseEnvio.registrado);
    });

    test('la foto se sube primero y un corte después no la vuelve a subir', () async {
      gateway.errores['registrarNovedadViaje'] = [const ErrorApi.red()];
      await cola.agregar(novedadViaje(foto: const FotoLocal(ruta: 'viaje.jpg', selloFecha: '2026-10-21T21:20:00.000Z')));

      await procesador.procesar(usuarioPrueba);
      expect((cola.jobs.single as NovedadViajeJob).foto?.storagePath, 'ruta/1.jpg', reason: 'queda anotada');
      await procesador.procesar(usuarioPrueba);

      expect(gateway.llamadas.where((l) => l == 'subirImagen'), hasLength(1));
      expect(gateway.novedadesViajeEnviadas.last.storagePath, 'ruta/1.jpg');
      expect(gateway.novedadesViajeEnviadas.map((n) => n.idEnvio).toSet(), {'nv1'}, reason: 'el mismo id en el reintento');
      expect(archivos.borrados, ['viaje.jpg']);
      expect(cola.jobs, isEmpty);
    });

    test('el inspector la manda con su proceso y, en la entrega, con la parada', () async {
      await cola.agregar(novedadViaje(proceso: 'entrega', lugarId: 'parada-1'));

      await procesador.procesar(usuarioPrueba);

      final enviada = gateway.novedadesViajeEnviadas.single;
      expect((enviada.proceso, enviada.lugarId), ('entrega', 'parada-1'));
    });
  });

  group('los rechazos quedan en la cola con el mensaje del servidor', () {
    test('un 400 queda fallido con su detalle y sin reintentar solo', () async {
      gateway.errores['registrarNovedadViaje'] = [
        conCodigo(400, 'VALIDATION_ERROR', 'Datos inválidos', details: {
          'fieldErrors': {
            'tipo': ['Ese tipo de novedad no es para tu rol'],
          },
        }),
      ];
      await cola.agregar(novedadViaje());

      final r = await procesador.procesar(usuarioPrueba);

      final job = cola.jobs.single;
      expect((r.fallidos, job.fallido, job.esProhibido), (1, true, false));
      expect(job.error, contains('Ese tipo de novedad no es para tu rol'));
      final desenlace = desenlaceDe(cola.jobs, 'nv1');
      expect(desenlace.fase, FaseEnvio.rechazado);
      expect(desenlace.motivo, contains('Ese tipo de novedad no es para tu rol'));
    });

    // Un 403 no es «lo empezó otra persona»: guardado como FORBIDDEN, la cola lo mostraría «De otro usuario» y no
    // dejaría ni reintentarlo ni quitarlo.
    test('un 403 con su motivo queda con el mensaje del servidor, nunca como «de otro usuario»', () async {
      gateway.errores['registrarNovedadViaje'] = [
        conCodigo(403, codigoProhibido, 'No participa en el cargue de este viaje: solo reporta quien tiene ese proceso',
            details: {'motivo': 'inspector_sin_ese_proceso'}),
      ];
      await cola.agregar(novedadViaje(proceso: 'cargue'));

      await procesador.procesar(usuarioPrueba);

      final job = cola.jobs.single;
      expect((job.fallido, job.esProhibido, job.codigoError), (true, false, 'inspector_sin_ese_proceso'));
      expect(job.error, 'No participa en el cargue de este viaje: solo reporta quien tiene ese proceso');
    });

    test('un 403 sin motivo tampoco queda como «de otro usuario»', () async {
      gateway.errores['registrarNovedadViaje'] = [conCodigo(403, codigoProhibido, 'El rol no puede hacerlo')];
      await cola.agregar(novedadViaje());

      await procesador.procesar(usuarioPrueba);

      expect((cola.jobs.single.esProhibido, cola.jobs.single.error), (false, 'El rol no puede hacerlo'));
    });

    test('descartarla borra su foto del teléfono', () async {
      await cola.agregar(novedadViaje(foto: const FotoLocal(ruta: 'viaje.jpg', selloFecha: '2026-10-21T21:20:00.000Z')));

      await procesador.descartar('nv1');

      expect(cola.jobs, isEmpty);
      expect(archivos.borrados, ['viaje.jpg']);
    });
  });

  group('hacia la API', () {
    (GatewayInformesApi, AdaptadorFalso) gatewayApi() {
      final adaptador = AdaptadorFalso((_) => json(201, {'id': 'nv-1'}));
      final api = ClienteApi(baseUrl: 'https://api.test', token: () async => 'jwt', dio: Dio()..httpClientAdapter = adaptador);
      return (GatewayInformesApi(api), adaptador);
    }

    test('la del conductor sin GPS no manda lat, lng, proceso ni parada', () async {
      final (api, adaptador) = gatewayApi();

      await api.registrarNovedadViaje(
        'v 1',
        tipo: 'intento_robo',
        texto: 'Intentaron abrir la niñera en la parada',
        fecha: '2026-10-22T03:10:00.000Z',
        idEnvio: 'envio-1',
      );

      final peticion = adaptador.peticiones.single;
      expect((peticion.method, peticion.uri.path), ('POST', '/api/viajes/v%201/novedades-viaje'));
      expect(peticion.data, {
        'tipo': 'intento_robo',
        'texto': 'Intentaron abrir la niñera en la parada',
        'fecha': '2026-10-22T03:10:00.000Z',
        'id_envio': 'envio-1',
      });
    });

    test('la del inspector en la entrega lleva el GPS, la foto sin nota, el proceso y la parada', () async {
      final (api, adaptador) = gatewayApi();

      await api.registrarNovedadViaje(
        'v1',
        tipo: 'camion_llego_tarde',
        texto: 'Llegó 3 horas después de la cita',
        fecha: '2026-10-21T19:05:00.000Z',
        lat: 4.7,
        lng: -74.2,
        foto: const FotoLocal(
          ruta: 'f.jpg',
          selloFecha: '2026-10-21T19:05:00.000Z',
          selloLat: 4.7,
          selloLng: -74.2,
          storagePath: 'uploads/f.jpg',
          hash: 'abc',
          nota: 'no debe viajar',
        ),
        idEnvio: 'envio-2',
        proceso: 'entrega',
        lugarId: 'parada-1',
      );

      final cuerpo = adaptador.peticiones.single.data as Map<String, dynamic>;
      expect((cuerpo['lat'], cuerpo['lng'], cuerpo['proceso'], cuerpo['lugar_id']), (4.7, -74.2, 'entrega', 'parada-1'));
      final evidencia = cuerpo['evidencia'] as Map<String, dynamic>;
      expect((evidencia['storage_path'], evidencia['hash_sha256']), ('uploads/f.jpg', 'abc'));
      expect(evidencia.keys, isNot(contains('nota')));
    });
  });

  // Lo que el servidor ya no admite en el cargue y en el transporte se retira con su mensaje, sin quedar «con error».
  group('los informes que el servidor ya no admite', () {
    test('S8-HU-07.3 CA-8: un informe de transporte hecho a mano se retira con «lo arma el sistema»', () async {
      gateway.errores['crearInspeccion'] = [
        conCodigo(409, 'CONFLICT', 'El informe de transporte lo arma el sistema con «Reportar novedad» y «Llegué»',
            details: {'motivo': 'transporte_lo_arma_el_sistema'}),
      ];
      await cola.agregar(informe());

      final r = await procesador.procesar(usuarioPrueba);

      expect((r.fallidos, r.retirados), (0, 1));
      expect(cola.jobs.single.retirado, isTrue);
      expect(cola.jobs.single.error, 'El informe de transporte lo arma el sistema con «Reportar novedad» y «Llegué»');
    });

    test('S8-HU-07.3 CA-6: un cargue sin inspector donde el lugar no lo admite se retira con el mensaje del servidor', () async {
      gateway.errores['crearInspeccion'] = [
        conCodigo(409, 'CONFLICT', 'Este lugar de cargue no admite cargue sin inspector: el cargue lo hace el inspector',
            details: {'motivo': 'lugar_no_admite_cargue_sin_inspector'}),
      ];
      await cola.agregar(informe(motivoSinInspector: 'inspector_no_llego'));

      final r = await procesador.procesar(usuarioPrueba);

      expect((r.fallidos, cola.jobs.single.retirado), (0, true));
      expect(cola.jobs.single.error, 'Este lugar de cargue no admite cargue sin inspector: el cargue lo hace el inspector');
    });

    // Sin un permiso pedido ese 403 es un rechazo definitivo (S8-HU-09.2 CA-2: solo es espera bajo un permiso pedido).
    test('S8-HU-07.3 CA-7: la llegada del cargue sin el permiso queda con su mensaje y se puede reintentar', () async {
      gateway.errores['crearInspeccion'] = [
        conCodigo(403, codigoProhibido, 'Pide el permiso «No hay inspector» y espera a que el coordinador lo conceda',
            details: {'motivo': 'conductor_sin_permiso_de_excepcion'}),
      ];
      await cola.agregar(informe());

      await procesador.procesar(usuarioPrueba);

      final job = cola.jobs.single;
      expect((job.fallido, job.esProhibido, job.retirado), (true, false, false), reason: 'no es «de otro usuario»');
      expect(job.error, 'Pide el permiso «No hay inspector» y espera a que el coordinador lo conceda');
      expect(job.codigoError, 'conductor_sin_permiso_de_excepcion');
    });
  });
}
