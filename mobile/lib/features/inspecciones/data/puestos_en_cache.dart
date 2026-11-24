import 'dart:convert';

import '../../../core/db/cache_lecturas.dart';
import '../domain/puestos_guardados.dart';

/// Los puestos de la niñera elegidos en el teléfono, en la misma tabla clave-valor que los borradores de captura.
class PuestosEnCache implements AlmacenPuestos {
  PuestosEnCache(this._cache);

  final CacheLecturasRepo _cache;

  @override
  Future<PuestosGuardados?> leer(String clave) async {
    final texto = await _cache.leer(clave);
    if (texto == null) return null;
    try {
      return PuestosGuardados.fromJson(jsonDecode(texto) as Map<String, dynamic>);
    } catch (_) {
      return null; // una copia ilegible no impide acomodar
    }
  }

  @override
  Future<void> guardar(String clave, PuestosGuardados puestos) => _cache.escribir(clave, jsonEncode(puestos.toJson()));

  @override
  Future<void> borrar(String clave) => _cache.borrar(clave);
}
