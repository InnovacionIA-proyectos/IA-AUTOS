import '../../../core/http/cliente_api.dart';
import '../../inspecciones/domain/foto_local.dart';
import '../../inspecciones/domain/hallazgo_local.dart';
import '../../inspecciones/domain/nota_aclaratoria.dart';
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
    String? motivoSinInspector,
    String? motivoSinInspectorTexto,
  }) async {
    // usuario_id lo fija el backend desde el token.
    final r = await _api.post('/api/inspecciones', cuerpo: {
      'etapa_id': etapaId,
      'momento': momento,
      'fecha': fecha,
      'lat': lat,
      'lng': lng,
      'notas': notas,
      // Solo en la excepción «No hay inspector»: en cualquier otro caso el servidor responde 400 «Campo no admitido».
      'motivo_sin_inspector': ?motivoSinInspector,
      'motivo_sin_inspector_texto': ?motivoSinInspectorTexto,
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
    String? extraId,
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
      // Solo la foto de un extra lo lleva: en cualquier otra evidencia el servidor no admite el campo.
      'extra_id': ?extraId,
    });
  }

  @override
  Future<String> crearExtra(String inspeccionId, {required String tipo, String? texto, String? observacion}) async {
    // `texto` solo con «otro» y `observacion` solo si la hay: el servidor responde 400 «Campo no admitido» al resto.
    final r = await _api.post('/api/inspecciones/${Uri.encodeComponent(inspeccionId)}/extras', cuerpo: {
      'tipo': tipo,
      'texto': ?texto,
      'observacion': ?observacion,
    });
    return (r as Map<String, dynamic>)['id'] as String;
  }

  @override
  Future<void> agregarHallazgo(String inspeccionId, HallazgoLocal hallazgo) async {
    final foto = hallazgo.foto;
    // La zona es cuadrante + lado (lado nulo en las centrales). `parte_vehiculo` y `coords_diagrama` ya no se
    // aceptan: si llegan, el servidor responde 400. El nivel va nulo cuando registra el conductor.
    await _api.post('/api/inspecciones/${Uri.encodeComponent(inspeccionId)}/hallazgos', cuerpo: {
      'cuadrante_numero': hallazgo.cuadranteNumero,
      'lado': hallazgo.lado,
      'clase_dano': hallazgo.clase,
      'tamano_dano': hallazgo.tamano,
      'nivel_severidad': hallazgo.nivel,
      'nota': ?hallazgo.nota,
      'es_preexistente': hallazgo.esPreexistente,
      // El servidor lo exige con `es_preexistente` en true y lo prohíbe en false: cuando no lo es, la clave no va.
      'hallazgo_origen_id': ?hallazgo.hallazgoOrigenId,
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
  Future<void> registrarPosicion(String viajeId, String vin, int? posicion) async {
    // La clave va presente aunque sea nula: `null` libera la posición de la unidad.
    await _api.enviar(
      'PUT',
      '/api/viajes/${Uri.encodeComponent(viajeId)}/vehiculos/${Uri.encodeComponent(vin)}/posicion',
      cuerpo: {'posicion': posicion},
    );
  }

  @override
  Future<void> cerrarInspeccion(String inspeccionId) async {
    await _api.post('/api/inspecciones/${Uri.encodeComponent(inspeccionId)}/cerrar');
  }

  @override
  Future<void> crearNovedad(
    String viajeId,
    String vin, {
    required String tipoNovedad,
    required String tamanoDano,
    required String fecha,
    required double lat,
    required double lng,
    required FotoLocal foto,
  }) async {
    // Sin texto libre ni zona, clase o nivel: el servidor responde 400 «Campo no admitido» si llega cualquiera. La
    // evidencia va con su sello completo y sin `nota`.
    await _api.post('${_rutaUnidad(viajeId, vin)}/novedades', cuerpo: {
      'fecha': fecha,
      'lat': lat,
      'lng': lng,
      'tipo_novedad': tipoNovedad,
      'tamano_dano': tamanoDano,
      'evidencia': {
        'storage_path': foto.storagePath,
        'sello_fecha': foto.selloFecha,
        'sello_lat': foto.selloLat,
        'sello_lng': foto.selloLng,
        'precision': foto.precision,
        'dispositivo_id': foto.dispositivoId,
        'hash_sha256': foto.hash,
      },
    });
  }

  @override
  Future<void> confirmarLlegada(String viajeId, String vin, {required String fecha, required double lat, required double lng}) async {
    await _api.post('${_rutaUnidad(viajeId, vin)}/llegada', cuerpo: {'fecha': fecha, 'lat': lat, 'lng': lng});
  }

  String _rutaUnidad(String viajeId, String vin) =>
      '/api/viajes/${Uri.encodeComponent(viajeId)}/vehiculos/${Uri.encodeComponent(vin)}';

  @override
  Future<void> firmarReciboSinEntrega(String inspeccionId, {required String firma}) async {
    await _api.post('/api/inspecciones/${Uri.encodeComponent(inspeccionId)}/transferencia/sin-entrega', cuerpo: {'firma': firma});
  }

  @override
  Future<void> firmarEntregaPunto(String viajeId, String lugarId, {required String firma}) async {
    await _api.post('${_rutaEntrega(viajeId, lugarId)}/firma', cuerpo: {'firma': firma});
  }

  @override
  Future<void> cerrarEntregaConUnaFirma(String viajeId, String lugarId, {required String motivo, String? firma}) async {
    await _api.post('${_rutaEntrega(viajeId, lugarId)}/una-firma', cuerpo: {'motivo': motivo, 'firma': ?firma});
  }

  String _rutaEntrega(String viajeId, String lugarId) =>
      '/api/viajes/${Uri.encodeComponent(viajeId)}/entregas/${Uri.encodeComponent(lugarId)}';

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

  @override
  Future<void> registrarNoFirma(
    String inspeccionId, {
    required String entregaId,
    required String recibeId,
    required String parte,
    required String motivo,
    required String testigoId,
    required FotoLocal foto,
  }) async {
    await _api.post('/api/inspecciones/${Uri.encodeComponent(inspeccionId)}/transferencia/no-firma', cuerpo: {
      'entrega_usuario_id': entregaId,
      'recibe_usuario_id': recibeId,
      'parte': parte,
      'motivo': motivo,
      'testigo_usuario_id': testigoId,
      // La foto va dentro, con la misma forma que la de un hallazgo: el servidor la guarda como constancia.
      'evidencia': {
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
  Future<void> agregarNota({required String entidad, required String entidadId, required String texto}) async {
    final id = Uri.encodeComponent(entidadId);
    // Cada registro tiene su ruta; el servidor no admite una sola con el tipo adentro.
    final ruta = switch (entidad) {
      EntidadNota.hallazgo => '/api/hallazgos/$id/notas',
      EntidadNota.transferencia => '/api/inspecciones/$id/transferencia/notas',
      _ => '/api/inspecciones/$id/notas',
    };
    await _api.post(ruta, cuerpo: {'texto': texto});
  }
}
