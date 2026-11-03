import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/features/vehiculos/presentation/vin_page.dart';

void main() {
  const base = 'https://servicios.test/contenedores';

  group('resolverUrlImagen', () {
    test('sin URL no hay imagen', () {
      expect(resolverUrlImagen(base, null), isNull);
      expect(resolverUrlImagen(base, ''), isNull);
    });

    test('una ruta relativa se pega a la URL base', () {
      expect(resolverUrlImagen(base, '/api/uploads/a.jpg'), '$base/api/uploads/a.jpg');
    });

    test('una URL firmada con el host interno se rearma sobre la URL base conservando la firma', () {
      expect(
        resolverUrlImagen(base, 'http://interno.sslip.io/api/uploads/f%20o.jpg?exp=123&firma=abc'),
        '$base/api/uploads/f%20o.jpg?exp=123&firma=abc',
      );
    });

    test('una URL absoluta que no es de la API se deja igual', () {
      expect(resolverUrlImagen(base, 'https://cdn.test/x.jpg'), 'https://cdn.test/x.jpg');
    });
  });
}
