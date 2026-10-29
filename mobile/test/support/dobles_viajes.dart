import 'package:traza_autos/features/viajes/data/reclamos_repositorio.dart';
import 'package:traza_autos/features/viajes/data/viajes_repositorio.dart';
import 'package:traza_autos/features/viajes/domain/pendiente.dart';
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
}) =>
    Viaje(
      id: id,
      codigoMtrack: codigo,
      placa: placa,
      estado: estado,
      unidad: unidad,
      trailer: trailer,
      conductorNombre: conductor,
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
