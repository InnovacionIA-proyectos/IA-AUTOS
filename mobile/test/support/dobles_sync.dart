import 'package:traza_autos/core/errores/error_api.dart';
import 'package:traza_autos/features/inspecciones/domain/foto_local.dart';
import 'package:traza_autos/features/inspecciones/domain/hallazgo_local.dart';
import 'package:traza_autos/features/sync/domain/job.dart';
import 'package:traza_autos/features/sync/domain/puertos.dart';

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
  Future<List<int>> leerBytes(String ruta) async {
    if (!existentes.contains(ruta)) throw StateError('No existe $ruta');
    return ruta.codeUnits;
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
  Future<ArchivoSubido> subirImagen(List<int> bytes, {String nombre = 'foto.jpg'}) async {
    await _paso('subirImagen');
    final n = ++_imagenes;
    return ArchivoSubido(storagePath: 'ruta/$n.jpg', hash: 'hash-$n');
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
    await _paso('agregarEvidencia');
    evidencias.add({
      'inspeccion': inspeccionId,
      'path': storagePath,
      'fecha': selloFecha,
      'lat': selloLat,
      'lng': selloLng,
      'vista_id': vistaId,
      'hallazgo_id': hallazgoId,
      'precision': precision,
      'dispositivo_id': dispositivoId,
      'nota': nota,
      'hash_sha256': hash,
    });
  }

  @override
  Future<void> agregarHallazgo(String inspeccionId, HallazgoLocal hallazgo) => _paso('agregarHallazgo');

  /// Cada posicion que se mando, en orden: (viaje, VIN, posicion; null es liberar).
  final List<(String, String, int?)> posicionesEnviadas = [];

  @override
  Future<void> registrarPosicion(String viajeId, String vin, int? posicion) {
    posicionesEnviadas.add((viajeId, vin, posicion));
    return _paso('registrarPosicion');
  }

  @override
  Future<void> cerrarInspeccion(String inspeccionId) => _paso('cerrarInspeccion');

  @override
  Future<void> firmarTransferencia(
    String inspeccionId, {
    required String entregaId,
    required String recibeId,
    required String? firma,
  }) {
    firmasEnviadas.add(firma);
    return _paso('firmarTransferencia');
  }

  @override
  Future<void> recibirConReservas(
    String inspeccionId, {
    required String entregaId,
    required String recibeId,
    required String? firma,
    required String reservas,
  }) {
    firmasEnviadas.add(firma);
    reservasEnviadas.add(reservas);
    return _paso('recibirConReservas');
  }

  /// Lo que viajó en cada firma, para comprobar que es una sola y la correcta.
  final List<String?> firmasEnviadas = [];

  @override
  Future<void> registrarNoFirma(
    String inspeccionId, {
    required String entregaId,
    required String recibeId,
    required String parte,
    required String motivo,
    required String testigoId,
    required FotoLocal foto,
  }) {
    constanciasEnviadas.add((parte: parte, motivo: motivo, testigoId: testigoId, foto: foto));
    return _paso('registrarNoFirma');
  }

  @override
  Future<void> agregarNota({required String entidad, required String entidadId, required String texto}) {
    notasEnviadas.add((entidad: entidad, entidadId: entidadId, texto: texto));
    return _paso('agregarNota');
  }

  /// Las notas aclaratorias que se enviaron, con a qué registro iban.
  final List<({String entidad, String entidadId, String texto})> notasEnviadas = [];

  /// Las observaciones de cada recibo con reservas.
  final List<String> reservasEnviadas = [];

  /// Lo que viajó en cada constancia de no firma.
  final List<({String parte, String motivo, String testigoId, FotoLocal foto})> constanciasEnviadas = [];
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
  String? viajeId,
  Map<String, int> posiciones = const {},
  Map<String, int> posicionesPrevias = const {},
  bool posicionesEnviadas = false,
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
  viajeId: viajeId,
  posiciones: posiciones,
  posicionesPrevias: posicionesPrevias,
  posicionesEnviadas: posicionesEnviadas,
);

FotoLocal foto(String ruta, {String fecha = '2026-01-02T10:01:00.000Z', double? lat = 6.21, double? lng = -75.51, String? storagePath, bool enviada = false}) =>
    FotoLocal(ruta: ruta, selloFecha: fecha, selloLat: lat, selloLng: lng, storagePath: storagePath, enviada: enviada);

const hallazgoEjemplo = HallazgoLocal(
  cuadranteNumero: 26,
  lado: 'derecho',
  zona: 'Llanta trasera derecha',
  clase: 'golpe',
  tamano: '1_5cm',
  nivel: 'v2',
  nota: 'rayón en el costado',
);
