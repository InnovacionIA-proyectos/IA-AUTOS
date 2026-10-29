import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/core/http/cliente_api.dart';
import 'package:traza_autos/features/catalogos/domain/catalogos.dart';
import 'package:traza_autos/features/inspecciones/domain/extra_local.dart';
import 'package:traza_autos/features/inspecciones/domain/foto_local.dart';
import 'package:traza_autos/features/sync/data/gateway_informes_api.dart';
import 'package:traza_autos/features/sync/domain/job.dart';
import 'package:traza_autos/features/sync/domain/procesador_cola.dart';

import '../support/arnes.dart';
import '../support/dio_falso.dart';
import '../support/dobles_sync.dart';

/// S6-HU-16.3 CA-4: los extras salen con el informe, antes de cerrar, y el envío se reanuda sin duplicar nada.

ExtraLocal extra(String nombre, {String tipo = 'platon', List<String> rutas = const ['e1.jpg'], String? texto, String? observacion}) =>
    ExtraLocal(
      tipo: tipo,
      nombre: nombre,
      textoOtro: texto,
      observacion: observacion,
      fotos: [for (final r in rutas) foto(r)],
    );

InformeJob conExtras(List<ExtraLocal> extras, {String? inspeccionId}) => InformeJob(
      id: 'j1',
      etiqueta: 'Informe',
      creadoEn: 1,
      usuarioId: usuarioPrueba,
      etapaId: 'etapa-1',
      momento: 'llegada',
      fecha: '2026-01-02T10:00:00.000Z',
      lat: 6.2,
      lng: -75.5,
      extras: extras,
      inspeccionId: inspeccionId,
    );

