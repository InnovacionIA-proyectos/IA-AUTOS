import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/features/custodia/domain/trazo_firma.dart';

void main() {
  test('sin trazos no hay firma', () {
    expect(TrazoFirma().aPathSvg(), isNull);
    expect(TrazoFirma().vacio, isTrue);
  });

  test('serializa como path SVG "M x y L x y" con un decimal (formato que dibuja la web)', () {
    final t = TrazoFirma()
      ..iniciar(const Offset(10, 20.26))
      ..agregar(const Offset(30.04, 40))
      ..agregar(const Offset(50, 60));

    expect(t.aPathSvg(), 'M 10.0 20.3 L 30.0 40.0 L 50.0 60.0');
  });

  test('varios trazos se concatenan, cada uno empezando con M', () {
    final t = TrazoFirma()
      ..iniciar(const Offset(0, 0))
      ..agregar(const Offset(5, 5))
      ..iniciar(const Offset(20, 20))
      ..agregar(const Offset(25, 25));

    expect(t.aPathSvg(), 'M 0.0 0.0 L 5.0 5.0 M 20.0 20.0 L 25.0 25.0');
  });

  test('un toque (un solo punto) se conserva como un segmento mínimo para que se vea', () {
    final t = TrazoFirma()..iniciar(const Offset(10, 10));
    expect(t.aPathSvg(), 'M 10.0 10.0 L 10.1 10.0');
  });

  test('agregar sin haber iniciado abre un trazo', () {
    final t = TrazoFirma()..agregar(const Offset(1, 2));
    expect(t.trazos, hasLength(1));
  });

  test('limpiar deja la firma vacía', () {
    final t = TrazoFirma()
      ..iniciar(const Offset(1, 1))
      ..limpiar();
    expect(t.vacio, isTrue);
    expect(t.aPathSvg(), isNull);
  });

  test('se puede reconstruir desde el path guardado (ida y vuelta)', () {
    const path = 'M 0.0 0.0 L 5.0 5.0 M 20.0 20.0 L 25.0 25.0 L 30.5 31.5';
    final t = TrazoFirma.desdePath(path);

    expect(t.trazos.map((s) => s.length), [2, 3]);
    expect(t.aPathSvg(), path);
  });

  test('desdePath tolera vacío, null y basura', () {
    expect(TrazoFirma.desdePath(null).vacio, isTrue);
    expect(TrazoFirma.desdePath('  ').vacio, isTrue);
    expect(TrazoFirma.desdePath('firma-antigua').vacio, isTrue);
  });

  test('el path empieza con M, que es lo que la web exige para dibujarlo', () {
    final t = TrazoFirma()
      ..iniciar(const Offset(3, 4))
      ..agregar(const Offset(5, 6));
    expect(t.aPathSvg()!.startsWith('M'), isTrue);
  });
}
