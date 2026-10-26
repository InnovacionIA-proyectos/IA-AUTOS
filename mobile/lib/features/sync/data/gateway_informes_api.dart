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
    String? notas,
  }) async {
    // usuario_id lo fija el backend desde el token.
    final r = await _api.post('/api/inspecciones', cuerpo: {
      'etapa_id': etapaId,
      'momento': momento,
      'fecha': fecha,
      'lat': lat,
      'lng': lng,
      'notas': notas,
    });
    return (r as Map<String, dynamic>)['id'] as String;
  }

  @override
  Future<ArchivoSubido> subirImagen(List<int> bytes, {String nombre = 'foto.jpg'}) async {
    final r = await _api.subirArchivo('/api/uploads', bytes: bytes, nombre: nombre) as Map<String, dynamic>;
    return ArchivoSubido(storagePath: r['storage_path'] as String, hash: r['hash_sha256'] as String?);
  }

  @override
  Future<void> agregarEvidencia(
    String inspeccionId, {
    required String storagePath,
    required String selloFecha,
    double? selloLat,
    double? selloLng,
    String? vistaId,
    String? hallazgoId,
    double? precision,
    String? dispositivoId,
    String? nota,
    String? hash,
  }) async {
    // La API exige las diez claves presentes; el valor puede ir nulo (una foto sin GPS, una vista sin nota).
    await _api.post('/api/inspecciones/${Uri.encodeComponent(inspeccionId)}/evidencias', cuerpo: {
      'hallazgo_id': hallazgoId,
      'vista_id': vistaId,
      'storage_path': storagePath,
      'sello_fecha': selloFecha,
      'sello_lat': selloLat,
      'sello_lng': selloLng,
      'precision': precision,
      'dispositivo_id': dispositivoId,
      'nota': nota,
      'hash_sha256': hash,
    });
  }

  @override
  Future<void> agregarHallazgo(String inspeccionId, HallazgoLocal hallazgo) async {
    final foto = hallazgo.foto;
    await _api.post('/api/inspecciones/${Uri.encodeComponent(inspeccionId)}/hallazgos', cuerpo: {
      'parte_vehiculo': hallazgo.parte,
      'clase_dano': hallazgo.clase,
      'tamano_dano': hallazgo.tamano,
      'coords_diagrama': hallazgo.coords?.toJson(),
      // La evidencia va dentro: el servidor no acepta un hallazgo sin su foto.
      'evidencia': foto == null
          ? null
          : {
              'storage_path': foto.storagePath,
              'sello_fecha': foto.selloFecha,
              'sello_lat': foto.selloLat,
              'sello_lng': foto.selloLng,
              'precision': foto.precision,
              'dispositivo_id': foto.dispositivoId,
              'nota': foto.nota,
              'hash_sha256': foto.hash,
            },
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
    required String? firma,
  }) async {
    await _api.post('/api/inspecciones/${Uri.encodeComponent(inspeccionId)}/transferencia', cuerpo: {
      'entrega_usuario_id': entregaId,
      'recibe_usuario_id': recibeId,
      'firma': firma,
    });
  }

  @override
  Future<void> recibirConReservas(
    String inspeccionId, {
    required String entregaId,
    required String recibeId,
    required String? firma,
    required String reservas,
  }) async {
    await _api.post('/api/inspecciones/${Uri.encodeComponent(inspeccionId)}/transferencia/reservas', cuerpo: {
      'entrega_usuario_id': entregaId,
      'recibe_usuario_id': recibeId,
      'firma': firma,
      'reservas': reservas,
    });
  }
}
