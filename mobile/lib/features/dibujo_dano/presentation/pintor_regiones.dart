import 'dart:math' as math;

import 'package:flutter/foundation.dart' show setEquals;
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

  /// Un daño que el vehículo **ya traía** de una inspección anterior. Queda fuera de la escala de severidad a
  /// propósito: no es de quien captura ahora y no debe competir por la mirada con lo que sí lo es.
  static const anterior = Color(0xFF4B5A6B);
  static const rellenoAnterior = Color(0x334B5A6B);
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

/// Con qué se pinta una región: fondo, línea y grosor del trazo (en píxeles de pantalla; el pintor lo divide por
/// la escala).
///
/// Una zona **con daño** conserva el color de su nivel aunque esté elegida, y lo que marca la elección es el
/// borde: si el amarillo le tapara el relleno, parecería que el daño cambió de estado al tocarlo.
/// [tieneDanoAnterior] marca la zona como un daño que el vehículo ya traía: se raya en diagonal y su borde va
/// discontinuo. No basta con cambiarle el color —hay quien no separa el gris del rojo, y la pantalla se mira al sol
/// en el patio—, así que la diferencia es también de forma.
///
/// Un daño que se está capturando **gana** sobre uno anterior en la misma zona: lo que se registra ahora es lo que
/// quien inspecciona tiene que ver. Si resulta ser el mismo, el flujo de «ya estaba registrado» lo enlaza y sigue
/// siendo un hallazgo de este informe.
({Color? fondo, Color? linea, double grosor, bool anterior}) pinturaDeRegion({
  required bool seleccionada,
  required bool tieneDano,
  required String? nivel,
  required bool cuadricula,
  bool tieneDanoAnterior = false,
}) {
  if (tieneDano) {
    final color = colorDeNivel(nivel);
    return (
      fondo: color.relleno,
      linea: seleccionada ? ColoresDibujo.seleccion : color.linea,
      grosor: seleccionada ? 2.5 : 2.0,
      anterior: false,
    );
  }
  if (tieneDanoAnterior) {
    return (
      fondo: ColoresDibujo.rellenoAnterior,
      linea: seleccionada ? ColoresDibujo.seleccion : ColoresDibujo.anterior,
      grosor: seleccionada ? 2.5 : 1.6,
      anterior: true,
    );
  }
  if (seleccionada) {
    return (fondo: ColoresDibujo.rellenoSeleccion, linea: ColoresDibujo.seleccion, grosor: 2.5, anterior: false);
  }
  if (cuadricula) {
    return (fondo: ColoresDibujo.rellenoCuadricula, linea: ColoresDibujo.cuadricula, grosor: 1.2, anterior: false);
  }
  return (fondo: null, linea: null, grosor: 0.0, anterior: false);
}

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
    this.anteriores = const {},
  });

  final PanelDano panel;
  final List<RegionDano> regiones;
  /// Clave de la zona con daño al nivel de severidad de ese daño; el nivel va nulo si nadie lo clasificó.
  final Map<String, String?> danos;

  /// Las zonas que ya traían un daño de una inspección anterior del mismo vehículo.
  final Set<String> anteriores;
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

      final pintura = pinturaDeRegion(
        seleccionada: r.clave == seleccionada,
        tieneDano: danos.containsKey(r.clave),
        tieneDanoAnterior: anteriores.contains(r.clave),
        nivel: danos[r.clave],
        cuadricula: cuadricula,
      );
      if (pintura.fondo case final fondo?) canvas.drawPath(camino, relleno..color = fondo);
      if (pintura.anterior) _rayar(canvas, camino);
      if (pintura.linea case final linea?) {
        canvas.drawPath(
          pintura.anterior ? _discontinuo(camino) : camino,
          trazo..color = linea..strokeWidth = pintura.grosor / escala,
        );
      }
    }

    if (cuadricula) _numeros(canvas, size);
  }

  /// Rayado diagonal de una zona con daño anterior, recortado a su polígono para que no se salga de la pieza.
  /// El paso y el grosor se dividen por la escala, igual que los trazos: con el zoom la trama debe verse igual.
  void _rayar(Canvas canvas, Path camino) {
    final caja = camino.getBounds();
    if (caja.isEmpty) return;
    final raya = Paint()
      ..style = PaintingStyle.stroke
      ..color = ColoresDibujo.anterior
      ..strokeWidth = 1.1 / escala;
    final paso = 7 / escala;
    canvas.save();
    canvas.clipPath(camino);
    // Diagonales a 45 grados; se empieza una altura antes del borde izquierdo para cubrir la esquina inferior.
    for (var x = caja.left - caja.height; x < caja.right; x += paso) {
      canvas.drawLine(Offset(x, caja.bottom), Offset(x + caja.height, caja.top), raya);
    }
    canvas.restore();
  }

  /// El mismo contorno, a guiones: Flutter no tiene trazo discontinuo, así que se recorta a pedazos.
  Path _discontinuo(Path origen) {
    final guion = 6 / escala;
    final hueco = 4 / escala;
    final salida = Path();
    for (final metrica in origen.computeMetrics()) {
      var desde = 0.0;
      while (desde < metrica.length) {
        final hasta = math.min(desde + guion, metrica.length);
        salida.addPath(metrica.extractPath(desde, hasta), Offset.zero);
        desde = hasta + hueco;
      }
    }
    return salida;
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
      !setEquals(anterior.anteriores, anteriores) ||
      !_mismos(anterior.danos, danos);

  static bool _mismos(Map<String, String?> a, Map<String, String?> b) =>
      a.length == b.length && a.keys.every((k) => b.containsKey(k) && b[k] == a[k]);
}
