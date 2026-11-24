import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/core/errores/error_api.dart';
import 'package:traza_autos/core/http/cliente_api.dart';
import 'package:traza_autos/features/custodia/domain/firma_ninera.dart';
import 'package:traza_autos/features/inspecciones/domain/inspeccion_resumen.dart';
import 'package:traza_autos/features/sync/data/gateway_informes_api.dart';
import 'package:traza_autos/features/sync/domain/job.dart';
import 'package:traza_autos/features/sync/domain/procesador_cola.dart';
import 'package:traza_autos/features/viajes/domain/viaje.dart';

import '../support/dio_falso.dart';
import '../support/dobles_sync.dart';

/// S8-HU-01.3: la firma del cargue de toda la niñera, una por parte, con la hora y el GPS del momento.

Viaje _viaje({List<ViajeVehiculo>? carros}) => Viaje(
      id: 'v1',
      codigoMtrack: 'MTK-1',
      placa: 'ABC123',
      estado: 'en_curso',
      inspectorId: 'ana',
      conductorId: 'luis',
      vehiculos: carros ??
          const [
            ViajeVehiculo(vin: 'VINB', posicion: 2),
            ViajeVehiculo(vin: 'VINA', posicion: 1),
            ViajeVehiculo(vin: 'VINX', estadoUnidad: 'no_sale'),
            ViajeVehiculo(vin: 'VINZ', estadoUnidad: 'bajado'),
          ],
    );

InspeccionResumen _informe(String vin, String momento, {String estado = 'cerrada'}) =>
    InspeccionResumen(id: '$vin-$momento', vin: vin, momento: momento, momentoNombre: momento, fecha: '', estado: estado);

FirmaNineraJob _firma({String parte = ParteTransferencia.recibe, List<ReservaCarro> reservas = const []}) => FirmaNineraJob(
      id: 'f1',
      etiqueta: 'Recibo de la niñera MTK-1',
      creadoEn: 0,
      usuarioId: usuarioPrueba,
      viajeId: 'v1',
      parte: parte,
      firma: 'M0 0 L1 1',
      fechaMomento: '2026-11-18T14:09:00-05:00',
      lat: 6.2442,
      lng: -75.5812,
      precisionM: 7.5,
      reservas: reservas,
    );

NoFirmaNineraJob _constancia() => NoFirmaNineraJob(
      id: 'c1',
      etiqueta: 'Constancia de la niñera MTK-1',
      creadoEn: 0,
      usuarioId: usuarioPrueba,
      viajeId: 'v1',
      parte: ParteTransferencia.recibe,
      motivo: 'Se niega a firmar el cargue',
      testigoId: 'marta',
      foto: foto('constancia.jpg'),
      fechaMomento: '2026-11-18T14:09:00-05:00',
      lat: 6.24,
      lng: -75.58,
    );

