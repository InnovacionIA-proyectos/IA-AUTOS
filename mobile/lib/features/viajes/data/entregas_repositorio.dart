import '../../../core/http/cliente_api.dart';
import '../domain/entrega_punto.dart';

/// Lo que hay por entregar en cada punto de un viaje. Solo lectura: la firma va por la cola.
class EntregasRepositorio {
  EntregasRepositorio(this._api);

  final ClienteApi _api;

  /// Un punto por lugar de destino del viaje.
  Future<List<EntregaPunto>> delViaje(String viajeId) async {
    final r = await _api.get('/api/viajes/${Uri.encodeComponent(viajeId)}/entregas') as Map<String, dynamic>;
    return [for (final p in r['puntos'] as List<dynamic>? ?? const []) EntregaPunto.fromJson(p as Map<String, dynamic>)];
  }
}
