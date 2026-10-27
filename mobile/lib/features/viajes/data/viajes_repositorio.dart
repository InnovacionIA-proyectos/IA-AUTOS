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
