import '../../inspecciones/domain/extra_local.dart';
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
    String? motivoSinInspector,
    String? motivoSinInspectorTexto,
  });

  /// Crea la **versión siguiente** de una inspección cerrada y devuelve su id. La anterior no cambia.
  ///
  /// Se llama al enviar, no al pulsar «Corregir»: el servidor no deja descartar una versión vacía, así que una
  /// corrección que alguien empieza y abandona dejaría la etapa con un borrador que impide volver a corregir.
  Future<String> corregirInspeccion(
    String inspeccionId, {
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
    String? extraId,
  });

  /// Crea un extra de la inspección (barras de techo, volco, platón u otro) y devuelve su id: con él se mandan sus
  /// fotos.
  /// Crea el extra y devuelve su id. [dano] va cuando se marco como danado: el servidor lo guarda como un
  /// hallazgo ligado a el, sin zona, que cuenta en las estadisticas como cualquier otro.
  Future<String> crearExtra(
    String inspeccionId, {
    required String tipo,
    String? texto,
    String? observacion,
    DanoExtra? dano,
  });

  Future<void> agregarHallazgo(String inspeccionId, HallazgoLocal hallazgo);

  /// Registra (o libera, con null) la posición de una unidad en la niñera. Solo con la segunda inspección del cargue
  /// en curso: por eso va después de crearla.
  Future<void> registrarPosicion(String viajeId, String vin, int? posicion);

  Future<void> cerrarInspeccion(String inspeccionId);

  /// Registra una novedad del conductor sobre una unidad del viaje. La foto ya está subida ([FotoLocal.storagePath]).
  ///
  /// [idEnvio] es el identificador del envío en la cola (hueco 6, S8-HU-07.3 CA-4): si el primer envío llegó y solo
  /// se perdió la respuesta, el servidor devuelve la misma novedad en vez de crear otra foto y otro aviso.
  Future<void> crearNovedad(
    String viajeId,
    String vin, {
    required String tipoNovedad,
    required String tamanoDano,
    required String fecha,
    required double lat,
    required double lng,
    required FotoLocal foto,
    String? idEnvio,
  });

  /// La novedad del viaje (S8-HU-07.3 CA-3), con la foto ya subida si la trae. Sin GPS [lat] y [lng] van nulos y el
  /// servidor la acepta «sin ubicación». [proceso] y [lugarId] solo los manda el inspector. [idEnvio] hace que el
  /// reenvío no la duplique.
  Future<void> registrarNovedadViaje(
    String viajeId, {
    required String tipo,
    required String texto,
    required String fecha,
    double? lat,
    double? lng,
    FotoLocal? foto,
    String? idEnvio,
    String? proceso,
    String? lugarId,
  });

  /// Confirma la llegada de una unidad, con la fecha y el lugar del momento en que el conductor lo hizo.
  Future<void> confirmarLlegada(String viajeId, String vin, {required String fecha, required double lat, required double lng});

  /// El recibo sin entrega: el conductor cierra solo con su firma la transferencia de la salida del cargue que hizo
  /// sin inspector.
  Future<void> firmarReciboSinEntrega(String inspeccionId, {required String firma});

  /// La firma de quien tiene la sesión sobre la entrega de una parada: el servidor la guarda en cada unidad
  /// pendiente. Quién recibe lo decide la parada (S8-HU-04.2): el inspector que la tomó o, donde el punto no tiene
  /// inspectores, la persona del lugar.
  ///
  /// En una parada sin inspectores recibe alguien que no es usuario: [recibeNombre], [recibeCedula] y [firmaRecibe]
  /// viajan juntos y entonces la entrega queda cerrada de una vez, porque las dos firmas llegan a la vez. En una
  /// parada con inspectores el servidor rechaza esos tres campos con 400: allí firma él desde su sesión.
  ///
  /// [rechazos] son los carros que no se entregan, con el daño que lo motiva; [reservas], las observaciones de quien
  /// recibe por carro; [fechaMomento], [lat], [lng] y [precisionM], la hora y el GPS del momento en que se firmó.
  Future<void> firmarEntregaPunto(
    String viajeId,
    String lugarId, {
    required String firma,
    String? recibeNombre,
    String? recibeCedula,
    String? firmaRecibe,
    List<ReservaCarro> reservas = const [],
    List<RechazoCarro> rechazos = const [],
    String? fechaMomento,
    double? lat,
    double? lng,
    double? precisionM,
  });

  /// «No hay inspector» en una parada con inspectores (S8-HU-05.2 CA-2): el motivo y la hora y el GPS del celular al
  /// tocarlo. Con una espera ya abierta el servidor devuelve esa misma: reenviar no crea otra ni reinicia el reloj.
  Future<void> activarSinInspector(
    String viajeId,
    String lugarId, {
    required String motivo,
    String? motivoTexto,
    String? fechaMomento,
    double? lat,
    double? lng,
    double? precisionM,
  });

  /// Cierra la entrega de una parada con una sola firma, con su motivo codificado y su evidencia: las [fotos] ya
  /// subidas, cada una con el VIN del carro al que va, y el testigo. [firma] solo hace falta si quien cierra todavía
  /// no había firmado.
  Future<void> cerrarEntregaConUnaFirma(
    String viajeId,
    String lugarId, {
    required String motivoCodigo,
    String? motivoTexto,
    String? firma,
    List<({String vin, FotoLocal foto})> fotos = const [],
    String? testigoNombre,
    String? testigoCedula,
    String? fechaMomento,
    double? lat,
    double? lng,
    double? precisionM,
  });

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

  /// La firma de toda la niñera de una parte (S8-HU-01.3 CA-5), con la hora y el GPS del momento y, si es quien
  /// recibe, sus reservas por carro.
  Future<void> firmarCargueNinera(
    String viajeId, {
    required String parte,
    required String firma,
    required String fechaMomento,
    required double lat,
    required double lng,
    double? precisionM,
    List<ReservaCarro> reservas = const [],
  });

  /// La constancia de no firma de toda la niñera (CA-14), con la foto ya subida.
  Future<void> registrarNoFirmaNinera(
    String viajeId, {
    required String parte,
    required String motivo,
    required String testigoId,
    required FotoLocal foto,
    required String fechaMomento,
    required double lat,
    required double lng,
    double? precisionM,
  });

  /// Agrega una nota aclaratoria al registro: `inspeccion`, `hallazgo` o `transferencia`. Cada uno tiene su ruta.
  Future<void> agregarNota({required String entidad, required String entidadId, required String texto});
}
