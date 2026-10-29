import '../../viajes/domain/viaje.dart';
import 'foto_local.dart';

const posicionMinima = 1;
const posicionMaxima = 7;

/// Mínimo de fotos de la acomodación mientras no se fije otro al compilar (ver `Entorno`).
const acomodacionFotosMinimasPorDefecto = 1;

/// El esquema de la Orden de Cargue: la fila de arriba (A1) y la de abajo (B1), con la posición de cada lugar.
const esquemaNinera = <List<int>>[
  [4, 3, 2, 1],
  [7, 6, 5],
];

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
