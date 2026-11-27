import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/core/errores/error_api.dart';
import 'package:traza_autos/core/http/cliente_api.dart';
import 'package:traza_autos/features/sync/data/gateway_informes_api.dart';
import 'package:traza_autos/features/sync/domain/job.dart';
import 'package:traza_autos/features/sync/domain/procesador_cola.dart';
import 'package:traza_autos/features/viajes/data/entregas_repositorio.dart';
import 'package:traza_autos/features/viajes/domain/entrega_punto.dart';

import '../support/arnes.dart';
import '../support/dio_falso.dart';
import '../support/dobles_sync.dart';

/// S8-HU-04.2: la firma de la parada va por la cola con la hora y el GPS del momento, los rechazos con su daño, las
/// reservas y, en el cierre con una sola firma, el motivo codificado, las fotos y el testigo.

EntregaPuntoJob entrega({
  String? motivoCodigo,
  String? firma = 'M0 0L10 10',
  bool fallido = false,
  double? lat = 10.39,
  double? lng = -75.51,
  List<ReservaCarro> reservas = const [],
  List<RechazoCarro> rechazos = const [],
  List<String> fotos = const [],
  List<String> vines = const [],
  String? testigoNombre,
  String? testigoCedula,
}) =>
    EntregaPuntoJob(
      id: 'e1',
      etiqueta: 'Entrega Autonorte',
      creadoEn: 1,
      usuarioId: usuarioPrueba,
      viajeId: 'v1',
      lugarId: 'l1',
      firma: firma,
      fechaMomento: '2026-11-18T14:10:00-05:00',
      lat: lat,
      lng: lng,
      precisionM: lat == null ? null : 8,
      reservas: reservas,
      rechazos: rechazos,
      motivoCodigo: motivoCodigo,
      fotos: [for (final ruta in fotos) foto(ruta)],
      vines: vines,
      testigoNombre: testigoNombre,
      testigoCedula: testigoCedula,
      fallido: fallido,
    );

/// Un rechazo de la parada con su motivo en `details`, como lo manda el servidor.
ErrorApi rechazoDeParada(int status, String motivo, String mensaje) =>
    ErrorApi(message: mensaje, code: status == 403 ? 'FORBIDDEN' : 'CONFLICT', status: status, details: {'motivo': motivo});

