import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../app/proveedores.dart';
import '../../core/db/cache_lecturas.dart';
import 'data/entregas_repositorio.dart';
import 'data/lugares_repositorio.dart';
import 'data/reclamos_repositorio.dart';
import 'data/viajes_repositorio.dart';
import 'domain/entrega_punto.dart';
import 'domain/pendiente.dart';
import 'domain/ubicacion_lugar.dart';
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

/// Mis viajes ya terminados. No se guardan: solo se consultan con señal.
final viajesTerminadosProvider = FutureProvider.autoDispose<List<Viaje>>((ref) async {
  if (ref.watch(authControllerProvider).sesion?.usuario.id == null) return const [];
  return ref.watch(viajesRepositorioProvider).terminados();
});

final reclamosRepositorioProvider = Provider<ReclamosRepositorio>(
  (ref) => ReclamosRepositorio(ref.watch(clienteApiProvider)),
);

/// Los camiones pendientes de los puntos del inspector. Se consulta con señal: quién tiene cada camión lo decide el
/// servidor y una copia vieja diría algo falso.
final pendientesProvider = FutureProvider.autoDispose<List<Pendiente>>(
  (ref) => ref.watch(reclamosRepositorioProvider).pendientes(),
);

/// El viaje de un pendiente, con sus unidades.
final viajeDePendienteProvider = FutureProvider.autoDispose.family<Viaje, String>(
  (ref, viajeId) => ref.watch(reclamosRepositorioProvider).viaje(viajeId),
);

final entregasRepositorioProvider = Provider<EntregasRepositorio>(
  (ref) => EntregasRepositorio(ref.watch(clienteApiProvider)),
);

/// Los puntos de entrega de un viaje. Se consulta con señal: lo que está pendiente lo dice el servidor.
final entregasDelViajeProvider = FutureProvider.autoDispose.family<List<EntregaPunto>, String>(
  (ref, viajeId) => ref.watch(entregasRepositorioProvider).delViaje(viajeId),
);

final lugaresRepositorioProvider = Provider<LugaresRepositorio>(
  (ref) => LugaresRepositorio(ref.watch(clienteApiProvider)),
);

/// Cómo está la ubicación de cada lugar. Si no se puede consultar, la pantalla no ofrece fijarla.
final estadosUbicacionProvider = FutureProvider.autoDispose<Map<String, EstadoUbicacionLugar>>(
  (ref) => ref.watch(lugaresRepositorioProvider).estados(),
);
