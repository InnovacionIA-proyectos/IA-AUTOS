/// Código con el que la API responde 403 a todo mientras el PIN sea temporal (lo asignó o lo reinició un
/// administrador). Lo único que lo levanta es cambiar el PIN.
const codigoCambioPinRequerido = 'PIN_CHANGE_REQUIRED';

/// Código con el que la API rechaza una acción que no le corresponde a quien la pide: rol insuficiente, o un
/// informe que empezó otra persona (solo su autor o un administrador lo continúan).
const codigoProhibido = 'FORBIDDEN';

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

  /// El PIN pasó a ser temporal: la petición es correcta y volverá a servir en cuanto se cambie.
  bool get esCambioPinRequerido => status == 403 && code == codigoCambioPinRequerido;

  /// Los errores que merecen reintento: red, 401, 408, 429, 5xx y el 403 por PIN temporal.
  /// El resto (400, los demás 403, 404, 409, 422...) no se arregla reintentando.
  bool get esTransitorio =>
      status == 0 || status == 401 || status == 408 || status == 429 || status >= 500 || esCambioPinRequerido;

  @override
  String toString() => 'ErrorApi($status $code: $message)';
}

/// Mensaje legible para el usuario a partir de cualquier error.
String mensajeDeError(Object error, [String porDefecto = 'Ocurrió un error inesperado.']) {
  if (error is ErrorApi) return error.message;
  return porDefecto;
}
