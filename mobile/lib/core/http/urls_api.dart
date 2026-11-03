import 'package:flutter/painting.dart';

import '../config/entorno.dart';

/// URL absoluta de una imagen que sirve la API. Las evidencias llegan con ruta relativa y firmada.
///
/// La API arma las URL firmadas (`/api/uploads/{archivo}?exp=&firma=`) con el host que ella ve, que detrás de la
/// pasarela de IIS es el interno del cluster (inalcanzable desde el teléfono y sin el prefijo `/contenedores`). La
/// firma solo depende del archivo y del vencimiento, así que se rearma la URL sobre [apiUrl] conservando ruta y query.
///
/// Null si no hay nada que mostrar, para que quien la use decida qué pinta en su lugar.
String? resolverUrlImagen(String apiUrl, String? relativa) {
  if (relativa == null || relativa.isEmpty) return null;
  if (!relativa.startsWith('http')) return '$apiUrl$relativa';
  final uri = Uri.tryParse(relativa);
  if (uri == null) return relativa;
  final i = uri.path.indexOf(_rutaArchivos);
  if (i < 0) return relativa;
  return '$apiUrl${uri.path.substring(i)}${uri.hasQuery ? '?${uri.query}' : ''}';
}

const _rutaArchivos = '/api/uploads/';

/// Encabezados que lleva toda imagen pedida a la API: los de enrutamiento de la pasarela (`X-Forwarded-Host`), o
/// ninguno si la app llama a la API directo. Salen de la misma configuración de compilación que el [Entorno].
final Map<String, String> encabezadosImagenesApi = Entorno.desdeCompilacion().encabezadosEnrutamiento;

/// Imagen de la API para el visor a pantalla completa, con los encabezados de la pasarela.
ImageProvider imagenApi(String url) =>
    NetworkImage(url, headers: encabezadosImagenesApi.isEmpty ? null : encabezadosImagenesApi);
