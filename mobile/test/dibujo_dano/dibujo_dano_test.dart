import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/features/dibujo_dano/domain/dibujo_dano.dart';
import 'package:traza_autos/features/dibujo_dano/domain/geometria.dart';
import 'package:traza_autos/features/dibujo_dano/domain/toque.dart';

import '../support/dobles_dibujo.dart';

/// Lo que más riesgo tiene es que un toque caiga en la zona equivocada o que una vista no coincida con otra: estas
/// pruebas corren sobre los datos reales de los assets, no sobre un ejemplo hecho a mano.
void main() {
  final dibujo = dibujoDeAssets();

  /// Píxeles de pantalla por píxel de referencia tan grandes que la tolerancia del borde casi desaparece: así se
  /// prueba solo «dónde cae el punto».
  const muyCerca = 100.0;

  group('carga de los datos', () {
    test('trae 7 paneles, 80 zonas y 251 regiones', () {
      expect(dibujo.paneles, hasLength(7));
      expect(dibujo.zonas, hasLength(80));
      expect([for (final p in dibujo.paneles) ...dibujo.regionesDe(p.codigo)], hasLength(251));
    });

    test('cada zona aparece en al menos una vista', () {
      for (final zona in dibujo.zonas) {
        expect(dibujo.panelesDondeSeVe(zona.clave), isNotEmpty, reason: zona.clave);
      }
    });

    test('el lado sale de los datos: 35 izquierdas, 35 derechas y 10 centrales', () {
      final porLado = <String?, int>{};
      for (final z in dibujo.zonas) {
        porLado[z.lado] = (porLado[z.lado] ?? 0) + 1;
      }
      expect(porLado, {LadoZona.izquierdo: 35, LadoZona.derecho: 35, null: 10});
    });

    // Decisión: ningún rango de números en el código. Si mañana Calidad numera distinto, el modelo lo acepta tal cual.
    test('el modelo no deduce el lado del número', () {
      final otro = DibujoDano.desdeJson(
        {
          'version': 99,
          'paneles': [
            {'codigo': 'superior', 'ancho_ref': 100, 'alto_ref': 100, 'imagen': 'superior.png'},
          ],
          'cuadrantes': [
            {'numero': 3, 'lado': null, 'nombre': 'Central con número bajo'},
            {'numero': 90, 'lado': 'izquierdo', 'nombre': 'Con lado y número alto'},
          ],
          'regiones': [
            {'panel': 'superior', 'cuadrante_numero': 3, 'lado': null, 'geometria': {'puntos': [[0, 0], [0.4, 0], [0.4, 0.4]]}},
            {'panel': 'superior', 'cuadrante_numero': 90, 'lado': 'izquierdo', 'geometria': {'puntos': [[0.5, 0.5], [1, 0.5], [1, 1]]}},
          ],
        },
        imagen: (_, archivo) => ImagenPanel.asset(archivo),
      );
      expect([for (final z in otro.zonas) z.clave], ['3', '90I']);
    });

    test('una región que apunta a una zona que no existe no se acepta', () {
      final json = jsonDibujoAssets();
      (json['regiones'] as List).add({
        'panel': 'superior',
        'cuadrante_numero': 7,
        'lado': null, // la 7 lleva lado: «7» sin lado no existe
        'geometria': {'puntos': [[0.1, 0.1], [0.2, 0.1], [0.2, 0.2]]},
      });
      expect(() => DibujoDano.desdeJson(json, imagen: (_, a) => ImagenPanel.asset(a)), throwsFormatException);
    });

    test('una coordenada fuera de 0..1 no se acepta', () {
      expect(() => leerPuntos({'puntos': [[0, 0], [1.2, 0], [1, 1]]}), throwsFormatException);
    });
  });

  group('la clave de una zona', () {
    test('se arma igual en todos lados', () {
      expect(claveDeZona(26, LadoZona.derecho), '26D');
      expect(claveDeZona(26, LadoZona.izquierdo), '26I');
      expect(claveDeZona(54, null), '54');
      expect(dibujo.zonaDe(26, LadoZona.derecho)!.clave, '26D');
    });

    test('el título es el nombre completo y debajo número y lado', () {
      final zona = dibujo.zonaDe(26, LadoZona.derecho)!;
      expect(zona.nombreCompleto, 'Llanta trasera derecha');
      expect(zona.descripcion, 'Cuadrante 26, lado derecho');
      expect(dibujo.zonaDe(54, null)!.descripcion, 'Cuadrante 54');
    });
  });

  group('resolver el toque', () {
    test('el punto interior de cada región devuelve su propia zona, en las 7 vistas', () {
      for (final panel in dibujo.paneles) {
        final regiones = dibujo.regionesDe(panel.codigo);
        for (final r in regiones) {
          final toque = resolverToque(regiones: regiones, punto: r.puntoEtiqueta, pantallaPorRef: muyCerca, acercado: true);
          expect(toque, isA<ToqueElegir>().having((t) => t.clave, 'clave', r.clave), reason: '${panel.codigo} ${r.clave}');
        }
      }
    });

    // La llanta viene con el rin como hueco (relleno par-impar): el anillo es la llanta y el centro es el rin.
    test('en una llanta, el anillo devuelve la llanta y el centro devuelve el rin', () {
      final regiones = dibujo.regionesDe('costado_izquierdo');
      RegionDano region(String clave) => regiones.singleWhere((r) => r.clave == clave);
      final llanta = region('6I'); // Llanta delantera izquierda
      final rin = region('34I'); // Rin delantero izquierdo

      expect(dentroDePoligono(rin.puntoEtiqueta, llanta.puntosRef), isFalse, reason: 'el centro del rin es el hueco de la llanta');
      final enRin = resolverToque(regiones: regiones, punto: rin.puntoEtiqueta, pantallaPorRef: muyCerca, acercado: true);
      final enLlanta = resolverToque(regiones: regiones, punto: llanta.puntoEtiqueta, pantallaPorRef: muyCerca, acercado: true);
      expect((enRin as ToqueElegir).clave, '34I');
      expect((enLlanta as ToqueElegir).clave, '6I');
    });

    test('un toque en el borde entre dos zonas pregunta cuál', () {
      final regiones = dibujo.regionesDe('costado_izquierdo');
      const pantallaPorRef = 1.0;
      const tolerancia = MedidasToque.tolerancia / pantallaPorRef;
      // Un vértice que esté a menos de la tolerancia de exactamente dos zonas.
      final (punto, claves) = [
        for (final r in regiones)
          for (final v in r.puntosRef)
            (v, {for (final o in regiones) if (distanciaAPoligono(v, o.puntosRef) <= tolerancia) o.clave}),
      ].firstWhere((c) => c.$2.length == 2);

      final toque = resolverToque(regiones: regiones, punto: punto, pantallaPorRef: pantallaPorRef, acercado: true);
      expect(toque, isA<ToqueAmbiguo>());
      expect((toque as ToqueAmbiguo).candidatos.toSet(), claves);
    });

    test('lejos de toda zona no elige nada', () {
      final regiones = dibujo.regionesDe('costado_izquierdo');
      final toque = resolverToque(regiones: regiones, punto: (x: 1, y: 1), pantallaPorRef: 1, acercado: true);
      expect(toque, isA<ToqueFuera>());
    });

    test('si las piezas se ven más chicas que un dedo, el primer toque acerca y no elige', () {
      final panel = dibujo.paneles.firstWhere((p) => p.codigo == 'costado_izquierdo');
      final regiones = dibujo.regionesDe(panel.codigo);
      // Un teléfono: el panel dibujado a 360 de ancho.
      final pantallaPorRef = 360 / panel.anchoRef;
      final punto = regiones.first.puntoEtiqueta;

      expect(resolverToque(regiones: regiones, punto: punto, pantallaPorRef: pantallaPorRef, acercado: false), isA<ToqueAcercar>());
      // Ya acercado, el mismo toque elige.
      expect(
        resolverToque(regiones: regiones, punto: punto, pantallaPorRef: pantallaPorRef * MedidasToque.acercamiento, acercado: true),
        isNot(isA<ToqueAcercar>()),
      );
    });
  });

  group('las vistas coinciden', () {
    test('una zona marcada se cuenta en cada pestaña donde se ve', () {
      const conDano = {'26D'};
      final dondeSeVe = {for (final p in dibujo.panelesDondeSeVe('26D')) p.codigo};
      for (final p in dibujo.paneles) {
        expect(dibujo.conDanoEn(p.codigo, conDano), dondeSeVe.contains(p.codigo) ? 1 : 0, reason: p.codigo);
      }
    });
  });
}
