/// URL absoluta de una imagen que sirve la API. Las evidencias llegan con ruta relativa y firmada.
///
/// Detrás de la pasarela de IIS la API firma con el host que ella ve, que es el interno del clúster: inalcanzable
/// desde el teléfono y sin el prefijo `/contenedores`. Por eso una URL absoluta de `/api/uploads/` se rearma sobre
/// [apiUrl] conservando ruta y consulta. Es seguro porque la firma del servidor es `HMAC(archivo|exp)`: no incluye
/// el host (`clsFirmadorUrls.Calcular`).
///
/// Null si no hay nada que mostrar, para que quien la use decida qué pinta en su lugar.
String? resolverUrlImagen(String apiUrl, String? relativa) {
  if (relativa == null || relativa.isEmpty) return null;
  if (!relativa.startsWith('http')) return '$apiUrl$relativa';
  final uri = Uri.tryParse(relativa);
  if (uri == null) return relativa;
  final i = uri.path.indexOf(_rutaArchivos);
  // Una URL absoluta que no sea de la API se deja igual: no es nuestra y no hay nada que rearmar.
  if (i < 0) return relativa;
  return '$apiUrl${uri.path.substring(i)}${uri.hasQuery ? '?${uri.query}' : ''}';
}

const _rutaArchivos = '/api/uploads/';
