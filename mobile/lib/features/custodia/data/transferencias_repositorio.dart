import '../../../core/http/cliente_api.dart';
import '../../vehiculos/domain/inventario.dart';

class TransferenciasRepositorio {
  TransferenciasRepositorio(this._api);

  final ClienteApi _api;

  /// La transferencia de un informe, si alguien ya la empezó. Null mientras no haya firmado nadie.
  ///
  /// Importa antes de firmar: la primera firma fija quién entrega y quién recibe, y el servidor rechaza con
  /// 409 cualquier intento posterior de cambiarlos. Quien firma de segundo no elige, hereda.
  Future<TransferenciaVista?> deInforme(String inspeccionId) async {
    final r = await _api.get('/api/inspecciones/${Uri.encodeComponent(inspeccionId)}') as Map<String, dynamic>;
    final t = r['transferencia'] as Map<String, dynamic>?;
    return t == null ? null : TransferenciaVista.fromJson(t);
  }
}
