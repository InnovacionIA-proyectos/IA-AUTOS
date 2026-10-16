import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/core/config/entorno.dart';
import 'package:traza_autos/core/errores/error_api.dart';
import 'package:traza_autos/core/http/cliente_api.dart';

import '../support/dio_falso.dart';

ClienteApi cliente(AdaptadorFalso adaptador, {String? token = 'jwt-123', void Function()? alNoAutorizado}) {
  final dio = Dio()..httpClientAdapter = adaptador;
  return ClienteApi(baseUrl: 'https://api.test', token: () async => token, alNoAutorizado: alNoAutorizado, dio: dio);
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

  group('clasificación transitoria', () {
    test('0, 401, 408, 429 y 5xx se reintentan; el resto no', () {
      bool t(int s) => ErrorApi(message: '', code: '', status: s).esTransitorio;
      expect([0, 401, 408, 429, 500, 503].every(t), isTrue);
      expect([400, 403, 404, 409, 422].any(t), isFalse);
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
}
