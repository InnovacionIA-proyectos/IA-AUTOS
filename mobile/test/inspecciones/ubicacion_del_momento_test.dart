import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/core/dispositivo/servicios.dart';
import 'package:traza_autos/features/viajes/presentation/acciones_conductor.dart';

import '../support/arnes.dart';

/// El sello de una foto dice dónde y con cuánta exactitud se tomó. Si esa exactitud es la de una lectura vieja,
/// el sello afirma algo que nadie comprobó — y es justo lo que da valor a la evidencia.
void main() {
  group('Posicion.delMomento', () {
    final ahora = DateTime(2026, 11, 9, 10);

    test('una lectura recién tomada es la de este momento', () {
      final pos = Posicion(6.2, -75.5, precision: 8, tomadaEn: ahora.subtract(const Duration(seconds: 2)));

      expect(pos.delMomento(ahora: ahora), isTrue);
    });

    // El caso real: bajo techo el GPS no alcanza a dar una lectura nueva y se usa la última conocida, que puede
    // ser de la entrada al patio, a cielo abierto y con 5 metros de margen.
    test('la última conocida, de hace horas, no lo es', () {
      final pos = Posicion(6.2, -75.5, precision: 5, tomadaEn: ahora.subtract(const Duration(hours: 3)));

      expect(pos.delMomento(ahora: ahora), isFalse);
    });

    test('sin hora no se puede afirmar que sea de ahora', () {
      expect(const Posicion(6.2, -75.5, precision: 5).delMomento(), isFalse);
    });
  });

  group('la ubicación del conductor', () {
    ProviderContainer vivo(Arnes arnes) {
      final contenedor = arnes.contenedor();
      addTearDown(contenedor.dispose);
      contenedor.listen(ubicacionActualProvider, (_, _) {});
      return contenedor;
    }

    test('con el GPS apagado no hay posición, y «Llegué» no se habilita', () async {
      final arnes = Arnes()..ubicacion.error = const UbicacionNoDisponible('El GPS está apagado.');

      expect(await vivo(arnes).read(ubicacionActualProvider.future), isNull);
    });

    // Lo que se veía en la carretera: el conductor encendía el GPS y el botón seguía gris, porque nadie había
    // tocado «Reintentar» — y nada en la pantalla decía que hiciera falta tocarlo.
    test('al encender el GPS se vuelve a preguntar sola', () async {
      final arnes = Arnes()..ubicacion.error = const UbicacionNoDisponible('El GPS está apagado.');
      final contenedor = vivo(arnes);
      expect(await contenedor.read(ubicacionActualProvider.future), isNull);

      arnes.ubicacion.error = null;
      arnes.ubicacion.encenderGps();
      await pumpEventQueue();

      expect(await contenedor.read(ubicacionActualProvider.future), isNotNull);
    });

    test('apagarlo no dispara una búsqueda que va a fallar', () async {
      final arnes = Arnes();
      final contenedor = vivo(arnes);
      expect(await contenedor.read(ubicacionActualProvider.future), isNotNull);

      arnes.ubicacion.error = const UbicacionNoDisponible('El GPS está apagado.');
      arnes.ubicacion.apagarGps();
      await pumpEventQueue();

      expect(await contenedor.read(ubicacionActualProvider.future), isNotNull, reason: 'la que ya tenía sigue valiendo');
    });
  });
}
