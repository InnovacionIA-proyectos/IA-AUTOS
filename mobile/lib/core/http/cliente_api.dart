import 'package:dio/dio.dart';

import '../errores/error_api.dart';

typedef ProveedorToken = Future<String?> Function();

/// Cliente HTTP de la API: antepone la URL base, adjunta el JWT y convierte cualquier fallo en [ErrorApi]
/// (cuerpo uniforme `{message, code, details}` o error de red con `status == 0`).
class ClienteApi {
  ClienteApi({
    required String baseUrl,
    required this.token,
    this.alNoAutorizado,
    Dio? dio,
  }) : _dio = dio ?? Dio() {
    _dio.options
      ..baseUrl = baseUrl
      ..connectTimeout = const Duration(seconds: 15)
      ..receiveTimeout = const Duration(seconds: 30)
      ..sendTimeout = const Duration(seconds: 60)
      ..headers = {'Content-Type': 'application/json', 'Accept': 'application/json'};
  }

  final Dio _dio;

  /// Devuelve el JWT de la sesión actual (o null si no hay sesión).
  final ProveedorToken token;

  /// Se invoca cuando la API responde 401 a una petición autenticada (sesión vencida o token inválido).
  final void Function()? alNoAutorizado;

  Future<dynamic> get(String ruta, {Map<String, dynamic>? query}) => _enviar('GET', ruta, query: query);

  Future<dynamic> post(String ruta, {Object? cuerpo}) => _enviar('POST', ruta, cuerpo: cuerpo);

  Future<dynamic> _enviar(String metodo, String ruta, {Object? cuerpo, Map<String, dynamic>? query}) async {
    final jwt = await token();
    try {
      final respuesta = await _dio.request<dynamic>(
        ruta,
        data: cuerpo,
        queryParameters: query,
        options: Options(method: metodo, headers: {if (jwt != null) 'Authorization': 'Bearer $jwt'}),
      );
      return respuesta.data;
    } on DioException catch (e) {
      final error = _aErrorApi(e);
      if (error.status == 401 && jwt != null &&!ruta.startsWith('/api/auth/login')) alNoAutorizado?.call();
      throw error;
    }
  }

  ErrorApi _aErrorApi(DioException e) {
    final respuesta = e.response;
    if (respuesta == null) {
      return switch (e.type) {
        DioExceptionType.connectionTimeout ||
        DioExceptionType.sendTimeout ||
        DioExceptionType.receiveTimeout =>
          const ErrorApi.red('Tiempo de espera agotado. Revisa tu conexión.'),
        _ => const ErrorApi.red(),
      };
    }
    final cuerpo = respuesta.data;
    final status = respuesta.statusCode ?? 0;
    if (cuerpo is Map) {
      return ErrorApi(
        message: (cuerpo['message'] as String?) ?? _mensajePorEstado(status),
        code: (cuerpo['code'] as String?) ?? 'ERROR',
        status: status,
        details: cuerpo['details'],
      );
    }
    return ErrorApi(message: _mensajePorEstado(status), code: 'ERROR', status: status);
  }

  String _mensajePorEstado(int status) => switch (status) {
        401 => 'Tu sesión no es válida. Inicia sesión de nuevo.',
        403 => 'No tienes permiso para esta acción.',
        404 => 'No se encontró lo que buscabas.',
        >= 500 => 'El servidor tuvo un problema. Inténtalo de nuevo.',
        _ => 'No se pudo completar la solicitud.',
      };
}
