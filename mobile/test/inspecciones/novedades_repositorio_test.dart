import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/core/http/cliente_api.dart';
import 'package:traza_autos/features/inspecciones/data/novedades_repositorio.dart';

import '../support/dio_falso.dart';

NovedadesRepositorio repositorio(AdaptadorFalso adaptador) => NovedadesRepositorio(
      ClienteApi(baseUrl: 'https://api.test', token: () async => 'jwt', dio: Dio()..httpClientAdapter = adaptador),
    );

void main() {
  group('porRevisar', () {
    test('pide solo las del conductor sin revisar, de ese VIN y ese viaje', () async {
      final adaptador = AdaptadorFalso((_) => json(200, {
            'items': [
              {'id': 'h1', 'vin': 'VIN00000000000001', 'proceso': 'transporte', 'momento': 'llegada', 'tamano_dano': '1_5cm'},
            ],
            'total': 1,
            'pagina': 1,
            'tamano': 20,
          }));

      final novedades = await repositorio(adaptador).porRevisar(vin: 'VIN00000000000001', viajeId: 'via-1');

      final q = adaptador.peticiones.single.uri.queryParameters;
      expect(adaptador.peticiones.single.uri.path, '/api/hallazgos');
      expect((q['vin'], q['viaje_id'], q['origen'], q['revisado']), ('VIN00000000000001', 'via-1', 'conductor', 'false'));
      expect(novedades.single.id, 'h1');
    });

    test('sin viaje no manda la clave: la ficha de la unidad las pide todas', () async {
      final adaptador = AdaptadorFalso((_) => json(200, {'items': [], 'total': 0, 'pagina': 1, 'tamano': 20}));

      await repositorio(adaptador).porRevisar(vin: 'VIN00000000000001');

      expect(adaptador.peticiones.single.uri.queryParameters.containsKey('viaje_id'), isFalse);
    });
  });

  group('revisar', () {
    test('manda zona, clase, nivel y el tamaño ya corregido', () async {
      final adaptador = AdaptadorFalso((_) => json(200, {'id': 'h1'}));

      await repositorio(adaptador).revisar(
        'h1',
        cuadranteNumero: 7,
        lado: 'izquierdo',
        clase: 'golpe',
        nivel: 'v2',
        tamano: 'mas_20cm',
      );

      final peticion = adaptador.peticiones.single;
      expect(peticion.uri.path, '/api/hallazgos/h1/revision');
      final cuerpo = peticion.data as Map<String, dynamic>;
      expect((cuerpo['cuadrante_numero'], cuerpo['lado'], cuerpo['clase_dano'], cuerpo['nivel_severidad'], cuerpo['tamano_dano']),
          (7, 'izquierdo', 'golpe', 'v2', 'mas_20cm'));
      // El contrato las exige presentes aunque vayan nulas.
      expect(cuerpo.containsKey('zona_provisional'), isTrue);
      expect(cuerpo.containsKey('nota'), isTrue);
      // Sin preexistencia, el origen no se manda.
      expect(cuerpo['es_preexistente'], false);
      expect(cuerpo.containsKey('hallazgo_origen_id'), isFalse);
    });

    test('si ya estaba registrado, viaja con su origen', () async {
      final adaptador = AdaptadorFalso((_) => json(200, {'id': 'h1'}));

      await repositorio(adaptador).revisar(
        'h1',
        cuadranteNumero: 7,
        lado: 'izquierdo',
        clase: 'golpe',
        nivel: 'v2',
        tamano: 'mas_20cm',
        esPreexistente: true,
        hallazgoOrigenId: 'h-previo',
      );

      final cuerpo = adaptador.peticiones.single.data as Map<String, dynamic>;
      expect((cuerpo['es_preexistente'], cuerpo['hallazgo_origen_id']), (true, 'h-previo'));
    });
  });
}
