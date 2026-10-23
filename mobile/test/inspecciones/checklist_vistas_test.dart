import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/features/catalogos/domain/catalogos.dart';
import 'package:traza_autos/features/inspecciones/domain/checklist_vistas.dart';
import 'package:traza_autos/features/inspecciones/domain/foto_local.dart';

/// Las vistas salen del catálogo, no de una lista en el código: TI y el coordinador de calidad deciden cuáles
/// son y cuáles son obligatorias, y la app tiene que obedecer sin una versión nueva.

Map<String, dynamic> vistaJson(
  String codigo,
  String nombre, {
  int orden = 1,
  bool obligatoria = true,
  bool activo = true,
  String? tipoVehiculo,
}) =>
    {
      'id': 'id-$codigo',
      'codigo': codigo,
      'nombre': nombre,
      'orden': orden,
      'activo': activo,
      'obligatoria': obligatoria,
      'tipo_vehiculo': tipoVehiculo,
    };

Catalogos catalogosCon(List<Map<String, dynamic>> vistas) => Catalogos.desdeRespuesta(
      {
        'version': 'v1',
        'catalogos': {Catalogo.vistasFoto: vistas},
      },
      usuarioId: 'u1',
    );

FotoLocal fotoDe(String vistaId, {double? lat = 6.2, String? nota}) => FotoLocal(
      ruta: '$vistaId.jpg',
      selloFecha: '2026-01-02T10:00:00.000Z',
      selloLat: lat,
      selloLng: lat == null ? null : -75.5,
      vistaId: vistaId,
      nota: nota,
    );

void main() {
  group('checklistDeVistas', () {
    test('toma las activas, en su orden, con su obligatoriedad', () {
      final catalogos = catalogosCon([
        vistaJson('trasera', 'Trasera', orden: 2),
        vistaJson('frontal', 'Frontal'),
        vistaJson('bauL', 'Piezas en el baúl', orden: 3, obligatoria: false),
      ]);

      final checklist = checklistDeVistas(catalogos);

      expect(checklist.map((v) => v.codigo), ['frontal', 'trasera', 'bauL']);
      expect(checklist.map((v) => v.obligatoria), [true, true, false]);
      expect(checklist.first.id, 'id-frontal', reason: 'es lo que viaja en la evidencia');
    });

    test('una vista inactiva no se pide', () {
      final checklist = checklistDeVistas(catalogosCon([
        vistaJson('frontal', 'Frontal'),
        vistaJson('retirada', 'Retirada', orden: 2, activo: false),
      ]));

      expect(checklist.map((v) => v.codigo), ['frontal']);
    });

    // Mientras el inventario no traiga el tipo del VIN, valen las vistas que no tienen tipo: son las de cualquiera.
    test('sin tipo de vehículo se usan las vistas sin tipo', () {
      final checklist = checklistDeVistas(catalogosCon([
        vistaJson('frontal', 'Frontal'),
        vistaJson('tolva', 'Tolva', orden: 2, tipoVehiculo: 'camion'),
      ]));

      expect(checklist.map((v) => v.codigo), ['frontal']);
    });

    // Las sin tipo valen para cualquiera y se suman a las del tipo. Con la lectura estricta del criterio, un VIN
    // con tipo se quedaría sin las genéricas y hoy el checklist saldría vacío: todas las sembradas vienen sin
    // tipo, y un checklist vacío deja cerrar un informe sin una sola foto.
    test('con tipo de vehículo se suman las suyas a las genéricas', () {
      final checklist = checklistDeVistas(
        catalogosCon([
          vistaJson('frontal', 'Frontal'),
          vistaJson('tolva', 'Tolva', orden: 2, tipoVehiculo: 'camion'),
        ]),
        tipoVehiculo: 'camion',
      );

      expect(checklist.map((v) => v.codigo), ['frontal', 'tolva']);
    });

    test('las de otro tipo no se piden', () {
      final checklist = checklistDeVistas(
        catalogosCon([
          vistaJson('frontal', 'Frontal'),
          vistaJson('tolva', 'Tolva', orden: 2, tipoVehiculo: 'camion'),
        ]),
        tipoVehiculo: 'automovil',
      );

      expect(checklist.map((v) => v.codigo), ['frontal']);
    });

    test('sin catálogo descargado el checklist viene vacío, no inventado', () {
      expect(checklistDeVistas(Catalogos.vacio), isEmpty);
    });
  });

  group('qué falta para poder cerrar', () {
    final checklist = checklistDeVistas(catalogosCon([
      vistaJson('frontal', 'Frontal'),
      vistaJson('trasera', 'Trasera', orden: 2),
      vistaJson('bauL', 'Piezas en el baúl', orden: 3, obligatoria: false),
    ]));

    test('falta la obligatoria que no tiene foto, y se dice cuál', () {
      final faltan = vistasObligatoriasSinFoto(checklist, [fotoDe('id-frontal')]);

      expect(faltan.map((v) => v.nombre), ['Trasera']);
    });

    test('las adicionales no se exigen', () {
      final faltan = vistasObligatoriasSinFoto(checklist, [fotoDe('id-frontal'), fotoDe('id-trasera')]);

      expect(faltan, isEmpty, reason: 'el baúl solo se toma si hay faltante o daño');
    });

    test('con el checklist vacío no se inventa que falte algo', () {
      expect(vistasObligatoriasSinFoto(const [], const []), isEmpty);
    });
  });

  group('repetir una foto', () {
    test('no borra la anterior: se registra otra de la misma vista y la última cuenta', () {
      final sinGps = fotoDe('id-frontal', lat: null);
      final repetida = FotoLocal(
        ruta: 'repetida.jpg',
        selloFecha: '2026-01-02T10:05:00.000Z',
        selloLat: 6.2,
        selloLng: -75.5,
        vistaId: 'id-frontal',
      );
      final fotos = [sinGps, repetida];

      expect(fotos.deLaVista('id-frontal'), hasLength(2));
      expect(fotos.ultimaDeLaVista('id-frontal')!.ruta, 'repetida.jpg');
      expect(fotos.ultimaDeLaVista('id-frontal')!.sinUbicacion, isFalse);
      expect(sinGps.sinUbicacion, isTrue, reason: 'la anterior se conserva marcada, no se borra');
    });
  });

  group('la etapa decide si hay checklist', () {
    test('Transporte no pide las 7 vistas: ahí lo que se exige es el GPS', () {
      expect(pideChecklist('transporte'), isFalse);
      expect(pideChecklist('cargue'), isTrue);
      expect(pideChecklist('descargue'), isTrue);
    });
  });

  group('guías de cámara', () {
    test('las cuatro que entregó diseño, por código de vista', () {
      for (final codigo in ['lateral_izquierda', 'lateral_derecha', 'diagonal_trasera_izquierda', 'diagonal_delantera_derecha']) {
        expect(guiaDe(codigo), startsWith('assets/guias/'), reason: codigo);
      }
    });

    test('frontal, trasera y superior se toman sin guía', () {
      // Frontal y trasera por diseño; superior porque todavía no existe limpia (en el prototipo solo está
      // dentro de la ficha de daños, con la numeración de cuadrantes, que es otra cosa).
      expect(guiaDe('frontal'), isNull);
      expect(guiaDe('trasera'), isNull);
      expect(guiaDe('superior'), isNull);
    });
  });
}
