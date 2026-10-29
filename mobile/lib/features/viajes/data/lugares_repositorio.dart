import '../../../core/dispositivo/servicios.dart';
import '../../../core/http/cliente_api.dart';
import '../domain/ubicacion_lugar.dart';

/// La ubicación de los lugares donde se carga y se entrega. Se consulta con señal: el estado lo dice el servidor.
class LugaresRepositorio {
  LugaresRepositorio(this._api);

  final ClienteApi _api;

  /// El estado de cada lugar por su id. Lo que no viene en el catálogo no tiene entrada.
  Future<Map<String, EstadoUbicacionLugar>> estados() async {
    final r = await _api.get('/api/catalogos/lugares') as Map<String, dynamic>;
    return {
      for (final l in r['items'] as List<dynamic>? ?? const [])
        if ((l as Map<String, dynamic>)['id'] is String)
          l['id'] as String: EstadoUbicacionLugar.desde(l['estado_ubicacion'] as String?),
    };
  }

  /// «Fijar ubicación aquí»: deja la posición del teléfono como la del lugar, pendiente de confirmar. Responde 409
  /// si el lugar ya tenía ubicación.
  Future<EstadoUbicacionLugar> fijar(String lugarId, Posicion posicion) async {
    final r = await _api.post(
      '/api/lugares/${Uri.encodeComponent(lugarId)}/ubicacion',
      cuerpo: {
        'lat': posicion.lat,
        'lng': posicion.lng,
        if (posicion.precision != null) 'precision': posicion.precision,
      },
    ) as Map<String, dynamic>;
    return EstadoUbicacionLugar.desde(r['estado_ubicacion'] as String?);
  }
}
