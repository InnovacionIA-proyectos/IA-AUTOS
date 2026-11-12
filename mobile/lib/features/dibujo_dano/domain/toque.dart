/// Qué pasó con un toque sobre un panel. Es la lógica del prototipo (`tocar()` en su JavaScript), separada del
/// widget para poder probarla con los datos reales y sin pantalla.
library;

import 'dibujo_dano.dart';
import 'geometria.dart';

sealed class ResultadoToque {
  const ResultadoToque();
}

/// Las piezas se ven más chicas que un dedo: el primer toque acerca la vista alrededor de [centro] (en píxeles de
/// referencia) y no elige nada.
class ToqueAcercar extends ResultadoToque {
  const ToqueAcercar(this.centro);

  final Punto centro;
}

/// El toque cae claramente en una zona.
class ToqueElegir extends ResultadoToque {
  const ToqueElegir(this.clave);

  final String clave;
}

/// El toque cae en el borde entre dos o tres zonas: hay que preguntar cuál. Máximo 3, la primera es la más probable.
class ToqueAmbiguo extends ResultadoToque {
  const ToqueAmbiguo(this.candidatos);

  final List<String> candidatos;
}

/// El toque no cae en ninguna zona ni cerca de una.
class ToqueFuera extends ResultadoToque {
  const ToqueFuera();
}

/// Medidas en **píxeles de pantalla** (lógicos), las del prototipo. Se pasan a píxeles de referencia dividiendo por
/// la escala, para que el borde tolerado mida lo mismo con o sin zoom.
abstract final class MedidasToque {
  /// Cuánto se tolera alrededor del borde de una zona.
  static const tolerancia = 6.0;

  /// Afuera de toda zona se tolera un poco más: un dedo que cae en la línea negra del dibujo.
  static const factorAfuera = 1.5;

  /// Diámetro mínimo de una pieza para tocarla sin acercar.
  static const dedo = 34.0;

  /// Si más de esta fracción de las piezas del panel son más chicas que un dedo, el primer toque acerca.
  static const fraccionChicas = 0.3;

  /// Cuánto acerca ese primer toque.
  static const acercamiento = 2.2;

  /// Máximo de zonas que se ofrecen cuando el toque es ambiguo.
  static const maxCandidatos = 3;
}

/// Resuelve un toque sobre las [regiones] de un panel.
///
/// - [punto] es donde cayó el dedo, en píxeles de la imagen de referencia (sin zoom: el `GestureDetector` va dentro
///   del `InteractiveViewer` y ya entrega la posición en las coordenadas del dibujo).
/// - [pantallaPorRef] es cuántos píxeles de pantalla mide un píxel de referencia **con el zoom actual**
///   (ancho dibujado × escala ÷ `ancho_ref`). Con ella se pasan las medidas de [MedidasToque] a la imagen.
/// - [acercado] es si este panel ya tiene zoom: solo el primer toque acerca.
///
/// El orden de [regiones] decide cuando el punto cae dentro de dos a la vez, igual que en el prototipo.
ResultadoToque resolverToque({
  required List<RegionDano> regiones,
  required Punto punto,
  required double pantallaPorRef,
  required bool acercado,
}) {
  if (regiones.isEmpty || pantallaPorRef <= 0) return const ToqueFuera();

  if (!acercado) {
    final chicas = regiones.where((r) => r.radioInterior * 2 * pantallaPorRef < MedidasToque.dedo).length;
    if (chicas > regiones.length * MedidasToque.fraccionChicas) return ToqueAcercar(punto);
  }

  final tolerancia = MedidasToque.tolerancia / pantallaPorRef;

  final dentro = regiones.where((r) => dentroDePoligono(punto, r.puntosRef)).firstOrNull;
  if (dentro != null) {
    final vecinas = <String>{
      for (final r in regiones)
        if (r.clave != dentro.clave && distanciaAPoligono(punto, r.puntosRef) <= tolerancia) r.clave,
    };
    if (vecinas.isEmpty) return ToqueElegir(dentro.clave);
    return ToqueAmbiguo([dentro.clave, ...vecinas].take(MedidasToque.maxCandidatos).toList());
  }

  final cercanas = [
    for (final r in regiones) (clave: r.clave, distancia: distanciaAPoligono(punto, r.puntosRef)),
  ].where((c) => c.distancia <= tolerancia * MedidasToque.factorAfuera).toList()
    ..sort((a, b) => a.distancia.compareTo(b.distancia));
  final claves = <String>{for (final c in cercanas) c.clave}.toList();

  return switch (claves.length) {
    0 => const ToqueFuera(),
    1 => ToqueElegir(claves.single),
    _ => ToqueAmbiguo(claves.take(MedidasToque.maxCandidatos).toList()),
  };
}
