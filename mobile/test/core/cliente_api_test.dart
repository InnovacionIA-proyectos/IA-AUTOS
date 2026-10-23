import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/core/config/entorno.dart';
import 'package:traza_autos/core/errores/error_api.dart';
import 'package:traza_autos/core/http/cliente_api.dart';

import '../support/dio_falso.dart';

ClienteApi cliente(
  AdaptadorFalso adaptador, {
  String? token = 'jwt-123',
  void Function()? alNoAutorizado,
  void Function()? alRequerirCambioDePin,
}) {
  final dio = Dio()..httpClientAdapter = adaptador;
  return ClienteApi(
    baseUrl: 'https://api.test',
    token: () async => token,
    alNoAutorizado: alNoAutorizado,
    alRequerirCambioDePin: alRequerirCambioDePin,
    dio: dio,
  );
}

void main() {
  group('ClienteApi', () {
    test('antepone la URL base, adjunta el token y devuelve el cuerpo decodificado', () async {
      final adaptador = AdaptadorFalso((_) => json(200, {'ok': true}));

      final r = await cliente(adaptador).get('/api/algo', query: {'vin': 'ABC'});

      expect(r, {'ok': true});
      expect(adaptador.peticiones.single.uri.toString(), 'https://api.test/api/algo?vin=ABC');
      expect(adaptador.peticiones.single.headers['Authorization'], 'Bearer jwt-123');
    });

    test('sin token no envía Authorization', () async {
      final adaptador = AdaptadorFalso((_) => json(200, {}));
      await cliente(adaptador, token: null).post('/api/auth/login', cuerpo: {'email': 'a'});
      expect(adaptador.peticiones.single.headers.containsKey('Authorization'), isFalse);
    });

    test('convierte el cuerpo de error uniforme en ErrorApi', () async {
      final adaptador = AdaptadorFalso((_) => json(422, {
            'error': 'Unprocessable',
            'code': 'VALIDATION_ERROR',
            'message': 'Datos inválidos',
            'details': {'fieldErrors': {'placa': ['obligatoria']}},
          }));

      final error = await cliente(adaptador).post('/api/x').then<ErrorApi?>((_) => null, onError: (Object e) => e as ErrorApi);

      expect(error!.status, 422);
      expect(error.code, 'VALIDATION_ERROR');
      expect(error.message, 'Datos inválidos');
      expect(error.esTransitorio, isFalse);
      expect((error.details as Map)['fieldErrors'], isNotNull);
    });

    test('un error 5xx sin cuerpo JSON usa un mensaje por defecto y es transitorio', () async {
      final adaptador = AdaptadorFalso((_) => ResponseBody.fromString('<html>', 502));

      final error = await cliente(adaptador).get('/api/x').then<ErrorApi?>((_) => null, onError: (Object e) => e as ErrorApi);

      expect(error!.status, 502);
      expect(error.esTransitorio, isTrue);
      expect(error.message, contains('servidor'));
    });

    test('sin respuesta (red caída) es un ErrorApi.red transitorio', () async {
      final adaptador = AdaptadorFalso((o) => throw DioException.connectionError(requestOptions: o, reason: 'sin red'));

      final error = await cliente(adaptador).get('/api/x').then<ErrorApi?>((_) => null, onError: (Object e) => e as ErrorApi);

      expect(error!.status, 0);
      expect(error.code, 'RED');
      expect(error.esTransitorio, isTrue);
    });

    test('un tiempo de espera agotado también es de red', () async {
      final adaptador = AdaptadorFalso((o) => throw DioException.receiveTimeout(timeout: const Duration(seconds: 30), requestOptions: o));

      final error = await cliente(adaptador).get('/api/x').then<ErrorApi?>((_) => null, onError: (Object e) => e as ErrorApi);

      expect(error!.status, 0);
      expect(error.message, contains('Tiempo de espera'));
    });

    test('un 401 con sesión avisa para cerrar la sesión; en el login no', () async {
      var avisos = 0;
      final adaptador = AdaptadorFalso((_) => json(401, {'code': 'UNAUTHORIZED', 'message': 'No autorizado'}));
      final c = cliente(adaptador, alNoAutorizado: () => avisos++);

      await c.get('/api/vehiculos').then((_) {}, onError: (_) {});
      expect(avisos, 1);

      await c.post('/api/auth/login', cuerpo: {}).then((_) {}, onError: (_) {});
      expect(avisos, 1);
    });

    test('un 401 sin token (aún no hay sesión) no dispara el aviso', () async {
      var avisos = 0;
      final adaptador = AdaptadorFalso((_) => json(401, {'message': 'x'}));
      await cliente(adaptador, token: null, alNoAutorizado: () => avisos++).get('/api/x').then((_) {}, onError: (_) {});
      expect(avisos, 0);
    });
  });

  group('ClienteApi.enviar (respuesta completa)', () {
    test('devuelve estado, encabezados y cuerpo', () async {
      final adaptador = AdaptadorFalso(
        (_) => ResponseBody.fromString(
          '{"version":"abc"}',
          200,
          headers: {
            Headers.contentTypeHeader: ['application/json'],
            'etag': ['"abc"'],
          },
        ),
      );

      final r = await cliente(adaptador).enviar('GET', '/api/catalogos');

      expect(r.estado, 200);
      expect(r.datos, {'version': 'abc'});
      // El nombre del encabezado no distingue mayúsculas.
      expect(r.encabezado('ETag'), '"abc"');
      expect(r.encabezado('no-vino'), isNull);
    });

    test('los encabezados propios viajan junto al token, sin pisarlo', () async {
      final adaptador = AdaptadorFalso((_) => json(200, {}));

      await cliente(adaptador).enviar('GET', '/api/catalogos', encabezados: {'If-None-Match': '"abc"'});

      final enviados = adaptador.peticiones.single.headers;
      expect(enviados['If-None-Match'], '"abc"');
      expect(enviados['Authorization'], 'Bearer jwt-123');
    });

    test('con estadosExtra {304} un 304 no es error: llega con su estado y su ETag', () async {
      final adaptador = AdaptadorFalso(
        (_) => ResponseBody.fromString('', 304, headers: {
          'etag': ['"abc"'],
        }),
      );

      final r = await cliente(adaptador).enviar(
        'GET',
        '/api/catalogos',
        encabezados: {'If-None-Match': '"abc"'},
        estadosExtra: {304},
      );

      expect(r.estado, 304);
      expect(r.encabezado('etag'), '"abc"');
    });

    test('sin estadosExtra un 304 sigue siendo ErrorApi (el comportamiento por defecto no cambia)', () async {
      final adaptador = AdaptadorFalso((_) => ResponseBody.fromString('', 304));

      final error = await cliente(adaptador)
          .enviar('GET', '/api/catalogos')
          .then<ErrorApi?>((_) => null, onError: (Object e) => e as ErrorApi);

      expect(error!.status, 304);
    });

    test('estadosExtra no afecta a los demás estados: un 401 sigue avisando y lanzando', () async {
      var avisos = 0;
      final adaptador = AdaptadorFalso((_) => json(401, {'code': 'UNAUTHORIZED', 'message': 'No autorizado'}));

      final error = await cliente(adaptador, alNoAutorizado: () => avisos++)
          .enviar('GET', '/api/catalogos', estadosExtra: {304})
          .then<ErrorApi?>((_) => null, onError: (Object e) => e as ErrorApi);

      expect(error!.status, 401);
      expect(avisos, 1);
    });

    test('un 403 PIN_CHANGE_REQUIRED avisa para mandar a cambiar el PIN; otro 403 no', () async {
      var avisos = 0;
      final pin = AdaptadorFalso((_) => json(403, {'code': codigoCambioPinRequerido, 'message': 'Cambia tu PIN'}));
      final otro = AdaptadorFalso((_) => json(403, {'code': 'FORBIDDEN', 'message': 'Sin permiso'}));

      await cliente(pin, alRequerirCambioDePin: () => avisos++).get('/api/viajes/mios').catchError((Object _) => null);
      expect(avisos, 1);

      await cliente(otro, alRequerirCambioDePin: () => avisos++).get('/api/viajes/mios').catchError((Object _) => null);
      expect(avisos, 1, reason: 'un 403 de permisos no manda a cambiar el PIN');
    });
  });

  group('clasificación transitoria', () {
    test('0, 401, 408, 429 y 5xx se reintentan; el resto no', () {
      bool t(int s) => ErrorApi(message: '', code: '', status: s).esTransitorio;
      expect([0, 401, 408, 429, 500, 503].every(t), isTrue);
      expect([400, 403, 404, 409, 422].any(t), isFalse);
    });

    // Es el único 403 que se arregla solo: el envío es correcto y vuelve a servir en cuanto se cambia el PIN.
    // Sin esto, un reinicio de PIN a mitad de camino deja el informe marcado como fallido.
    test('el 403 por PIN temporal sí se reintenta, los demás 403 no', () {
      const pin = ErrorApi(message: '', code: codigoCambioPinRequerido, status: 403);
      const otro = ErrorApi(message: '', code: 'FORBIDDEN', status: 403);
      expect(pin.esCambioPinRequerido, isTrue);
      expect(pin.esTransitorio, isTrue);
      expect(otro.esCambioPinRequerido, isFalse);
      expect(otro.esTransitorio, isFalse);
    });
  });

  group('Entorno', () {
    test('Flavor.desde acepta dev, qa y prod; lo desconocido cae en dev', () {
      expect(Flavor.desde('qa'), Flavor.qa);
      expect(Flavor.desde('prod'), Flavor.prod);
      expect(Flavor.desde('otro'), Flavor.dev);
    });

    test('sin API_URL el entorno no está configurado', () {
      expect(const Entorno(flavor: Flavor.dev, apiUrl: '').configurado, isFalse);
      expect(const Entorno(flavor: Flavor.prod, apiUrl: 'https://x').esProduccion, isTrue);
    });
  });

  group('el detalle del servidor', () {
    // «Datos invalidos» a secas no le dice nada a quien esta en el patio; «Falta la foto de la vista Superior» si.
    test('se expone linea por linea y se pega al mensaje', () {
      const error = ErrorApi(
        message: 'Datos invalidos',
        code: 'VALIDATION_ERROR',
        status: 400,
        details: {
          'fieldErrors': {
            'evidencias': ['Falta la foto de la vista Superior', 'Falta la foto de la vista Frontal'],
            'hallazgos': ['El hallazgo de Capot necesita su foto'],
          },
        },
      );

      expect(error.detalles, hasLength(3));
      expect(error.mensajeDetallado, contains('Datos invalidos'));
      expect(error.mensajeDetallado, contains('· Falta la foto de la vista Superior'));
      expect(error.mensajeDetallado.split('\n'), hasLength(4));
    });

    // Forma real del 400 al cerrar una inspeccion, comprobada contra la API: `details` es una LISTA de textos,
    // no el mapa `fieldErrors`. Con solo el mapa se perdia justo lo accionable.
    test('tambien cuando el servidor manda una lista de textos', () {
      const error = ErrorApi(
        message: 'La inspeccion no cumple los requisitos para cerrarse',
        code: 'VALIDATION_ERROR',
        status: 400,
        details: ['Falta la foto de la vista Trasera', 'Falta la foto de la vista Superior'],
      );

      expect(error.detalles, ['Falta la foto de la vista Trasera', 'Falta la foto de la vista Superior']);
      expect(error.mensajeDetallado, contains('Falta la foto de la vista Superior'));
    });

    test('sin detalle queda el mensaje solo', () {
      const error = ErrorApi(message: 'No se pudo', code: 'X', status: 400);

      expect(error.detalles, isEmpty);
      expect(error.mensajeDetallado, 'No se pudo');
    });
  });
}
