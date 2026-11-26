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

  /// S7-HU-05: los puestos los manda el viaje. La app ya no lleva escrito ningún esquema: con una niñera de
  /// nueve dibujaba siete, que es otra niñera.
  group('los puestos de la niñera', () {
    test('se dibujan los que manda el servidor, con su etiqueta y en su orden', () {
      final viaje = Viaje(
        id: 'v1',
        codigoMtrack: 'MTK-1',
        placa: 'ABC123',
        estado: 'en_curso',
        capacidad: 3,
        disposicion: const [
          PuestoNinera(numero: 3, etiqueta: 'A1-3'),
          PuestoNinera(numero: 1, etiqueta: 'A1-1'),
          PuestoNinera(numero: 2, etiqueta: 'A1-2'),
        ],
      );

      expect(puestosDe(viaje).map((p) => p.etiqueta), ['A1-3', 'A1-1', 'A1-2'], reason: 'el orden es el suyo');
      expect(capacidadDe(viaje), 3);
    });

    // ESC-06: los viajes de antes de que el viaje guardara su capacidad no se mueven.
    test('un viaje sin capacidad ni disposición se dibuja de siete puestos', () {
      const viaje = Viaje(id: 'v1', codigoMtrack: 'MTK-1', placa: 'ABC123', estado: 'en_curso');

      expect(capacidadDe(viaje), 7);
      expect(puestosDe(viaje).map((p) => p.numero), [1, 2, 3, 4, 5, 6, 7]);
      expect(puestosDe(viaje).map((p) => p.etiqueta), ['1', '2', '3', '4', '5', '6', '7']);
    });

    test('con capacidad pero sin disposición se numeran hasta la capacidad', () {
      const viaje = Viaje(id: 'v1', codigoMtrack: 'MTK-1', placa: 'ABC123', estado: 'en_curso', capacidad: 9);

      expect(puestosDe(viaje).map((p) => p.numero), [1, 2, 3, 4, 5, 6, 7, 8, 9]);
    });

    test('sin viaje todavía, se dibuja la niñera por defecto', () {
      expect(capacidadDe(null), 7);
      expect(puestosDe(null), hasLength(7));
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

  /// S7-HU-05 CA-2: la niñera debería fotografiarse una sola vez por viaje. **Todavía no se puede**: el servidor
  /// cuenta las fotos de la acomodación por inspección (`ContarFotosLibresAsync(inspeccion.Id)`), no por viaje,
  /// así que relajarlo aquí dejaría cerrar lo que allá vuelve con un 400 y deja el informe a medias. Estas
  /// pruebas fijan que la app pide lo mismo que el servidor, que es lo único que puede hacer hasta que cambie.
  group('la foto de la acomodación se cuenta por informe, como en el servidor', () {
    test('sin foto en este informe no deja cerrar, aunque el viaje ya tenga otras', () {
      final bloqueo = bloqueoDeCierre(
        hayUbicacion: true,
        fotos: const [],
        hallazgos: const [],
        acomodacion: Acomodacion(unidades: [_a, _b], posiciones: {_a.vin: 1, _b.vin: 2}, fotosMinimas: 1),
      );

      expect(bloqueo, isA<FaltanFotosDeAcomodacion>());
    });

    test('con la foto de este informe, cierra', () {
      final bloqueo = bloqueoDeCierre(
        hayUbicacion: true,
        fotos: [_foto()],
        hallazgos: const [],
        acomodacion: Acomodacion(unidades: [_a], posiciones: {_a.vin: 1}, fotosMinimas: 1),
      );

      expect(bloqueo, isNull);
    });

    test('la foto no tapa que a una unidad le falte el puesto', () {
      final bloqueo = bloqueoDeCierre(
        hayUbicacion: true,
        fotos: [_foto()],
        hallazgos: const [],
        acomodacion: Acomodacion(unidades: [_a, _b], posiciones: {_a.vin: 1}, fotosMinimas: 1),
      );

      expect(bloqueo, isA<FaltaPosicion>());
    });
  });

  /// La copia local tiene que conservar la niñera: sin señal, una de nueve se dibujaba de siete.
  group('el viaje guardado', () {
    test('conserva capacidad, disposición y cuántas fotos de acomodación tiene', () {
      const original = Viaje(
        id: 'v1',
        codigoMtrack: 'MTK-1',
        placa: 'ABC123',
        estado: 'en_curso',
        capacidad: 9,
        disposicion: [PuestoNinera(numero: 1, etiqueta: 'A1-1'), PuestoNinera(numero: 2, etiqueta: 'A1-2')],
        fotosAcomodacion: 2,
      );

      final leido = Viaje.fromJson(original.toJson());

      expect(leido.capacidad, 9);
      expect(leido.disposicion.map((p) => p.etiqueta), ['A1-1', 'A1-2']);
      expect(leido.fotosAcomodacion, 2);
      expect(puestosDe(leido), hasLength(2), reason: 'se dibuja la que trae, no una inventada');
    });

    test('un viaje sin nada de eso se lee igual que siempre', () {
      const original = Viaje(id: 'v1', codigoMtrack: 'MTK-1', placa: 'ABC123', estado: 'en_curso');

      final leido = Viaje.fromJson(original.toJson());

      expect(leido.capacidad, isNull);
      expect(leido.disposicion, isEmpty);
      expect(capacidadDe(leido), 7);
    });
  });
}
