/// Coordenadas de un momento (la foto o el informe).
class Posicion {
  const Posicion(this.lat, this.lng, {this.precision, this.tomadaEn});

  final double lat;
  final double lng;

  /// Radio de error en metros que reporta el GPS. La API lo guarda con la evidencia y la app avisa cuando es
  /// grande: una foto sellada con 200 metros de margen no prueba dónde se tomó.
  final double? precision;

  /// Cuándo la obtuvo el GPS. Distingue una lectura de **ahora** de la última conocida, que puede ser de hace
  /// horas y de otro sitio: su precisión era buena allí y entonces, y sellarla como si fuera la de esta foto
  /// diría que la foto se tomó con una exactitud que nadie comprobó. Null si el servicio no lo informa.
  final DateTime? tomadaEn;

  /// Si la lectura es de hace menos de [margen], es la de este momento.
  bool delMomento({Duration margen = const Duration(seconds: 30), DateTime? ahora}) =>
      tomadaEn != null && (ahora ?? DateTime.now()).difference(tomadaEn!).abs() < margen;
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
  /// Null si no hay ninguna. Quien la use debe mirar [Posicion.delMomento] antes de darla por buena.
  Future<Posicion?> obtenerRapida();

  /// Si el GPS del teléfono está encendido, y cada vez que eso cambia.
  ///
  /// Sin esto, encender el GPS con la pantalla de captura abierta no servía de nada: el bloqueo
  /// «Esperando ubicación…» seguía puesto hasta que alguien tocara «Reintentar». Quien está en el patio con el
  /// vehículo delante no tiene por qué saber que hace falta ese toque.
  Stream<bool> get servicioActivo;

  Future<void> abrirAjustes();
}

/// Identidad del equipo, para saber desde qué celular se registró cada evidencia.
///
/// Es un identificador propio que la app genera una vez y conserva, no un número de serie ni nada del hardware:
/// sirve para rastrear un equipo que manda evidencia dudosa, sin leer datos del dispositivo.
abstract interface class ServicioDispositivo {
  Future<String> id();
}

abstract interface class ServicioFotos {
  /// Copia la foto recién tomada a un archivo persistente de la app (la cámara la deja en caché) y devuelve su ruta.
  Future<String> guardar(String rutaOrigen);
}
