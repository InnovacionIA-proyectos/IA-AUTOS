import 'dart:convert';

import '../../../core/db/cache_lecturas.dart';
import '../../../core/errores/error_api.dart';
import '../../../core/http/cliente_api.dart';
import '../../../core/http/listados.dart';
import '../domain/hallazgo_previo.dart';

/// Los hallazgos previos de un VIN, guardados para que la propuesta de «ya estaba registrado» funcione en el patio.
///
/// Salen de `/api/vehiculos/{vin}/hallazgos-previos`: toda la historia del vehículo, venga del viaje que venga. La
/// ruta hermana `/api/inspecciones/{id}/hallazgos-previos` no sirve aquí, porque pide una inspección ya creada y el
/// informe se arma entero en el equipo y se crea al cerrarlo. El inventario tampoco: va recortado a los viajes
/// propios, así que lo que el vehículo traía de un viaje anterior, con otra tripulación, no aparecía.
class HallazgosPreviosRepositorio {
  HallazgosPreviosRepositorio(this._api, this._cache);

  final ClienteApi _api;
  final CacheLecturasRepo _cache;

  static String _clave(String vin) => 'hallazgos-previos:$vin';

  /// Los previos del VIN. Con señal se bajan y se guardan; sin ella se devuelve la última copia, y si nunca hubo
  /// una, la lista vacía: no proponer nada es peor que proponer, pero mucho mejor que no dejar capturar.
  ///
  /// Al conductor la API le responde 403 y se queda sin previos, que es lo correcto: no es él quien decide si un
  /// daño ya venía de antes.
  Future<List<HallazgoPrevio>> de(String vin) async {
    try {
      final r = await _api.get('/api/vehiculos/${Uri.encodeComponent(vin)}/hallazgos-previos');
      final previos = [for (final h in elementosDe(r, ruta: '/api/vehiculos/{vin}/hallazgos-previos')) HallazgoPrevio.fromApi(h as Map<String, dynamic>)];
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
