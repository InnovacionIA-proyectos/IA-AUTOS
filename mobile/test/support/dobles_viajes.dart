import 'dart:async';

import 'package:traza_autos/core/dispositivo/servicios.dart';
import 'package:traza_autos/features/sync/domain/job.dart';
import 'package:traza_autos/features/viajes/data/entregas_repositorio.dart';
import 'package:traza_autos/features/viajes/data/lugares_repositorio.dart';
import 'package:traza_autos/features/viajes/data/reclamos_repositorio.dart';
import 'package:traza_autos/features/viajes/data/permisos_repositorio.dart';
import 'package:traza_autos/features/viajes/domain/entrega_punto.dart';
import 'package:traza_autos/features/viajes/domain/espera_del_punto.dart';
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
  /// S8-HU-07.3 CA-6: si el lugar de cargue admite cargue sin inspector. Falso como en todos los lugares de hoy.
  bool admiteSinInspector = false,
  String? inspectorId,
  String? conductorId,
  String? lugarCargueId,
  String? muelleId,
  String? muelle,
  bool cargueCerrado = false,
  int fotosAcomodacion = 0,
  Map<String, String> destinos = const {},
  Map<String, DateTime> destinosCambiados = const {},
  /// El `estado_unidad` de cada VIN que no esté activo (`no_sale`, `bajado`), y el «no sale» de cada uno.
  Map<String, String> estados = const {},
  Map<String, NoSale> noSales = const {},
  String? papel,
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
      cargueAdmiteSinInspector: admiteSinInspector,
      inspectorId: inspectorId,
      conductorId: conductorId,
      lugarCargueId: lugarCargueId,
      muelleId: muelleId,
      muelle: muelle,
      cargueCerrado: cargueCerrado,
      fotosAcomodacion: fotosAcomodacion,
      papel: papel,
      vehiculos: [
        for (final v in vines)
          ViajeVehiculo(
            vin: v,
            marca: 'Renault',
            modelo: 'Kwid',
            posicion: posiciones[v],
            estadoUnidad: estados[v] ?? estadoUnidadActiva,
            noSale: noSales[v],
            lugarDestino: destinos[v],
            destinoCambiadoAt: destinosCambiados[v],
          ),
      ],
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

  /// Lo que responde «cambiar el cargue» (el viaje ya cambiado) o el error que el servidor devolvería, y con qué
  /// se la llamó.
  Viaje? viajeTrasCambiarCargue;
  Object? errorCambiarCargue;
  final List<Map<String, Object?>> carguesCambiados = [];

  @override
  Future<Viaje> cambiarCargue(String viajeId, {String? muelleId, bool quitarMuelle = false, String? trailer}) async {
    carguesCambiados.add({
      'viaje_id': viajeId,
      if (muelleId != null || quitarMuelle) 'muelle_id': muelleId,
      'trailer': ?trailer,
    });
    if (errorCambiarCargue != null) throw errorCambiarCargue!;
    if (viajeTrasCambiarCargue != null) viajeDeEtapa = viajeTrasCambiarCargue;
    return viajeTrasCambiarCargue ?? viajeDeEtapa!;
  }

  /// El PDF de la niñera: lo que responde, el error que lanza y cuántas veces se pidió.
  List<int> pdf = const [37, 80, 68, 70];
  Object? errorPdf;
  int pdfsPedidos = 0;

  @override
  Future<List<int>> pdfNinera(String viajeId) async {
    pdfsPedidos++;
    if (errorPdf != null) throw errorPdf!;
    return pdf;
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
    required String motivoCodigo,
    String? motivoTexto,
    String? hallazgoId,
    String? vinReemplazo,
  }) async {
    noSalesMarcados.add({
      'viaje_id': viajeId,
      'vin': vin,
      'motivo_codigo': motivoCodigo,
      'motivo_texto': motivoTexto,
      'hallazgo_id': hallazgoId,
      'vin_reemplazo': vinReemplazo,
    });
    if (errorNoSale != null) throw errorNoSale!;
    if (viajeTrasNoSale != null) viajeDeEtapa = viajeTrasNoSale;
    return viajeTrasNoSale ?? viajeDeEtapa!;
  }

  /// Lo que responde «vuelve a salir»: el viaje con el carro activo otra vez, o el 409 del servidor.
  Viaje? viajeTrasVolver;
  Object? errorVolver;
  final List<(String, String)> vueltasASalir = [];

  @override
  Future<Viaje> vuelveASalir(String viajeId, String vin) async {
    vueltasASalir.add((viajeId, vin));
    if (errorVolver != null) throw errorVolver!;
    if (viajeTrasVolver != null) viajeDeEtapa = viajeTrasVolver;
    return viajeTrasVolver ?? viajeDeEtapa!;
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

  /// Lo que lanza al pedir el viaje suelto: un 403 o un 404 del servidor, o la falta de señal.
  Object? errorViaje;

  /// Si está puesto, el reclamo no responde hasta que se complete: como un GPS o una red lentos.
  Completer<void>? esperaAlReclamar;
  int consultas = 0;
  final List<(String, String)> reclamados = [];
  final List<(String, String)> liberados = [];

  /// La parada que se mandó en cada reclamo y en cada liberación, en el mismo orden (S8-HU-04.2 CA-10).
  final List<String?> paradasReclamadas = [];
  final List<String?> paradasLiberadas = [];

  /// El GPS que se mandó en cada reclamo, en el mismo orden; null si salió sin ubicación (S8-HU-09.2 CA-4).
  final List<Posicion?> posicionesReclamadas = [];

  /// Lo que pasa en el servidor tras un reclamo que sale bien: tomar el cargue hace al inspector inspector del viaje.
  void Function(String viajeId, String proceso)? alReclamar;

  @override
  Future<List<Pendiente>> pendientes() async {
    consultas++;
    if (errorPendientes != null) throw errorPendientes!;
    return pendientesActuales;
  }

  @override
  Future<void> reclamar(String viajeId, String proceso, {String? lugarId, Posicion? posicion}) async {
    reclamados.add((viajeId, proceso));
    paradasReclamadas.add(lugarId);
    posicionesReclamadas.add(posicion);
    await esperaAlReclamar?.future;
    if (errorReclamar != null) {
      if (despuesDeConsultar != null) pendientesActuales = despuesDeConsultar!;
      throw errorReclamar!;
    }
    alReclamar?.call(viajeId, proceso);
    pendientesActuales = [
      for (final p in pendientesActuales)
        if (p.viajeId == viajeId && p.proceso == proceso && (lugarId == null || p.lugarId == lugarId))
          Pendiente(
            viajeId: p.viajeId,
            proceso: p.proceso,
            lugar: p.lugar,
            lugarId: p.lugarId,
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
  Future<void> liberar(String viajeId, String proceso, {String? lugarId}) async {
    liberados.add((viajeId, proceso));
    paradasLiberadas.add(lugarId);
    if (errorLiberar != null) throw errorLiberar!;
    pendientesActuales = [
      for (final p in pendientesActuales)
        if (p.viajeId == viajeId && p.proceso == proceso && (lugarId == null || p.lugarId == lugarId))
          Pendiente(
            viajeId: p.viajeId,
            proceso: p.proceso,
            lugar: p.lugar,
            lugarId: p.lugarId,
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
  Future<Viaje> viaje(String viajeId) async {
    if (errorViaje != null) throw errorViaje!;
    return viajeDelPendiente ?? viaje2(viajeId);
  }
}

Viaje viaje2(String id) => viaje(id, vines: const ['VINAAAAAAAAAAAAA1', 'VINBBBBBBBBBBBBB2']);

Pendiente pendienteDe(
  String viajeId, {
  String placa = 'ABC123',
  String proceso = 'cargue',
  EstadoPendiente estado = EstadoPendiente.disponible,
  String? por,
  String lugar = 'CEDI Envigado',
  String? lugarId,
}) =>
    Pendiente(
      viajeId: viajeId,
      proceso: proceso,
      lugar: lugar,
      // Cada pendiente con su lugar: en la entrega es la parada, que es lo que se toma y se libera.
      lugarId: lugarId ?? 'l-$lugar',
      estado: estado,
      unidades: 2,
      codigoMtrack: 'MTK-$viajeId',
      placa: placa,
      reclamadoPorNombre: por,
    );

/// Las entregas por parada de mentira.
class EntregasRepositorioFalso implements EntregasRepositorio {
  List<EntregaPunto> puntos = const [];
  int unidadesSinDestino = 0;
  Object? error;
  int consultas = 0;

  /// La revisión de llegada de cada carro, por id de la inspección. Sin ella, cerrada y sin daños con foto.
  Map<String, RevisionDeLlegada> revisiones = {};
  Object? errorRevision;
  final List<String> revisionesConsultadas = [];

  @override
  Future<EntregasDelViaje> delViaje(String viajeId) async {
    consultas++;
    if (error != null) throw error!;
    return EntregasDelViaje(puntos: puntos, unidadesSinDestino: unidadesSinDestino);
  }

  @override
  Future<EntregaPunto> deParada(String viajeId, String lugarId) async {
    consultas++;
    if (error != null) throw error!;
    return puntos.firstWhere((p) => p.lugarId == lugarId);
  }

  /// Las activaciones que la pantalla anotó en la copia sin señal de la parada.
  final List<SinInspectorParadaJob> anotadas = [];

  @override
  Future<void> anotarActivacion(SinInspectorParadaJob activacion) async => anotadas.add(activacion);

  @override
  Future<RevisionDeLlegada> revisionDeLlegada(String inspeccionId) async {
    revisionesConsultadas.add(inspeccionId);
    if (errorRevision != null) throw errorRevision!;
    return revisiones[inspeccionId] ?? const RevisionDeLlegada(cerrada: true);
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
  ReceptorParada? receptor,
  String? receptorNombre,
  bool conductorLlego = true,
  EstadoParada? estadoParada,
  Map<String, EstadoEntrega> estados = const {},
  Map<String, String> reservas = const {},
  Set<String> entregaFirmada = const {},
  Set<String> recibeFirmada = const {},
  // S8-HU-05.2: la espera del inspector del punto, sus plazos y los daños que el conductor dejó sin nivel.
  EsperaDelPunto? espera,
  int avisoMinutos = 15,
  int planBMinutos = 30,
  List<DanoPorCalificar> danosPorCalificar = const [],
}) =>
    EntregaPunto(
      viajeId: viajeId,
      lugarId: 'l-$lugar',
      lugar: lugar,
      unidades: [
        for (final v in vines)
          UnidadEntrega(
            vin: v,
            estado: estados[v] ?? estado,
            // La revisión de llegada de cada carro se llama como él: así las pruebas le ponen daños por VIN.
            inspeccionId: (estados[v] ?? estado) == EstadoEntrega.sinInspeccion ? null : 'ins-$v',
            reservas: reservas[v],
            entregaFirmada: entregaFirmada.contains(v),
            recibeFirmada: recibeFirmada.contains(v),
          ),
      ],
      pendientes: estado == EstadoEntrega.cerrada ? 0 : vines.length,
      entregada: estado == EstadoEntrega.cerrada,
      tieneInspector: tieneInspector,
      receptorDelServidor: receptor,
      receptorNombre: receptorNombre,
      conductorLlego: conductorLlego,
      estadoDelServidor: estadoParada,
      espera: espera,
      esperaAvisoMinutos: avisoMinutos,
      esperaPlanBMinutos: planBMinutos,
      danosPorCalificar: danosPorCalificar,
    );

/// La ubicación de los lugares de mentira.
class LugaresRepositorioFalso implements LugaresRepositorio {
  Map<String, EstadoUbicacionLugar> estadosPorLugar = {};
  Object? errorAlConsultar;
  Object? errorAlFijar;
  final fijados = <(String, Posicion)>[];

  /// Cuántas veces se preguntó por el estado de los lugares.
  int consultas = 0;

  @override
  Future<Map<String, EstadoUbicacionLugar>> estados() async {
    consultas++;
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
