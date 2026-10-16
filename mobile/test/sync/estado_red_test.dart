import 'package:connectivity_plus/connectivity_plus.dart';
import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/features/sync/data/estado_red_conectividad.dart';

import '../support/dio_falso.dart';

void main() {
  group('estado de red', () {
    test('wifi, datos y ethernet son estables; sin red, VPN u otros no', () {
      expect(esEstable([ConnectivityResult.wifi]), isTrue);
      expect(esEstable([ConnectivityResult.mobile]), isTrue);
      expect(esEstable([ConnectivityResult.ethernet]), isTrue);
      expect(esEstable([ConnectivityResult.vpn, ConnectivityResult.wifi]), isTrue);
      expect(esEstable([ConnectivityResult.none]), isFalse);
      expect(esEstable([ConnectivityResult.vpn]), isFalse);
      expect(esEstable([ConnectivityResult.bluetooth]), isFalse);
      expect(esEstable([]), isFalse);
    });

    test('apiAlcanzable consulta /health y exige 2xx', () async {
      final adaptador = AdaptadorFalso((_) => json(200, {'status': 'ok'}));
      final red = EstadoRedConectividad(apiUrl: 'https://api.test', dio: Dio()..httpClientAdapter = adaptador);
      expect(await red.apiAlcanzable(), isTrue);
      expect(adaptador.peticiones.single.uri.toString(), 'https://api.test/health');
    });

    test('apiAlcanzable es false con 503, con fallo de red o sin URL configurada', () async {
      final r503 = EstadoRedConectividad(apiUrl: 'https://api.test', dio: Dio()..httpClientAdapter = AdaptadorFalso((_) => json(503, {})));
      expect(await r503.apiAlcanzable(), isFalse);

      final rCaida = EstadoRedConectividad(
        apiUrl: 'https://api.test',
        dio: Dio()..httpClientAdapter = AdaptadorFalso((o) => throw DioException.connectionError(requestOptions: o, reason: 'x')),
      );
      expect(await rCaida.apiAlcanzable(), isFalse);

      expect(await EstadoRedConectividad(apiUrl: '').apiAlcanzable(), isFalse);
    });
  });
}
