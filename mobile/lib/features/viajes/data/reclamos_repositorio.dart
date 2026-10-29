import '../../../core/http/cliente_api.dart';
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
    return [for (final p in r as List<dynamic>) Pendiente.fromJson(p as Map<String, dynamic>)];
  }

  /// «Revisar este». El servidor responde 409 si ya lo tomó otro («Ya fue tomado por otro inspector») o si el
  /// cargue no es de hoy; el error se propaga tal cual para mostrarlo.
  Future<void> reclamar(String viajeId, String proceso) async {
    await _api.post('/api/viajes/${Uri.encodeComponent(viajeId)}/reclamos', cuerpo: {'proceso': proceso});
  }

  /// Suelta el reclamo propio: el camión vuelve a estar pendiente y lo ya capturado conserva su autor.
  Future<void> liberar(String viajeId, String proceso) async {
    await _api.enviar('DELETE', '/api/viajes/${Uri.encodeComponent(viajeId)}/reclamos/${Uri.encodeComponent(proceso)}');
  }

  /// El viaje de un pendiente, con sus unidades. No sale de la copia de «Mis viajes»: un camión que se acaba de
  /// reclamar todavía no está ahí.
  Future<Viaje> viaje(String viajeId) async {
    final r = await _api.get('/api/viajes/${Uri.encodeComponent(viajeId)}');
    return Viaje.fromJson(r as Map<String, dynamic>);
  }
}
