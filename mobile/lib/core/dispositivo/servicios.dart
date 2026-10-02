/// Coordenadas de un momento (la foto o el informe).
class Posicion {
  const Posicion(this.lat, this.lng);

  final double lat;
  final double lng;
}

/// El usuario negó el permiso de ubicación (definitivamente o esta vez) o el GPS está apagado.
class UbicacionNoDisponible implements Exception {
  const UbicacionNoDisponible(this.motivo, {this.permanente = false});

  final String motivo;

  /// Si es permanente hay que ir a los ajustes del sistema; si no, se puede volver a pedir.
  final bool permanente;

  @override
  String toString() => motivo;
}

abstract interface class ServicioUbicacion {
  /// Pide permiso si hace falta y devuelve la posición actual. Lanza [UbicacionNoDisponible].
  Future<Posicion> obtenerActual();

  /// Posición para sellar una foto sin hacer esperar al usuario: la actual con tiempo corto o la última conocida.
  /// Null si no hay ninguna.
  Future<Posicion?> obtenerRapida();

  Future<void> abrirAjustes();
}

abstract interface class ServicioFotos {
  /// Copia la foto recién tomada a un archivo persistente de la app (la cámara la deja en caché) y devuelve su ruta.
  Future<String> guardar(String rutaOrigen);
}