void main() {
  late ColaEnMemoria cola;
  late GatewayFalso gateway;
  late ArchivosFalsos archivos;
  late ProcesadorCola procesador;

  setUp(() {
    cola = ColaEnMemoria();
    gateway = GatewayFalso();
    archivos = ArchivosFalsos(const ['/tmp/cierre-1.jpg', '/tmp/cierre-2.jpg']);
    procesador = ProcesadorCola(cola: cola, gateway: gateway, red: RedFalsa(), archivos: archivos);
  });

  group('el job de la entrega por parada', () {
    test('se guarda y se lee igual, con todo lo que se decidió al firmar', () {
      final original = entrega(
        reservas: const [ReservaCarro(vin: 'VIN2', observaciones: 'Llegó sucio y sin manual')],
        rechazos: const [RechazoCarro(vin: 'VIN4', hallazgoId: 'h-golpe')],
      );
      final de = EntregaPuntoJob.fromJson(original.toJson());
      expect((de.viajeId, de.lugarId, de.firma, de.unaSolaFirma), ('v1', 'l1', 'M0 0L10 10', false));
      expect((de.fechaMomento, de.lat, de.lng, de.precisionM), ('2026-11-18T14:10:00-05:00', 10.39, -75.51, 8.0));
      expect(de.reservas, original.reservas);
      expect(de.rechazos, original.rechazos);
      expect(Job.fromJson(original.toJson()), isA<EntregaPuntoJob>());
    });

    test('el cierre con una sola firma conserva su motivo, sus fotos, sus carros y su testigo', () {
      final de = EntregaPuntoJob.fromJson(entrega(
        motivoCodigo: MotivoUnaFirma.seNegoAFirmar,
        fotos: ['/tmp/cierre-1.jpg'],
        vines: ['VIN1', 'VIN2'],
        testigoNombre: 'Andrés Pardo',
        testigoCedula: '79 123 456',
      ).toJson());
      expect((de.motivoCodigo, de.unaSolaFirma), (MotivoUnaFirma.seNegoAFirmar, true));
      expect(de.vines, ['VIN1', 'VIN2']);
      expect(de.fotos.single.ruta, '/tmp/cierre-1.jpg');
      expect((de.testigoNombre, de.testigoCedula), ('Andrés Pardo', '79 123 456'));
    });

    test('sin GPS se guarda sin ubicación', () {
      final de = EntregaPuntoJob.fromJson(entrega(lat: null, lng: null).toJson());
      expect((de.sinUbicacion, de.lat, de.lng), (true, null, null));
    });

    // Un cierre que una versión anterior dejó en la cola con un motivo escrito no puede salir como la firma de una
    // parte: sigue siendo un cierre con una sola firma, y el servidor dirá qué motivos admite.
    test('un cierre viejo, con motivo escrito, sigue siendo un cierre con una sola firma', () {
      final viejo = {...entrega().toJson()}
        ..remove('motivo_codigo')
        ..remove('motivo_texto')
        ..['motivo'] = 'no hay inspector en el punto';
      final de = EntregaPuntoJob.fromJson(viejo);
      expect((de.unaSolaFirma, de.motivoCodigo, de.motivoTexto), (true, 'otro', 'no hay inspector en el punto'));
    });

    test('conserva sus datos al contar un intento o cambiar de dueño', () {
      final intentado = entrega(rechazos: const [RechazoCarro(vin: 'VIN4', hallazgoId: 'h1')])
          .conIntento(intentos: 2, error: 'sin red', fallido: false);
      expect((intentado.intentos, intentado.viajeId, intentado.firma, intentado.rechazos.single.vin), (2, 'v1', 'M0 0L10 10', 'VIN4'));
      expect(entrega().conUsuario('u2').usuarioId, 'u2');
    });
  });

  group('envío por la cola', () {
    test('la firma sale una sola vez, con la hora y el GPS del momento, y saca el job de la cola', () async {
      await cola.agregar(entrega(
        reservas: const [ReservaCarro(vin: 'VIN2', observaciones: 'Llegó sucio')],
        rechazos: const [RechazoCarro(vin: 'VIN4', hallazgoId: 'h-golpe')],
      ));

      final r = await procesador.procesar(usuarioPrueba);

      final enviada = gateway.entregasEnviadas.single;
      expect((enviada.viajeId, enviada.lugarId, enviada.motivoCodigo, enviada.firma), ('v1', 'l1', null, 'M0 0L10 10'));
      expect(enviada.momento, (fecha: '2026-11-18T14:10:00-05:00', lat: 10.39, lng: -75.51, precision: 8.0));
      expect(enviada.rechazos, const [RechazoCarro(vin: 'VIN4', hallazgoId: 'h-golpe')]);
      expect(enviada.reservas.single.observaciones, 'Llegó sucio');
      expect(cola.jobs, isEmpty);
      expect((r.enviados, r.pendientes, r.fallidos), (1, 0, 0));
    });

    test('con motivo sale el cierre con una sola firma: cada foto a cada carro, una sola subida por foto', () async {
      await cola.agregar(entrega(
        motivoCodigo: MotivoUnaFirma.seNegoAFirmar,
        fotos: ['/tmp/cierre-1.jpg'],
        vines: ['VIN1', 'VIN2', 'VIN3'],
        testigoNombre: 'Andrés Pardo',
        testigoCedula: '79 123 456',
      ));

      await procesador.procesar(usuarioPrueba);

      final enviada = gateway.entregasEnviadas.single;
      expect(enviada.motivoCodigo, MotivoUnaFirma.seNegoAFirmar);
      expect(enviada.fotos.map((f) => f.vin), ['VIN1', 'VIN2', 'VIN3']);
      expect(enviada.fotos.map((f) => f.foto.storagePath).toSet(), hasLength(1), reason: 'la misma foto, subida una vez');
      expect(gateway.llamadas.where((l) => l == 'subirImagen'), hasLength(1));
      expect(enviada.testigo, ('Andrés Pardo', '79 123 456'));
      expect(archivos.borrados, ['/tmp/cierre-1.jpg'], reason: 'ya registrada, la foto local sobra');
    });

    // CA-7: reintentar no duplica nada. La foto que ya subió no se vuelve a subir.
    test('si se corta después de subir la foto, al reintentar no la sube otra vez', () async {
      gateway.errores['cerrarEntregaConUnaFirma'] = [Exception('sin red')];
      await cola.agregar(entrega(motivoCodigo: MotivoUnaFirma.seNegoAFirmar, fotos: ['/tmp/cierre-1.jpg'], vines: ['VIN1']));

      await procesador.procesar(usuarioPrueba);
      expect(cola.jobs, hasLength(1));
      expect((cola.jobs.single as EntregaPuntoJob).fotos.single.storagePath, isNotNull, reason: 'el progreso quedó guardado');

      await procesador.procesar(usuarioPrueba);
      expect(cola.jobs, isEmpty);
      expect(gateway.llamadas.where((l) => l == 'subirImagen'), hasLength(1));
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

    // CA-3: la pantalla decide por el motivo y no por el texto; por eso el motivo queda guardado en el job.
    test('un rechazo de la parada guarda su motivo como código del job', () async {
      gateway.errores['firmarEntregaPunto'] = [
        rechazoDeParada(409, MotivoParada.sinReceptor, 'Este punto tiene inspector: pídele a un inspector del punto que tome la entrega en su app'),
      ];
      await cola.agregar(entrega());

      await procesador.procesar(usuarioPrueba);

      final job = cola.jobs.single;
      expect((job.fallido, job.codigoError), (true, MotivoParada.sinReceptor));
    });

    // Un 403 `entrega_tomada` no es algo que solo su dueño pueda arreglar: quien lo ve en la cola lo puede quitar.
    test('un 403 de la parada no queda como prohibido', () async {
      gateway.errores['firmarEntregaPunto'] = [rechazoDeParada(403, MotivoParada.entregaTomada, 'Esta entrega la tomó otro inspector')];
      await cola.agregar(entrega());

      await procesador.procesar(usuarioPrueba);

      expect((cola.jobs.single.codigoError, cola.jobs.single.esProhibido), (MotivoParada.entregaTomada, false));
    });

    test('un motivo que no es de la parada deja el código del error', () async {
      gateway.errores['firmarEntregaPunto'] = [rechazoDeParada(409, 'otra_cosa', 'No')];
      await cola.agregar(entrega());

      await procesador.procesar(usuarioPrueba);

      expect(cola.jobs.single.codigoError, 'CONFLICT');
    });

    // CA-11: la firma carro por carro del descargue que quedó en la cola se retira con lo que dice el servidor.
    test('una firma carro por carro del descargue se retira con el mensaje del servidor', () async {
      gateway.errores['firmarTransferencia'] = [
        ErrorApi(
          message: 'En el destino la entrega se firma por parada: usa «Entregar en esta parada»',
          code: 'CONFLICT',
          status: 409,
          details: const {'motivo': 'entrega_por_parada'},
        ),
      ];
      await cola.agregar(const FirmaJob(
        id: 'f1',
        etiqueta: 'Firma',
        creadoEn: 1,
        usuarioId: usuarioPrueba,
        inspeccionId: 'ins-1',
        entregaId: 'u9',
        recibeId: 'u1',
        firma: 'M0 0L1 1',
      ));

      await procesador.procesar(usuarioPrueba);

      expect(cola.jobs.single.retirado, isTrue);
      expect(cola.jobs.single.error, contains('Entregar en esta parada'));
    });

    test('descartar un cierre borra sus fotos del teléfono', () async {
      await cola.agregar(entrega(motivoCodigo: MotivoUnaFirma.seNegoAFirmar, fotos: ['/tmp/cierre-2.jpg'], vines: ['VIN1'], fallido: true));

      await procesador.descartar('e1');

      expect(cola.jobs, isEmpty);
      expect(archivos.borrados, ['/tmp/cierre-2.jpg']);
    });
  });

  group('hacia la API', () {
    (GatewayInformesApi, AdaptadorFalso) gatewayApi() {
      final adaptador = AdaptadorFalso((_) => json(200, {'viaje_id': 'v1'}));
      final api = ClienteApi(baseUrl: 'https://api.test', token: () async => 'jwt', dio: Dio()..httpClientAdapter = adaptador);
      return (GatewayInformesApi(api), adaptador);
    }

    test('la firma va a la parada del viaje con el momento, los rechazos y las reservas', () async {
      final (api, adaptador) = gatewayApi();

      await api.firmarEntregaPunto(
        'v 1',
        'l1',
        firma: 'M0 0L5 5',
        reservas: const [ReservaCarro(vin: 'VIN2', observaciones: 'Llegó sucio y sin manual')],
        rechazos: const [RechazoCarro(vin: 'VIN4', hallazgoId: 'h-golpe')],
        fechaMomento: '2026-11-18T14:10:00-05:00',
        lat: 10.39,
        lng: -75.51,
        precisionM: 8,
      );

      final peticion = adaptador.peticiones.single;
      expect((peticion.method, peticion.uri.path), ('POST', '/api/viajes/v%201/entregas/l1/firma'));
      expect(peticion.data, {
        'firma': 'M0 0L5 5',
        'reservas': [
          {'vin': 'VIN2', 'observaciones': 'Llegó sucio y sin manual'},
        ],
        'rechazos': [
          {'vin': 'VIN4', 'hallazgo_id': 'h-golpe'},
        ],
        'fecha_momento': '2026-11-18T14:10:00-05:00',
        'lat': 10.39,
        'lng': -75.51,
        'precision_m': 8.0,
      });
    });

    // Los campos que el servidor ya no acepta (400 «Campo no admitido») no salen nunca.
    test('la firma no lleva quién entrega ni quién recibe, ni un motivo parcial', () async {
      final (api, adaptador) = gatewayApi();

      await api.firmarEntregaPunto('v1', 'l1', firma: 'M0 0L5 5');

      final cuerpo = adaptador.peticiones.single.data as Map<String, dynamic>;
      expect(cuerpo.keys, ['firma']);
    });

    test('sin GPS la firma no lleva coordenadas, solo la hora', () async {
      final (api, adaptador) = gatewayApi();

      await api.firmarEntregaPunto('v1', 'l1', firma: 'M0 0L5 5', fechaMomento: '2026-11-18T14:10:00-05:00');

      expect(adaptador.peticiones.single.data, {'firma': 'M0 0L5 5', 'fecha_momento': '2026-11-18T14:10:00-05:00'});
    });

    test('el cierre con una sola firma lleva el motivo codificado, las fotos con su VIN y el testigo', () async {
      final (api, adaptador) = gatewayApi();

      await api.cerrarEntregaConUnaFirma(
        'v1',
        'l1',
        motivoCodigo: MotivoUnaFirma.seNegoAFirmar,
        motivoTexto: 'Se fue sin firmar',
        firma: 'M0 0L5 5',
        fotos: [(vin: 'VIN1', foto: foto('/tmp/x.jpg', storagePath: 'cierre/x.jpg'))],
        testigoNombre: 'Andrés Pardo',
        testigoCedula: '79 123 456',
        fechaMomento: '2026-11-18T20:30:00-05:00',
        lat: 10.39,
        lng: -75.51,
      );

      final peticion = adaptador.peticiones.single;
      expect(peticion.uri.path, '/api/viajes/v1/entregas/l1/una-firma');
      final cuerpo = peticion.data as Map<String, dynamic>;
      expect(
        (cuerpo['motivo_codigo'], cuerpo['motivo_texto'], cuerpo['firma'], cuerpo['testigo_nombre'], cuerpo['testigo_cedula']),
        (MotivoUnaFirma.seNegoAFirmar, 'Se fue sin firmar', 'M0 0L5 5', 'Andrés Pardo', '79 123 456'),
      );
      expect(cuerpo['fotos'], [
        {
          'vin': 'VIN1',
          'storage_path': 'cierre/x.jpg',
          'sello_fecha': '2026-01-02T10:01:00.000Z',
          'sello_lat': 6.21,
          'sello_lng': -75.51,
          'precision': null,
          'dispositivo_id': null,
          'nota': null,
          'hash_sha256': null,
        },
      ]);
      expect((cuerpo['fecha_momento'], cuerpo['lat'], cuerpo['lng']), ('2026-11-18T20:30:00-05:00', 10.39, -75.51));
      expect(cuerpo.containsKey('motivo'), isFalse, reason: 'el motivo escrito de antes ya no existe');
    });

    test('lee las paradas del viaje con sus unidades y los carros sin destino', () async {
      final adaptador = AdaptadorFalso((_) => json(200, {
            'viaje_id': 'v1',
            'viaje_estado': 'en_curso',
            'unidades_sin_destino': 2,
            'puntos': [
              {
                'viaje_id': 'v1',
                'lugar_id': 'l1',
                'lugar': 'Concesionario Autonorte',
                'ciudad': 'Bogotá',
                'unidades_total': 3,
                'unidades_pendientes': 1,
                'entregada': false,
                'estado_parada': 'pendiente',
                'receptor': 'inspector_del_punto',
                'receptor_nombre': 'Pedro',
                'conductor_llego': false,
                'unidades': [
                  {
                    'vin': 'VIN1',
                    'estado': 'pendiente',
                    'inspeccion_id': 'ins-1',
                    'entrega_firmada_at': '2026-10-29T10:00:00Z',
                    'recibe_firmada_at': null,
                    'reservas': 'Llegó sucio',
                  },
                  {'vin': 'VIN2', 'estado': 'cerrada', 'tipo_cierre': 'firmada'},
                  {
                    'vin': 'VIN3',
                    'estado': 'rechazado',
                    'rechazo': {'hallazgo_id': 'h1', 'marcado_por_nombre': 'Pedro', 'motivo': 'rechazado_por_novedad'},
                  },
                ],
              },
            ],
          }));
      final repo = EntregasRepositorio(ClienteApi(baseUrl: 'https://api.test', token: () async => 'jwt', dio: Dio()..httpClientAdapter = adaptador));

      final entregas = await repo.delViaje('v1');

      expect(adaptador.peticiones.single.uri.path, '/api/viajes/v1/entregas');
      expect(entregas.unidadesSinDestino, 2);
      final punto = entregas.puntos.single;
      expect((punto.lugar, punto.ciudad, punto.pendientes, punto.entregada), ('Concesionario Autonorte', 'Bogotá', 1, false));
      expect((punto.estado, punto.receptor, punto.receptorNombre, punto.conductorLlego),
          (EstadoParada.pendiente, ReceptorParada.inspectorDelPunto, 'Pedro', false));
      expect(punto.porEntregar.map((u) => u.vin), ['VIN1']);
      expect(punto.rechazadas.single.rechazo?.marcadoPorNombre, 'Pedro');
      expect((punto.unidades.first.inspeccionId, punto.unidades.first.reservas), ('ins-1', 'Llegó sucio'));
      expect(punto.unidades.first.entregaFirmada, isTrue);
      expect(punto.unidades.first.recibeFirmada, isFalse);
      expect(punto.unidades[1].cerrada, isTrue);
    });

    test('una parada se pide sola, por su lugar', () async {
      final adaptador = AdaptadorFalso((_) => json(200, {
            'viaje_id': 'v1',
            'lugar_id': 'l 1',
            'lugar': 'Puerto de Cartagena',
            'estado_parada': 'firmada_todos_rechazados',
            'unidades': <dynamic>[],
          }));
      final repo = EntregasRepositorio(ClienteApi(baseUrl: 'https://api.test', token: () async => 'jwt', dio: Dio()..httpClientAdapter = adaptador));

      final punto = await repo.deParada('v1', 'l 1');

      expect(adaptador.peticiones.single.uri.path, '/api/viajes/v1/entregas/l%201');
      expect(punto.estado, EstadoParada.firmadaTodosRechazados);
    });

    // CA-4: solo un daño con foto justifica el rechazo; los demás no se ofrecen.
    test('la revisión de llegada dice si está cerrada y cuáles de sus daños tienen foto', () async {
      final adaptador = AdaptadorFalso((_) => json(200, {
            'id': 'ins-1',
            'estado': 'cerrada',
            'hallazgos': [
              {'id': 'h1', 'zona': 'Izquierdo 7 · Puerta delantera', 'clase_dano': 'golpe'},
              {'id': 'h2', 'zona': 'Techo'},
            ],
            'evidencias': [
              {'id': 'e1', 'hallazgo_id': 'h1'},
              {'id': 'e2', 'hallazgo_id': null},
            ],
          }));
      final repo = EntregasRepositorio(ClienteApi(baseUrl: 'https://api.test', token: () async => 'jwt', dio: Dio()..httpClientAdapter = adaptador));

      final revision = await repo.revisionDeLlegada('ins-1');

      expect(adaptador.peticiones.single.uri.path, '/api/inspecciones/ins-1');
      expect(revision.cerrada, isTrue);
      expect(revision.danosConFoto.map((d) => (d.hallazgoId, d.zona, d.clase)), [('h1', 'Izquierdo 7 · Puerta delantera', 'golpe')]);
    });

    // 3: la misma regla del servidor (`clsFotosDeHallazgos`): ni el sello ni la constancia son la foto del daño, y el
    // daño de un extra tiene foto si su extra la tiene.
    test('un daño cuenta con foto como lo cuenta el servidor', () {
      final revision = RevisionDeLlegada.fromJson({
        'estado': 'cerrada',
        'hallazgos': [
          {'id': 'h-propia', 'zona': 'Puerta'},
          {'id': 'h-sello', 'zona': 'Capó'},
          {'id': 'h-constancia', 'zona': 'Techo'},
          {'id': 'h-extra', 'zona': 'Platón', 'extra_id': 'x1'},
          {'id': 'h-extra-sin-foto', 'zona': 'Barra', 'extra_id': 'x2'},
        ],
        'evidencias': [
          {'id': 'e1', 'hallazgo_id': 'h-propia'},
          {'id': 'e2', 'hallazgo_id': 'h-sello', 'es_sello': true},
          {'id': 'e3', 'hallazgo_id': 'h-constancia', 'es_constancia': true},
          {'id': 'e4', 'hallazgo_id': null, 'extra_id': 'x1'},
          {'id': 'e5', 'hallazgo_id': null, 'extra_id': 'x2', 'es_constancia': true},
        ],
      });

      expect(revision.danosConFoto.map((d) => d.hallazgoId), ['h-propia', 'h-extra']);
    });

    test('una unidad sin la revisión de llegada bloquea la firma de la parada', () {
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
      expect(punto.pendientes, 2, reason: 'sin el dato del servidor se cuentan las que faltan por entregar');
      // Un servidor de antes no manda ni el estado de la parada ni quién recibe: se deducen.
      expect((punto.estado, punto.receptor, punto.conductorLlego), (EstadoParada.pendiente, ReceptorParada.personaDelLugar, true));
    });
  });
}
