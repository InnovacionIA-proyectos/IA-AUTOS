import '../../../core/errores/error_api.dart';
import '../../../core/http/cliente_api.dart';
import '../domain/permiso_sin_inspector.dart';

/// El permiso de la excepción «No hay inspector» de un viaje: pedirlo y mirar cómo va.
///
/// Concederlo no está aquí a propósito: se hace desde el portal, y la app solo espera.
class PermisosRepositorio {
  PermisosRepositorio(this._api);

  final ClienteApi _api;

  String _ruta(String viajeId) => '/api/viajes/${Uri.encodeComponent(viajeId)}/permiso-sin-inspector';

  /// El permiso del viaje, o null si todavía no se ha pedido ninguno.
  ///
  /// El 404 es la respuesta normal a «este viaje no tiene solicitud», no una falla: se traduce a null para que
  /// la pantalla no tenga que leer códigos de estado. Esta consulta es también la que deja que el reloj
  /// conceda: el servidor lo resuelve al responder.
  Future<PermisoSinInspector?> delViaje(String viajeId) async {
    try {
      return PermisoSinInspector.fromJson(await _api.get(_ruta(viajeId)) as Map<String, dynamic>);
    } on ErrorApi catch (e) {
      if (e.status == 404) return null;
      rethrow;
    }
  }

  /// Pide el permiso con el motivo, el momento y el lugar en que se pidió.
  ///
  /// Si el viaje ya tenía solicitud, el servidor devuelve esa misma: pedir dos veces no abre dos.
  Future<PermisoSinInspector> solicitar(
    String viajeId, {
    required String motivo,
    required DateTime fecha,
    String? motivoTexto,
    double? lat,
    double? lng,
  }) async {
    final r = await _api.post(_ruta(viajeId), cuerpo: {
      'motivo_sin_inspector': motivo,
      'motivo_sin_inspector_texto': motivoTexto,
      'fecha': fecha.toUtc().toIso8601String(),
      'lat': lat,
      'lng': lng,
    });
    return PermisoSinInspector.fromJson(r as Map<String, dynamic>);
  }
}
