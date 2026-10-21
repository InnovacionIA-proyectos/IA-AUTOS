import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/features/vehiculos/domain/etiquetas.dart';
import 'package:traza_autos/features/vehiculos/domain/inventario.dart';
import 'package:traza_autos/features/vehiculos/domain/partes.dart';

Map<String, dynamic> inspeccionJson(String id, String fecha, {String estado = 'cerrada', bool firmada = false, List<Map<String, dynamic>> hallazgos = const []}) => {
      'id': id,
      'momento': 'informe',
      'fecha': fecha,
      'estado': estado,
      'notas': null,
      'hallazgos': hallazgos,
      'evidencias': [
        {'id': 'ev-$id', 'url': '/api/uploads/$id.jpg'},
      ],
      'transferencia': firmada ? {'firmada': true} : null,
    };

Map<String, dynamic> etapaJson(String id, int orden, String proceso, {bool requiere = true, List<Map<String, dynamic>> inspecciones = const []}) =>
    {'id': id, 'orden': orden, 'proceso': proceso, 'requiere_inspeccion': requiere, 'inspecciones': inspecciones};

Inventario inventario(List<Map<String, dynamic>> etapas) =>
    Inventario.fromJson({'id': 'v1', 'vin': 'VIN00000000000001', 'marca': 'Renault', 'modelo': 'Duster', 'operacion': 'importacion', 'etapas': etapas});

