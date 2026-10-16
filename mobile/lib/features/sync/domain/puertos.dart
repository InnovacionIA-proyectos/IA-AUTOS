import '../../inspecciones/domain/hallazgo_local.dart';
import 'job.dart';

/// Persistencia de la cola. El orden de `listar` es el de inserción (FIFO).
abstract interface class ColaRepositorio {
  Future<void> agregar(Job job);
  Future<List<Job>> listar();

  /// Reemplaza el job con el mismo id (progreso, intentos, error). No hace nada si ya no existe.
  Future<void> guardar(Job job);
  Future<void> eliminar(String id);
}

/// Estado de la conectividad. "Señal estable" = wifi, datos o ethernet con internet alcanzable.
abstract interface class EstadoRed {
  /// Emite true cuando la conexión pasa a ser estable y false cuando se pierde.
  Stream<bool> get cambios;

  Future<bool> redEstable();

  /// La API responde de verdad (GET /health) en un tiempo corto.
  Future<bool> apiAlcanzable();
}

/// Acceso a los archivos de las fotos guardadas en el dispositivo.
abstract interface class ArchivosLocales {
  Future<String> leerBase64(String ruta);
  Future<void> borrar(String ruta);
}

/// Operaciones de la API que usa la cola, ya con los nombres y formas del contrato.
abstract interface class GatewayInformes {
  /// Devuelve el id de la inspección creada.
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
  });

  /// Sube la imagen en base64 y devuelve su `storage_path`.
  Future<String> subirImagen(String base64, {String ext = 'jpg'});

  Future<void> agregarEvidencia(
    String inspeccionId, {
    required String storagePath,
    required String selloFecha,
    double? selloLat,
    double? selloLng,
  });

  Future<void> agregarHallazgo(String inspeccionId, HallazgoLocal hallazgo);

  Future<void> cerrarInspeccion(String inspeccionId);

  Future<void> firmarTransferencia(
    String inspeccionId, {
    required String entregaId,
    required String recibeId,
    String? firmaEntrega,
    String? firmaRecibe,
  });
}
