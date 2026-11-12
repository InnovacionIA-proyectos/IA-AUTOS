import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/features/auth/domain/sesion.dart';
import 'package:traza_autos/features/vehiculos/domain/consulta.dart';

/// S7-HU-09: quién consulta los carros de su cliente y quién va recortado a sus viajes. La línea es la misma que
/// usa el servidor, y de ella dependen la búsqueda, lo que la ficha ofrece y lo que se dice cuando no se puede ver.
void main() {
  /// La regla de la que cuelgan la búsqueda, el filtro de autor del listado de inspecciones y lo que la ficha
  /// ofrece. Es el `SoloSusViajes()` del servidor: si se desalinea, la app pide filtros que sobran u ofrece
  /// pantallas que luego responden 403.
  group('soloSusViajes', () {
    test('el inspector y el conductor van recortados a sus viajes', () {
      expect(soloSusViajes('inspector'), isTrue);
      expect(soloSusViajes('conductor'), isTrue);
    });

    test('el coordinador de calidad y los demás van por su cliente', () {
      for (final rol in ['coordinador_calidad', 'comercial', 'admin', 'super_admin']) {
        expect(soloSusViajes(rol), isFalse, reason: rol);
      }
    });

    test('sin rol no se da por bueno ningún recorte', () => expect(soloSusViajes(null), isFalse));
  });

  group('buscaEnElServidor', () {
    test('el inspector y el conductor buscan entre las unidades de sus viajes', () {
      expect(buscaEnElServidor('inspector'), isFalse);
      expect(buscaEnElServidor('conductor'), isFalse);
    });

    test('el coordinador de calidad y los demás buscan los carros de su cliente', () {
      expect(buscaEnElServidor('coordinador_calidad'), isTrue);
      expect(buscaEnElServidor('comercial'), isTrue);
      expect(buscaEnElServidor('admin'), isTrue);
      expect(buscaEnElServidor('super_admin'), isTrue);
    });

    test('sin sesión no se busca en ninguna parte', () {
      expect(buscaEnElServidor(null), isFalse);
    });
  });

  group('cuando no se puede abrir el carro', () {
    test('al coordinador se le habla de su cliente, no de un viaje que no tiene', () {
      expect(mensajeSinAcceso('coordinador_calidad'), 'Este carro no es de tu cliente.');
      expect(detalleSinAcceso('coordinador_calidad'), contains('cliente'));
    });

    test('al inspector se le habla de sus viajes', () {
      expect(mensajeSinAcceso('inspector'), 'Esta unidad no está en tus viajes.');
      expect(detalleSinAcceso('inspector'), contains('viajes'));
    });
  });
}
