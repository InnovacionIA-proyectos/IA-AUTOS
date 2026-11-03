import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../app/proveedores.dart';
import '../auth/presentation/auth_controller.dart';
import '../viajes/providers.dart';
import 'data/vehiculos_repositorio.dart';

final vehiculosRepositorioProvider = Provider<VehiculosRepositorio>(
  (ref) => VehiculosRepositorio(
    ref.watch(clienteApiProvider),
    cache: ref.watch(cacheViajesProvider),
    usuarioId: () => ref.read(authControllerProvider).sesion?.usuario.id,
  ),
);
