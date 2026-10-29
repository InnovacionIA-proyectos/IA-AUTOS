import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/features/inspecciones/domain/acomodacion.dart';
import 'package:traza_autos/features/inspecciones/domain/foto_local.dart';
import 'package:traza_autos/features/inspecciones/domain/reglas_informe.dart';
import 'package:traza_autos/features/sync/domain/job.dart';
import 'package:traza_autos/features/viajes/domain/viaje.dart';

import '../support/dobles_sync.dart';

const _a = ViajeVehiculo(vin: 'VINA0000000000001');
const _b = ViajeVehiculo(vin: 'VINB0000000000002');
const _c = ViajeVehiculo(vin: 'VINC0000000000003');
const _noSale = ViajeVehiculo(vin: 'VIND0000000000004', estadoUnidad: 'no_sale');

FotoLocal _foto({String? vistaId}) =>
    FotoLocal(ruta: 'f.jpg', selloFecha: '2026-01-02T10:00:00.000Z', selloLat: 6.2, selloLng: -75.5, vistaId: vistaId);

BloqueoCierre? _bloqueo({
  required List<ViajeVehiculo>? unidades,
  Map<String, int> posiciones = const {},
  List<FotoLocal> fotos = const [],
  int minimas = 1,
}) => bloqueoDeCierre(
  hayUbicacion: true,
  fotos: fotos,
  hallazgos: const [],
  acomodacion: Acomodacion(unidades: unidades, posiciones: posiciones, fotosMinimas: minimas),
);

void main() {
  group('qué es la segunda inspección del cargue', () {
    test('es la salida del cargue y nada más', () {
      expect(esSegundaInspeccionDelCargue(proceso: 'cargue', momento: 'salida'), isTrue);
      expect(esSegundaInspeccionDelCargue(proceso: 'cargue', momento: 'llegada'), isFalse);
      expect(esSegundaInspeccionDelCargue(proceso: 'descargue', momento: 'salida'), isFalse);
      expect(esSegundaInspeccionDelCargue(proceso: 'transporte', momento: 'salida'), isFalse);
    });
  });

  group('lugares de la niñera', () {
    test('el esquema trae los siete lugares, 4-3-2-1 arriba y 7-6-5 abajo', () {
      expect(esquemaNinera, [
        [4, 3, 2, 1],
        [7, 6, 5],
      ]);
      expect(esquemaNinera.expand((f) => f).toList()..sort(), [1, 2, 3, 4, 5, 6, 7]);
    });

    test('ocupante dice quién tiene un lugar, sin contar a quien lo está eligiendo', () {
      final posiciones = {_a.vin: 4, _b.vin: 3};
      expect(ocupante(posiciones, 4), _a.vin);
      expect(ocupante(posiciones, 4, except: _a.vin), isNull);
      expect(ocupante(posiciones, 7), isNull);
    });

    test('vinesSinPosicion ignora las unidades que no salen', () {
      expect(vinesSinPosicion([_a, _b, _noSale], {_a.vin: 4}), [_b.vin]);
      expect(vinesSinPosicion([_a, _noSale], {_a.vin: 4}), isEmpty);
    });

    test('el viaje trae las posiciones de sus unidades que salen', () {
      final viaje = Viaje(
        id: 'v1',
        codigoMtrack: 'M1',
        placa: 'AAA111',
        estado: 'en_curso',
        vehiculos: [
          ViajeVehiculo.fromJson({'vin': _a.vin, 'posicion': 4}),
          ViajeVehiculo.fromJson({'vin': _b.vin}),
          ViajeVehiculo.fromJson({'vin': _noSale.vin, 'estado_unidad': 'no_sale', 'posicion': 6}),
        ],
      );
      expect(posicionesDelViaje(viaje), {_a.vin: 4});
      expect(unidadesActivas(viaje).map((u) => u.vin), [_a.vin, _b.vin]);
    });

    test('la posición del viaje sobrevive a guardarse y leerse', () {
      final ida = ViajeVehiculo.fromJson({'vin': _a.vin, 'posicion': 3});
      expect(ViajeVehiculo.fromJson(ida.toJson()).posicion, 3);
      expect(ViajeVehiculo.fromJson({'vin': _a.vin}).posicion, isNull);
    });
  });

  group('lo que bloquea el cierre de la acomodación', () {
    test('sin conocer las unidades del viaje no se puede cerrar', () {
      expect(_bloqueo(unidades: null), isA<SinUnidadesDelViaje>());
    });

    test('falta la posición de una unidad: lo dice con el VIN y las mismas palabras del servidor', () {
      final b = _bloqueo(unidades: [_a, _b, _c], posiciones: {_a.vin: 4, _c.vin: 7}, fotos: [_foto()]);
      expect(b, isA<FaltaPosicion>());
      expect(b!.mensaje, 'Falta la posición de la unidad ${_b.vin}');
    });

    test('con todas las posiciones faltan las fotos de la acomodación', () {
      final b = _bloqueo(unidades: [_a], posiciones: {_a.vin: 4}, minimas: 2, fotos: [_foto()]);
      expect(b!.mensaje, 'Faltan fotos de la acomodación: se exige al menos 2 y hay 1');
    });

    test('una foto de una vista no cuenta como foto de la acomodación', () {
      final b = _bloqueo(unidades: [_a], posiciones: {_a.vin: 4}, fotos: [_foto(vistaId: 'vista-1')]);
      expect(b, isA<FaltanFotosDeAcomodacion>());
    });

    test('una unidad que no sale no pide posición', () {
      expect(_bloqueo(unidades: [_a, _noSale], posiciones: {_a.vin: 4}, fotos: [_foto()]), isNull);
    });

    test('un informe que no es de acomodación no pide nada de esto', () {
      expect(bloqueoDeCierre(hayUbicacion: true, fotos: const [], hallazgos: const []), isNull);
    });
  });

  group('el job de la acomodación', () {
    test('guarda el viaje y las posiciones, y los informes viejos se leen sin ellos', () {
      final job = informe(viajeId: 'v1', posiciones: {_a.vin: 4}, posicionesPrevias: {_a.vin: 3});
      final de = InformeJob.fromJson(job.toJson());
      expect(de.viajeId, 'v1');
      expect(de.posiciones, {_a.vin: 4});
      expect(de.posicionesPrevias, {_a.vin: 3});
      expect(de.posicionesEnviadas, isFalse);

      final viejo = job.toJson()
        ..remove('viaje_id')
        ..remove('posiciones')
        ..remove('posiciones_previas')
        ..remove('posiciones_enviadas');
      final leido = InformeJob.fromJson(viejo);
      expect(leido.viajeId, isNull);
      expect(leido.posiciones, isEmpty);
      expect(leido.posicionesEnviadas, isFalse);
    });

    test('el progreso se conserva al marcar las posiciones como enviadas', () {
      final job = informe(viajeId: 'v1', posiciones: {_a.vin: 4}).conProgreso(posicionesEnviadas: true);
      expect(job.posicionesEnviadas, isTrue);
      expect(job.conIntento(intentos: 1, error: null, fallido: false).posiciones, {_a.vin: 4});
    });
  });
}
