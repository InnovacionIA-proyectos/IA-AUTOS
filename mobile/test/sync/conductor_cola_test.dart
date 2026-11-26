import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/core/errores/error_api.dart';
import 'package:traza_autos/core/http/cliente_api.dart';
import 'package:traza_autos/features/inspecciones/domain/foto_local.dart';
import 'package:traza_autos/features/sync/data/gateway_informes_api.dart';
import 'package:traza_autos/features/sync/domain/job.dart';
import 'package:traza_autos/features/sync/domain/procesador_cola.dart';
import 'package:traza_autos/features/viajes/domain/viaje.dart';

import '../support/dio_falso.dart';
import '../support/dobles_sync.dart';

/// S6-HU-02.3: la cola del conductor en ruta. La novedad y «Llegué» llevan la fecha y el lugar del momento en que se
/// registraron, no del envío, y cada error tiene su regla.

NovedadJob novedad({String id = 'n1', String ruta = 'novedad.jpg', String? storagePath}) => NovedadJob(
      id: id,
      etiqueta: 'Novedad VIN1',
      creadoEn: 1,
      usuarioId: usuarioPrueba,
      viajeId: 'v1',
      vin: 'VIN1',
      tipoNovedad: 'granizo',
      tamanoDano: '5_10cm',
      fecha: '2026-10-21T13:30:00.000Z',
      lat: 4.7,
      lng: -74.2,
      foto: FotoLocal(ruta: ruta, selloFecha: '2026-10-21T13:30:00.000Z', selloLat: 4.7, selloLng: -74.2, storagePath: storagePath),
    );

LlegadaJob llegada({String id = 'l1'}) => LlegadaJob(
      id: id,
      etiqueta: 'Llegada VIN1',
      creadoEn: 2,
      usuarioId: usuarioPrueba,
      viajeId: 'v1',
      vin: 'VIN1',
      fecha: '2026-10-21T19:10:00.000Z',
      lat: 4.9,
      lng: -74.4,
    );

ErrorApi conCodigo(int status, String codigo, String mensaje, {Object? details}) =>
    ErrorApi(message: mensaje, code: codigo, status: status, details: details);

