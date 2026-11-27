import 'dart:convert';

import '../../../core/db/cache_lecturas.dart';
import '../domain/borrador_captura.dart';

/// Los borradores de captura en la tabla clave-valor de la base del teléfono, la misma que guarda el id del equipo y
/// las imágenes del dibujo: sobrevive a cerrar la app y no pide migrar el esquema.
class BorradoresEnCache implements AlmacenBorradores {
  BorradoresEnCache(this._cache);

  final CacheLecturasRepo _cache;

  @override
  Future<BorradorCaptura?> leer(String clave) async {
    final texto = await _cache.leer(clave);
    if (texto == null) return null;
    try {
      return BorradorCaptura.fromJson(jsonDecode(texto) as Map<String, dynamic>);
    } catch (_) {
      // Un borrador ilegible (de una versión anterior de la app) no puede impedir capturar.
      return null;
    }
  }

  @override
  Future<void> guardar(String clave, BorradorCaptura borrador) => _cache.escribir(clave, jsonEncode(borrador.toJson()));

  @override
  Future<void> borrar(String clave) => _cache.borrar(clave);
}
