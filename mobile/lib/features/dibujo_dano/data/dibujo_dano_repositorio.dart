import 'dart:convert';

import 'package:flutter/services.dart';

import '../../../core/db/cache_lecturas.dart';
import '../../../core/http/cliente_api.dart';
import '../../catalogos/domain/catalogos.dart';
import '../domain/dibujo_dano.dart';
import 'dibujo_desde_catalogos.dart';

/// Dónde se guardan las imágenes de fondo que se bajaron del servidor, para dibujar sin señal.
abstract interface class AlmacenImagenesPanel {
  Future<Uint8List?> leer(String clave);

  Future<void> guardar(String clave, Uint8List bytes);
}

/// Guarda cada imagen en la caché de lecturas de Drift (en base64, una fila por imagen).
///
/// No es una tabla nueva a propósito: subir `schemaVersion` obliga a migrar la base donde vive la cola de envíos, y
/// las 7 imágenes pesan alrededor de 1,3 MB. La clave lleva la URL completa, que incluye `?v=`: cuando el portal
/// cambia una imagen la URL cambia y la imagen vieja ya no se lee.
class AlmacenImagenesEnCache implements AlmacenImagenesPanel {
  AlmacenImagenesEnCache(this._cache);

  final CacheLecturasRepo _cache;

  static String _clave(String clave) => 'dibujo-dano:imagen:$clave';

  @override
  Future<Uint8List?> leer(String clave) async {
    final texto = await _cache.leer(_clave(clave));
    if (texto == null) return null;
    try {
      return base64Decode(texto);
    } on FormatException {
      return null; // fila dañada: se trata como ausente y se vuelve a bajar
    }
  }

  @override
  Future<void> guardar(String clave, Uint8List bytes) => _cache.escribir(_clave(clave), base64Encode(bytes));
}

/// Entrega el dibujo de daños vigente.
///
/// Regla única: **el del servidor si está completo, y si no, el de los assets**. Completo quiere decir que el
/// catálogo trae todo lo que pide [dibujoDesdeCatalogos] y que las imágenes de **todos** sus paneles están en el
/// equipo (guardadas o recién bajadas). Nunca se mezclan polígonos del servidor con imágenes de los assets: los
/// polígonos solo valen sobre la imagen exacta con la que se calcularon.
///
/// Los assets son la copia de respaldo: con ellos la primera inspección funciona aunque el equipo nunca se haya
/// conectado.
class DibujoDanoRepositorio {
  DibujoDanoRepositorio({required this._assets, required this._api, required this._imagenes});

  static const rutaJson = 'assets/dibujo_dano/regiones-zonas-dano.json';
  static const carpetaImagenes = 'assets/dibujo_dano/paneles/';

  final AssetBundle _assets;
  final ClienteApi _api;
  final AlmacenImagenesPanel _imagenes;

  DibujoDano? _deAssets;

  /// Lo último que se armó desde el servidor, por versión del catálogo: los catálogos se revalidan seguido y casi
  /// siempre responden 304, así que no se decodifican las imágenes cada vez.
  ({String version, DibujoDano dibujo})? _delServidor;

  /// La copia que viene dentro de la app. Se lee una sola vez.
  Future<DibujoDano> deAssets() async {
    final ya = _deAssets;
    if (ya != null) return ya;
    // `load` y no `loadString`: con más de 50 KB `loadString` decodifica en otro isolate (`compute`), y en las
    // pruebas de widgets ese isolate no termina dentro del reloj falso. El JSON pesa unos 400 KB y decodificarlo aquí
    // tarda milisegundos.
    final datos = await _assets.load(rutaJson);
    final texto = utf8.decode(datos.buffer.asUint8List(datos.offsetInBytes, datos.lengthInBytes));
    final dibujo = DibujoDano.desdeJson(
      jsonDecode(texto) as Map<String, dynamic>,
      imagen: (_, archivo) => ImagenPanel.asset('$carpetaImagenes$archivo'),
    );
    return _deAssets = dibujo;
  }

  /// El dibujo que debe usar la pantalla con estos [catalogos].
  Future<DibujoDano> vigente(Catalogos catalogos) async {
    final cacheado = _delServidor;
    if (cacheado != null && cacheado.version == catalogos.version && catalogos.version.isNotEmpty) return cacheado.dibujo;

    final delServidor = await _desdeServidor(catalogos);
    if (delServidor != null) {
      _delServidor = (version: catalogos.version, dibujo: delServidor);
      return delServidor;
    }
    return deAssets();
  }

  Future<DibujoDano?> _desdeServidor(Catalogos catalogos) async {
    final paneles = catalogos.opciones(Catalogo.panelesDano);
    // Antes de bajar nada: si el catálogo no trae el tamaño de referencia de cada panel ni las zonas con su lado,
    // es la publicación anterior y no hay nada que hacer con ella.
    final completo = paneles.isNotEmpty &&
        catalogos.opciones(Catalogo.zonasDano).isNotEmpty &&
        paneles.every((p) => p.campo<num>('ancho_ref') != null && p.campo<num>('alto_ref') != null);
    if (!completo) return null;

    final imagenes = <String, ImagenPanel>{};
    for (final p in paneles) {
      final codigo = p.codigo;
      final url = p.campo<String>('imagen_url');
      if (codigo == null || url == null) return null;
      final bytes = await _imagenDe(url);
      if (bytes == null) return null;
      imagenes[codigo] = ImagenPanel.bytes(bytes);
    }
    return dibujoDesdeCatalogos(catalogos, imagenes);
  }

  /// La imagen guardada o, si no está, la bajada ahora. Sin señal o con error devuelve null.
  Future<Uint8List?> _imagenDe(String url) async {
    final guardada = await _imagenes.leer(url);
    if (guardada != null) return guardada;
    try {
      final bytes = await _api.descargarBytes(url);
      if (bytes.isEmpty) return null;
      await _imagenes.guardar(url, bytes);
      return bytes;
    } catch (_) {
      return null;
    }
  }
}
