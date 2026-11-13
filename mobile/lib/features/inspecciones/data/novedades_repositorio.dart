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

  /// Los daños del viaje que nadie ha calificado, del más antiguo al más reciente.
  ///
  /// Se piden como las novedades del conductor sin revisar, porque **el contrato no tiene un filtro de «sin
  /// nivel»**: `nivel` solo admite v1_plus, v1, v2 y v3. Es el mejor proxy que hay y cubre lo que la app produce
  /// hoy —quien inspecciona siempre pone nivel—, pero no es lo mismo: un hallazgo histórico puede tener el nivel
  /// vacío con origen «inspector» y entonces no sale en esta lista. El arreglo de fondo es un filtro de verdad
  /// en el servidor (S7-HU-03.1).
  ///
  /// Por viaje entero y al revés que la ficha: lo viejo primero, que es lo que lleva más tiempo esperando. El
  /// servidor ya recorta por quién pregunta: a un inspector que no es del viaje le responde la lista vacía.
  Future<List<NovedadPendiente>> sinCalificarDelViaje(String viajeId) async {
    final r = await _api.get('/api/hallazgos', query: {
      'viaje_id': viajeId,
      'origen': 'conductor',
      'revisado': 'false',
      'orden': 'antiguos',
      'tamano': '200',
    });
    final items = (r as Map<String, dynamic>)['items'] as List<dynamic>? ?? const [];
    return [for (final h in items) NovedadPendiente.fromJson(h as Map<String, dynamic>)];
  }

  /// Completa la novedad: su zona, su clase, su nivel y el tamaño ya corregido.
  ///
  /// Las claves de la zona, la clase, el nivel, el tamaño y la nota van presentes aunque sean nulas, que es lo
  /// que el contrato exige; las dos de preexistencia solo cuando quien revisa confirma que el daño ya venía de
  /// antes. La zona provisional ya no existe: con el dibujo v6 el frente y las ruedas son cuadrantes (3.0.0).
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
      'clase_dano': clase,
      'nivel_severidad': nivel,
      'tamano_dano': tamano,
      'nota': nota,
      'es_preexistente': esPreexistente,
      'hallazgo_origen_id': ?hallazgoOrigenId,
    });
  }
}
