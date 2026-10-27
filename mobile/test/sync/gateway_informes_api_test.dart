import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/core/http/cliente_api.dart';
import 'package:traza_autos/features/inspecciones/domain/foto_local.dart';
import 'package:traza_autos/features/inspecciones/domain/hallazgo_local.dart';
import 'package:traza_autos/features/sync/data/gateway_informes_api.dart';
import 'package:traza_autos/features/vehiculos/domain/coords_diagrama.dart';

import '../support/dio_falso.dart';

/// Lo que de verdad sale hacia la API. Probar el procesador no alcanza: ahí se ve el orden de las llamadas, no
/// el cuerpo, y un campo que falta en el cuerpo es un 400 en el patio.

(GatewayInformesApi, AdaptadorFalso) gateway(ResponseBody Function(RequestOptions) responder) {
  final adaptador = AdaptadorFalso(responder);
  final dio = Dio()..httpClientAdapter = adaptador;
  return (GatewayInformesApi(ClienteApi(baseUrl: 'https://api.test', token: () async => 'jwt', dio: dio)), adaptador);
}

const fotoDelDano = FotoLocal(
  ruta: 'dano.jpg',
  selloFecha: '2026-01-02T10:01:00.000Z',
  selloLat: 6.21,
  selloLng: -75.51,
  precision: 9.5,
  dispositivoId: 'equipo-1',
  hash: 'hash-abc',
  storagePath: 'uploads/dano.jpg',
);

void main() {
  test('el hallazgo lleva su evidencia dentro, con el sello y el hash', () async {
    final (api, adaptador) = gateway((_) => json(201, {'id': 'h1'}));

    await api.agregarHallazgo(
      'insp-1',
      const HallazgoLocal(
        parte: 'Capot',
        clase: 'golpe',
        tamano: '1_5cm',
        coords: CoordsDiagrama(0.5, 0.66),
        foto: fotoDelDano,
      ),
    );

    final cuerpo = adaptador.peticiones.single.data as Map<String, dynamic>;
    expect(cuerpo['parte_vehiculo'], 'Capot');
    final evidencia = cuerpo['evidencia'] as Map<String, dynamic>;
    expect(evidencia['storage_path'], 'uploads/dano.jpg');
    expect(evidencia['sello_fecha'], '2026-01-02T10:01:00.000Z');
    expect(evidencia['sello_lat'], 6.21);
    expect(evidencia['precision'], 9.5);
    expect(evidencia['dispositivo_id'], 'equipo-1');
    expect(evidencia['hash_sha256'], 'hash-abc');
  });

  test('la evidencia de una vista lleva las diez claves, aunque alguna vaya nula', () async {
    final (api, adaptador) = gateway((_) => json(201, {'id': 'e1'}));

    await api.agregarEvidencia(
      'insp-1',
      storagePath: 'uploads/frontal.jpg',
      selloFecha: '2026-01-02T10:00:00.000Z',
      vistaId: 'v-frontal',
    );

    final cuerpo = adaptador.peticiones.single.data as Map<String, dynamic>;
    // La API las exige presentes; el valor puede ir nulo (una foto sin GPS, una vista sin nota).
    expect(
      cuerpo.keys,
      containsAll([
        'hallazgo_id', 'vista_id', 'storage_path', 'sello_fecha', 'sello_lat',
        'sello_lng', 'precision', 'dispositivo_id', 'nota', 'hash_sha256',
      ]),
    );
    expect(cuerpo['vista_id'], 'v-frontal');
    expect(cuerpo['sello_lat'], isNull);
  });

  test('la imagen sube como multipart y se queda con el hash del servidor', () async {
    final (api, adaptador) = gateway((_) => json(201, {'storage_path': 'uploads/x.jpg', 'hash_sha256': 'h-1'}));

    final subido = await api.subirImagen([1, 2, 3], nombre: 'frontal.jpg');

    expect(subido.storagePath, 'uploads/x.jpg');
    expect(subido.hash, 'h-1');
    expect(adaptador.peticiones.single.data, isA<FormData>());
    expect((adaptador.peticiones.single.data as FormData).files.single.key, 'archivo');
  });

  test('la inspección se crea con su momento, no con un valor fijo', () async {
    final (api, adaptador) = gateway((_) => json(201, {'id': 'insp-1'}));

    await api.crearInspeccion(
      etapaId: 'e1',
      momento: 'llegada',
      fecha: '2026-01-02T10:00:00.000Z',
      lat: 6.2,
      lng: -75.5,
    );

    expect((adaptador.peticiones.single.data as Map<String, dynamic>)['momento'], 'llegada');
  });
}
