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

  group('pinturaDeRegion', () {
    // Lo que se vio en el celular: al tocar una zona azul «por revisar» para abrir su detalle, el amarillo de la
    // selección tapaba el color del nivel y parecía que el daño hubiera cambiado de estado.
    test('una zona con daño conserva el color de su nivel aunque esté elegida', () {
      final elegida = pinturaDeRegion(seleccionada: true, tieneDano: true, nivel: null, cuadricula: true);

      expect(elegida.fondo, ColoresDibujo.rellenoPorRevisar, reason: 'el relleno sigue diciendo el nivel');
      expect(elegida.linea, ColoresDibujo.seleccion, reason: 'lo que marca la elección es el borde');
    });

    test('una zona sin daño elegida sí va en amarillo', () {
      final elegida = pinturaDeRegion(seleccionada: true, tieneDano: false, nivel: null, cuadricula: true);

      expect(elegida.fondo, ColoresDibujo.rellenoSeleccion);
    });

    test('una zona con daño sin elegir va de su nivel, borde y relleno', () {
      final v2 = pinturaDeRegion(seleccionada: false, tieneDano: true, nivel: 'v2', cuadricula: true);

      expect((v2.fondo, v2.linea), (ColoresDibujo.rellenoNivelV2, ColoresDibujo.nivelV2));
    });

    test('sin cuadrícula, una zona sin daño ni elección no se pinta', () {
      final nada = pinturaDeRegion(seleccionada: false, tieneDano: false, nivel: null, cuadricula: false);

      expect((nada.fondo, nada.linea), (null, null));
    });
  });

  /// Lo que el vehículo ya traía. La diferencia tiene que notarse sin depender del color: el criterio lo pide
  /// («por algo más que el color») y en el patio la pantalla se mira al sol.
  group('daños anteriores', () {
    test('se marcan como anteriores, fuera de la escala de severidad', () {
      final anterior = pinturaDeRegion(
        seleccionada: false,
        tieneDano: false,
        tieneDanoAnterior: true,
        nivel: null,
        cuadricula: true,
      );

      expect(anterior.anterior, isTrue, reason: 'rayado en diagonal y borde discontinuo, no solo otro color');
      expect(anterior.linea, ColoresDibujo.anterior);
      expect(anterior.linea, isNot(ColoresDibujo.dano));
      expect(anterior.linea, isNot(ColoresDibujo.porRevisar), reason: 'no es una novedad por revisar: es de otro informe');
    });

    test('un daño que se está capturando gana sobre el anterior de la misma zona', () {
      final ambos = pinturaDeRegion(
        seleccionada: false,
        tieneDano: true,
        tieneDanoAnterior: true,
        nivel: 'v2',
        cuadricula: true,
      );

      expect(ambos.anterior, isFalse, reason: 'lo que se registra ahora es lo que quien inspecciona tiene que ver');
      expect(ambos.linea, ColoresDibujo.nivelV2);
    });

    test('elegir una zona anterior cambia el borde, pero sigue rayada', () {
      final elegida = pinturaDeRegion(
        seleccionada: true,
        tieneDano: false,
        tieneDanoAnterior: true,
        nivel: null,
        cuadricula: true,
      );

      expect(elegida.linea, ColoresDibujo.seleccion);
      expect(elegida.anterior, isTrue, reason: 'elegirla no la convierte en un daño de este informe');
    });

    test('una zona sin nada sigue siendo cuadrícula', () {
      final sin = pinturaDeRegion(seleccionada: false, tieneDano: false, nivel: null, cuadricula: true);

      expect(sin.anterior, isFalse);
      expect(sin.linea, ColoresDibujo.cuadricula);
    });
  });
}
