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

    test('si la unidad no sale, queda fallida con el motivo del servidor', () async {
      gateway.errores['crearNovedad'] = [conCodigo(409, 'UNIDAD_NO_SALE', 'La unidad no sale del viaje')];
      await cola.agregar(novedad());

      final r = await procesador.procesar(usuarioPrueba);

      expect(r.fallidos, 1);
      expect(cola.jobs.single.error, contains('no sale'));
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

    test('un 409 UNIDAD_NO_SALE la deja fallida con su mensaje', () async {
      gateway.errores['confirmarLlegada'] = [conCodigo(409, 'UNIDAD_NO_SALE', 'La unidad no sale del viaje')];
      await cola.agregar(llegada());

      final r = await procesador.procesar(usuarioPrueba);

      expect(r.fallidos, 1);
      expect(cola.jobs.single.error, contains('no sale'));
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
}
