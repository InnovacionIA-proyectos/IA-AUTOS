import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/features/vehiculos/domain/etiquetas.dart';
import 'package:traza_autos/features/vehiculos/domain/inventario.dart';

Map<String, dynamic> inspeccionJson(
  String id,
  String fecha, {
  String estado = 'cerrada',
  bool firmada = false,
  String momento = Momento.salida,
  String? cerradaEn,
  String? tipoCierre,
  /// Solo firmó quien entrega: la otra firma queda pendiente, que es el estado normal entre una y otra.
  bool soloEntrega = false,
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
      // Como la sirve la API: una firma por parte, con su fecha. `firmada` del DTO es «ya firmaron las dos».
      'transferencia': firmada || tipoCierre != null || soloEntrega
          ? {
              'entrega_usuario_id': 'u-entrega',
              'recibe_usuario_id': 'u-recibe',
              'entrega_firmada_at': firmada || soloEntrega ? '2026-01-01T12:00:00Z' : null,
              'recibe_firmada_at': firmada ? '2026-01-01T12:30:00Z' : null,
              'tipo_cierre': tipoCierre ?? (firmada ? 'firmada' : null),
            }
          : null,
    };

Map<String, dynamic> etapaJson(
  String id,
  int orden,
  String proceso, {
  bool requiere = true,
  String? viajeId,
  bool conApoyo = false,
  List<Map<String, dynamic>> inspecciones = const [],
}) =>
    {
      'id': id,
      'orden': orden,
      'proceso': proceso,
      'requiere_inspeccion': requiere,
      'viaje_id': viajeId,
      'con_personal_apoyo': conApoyo,
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
            {
              'id': 'h1', 'parte_vehiculo': null, 'coords_diagrama': null, 'clase_dano': 'golpe', 'tamano_dano': '1_5cm',
              'cuadrante_numero': 26, 'lado': 'derecho', 'zona': 'Derecho 26 · Llanta trasera', 'nivel_severidad': 'v2',
            },
          ]),
        ]),
      ]);

      expect(inv.vin, 'VIN00000000000001');
      final insp = inv.etapas.single.inspecciones.single;
      expect(insp.cerrada, isTrue);
      final h = insp.hallazgos.single;
      expect((h.cuadranteNumero, h.lado, h.zona, h.nivel), (26, 'derecho', 'Derecho 26 · Llanta trasera', 'v2'));
      expect(insp.evidencias.single.url, '/api/uploads/i1.jpg');
    });

    test('un hallazgo histórico (solo parte) y una novedad del conductor (sin clase ni zona) se leen sin romper', () {
      final inv = inventario([
        etapaJson('e1', 1, 'cargue', inspecciones: [
          inspeccionJson('i1', '2026-01-02T10:00:00Z', hallazgos: [
            {'id': 'h1', 'parte_vehiculo': 'Capot', 'clase_dano': 'golpe', 'tamano_dano': '1_5cm', 'coords_diagrama': {'x': 0.5, 'y': 0.66}},
            {'id': 'h2', 'parte_vehiculo': null, 'clase_dano': null, 'tamano_dano': '5_10cm', 'zona': null, 'tipo_novedad': 'piedra'},
          ]),
        ]),
      ]);

      final [historico, novedad] = inv.etapas.single.inspecciones.single.hallazgos;
      expect((historico.zona, historico.cuadranteNumero), ('Capot', null));
      expect((novedad.clase, novedad.cuadranteNumero, novedad.tamano), (null, null, '5_10cm'));
    });

    // Un VIN puede haber andado en varios viajes y el inventario los trae todos. Quien abre la unidad lo hace
    // desde un viaje: mezclar las etapas de otro haría ver como pendiente lo que ya se cerró en su momento.
    test('con un viaje, solo se ven sus etapas', () {
      final inv = inventario([
        {'id': 'e1', 'orden': 1, 'proceso': 'cargue', 'requiere_inspeccion': true, 'viaje_id': 'via-1'},
        {'id': 'e2', 'orden': 1, 'proceso': 'cargue', 'requiere_inspeccion': true, 'viaje_id': 'via-2'},
        {'id': 'e3', 'orden': 2, 'proceso': 'transporte', 'requiere_inspeccion': true, 'viaje_id': 'via-1'},
      ]);

      expect([for (final e in inv.delViaje('via-1').etapas) e.id], ['e1', 'e3']);
      expect(inv.delViaje('via-1').vin, inv.vin, reason: 'lo demás del inventario no cambia');
    });

    test('sin viaje se ven todas: se llegó sin saber desde cuál', () {
      final inv = inventario([
        {'id': 'e1', 'orden': 1, 'proceso': 'cargue', 'requiere_inspeccion': true, 'viaje_id': 'via-1'},
        {'id': 'e2', 'orden': 1, 'proceso': 'cargue', 'requiere_inspeccion': true, 'viaje_id': 'via-2'},
      ]);

      expect(inv.delViaje(null).etapas, hasLength(2));
    });

    // Un informe cerrado no se edita: lo que se aclara después llega al lado, con su autor y su fecha.
    test('las notas aclaratorias del informe se leen con su autor', () {
      final inv = inventario([
        etapaJson('e1', 1, 'cargue', inspecciones: [
          {
            ...inspeccionJson('i1', '2026-01-02T10:00:00Z'),
            'notas_aclaratorias': [
              {
                'id': 'n1',
                'texto': 'La placa estaba mal escrita',
                'autor_nombre': 'Ana Pérez',
                'autor_rol': 'inspector',
                'created_at': '2026-01-03T08:00:00Z',
              },
            ],
          },
        ]),
      ]);

      final nota = inv.etapas.single.inspecciones.single.notasAclaratorias.single;
      expect((nota.texto, nota.autorNombre), ('La placa estaba mal escrita', 'Ana Pérez'));
      expect(nota.creadoEn, DateTime.utc(2026, 1, 3, 8));
    });

    test('un informe sin notas aclaratorias no rompe', () {
      final inv = inventario([
        etapaJson('e1', 1, 'cargue', inspecciones: [inspeccionJson('i1', '2026-01-02T10:00:00Z')]),
      ]);

      expect(inv.etapas.single.inspecciones.single.notasAclaratorias, isEmpty);
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
      expect((accion.inspeccionId, accion.soloFirma, accion.cerrada), ('i1', false, false));
    });

    test('con transferencia ya firmada se informa y no se vuelve a firmar', () {
      final inv = inventario([etapaJson('e1', 1, 'cargue', inspecciones: [inspeccionJson('i1', '2026-01-01T10:00:00Z', firmada: true)])]);
      expect((accionDeEtapa(inv.etapas.single, inv) as FirmarTransferencia).cerrada, isTrue);
    });

    // Entre una firma y la otra pueden pasar horas, porque cada parte firma desde su sesión. Ese estado
    // intermedio no es «sin firmar» ni «firmada», y la tarjeta tiene que poder distinguirlo.
    test('con una sola firma la transferencia sigue abierta y se sabe quién falta', () {
      final inv = inventario([etapaJson('e1', 1, 'cargue', inspecciones: [inspeccionJson('i1', '2026-01-01T10:00:00Z', soloEntrega: true)])]);
      final accion = accionDeEtapa(inv.etapas.single, inv) as FirmarTransferencia;

      expect(accion.cerrada, isFalse, reason: 'todavía admite la otra firma');
      expect(accion.transferencia!.completa, isFalse);
      expect(accion.transferencia!.faltaRecibe, isTrue);
    });

    test('sin ninguna firma no se dice que falte nadie en particular', () {
      final inv = inventario([etapaJson('e1', 1, 'cargue', inspecciones: [inspeccionJson('i1', '2026-01-01T10:00:00Z')])]);
      expect((accionDeEtapa(inv.etapas.single, inv) as FirmarTransferencia).transferencia, isNull);
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
            inspeccionJson('ll', '2026-01-01T10:00:00Z',
                momento: Momento.llegada, cerradaEn: '2026-01-01T11:00:00Z', soloEntrega: true),
          ]),
          etapaJson('e2', 2, 'transporte'),
        ]);

        final salida = momentosDeEtapa(inv.etapas[1], inv).first;

        expect(salida.bloqueo!.motivo, BloqueoMomento.transferenciaAbierta);
        expect(mensajeBloqueo(salida.bloqueo!.motivo, 'Cargue'), 'Falta cerrar la transferencia de Cargue');
      });

      // El defecto visto en el celular: la transferencia cuelga del primer informe que se cierra, que es el de
      // salida, y la app la buscaba en el de llegada. Con la constancia ya registrada sobre la salida, la
      // etapa siguiente quedaba bloqueada para siempre aunque el servidor la dejara pasar.
      test('la transferencia cuenta venga del informe que venga, no solo del de llegada', () {
        final inv = inventario([
          etapaJson('e1', 1, 'cargue', inspecciones: [
            inspeccionJson('s', '2026-01-01T08:00:00Z', cerradaEn: '2026-01-01T09:00:00Z', tipoCierre: 'no_firma'),
            inspeccionJson('ll', '2026-01-01T10:00:00Z', momento: Momento.llegada, cerradaEn: '2026-01-01T11:00:00Z'),
          ]),
          etapaJson('e2', 2, 'transporte'),
        ]);

        expect(momentosDeEtapa(inv.etapas[1], inv).first.bloqueo, isNull);
      });

      // La misma pregunta que hace el servidor: bloquea una transferencia **abierta**, no la ausencia de una.
      // Si Calidad quiere exigir que toda etapa tenga su transferencia, la regla tiene que cambiar en la API
      // primero: una regla propia más estricta aquí bloquea trabajo que la API aceptaría.
      test('sin ninguna transferencia no se bloquea, como en el servidor', () {
        final inv = inventario([
          etapaJson('e1', 1, 'cargue', inspecciones: [
            inspeccionJson('ll', '2026-01-01T10:00:00Z', momento: Momento.llegada, cerradaEn: '2026-01-01T11:00:00Z'),
          ]),
          etapaJson('e2', 2, 'transporte'),
        ]);

        expect(momentosDeEtapa(inv.etapas[1], inv).first.bloqueo, isNull);
      });

      test('una sola firma no alcanza: la transferencia sigue abierta', () {
        final inv = inventario([
          etapaJson('e1', 1, 'cargue', inspecciones: [
            inspeccionJson('ll', '2026-01-01T10:00:00Z',
                momento: Momento.llegada, cerradaEn: '2026-01-01T11:00:00Z', soloEntrega: true),
          ]),
          etapaJson('e2', 2, 'transporte'),
        ]);

        expect(momentosDeEtapa(inv.etapas[1], inv).first.bloqueo!.motivo, BloqueoMomento.transferenciaAbierta);
      });

      // Lo que cuenta es el tipo de cierre, no que haya firmas: una constancia de no firma cierra la
      // transferencia sin que nadie haya firmado, y la etapa siguiente debe poder arrancar.
      test('una constancia de no firma cierra la transferencia aunque no haya firmas', () {
        final inv = inventario([
          etapaJson('e1', 1, 'cargue', inspecciones: [
            inspeccionJson('ll', '2026-01-01T10:00:00Z',
                momento: Momento.llegada, cerradaEn: '2026-01-01T11:00:00Z', tipoCierre: 'no_firma'),
          ]),
          etapaJson('e2', 2, 'transporte'),
        ]);

        expect(momentosDeEtapa(inv.etapas[1], inv).first.bloqueo, isNull);
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

  group('quien hace la salida del cargue', () {
    // La salida del cargue la hace un inspector. Si no hay, el conductor activa «No hay inspector» y da el motivo:
    // en vez de «Capturar» se le ofrece esa excepción.
    test('con la marca de personal de apoyo, el conductor usa la excepción en la salida del cargue', () {
      final inv = inventario([etapaJson('e1', 1, 'cargue', conApoyo: true)]);

      expect(conductorUsaExcepcion(inv.etapas.single, rol: 'conductor', momento: 'salida'), isTrue);
      expect(conductorUsaExcepcion(inv.etapas.single, rol: 'inspector', momento: 'salida'), isFalse, reason: 'a el si le toca');
    });

    test('sin la marca en la respuesta (contrato 2.10) la regla es la misma para todos los cargues', () {
      final json = etapaJson('e1', 1, 'cargue')..remove('con_personal_apoyo');
      final etapa = inventario([json]).etapas.single;

      expect(etapa.traeMarcaApoyo, isFalse);
      expect(conductorUsaExcepcion(etapa, rol: 'conductor', momento: 'salida'), isTrue);
    });

    test('con la marca en falso el conductor captura: no hay quien lo haga por el', () {
      final inv = inventario([etapaJson('e1', 1, 'cargue')]);

      expect(conductorUsaExcepcion(inv.etapas.single, rol: 'conductor', momento: 'salida'), isFalse);
    });

    test('solo es de la salida: la llegada del cargue la captura como siempre', () {
      final inv = inventario([etapaJson('e1', 1, 'cargue', conApoyo: true)]);

      expect(conductorUsaExcepcion(inv.etapas.single, rol: 'conductor', momento: 'llegada'), isFalse);
    });

    test('solo aplica al cargue: en descargue el conductor captura igual', () {
      final json = etapaJson('e1', 1, 'descargue', conApoyo: true)..remove('con_personal_apoyo');
      final inv = inventario([json]);

      expect(conductorUsaExcepcion(inv.etapas.single, rol: 'conductor', momento: 'salida'), isFalse);
    });
  });

  group('lo que trae la inspección sin inspector', () {
    test('lee el motivo y las novedades de proceso', () {
      final inv = inventario([
        etapaJson('e1', 1, 'cargue', inspecciones: [
          {
            ...inspeccionJson('i1', '2026-01-01T08:00:00Z', cerradaEn: '2026-01-01T09:00:00Z'),
            'motivo_sin_inspector': {'codigo': 'otro', 'nombre': 'Otro', 'texto': 'El inspector se enfermó'},
            'novedades_proceso': [
              {'tipo': 'cargue_sin_inspector', 'motivo_codigo': 'otro', 'motivo': 'Otro', 'texto': 'El inspector se enfermó', 'usuario_nombre': 'Carlos', 'fecha': '2026-01-01T08:00:00Z'},
            ],
          },
        ]),
      ]);

      final inspeccion = inv.etapas.single.inspecciones.single;
      expect(inspeccion.fueSinInspector, isTrue);
      expect(inspeccion.motivoSinInspector!.resumen, 'Otro: El inspector se enfermó');
      expect(inspeccion.novedadesProceso.single.tipo, 'cargue_sin_inspector');
      expect(inspeccion.novedadesProceso.single.resumen, 'Otro: El inspector se enfermó');
    });

    test('una inspección normal no trae ni motivo ni novedades', () {
      final inv = inventario([etapaJson('e1', 1, 'cargue', inspecciones: [inspeccionJson('i1', '2026-01-01T08:00:00Z')])]);

      final inspeccion = inv.etapas.single.inspecciones.single;
      expect(inspeccion.fueSinInspector, isFalse);
      expect(inspeccion.novedadesProceso, isEmpty);
    });
  });
}
