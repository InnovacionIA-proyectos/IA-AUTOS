import 'dart:convert';

import '../../../core/db/cache_lecturas.dart';
import '../../../core/errores/error_api.dart';
import '../../vehiculos/data/vehiculos_repositorio.dart';
import '../domain/hallazgo_previo.dart';

/// Los hallazgos previos de un VIN, guardados para que la propuesta de «ya estaba registrado» funcione en el patio.
///
/// Salen del inventario, no de `/api/inspecciones/{id}/hallazgos-previos`: esa ruta pide una inspección ya creada y
/// aquí todavía no hay ninguna, porque el informe se arma en el equipo y se crea al cerrarlo.
class HallazgosPreviosRepositorio {
  HallazgosPreviosRepositorio(this._vehiculos, this._cache);

  final VehiculosRepositorio _vehiculos;
  final CacheLecturasRepo _cache;

  static String _clave(String vin) => 'hallazgos-previos:$vin';

  /// Los previos del VIN. Con señal se bajan y se guardan; sin ella se devuelve la última copia, y si nunca hubo
  /// una, la lista vacía: no proponer nada es peor que proponer, pero mucho mejor que no dejar capturar.
  Future<List<HallazgoPrevio>> de(String vin) async {
    try {
      final previos = hallazgosPreviosDe(await _vehiculos.inventario(vin));
      await _cache.escribir(_clave(vin), jsonEncode([for (final p in previos) p.toJson()]));
      return previos;
    } on ErrorApi {
      return _guardados(vin);
    }
  }

  Future<List<HallazgoPrevio>> _guardados(String vin) async {
    final texto = await _cache.leer(_clave(vin));
    if (texto == null) return const [];
    try {
      return [
        for (final p in jsonDecode(texto) as List<dynamic>) HallazgoPrevio.fromJson(p as Map<String, dynamic>),
      ];
    } on FormatException {
      return const []; // copia dañada: se trata como ausente
    }
  }
}
