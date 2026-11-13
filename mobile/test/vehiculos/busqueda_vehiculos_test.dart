import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/core/http/cliente_api.dart';
import 'package:traza_autos/features/vehiculos/data/vehiculos_repositorio.dart';

import '../support/dio_falso.dart';

/// S7-HU-09 CA-5: la búsqueda del coordinador la contesta el servidor, no la copia local de viajes. Es la
/// diferencia entre encontrar los carros de su cliente y no encontrar nada, porque viajes no tiene ninguno.
Map<String, dynamic> carro(String vin, {String marca = 'Renault', String? modelo = 'Kwid', bool activo = true}) => {
      'vin': vin,
      'marca': marca,
      'modelo': modelo,
      'color': 'Blanco',
      'operacion': 'nacional',
      'activo': activo,
    };

void main() {
  VehiculosRepositorio repositorio(AdaptadorFalso adaptador) => VehiculosRepositorio(
        ClienteApi(baseUrl: 'https://api.test', token: () async => 'jwt', dio: Dio()..httpClientAdapter = adaptador),
      );

  test('pregunta al servidor por la ruta del autocompletado, con lo escrito', () async {
    final adaptador = AdaptadorFalso((_) => json(200, [carro('VIN00000000000001')]));

    final carros = await repositorio(adaptador).buscar('VIN0000');

    final peticion = adaptador.peticiones.single.uri;
    expect(peticion.path, '/api/vehiculos');
    expect(peticion.queryParameters['vin'], 'VIN0000');
    expect(carros.single.vin, 'VIN00000000000001');
    expect(carros.single.descripcion, 'Renault Kwid Blanco');
  });

  test('un carro dado de baja se sigue encontrando: conserva su historial', () async {
    final adaptador = AdaptadorFalso((_) => json(200, [carro('VIN00000000000002', activo: false)]));

    final carros = await repositorio(adaptador).buscar('VIN0000');

    expect(carros.single.activo, isFalse);
  });

  test('sin coincidencias la lista va vacía, no revienta', () async {
    final adaptador = AdaptadorFalso((_) => json(200, <dynamic>[]));

    expect(await repositorio(adaptador).buscar('NADA'), isEmpty);
  });

  test('lo que no venga no deja huecos en la descripción', () async {
    final adaptador = AdaptadorFalso((_) => json(200, [
          {'vin': 'VIN00000000000003', 'marca': 'Mazda', 'modelo': null, 'color': null, 'operacion': 'nacional', 'activo': true},
        ]));

    expect((await repositorio(adaptador).buscar('VIN0000')).single.descripcion, 'Mazda');
  });
}
