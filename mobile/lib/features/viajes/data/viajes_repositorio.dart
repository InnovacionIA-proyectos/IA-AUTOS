import 'dart:convert';

import '../../../core/db/cache_lecturas.dart';
import '../../../core/http/cliente_api.dart';
import '../domain/viaje.dart';

/// Los viajes del usuario, con caché local para poder trabajar sin señal.
class ViajesRepositorio {
  ViajesRepositorio(this._api, this._cache);

  final ClienteApi _api;
  final CacheLecturasRepo _cache;

  /// La caché es por usuario: un viaje solo lo ven su conductor y su inspector, así que en un equipo compartido
  /// nadie debe leer los de otra persona. Mismo criterio que el listado de inspecciones.
  static String clave(String usuarioId) => 'viajes.$usuarioId';

  Future<List<Viaje>?> leerCache(String usuarioId) async {
    final crudo = await _cache.leer(clave(usuarioId));
    if (crudo == null) return null;
    try {
      return [for (final v in jsonDecode(crudo) as List<dynamic>) Viaje.fromJson(v as Map<String, dynamic>)];
    } catch (_) {
      return null; // caché corrupta: se ignora
    }
  }

  /// Mis viajes desde la API (los `programado` y `en_curso`), guardando la copia.
  Future<List<Viaje>> listarRemoto(String usuarioId) async {
    final r = await _api.get('/api/viajes/mios');
    final viajes = [for (final v in r as List<dynamic>) Viaje.fromJson(v as Map<String, dynamic>)];
    await _cache.escribir(clave(usuarioId), jsonEncode([for (final v in viajes) v.toJson()]));
    return viajes;
  }

  /// Marca que una unidad no sale del viaje y devuelve el viaje ya actualizado.
  ///
  /// Va directo a la API, sin pasar por la cola: el servidor puede rechazarlo con 409 (el hallazgo no es V1+, su
  /// inspección no está cerrada, la unidad ya no salía) y quien lo decide tiene que enterarse ahí mismo, no horas
  /// después en la pantalla de envíos.
  Future<Viaje> marcarNoSale(
    String viajeId,
    String vin, {
    required String motivo,
    required String hallazgoId,
    String? vinReemplazo,
  }) async {
    final r = await _api.post(
      '/api/viajes/${Uri.encodeComponent(viajeId)}/vehiculos/${Uri.encodeComponent(vin)}/no-sale',
      // Las tres claves van presentes aunque el reemplazo sea nulo: el contrato las exige.
      cuerpo: {'motivo': motivo, 'hallazgo_id': hallazgoId, 'vin_reemplazo': vinReemplazo},
    );
    return Viaje.fromJson(r as Map<String, dynamic>);
  }

  /// Mis viajes ya terminados.
  ///
  /// No se guardan en la copia local: solo se miran con señal. Mezclarlos con los activos dejaría a quien está
  /// en el patio con viajes en la lista que ya no puede trabajar.
  Future<List<Viaje>> terminados() async {
    final r = await _api.get('/api/viajes/mios', query: {'estado': 'finalizado'});
    return [for (final v in r as List<dynamic>) Viaje.fromJson(v as Map<String, dynamic>)];
  }

  /// El viaje de una etapa, para que la captura muestre su placa, tráiler, unidad y conductor.
  ///
  /// Primero la copia guardada, que es lo que hace que funcione sin señal. Si no está ahí —la copia es vieja, o
  /// quien captura no es el conductor ni el inspector del viaje, como un administrador— se pide ese viaje suelto.
  /// Devuelve null si tampoco se puede: la pantalla lo muestra sin los datos en vez de impedir la captura.
  Future<Viaje?> deLaEtapa(String usuarioId, String viajeId) async {
    final guardados = await leerCache(usuarioId);
    for (final viaje in guardados ?? const <Viaje>[]) {
      if (viaje.id == viajeId) return viaje;
    }

    try {
      final r = await _api.get('/api/viajes/${Uri.encodeComponent(viajeId)}');
      return Viaje.fromJson(r as Map<String, dynamic>);
    } catch (_) {
      return null;
    }
  }
}
