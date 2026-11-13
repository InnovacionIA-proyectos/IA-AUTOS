import 'package:traza_autos/core/dispositivo/servicios.dart';
import 'package:traza_autos/features/viajes/data/entregas_repositorio.dart';
import 'package:traza_autos/features/viajes/data/lugares_repositorio.dart';
import 'package:traza_autos/features/viajes/data/reclamos_repositorio.dart';
import 'package:traza_autos/features/viajes/data/permisos_repositorio.dart';
import 'package:traza_autos/features/viajes/domain/entrega_punto.dart';
import 'package:traza_autos/features/viajes/domain/ubicacion_lugar.dart';
import 'package:traza_autos/features/viajes/data/viajes_repositorio.dart';
import 'package:traza_autos/features/viajes/domain/pendiente.dart';
import 'package:traza_autos/features/viajes/domain/permiso_sin_inspector.dart';
import 'package:traza_autos/features/viajes/domain/viaje.dart';

Viaje viaje(
  String id, {
  List<String> vines = const [],
  String codigo = 'MTK-1',
  String placa = 'ABC123',
  String? trailer,
  String? unidad = 'Niñera',
  String? conductor,
  String estado = 'en_curso',
  Map<String, int> posiciones = const {},
  bool cargueTieneInspector = false,
}) =>
    Viaje(
      id: id,
      codigoMtrack: codigo,
      placa: placa,
      estado: estado,
      unidad: unidad,
      trailer: trailer,
      conductorNombre: conductor,
      cargueTieneInspector: cargueTieneInspector,
      vehiculos: [for (final v in vines) ViajeVehiculo(vin: v, marca: 'Renault', modelo: 'Kwid', posicion: posiciones[v])],
    );

class ViajesRepositorioFalso implements ViajesRepositorio {
  List<Viaje>? guardados;
  List<Viaje> remotos = const [];
  Object? error;
  int llamadasRemotas = 0;

  /// Lo que devuelve `deLaEtapa`, y con qué se la llamó: la captura no debe preguntar cuando la etapa no tiene
  /// viaje.
  Viaje? viajeDeEtapa;
  Object? errorDeLaEtapa;
  final List<String> etapasConsultadas = [];

  /// Lo que responde `marcarNoSale`: el viaje ya marcado, o el error (409) que el servidor devolvería.
  Viaje? viajeTrasNoSale;
  Object? errorNoSale;
  final List<Map<String, Object?>> noSalesMarcados = [];

  @override
  Future<List<Viaje>?> leerCache(String usuarioId) async => guardados;

  @override
  Future<List<Viaje>> listarRemoto(String usuarioId) async {
    llamadasRemotas++;
    if (error != null) throw error!;
    guardados = remotos;
    return remotos;
  }

  /// Los viajes ya terminados: vacíos salvo que la prueba los ponga.
  List<Viaje> finalizados = const [];

  @override
  Future<List<Viaje>> terminados() async => finalizados;

  @override
  Future<Viaje?> deLaEtapa(String usuarioId, String viajeId) async {
    etapasConsultadas.add(viajeId);
    if (errorDeLaEtapa != null) throw errorDeLaEtapa!;
    return viajeDeEtapa;
  }

  @override
  Future<Viaje> marcarNoSale(
    String viajeId,
    String vin, {
    required String motivo,
    required String hallazgoId,
    String? vinReemplazo,
  }) async {
    noSalesMarcados.add({
      'viaje_id': viajeId,
      'vin': vin,
      'motivo': motivo,
      'hallazgo_id': hallazgoId,
      'vin_reemplazo': vinReemplazo,
    });
    if (errorNoSale != null) throw errorNoSale!;
    return viajeTrasNoSale ?? viajeDeEtapa!;
  }
}

/// Los pendientes del inspector de mentira: lo que devuelve cada consulta, y lo que se reclamó o liberó.
class ReclamosRepositorioFalso implements ReclamosRepositorio {
  List<Pendiente> pendientesActuales = const [];

  /// Lo que responde la lista tras la siguiente consulta, para simular que otro inspector se adelantó.
  List<Pendiente>? despuesDeConsultar;
  Object? errorPendientes;
  Object? errorReclamar;
  Object? errorLiberar;
  Viaje? viajeDelPendiente;
  int consultas = 0;
  final List<(String, String)> reclamados = [];
  final List<(String, String)> liberados = [];

  @override
  Future<List<Pendiente>> pendientes() async {
    consultas++;
    if (errorPendientes != null) throw errorPendientes!;
    return pendientesActuales;
  }

  @override
  Future<void> reclamar(String viajeId, String proceso) async {
    reclamados.add((viajeId, proceso));
    if (errorReclamar != null) {
      if (despuesDeConsultar != null) pendientesActuales = despuesDeConsultar!;
      throw errorReclamar!;
    }
    pendientesActuales = [
      for (final p in pendientesActuales)
        if (p.viajeId == viajeId && p.proceso == proceso)
          Pendiente(
            viajeId: p.viajeId,
            proceso: p.proceso,
            lugar: p.lugar,
            estado: EstadoPendiente.mio,
            unidades: p.unidades,
            codigoMtrack: p.codigoMtrack,
            placa: p.placa,
            fechaProgramada: p.fechaProgramada,
          )
        else
          p,
    ];
  }

