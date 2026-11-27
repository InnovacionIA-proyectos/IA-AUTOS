import '../../../core/dispositivo/servicios.dart';
import '../../../core/http/cliente_api.dart';
import '../../../core/http/listados.dart';
import '../domain/pendiente.dart';
import '../domain/viaje.dart';

/// Reclamar y liberar el camión que el inspector va a revisar.
///
/// Todo va directo a la API, sin cola ni copia: quien decide quién se queda con el camión es el servidor, y quien
/// reclama tiene que saber ahí mismo si lo consiguió.
class ReclamosRepositorio {
  ReclamosRepositorio(this._api);

  final ClienteApi _api;

  /// Los camiones pendientes de los puntos del inspector. Un inspector sin puntos recibe una lista vacía.
  Future<List<Pendiente>> pendientes() async {
    final r = await _api.get('/api/viajes/pendientes');
    return [for (final p in elementosDe(r, ruta: '/api/viajes/pendientes')) Pendiente.fromJson(p as Map<String, dynamic>)];
  }

  /// «Tomar». El servidor responde 409 si ya lo tomó otro («Ya fue tomado por otro inspector») o si el
  /// cargue no es de hoy; el error se propaga tal cual para mostrarlo.
  ///
  /// La entrega se toma por parada (S8-HU-04.2 CA-10): [lugarId] es obligatorio con `entrega` y el cargue no lo lleva.
  ///
  /// [posicion] es el GPS del momento en que se toma la entrega (S8-HU-09.2 CA-4): `lat` y `lng` juntas y su
  /// `precision`. Con ella el servidor compara la distancia con el rango del lugar y, si queda lejos, avisa al
  /// coordinador; nunca bloquea. Sin ella el reclamo sale igual, sin esos campos: el GPS no detiene a nadie.
  Future<void> reclamar(String viajeId, String proceso, {String? lugarId, Posicion? posicion}) async {
    await _api.post(
      '/api/viajes/${Uri.encodeComponent(viajeId)}/reclamos',
      cuerpo: {
        'proceso': proceso,
        'lugar_id': ?lugarId,
        if (posicion != null) ...{
          'lat': posicion.lat,
          'lng': posicion.lng,
          'precision': ?posicion.precision,
        },
      },
    );
  }

  /// Suelta el reclamo propio: el camión vuelve a estar pendiente y lo ya capturado conserva su autor. La entrega se
  /// libera por parada, con `?lugar_id=`.
  Future<void> liberar(String viajeId, String proceso, {String? lugarId}) async {
    await _api.enviar(
      'DELETE',
      '/api/viajes/${Uri.encodeComponent(viajeId)}/reclamos/${Uri.encodeComponent(proceso)}',
      query: lugarId == null ? null : {'lugar_id': lugarId},
    );
  }

  /// El viaje de un pendiente, con sus unidades. No sale de la copia de «Mis viajes»: un camión que se acaba de
  /// reclamar todavía no está ahí.
  Future<Viaje> viaje(String viajeId) async {
    final r = await _api.get('/api/viajes/${Uri.encodeComponent(viajeId)}');
    return Viaje.fromJson(r as Map<String, dynamic>);
  }
}
