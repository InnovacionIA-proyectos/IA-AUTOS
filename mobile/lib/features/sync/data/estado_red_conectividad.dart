import 'package:connectivity_plus/connectivity_plus.dart';
import 'package:dio/dio.dart';

import '../domain/puertos.dart';

/// "Señal estable" = conectado por wifi, datos (celular) o ethernet. `none` y `other` (por ejemplo solo VPN o
/// bluetooth) no cuentan. El acceso real a internet se comprueba aparte con [EstadoRedConectividad.apiAlcanzable].
bool esEstable(List<ConnectivityResult> resultados) => resultados.any(
      (r) => r == ConnectivityResult.wifi || r == ConnectivityResult.mobile || r == ConnectivityResult.ethernet,
    );

class EstadoRedConectividad implements EstadoRed {
  EstadoRedConectividad({required this.apiUrl, this.encabezados = const {}, Connectivity? conectividad, Dio? dio})
      : _conectividad = conectividad ?? Connectivity(),
        _dio = dio ?? Dio();

  final String apiUrl;

  /// Encabezados de enrutamiento de la pasarela: sin ellos /health responde 404 y la cola nunca sincronizaría.
  final Map<String, String> encabezados;
  final Connectivity _conectividad;
  final Dio _dio;

  @override
  Stream<bool> get cambios => _conectividad.onConnectivityChanged.map(esEstable).distinct();

  @override
  Future<bool> redEstable() async => esEstable(await _conectividad.checkConnectivity());

  /// Alcance real de la API (GET /health) con un tiempo corto: evita empezar a sincronizar sobre una señal que se
  /// cae a mitad.
  @override
  Future<bool> apiAlcanzable() async {
    if (apiUrl.isEmpty) return false;
    try {
      final r = await _dio.get<dynamic>(
        '$apiUrl/health',
        options: Options(
          headers: encabezados,
          sendTimeout: const Duration(seconds: 5),
          receiveTimeout: const Duration(seconds: 5),
          validateStatus: (_) => true,
        ),
      );
      return r.statusCode != null && r.statusCode! >= 200 && r.statusCode! < 300;
    } catch (_) {
      return false;
    }
  }
}
