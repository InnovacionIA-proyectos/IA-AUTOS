import '../../../core/http/cliente_api.dart';
import '../../auth/domain/sesion.dart';

class UsuariosRepositorio {
  UsuariosRepositorio(this._api);

  final ClienteApi _api;

  /// Para quienes no son administradores la API devuelve la lista reducida `{id, nombre, rol}`.
  Future<List<Usuario>> listar() async {
    final r = await _api.get('/api/usuarios');
    return [for (final u in r as List<dynamic>) Usuario.fromJson(u as Map<String, dynamic>)];
  }
}
