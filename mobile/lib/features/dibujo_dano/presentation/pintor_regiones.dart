import 'dart:math' as math;

import 'package:flutter/material.dart';

import '../domain/dibujo_dano.dart';

/// Colores del prototipo. Son del dibujo, no del tema de la app: amarillo para lo elegido y rojo para lo que ya tiene
/// daño se leen igual al sol y en cualquier tema.
abstract final class ColoresDibujo {
  static const cuadricula = Color(0xFFC38E00);
  static const rellenoCuadricula = Color(0x0DF6BE00);
  static const dano = Color(0xFFD11F3A);
  static const rellenoDano = Color(0x6BD11F3A);
  static const seleccion = Color(0xFF1B2836);
  static const rellenoSeleccion = Color(0x8CF6BE00);
  static const puntoAmarillo = Color(0xFFF6BE00);
  static const numero = Color(0xFF6E5100);

  /// La escala de severidad, para leer el dibujo de un vistazo: V1+ y V1 comparten el rojo del rechazo.
  static const nivelV2 = Color(0xFFE08700);
  static const rellenoNivelV2 = Color(0x6BE08700);
  static const nivelV3 = Color(0xFF6B7280);
  static const rellenoNivelV3 = Color(0x6B6B7280);

  /// Una novedad que nadie ha clasificado todavía: la del conductor, a la espera de un inspector.
  static const porRevisar = Color(0xFF1D6FD1);
  static const rellenoPorRevisar = Color(0x6B1D6FD1);
}

/// Con qué color se pinta un daño según su nivel de severidad.
///
/// V1+ y V1 van en el mismo rojo: los dos rechazan el vehículo y quien mira necesita verlos iguales. Un nivel que
/// la app no conozca (el catálogo lo administra el portal) se pinta como sin clasificar, nunca se oculta.
({Color linea, Color relleno}) colorDeNivel(String? nivel) => switch (nivel) {
      'v1_plus' || 'v1' => (linea: ColoresDibujo.dano, relleno: ColoresDibujo.rellenoDano),
      'v2' => (linea: ColoresDibujo.nivelV2, relleno: ColoresDibujo.rellenoNivelV2),
      'v3' => (linea: ColoresDibujo.nivelV3, relleno: ColoresDibujo.rellenoNivelV3),
      _ => (linea: ColoresDibujo.porRevisar, relleno: ColoresDibujo.rellenoPorRevisar),
    };

/// Pinta las regiones de un panel encima de su imagen: la cuadrícula, las zonas con daño en rojo y la elegida en
/// amarillo, con el número de cada zona.
///
/// Va dentro del `InteractiveViewer`, así que el lienzo crece con el zoom. Los trazos se dividen por [escala] para que
/// la línea mida lo mismo en pantalla con o sin zoom (en el prototipo, `vector-effect: non-scaling-stroke`); los
/// números sí crecen, igual que allá.
class PintorRegiones extends CustomPainter {
  PintorRegiones({
    required this.panel,
    required this.regiones,
    required this.danos,
    required this.seleccionada,
    required this.escala,
    this.cuadricula = true,
  });

  final PanelDano panel;
  final List<RegionDano> regiones;
  /// Clave de la zona con daño al nivel de severidad de ese daño; el nivel va nulo si nadie lo clasificó.
  final Map<String, String?> danos;
  final String? seleccionada;
  final double escala;
  final bool cuadricula;

  @override
  void paint(Canvas canvas, Size size) {
    final trazo = Paint()
      ..style = PaintingStyle.stroke
      ..strokeJoin = StrokeJoin.round;
    final relleno = Paint()..style = PaintingStyle.fill;

    for (final r in regiones) {
      // Par-impar: el hueco de la llanta (el rin) y el del parachoques (el antiniebla) quedan sin pintar.
      final camino = Path()
        ..fillType = PathFillType.evenOdd
        ..addPolygon([for (final p in r.puntos) Offset(p.x * size.width, p.y * size.height)], true);

      final (Color? fondo, Color? linea, double grosor) = switch (r.clave) {
        final c when c == seleccionada => (ColoresDibujo.rellenoSeleccion, ColoresDibujo.seleccion, 2.5),
        final c when danos.containsKey(c) => switch (colorDeNivel(danos[c])) {
          final color => (color.relleno, color.linea, 2.0),
        },
        _ when cuadricula => (ColoresDibujo.rellenoCuadricula, ColoresDibujo.cuadricula, 1.2),
        _ => (null, null, 0.0),
      };
      if (fondo != null) canvas.drawPath(camino, relleno..color = fondo);
      if (linea != null) canvas.drawPath(camino, trazo..color = linea..strokeWidth = grosor / escala);
    }

    if (cuadricula) _numeros(canvas, size);
  }

  /// El número de cada zona en su punto interior, del tamaño que quepa (la regla del prototipo).
  void _numeros(Canvas canvas, Size size) {
    final porRef = size.width / panel.anchoRef;
    final porRefAlto = size.height / panel.altoRef;
    final base = math.max(panel.anchoRef, panel.altoRef) / 62;
    for (final r in regiones) {
      final tamano = math.min(base, math.max(base * 0.55, r.radioInterior * 1.3)) * porRef;
      final texto = '${r.zona.numero}';
      final centro = Offset(r.puntoEtiqueta.x * porRef, r.puntoEtiqueta.y * porRefAlto);
      final contorno = _texto(texto, TextStyle(
        fontSize: tamano,
        fontWeight: FontWeight.w700,
        foreground: Paint()
          ..style = PaintingStyle.stroke
          // En el prototipo el borde blanco del número crece con el zoom, como el número.
          ..strokeWidth = 3 * porRef
          ..color = Colors.white,
      ));
      final lleno = _texto(texto, TextStyle(fontSize: tamano, fontWeight: FontWeight.w700, color: ColoresDibujo.numero));
      final esquina = centro - Offset(lleno.width / 2, lleno.height / 2);
      contorno.paint(canvas, esquina);
      lleno.paint(canvas, esquina);
      contorno.dispose();
      lleno.dispose();
    }
  }

  static TextPainter _texto(String texto, TextStyle estilo) =>
      TextPainter(text: TextSpan(text: texto, style: estilo), textDirection: TextDirection.ltr)..layout();

  @override
  bool shouldRepaint(PintorRegiones anterior) =>
      anterior.panel != panel ||
      anterior.regiones != regiones ||
      anterior.seleccionada != seleccionada ||
      anterior.escala != escala ||
      anterior.cuadricula != cuadricula ||
      !_mismos(anterior.danos, danos);

  static bool _mismos(Map<String, String?> a, Map<String, String?> b) =>
      a.length == b.length && a.keys.every((k) => b.containsKey(k) && b[k] == a[k]);
}
