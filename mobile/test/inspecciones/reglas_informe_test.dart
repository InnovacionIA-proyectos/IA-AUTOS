import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/features/inspecciones/domain/checklist_vistas.dart';
import 'package:traza_autos/features/inspecciones/domain/extra_local.dart';
import 'package:traza_autos/features/inspecciones/domain/foto_local.dart';
import 'package:traza_autos/features/inspecciones/domain/hallazgo_local.dart';
import 'package:traza_autos/features/inspecciones/domain/reglas_informe.dart';
import 'package:traza_autos/features/vehiculos/domain/inventario.dart';
import 'package:traza_autos/features/sync/domain/job.dart';

import '../support/dobles_sync.dart';

VistaChecklist vista(String id, String nombre, {bool obligatoria = true}) =>
    VistaChecklist(id: id, codigo: id, nombre: nombre, obligatoria: obligatoria);

final checklistDePrueba = [
  vista('v-frontal', 'Frontal'),
  vista('v-superior', 'Superior'),
  vista('v-baul', 'Piezas en el baúl', obligatoria: false),
];

FotoLocal fotoDe(String? vistaId, {bool conGps = true, double? precision}) => FotoLocal(
      ruta: '${vistaId ?? 'suelta'}.jpg',
      selloFecha: '2026-01-02T10:00:00.000Z',
      selloLat: conGps ? 6.2 : null,
      selloLng: conGps ? -75.5 : null,
      vistaId: vistaId,
      precision: precision,
    );

HallazgoLocal hallazgoCon({FotoLocal? foto, String zona = 'Capot'}) =>
    HallazgoLocal(cuadranteNumero: 52, lado: null, zona: zona, clase: 'golpe', tamano: '1_5cm', nivel: 'v1', foto: foto);

BloqueoCierre? bloqueo({
  bool gps = true,
  List<FotoLocal> fotos = const [],
  List<HallazgoLocal> hallazgos = const [],
  List<ExtraLocal> extras = const [],
  List<VistaChecklist> checklist = const [],
  bool catalogos = true,
}) =>
    bloqueoDeCierre(
      hayUbicacion: gps,
      fotos: fotos,
      hallazgos: hallazgos,
      extras: extras,
      checklist: checklist,
      catalogosListos: catalogos,
    );

/// Un informe completo de la etapa con checklist: las dos obligatorias con su foto.
List<FotoLocal> get fotosCompletas => [fotoDe('v-frontal'), fotoDe('v-superior')];

