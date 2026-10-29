import 'dart:convert';

import '../../../core/db/cache_lecturas.dart';
import '../../../core/errores/error_api.dart';
import '../../../core/http/cliente_api.dart';
import '../domain/inventario.dart';

class VehiculosRepositorio {
  /// [cache] y [usuarioId] hacen posible mirar el inventario sin señal: con ellos, cada inventario que se baja se
  /// guarda a nombre de quien lo bajó. La copia es por usuario porque lo que ve cada rol cambia (al conductor se le
  /// oculta el nivel de los hallazgos), y en un equipo compartido nadie debe leer la de otra persona.
  VehiculosRepositorio(this._api, {this._cache, this._usuarioId});

  final ClienteApi _api;
  final CacheLecturasRepo? _cache;
  final String? Function()? _usuarioId;

  String? _clave(String vin) => switch (_usuarioId?.call()) {
        final usuario? => 'inventario.$usuario.$vin',
        null => null,
      };

  /// El inventario del VIN. Con señal se pide y se guarda; sin ella (no hubo respuesta del servidor) se devuelve la
  /// última copia, si la hay. Un error del servidor —un 403, un 404— nunca se tapa con una copia.
  Future<Inventario> inventario(String vin) async {
    try {
      final r = await _api.get('/api/vehiculos/${Uri.encodeComponent(vin)}/inventario');
      await _guardar(vin, r);
      return Inventario.fromJson(r as Map<String, dynamic>);
    } on ErrorApi catch (e) {
      if (e.status != 0) rethrow;
      final copia = await _leer(vin);
      if (copia == null) rethrow;
      return copia;
    }
  }

  Future<void> _guardar(String vin, Object? respuesta) async {
    final clave = _clave(vin);
    if (_cache == null || clave == null) return;
    try {
      await _cache.escribir(clave, jsonEncode(respuesta));
    } catch (_) {
      // Una copia que no se pudo guardar no impide mirar lo que acaba de llegar.
    }
  }

  Future<Inventario?> _leer(String vin) async {
    final clave = _clave(vin);
    if (_cache == null || clave == null) return null;
    final texto = await _cache.leer(clave);
    if (texto == null) return null;
    try {
      return Inventario.fromJson(jsonDecode(texto) as Map<String, dynamic>);
    } catch (_) {
      return null; // copia dañada: se trata como ausente
    }
  }
}
