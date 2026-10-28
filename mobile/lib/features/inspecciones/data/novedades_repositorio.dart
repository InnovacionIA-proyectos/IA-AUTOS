import '../../../core/http/cliente_api.dart';
import '../domain/novedad_pendiente.dart';

/// Las novedades del conductor pendientes de revisión, y la revisión que las completa.
///
/// Va directo a la API, sin cola: revisar es una decisión que el servidor puede rechazar (409 si la novedad no es
/// del conductor o si alguien ya la revisó) y quien la toma tiene que enterarse ahí mismo.
class NovedadesRepositorio {
  NovedadesRepositorio(this._api);

  final ClienteApi _api;

  /// Las novedades que registró el conductor en este VIN y que nadie ha revisado.
  Future<List<NovedadPendiente>> porRevisar({required String vin, String? viajeId}) async {
    final r = await _api.get('/api/hallazgos', query: {
      'vin': vin,
      'viaje_id': ?viajeId,
      'origen': 'conductor',
      'revisado': 'false',
    });
    final items = (r as Map<String, dynamic>)['items'] as List<dynamic>? ?? const [];
    return [for (final h in items) NovedadPendiente.fromJson(h as Map<String, dynamic>)];
  }

  /// Completa la novedad: su zona, su clase, su nivel y el tamaño ya corregido.
  ///
  /// Las siete primeras claves van presentes aunque sean nulas, que es lo que el contrato exige; las dos de
  /// preexistencia solo cuando quien revisa confirma que el daño ya venía de antes.
  Future<void> revisar(
    String hallazgoId, {
    required int? cuadranteNumero,
    required String? lado,
    required String clase,
    required String nivel,
    required String tamano,
    String? nota,
    bool esPreexistente = false,
    String? hallazgoOrigenId,
  }) async {
    await _api.post('/api/hallazgos/${Uri.encodeComponent(hallazgoId)}/revision', cuerpo: {
      'cuadrante_numero': cuadranteNumero,
      'lado': lado,
      'zona_provisional': null,
      'clase_dano': clase,
      'nivel_severidad': nivel,
      'tamano_dano': tamano,
      'nota': nota,
      'es_preexistente': esPreexistente,
      'hallazgo_origen_id': ?hallazgoOrigenId,
    });
  }
}
