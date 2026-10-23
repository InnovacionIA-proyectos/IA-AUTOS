import 'package:dio/dio.dart';

import '../errores/error_api.dart';

typedef ProveedorToken = Future<String?> Function();

/// Respuesta completa de la API: estado, encabezados y cuerpo. La usan las llamadas que necesitan algo más que el
/// cuerpo: leer el `ETag` de los catálogos, o distinguir un 304 («no cambió») de una respuesta vacía.
class RespuestaApi {
  const RespuestaApi({required this.estado, required this.datos, this.encabezados = const {}});

  final int estado;
  final dynamic datos;

  /// Encabezados de la respuesta, con el nombre en minúsculas (como los entrega Dio).
  final Map<String, List<String>> encabezados;

  /// Primer valor del encabezado, sin importar mayúsculas; null si no vino.
  String? encabezado(String nombre) {
    final valores = encabezados[nombre.toLowerCase()];
    return valores == null || valores.isEmpty ? null : valores.first;
  }
}

/// Cliente HTTP de la API: antepone la URL base, adjunta el JWT y convierte cualquier fallo en [ErrorApi]
/// (cuerpo uniforme `{message, code, details}` o error de red con `status == 0`).
class ClienteApi {
  ClienteApi({
    required String baseUrl,
    required this.token,
    this.alNoAutorizado,
    this.alRequerirCambioDePin,
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

  /// Se invoca cuando la API responde 403 PIN_CHANGE_REQUIRED: un administrador reinició el PIN con la sesión ya
  /// abierta, así que desde ese momento el resto de la API está cerrada hasta cambiarlo.
  final void Function()? alRequerirCambioDePin;

  Future<dynamic> get(String ruta, {Map<String, dynamic>? query, Map<String, String>? encabezados}) async =>
      (await enviar('GET', ruta, query: query, encabezados: encabezados)).datos;

  Future<dynamic> post(String ruta, {Object? cuerpo, Map<String, String>? encabezados}) async =>
      (await enviar('POST', ruta, cuerpo: cuerpo, encabezados: encabezados)).datos;

  /// Sube un archivo como `multipart/form-data`.
  ///
  /// Multipart y no base64: base64 infla el cuerpo un tercio y obliga a tener la foto entera en memoria dos
  /// veces, que en un celular de gama baja con varias fotos en cola es justo lo que lo tumba.
  Future<dynamic> subirArchivo(String ruta, {required List<int> bytes, required String nombre, String campo = 'archivo'}) async {
    final datos = FormData.fromMap({campo: MultipartFile.fromBytes(bytes, filename: nombre)});
    return (await enviar('POST', ruta, cuerpo: datos)).datos;
  }

  /// Petición que devuelve la respuesta completa (estado, encabezados y cuerpo).
  ///
  /// [estadosExtra] son los estados fuera de 2xx que NO son un error para quien llama; por ejemplo `{304}` en una
  /// petición condicional con `If-None-Match`. Se aplica **solo a esta petición**: nunca se toca
  /// `_dio.options.validateStatus`, porque aceptar estados de forma global dejaría sin efecto la conversión a
  /// [ErrorApi] en toda la app y, con ella, el aviso de 401 y la clasificación de reintentos de la cola de envío.
  Future<RespuestaApi> enviar(
    String metodo,
    String ruta, {
    Object? cuerpo,
    Map<String, dynamic>? query,
    Map<String, String>? encabezados,
    Set<int> estadosExtra = const {},
  }) async {
    final jwt = await token();
    try {
      final respuesta = await _dio.request<dynamic>(
        ruta,
        data: cuerpo,
        queryParameters: query,
        options: Options(
          method: metodo,
          headers: {if (jwt != null) 'Authorization': 'Bearer $jwt', ...?encabezados},
          validateStatus: (estado) =>
              estado != null && ((estado >= 200 && estado < 300) || estadosExtra.contains(estado)),
        ),
      );
      return RespuestaApi(
        estado: respuesta.statusCode ?? 0,
        datos: respuesta.data,
        encabezados: respuesta.headers.map,
      );
    } on DioException catch (e) {
      final error = _aErrorApi(e);
      if (error.status == 401 && jwt != null && !ruta.startsWith('/api/auth/login')) alNoAutorizado?.call();
      if (error.esCambioPinRequerido) alRequerirCambioDePin?.call();
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
