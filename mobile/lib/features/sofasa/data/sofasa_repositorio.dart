import '../../../core/http/cliente_api.dart';
import '../../../core/http/listados.dart';
import '../domain/orden_sofasa.dart';

class SofasaRepositorio {
  SofasaRepositorio(this._api);

  final ClienteApi _api;

  /// Órdenes de carga sincronizadas por el ETL. Filtro opcional por VIN.
  Future<List<OrdenSofasa>> ordenes({String? vin}) async {
    final filtro = vin?.trim().toUpperCase() ?? '';
    final r = await _api.get('/api/sofasa/ordenes', query: filtro.isEmpty ? null : {'vin': filtro});
    return [
      for (final o in elementosDe(r, ruta: '/api/sofasa/ordenes')) OrdenSofasa.fromJson(o as Map<String, dynamic>),
    ];
  }
}