void main() {
  group('quién firma y qué', () {
    test('entrega el inspector del viaje y recibe su conductor; nadie más firma', () {
      expect(parteEnLaNinera(_viaje(), 'ana'), ParteTransferencia.entrega);
      expect(parteEnLaNinera(_viaje(), 'luis'), ParteTransferencia.recibe);
      expect(parteEnLaNinera(_viaje(), 'marta'), isNull);
      expect(parteEnLaNinera(_viaje(), null), isNull);
    });

    // ESC-04: un carro que no sale no entra en la firma ni se le pide nada.
    test('entran los carros activos, por su puesto; los que no salen o se bajaron, no', () {
      expect(carrosDeLaFirma(_viaje()).map((c) => c.vin), ['VINA', 'VINB']);
    });
  });

  // CA-6 y ESC-03.
  group('lo que falta para firmar', () {
    test('con la llegada y la salida de cada carro activo cerradas, nada', () {
      final informes = [
        for (final vin in ['VINA', 'VINB']) ...[_informe(vin, 'llegada'), _informe(vin, 'salida')],
      ];

      expect(faltanteDelCargue(_viaje(), informes), isNull);
    });

    test('nombra el carro y lo que le falta, con las palabras del servidor', () {
      final informes = [
        _informe('VINA', 'llegada'),
        _informe('VINA', 'salida'),
        _informe('VINB', 'llegada'),
        _informe('VINB', 'salida', estado: 'borrador'),
      ];

      expect(faltanteDelCargue(_viaje(), informes), 'Falta la salida del cargue del VIN VINB');
    });

    test('la llegada que falta se dice antes que la salida', () {
      expect(faltanteDelCargue(_viaje(), const []), 'Falta la llegada del cargue del VIN VINA');
    });

    test('el carro que no sale no se exige', () {
      final informes = [
        for (final vin in ['VINA', 'VINB']) ...[_informe(vin, 'llegada'), _informe(vin, 'salida')],
      ];

      expect(faltanteDelCargue(_viaje(), informes), isNull, reason: 'VINX no sale y no tiene informes');
    });
  });

  // Revisión: el servidor exige cerrada la última versión; una corrección en borrador no cuenta como cerrada.
  test('una llegada cerrada con su corrección en borrador no está cerrada', () {
    final informes = [
      for (final vin in ['VINA', 'VINB']) ...[_informe(vin, 'llegada'), _informe(vin, 'salida')],
      InspeccionResumen(id: 'VINB-llegada-v2', vin: 'VINB', momento: 'llegada', momentoNombre: 'llegada', fecha: '', estado: 'borrador', version: 2),
    ];

    expect(faltanteDelCargue(_viaje(), informes), 'Falta la llegada del cargue del VIN VINB');
  });

  test('una versión vieja en borrador ya corregida y cerrada no bloquea', () {
    final informes = [
      for (final vin in ['VINA', 'VINB']) ...[_informe(vin, 'llegada'), _informe(vin, 'salida')],
      InspeccionResumen(id: 'v0', vin: 'VINA', momento: 'salida', momentoNombre: 'salida', fecha: '', estado: 'borrador', version: 0),
    ];

    expect(faltanteDelCargue(_viaje(), informes), isNull);
  });

  test('la hora lleva el desfase de la zona, también negativo', () {
    final momento = DateTime.utc(2026, 11, 18, 19, 9);

    expect(isoConZona(momento, desfase: const Duration(hours: -5)), '2026-11-18T14:09:00-05:00');
    expect(isoConZona(momento, desfase: const Duration(hours: 5, minutes: 30)), '2026-11-19T00:39:00+05:30');
    expect(isoConZona(momento, desfase: Duration.zero), '2026-11-18T19:09:00+00:00');
  });

  // ESC-05: la hora es la del patio, con su zona.
  test('la hora del momento va con su zona', () {
    final texto = isoConZona(DateTime(2026, 11, 18, 14, 9, 5));

    expect(texto, matches(RegExp(r'^2026-11-18T14:09:05[+-]\d{2}:\d{2}$')));
    expect(DateTime.parse(texto).toLocal(), DateTime(2026, 11, 18, 14, 9, 5));
  });

  test('los dos envíos sobreviven a la cola guardada', () {
    final firma = Job.fromJson(_firma(reservas: const [ReservaCarro(vin: 'VINA', observaciones: 'Rayón')]).toJson());
    final constancia = Job.fromJson(_constancia().toJson());

    expect(firma, isA<FirmaNineraJob>());
    expect((firma as FirmaNineraJob).reservas, const [ReservaCarro(vin: 'VINA', observaciones: 'Rayón')]);
    expect(firma.precisionM, 7.5);
    expect(constancia, isA<NoFirmaNineraJob>());
    expect((constancia as NoFirmaNineraJob).testigoId, 'marta');
  });

  group('por la cola', () {
    late ColaEnMemoria cola;
    late GatewayFalso gateway;
    late ProcesadorCola procesador;

    setUp(() {
      cola = ColaEnMemoria();
      gateway = GatewayFalso();
      procesador = ProcesadorCola(cola: cola, gateway: gateway, red: RedFalsa(), archivos: ArchivosFalsos(const ['constancia.jpg']));
    });

    test('la firma sale con su parte, su momento, su GPS y sus reservas', () async {
      await cola.agregar(_firma(reservas: const [ReservaCarro(vin: 'VINA', observaciones: 'Rayón en la puerta')]));

      final r = await procesador.procesar(usuarioPrueba);

      expect(r.enviados, 1);
      final enviada = gateway.firmasNinera.single;
      expect((enviada.viajeId, enviada.parte, enviada.fechaMomento, enviada.lat), ('v1', 'recibe', '2026-11-18T14:09:00-05:00', 6.2442));
      expect(enviada.reservas.single.observaciones, 'Rayón en la puerta');
      expect(cola.jobs, isEmpty);
    });

    // CA-7: el rechazo por cargue incompleto queda a la vista con el mensaje del servidor y la firma se conserva.
    test('si el cargue está incompleto, la firma se conserva con el mensaje del servidor', () async {
      gateway.errores['firmarCargueNinera'] = [
        const ErrorApi(
          message: 'Falta la salida del cargue del VIN VINB',
          code: 'CONFLICT',
          status: 409,
          details: {'motivo': 'cargue_incompleto'},
        ),
      ];
      await cola.agregar(_firma());

      await procesador.procesar(usuarioPrueba);

      final job = cola.jobs.single as FirmaNineraJob;
      expect(job.fallido, isTrue);
      expect(job.retirado, isFalse);
      expect(job.error, contains('Falta la salida del cargue del VIN VINB'));
      expect(job.firma, 'M0 0 L1 1', reason: 'el trazo no se pierde');
    });

    test('la constancia sube su foto una sola vez y luego se registra', () async {
      gateway.errores['registrarNoFirmaNinera'] = [errorHttp(503)];
      await cola.agregar(_constancia());

      await procesador.procesar(usuarioPrueba);
      gateway.llamadas.clear();
      await procesador.procesar(usuarioPrueba);

      expect(gateway.llamadas, ['registrarNoFirmaNinera'], reason: 'la foto ya estaba subida');
      expect(gateway.constanciasNinera.last.foto.storagePath, isNotNull);
      expect(cola.jobs, isEmpty);
    });

    // CA-11: un envío viejo de firma carro por carro en el cargue se retira con el mensaje del servidor.
    test('una firma carro por carro del cargue que el servidor ya no admite se retira', () async {
      gateway.errores['firmarTransferencia'] = [
        const ErrorApi(
          message: 'En el cargue se firma por niñera: firma desde la pantalla de la niñera',
          code: 'CONFLICT',
          status: 409,
          details: {'motivo': 'firma_por_ninera'},
        ),
      ];
      await cola.agregar(const FirmaJob(
        id: 'vieja',
        etiqueta: 'Firma VINA · Cargue',
        creadoEn: 0,
        usuarioId: usuarioPrueba,
        inspeccionId: 'i1',
        entregaId: 'ana',
        recibeId: 'luis',
        firma: 'M0 0',
      ));

      final r = await procesador.procesar(usuarioPrueba);

      expect(r.fallidos, 0);
      expect(cola.jobs.single.retirado, isTrue);
      expect(cola.jobs.single.error, 'En el cargue se firma por niñera: firma desde la pantalla de la niñera');
    });
  });

  group('hacia la API', () {
    (GatewayInformesApi, AdaptadorFalso) gatewayReal() {
      final adaptador = AdaptadorFalso((_) => json(200, {'viaje_id': 'v1'}));
      final dio = Dio()..httpClientAdapter = adaptador;
      return (GatewayInformesApi(ClienteApi(baseUrl: 'https://api.test', token: () async => 'jwt', dio: dio)), adaptador);
    }

    test('la firma va a su ruta con lo que pide el contrato', () async {
      final (api, adaptador) = gatewayReal();

      await api.firmarCargueNinera(
        'v 1',
        parte: 'recibe',
        firma: 'M0 0',
        fechaMomento: '2026-11-18T14:09:00-05:00',
        lat: 6.2442,
        lng: -75.5812,
        precisionM: 7.5,
        reservas: const [ReservaCarro(vin: 'VINA', observaciones: 'Rayón')],
      );

      final peticion = adaptador.peticiones.single;
      expect(peticion.uri.path, '/api/viajes/v%201/cargue/firma');
      expect(peticion.data, {
        'firma': 'M0 0',
        'parte': 'recibe',
        'fecha_momento': '2026-11-18T14:09:00-05:00',
        'lat': 6.2442,
        'lng': -75.5812,
        'precision_m': 7.5,
        'reservas': [
          {'vin': 'VINA', 'observaciones': 'Rayón'},
        ],
      });
    });

    test('sin reservas no se manda la lista', () async {
      final (api, adaptador) = gatewayReal();

      await api.firmarCargueNinera('v1', parte: 'entrega', firma: 'M0 0', fechaMomento: 'x', lat: 1, lng: 2);

      expect((adaptador.peticiones.single.data as Map).containsKey('reservas'), isFalse);
    });

    test('la constancia va a su ruta con la foto, el testigo y el momento', () async {
      final (api, adaptador) = gatewayReal();

      await api.registrarNoFirmaNinera(
        'v1',
        parte: 'recibe',
        motivo: 'Se niega',
        testigoId: 'marta',
        foto: foto('c.jpg', storagePath: 'uploads/c.jpg'),
        fechaMomento: '2026-11-18T14:09:00-05:00',
        lat: 6.24,
        lng: -75.58,
      );

      final peticion = adaptador.peticiones.single;
      expect(peticion.uri.path, '/api/viajes/v1/cargue/no-firma');
      final cuerpo = peticion.data as Map<String, dynamic>;
      expect((cuerpo['parte'], cuerpo['motivo'], cuerpo['testigo_usuario_id']), ('recibe', 'Se niega', 'marta'));
      expect((cuerpo['evidencia'] as Map)['storage_path'], 'uploads/c.jpg');
      expect((cuerpo['fecha_momento'], cuerpo['lat'], cuerpo['lng']), ('2026-11-18T14:09:00-05:00', 6.24, -75.58));
    });
  });
}
