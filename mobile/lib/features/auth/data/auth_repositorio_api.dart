import '../../../core/http/cliente_api.dart';
import '../domain/sesion.dart';

class AuthRepositorioApi implements AuthRepositorio {
  AuthRepositorioApi(this._api);

  final ClienteApi _api;

  @override
  Future<Sesion> iniciarSesion(String email, String password) async {
    final r = await _api.post('/api/auth/login', cuerpo: {'email': email, 'password': password});
    return Sesion.fromJson(r as Map<String, dynamic>);
  }

  @override
  Future<Sesion> iniciarSesionConPin(String cedula, String pin) async {
    final r = await _api.post('/api/auth/login-conductor', cuerpo: {'cedula': cedula, 'pin': pin});
    return Sesion.fromJson(r as Map<String, dynamic>);
  }

  @override
  Future<void> cambiarPin(String pinActual, String pinNuevo) =>
      _api.post('/api/auth/cambiar-pin', cuerpo: {'pin_actual': pinActual, 'pin_nuevo': pinNuevo});

  @override
  Future<List<String>> capacidades() async {
    final r = await _api.get('/api/auth/me');
    final lista = (r as Map<String, dynamic>)['capacidades'] as List<dynamic>?;
    return [...?lista?.cast<String>()];
  }
}
