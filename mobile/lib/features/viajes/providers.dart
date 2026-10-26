import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../app/proveedores.dart';
import '../../core/db/cache_lecturas.dart';
import 'data/viajes_repositorio.dart';
import 'domain/viaje.dart';
import '../auth/presentation/auth_controller.dart';

final cacheViajesProvider = Provider<CacheLecturasRepo>((ref) => CacheLecturasRepo(ref.watch(appDatabaseProvider)));

final viajesRepositorioProvider = Provider<ViajesRepositorio>(
  (ref) => ViajesRepositorio(ref.watch(clienteApiProvider), ref.watch(cacheViajesProvider)),
);

/// El viaje de una etapa. La captura lo observa para mostrar placa, tráiler, unidad y conductor.
final viajeDeEtapaProvider = FutureProvider.autoDispose.family<Viaje?, String>((ref, viajeId) async {
  final usuarioId = ref.watch(authControllerProvider).sesion?.usuario.id;
  if (usuarioId == null) return null;
  return ref.watch(viajesRepositorioProvider).deLaEtapa(usuarioId, viajeId);
});
