import 'dart:convert';

import '../../../core/http/cliente_api.dart';
import '../../../core/http/listados.dart';
import '../domain/inspeccion_resumen.dart';
import '../../../core/db/cache_lecturas.dart';

/// Una página del listado: los informes que llegaron y cuántos hay en total, para saber si queda más por pedir.
class PaginaInspecciones {
  const PaginaInspecciones({required this.items, required this.total});

  final List<InspeccionResumen> items;

  /// Lo que dice el servidor que hay con esos filtros, no lo que vino en esta página.
  final int total;
}

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

  /// Cuántos informes trae cada página. El servidor acepta hasta 200, pero una página corta llega antes por una
  /// red de patio y la lista sigue creciendo con «Cargar más»; 50 es además el valor por defecto de la ruta.
  static const tamanoPagina = 50;

  /// Una página del listado.
  ///
  /// [usuarioId] es siempre la clave de la caché, sea quien sea. [soloSuyas] decide otra cosa: si además se manda
  /// como filtro. El inspector y el conductor piden solo sus informes; el coordinador de calidad no manda filtro
  /// de autor, y entonces el servidor le responde los de su cliente, que es justo lo que viene a revisar. Pedir
  /// «solo los míos» por él le dejaba la pestaña vacía, porque él no captura ninguno.
  Future<PaginaInspecciones> listarRemoto(
    String usuarioId, {
    bool soloSuyas = true,
    String? vin,
    int pagina = 1,
  }) async {
    // `usuario_id`, en snake_case como el resto del contrato: el v2 renombró el filtro y con el nombre viejo
    // el servidor lo ignora, de modo que cada quien vería los informes de todos.
    final r = await _api.get('/api/inspecciones', query: {
      if (soloSuyas) 'usuario_id': usuarioId,
      if (vin != null && vin.isNotEmpty) 'vin': vin,
      'pagina': '$pagina',
      'tamano': '$tamanoPagina',
    });
    final lista = [
      for (final i in elementosDe(r, ruta: '/api/inspecciones')) InspeccionResumen.fromJson(i as Map<String, dynamic>),
    ];
    // Se guarda la primera página de la lista sin buscar, y nada más. Guardar la página 3 o el resultado de una
    // búsqueda dejaría, sin señal, un pedazo suelto haciéndose pasar por la lista entera. Así la copia local
    // tampoco crece con un cliente de 2.000 informes: son 50 como mucho.
    if (pagina == 1 && (vin == null || vin.isEmpty)) {
      await _cache.escribir(_clave(usuarioId), jsonEncode([for (final i in lista) i.toJson()]));
    }
    return PaginaInspecciones(items: lista, total: totalDe(r) ?? lista.length);
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
