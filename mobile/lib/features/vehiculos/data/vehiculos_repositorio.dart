import '../../../core/http/cliente_api.dart';
import '../domain/inventario.dart';

class VehiculosRepositorio {
  VehiculosRepositorio(this._api);

  final ClienteApi _api;

  Future<Inventario> inventario(String vin) async {
    final r = await _api.get('/api/vehiculos/${Uri.encodeComponent(vin)}/inventario');
    return Inventario.fromJson(r as Map<String, dynamic>);
  }
}
