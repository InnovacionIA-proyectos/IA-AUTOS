/// Error devuelto por la API (cuerpo uniforme `{error, code, message, timestamp, details}`) o de red.
///
/// `status == 0` significa que la petición no llegó al servidor (sin red, tiempo agotado, DNS...).
class ErrorApi implements Exception {
  const ErrorApi({
    required this.message,
    required this.code,
    required this.status,
    this.details,
  });

  /// Error de conectividad: no hubo respuesta del servidor.
  const ErrorApi.red([String message = 'No se pudo conectar con el servidor.'])
      : this(message: message, code: 'RED', status: 0);

  final String message;
  final String code;
  final int status;
  final Object? details;

  /// Los errores que merecen reintento: red, 401, 408, 429 y 5xx.
  /// El resto (400, 403, 404, 409, 422...) no se arregla reintentando.
  bool get esTransitorio => status == 0 || status == 401 || status == 408 || status == 429 || status >= 500;

  @override
  String toString() => 'ErrorApi($status $code: $message)';
}

/// Mensaje legible para el usuario a partir de cualquier error.
String mensajeDeError(Object error, [String porDefecto = 'Ocurrió un error inesperado.']) {
  if (error is ErrorApi) return error.message;
  return porDefecto;
}
