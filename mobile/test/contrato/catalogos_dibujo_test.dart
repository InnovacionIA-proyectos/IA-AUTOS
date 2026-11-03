import 'dart:convert';
import 'dart:io';
import 'dart:typed_data';

import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/features/catalogos/domain/catalogos.dart';
import 'package:traza_autos/features/dibujo_dano/data/dibujo_desde_catalogos.dart';
import 'package:traza_autos/features/dibujo_dano/domain/dibujo_dano.dart';

/// El dibujo del servidor se arma con la respuesta **literal** de `GET /api/catalogos`, no con una forma supuesta.
///
/// Esta prueba existe por un error concreto: la app esperaba un catálogo `zonas-dano` que nunca llegó, porque el
/// backend publicó las zonas dentro de `cuadrantes`. Los dobles de prueba repetían la forma propuesta, así que todo
/// pasaba en verde mientras en el celular el dibujo seguía saliendo de los assets. El recorte de abajo es un pedazo
/// de una respuesta real (2 paneles, 2 cuadrantes y sus 4 regiones); si el backend cambia la forma, esto se cae.
void main() {
  final crudo = jsonDecode(File('test/contrato/fixtures/catalogos-dibujo.json').readAsStringSync())
      as Map<String, dynamic>;
  final delServidor = Catalogos.desdeRespuesta(crudo, usuarioId: 'u1');

  Map<String, ImagenPanel> imagenesDe(Catalogos c) => {
        for (final p in c.opciones(Catalogo.panelesDano)) p.codigo!: ImagenPanel.bytes(Uint8List(1)),
      };

  test('las zonas salen de `cuadrantes`, con una por lado y una sola en las centrales', () {
    final dibujo = dibujoDesdeCatalogos(delServidor, imagenesDe(delServidor))!;

    expect(dibujo.origen, OrigenDibujo.servidor);
    // El cuadrante 2 lleva lado y da dos zonas; el 50 es central y da una.
    expect([for (final z in dibujo.zonas) z.clave], containsAll(['2I', '2D', '50']));
    expect(dibujo.zonas, hasLength(3));
  });

  test('cada zona trae su nombre completo y su área AIAG, que es lo que lee la ficha', () {
    final dibujo = dibujoDesdeCatalogos(delServidor, imagenesDe(delServidor))!;
    final faroDerecho = dibujo.zonaDe(2, LadoZona.derecho)!;

    expect(faroDerecho.nombre, 'Faro'); // el del cuadrante, sin lado
    expect(faroDerecho.nombreCompleto, 'Faro derecho');
    expect(faroDerecho.aiagArea, '24');
    expect(dibujo.zonaDe(50, null)!.lado, isNull);
  });

  test('los paneles traen su tamaño de referencia y cada región cae en el panel que dice', () {
    final dibujo = dibujoDesdeCatalogos(delServidor, imagenesDe(delServidor))!;

    expect([for (final p in dibujo.paneles) p.codigo], ['costado_izquierdo', 'frontal']);
    expect(dibujo.paneles.first.anchoRef, 1071);
    expect(dibujo.paneles.first.altoRef, 491);
    expect([for (final r in dibujo.regionesDe('costado_izquierdo')) r.clave], ['2I']);
    expect([for (final r in dibujo.regionesDe('frontal')) r.clave], containsAll(['2I', '2D', '50']));
  });

  test('sin la imagen de un panel no se usa nada del servidor: los polígonos solo valen sobre su imagen', () {
    // Es el caso de una instalación donde el portal todavía no subió las imágenes (`imagen_url` en null).
    expect(dibujoDesdeCatalogos(delServidor, const {}), isNull);
  });
}
