import '../../../core/http/cliente_api.dart';
import '../../auth/domain/sesion.dart';

class UsuariosRepositorio {
  UsuariosRepositorio(this._api);

  final ClienteApi _api;

  /// Para quienes no son administradores la API devuelve la lista reducida `{id, nombre, rol}`.
  Future<List<Usuario>> listar() async => _lista('/api/usuarios');

  /// Quienes pueden firmar una transferencia de este viaje: su conductor y su inspector, y nadie más. Es lo que
  /// debe ofrecer el selector; con la lista completa de usuarios salían como firmantes personas que no
  /// participan en el viaje y a las que el servidor rechazaría.
  Future<List<Usuario>> firmantesDeViaje(String viajeId) =>
      _lista('/api/viajes/${Uri.encodeComponent(viajeId)}/firmantes');

  Future<List<Usuario>> _lista(String ruta) async {
    final r = await _api.get(ruta);
    return [for (final u in r as List<dynamic>) Usuario.fromJson(u as Map<String, dynamic>)];
  }
}
