import 'package:traza_autos/features/viajes/data/viajes_repositorio.dart';
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
}) =>
    Viaje(
      id: id,
      codigoMtrack: codigo,
      placa: placa,
      estado: estado,
      unidad: unidad,
      trailer: trailer,
      conductorNombre: conductor,
      vehiculos: [for (final v in vines) ViajeVehiculo(vin: v, marca: 'Renault', modelo: 'Kwid')],
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