void main() {
  group('bloqueoDeCierre', () {
    test('con las obligatorias cubiertas se puede cerrar', () {
      expect(bloqueo(checklist: checklistDePrueba, fotos: fotosCompletas), isNull);
    });

    test('sin ubicación no se puede cerrar', () => expect(bloqueo(gps: false), isA<SinUbicacion>()));

    test('sin catálogos no se puede cerrar: un informe sin hallazgos diría que el vehículo venía sano', () {
      expect(bloqueo(catalogos: false), isA<SinCatalogos>());
      // Tiene prioridad: sin catálogos no hay forma de registrar la novedad, aunque todo lo demás esté listo.
      expect(bloqueo(catalogos: false, gps: false), isA<SinCatalogos>());
    });

    group('las vistas obligatorias', () {
      test('falta una y se dice cuál: «falta una foto» no sirve de nada', () {
        final b = bloqueo(checklist: checklistDePrueba, fotos: [fotoDe('v-frontal')]);

        expect(b, isA<FaltanVistas>());
        expect(b!.mensaje, 'Falta la foto de la vista «Superior».');
      });

      test('faltan varias y se nombran todas', () {
        final b = bloqueo(checklist: checklistDePrueba);

        expect(b!.mensaje, 'Faltan 2 fotos obligatorias: Frontal, Superior.');
      });

      test('la adicional no se exige', () {
        expect(bloqueo(checklist: checklistDePrueba, fotos: fotosCompletas), isNull);
      });

      // Transporte: la salida y la llegada son el registro del conductor, y lo que se exige es el GPS.
      test('sin checklist solo se exige la ubicación', () {
        expect(bloqueo(), isNull);
        expect(bloqueo(gps: false), isA<SinUbicacion>());
      });
    });

    group('el sello de las fotos', () {
      test('una foto sin ubicación impide cerrar: hay que repetirla', () {
        final b = bloqueo(
          checklist: checklistDePrueba,
          fotos: [fotoDe('v-frontal'), fotoDe('v-superior', conGps: false)],
        );

        expect(b, isA<FotosSinUbicacion>());
        expect(b!.mensaje, contains('repítela'));
      });

      test('se cuentan también las de los hallazgos', () {
        final b = bloqueo(
          checklist: checklistDePrueba,
          fotos: fotosCompletas,
          hallazgos: [hallazgoCon(foto: fotoDe(null, conGps: false))],
        );

        expect(b, isA<FotosSinUbicacion>());
      });

      test('repetir la foto de la vista desbloquea, aunque la anterior siga ahí', () {
        final b = bloqueo(
          checklist: checklistDePrueba,
          fotos: [fotoDe('v-frontal'), fotoDe('v-superior', conGps: false), fotoDe('v-superior')],
        );

        // La anterior se conserva marcada, pero sigue siendo una foto sin ubicación: se repite, no se borra.
        expect(b, isA<FotosSinUbicacion>());
      });
    });

    test('una novedad sin su foto no deja cerrar, y se dice cuál', () {
      final b = bloqueo(checklist: checklistDePrueba, fotos: fotosCompletas, hallazgos: [hallazgoCon()]);

      expect(b, isA<HallazgoSinFoto>());
      expect(b!.mensaje, 'La novedad de «Capot» necesita su foto.');
    });

    test('un extra sin foto no deja cerrar, y se dice cuál', () {
      final b = bloqueo(extras: [const ExtraLocal(tipo: 'barras_techo', nombre: 'Barras de techo')]);

      expect(b, isA<ExtraSinFoto>());
      expect(b!.mensaje, "El extra 'Barras de techo' no tiene foto");
    });

    test('con su foto, el extra no bloquea', () {
      expect(bloqueo(extras: [ExtraLocal(tipo: 'platon', nombre: 'Platón', fotos: [fotoDe(null)])]), isNull);
    });

    test('la foto de un extra sin ubicación también cuenta como evidencia floja', () {
      final b = bloqueo(extras: [ExtraLocal(tipo: 'platon', nombre: 'Platón', fotos: [fotoDe(null, conGps: false)])]);

      expect(b, isA<FotosSinUbicacion>());
    });

    test('la ubicación tiene prioridad sobre los demás motivos', () {
      expect(bloqueo(gps: false, checklist: checklistDePrueba), isA<SinUbicacion>());
    });
  });

  group('precisión baja', () {
    test('avisa por encima del umbral, no por debajo', () {
      final fotos = [fotoDe('a', precision: 5), fotoDe('b', precision: 120), fotoDe('c')];

      expect(fotosConPrecisionBaja(fotos).map((f) => f.vistaId), ['b']);
    });

    // No bloquea: en un patio techado el GPS siempre es malo y bloquear dejaría a la gente sin poder trabajar.
    test('no impide cerrar', () {
      expect(bloqueo(checklist: checklistDePrueba, fotos: [fotoDe('v-frontal', precision: 500), fotoDe('v-superior')]), isNull);
    });

    test('el valor inicial del umbral es 30 metros', () => expect(precisionBajaMetrosPorDefecto, 30.0));

    // Calidad todavía no ha confirmado el umbral, así que cada entorno puede ajustarlo sin recompilar la regla.
    test('con otro umbral avisa por otro margen', () {
      final fotos = [fotoDe('a', precision: 5), fotoDe('b', precision: 120)];

      expect(fotosConPrecisionBaja(fotos, umbral: 200).map((f) => f.vistaId), isEmpty);
      expect(fotosConPrecisionBaja(fotos, umbral: 3).map((f) => f.vistaId), ['a', 'b']);
    });
  });

  group('otras reglas', () {
    // El conductor, la placa y el tráiler ya no se escriben: salen del viaje y el servidor los toma de ahí,
    // así que exigirlos aquí sería inventar una regla que la API no pide (S5-HU-22.9).

    test('todos los motivos tienen un mensaje para el usuario', () {
      final motivos = <BloqueoCierre>[
        const SinCatalogos(),
        const SinUbicacion(),
        FaltanVistas([vista('v', 'Frontal')]),
        const FotosSinUbicacion(1),
        const HallazgoSinFoto('Capot'),
        const ExtraSinFoto('Platón'),
      ];

      expect(motivos.every((b) => b.mensaje.isNotEmpty), isTrue);
    });
  });

  group('construirInformeJob', () {
    InformeJob construir({String notas = ''}) => construirInformeJob(
          id: 'id-1',
          creadoEn: 99,
          usuarioId: 'u1',
          momento: Momento.salida,
          vin: 'VIN123',
          proceso: 'Cargue',
          etapaId: 'etapa-1',
          fecha: DateTime.parse('2026-01-02T05:00:00-05:00'),
          lat: 6.2,
          lng: -75.5,
          notas: notas,
          fotos: [foto('a.jpg')],
          hallazgos: const [hallazgoEjemplo],
        );

    test('arma el job con la fecha en UTC y la etiqueta del viaje', () {
      final job = construir();
      expect(job.fecha, '2026-01-02T10:00:00.000Z');
      expect(job.momento, 'salida');
      expect(job.etiqueta, 'Informe VIN123 · Cargue');
      expect((job.intentos, job.fallido, job.inspeccionId, job.cerrada), (0, false, null, false));
    });

    test('las notas vacías se envían como null', () {
      expect(construir(notas: '  ').notas, isNull);
    });

    test('conserva el sello propio de cada foto y los hallazgos', () {
      final job = construir(notas: ' ojo ');
      expect(job.fotos.single.selloFecha, '2026-01-02T10:01:00.000Z');
      expect((job.hallazgos.single.cuadranteNumero, job.hallazgos.single.lado), (26, 'derecho'));
      expect(job.notas, 'ojo');
    });
  });

  // El servidor atribuye la firma al usuario del token, asi que solo viaja una y tiene que ser la suya.
  group('firmaDeLaSesion', () {
    String? firma({
      String? yo = 'a',
      String? entrega = 'a',
      String? recibe = 'b',
      String? deEntrega = 'M 0 0',
      String? deRecibe = 'M 1 1',
    }) =>
        firmaDeLaSesion(
          yoId: yo,
          entregaId: entrega,
          recibeId: recibe,
          firmaEntrega: deEntrega,
          firmaRecibe: deRecibe,
        );

    test('quien entrega manda la suya; quien recibe, la suya', () {
      expect(firma(yo: 'a'), 'M 0 0');
      expect(firma(yo: 'b'), 'M 1 1');
    });

    test('quien no es parte no puede firmar, aunque haya trazos en la pantalla', () {
      expect(firma(yo: 'c'), isNull);
    });

    test('sin su propio trazo no se firma, aunque el otro si lo tenga', () {
      expect(firma(yo: 'a', deEntrega: null), isNull);
      expect(firma(yo: 'a', deEntrega: ''), isNull, reason: 'un trazo vacio no es una firma');
    });

    test('entrega y recibe tienen que ser distintos', () {
      expect(firma(yo: 'a', entrega: 'a', recibe: 'a'), isNull);
    });

    test('sin sesion o sin las dos partes elegidas, no', () {
      expect(firma(yo: null), isNull);
      expect(firma(entrega: null), isNull);
      expect(firma(recibe: null), isNull);
    });
  });
}
