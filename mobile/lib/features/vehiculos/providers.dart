import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../app/proveedores.dart';
import '../auth/presentation/auth_controller.dart';
import '../viajes/providers.dart';
import 'data/vehiculos_repositorio.dart';
import 'domain/inventario.dart';

final vehiculosRepositorioProvider = Provider<VehiculosRepositorio>(
  (ref) => VehiculosRepositorio(
    ref.watch(clienteApiProvider),
    cache: ref.watch(cacheViajesProvider),
    usuarioId: () => ref.read(authControllerProvider).sesion?.usuario.id,
  ),
);

/// Los carros que coinciden con lo escrito, preguntando al servidor.
///
/// Desde tres caracteres: con menos, cada tecla sería una petición y la respuesta no acotaría nada. Se descarta al
/// salir de la pantalla porque es una búsqueda, no un dato que valga la pena conservar.
final busquedaVehiculosProvider =
    FutureProvider.autoDispose.family<List<VehiculoBusqueda>, String>((ref, termino) async {
  final limpio = termino.trim();
  if (limpio.length < 3) return const [];
  return ref.watch(vehiculosRepositorioProvider).buscar(limpio);
});
