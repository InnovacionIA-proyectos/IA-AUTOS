import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/features/dibujo_dano/presentation/pintor_regiones.dart';

/// El color con el que se lee un daño en el dibujo. Es la escala de severidad, no una decisión de diseño: quien
/// mira la pantalla del VIN tiene que ver de un vistazo si hay algo que rechaza el vehículo.
void main() {
  test('V1+ y V1 se leen igual: rojo, se rechaza', () {
    expect(colorDeNivel('v1_plus'), colorDeNivel('v1'));
    expect(colorDeNivel('v1_plus').linea, ColoresDibujo.dano);
  });

  test('V2 ámbar, V3 gris: ninguno es el rojo del rechazo', () {
    expect(colorDeNivel('v2').linea, ColoresDibujo.nivelV2);
    expect(colorDeNivel('v3').linea, ColoresDibujo.nivelV3);
    expect(colorDeNivel('v2').linea, isNot(ColoresDibujo.dano));
    expect(colorDeNivel('v3').linea, isNot(ColoresDibujo.dano));
  });

  test('sin nivel es azul: la novedad del conductor que nadie ha clasificado', () {
    expect(colorDeNivel(null).linea, ColoresDibujo.porRevisar);
  });

  test('un nivel que la app no conoce se trata como sin clasificar, no se pierde', () {
    expect(colorDeNivel('v9').linea, ColoresDibujo.porRevisar);
  });
}
