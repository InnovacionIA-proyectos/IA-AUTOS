/// URL absoluta de una imagen que sirve la API. Las evidencias llegan con ruta relativa y firmada.
///
/// Null si no hay nada que mostrar, para que quien la use decida qué pinta en su lugar.
String? resolverUrlImagen(String apiUrl, String? relativa) {
  if (relativa == null || relativa.isEmpty) return null;
  return relativa.startsWith('http') ? relativa : '$apiUrl$relativa';
}
