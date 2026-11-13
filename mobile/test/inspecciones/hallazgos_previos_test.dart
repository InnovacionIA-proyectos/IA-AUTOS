import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/features/inspecciones/domain/hallazgo_previo.dart';
import 'package:traza_autos/features/vehiculos/domain/inventario.dart';

/// Un hallazgo previo como lo manda `/api/vehiculos/{vin}/hallazgos-previos`.
Map<String, dynamic> previoApi(
  String id, {
  int? cuadrante = 7,
  String? lado = 'izquierdo',
  String zona = 'Izquierdo 7 · Espejo retrovisor',
  String clase = 'golpe',
  String proceso = 'cargue',
  String cerradaAt = '2026-10-01T09:00:00Z',
  List<Map<String, dynamic>> evidencias = const [],
}) =>
    {
      'id': id,
      'cuadrante_numero': cuadrante,
      'lado': lado,
      'zona': zona,
      'clase_dano': clase,
      'tamano_dano': '1_5cm',
      'nivel_severidad': 'v2',
      'proceso': proceso,
      'momento': Momento.salida,
      'viaje_id': 'vj-1',
      'codigo_mtrack': 'MTK-900',
      'cerrada_at': cerradaAt,
      'evidencias': evidencias,
    };

void main() {
  group('HallazgoPrevio.fromApi', () {
    test('lee la zona, el origen y el cierre que manda el servidor', () {
      final previo = HallazgoPrevio.fromApi(previoApi('h1', proceso: 'transporte'));

      expect(previo.id, 'h1');
      expect(previo.clave, '7I');
      expect(previo.clase, 'golpe');
      expect(previo.tamano, '1_5cm');
      expect(previo.proceso, 'transporte');
      expect(previo.cerradaEn, DateTime.utc(2026, 10, 1, 9));
    });

    // Sin estos dos la lista no puede responder «qué era» ni «de dónde viene», que es justo lo que se mira para
    // decidir si el daño de enfrente es el mismo.
    test('lee el nivel y el viaje donde se registró', () {
      final previo = HallazgoPrevio.fromApi(previoApi('h1'));

      expect(previo.nivel, 'v2');
      expect(previo.codigoMtrack, 'MTK-900');
    });

    test('el nivel y el viaje sobreviven a la copia guardada, que es la que sirve sin señal', () {
      final guardado = HallazgoPrevio.fromJson(HallazgoPrevio.fromApi(previoApi('h1')).toJson());

      expect(guardado.nivel, 'v2');
      expect(guardado.codigoMtrack, 'MTK-900');
      expect(guardado.clave, '7I');
    });

    test('una novedad del conductor sin revisar llega sin nivel, y no se inventa', () {
      final sinNivel = {...previoApi('h1'), 'nivel_severidad': null};

      expect(HallazgoPrevio.fromApi(sinNivel).nivel, isNull);
    });

    test('un daño de un viaje anterior llega igual: el servidor ya no recorta por viaje', () {
      final previos = [
        for (final h in [previoApi('h-otro-viaje'), previoApi('h-este-viaje')]) HallazgoPrevio.fromApi(h),
      ];

      expect([for (final p in previos) p.id], ['h-otro-viaje', 'h-este-viaje']);
      expect(previoDe(previos, clave: '7I', clase: 'golpe')?.id, 'h-otro-viaje');
    });

    test('la foto es la primera evidencia, la de cuando se registró', () {
      final previo = HallazgoPrevio.fromApi(previoApi('h1', evidencias: [
        {'id': 'ev-1', 'url': '/api/uploads/dano.jpg', 'url_expira_at': '2026-10-01T10:00:00Z'},
        {'id': 'ev-2', 'url': '/api/uploads/despues.jpg'},
      ]));

      expect(previo.fotoUrl, '/api/uploads/dano.jpg');
      expect(previo.fotoVenceEn, DateTime.utc(2026, 10, 1, 10));
    });

    test('sin evidencias no hay foto, y aún así se puede proponer', () {
      final previo = HallazgoPrevio.fromApi(previoApi('h1'));

      expect(previo.fotoUrl, isNull);
      expect(previoDe([previo], clave: '7I', clase: 'golpe')?.id, 'h1');
    });

    test('un hallazgo histórico sin cuadrante no se propone: no se sabe dónde pintarlo', () {
      final previo = HallazgoPrevio.fromApi(previoApi('h1', cuadrante: null, lado: null));

      expect(previo.clave, isNull);
      expect(previoDe([previo], clave: '7I', clase: 'golpe'), isNull);
    });

    test('lo guardado en la copia local se vuelve a leer igual', () {
      final previo = HallazgoPrevio.fromApi(previoApi('h1', evidencias: [
        {'id': 'ev-1', 'url': '/api/uploads/dano.jpg'},
      ]));
      final devuelta = HallazgoPrevio.fromJson(previo.toJson());

      expect(devuelta.id, previo.id);
      expect(devuelta.clave, previo.clave);
      expect(devuelta.clase, previo.clase);
      expect(devuelta.proceso, previo.proceso);
      expect(devuelta.cerradaEn, previo.cerradaEn);
      expect(devuelta.fotoUrl, previo.fotoUrl);
    });
  });

  group('previoDe', () {
    final previos = [
      HallazgoPrevio(
        id: 'h-nuevo', cuadranteNumero: 7, lado: 'izquierdo', zona: 'Izquierdo 7 · Espejo retrovisor',
        clase: 'golpe', tamano: '1_5cm', proceso: 'transporte', momento: Momento.salida,
        cerradaEn: DateTime.utc(2026, 10, 2),
      ),
      HallazgoPrevio(
        id: 'h-viejo', cuadranteNumero: 7, lado: 'izquierdo', zona: 'Izquierdo 7 · Espejo retrovisor',
        clase: 'golpe', tamano: '1_5cm', proceso: 'cargue', momento: Momento.salida,
        cerradaEn: DateTime.utc(2026, 10, 1),
      ),
      HallazgoPrevio(
        id: 'h-otra-clase', cuadranteNumero: 7, lado: 'izquierdo', zona: 'Izquierdo 7 · Espejo retrovisor',
        clase: 'rayon', tamano: '1_5cm', proceso: 'cargue', momento: Momento.salida,
        cerradaEn: DateTime.utc(2026, 10, 1),
      ),
    ];

    test('propone el más reciente de la misma zona y la misma clase', () {
      expect(previoDe(previos, clave: '7I', clase: 'golpe')?.id, 'h-nuevo');
    });

    test('otra clase en la misma zona no es el mismo daño', () {
      expect(previoDe(previos, clave: '7I', clase: 'abolladura'), isNull);
    });

    test('la misma clase en otra zona tampoco', () {
      expect(previoDe(previos, clave: '7D', clase: 'golpe'), isNull);
    });
  });
}
