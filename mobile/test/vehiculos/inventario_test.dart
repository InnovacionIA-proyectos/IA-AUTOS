import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/features/vehiculos/domain/etiquetas.dart';
import 'package:traza_autos/features/vehiculos/domain/inventario.dart';
import 'package:traza_autos/features/vehiculos/domain/partes.dart';

Map<String, dynamic> inspeccionJson(
  String id,
  String fecha, {
  String estado = 'cerrada',
  bool firmada = false,
  String momento = Momento.salida,
  String? cerradaEn,
  String? tipoCierre,
  List<Map<String, dynamic>> hallazgos = const [],
}) =>
    {
      'id': id,
      'momento': momento,
      'momento_nombre': momento == Momento.salida ? 'Inspeccion de salida' : 'Inspeccion de llegada',
      'fecha': fecha,
      'estado': estado,
      'cerrada_at': cerradaEn,
      'notas': null,
      'hallazgos': hallazgos,
      'evidencias': [
        {'id': 'ev-$id', 'url': '/api/uploads/$id.jpg'},
      ],
      'transferencia': firmada || tipoCierre != null ? {'firmada': firmada, 'tipo_cierre': tipoCierre} : null,
    };

Map<String, dynamic> etapaJson(
  String id,
  int orden,
  String proceso, {
  bool requiere = true,
  String? viajeId,
  List<Map<String, dynamic>> inspecciones = const [],
}) =>
    {
      'id': id,
      'orden': orden,
      'proceso': proceso,
      'requiere_inspeccion': requiere,
      'viaje_id': viajeId,
      'inspecciones': inspecciones,
    };

