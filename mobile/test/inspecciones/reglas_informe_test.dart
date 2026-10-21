import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/features/inspecciones/domain/reglas_informe.dart';
import 'package:traza_autos/features/sync/domain/job.dart';
import 'package:traza_autos/features/vehiculos/domain/coords_diagrama.dart';

import '../support/dobles_sync.dart';

BloqueoCierre? bloqueo({
  bool gps = true,
  String conductor = 'Ana',
  String placa = 'ABC123',
  int hallazgos = 0,
  int fotos = 0,
  bool catalogos = true,
}) =>
    bloqueoDeCierre(
      hayUbicacion: gps,
      conductor: conductor,
      placa: placa,
      cantidadHallazgos: hallazgos,
      cantidadFotos: fotos,
      catalogosListos: catalogos,
    );

void main() {
  group('bloqueoDeCierre', () {
    test('sin novedades no exige fotos', () => expect(bloqueo(), isNull));

    test('sin ubicación no se puede cerrar', () => expect(bloqueo(gps: false), BloqueoCierre.sinUbicacion));

    test('sin catálogos no se puede cerrar: un informe sin hallazgos diría que el vehículo venía sano', () {
      expect(bloqueo(catalogos: false), BloqueoCierre.sinCatalogos);
      // Tiene prioridad: sin catálogos no hay forma de registrar la novedad, aunque todo lo demás esté listo.
      expect(bloqueo(catalogos: false, gps: false, conductor: ''), BloqueoCierre.sinCatalogos);
    });

    test('exige conductor y placa (también si solo tienen espacios)', () {
      expect(bloqueo(conductor: ''), BloqueoCierre.faltaTransporte);
      expect(bloqueo(placa: '   '), BloqueoCierre.faltaTransporte);
    });

    test('con novedad exige al menos una foto; con la foto ya se puede cerrar', () {
      expect(bloqueo(hallazgos: 1), BloqueoCierre.faltaFotoNovedad);
      expect(bloqueo(hallazgos: 2, fotos: 1), isNull);
    });

    test('las fotos sin novedad son opcionales pero permitidas', () => expect(bloqueo(fotos: 3), isNull));

    test('la ubicación tiene prioridad sobre los demás motivos', () {
      expect(bloqueo(gps: false, conductor: '', hallazgos: 1), BloqueoCierre.sinUbicacion);
    });

    test('cada motivo tiene un mensaje para el usuario', () {
      expect(BloqueoCierre.values.every((b) => b.mensaje.isNotEmpty), isTrue);
    });
  });

  group('construirInformeJob', () {
    InformeJob construir({String trailer = '', String notas = ''}) => construirInformeJob(
          id: 'id-1',
          creadoEn: 99,
          vin: 'VIN123',
          proceso: 'Cargue',
          etapaId: 'etapa-1',
          fecha: DateTime.parse('2026-01-02T05:00:00-05:00'),
          lat: 6.2,
          lng: -75.5,
          conductor: '  Ana  ',
          placa: ' abc123 ',
          trailer: trailer,
          notas: notas,
          fotos: [foto('a.jpg')],
          hallazgos: const [hallazgoEjemplo],
        );

    test('arma el job con la fecha en UTC, textos recortados y momento "informe"', () {
      final job = construir();
      expect(job.fecha, '2026-01-02T10:00:00.000Z');
      expect(job.momento, 'informe');
      expect(job.conductorNombre, 'Ana');
      expect(job.placa, 'abc123');
      expect(job.etiqueta, 'Informe VIN123 · Cargue');
      expect((job.intentos, job.fallido, job.inspeccionId, job.cerrada), (0, false, null, false));
    });

    test('trailer y notas vacíos se envían como null', () {
      final job = construir(trailer: '  ', notas: '');
      expect(job.trailer, isNull);
      expect(job.notas, isNull);
    });

    test('conserva el sello propio de cada foto y los hallazgos', () {
      final job = construir(trailer: 'T-9', notas: ' ojo ');
      expect(job.fotos.single.selloFecha, '2026-01-02T10:01:00.000Z');
      expect(job.hallazgos.single.coords, const CoordsDiagrama(0.5, 0.66));
      expect((job.trailer, job.notas), ('T-9', 'ojo'));
    });
  });

  group('firmaValida', () {
    test('exige personas distintas y al menos una firma', () {
      expect(firmaValida(entregaId: 'a', recibeId: 'b', hayFirma: true), isTrue);
      expect(firmaValida(entregaId: 'a', recibeId: 'a', hayFirma: true), isFalse);
      expect(firmaValida(entregaId: 'a', recibeId: 'b', hayFirma: false), isFalse);
      expect(firmaValida(entregaId: null, recibeId: 'b', hayFirma: true), isFalse);
      expect(firmaValida(entregaId: 'a', recibeId: null, hayFirma: true), isFalse);
    });
  });
}
