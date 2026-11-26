import '../../viajes/domain/viaje.dart';
import 'foto_local.dart';

const posicionMinima = 1;

/// Cuántos puestos tiene una niñera cuando el viaje no dice otra cosa. Son los viajes de antes de que el viaje
/// guardara su capacidad: se siguen comportando como siempre (S7-HU-05 CA-6).
const capacidadPorDefecto = 7;

/// Mínimo de fotos de la acomodación mientras no se fije otro al compilar (ver `Entorno`).
const acomodacionFotosMinimasPorDefecto = 1;

/// Un puesto de la niñera, tal como lo manda el servidor: su número y cómo se llama en la Orden de Cargue.
///
/// La app **no** lleva escrita ninguna disposición. Antes tenía el esquema de una niñera de siete puestos fijo
/// en el código, y con una de nueve dibujaba otra cosa de la que se estaba cargando.
class PuestoNinera {
  const PuestoNinera({required this.numero, required this.etiqueta});

  factory PuestoNinera.fromJson(Map<String, dynamic> json) => PuestoNinera(
        numero: (json['numero'] as num).toInt(),
        etiqueta: json['etiqueta'] as String? ?? '${json['numero']}',
      );

  final int numero;

  /// Cómo se nombra ese puesto («A1-4»). Para quien carga, el número solo no basta.
  final String etiqueta;

  Map<String, dynamic> toJson() => {'numero': numero, 'etiqueta': etiqueta};
}

/// Cuántos puestos tiene la niñera de este viaje.
int capacidadDe(Viaje? viaje) => viaje?.capacidad ?? capacidadPorDefecto;

/// Los puestos que hay que dibujar: los que manda el viaje y, si no manda ninguno, tantos como su capacidad.
///
/// El respaldo no es un esquema: es la misma lista numerada de 1 a la capacidad. Dibujar de más o de menos sería
/// peor que dibujar sin forma.
List<PuestoNinera> puestosDe(Viaje? viaje) {
  final delServidor = viaje?.disposicion ?? const <PuestoNinera>[];
  if (delServidor.isNotEmpty) return delServidor;
  return [for (var n = posicionMinima; n <= capacidadDe(viaje); n++) PuestoNinera(numero: n, etiqueta: '$n')];
}

/// La segunda inspección del cargue es la salida de esa etapa: en vez de las vistas de estado documenta cómo
/// quedaron acomodadas las unidades en la niñera.
bool esSegundaInspeccionDelCargue({required String proceso, required String momento}) =>
    proceso == 'cargue' && momento == 'salida';

/// Las fotos de la acomodación son las que no cubren una vista ni un hallazgo.
List<FotoLocal> fotosDeAcomodacion(List<FotoLocal> fotos) => [...fotos.where((f) => f.vistaId == null)];

/// Las unidades que deben tener lugar: las que siguen saliendo en el viaje.
List<ViajeVehiculo> unidadesActivas(Viaje viaje) => [...viaje.vehiculos.where((v) => v.sale)];

/// Las posiciones que el viaje ya tiene registradas, por VIN.
Map<String, int> posicionesDelViaje(Viaje viaje) => {
      for (final v in unidadesActivas(viaje))
        if (v.posicion != null) v.vin: v.posicion!,
    };

/// El VIN que ocupa [posicion], o null si está libre. [except] es la unidad que la está eligiendo.
String? ocupante(Map<String, int> posiciones, int posicion, {String? except}) {
  for (final e in posiciones.entries) {
    if (e.value == posicion && e.key != except) return e.key;
  }
  return null;
}

/// Los VIN de las unidades activas que todavía no tienen posición, en orden.
List<String> vinesSinPosicion(List<ViajeVehiculo> unidades, Map<String, int> posiciones) =>
    [for (final u in unidades) if (u.sale && !posiciones.containsKey(u.vin)) u.vin]..sort();