void main() {
  late ColaEnMemoria cola;
  late GatewayFalso gateway;
  late ArchivosFalsos archivos;
  late ProcesadorCola procesador;

  setUp(() {
    cola = ColaEnMemoria();
    gateway = GatewayFalso();
    archivos = ArchivosFalsos(['novedad.jpg']);
    procesador = ProcesadorCola(cola: cola, gateway: gateway, red: RedFalsa(), archivos: archivos);
  });

  group('los jobs', () {
    test('la novedad se guarda y se lee igual, con la fecha y el lugar del momento', () {
      final de = NovedadJob.fromJson(novedad().toJson());
      expect((de.viajeId, de.vin, de.tipoNovedad, de.tamanoDano), ('v1', 'VIN1', 'granizo', '5_10cm'));
      expect((de.fecha, de.lat, de.lng, de.usuarioId), ('2026-10-21T13:30:00.000Z', 4.7, -74.2, usuarioPrueba));
      expect(Job.fromJson(novedad().toJson()), isA<NovedadJob>());
    });

    test('«Llegué» se guarda y se lee igual', () {
      final de = LlegadaJob.fromJson(llegada().toJson());
      expect((de.viajeId, de.vin, de.fecha, de.lat, de.lng), ('v1', 'VIN1', '2026-10-21T19:10:00.000Z', 4.9, -74.4));
      expect(Job.fromJson(llegada().toJson()), isA<LlegadaJob>());
    });

    test('reintentar a mano deja limpio el error de una espera', () {
      final esperando = novedad().conIntento(intentos: 0, error: 'Falta cerrar Cargue', fallido: false);
      expect(esperando.error, 'Falta cerrar Cargue');
      expect(esperando.conIntento(intentos: 0, error: null, fallido: false).error, isNull);
    });
  });

  group('la novedad', () {
    test('sube la foto y después la crea, con la fecha y el lugar del registro', () async {
      await cola.agregar(novedad());

      final r = await procesador.procesar(usuarioPrueba);

      expect(gateway.llamadas, ['subirImagen', 'crearNovedad']);
      expect(gateway.novedadesEnviadas.single.$1, 'v1');
      expect(gateway.novedadesEnviadas.single.$2, 'VIN1');
      expect(gateway.novedadesEnviadas.single.$3, 'granizo');
      expect(gateway.novedadesEnviadas.single.$5, '2026-10-21T13:30:00.000Z');
      expect((gateway.novedadesEnviadas.single.$6, gateway.novedadesEnviadas.single.$7), (4.7, -74.2));
      expect(gateway.novedadesEnviadas.single.$8, 'ruta/1.jpg', reason: 'viaja la foto ya subida');
      expect((r.enviados, r.fallidos), (1, 0));
      expect(archivos.borrados, ['novedad.jpg']);
    });

    test('un corte después de subir la foto no la sube otra vez', () async {
      gateway.errores['crearNovedad'] = [Exception('sin red')];
      await cola.agregar(novedad());

      await procesador.procesar(usuarioPrueba);
      expect(cola.jobs, hasLength(1));
      await procesador.procesar(usuarioPrueba);

      expect(gateway.llamadas.where((l) => l == 'subirImagen'), hasLength(1));
      expect(cola.jobs, isEmpty);
    });

    test('red y 5xx se reintentan solos; no son un fallo', () async {
      gateway.errores['crearNovedad'] = [conCodigo(503, 'UNAVAILABLE', 'No disponible')];
      await cola.agregar(novedad());

      final r = await procesador.procesar(usuarioPrueba);

      expect((r.pendientes, r.fallidos), (1, 0));
      expect(cola.jobs.single.fallido, isFalse);
    });

    test('un 400 la deja fallida con su mensaje y sin reintentar', () async {
      gateway.errores['crearNovedad'] = [conCodigo(400, 'VALIDATION_ERROR', 'Datos inválidos')];
      await cola.agregar(novedad());

      final r = await procesador.procesar(usuarioPrueba);

      expect(r.fallidos, 1);
      expect(cola.jobs.single.error, contains('Datos inválidos'));
    });

    test('con el cargue sin cerrar espera: dice qué falta, no gasta intentos y no frena lo demás', () async {
      gateway.errores['crearNovedad'] = [
        conCodigo(409, codigoEtapaPreviaSinCierre, 'La etapa anterior no está cerrada', details: {'etapa_previa': 'Cargue'}),
      ];
      await cola.agregar(novedad(id: 'n1'));
      await cola.agregar(llegada(id: 'l1'));

      final r = await procesador.procesar(usuarioPrueba);

      final enEspera = cola.jobs.firstWhere((j) => j.id == 'n1');
      expect(enEspera.error, 'Falta cerrar Cargue');
      expect((enEspera.fallido, enEspera.intentos), (false, 0));
      expect(gateway.llegadasEnviadas, hasLength(1), reason: 'el siguiente job sí salió');
      expect(r.pendientes, 1);
    });

    test('al cerrarse el cargue, la que esperaba sale en la siguiente pasada', () async {
      gateway.errores['crearNovedad'] = [
        conCodigo(409, codigoEtapaPreviaSinCierre, 'La etapa anterior no está cerrada', details: {'etapa_previa': 'Cargue'}),
      ];
      await cola.agregar(novedad());

      await procesador.procesar(usuarioPrueba);
      await procesador.procesar(usuarioPrueba);

      expect(cola.jobs, isEmpty);
      expect(gateway.novedadesEnviadas, hasLength(2));
    });

    // S8-HU-02.2 CA-4: no es un error de nadie, el carro ya no sale. Se retira con el motivo del servidor.
    test('si la unidad no sale, se retira con el motivo del servidor y no cuenta como error', () async {
      gateway.errores['crearNovedad'] = [conCodigo(409, 'UNIDAD_NO_SALE', 'La unidad no sale del viaje')];
      await cola.agregar(novedad());

      final r = await procesador.procesar(usuarioPrueba);

      expect(r.fallidos, 0);
      expect(cola.jobs.single.retirado, isTrue);
      expect(cola.jobs.single.error, 'La unidad no sale del viaje');
      expect(await procesador.contarFallidos(usuarioPrueba), 0);
    });

    // S8-HU-07.3 CA-4 (hueco 6): el id del job viaja como `id_envio`, el mismo en cada reintento.
    test('sale con el id del job como id_envio, y el reintento repite el mismo', () async {
      gateway.errores['crearNovedad'] = [Exception('se cortó la respuesta')];
      await cola.agregar(novedad(id: 'envio-1'));

      await procesador.procesar(usuarioPrueba);
      await procesador.procesar(usuarioPrueba);

      expect(gateway.idsEnvioNovedad, ['envio-1', 'envio-1']);
      expect(cola.jobs, isEmpty);
    });

    // CA-4: el primer envío llegó y se perdió la respuesta; el reenvío recibe la misma novedad (200) y la cola la da
    // por registrada, sin volver a subir la foto.
    test('el reenvío de un envío cuya respuesta se perdió se da por registrado', () async {
      // Un servidor como el de verdad: guarda la novedad la primera vez y la respuesta se pierde en el camino; el
      // reenvío con el mismo `id_envio` responde 200 con la misma novedad, sin crear otra.
      final servidor = _ServidorConReenvio();
      final conServidor = ProcesadorCola(cola: cola, gateway: servidor, red: RedFalsa(), archivos: archivos);
      await cola.agregar(novedad(id: 'envio-1'));

      final primera = await conServidor.procesar(usuarioPrueba);
      expect((primera.enviados, primera.pendientes), (0, 1));
      expect(servidor.guardadas, {'envio-1': 1}, reason: 'ya quedó en el servidor');
      final segunda = await conServidor.procesar(usuarioPrueba);

      expect((segunda.enviados, segunda.fallidos), (1, 0));
      expect(servidor.guardadas, {'envio-1': 1}, reason: 'el reenvío no crea otra');
      expect(servidor.respuestas, ['respuesta perdida', '200 la misma']);
      expect(servidor.llamadas.where((l) => l == 'subirImagen'), hasLength(1), reason: 'ni otra foto');
      expect(cola.jobs, isEmpty);
    });

    // CA-5: el 409 ETAPA_PREVIA_SIN_CIERRE de la entrega del cargue sigue dejando el envío en espera, con lo que falta.
    test('con la entrega del cargue sin cerrar espera y dice «Falta cerrar la entrega del cargue»', () async {
      gateway.errores['crearNovedad'] = [
        conCodigo(
          409,
          codigoEtapaPreviaSinCierre,
          'Falta cerrar la entrega del cargue: fírmenla, recíbanla con reservas o registren la no firma',
          details: {'motivo': 'transferencia_sin_cierre', 'etapa_previa': 'Cargue'},
        ),
      ];
      await cola.agregar(novedad());

      final r = await procesador.procesar(usuarioPrueba);

      expect(cola.jobs.single.error, 'Falta cerrar la entrega del cargue');
      expect((cola.jobs.single.fallido, cola.jobs.single.intentos, r.fallidos), (false, 0, 0));
    });

    // Un 404 no es un carro bajado (al conductor el servidor le responde 409 UNIDAD_NO_SALE también en ese caso):
    // no se retira como si lo fuera.
    test('un 404 no se toma como carro retirado', () async {
      gateway.errores['crearNovedad'] = [conCodigo(404, 'NOT_FOUND', 'El viaje no existe')];
      await cola.agregar(novedad());

      await procesador.procesar(usuarioPrueba);

      expect(cola.jobs.single.retirado, isFalse);
    });
  });

  group('«Llegué»', () {
    test('sale con la fecha y el lugar del momento en que se pulsó', () async {
      await cola.agregar(llegada());

      await procesador.procesar(usuarioPrueba);

      expect(gateway.llegadasEnviadas, [('v1', 'VIN1', '2026-10-21T19:10:00.000Z', 4.9, -74.4)]);
      expect(cola.jobs, isEmpty);
    });

    test('un 409 LLEGADA_YA_CERRADA es un éxito: la primera confirmación ya quedó', () async {
      gateway.errores['confirmarLlegada'] = [conCodigo(409, codigoLlegadaYaCerrada, 'La llegada ya estaba confirmada')];
      await cola.agregar(llegada());

      final r = await procesador.procesar(usuarioPrueba);

      expect((r.enviados, r.fallidos), (1, 0));
      expect(cola.jobs, isEmpty);
    });

    test('un 409 UNIDAD_NO_SALE la retira con su mensaje, sin dejarla «con error»', () async {
      gateway.errores['confirmarLlegada'] = [conCodigo(409, 'UNIDAD_NO_SALE', 'La unidad no sale del viaje')];
      await cola.agregar(llegada());

      final r = await procesador.procesar(usuarioPrueba);

      expect(r.fallidos, 0);
      expect(cola.jobs.single.retirado, isTrue);
      expect(cola.jobs.single.error, 'La unidad no sale del viaje');
    });

    test('con el cargue sin cerrar espera, igual que la novedad', () async {
      gateway.errores['confirmarLlegada'] = [
        conCodigo(409, codigoEtapaPreviaSinCierre, 'La etapa anterior no está cerrada', details: {'etapa_previa': 'Cargue'}),
      ];
      await cola.agregar(llegada());

      await procesador.procesar(usuarioPrueba);

      expect(cola.jobs.single.error, 'Falta cerrar Cargue');
      expect(cola.jobs.single.fallido, isFalse);
    });

    test('un 400 sin ubicación la deja fallida', () async {
      gateway.errores['confirmarLlegada'] = [conCodigo(400, 'VALIDATION_ERROR', 'Falta la ubicación')];
      await cola.agregar(llegada());

      expect((await procesador.procesar(usuarioPrueba)).fallidos, 1);
    });
  });

  group('hacia la API', () {
    (GatewayInformesApi, AdaptadorFalso) gatewayApi() {
      final adaptador = AdaptadorFalso((_) => json(201, {'id': 'h1'}));
      final api = ClienteApi(baseUrl: 'https://api.test', token: () async => 'jwt', dio: Dio()..httpClientAdapter = adaptador);
      return (GatewayInformesApi(api), adaptador);
    }

    test('la novedad lleva solo lo que el contrato admite, y la evidencia sin nota', () async {
      final (api, adaptador) = gatewayApi();

      await api.crearNovedad(
        'v 1',
        'VIN1',
        tipoNovedad: 'granizo',
        tamanoDano: '5_10cm',
        fecha: '2026-10-21T13:30:00.000Z',
        lat: 4.7,
        lng: -74.2,
        foto: const FotoLocal(
          ruta: 'f.jpg',
          selloFecha: '2026-10-21T13:30:00.000Z',
          selloLat: 4.7,
          selloLng: -74.2,
          precision: 5,
          dispositivoId: 'equipo-1',
          hash: 'abc',
          storagePath: 'uploads/f.jpg',
          nota: 'no debe viajar',
        ),
      );

      final peticion = adaptador.peticiones.single;
      expect((peticion.method, peticion.uri.path), ('POST', '/api/viajes/v%201/vehiculos/VIN1/novedades'));
      final cuerpo = peticion.data as Map<String, dynamic>;
      expect(cuerpo.keys.toSet(), {'fecha', 'lat', 'lng', 'tipo_novedad', 'tamano_dano', 'evidencia'});
      expect((cuerpo['tipo_novedad'], cuerpo['tamano_dano'], cuerpo['lat'], cuerpo['lng']), ('granizo', '5_10cm', 4.7, -74.2));
      final evidencia = cuerpo['evidencia'] as Map<String, dynamic>;
      expect(evidencia['storage_path'], 'uploads/f.jpg');
      expect(evidencia.keys, isNot(contains('nota')), reason: 'el servidor responde 400 si llega');
      expect((evidencia['sello_lat'], evidencia['sello_lng'], evidencia['hash_sha256']), (4.7, -74.2, 'abc'));
    });

    test('con el id del envío, la novedad lo manda como id_envio (S8-HU-07.3 CA-4)', () async {
      final (api, adaptador) = gatewayApi();

      await api.crearNovedad(
        'v1',
        'VIN1',
        tipoNovedad: 'piedra',
        tamanoDano: '5_10cm',
        fecha: '2026-10-21T13:30:00.000Z',
        lat: 4.7,
        lng: -74.2,
        foto: const FotoLocal(ruta: 'f.jpg', selloFecha: '2026-10-21T13:30:00.000Z', storagePath: 'uploads/f.jpg'),
        idEnvio: 'envio-1',
      );

      final cuerpo = adaptador.peticiones.single.data as Map<String, dynamic>;
      expect(cuerpo['id_envio'], 'envio-1');
    });

    test('la llegada manda la fecha y el lugar', () async {
      final (api, adaptador) = gatewayApi();

      await api.confirmarLlegada('v1', 'VIN1', fecha: '2026-10-21T19:10:00.000Z', lat: 4.9, lng: -74.4);

      final peticion = adaptador.peticiones.single;
      expect(peticion.uri.path, '/api/viajes/v1/vehiculos/VIN1/llegada');
      expect(peticion.data, {'fecha': '2026-10-21T19:10:00.000Z', 'lat': 4.9, 'lng': -74.4});
    });
  });

  test('el viaje trae el estado de transporte y las novedades de cada unidad', () {
    final unidad = ViajeVehiculo.fromJson({'vin': 'VIN1', 'estado_transporte': 'llegada_con_novedad', 'novedades': 2});
    expect((unidad.estadoTransporte, unidad.novedades), ('llegada_con_novedad', 2));
    final guardada = ViajeVehiculo.fromJson(unidad.toJson());
    expect((guardada.estadoTransporte, guardada.novedades), ('llegada_con_novedad', 2));
    expect(ViajeVehiculo.fromJson({'vin': 'VIN1'}).estadoTransporte, EstadoTransporte.sinRegistro);
  });

  // S8-HU-07.3 CA-5 (decisión 10): el dato del servidor se lee y se guarda en la copia sin señal.
  test('la unidad trae si puede registrar la ruta y por qué no, y la copia sin señal los conserva', () {
    final unidad = ViajeVehiculo.fromJson({
      'vin': 'VIN1',
      'puede_registrar_ruta': false,
      'motivo_sin_ruta': 'entrega_del_cargue_sin_cerrar',
    });
    expect((unidad.puedeRegistrarRuta, unidad.motivoSinRuta, unidad.rutaBloqueada), (false, 'entrega_del_cargue_sin_cerrar', true));
    final guardada = ViajeVehiculo.fromJson(unidad.toJson());
    expect((guardada.puedeRegistrarRuta, guardada.motivoSinRuta), (false, 'entrega_del_cargue_sin_cerrar'));

    final anterior = ViajeVehiculo.fromJson({'vin': 'VIN1'});
    expect((anterior.puedeRegistrarRuta, anterior.rutaBloqueada), (null, false), reason: 'un servidor anterior: como hoy');
    expect(textoSinRuta('entrega_del_cargue_sin_cerrar'), 'Falta cerrar la entrega del cargue');
    expect(textoSinRuta('algo_nuevo'), 'Todavía no puedes registrar la ruta de este carro');
  });
}

/// El servidor del reenvío (S8-HU-07.3 CA-4): guarda una novedad por `id_envio` y, con uno ya guardado, responde
/// 200 con la misma. La primera respuesta se pierde en el camino, como con un corte de señal justo después de enviar.
class _ServidorConReenvio extends GatewayFalso {
  /// Cuántas novedades guardó por `id_envio`.
  final Map<String?, int> guardadas = {};
  final List<String> respuestas = [];
  bool _perderLaPrimera = true;

  @override
  Future<void> crearNovedad(
    String viajeId,
    String vin, {
    required String tipoNovedad,
    required String tamanoDano,
    required String fecha,
    required double lat,
    required double lng,
    required FotoLocal foto,
    String? idEnvio,
  }) async {
    final repetida = idEnvio != null && guardadas.containsKey(idEnvio);
    if (!repetida) guardadas[idEnvio] = (guardadas[idEnvio] ?? 0) + 1;
    if (_perderLaPrimera) {
      _perderLaPrimera = false;
      respuestas.add('respuesta perdida');
      throw const ErrorApi.red();
    }
    respuestas.add(repetida ? '200 la misma' : '201 nueva');
  }
}
