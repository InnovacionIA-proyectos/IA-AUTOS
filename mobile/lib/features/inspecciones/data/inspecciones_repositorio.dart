import 'dart:convert';

import '../../../core/http/cliente_api.dart';
import '../../../core/http/listados.dart';
import '../domain/inspeccion_resumen.dart';
import '../../../core/db/cache_lecturas.dart';

/// Listado de inspecciones con caché local: sin señal se muestra lo último sincronizado.
class InspeccionesRepositorio {
  InspeccionesRepositorio(this._api, this._cache);

  final ClienteApi _api;
  final CacheLecturasRepo _cache;

  /// La caché es por usuario: en un dispositivo compartido nadie ve las inspecciones de otra persona.
  static String _clave(String usuarioId) => 'inspecciones.$usuarioId';

  Future<List<InspeccionResumen>?> leerCache(String usuarioId) async {
    final crudo = await _cache.leer(_clave(usuarioId));
    if (crudo == null) return null;
    try {
      return [for (final i in jsonDecode(crudo) as List<dynamic>) InspeccionResumen.fromJson(i as Map<String, dynamic>)];
    } catch (_) {
      return null; // caché corrupta: se ignora
    }
  }

  Future<List<InspeccionResumen>> listarRemoto(String usuarioId) async {
    // `usuario_id`, en snake_case como el resto del contrato: el v2 renombró el filtro y con el nombre viejo
    // el servidor lo ignora, de modo que cada quien vería los informes de todos.
    final r = await _api.get('/api/inspecciones', query: {'usuario_id': usuarioId});
    final lista = [
      for (final i in elementosDe(r, ruta: '/api/inspecciones')) InspeccionResumen.fromJson(i as Map<String, dynamic>),
    ];
    await _cache.escribir(_clave(usuarioId), jsonEncode([for (final i in lista) i.toJson()]));
    return lista;
  }

  /// Los informes de un proceso de un viaje, para saber cómo va cada unidad.
  ///
  /// No se guarda copia: se mira mientras se trabaja el camión, con señal, y una copia vieja diría que una unidad
  /// sigue sin informe cuando ya se cerró. El filtro por proceso lo aplica el servidor.
  Future<List<InspeccionResumen>> delViaje(String viajeId, {required String proceso}) async {
    final r = await _api.get('/api/inspecciones', query: {'viaje_id': viajeId, 'proceso': proceso, 'tamano': '200'});
    return [
      for (final i in elementosDe(r, ruta: '/api/inspecciones')) InspeccionResumen.fromJson(i as Map<String, dynamic>),
    ];
  }
}
