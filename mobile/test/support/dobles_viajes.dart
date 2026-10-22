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
}) =>
    Viaje(
      id: id,
      codigoMtrack: codigo,
      placa: placa,
      estado: 'en_curso',
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

  @override
  Future<List<Viaje>?> leerCache(String usuarioId) async => guardados;

  @override
  Future<List<Viaje>> listarRemoto(String usuarioId) async {
    llamadasRemotas++;
    if (error != null) throw error!;
    guardados = remotos;
    return remotos;
  }

  @override
  Future<Viaje?> deLaEtapa(String usuarioId, String viajeId) async {
    etapasConsultadas.add(viajeId);
    if (errorDeLaEtapa != null) throw errorDeLaEtapa!;
    return viajeDeEtapa;
  }
}
