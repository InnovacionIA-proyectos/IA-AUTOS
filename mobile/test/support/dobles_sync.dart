import 'package:traza_autos/core/errores/error_api.dart';
import 'package:traza_autos/features/inspecciones/domain/extra_local.dart';
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

  /// Cada extra que salio, con su dano si lo llevaba.
  final List<({String tipo, DanoExtra? dano})> extrasEnviados = [];
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
    String? motivoSinInspector,
    String? motivoSinInspectorTexto,
  }) async {
    motivosEnviados.add((motivoSinInspector, motivoSinInspectorTexto));
    await _paso('crearInspeccion');
    return 'insp-${++_inspecciones}';
  }

  /// Las inspecciones que se mandaron a corregir, en orden.
  final corregidas = <String>[];

  @override
  Future<String> corregirInspeccion(
    String inspeccionId, {
    required String fecha,
    required double lat,
    required double lng,
    String? notas,
  }) async {
    corregidas.add(inspeccionId);
    await _paso('corregirInspeccion');
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
    String? extraId,
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
      'extra_id': extraId,
    });
  }

  /// Cada extra que se creó: (inspección, tipo, texto, observación).
  final List<(String, String, String?, String?)> extrasCreados = [];

  @override
  Future<String> crearExtra(
    String inspeccionId, {
    required String tipo,
    String? texto,
    String? observacion,
    DanoExtra? dano,
  }) async {
    extrasEnviados.add((tipo: tipo, dano: dano));
    await _paso('crearExtra');
    extrasCreados.add((inspeccionId, tipo, texto, observacion));
    return 'extra-${extrasCreados.length}';
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

  /// Cada novedad del conductor que salió: (viaje, VIN, tipo, tamaño, fecha, lat, lng, ruta de la foto subida).
  final List<(String, String, String, String, String, double, double, String?)> novedadesEnviadas = [];

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
    String? idEnvio,
  }) {
    novedadesEnviadas.add((viajeId, vin, tipoNovedad, tamanoDano, fecha, lat, lng, foto.storagePath));
    idsEnvioNovedad.add(idEnvio);
    return _paso('crearNovedad');
  }

  /// El `id_envio` con que salió cada novedad de un carro, en el mismo orden que [novedadesEnviadas].
  final List<String?> idsEnvioNovedad = [];

  /// Cada novedad del viaje que salió, con todo lo que llevaba (S8-HU-07.3).
  final List<NovedadViajeEnviada> novedadesViajeEnviadas = [];

  @override
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
  }) {
    novedadesViajeEnviadas.add((
      viajeId: viajeId,
      tipo: tipo,
      texto: texto,
      fecha: fecha,
      lat: lat,
      lng: lng,
      storagePath: foto?.storagePath,
      idEnvio: idEnvio,
      proceso: proceso,
      lugarId: lugarId,
    ));
    return _paso('registrarNovedadViaje');
  }

  /// Cada llegada que salió: (viaje, VIN, fecha, lat, lng).
  final List<(String, String, String, double, double)> llegadasEnviadas = [];

  @override
  Future<void> confirmarLlegada(String viajeId, String vin, {required String fecha, required double lat, required double lng}) {
    llegadasEnviadas.add((viajeId, vin, fecha, lat, lng));
    return _paso('confirmarLlegada');
  }

  /// El motivo «No hay inspector» con que se creó cada inspección: (código, texto); (null, null) en las normales.
  final List<(String?, String?)> motivosEnviados = [];

  /// Las inspecciones sobre las que se firmó el recibo sin entrega, con el trazo.
  final List<(String, String)> recibosSinEntrega = [];

  @override
  Future<void> firmarReciboSinEntrega(String inspeccionId, {required String firma}) {
    recibosSinEntrega.add((inspeccionId, firma));
    return _paso('firmarReciboSinEntrega');
  }

  /// Cada firma de una parada que salió, con todo lo que llevaba. `motivoCodigo` es null en la firma de una parte y
  /// trae el motivo en el cierre con una sola firma.
  final List<EntregaEnviada> entregasEnviadas = [];

  /// Quién recibió de fuera en cada entrega que salió: (nombre, cédula, su firma). Vacía si recibió un usuario.
  final List<(String?, String?, String?)> externosEnviados = [];

  @override
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
  }) {
    entregasEnviadas.add((
      viajeId: viajeId,
      lugarId: lugarId,
      firma: firma,
      motivoCodigo: null,
      motivoTexto: null,
      reservas: reservas,
      rechazos: rechazos,
      fotos: const [],
      testigo: (null, null),
      momento: (fecha: fechaMomento, lat: lat, lng: lng, precision: precisionM),
    ));
    externosEnviados.add((recibeNombre, recibeCedula, firmaRecibe));
    return _paso('firmarEntregaPunto');
  }

  /// Cada «No hay inspector» de una parada que salió (S8-HU-05.2 CA-2), con su motivo, su hora y su GPS.
  final List<ActivacionEnviada> activacionesEnviadas = [];

  @override
  Future<void> activarSinInspector(
    String viajeId,
    String lugarId, {
    required String motivo,
    String? motivoTexto,
    String? fechaMomento,
    double? lat,
    double? lng,
    double? precisionM,
  }) {
    activacionesEnviadas.add((
      viajeId: viajeId,
      lugarId: lugarId,
      motivo: motivo,
      motivoTexto: motivoTexto,
      momento: (fecha: fechaMomento, lat: lat, lng: lng, precision: precisionM),
    ));
    return _paso('activarSinInspector');
  }

  @override
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
  }) {
    entregasEnviadas.add((
      viajeId: viajeId,
      lugarId: lugarId,
      firma: firma,
      motivoCodigo: motivoCodigo,
      motivoTexto: motivoTexto,
      reservas: const [],
      rechazos: const [],
      fotos: fotos,
      testigo: (testigoNombre, testigoCedula),
      momento: (fecha: fechaMomento, lat: lat, lng: lng, precision: precisionM),
    ));
    return _paso('cerrarEntregaConUnaFirma');
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

  /// Las firmas de la niñera que se enviaron.
  final List<({String viajeId, String parte, String fechaMomento, double lat, double lng, List<ReservaCarro> reservas})>
      firmasNinera = [];

  /// Las constancias de no firma de toda la niñera que se enviaron.
  final List<({String viajeId, String parte, String motivo, String testigoId, FotoLocal foto})> constanciasNinera = [];

  @override
  Future<void> firmarCargueNinera(
    String viajeId, {
    required String parte,
    required String firma,
    required String fechaMomento,
    required double lat,
    required double lng,
    double? precisionM,
    List<ReservaCarro> reservas = const [],
  }) {
    firmasNinera.add((viajeId: viajeId, parte: parte, fechaMomento: fechaMomento, lat: lat, lng: lng, reservas: reservas));
    return _paso('firmarCargueNinera');
  }

  @override
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
  }) {
    constanciasNinera.add((viajeId: viajeId, parte: parte, motivo: motivo, testigoId: testigoId, foto: foto));
    return _paso('registrarNoFirmaNinera');
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

/// Lo que viajó en una firma de parada (S8-HU-04.2), tal como lo recibió el doble del servidor.
typedef EntregaEnviada = ({
  String viajeId,
  String lugarId,
  String? firma,
  String? motivoCodigo,
  String? motivoTexto,
  List<ReservaCarro> reservas,
  List<RechazoCarro> rechazos,
  List<({String vin, FotoLocal foto})> fotos,
  (String?, String?) testigo,
  ({String? fecha, double? lat, double? lng, double? precision}) momento,
});

/// Lo que llevó una activación de «No hay inspector» en una parada.
typedef ActivacionEnviada = ({
  String viajeId,
  String lugarId,
  String motivo,
  String? motivoTexto,
  ({String? fecha, double? lat, double? lng, double? precision}) momento,
});

/// Lo que viajó en una novedad del viaje (S8-HU-07.3 CA-3), tal como lo recibió el doble del servidor.
typedef NovedadViajeEnviada = ({
  String viajeId,
  String tipo,
  String texto,
  String fecha,
  double? lat,
  double? lng,
  String? storagePath,
  String? idEnvio,
  String? proceso,
  String? lugarId,
});

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
  String? motivoSinInspector,
  String? motivoSinInspectorTexto,
  bool bajoPermiso = false,
  String? corrigeInspeccionId,
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
  motivoSinInspector: motivoSinInspector,
  motivoSinInspectorTexto: motivoSinInspectorTexto,
  bajoPermiso: bajoPermiso,
  corrigeInspeccionId: corrigeInspeccionId,
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
