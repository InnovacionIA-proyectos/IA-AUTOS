import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/features/vehiculos/domain/inventario.dart';
import 'package:traza_autos/features/vehiculos/domain/no_sale.dart';

Map<String, dynamic> hallazgo(String id, String? nivel) =>
    {'id': id, 'zona': 'Izquierdo 7 · Espejo retrovisor', 'cuadrante_numero': 7, 'lado': 'izquierdo', 'clase_dano': 'golpe', 'tamano_dano': '1_5cm', 'nivel_severidad': nivel};

Map<String, dynamic> etapa(
  String id,
  String? viajeId, {
  required String estado,
  String? cerradaEn,
  List<Map<String, dynamic>> hallazgos = const [],
}) =>
    {
      'id': id,
      'orden': 1,
      'proceso': 'cargue',
      'requiere_inspeccion': true,
      'viaje_id': viajeId,
      'inspecciones': [
        {
          'id': 'i-$id',
          'momento': 'salida',
          'fecha': '2026-10-01T08:00:00Z',
          'estado': estado,
          'cerrada_at': cerradaEn,
          'hallazgos': hallazgos,
        },
      ],
    };

/// Una etapa con una inspección corregida: la versión 1 con [danosV1] daños V1+ y la versión 2 con [danosV2].
/// Mientras la corrección siga en borrador no reemplaza nada, y la versión 1 sigue siendo la que vale.
Map<String, dynamic> etapaCorregida(
  String id,
  String? viajeId, {
  required int danosV1,
  required int danosV2,
  bool correccionCerrada = true,
}) =>
    {
      'id': id,
      'orden': 1,
      'proceso': 'cargue',
      'requiere_inspeccion': true,
      'viaje_id': viajeId,
      'inspecciones': [
        {
          'id': 'i1-$id',
          'momento': 'salida',
          'fecha': '2026-10-01T08:00:00Z',
          'estado': 'cerrada',
          'cerrada_at': '2026-10-01T09:00:00Z',
          'version': 1,
          'hallazgos': [for (var i = 1; i <= danosV1; i++) hallazgo('v1-h$i', 'v1_plus')],
        },
        {
          'id': 'i2-$id',
          'momento': 'salida',
          'fecha': '2026-10-02T08:00:00Z',
          'estado': correccionCerrada ? 'cerrada' : 'borrador',
          'cerrada_at': correccionCerrada ? '2026-10-02T09:00:00Z' : null,
          'version': 2,
          'corrige_inspeccion_id': 'i1-$id',
          'hallazgos': [for (var i = 1; i <= danosV2; i++) hallazgo('v2-h$i', 'v1_plus')],
        },
      ],
    };

Inventario inventario(List<Map<String, dynamic>> etapas) => Inventario.fromJson({
      'id': 'v1', 'vin': 'VIN00000000000001', 'marca': 'Renault', 'modelo': 'Logan', 'operacion': 'exportacion',
      'etapas': etapas,
    });

void main() {
  group('hallazgosQueImpidenSalir', () {
    test('un V1+ de una inspección cerrada del viaje es el que habilita «no sale»', () {
      final impiden = hallazgosQueImpidenSalir(
        inventario([etapa('e1', 'via-1', estado: 'cerrada', cerradaEn: '2026-10-01T09:00:00Z', hallazgos: [hallazgo('h1', 'v1_plus')])]),
        viajeId: 'via-1',
      );

      expect([for (final h in impiden) h.id], ['h1']);
    });

    test('un V1 a secas no basta: la regla es V1+', () {
      final impiden = hallazgosQueImpidenSalir(
        inventario([etapa('e1', 'via-1', estado: 'cerrada', cerradaEn: '2026-10-01T09:00:00Z', hallazgos: [hallazgo('h1', 'v1')])]),
        viajeId: 'via-1',
      );

      expect(impiden, isEmpty);
    });

    test('un informe en borrador no cuenta aunque traiga el V1+', () {
      final impiden = hallazgosQueImpidenSalir(
        inventario([etapa('e1', 'via-1', estado: 'borrador', hallazgos: [hallazgo('h1', 'v1_plus')])]),
        viajeId: 'via-1',
      );

      expect(impiden, isEmpty, reason: 'el servidor responde 409 si la inspección no está cerrada');
    });

    test('los 4 daños de una versión reemplazada ya no justifican nada: el que vale es el de la corrección', () {
      final impiden = hallazgosQueImpidenSalir(
        inventario([etapaCorregida('e1', 'via-1', danosV1: 4, danosV2: 1)]),
        viajeId: 'via-1',
      );

      expect([for (final h in impiden) h.id], ['v2-h1'], reason: 'S7-HU-20 ESC-04: el daño que la corrección ya no trae no existe');
    });

    test('mientras la corrección siga en borrador, los 4 de la versión 1 siguen contando', () {
      final impiden = hallazgosQueImpidenSalir(
        inventario([etapaCorregida('e1', 'via-1', danosV1: 4, danosV2: 1, correccionCerrada: false)]),
        viajeId: 'via-1',
      );

      expect([for (final h in impiden) h.id], ['v1-h1', 'v1-h2', 'v1-h3', 'v1-h4']);
    });

    test('un V1+ de otro viaje no saca la unidad de este', () {
      final impiden = hallazgosQueImpidenSalir(
        inventario([etapa('e1', 'via-otro', estado: 'cerrada', cerradaEn: '2026-10-01T09:00:00Z', hallazgos: [hallazgo('h1', 'v1_plus')])]),
        viajeId: 'via-1',
      );

      expect(impiden, isEmpty);
    });
  });

  group('puedeMarcarNoSale', () {
    test('solo el inspector de ese viaje', () {
      expect(puedeMarcarNoSale(rol: 'inspector', esInspectorDelViaje: true), isTrue);
      expect(puedeMarcarNoSale(rol: 'inspector', esInspectorDelViaje: false), isFalse);
    });

    // S8-HU-02.2 CA-2: el conductor, el coordinador en la app y TI no la ven.
    test('nadie más, aunque sea «del viaje»', () {
      for (final rol in ['conductor', 'coordinador_calidad', 'admin', 'super_admin']) {
        expect(puedeMarcarNoSale(rol: rol, esInspectorDelViaje: true), isFalse, reason: rol);
      }
    });
  });

  // S8-HU-02.2 CA-5: «Vuelve a salir» mientras la entrega del cargue no se haya cerrado.
  group('puedeVolverASalir', () {
    test('el inspector del viaje, con el cargue abierto', () {
      expect(puedeVolverASalir(rol: 'inspector', esInspectorDelViaje: true, cargueCerrado: false), isTrue);
    });

    test('con el cargue cerrado ya no, ni otro rol', () {
      expect(puedeVolverASalir(rol: 'inspector', esInspectorDelViaje: true, cargueCerrado: true), isFalse);
      expect(puedeVolverASalir(rol: 'conductor', esInspectorDelViaje: true, cargueCerrado: false), isFalse);
      expect(puedeVolverASalir(rol: 'inspector', esInspectorDelViaje: false, cargueCerrado: false), isFalse);
    });
  });
}
