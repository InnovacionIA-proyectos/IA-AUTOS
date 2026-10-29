import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../auth/presentation/auth_controller.dart';
import '../../sync/presentation/sync_controller.dart';
import '../domain/viaje.dart';
import '../../vehiculos/providers.dart';
import '../providers.dart';

enum FaseViajes { cargando, listo, error, sinConexion }

class EstadoViajes {
  const EstadoViajes(this.fase, [this.viajes = const []]);

  final FaseViajes fase;
  final List<Viaje> viajes;

  /// Todos los VIN de mis viajes, que es el universo en el que se puede buscar.
  Iterable<(Viaje, ViajeVehiculo)> get vehiculos sync* {
    for (final viaje in viajes) {
      for (final vehiculo in viaje.vehiculos) {
        yield (viaje, vehiculo);
      }
    }
  }

  /// Unidades que coinciden con el texto, por VIN, marca, modelo o código del viaje: en el patio se busca por lo
  /// que se tiene a mano, que no siempre es el VIN completo. Con el texto vacío devuelve todas.
  ///
  /// Nunca sale de mis viajes: un VIN ajeno no se puede abrir, la API responde 403.
  List<(Viaje, ViajeVehiculo)> buscar(String texto) {
    final buscado = texto.trim().toUpperCase();
    final todos = vehiculos.toList();
    if (buscado.isEmpty) return todos;
    return [
      for (final (viaje, vehiculo) in todos)
        if ('${vehiculo.vin} ${vehiculo.marca ?? ''} ${vehiculo.modelo ?? ''} ${viaje.codigoMtrack}'
            .toUpperCase()
            .contains(buscado))
          (viaje, vehiculo),
    ];
  }
}

/// Mis viajes: la entrada del trabajo de campo.
///
/// Muestra primero la copia guardada y refresca después, igual que el listado de inspecciones: sin señal se
/// conserva lo último descargado y no se borra nada.
class MisViajesController extends Notifier<EstadoViajes> {
  @override
  EstadoViajes build() => const EstadoViajes(FaseViajes.cargando);

  Future<void> cargar() async {
    final usuarioId = ref.read(authControllerProvider).sesion?.usuario.id;
    if (usuarioId == null) return;
    final repositorio = ref.read(viajesRepositorioProvider);
    final online = ref.read(syncControllerProvider).online;

    final cache = await repositorio.leerCache(usuarioId);
    if (cache != null) {
      state = EstadoViajes(FaseViajes.listo, cache);
    } else if (!online) {
      state = const EstadoViajes(FaseViajes.sinConexion);
      return;
    } else {
      state = const EstadoViajes(FaseViajes.cargando);
    }
    if (!online) return;

    try {
      final viajes = await repositorio.listarRemoto(usuarioId);
      state = EstadoViajes(FaseViajes.listo, viajes);
      if (ref.read(authControllerProvider).sesion?.usuario.rol == 'conductor') await _descargarInventarios(viajes);
    } catch (_) {
      if (cache == null) state = const EstadoViajes(FaseViajes.error);
    }
  }

  /// El conductor mira en lectura los hallazgos de su viaje aunque después no haya señal: por eso, al abrir con señal,
  /// se baja el inventario de cada unidad y queda guardado. Lo que no se pueda bajar se salta: la lista ya se ve.
  Future<void> _descargarInventarios(List<Viaje> viajes) async {
    final repositorio = ref.read(vehiculosRepositorioProvider);
    for (final viaje in viajes) {
      for (final unidad in viaje.vehiculos) {
        try {
          await repositorio.inventario(unidad.vin);
        } catch (_) {
          continue;
        }
      }
    }
  }
}

final misViajesControllerProvider = NotifierProvider<MisViajesController, EstadoViajes>(MisViajesController.new);
