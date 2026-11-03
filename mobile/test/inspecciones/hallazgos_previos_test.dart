import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/features/inspecciones/domain/hallazgo_previo.dart';
import 'package:traza_autos/features/vehiculos/domain/inventario.dart';

Map<String, dynamic> hallazgoJson(
  String id, {
  int? cuadrante,
  String? lado,
  String zona = 'Izquierdo 7 · Espejo retrovisor',
  String clase = 'golpe',
}) =>
    {
      'id': id,
      'cuadrante_numero': cuadrante,
      'lado': lado,
      'zona': zona,
      'clase_dano': clase,
      'tamano_dano': '1_5cm',
      'nivel_severidad': 'v2',
      'nivel_nombre': 'V2',
    };

Map<String, dynamic> inspeccionJson(
  String id, {
  required String estado,
  String? cerradaEn,
  List<Map<String, dynamic>> hallazgos = const [],
  List<Map<String, dynamic>> evidencias = const [],
}) =>
    {
      'id': id,
      'momento': Momento.salida,
      'momento_nombre': 'Inspección de salida',
      'fecha': '2026-10-01T08:00:00Z',
      'estado': estado,
      'cerrada_at': cerradaEn,
      'hallazgos': hallazgos,
      'evidencias': evidencias,
    };

Inventario inventario(List<Map<String, dynamic>> etapas) => Inventario.fromJson({
      'id': 'v1',
      'vin': 'VIN00000000000001',
      'marca': 'Renault',
      'modelo': 'Logan',
      'operacion': 'exportacion',
      'etapas': etapas,
    });

Map<String, dynamic> etapaJson(String id, int orden, String proceso, List<Map<String, dynamic>> inspecciones) =>
    {'id': id, 'orden': orden, 'proceso': proceso, 'requiere_inspeccion': true, 'inspecciones': inspecciones};

void main() {
  group('hallazgosPreviosDe', () {
    test('toma los hallazgos de las inspecciones cerradas, la más reciente primero', () {
      final previos = hallazgosPreviosDe(inventario([
        etapaJson('e1', 1, 'cargue', [
          inspeccionJson('i1', estado: 'cerrada', cerradaEn: '2026-10-01T09:00:00Z', hallazgos: [hallazgoJson('h1')]),
        ]),
        etapaJson('e2', 2, 'transporte', [
          inspeccionJson('i2', estado: 'cerrada', cerradaEn: '2026-10-02T09:00:00Z', hallazgos: [hallazgoJson('h2')]),
        ]),
      ]));

      expect([for (final p in previos) p.id], ['h2', 'h1']);
      expect(previos.first.proceso, 'transporte');
    });

    test('un informe en borrador no cuenta: todavía no es historia del vehículo', () {
      final previos = hallazgosPreviosDe(inventario([
        etapaJson('e1', 1, 'cargue', [
          inspeccionJson('i1', estado: 'borrador', hallazgos: [hallazgoJson('h1')]),
        ]),
      ]));

      expect(previos, isEmpty);
    });

    test('cada hallazgo se lleva su foto, que es la que se le muestra a quien compara', () {
      final previos = hallazgosPreviosDe(inventario([
        etapaJson('e1', 1, 'cargue', [
          inspeccionJson(
            'i1',
            estado: 'cerrada',
            cerradaEn: '2026-10-01T09:00:00Z',
            hallazgos: [hallazgoJson('h1')],
            evidencias: [
              {'id': 'ev-vista', 'url': '/api/uploads/vista.jpg', 'hallazgo_id': null},
              {'id': 'ev-dano', 'url': '/api/uploads/dano.jpg', 'hallazgo_id': 'h1'},
            ],
          ),
        ]),
      ]));

      expect(previos.single.fotoUrl, '/api/uploads/dano.jpg');
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
