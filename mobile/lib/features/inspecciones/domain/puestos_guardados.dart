/// Los puestos de la niñera que se eligieron en este teléfono y el servidor todavía no tiene (S8-HU-01.3 CA-4).
///
/// Son del **viaje**, no de un carro: se eligen desde la pantalla de la niñera o desde la acomodación de cualquier
/// carro, y la acomodación que se cierre primero los manda todos. Por eso se guardan aparte del borrador de cada
/// captura, con lo que había en el servidor al elegirlos: si al volver el servidor ya tiene otra cosa (se acomodó o
/// se cerró desde otro teléfono), ese cambio quedó viejo y no se aplica.
class PuestosGuardados {
  const PuestosGuardados({this.cambios = const {}, this.base = const {}});

  factory PuestosGuardados.fromJson(Map<String, dynamic> json) => PuestosGuardados(
        cambios: {
          for (final e in (json['cambios'] as Map<String, dynamic>? ?? const {}).entries) e.key: (e.value as num?)?.toInt(),
        },
        base: {
          for (final e in (json['base'] as Map<String, dynamic>? ?? const {}).entries) e.key: (e.value as num?)?.toInt(),
        },
      );

  /// Lo que se eligió frente a lo que tiene el servidor, y lo que el servidor tenía de cada uno de esos VIN.
  factory PuestosGuardados.entre({required Map<String, int> servidor, required Map<String, int> elegidos}) {
    final cambios = {
      for (final vin in {...servidor.keys, ...elegidos.keys})
        if (elegidos[vin] != servidor[vin]) vin: elegidos[vin],
    };
    return PuestosGuardados(cambios: cambios, base: {for (final vin in cambios.keys) vin: servidor[vin]});
  }

  /// VIN → puesto elegido, o null si se liberó un puesto que el servidor sí tenía.
  final Map<String, int?> cambios;

  /// VIN → puesto que tenía en el servidor cuando se eligió.
  final Map<String, int?> base;

  bool get vacio => cambios.isEmpty;

  /// Los puestos que quedan: los del servidor con encima lo elegido aquí que todavía vale. Nunca deja dos carros en
  /// el mismo puesto: si un cambio guardado choca con el servidor, gana el servidor.
  Map<String, int> sobre(Map<String, int> servidor) {
    final resultado = {...servidor};
    for (final e in cambios.entries) {
      if (servidor[e.key] != base[e.key]) continue;
      final puesto = e.value;
      if (puesto == null) {
        resultado.remove(e.key);
        continue;
      }
      final ocupado = resultado.entries.any((o) => o.value == puesto && o.key != e.key && !cambios.containsKey(o.key));
      if (!ocupado) resultado[e.key] = puesto;
    }
    // Lo que todavía choca (dos elegidos al mismo puesto por cambios viejos) se suelta.
    final vistos = <int>{};
    resultado.removeWhere((_, puesto) => !vistos.add(puesto));
    return resultado;
  }

  Map<String, dynamic> toJson() => {'cambios': cambios, 'base': base};
}

/// Una copia por persona y por viaje: en un teléfono compartido nadie ve ni manda lo que eligió otro.
String clavePuestos({required String usuarioId, required String viajeId}) => 'puestos-ninera:$usuarioId:$viajeId';

/// Dónde se guardan en el teléfono.
abstract interface class AlmacenPuestos {
  Future<PuestosGuardados?> leer(String clave);
  Future<void> guardar(String clave, PuestosGuardados puestos);
  Future<void> borrar(String clave);
}
