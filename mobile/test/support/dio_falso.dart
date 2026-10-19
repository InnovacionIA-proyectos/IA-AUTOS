import 'dart:async';
import 'dart:convert';
import 'dart:typed_data';

import 'package:dio/dio.dart';

/// Adaptador de Dio que responde lo que se programe y registra las peticiones.
class AdaptadorFalso implements HttpClientAdapter {
  AdaptadorFalso(this.responder);

  final FutureOr<ResponseBody> Function(RequestOptions) responder;
  final List<RequestOptions> peticiones = [];

  @override
  Future<ResponseBody> fetch(RequestOptions options, Stream<Uint8List>? requestStream, Future<void>? cancelFuture) async {
    peticiones.add(options);
    return responder(options);
  }

  @override
  void close({bool force = false}) {}
}

ResponseBody json(int status, Object cuerpo) =>
    ResponseBody.fromString(jsonEncode(cuerpo), status, headers: {Headers.contentTypeHeader: ['application/json']});