  @override
  Future<void> liberar(String viajeId, String proceso) async {
    liberados.add((viajeId, proceso));
    if (errorLiberar != null) throw errorLiberar!;
    pendientesActuales = [
      for (final p in pendientesActuales)
        if (p.viajeId == viajeId && p.proceso == proceso)
          Pendiente(
            viajeId: p.viajeId,
            proceso: p.proceso,
            lugar: p.lugar,
            estado: EstadoPendiente.disponible,
            unidades: p.unidades,
            codigoMtrack: p.codigoMtrack,
            placa: p.placa,
            fechaProgramada: p.fechaProgramada,
          )
        else
          p,
    ];
  }

  @override
  Future<Viaje> viaje(String viajeId) async => viajeDelPendiente ?? viaje2(viajeId);
}

Viaje viaje2(String id) => viaje(id, vines: const ['VINAAAAAAAAAAAAA1', 'VINBBBBBBBBBBBBB2']);

Pendiente pendienteDe(
  String viajeId, {
  String placa = 'ABC123',
  String proceso = 'cargue',
  EstadoPendiente estado = EstadoPendiente.disponible,
  String? por,
  String lugar = 'CEDI Envigado',
}) =>
    Pendiente(
      viajeId: viajeId,
      proceso: proceso,
      lugar: lugar,
      estado: estado,
      unidades: 2,
      codigoMtrack: 'MTK-$viajeId',
      placa: placa,
      reclamadoPorNombre: por,
    );

/// Las entregas por punto de mentira.
class EntregasRepositorioFalso implements EntregasRepositorio {
  List<EntregaPunto> puntos = const [];
  Object? error;
  int consultas = 0;

  @override
  Future<List<EntregaPunto>> delViaje(String viajeId) async {
    consultas++;
    if (error != null) throw error!;
    return puntos;
  }
}

EntregaPunto puntoDe(
  String lugar, {
  List<String> vines = const ['VINAAAAAAAAAAAAA1', 'VINBBBBBBBBBBBBB2'],
  EstadoEntrega estado = EstadoEntrega.pendiente,
  String viajeId = 'v1',
  // El punto corriente tiene inspector asignado y la entrega la firma él: así, quien prueba otra cosa no se
  // encuentra de regalo la pantalla pidiendo nombre y cédula.
  bool tieneInspector = true,
}) =>
    EntregaPunto(
      viajeId: viajeId,
      lugarId: 'l-$lugar',
      lugar: lugar,
      unidades: [for (final v in vines) UnidadEntrega(vin: v, estado: estado)],
      pendientes: estado == EstadoEntrega.cerrada ? 0 : vines.length,
      entregada: estado == EstadoEntrega.cerrada,
      tieneInspector: tieneInspector,
    );

/// La ubicación de los lugares de mentira.
class LugaresRepositorioFalso implements LugaresRepositorio {
  Map<String, EstadoUbicacionLugar> estadosPorLugar = {};
  Object? errorAlConsultar;
  Object? errorAlFijar;
  final fijados = <(String, Posicion)>[];

  @override
  Future<Map<String, EstadoUbicacionLugar>> estados() async {
    if (errorAlConsultar != null) throw errorAlConsultar!;
    return estadosPorLugar;
  }

  @override
  Future<EstadoUbicacionLugar> fijar(String lugarId, Posicion posicion) async {
    if (errorAlFijar != null) throw errorAlFijar!;
    fijados.add((lugarId, posicion));
    estadosPorLugar = {...estadosPorLugar, lugarId: EstadoUbicacionLugar.pendienteConfirmar};
    return EstadoUbicacionLugar.pendienteConfirmar;
  }
}

/// El permiso de la excepción «No hay inspector»: lo que hay y lo que se pidió.
class PermisosRepositorioFalso implements PermisosRepositorio {
  /// Lo que responde la consulta. Null es «este viaje no tiene solicitud», que es el 404 del servidor.
  PermisoSinInspector? permiso;

  /// Lo que lanza al pedirlo: el 409 de un punto que resultó no tener inspectores, por ejemplo.
  Object? errorAlSolicitar;

  final List<Map<String, Object?>> solicitudes = [];
  int consultas = 0;

  @override
  Future<PermisoSinInspector?> delViaje(String viajeId) async {
    consultas++;
    return permiso;
  }

  @override
  Future<PermisoSinInspector> solicitar(
    String viajeId, {
    required String motivo,
    required DateTime fecha,
    String? motivoTexto,
    double? lat,
    double? lng,
  }) async {
    solicitudes.add({'viajeId': viajeId, 'motivo': motivo, 'texto': motivoTexto, 'lat': lat, 'lng': lng});
    if (errorAlSolicitar != null) throw errorAlSolicitar!;
    return permiso ??= permisoDe(viajeId: viajeId, motivoCodigo: motivo, motivoTexto: motivoTexto);
  }
}

PermisoSinInspector permisoDe({
  String viajeId = 'viaje-1',
  bool concedido = false,
  bool porTiempo = false,
  String motivoCodigo = 'otro',
  String? motivoTexto = 'El inspector se enfermó',
  String? concedidoPorNombre,
  Duration falta = const Duration(minutes: 7, seconds: 30),
  DateTime? ahora,
}) {
  final momento = ahora ?? DateTime.now().toUtc();
  return PermisoSinInspector(
    id: 'permiso-1',
    viajeId: viajeId,
    estado: concedido ? EstadoPermiso.concedido : EstadoPermiso.pendiente,
    solicitadoAt: momento,
    venceAt: momento.add(falta),
    motivoCodigo: motivoCodigo,
    lugar: 'CEDI 1',
    motivo: 'Otro',
    motivoTexto: motivoTexto,
    fecha: momento,
    lat: 6.2442,
    lng: -75.5812,
    concedidoAt: concedido ? momento : null,
    concedidoPorNombre: concedidoPorNombre,
    porTiempo: porTiempo,
  );
}
