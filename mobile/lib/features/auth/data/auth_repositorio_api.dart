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
}
