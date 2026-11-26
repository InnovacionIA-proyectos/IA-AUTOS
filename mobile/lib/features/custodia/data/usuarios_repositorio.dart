import '../../../core/http/cliente_api.dart';
import '../../../core/http/listados.dart';
import '../../auth/domain/sesion.dart';

class UsuariosRepositorio {
  UsuariosRepositorio(this._api);

  final ClienteApi _api;

  /// La forma de la respuesta depende de quién pregunta, no de la ruta: a quien administra usuarios la API le
  /// responde una página y a los demás la lista reducida `{id, nombre, rol}`. Hoy el selector de testigo lo usa un
  /// inspector, que recibe el arreglo; con la sesión de un administrador llegaría la página.
  Future<List<Usuario>> listar() async => _lista('/api/usuarios');

  /// Quienes pueden firmar una transferencia de este viaje: su conductor y su inspector, y nadie más. Es lo que
  /// debe ofrecer el selector; con la lista completa de usuarios salían como firmantes personas que no
  /// participan en el viaje y a las que el servidor rechazaría.
  Future<List<Usuario>> firmantesDeViaje(String viajeId) =>
      _lista('/api/viajes/${Uri.encodeComponent(viajeId)}/firmantes');

  Future<List<Usuario>> _lista(String ruta) async {
    final r = await _api.get(ruta);
    return [for (final u in elementosDe(r, ruta: ruta)) Usuario.fromJson(u as Map<String, dynamic>)];
  }
}