void main() {
  late ColaEnMemoria cola;
  late GatewayFalso gateway;
  late ProcesadorCola procesador;

  setUp(() {
    cola = ColaEnMemoria();
    gateway = GatewayFalso();
    procesador = ProcesadorCola(cola: cola, gateway: gateway, red: RedFalsa(), archivos: ArchivosFalsos(['e1.jpg', 'e2.jpg', 'e3.jpg']));
  });

  test('el catálogo se llama como lo publica la API', () {
    expect(Catalogo.tiposExtra, 'tipos-extra');
  });

  group('el extra en el modelo local', () {
    test('guarda el id que le dio el servidor y lo conserva al agregarle fotos', () {
      final conId = extra('Platón').conId('x-1');
      expect(ExtraLocal.fromJson(conId.toJson()).extraId, 'x-1');
      expect(conId.conFoto(foto('f.jpg')).extraId, 'x-1');
      expect(ExtraLocal.fromJson(extra('Platón').toJson()).extraId, isNull, reason: 'sin enviar, sin id');
    });

    test('los informes encolados antes de enviar extras se leen igual', () {
      final json = extra('Platón').toJson()..remove('extra_id');
      expect(ExtraLocal.fromJson(json).extraId, isNull);
    });
  });

  group('el envío', () {
    test('crea el extra, manda su foto y recién entonces cierra', () async {
      await cola.agregar(conExtras([extra('Platón')]));

      await procesador.procesar(usuarioPrueba);

      expect(gateway.llamadas, ['crearInspeccion', 'crearExtra', 'subirImagen', 'agregarEvidencia', 'cerrarInspeccion']);
      expect(gateway.extrasCreados.single, ('insp-1', 'platon', null, null));
      expect(gateway.evidencias.single['extra_id'], 'extra-1', reason: 'la foto va con el id que devolvió el servidor');
      expect(cola.jobs, isEmpty);
    });

    test('«Otro» lleva su texto y la observación solo si la hay', () async {
      await cola.agregar(conExtras([
        extra('Otro: portabicicletas', tipo: 'otro', texto: 'portabicicletas', observacion: 'en el techo'),
        extra('Volco', tipo: 'volco'),
      ]));

      await procesador.procesar(usuarioPrueba);

      expect(gateway.extrasCreados, [('insp-1', 'otro', 'portabicicletas', 'en el techo'), ('insp-1', 'volco', null, null)]);
    });

    test('varias fotos de un extra salen todas, con su sello propio', () async {
      await cola.agregar(conExtras([extra('Platón', rutas: ['e1.jpg', 'e2.jpg'])]));

      await procesador.procesar(usuarioPrueba);

      expect(gateway.evidencias, hasLength(2));
      expect(gateway.evidencias.map((e) => e['extra_id']), ['extra-1', 'extra-1']);
    });

    test('un corte después de crear el extra no lo crea otra vez', () async {
      gateway.fallarEnLlamada('subirImagen', 1, Exception('sin red'));
      await cola.agregar(conExtras([extra('Platón')]));

      await procesador.procesar(usuarioPrueba);
      await procesador.procesar(usuarioPrueba);

      expect(gateway.extrasCreados, hasLength(1));
      expect(gateway.evidencias, hasLength(1));
      expect(cola.jobs, isEmpty);
    });

    test('un corte después de subir la foto no la sube ni la registra de nuevo', () async {
      gateway.fallarEnLlamada('agregarEvidencia', 1, Exception('sin red'));
      await cola.agregar(conExtras([extra('Platón')]));

      await procesador.procesar(usuarioPrueba);
      await procesador.procesar(usuarioPrueba);

      expect(gateway.llamadas.where((l) => l == 'subirImagen'), hasLength(1));
      expect(gateway.evidencias, hasLength(1));
    });

    test('un informe sin extras no toca esa ruta', () async {
      await cola.agregar(conExtras(const []));

      await procesador.procesar(usuarioPrueba);

      expect(gateway.llamadas, ['crearInspeccion', 'cerrarInspeccion']);
    });

    test('si el servidor rechaza el extra, el informe queda fallido con su mensaje y no se cierra', () async {
      gateway.errores['crearExtra'] = [errorApi(400, 'El tipo no existe o está inactivo')];
      await cola.agregar(conExtras([extra('Platón')]));

      final r = await procesador.procesar(usuarioPrueba);

      expect(r.fallidos, 1);
      expect(cola.jobs.single.error, contains('El tipo no existe'));
      expect(gateway.llamadas, isNot(contains('cerrarInspeccion')));
    });
  });

  group('hacia la API', () {
    (GatewayInformesApi, AdaptadorFalso) gatewayApi() {
      final adaptador = AdaptadorFalso((_) => json(201, {'id': 'extra-9'}));
      final api = ClienteApi(baseUrl: 'https://api.test', token: () async => 'jwt', dio: Dio()..httpClientAdapter = adaptador);
      return (GatewayInformesApi(api), adaptador);
    }

    test('crear un extra manda solo lo que admite el contrato y devuelve su id', () async {
      final (api, adaptador) = gatewayApi();

      final id = await api.crearExtra('insp 1', tipo: 'otro', texto: 'portabicicletas', observacion: 'en el techo');

      expect(id, 'extra-9');
      final peticion = adaptador.peticiones.single;
      expect((peticion.method, peticion.uri.path), ('POST', '/api/inspecciones/insp%201/extras'));
      expect(peticion.data, {'tipo': 'otro', 'texto': 'portabicicletas', 'observacion': 'en el techo'});
    });

    test('sin texto ni observación, esos campos no viajan', () async {
      final (api, adaptador) = gatewayApi();

      await api.crearExtra('insp-1', tipo: 'platon');

      expect(adaptador.peticiones.single.data, {'tipo': 'platon'});
    });

    test('la foto de un extra lleva su extra_id; las demás evidencias no', () async {
      final (api, adaptador) = gatewayApi();

      await api.agregarEvidencia('insp-1', storagePath: 'a.jpg', selloFecha: '2026-01-02T10:00:00Z', extraId: 'extra-9');
      await api.agregarEvidencia('insp-1', storagePath: 'b.jpg', selloFecha: '2026-01-02T10:00:00Z', vistaId: 'v1');

      expect((adaptador.peticiones.first.data as Map)['extra_id'], 'extra-9');
      expect((adaptador.peticiones.last.data as Map).keys, isNot(contains('extra_id')));
    });
  });

  test('FotoLocal de un extra no pierde su sello al guardarse y leerse', () {
    final f = FotoLocal.fromJson(foto('e1.jpg').toJson());
    expect(f.selloFecha, foto('e1.jpg').selloFecha);
  });
}