/// Una etapa con su llegada cerrada y su transferencia cerrada: la que no bloquea a la siguiente.
Map<String, dynamic> etapaCompleta(String id, int orden, String proceso) => etapaJson(id, orden, proceso, inspecciones: [
      inspeccionJson('$id-s', '2026-01-01T08:00:00Z', cerradaEn: '2026-01-01T09:00:00Z'),
      inspeccionJson('$id-ll', '2026-01-01T10:00:00Z',
          momento: Momento.llegada, cerradaEn: '2026-01-01T11:00:00Z', tipoCierre: 'firmada'),
    ]);

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
    // Capturar ya no sale de aquí: son dos momentos por etapa y los decide `momentosDeEtapa`.
    test('etapa que requiere informe y aún no lo tiene: no hay sobre qué firmar', () {
      final inv = inventario([etapaJson('e1', 1, 'cargue')]);
      expect(accionDeEtapa(inv.etapas.single, inv), isA<SinInforme>());
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

  group('momentosDeEtapa', () {
    test('una etapa que exige inspeccion tiene salida y llegada, en ese orden', () {
      final inv = inventario([etapaJson('e1', 1, 'cargue')]);

      final momentos = momentosDeEtapa(inv.etapas.single, inv);

      expect(momentos.map((m) => m.momento), [Momento.salida, Momento.llegada]);
      expect(momentos.every((m) => m.inspeccion == null), isTrue);
    });

    test('una etapa que no la exige no tiene momentos: ahi solo se firma', () {
      final inv = inventario([etapaJson('e1', 1, 'transporte', requiere: false)]);

      expect(momentosDeEtapa(inv.etapas.single, inv), isEmpty);
    });

    test('cada momento toma su propia inspeccion', () {
      final inv = inventario([
        etapaJson('e1', 1, 'cargue', inspecciones: [
          inspeccionJson('i-salida', '2026-01-01T08:00:00Z', cerradaEn: '2026-01-01T09:00:00Z'),
          inspeccionJson('i-llegada', '2026-01-01T10:00:00Z', momento: Momento.llegada, estado: 'borrador'),
        ]),
      ]);

      final momentos = momentosDeEtapa(inv.etapas.single, inv);

      expect(momentos[0].inspeccion!.id, 'i-salida');
      expect(momentos[0].cerrada, isTrue);
      expect(momentos[0].inspeccion!.cerradaEn, DateTime.parse('2026-01-01T09:00:00Z'));
      expect(momentos[1].inspeccion!.id, 'i-llegada');
      expect(momentos[1].enBorrador, isTrue);
      expect(momentos[1].sePuedeCapturar, isFalse, reason: 'ya esta empezada');
    });

    test('con varias versiones del mismo momento manda la ultima', () {
      final inv = inventario([
        etapaJson('e1', 1, 'cargue', inspecciones: [
          inspeccionJson('v1', '2026-01-01T08:00:00Z'),
          inspeccionJson('v2', '2026-01-01T09:00:00Z'),
        ]),
      ]);

      expect(momentosDeEtapa(inv.etapas.single, inv).first.inspeccion!.id, 'v2');
    });

    // El servidor responde 409 ETAPA_PREVIA_SIN_CIERRE; la app lo sabe antes de llamar y lo dice.
    group('la salida espera a la etapa anterior', () {
      test('la primera etapa no espera a nadie', () {
        final inv = inventario([etapaJson('e1', 1, 'cargue')]);

        expect(momentosDeEtapa(inv.etapas.single, inv).first.sePuedeCapturar, isTrue);
      });

      test('si la anterior no tiene la llegada cerrada, se bloquea y se dice cual falta', () {
        final inv = inventario([
          etapaJson('e1', 1, 'cargue', inspecciones: [inspeccionJson('s', '2026-01-01T08:00:00Z')]),
          etapaJson('e2', 2, 'transporte'),
        ]);

        final salida = momentosDeEtapa(inv.etapas[1], inv).first;

        expect(salida.sePuedeCapturar, isFalse);
        expect(salida.bloqueo!.motivo, BloqueoMomento.sinLlegada);
        expect(salida.bloqueo!.procesoPrevio, 'cargue');
        expect(mensajeBloqueo(salida.bloqueo!.motivo, 'Cargue'), 'Falta cerrar la llegada de Cargue');
      });

      test('con la llegada cerrada pero la transferencia abierta, tambien', () {
        final inv = inventario([
          etapaJson('e1', 1, 'cargue', inspecciones: [
            inspeccionJson('ll', '2026-01-01T10:00:00Z', momento: Momento.llegada, cerradaEn: '2026-01-01T11:00:00Z'),
          ]),
          etapaJson('e2', 2, 'transporte'),
        ]);

        final salida = momentosDeEtapa(inv.etapas[1], inv).first;

        expect(salida.bloqueo!.motivo, BloqueoMomento.transferenciaAbierta);
        expect(mensajeBloqueo(salida.bloqueo!.motivo, 'Cargue'), 'Falta cerrar la transferencia de Cargue');
      });

      test('firmada no alcanza: lo que cuenta es el tipo de cierre', () {
        final inv = inventario([
          etapaJson('e1', 1, 'cargue', inspecciones: [
            inspeccionJson('ll', '2026-01-01T10:00:00Z',
                momento: Momento.llegada, cerradaEn: '2026-01-01T11:00:00Z', firmada: true),
          ]),
          etapaJson('e2', 2, 'transporte'),
        ]);

        expect(momentosDeEtapa(inv.etapas[1], inv).first.bloqueo!.motivo, BloqueoMomento.transferenciaAbierta);
      });

      test('con la anterior cerrada de verdad, se puede', () {
        final inv = inventario([etapaCompleta('e1', 1, 'cargue'), etapaJson('e2', 2, 'transporte')]);

        expect(momentosDeEtapa(inv.etapas[1], inv).first.sePuedeCapturar, isTrue);
      });

      test('la llegada no espera a la etapa anterior: eso lo impone su propia salida', () {
        final inv = inventario([etapaJson('e1', 1, 'cargue'), etapaJson('e2', 2, 'transporte')]);

        expect(momentosDeEtapa(inv.etapas[1], inv)[1].bloqueo, isNull);
      });

      // El inventario de un VIN puede traer etapas de varios viajes; la anterior es la del mismo viaje.
      test('no se mira la etapa de otro viaje', () {
        final inv = inventario([
          etapaJson('ajena', 1, 'cargue', viajeId: 'otro-viaje'),
          etapaJson('mia', 2, 'transporte', viajeId: 'mi-viaje'),
        ]);

        expect(momentosDeEtapa(inv.etapas[1], inv).first.sePuedeCapturar, isTrue);
      });
    });
  });
}