void main() {
  group('Inventario.fromJson', () {
    test('lee el contrato snake_case de la API', () {
      final inv = inventario([
        etapaJson('e1', 1, 'cargue', inspecciones: [
          inspeccionJson('i1', '2026-01-02T10:00:00Z', hallazgos: [
            {'id': 'h1', 'parte_vehiculo': 'Capot', 'clase_dano': 'golpe', 'tamano_dano': '1_5cm', 'coords_diagrama': {'x': 0.5, 'y': 0.66}},
          ]),
        ]),
      ]);

      expect(inv.vin, 'VIN00000000000001');
      final insp = inv.etapas.single.inspecciones.single;
      expect(insp.cerrada, isTrue);
      expect(insp.hallazgos.single.coords!.y, 0.66);
      expect(insp.evidencias.single.url, '/api/uploads/i1.jpg');
    });

    test('la etapa trae su lugar y su viaje, que la API ya resuelve', () {
      final inv = inventario([
        {'id': 'e1', 'orden': 1, 'proceso': 'cargue', 'lugar': 'CEDI 1', 'viaje_id': 'via-1', 'requiere_inspeccion': true},
      ]);

      final etapa = inv.etapas.single;
      expect(etapa.lugar, 'CEDI 1', reason: 'el lugar llega como nombre, no como código');
      expect(etapa.viajeId, 'via-1', reason: 'de ahí salen la placa, el tráiler, la unidad y el conductor');
    });

    test('una etapa sin lugar ni viaje no rompe', () {
      final etapa = inventario([
        {'id': 'e1', 'orden': 1, 'proceso': 'cargue', 'requiere_inspeccion': true},
      ]).etapas.single;

      expect((etapa.lugar, etapa.viajeId), (null, null));
    });

    test('tolera campos opcionales ausentes', () {
      final inv = Inventario.fromJson({'id': 'v', 'vin': 'X', 'marca': 'Kia', 'operacion': 'nacional'});
      expect(inv.etapas, isEmpty);
      expect(inv.modelo, isNull);
      expect(inv.ultimaInspeccion, isNull);
    });
  });

  group('ultimaInspeccion', () {
    test('es la de fecha más reciente entre todas las etapas', () {
      final inv = inventario([
        etapaJson('e1', 1, 'cargue', inspecciones: [inspeccionJson('vieja', '2026-01-01T10:00:00Z')]),
        etapaJson('e2', 2, 'transporte', inspecciones: [inspeccionJson('nueva', '2026-01-03T10:00:00Z'), inspeccionJson('media', '2026-01-02T10:00:00Z')]),
      ]);

      final ultima = inv.ultimaInspeccion!;
      expect(ultima.inspeccion.id, 'nueva');
      expect(ultima.etapa.proceso, 'transporte');
    });
  });

  group('accionDeEtapa', () {
    test('etapa que requiere informe y aún no lo tiene: capturar', () {
      final inv = inventario([etapaJson('e1', 1, 'cargue')]);
      expect(accionDeEtapa(inv.etapas.single, inv), isA<CapturarInforme>());
    });

    test('con informe cerrado sin firma: firmar sobre ese informe', () {
      final inv = inventario([etapaJson('e1', 1, 'cargue', inspecciones: [inspeccionJson('i1', '2026-01-01T10:00:00Z')])]);
      final accion = accionDeEtapa(inv.etapas.single, inv) as FirmarTransferencia;
      expect((accion.inspeccionId, accion.soloFirma, accion.firmada), ('i1', false, false));
    });

    test('con transferencia ya firmada se informa y no se vuelve a firmar', () {
      final inv = inventario([etapaJson('e1', 1, 'cargue', inspecciones: [inspeccionJson('i1', '2026-01-01T10:00:00Z', firmada: true)])]);
      expect((accionDeEtapa(inv.etapas.single, inv) as FirmarTransferencia).firmada, isTrue);
    });

    test('prefiere el informe cerrado sobre un borrador', () {
      final inv = inventario([
        etapaJson('e1', 1, 'cargue', inspecciones: [inspeccionJson('borrador', '2026-01-02T10:00:00Z', estado: 'borrador'), inspeccionJson('cerrado', '2026-01-01T10:00:00Z')]),
      ]);
      expect((accionDeEtapa(inv.etapas.single, inv) as FirmarTransferencia).inspeccionId, 'cerrado');
    });

    test('etapa de solo firma: firma sobre el último informe del VIN', () {
      final inv = inventario([
        etapaJson('e1', 1, 'cargue', inspecciones: [inspeccionJson('base', '2026-01-01T10:00:00Z')]),
        etapaJson('e2', 2, 'punto_origen', requiere: false),
      ]);
      final accion = accionDeEtapa(inv.etapas[1], inv) as FirmarTransferencia;
      expect((accion.inspeccionId, accion.soloFirma), ('base', true));
    });

    test('etapa de solo firma sin ningún informe en el VIN: no hay nada que firmar', () {
      final inv = inventario([etapaJson('e1', 1, 'punto_origen', requiere: false)]);
      expect(accionDeEtapa(inv.etapas.single, inv), isA<SinInforme>());
    });
  });

  group('catálogo de partes', () {
    final todas = <(String, double, double)>[
      for (final p in catalogoPartes) ...[
        if (p.coords != null) (p.base, p.coords!.x, p.coords!.y),
        for (final pos in p.posiciones) ('${p.base} ${pos.etiqueta}', pos.coords.x, pos.coords.y),
      ],
    ];

    test('todas las coordenadas están normalizadas entre 0 y 1', () {
      for (final (nombre, x, y) in todas) {
        expect(x, inInclusiveRange(0, 1), reason: nombre);
        expect(y, inInclusiveRange(0, 1), reason: nombre);
      }
    });

    test('cada parte tiene coordenadas propias o posiciones, nunca ambas ni ninguna', () {
      for (final p in catalogoPartes) {
        expect(p.coords != null, p.posiciones.isEmpty, reason: p.base);
      }
    });

    test('no hay etiquetas repetidas', () {
      final etiquetas = todas.map((t) => t.$1).toList();
      expect(etiquetas.toSet().length, etiquetas.length);
    });

    test('etiquetaParte compone parte y posición en minúsculas', () {
      expect(etiquetaParte('Guardafango', 'Delantero derecho'), 'Guardafango delantero derecho');
      expect(etiquetaParte('Capot'), 'Capot');
    });
  });

  group('etiquetas fijas del contrato', () {
    test('devuelven el valor tal cual si no lo conocen', () {
      expect(etiquetar(etiquetaOperacion, 'importacion'), 'Importación');
      expect(etiquetar(etiquetaOperacion, 'operacion_nueva'), 'operacion_nueva');
    });

    test('cubren los valores permitidos del contrato', () {
      for (final o in ['importacion', 'exportacion', 'nacional']) {
        expect(etiquetaOperacion.containsKey(o), isTrue, reason: o);
      }
      for (final e in ['borrador', 'cerrada']) {
        expect(etiquetaEstadoInspeccion.containsKey(e), isTrue, reason: e);
      }
      for (final r in ['inspector', 'conductor', 'coordinador_calidad', 'comercial', 'admin', 'super_admin']) {
        expect(etiquetaRol.containsKey(r), isTrue, reason: r);
      }
    });
  });
}
