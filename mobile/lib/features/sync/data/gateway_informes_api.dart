import '../../../core/http/cliente_api.dart';
import '../../inspecciones/domain/hallazgo_local.dart';
import '../domain/puertos.dart';

/// Implementación de [GatewayInformes] sobre la API REST (contrato en backend/docs/openapi.json).
class GatewayInformesApi implements GatewayInformes {
  GatewayInformesApi(this._api);

  final ClienteApi _api;

  @override
  Future<String> crearInspeccion({
    required String etapaId,
    required String momento,
    required String fecha,
    required double lat,
    required double lng,
    String? conductorNombre,
    String? placa,
    String? trailer,
    String? notas,
  }) async {
    // usuario_id lo fija el backend desde el token.
    final r = await _api.post('/api/inspecciones', cuerpo: {
      'etapa_id': etapaId,
      'momento': momento,
      'fecha': fecha,
      'lat': lat,
      'lng': lng,
      'conductor_nombre': conductorNombre,
      'placa': placa,
      'trailer': trailer,
      'notas': notas,
    });
    return (r as Map<String, dynamic>)['id'] as String;
  }

  @override
  Future<String> subirImagen(String base64, {String ext = 'jpg'}) async {
    final r = await _api.post('/api/uploads', cuerpo: {'data': base64, 'ext': ext});
    return (r as Map<String, dynamic>)['storage_path'] as String;
  }

  @override
  Future<void> agregarEvidencia(
    String inspeccionId, {
    required String storagePath,
    required String selloFecha,
    double? selloLat,
    double? selloLng,
  }) async {
    await _api.post('/api/inspecciones/${Uri.encodeComponent(inspeccionId)}/evidencias', cuerpo: {
      'storage_path': storagePath,
      'sello_fecha': selloFecha,
      'sello_lat': selloLat,
      'sello_lng': selloLng,
    });
  }

  @override
  Future<void> agregarHallazgo(String inspeccionId, HallazgoLocal hallazgo) async {
    await _api.post('/api/inspecciones/${Uri.encodeComponent(inspeccionId)}/hallazgos', cuerpo: {
      'parte_vehiculo': hallazgo.parte,
      'clase_dano': hallazgo.clase,
      'tamano_dano': hallazgo.tamano,
      'coords_diagrama': hallazgo.coords?.toJson(),
    });
  }

  @override
  Future<void> cerrarInspeccion(String inspeccionId) async {
    await _api.post('/api/inspecciones/${Uri.encodeComponent(inspeccionId)}/cerrar');
  }

  @override
  Future<void> firmarTransferencia(
    String inspeccionId, {
    required String entregaId,
    required String recibeId,
    String? firmaEntrega,
    String? firmaRecibe,
  }) async {
    await _api.post('/api/inspecciones/${Uri.encodeComponent(inspeccionId)}/transferencia', cuerpo: {
      'entrega_usuario_id': entregaId,
      'recibe_usuario_id': recibeId,
      'firma_entrega': ?firmaEntrega,
      'firma_recibe': ?firmaRecibe,
    });
  }
}
