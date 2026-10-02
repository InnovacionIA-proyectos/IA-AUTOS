import '../../sync/domain/job.dart';

/// Catálogo de partes del vehículo para marcar novedades sobre el diagrama de cuadrantes
/// (assets/diagrama-novedades.png, 704x1536).
///
/// Cada parte puede tener sub-posiciones (delantero o trasero, derecho, izquierdo o centro). Las coordenadas son
/// NORMALIZADAS (0..1) sobre la imagen y sirven para pintar el punto rojo en la zona del daño. Son las mismas que
/// usaba la app original, para que los informes nuevos y los históricos se dibujen igual.
class OpcionPosicion {
  const OpcionPosicion(this.etiqueta, this.coords);

  final String etiqueta;
  final CoordsDiagrama coords;
}

class ParteDef {
  const ParteDef(this.base, {this.posiciones = const [], this.coords});

  final String base;

  /// Si tiene posiciones, al tocar se pregunta cuál. Si no, es una parte única con [coords].
  final List<OpcionPosicion> posiciones;
  final CoordsDiagrama? coords;
}

const catalogoPartes = <ParteDef>[
  ParteDef('Fachada delantera', coords: CoordsDiagrama(0.5, 0.85)),
  ParteDef('Capot', coords: CoordsDiagrama(0.5, 0.66)),
  ParteDef('Portillon', coords: CoordsDiagrama(0.5, 0.4)),
  ParteDef('Techo', coords: CoordsDiagrama(0.5, 0.53)),
  ParteDef('Guardafango', posiciones: [
    OpcionPosicion('Delantero derecho', CoordsDiagrama(0.84, 0.66)),
    OpcionPosicion('Delantero izquierdo', CoordsDiagrama(0.16, 0.66)),
    OpcionPosicion('Trasero derecho', CoordsDiagrama(0.84, 0.42)),
    OpcionPosicion('Trasero izquierdo', CoordsDiagrama(0.16, 0.42)),
  ]),
  ParteDef('Puertas', posiciones: [
    OpcionPosicion('Delantera derecha', CoordsDiagrama(0.81, 0.58)),
    OpcionPosicion('Delantera izquierda', CoordsDiagrama(0.19, 0.58)),
    OpcionPosicion('Trasera derecha', CoordsDiagrama(0.81, 0.5)),
    OpcionPosicion('Trasera izquierda', CoordsDiagrama(0.19, 0.5)),
  ]),
  ParteDef('Vidrios puertas', posiciones: [
    OpcionPosicion('Delantero derecho', CoordsDiagrama(0.87, 0.55)),
    OpcionPosicion('Delantero izquierdo', CoordsDiagrama(0.13, 0.55)),
    OpcionPosicion('Trasero derecho', CoordsDiagrama(0.87, 0.47)),
    OpcionPosicion('Trasero izquierdo', CoordsDiagrama(0.13, 0.47)),
  ]),
  ParteDef('Llantas/rines', posiciones: [
    OpcionPosicion('Delantero derecho', CoordsDiagrama(0.75, 0.66)),
    OpcionPosicion('Delantero izquierdo', CoordsDiagrama(0.255, 0.66)),
    OpcionPosicion('Trasero derecho', CoordsDiagrama(0.75, 0.43)),
    OpcionPosicion('Trasero izquierdo', CoordsDiagrama(0.255, 0.43)),
  ]),
  ParteDef('Espejo retrovisor', posiciones: [
    OpcionPosicion('Izquierdo', CoordsDiagrama(0.35, 0.62)),
    OpcionPosicion('Centro', CoordsDiagrama(0.5, 0.59)),
    OpcionPosicion('Derecho', CoordsDiagrama(0.66, 0.62)),
  ]),
  ParteDef('Limpia brisas', posiciones: [
    OpcionPosicion('Izquierdo', CoordsDiagrama(0.44, 0.61)),
    OpcionPosicion('Derecho', CoordsDiagrama(0.56, 0.61)),
  ]),
  ParteDef('Para brisas', posiciones: [
    OpcionPosicion('Delantero', CoordsDiagrama(0.5, 0.61)),
    OpcionPosicion('Trasero', CoordsDiagrama(0.5, 0.45)),
  ]),
  ParteDef('Stop trasero', posiciones: [
    OpcionPosicion('Izquierdo', CoordsDiagrama(0.42, 0.26)),
    OpcionPosicion('Centro', CoordsDiagrama(0.5, 0.26)),
    OpcionPosicion('Derecho', CoordsDiagrama(0.58, 0.26)),
  ]),
  ParteDef('Parachoque', posiciones: [
    OpcionPosicion('Delantero', CoordsDiagrama(0.5, 0.84)),
    OpcionPosicion('Trasero', CoordsDiagrama(0.5, 0.29)),
  ]),
  ParteDef('Panel radio', coords: CoordsDiagrama(0.5, 0.57)),
  ParteDef('Antena', coords: CoordsDiagrama(0.44, 0.4)),
  ParteDef('Copas rines', coords: CoordsDiagrama(0.17, 0.55)),
  ParteDef('Gato', coords: CoordsDiagrama(0.42, 0.95)),
  ParteDef('Manivela', coords: CoordsDiagrama(0.58, 0.95)),
];

/// Etiqueta legible que se guarda en `parte_vehiculo` y se muestra en listados: "Guardafango delantero derecho".
String etiquetaParte(String base, [String? posicion]) => posicion == null ? base : '$base ${posicion.toLowerCase()}';
