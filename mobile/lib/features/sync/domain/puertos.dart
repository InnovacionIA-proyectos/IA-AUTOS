import '../../inspecciones/domain/foto_local.dart';
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
  /// Contenido del archivo para subirlo como multipart.
  Future<List<int>> leerBytes(String ruta);

  Future<void> borrar(String ruta);
}

/// Lo que devuelve la API al subir un archivo: dónde quedó y su huella.
class ArchivoSubido {
  const ArchivoSubido({required this.storagePath, this.hash});

  final String storagePath;

  /// La calcula el servidor. La app la guarda y la reenvía con la evidencia.
  final String? hash;
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
    String? notas,
  });

  /// Sube la imagen como multipart y devuelve dónde quedó y su huella.
  Future<ArchivoSubido> subirImagen(List<int> bytes, {String nombre = 'foto.jpg'});

  /// Registra una evidencia. [vistaId] es la vista del checklist que cubre; [hallazgoId], el hallazgo al que
  /// pertenece. La API exige todos los campos presentes, aunque algunos puedan ir nulos.
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
  });

  Future<void> agregarHallazgo(String inspeccionId, HallazgoLocal hallazgo);

  /// Registra (o libera, con null) la posición de una unidad en la niñera. Solo con la segunda inspección del cargue
  /// en curso: por eso va después de crearla.
  Future<void> registrarPosicion(String viajeId, String vin, int? posicion);

  Future<void> cerrarInspeccion(String inspeccionId);

  /// Firma la transferencia. Solo viaja **una** firma, la de quien tiene la sesión: el servidor se la atribuye
  /// al usuario del token.
  Future<void> firmarTransferencia(
    String inspeccionId, {
    required String entregaId,
    required String recibeId,
    required String? firma,
  });

  /// Quien recibe firma dejando sus observaciones: la transferencia queda cerrada como `con_reservas`. Es otro
  /// endpoint, no un campo más de la firma, porque solo quien recibe puede y el texto es obligatorio.
  Future<void> recibirConReservas(
    String inspeccionId, {
    required String entregaId,
    required String recibeId,
    required String? firma,
    required String reservas,
  });

  /// Constancia de que una parte no firmó. La registra la otra, con motivo, testigo y la foto ya subida.
  Future<void> registrarNoFirma(
    String inspeccionId, {
    required String entregaId,
    required String recibeId,
    required String parte,
    required String motivo,
    required String testigoId,
    required FotoLocal foto,
  });

  /// Agrega una nota aclaratoria al registro: `inspeccion`, `hallazgo` o `transferencia`. Cada uno tiene su ruta.
  Future<void> agregarNota({required String entidad, required String entidadId, required String texto});
}
