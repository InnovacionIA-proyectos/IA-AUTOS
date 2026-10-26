import 'dart:convert';

import '../../../core/db/cache_lecturas.dart';
import '../../../core/http/cliente_api.dart';
import '../domain/catalogos.dart';

/// Descarga y guarda los catálogos maestros.
///
/// Una sola fila en la caché de lecturas (`{version, usuarioId, catalogos}`), no una tabla propia: el esquema de
/// Drift se queda en la versión 1 y la cola de envíos —el único dato de la app que no se puede recuperar— no pasa
/// por ninguna migración.
///
/// La frescura la decide el servidor: se guarda la versión y se revalida con `If-None-Match`, de modo que la
/// comprobación habitual cuesta un `304` sin cuerpo.
class CatalogosRepositorio {
  CatalogosRepositorio(this._api, this._cache);

  /// Clave única y global: la API sirve la misma carga a todo usuario autenticado, así que una sola copia por
  /// dispositivo. Lo que no se comparte entre usuarios lo filtra [Catalogos.desdeAlmacen].
  static const clave = 'catalogos';

  static const _ruta = '/api/catalogos';

  final ClienteApi _api;
  final CacheLecturasRepo _cache;

  /// Última copia guardada, o null si no hay ninguna o quedó ilegible. Una caché corrupta se trata como ausente:
  /// nunca se sirve a medias.
  Future<Catalogos?> leerCache(String usuarioId) async {
    final crudo = await _cache.leer(clave);
    if (crudo == null) return null;
    try {
      return Catalogos.desdeAlmacen(jsonDecode(crudo) as Map<String, dynamic>, usuarioId: usuarioId);
    } catch (_) {
      return null; // caché corrupta: se ignora y se vuelve a descargar
    }
  }

  /// Revalida contra la API y devuelve la copia vigente.
  ///
  /// Con una copia propia al día responde `304` y se conserva lo guardado. Se descarga completo cuando no hay
  /// copia, cuando quedó ilegible o cuando la descargó otra persona (así el usuario actual obtiene la suya y la
  /// fila queda sellada a su nombre).
  Future<Catalogos> sincronizar(String usuarioId) async {
    final local = await leerCache(usuarioId);
    final propia = local != null && local.usuarioId == usuarioId;

    var respuesta = await _pedir(propia ? local.etag : null);
    if (respuesta.estado == 304) {
      if (propia) return local;
      // 304 sin una copia utilizable que conservar: se repite sin la condición para no quedar pidiendo
      // eternamente algo que el servidor nunca va a mandar.
      respuesta = await _pedir(null);
    }

    // Se interpreta ANTES de guardar: si el cuerpo no sirve, la copia anterior queda intacta.
    final catalogos = Catalogos.desdeRespuesta(
      _cuerpo(respuesta),
      usuarioId: usuarioId,
      etag: respuesta.encabezado('ETag'),
    );
    await _cache.escribir(clave, jsonEncode(catalogos.toJson()));
    return catalogos;
  }

  /// Se devuelve el validador tal como llego en el `ETag`, sin reconstruirlo: si un proxy lo reescribe (por
  /// ejemplo anadiendo `-gzip`), uno armado a mano nunca casaria y se bajaria el catalogo completo cada vez.
  Future<RespuestaApi> _pedir(String? etag) => _api.enviar(
        'GET',
        _ruta,
        encabezados: etag == null || etag.isEmpty ? null : {'If-None-Match': etag},
        estadosExtra: const {304},
      );

  /// No basta con que el cuerpo sea un objeto JSON: un portal cautivo o un balanceador pueden responder `200` con
  /// un sobre de error, y guardar eso dejaría al inspector sin catálogos hasta recuperar la API de verdad. Se exige
  /// que traiga catálogos con contenido; cualquier otra cosa se rechaza y la copia anterior queda intacta.
  Map<String, dynamic> _cuerpo(RespuestaApi respuesta) {
    final datos = respuesta.datos;
    final catalogos = datos is Map<String, dynamic> ? datos['catalogos'] : null;
    // No basta con que haya claves: un backend a medio desplegar o una base recién migrada puede devolver todos
    // los catálogos en cero. Guardar eso pisa la copia buena y, como la copia nueva trae su ETag, la siguiente
    // revalidación responde 304 y la deja clavada hasta que cambie la versión en el servidor.
    if (catalogos is! Map || !catalogos.values.any((v) => v is List && v.isNotEmpty)) {
      throw const FormatException('La respuesta de los catálogos no tiene la forma esperada');
    }
    return datos! as Map<String, dynamic>;
  }
}
