import 'package:traza_autos/core/errores/error_api.dart';
import 'package:traza_autos/features/inspecciones/domain/foto_local.dart';
import 'package:traza_autos/features/inspecciones/domain/hallazgo_local.dart';
import 'package:traza_autos/features/sync/domain/job.dart';
import 'package:traza_autos/features/sync/domain/puertos.dart';
import 'package:traza_autos/features/vehiculos/domain/coords_diagrama.dart';

class ColaEnMemoria implements ColaRepositorio {
  final List<Job> jobs = [];

  @override
  Future<void> agregar(Job job) async => jobs.add(job);

  @override
  Future<List<Job>> listar() async => List.unmodifiable(jobs);

  @override
  Future<void> guardar(Job job) async {
    final i = jobs.indexWhere((j) => j.id == job.id);
    if (i != -1) jobs[i] = job;
  }

  @override
  Future<void> eliminar(String id) async => jobs.removeWhere((j) => j.id == id);
}

class RedFalsa implements EstadoRed {
  bool estable = true;
  bool alcanzable = true;

  /// Cuántas consultas a `redEstable` responden true antes de "caerse" (null = nunca).
  int? consultasEstables;
  int consultas = 0;

  @override
  Stream<bool> get cambios => const Stream.empty();

  @override
  Future<bool> redEstable() async {
    consultas++;
    final limite = consultasEstables;
    if (limite != null && consultas > limite) return false;
    return estable;
  }

  @override
  Future<bool> apiAlcanzable() async => alcanzable;
}

class ArchivosFalsos implements ArchivosLocales {
  ArchivosFalsos(Iterable<String> rutas) : existentes = {...rutas};

  final Set<String> existentes;
  final List<String> borrados = [];

  @override
  Future<String> leerBase64(String ruta) async {
    if (!existentes.contains(ruta)) throw StateError('No existe $ruta');
    return 'base64:$ruta';
  }

  @override
  Future<void> borrar(String ruta) async {
    existentes.remove(ruta);
    borrados.add(ruta);
  }
}

/// Registra cada llamada y permite programar errores por operación.
class GatewayFalso implements GatewayInformes {
  final List<String> llamadas = [];
  final Map<String, List<Object>> errores = {};
  final List<Map<String, Object?>> evidencias = [];
  int _inspecciones = 0;
  int _imagenes = 0;

  final Map<String, int> _conteo = {};
  final Map<String, (int, Object)> _enLlamada = {};

  Future<void> _paso(String operacion) async {
    llamadas.add(operacion);
    final n = _conteo[operacion] = (_conteo[operacion] ?? 0) + 1;
    final programado = _enLlamada[operacion];
    if (programado != null && programado.$1 == n) throw programado.$2;
    final pendientes = errores[operacion];
    if (pendientes != null && pendientes.isNotEmpty) throw pendientes.removeAt(0);
  }

  /// Hace que la N-ésima llamada (contando desde 1, en toda la vida del doble) a [operacion] lance [error].
  void fallarEnLlamada(String operacion, int n, Object error) => _enLlamada[operacion] = (n, error);

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
    await _paso('crearInspeccion');
    return 'insp-${++_inspecciones}';
  }

  @override
  Future<String> subirImagen(String base64, {String ext = 'jpg'}) async {
    await _paso('subirImagen');
    return 'ruta/${++_imagenes}.$ext';
  }

  @override
  Future<void> agregarEvidencia(
    String inspeccionId, {
    required String storagePath,
    required String selloFecha,
    double? selloLat,
    double? selloLng,
  }) async {
    await _paso('agregarEvidencia');
    evidencias.add({'inspeccion': inspeccionId, 'path': storagePath, 'fecha': selloFecha, 'lat': selloLat, 'lng': selloLng});
  }

  @override
  Future<void> agregarHallazgo(String inspeccionId, HallazgoLocal hallazgo) => _paso('agregarHallazgo');

  @override
  Future<void> cerrarInspeccion(String inspeccionId) => _paso('cerrarInspeccion');

  @override
  Future<void> firmarTransferencia(
    String inspeccionId, {
    required String entregaId,
    required String recibeId,
    String? firmaEntrega,
    String? firmaRecibe,
  }) => _paso('firmarTransferencia');
}

ErrorApi errorHttp(int status, [String mensaje = 'error']) => ErrorApi(message: mensaje, code: 'X', status: status);

/// El mismo id de usuario que `sesionPrueba`: lo normal es que el job sea de quien tiene la sesión. Se pasa otro,
/// o null para un job huérfano (encolado por una versión anterior de la app), cuando eso es lo que se prueba.
const usuarioPrueba = 'u1';

InformeJob informe({
  String id = 'j1',
  String? usuarioId = usuarioPrueba,
  List<FotoLocal>? fotos,
  List<HallazgoLocal>? hallazgos,
  int intentos = 0,
  bool fallido = false,
  String? inspeccionId,
  int hallazgosEnviados = 0,
  bool cerrada = false,
}) => InformeJob(
  id: id,
  etiqueta: 'Informe $id',
  creadoEn: 1,
  intentos: intentos,
  fallido: fallido,
  usuarioId: usuarioId,
  etapaId: 'etapa-1',
  momento: 'informe',
  fecha: '2026-01-02T10:00:00.000Z',
  lat: 6.2,
  lng: -75.5,
  fotos: fotos ?? const [],
  hallazgos: hallazgos ?? const [],
  inspeccionId: inspeccionId,
  hallazgosEnviados: hallazgosEnviados,
  cerrada: cerrada,
);

FotoLocal foto(String ruta, {String fecha = '2026-01-02T10:01:00.000Z', double? lat = 6.21, double? lng = -75.51, String? storagePath, bool enviada = false}) =>
    FotoLocal(ruta: ruta, selloFecha: fecha, selloLat: lat, selloLng: lng, storagePath: storagePath, enviada: enviada);

const hallazgoEjemplo = HallazgoLocal(parte: 'Capot', clase: 'golpe', tamano: '1_5cm', coords: CoordsDiagrama(0.5, 0.66));
