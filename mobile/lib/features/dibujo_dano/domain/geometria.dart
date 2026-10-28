/// Geometría del dibujo de daños, en Dart puro para poder probarla sin Flutter.
///
/// Todas las funciones trabajan en una misma unidad, la que les pase quien llama. El dibujo usa **píxeles de la
/// imagen de referencia** (coordenada normalizada × `ancho_ref` o `alto_ref`): así una distancia mide lo mismo en
/// horizontal y en vertical, que con coordenadas normalizadas no pasa en una imagen que no es cuadrada.
library;

import 'dart:math' as math;

/// Un punto (o un vector) del plano.
typedef Punto = ({double x, double y});

/// Si [p] cae dentro del polígono, con la regla **par-impar**.
///
/// Es la misma que usa el prototipo y la misma que `PathFillType.evenOdd` al pintar: una pieza con hueco (la
/// llanta alrededor del rin, la esquina del parachoques alrededor del antiniebla) viene como un solo contorno que
/// entra y sale por un puente, y con esta regla el hueco queda fuera. Con la regla «distinto de cero» el toque en el
/// rin caería en la llanta.
bool dentroDePoligono(Punto p, List<Punto> poligono) {
  var dentro = false;
  for (var i = 0, j = poligono.length - 1; i < poligono.length; j = i++) {
    final a = poligono[i];
    final b = poligono[j];
    if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) {
      dentro = !dentro;
    }
  }
  return dentro;
}

/// Distancia de [p] al segmento [a]-[b].
double distanciaASegmento(Punto p, Punto a, Punto b) {
  final dx = b.x - a.x;
  final dy = b.y - a.y;
  final largo2 = dx * dx + dy * dy;
  final t = largo2 == 0 ? 0.0 : math.max(0.0, math.min(1.0, ((p.x - a.x) * dx + (p.y - a.y) * dy) / largo2));
  final cx = a.x + t * dx;
  final cy = a.y + t * dy;
  return math.sqrt((p.x - cx) * (p.x - cx) + (p.y - cy) * (p.y - cy));
}

/// Distancia de [p] al borde más cercano del polígono, esté dentro o fuera.
double distanciaAlBorde(Punto p, List<Punto> poligono) {
  var minima = double.infinity;
  for (var i = 0; i < poligono.length; i++) {
    final d = distanciaASegmento(p, poligono[i], poligono[(i + 1) % poligono.length]);
    if (d < minima) minima = d;
  }
  return minima;
}

/// 0 si [p] está dentro; si no, la distancia al borde. Es lo que decide la tolerancia de un toque que cae afuera.
double distanciaAPoligono(Punto p, List<Punto> poligono) =>
    dentroDePoligono(p, poligono) ? 0 : distanciaAlBorde(p, poligono);

/// El punto interior más alejado del borde, buscado en una rejilla de 15 × 15 sobre la caja del polígono, y esa
/// distancia ([radio]).
///
/// Sirve para dos cosas, igual que en el prototipo: ahí se escribe el número del cuadrante (el centro de la caja
/// puede caer en el hueco o fuera de una pieza en forma de L) y el diámetro (`2 × radio`) dice si la pieza, en
/// pantalla, es más chica que un dedo. Si ningún punto de la rejilla cae dentro, devuelve el centro de la caja con
/// radio 0.
({Punto punto, double radio}) puntoInterior(List<Punto> poligono) {
  var x0 = double.infinity, y0 = double.infinity, x1 = -double.infinity, y1 = -double.infinity;
  for (final q in poligono) {
    x0 = math.min(x0, q.x);
    y0 = math.min(y0, q.y);
    x1 = math.max(x1, q.x);
    y1 = math.max(y1, q.y);
  }
  Punto mejor = (x: (x0 + x1) / 2, y: (y0 + y1) / 2);
  var radio = 0.0;
  for (var i = 1; i < 16; i++) {
    for (var j = 1; j < 16; j++) {
      final candidato = (x: x0 + (x1 - x0) * i / 16, y: y0 + (y1 - y0) * j / 16);
      if (!dentroDePoligono(candidato, poligono)) continue;
      final d = distanciaAlBorde(candidato, poligono);
      if (d > radio) {
        radio = d;
        mejor = candidato;
      }
    }
  }
  return (punto: mejor, radio: radio);
}
