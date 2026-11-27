import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../app/proveedores.dart';
import '../../core/db/cache_lecturas.dart';
import '../../core/errores/error_api.dart';
import 'data/entregas_repositorio.dart';
import 'data/lugares_repositorio.dart';
import 'data/permisos_repositorio.dart';
import 'data/reclamos_repositorio.dart';
import 'data/viajes_repositorio.dart';
import 'domain/entrega_punto.dart';
import 'domain/pendiente.dart';
import 'domain/permiso_sin_inspector.dart';
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

/// Si ya llegó [momento]. Quien lo mira se vuelve a pintar justo al llegar, con un solo temporizador y no con un
/// segundero: así el botón de la ficha cambia a «Continuar el cargue» cuando la cuenta del permiso llega a cero
/// (S8-HU-09.2 CA-2) sin despertar la pantalla cada segundo.
final cumplidoProvider = Provider.autoDispose.family<bool, DateTime>((ref, momento) {
  final falta = momento.difference(DateTime.now());
  if (falta <= Duration.zero) return true;
  final temporizador = Timer(falta, ref.invalidateSelf);
  ref.onDispose(temporizador.cancel);
  return false;
});

/// El viaje al que lleva un aviso (S8-HU-09.2 CA-6): del servidor (`GET /api/viajes/{id}`) y, solo si no hubo
/// respuesta, de la copia guardada. A diferencia del viaje de la etapa, un 403 o un 404 no se esconden detrás de la
/// copia: que el viaje no sea de quien mira, o que ya no exista, no se arregla con señal y hay que decirlo así.
final viajeDelAvisoProvider = FutureProvider.autoDispose.family<Viaje, String>((ref, viajeId) async {
  try {
    return await ref.watch(reclamosRepositorioProvider).viaje(viajeId);
  } on ErrorApi catch (e) {
    if (e.status != 0) rethrow;
    final usuarioId = ref.watch(authControllerProvider.select((s) => s.sesion?.usuario.id));
    final copia = usuarioId == null ? null : await ref.watch(viajesRepositorioProvider).leerCache(usuarioId);
    return copia?.where((v) => v.id == viajeId).firstOrNull ?? (throw e);
  }
});

final entregasRepositorioProvider = Provider<EntregasRepositorio>(
  (ref) => EntregasRepositorio(
    ref.watch(clienteApiProvider),
    // Perezosos: la base solo se abre cuando hay una parada que guardar o no hubo señal.
    copias: () => ref.read(cacheViajesProvider),
    usuarioActual: () => ref.read(authControllerProvider).sesion?.usuario.id,
  ),
);

/// La hora del teléfono. El reloj de la espera del inspector del punto la lee aquí para que las pruebas la fijen
/// (S8-HU-05.2 CA-3).
final horaDelTelefonoProvider = Provider<DateTime Function()>((ref) => DateTime.now);

/// Un pulso cada 30 segundos, para repintar los minutos que faltan donde no hay otro temporizador (S8-HU-05.2 CA-9).
final tictacProvider = StreamProvider.autoDispose<int>((ref) => Stream.periodic(const Duration(seconds: 30), (i) => i));

/// Las paradas de un viaje. Se consulta con señal: lo que está pendiente lo dice el servidor.
final entregasDelViajeProvider = FutureProvider.autoDispose.family<EntregasDelViaje, String>(
  (ref, viajeId) => ref.watch(entregasRepositorioProvider).delViaje(viajeId),
);

/// Una parada del viaje: lo que abre «Entregar en esta parada» (S8-HU-04.2). Sin señal, la copia guardada de la
/// última vez (S8-HU-05.2 CA-3).
final entregaDeParadaProvider = FutureProvider.autoDispose.family<EntregaPunto, ({String viajeId, String lugarId})>(
  (ref, parada) => ref.watch(entregasRepositorioProvider).deParada(parada.viajeId, parada.lugarId),
);

/// La revisión de llegada de un carro de la parada: si está cerrada y sus daños con foto (S8-HU-04.2 CA-4).
final revisionDeLlegadaProvider = FutureProvider.autoDispose.family<RevisionDeLlegada, String>(
  (ref, inspeccionId) => ref.watch(entregasRepositorioProvider).revisionDeLlegada(inspeccionId),
);

final permisosRepositorioProvider = Provider<PermisosRepositorio>(
  (ref) => PermisosRepositorio(ref.watch(clienteApiProvider)),
);

/// El permiso de la excepción «No hay inspector» de un viaje, o null si no se ha pedido.
///
/// Se consulta con señal y cada vez que se entra: quien concede es el servidor —un coordinador o el reloj—, así
/// que el estado de verdad vive allá y no en esta pantalla.
final permisoSinInspectorProvider = FutureProvider.autoDispose.family<PermisoSinInspector?, String>(
  (ref, viajeId) => ref.watch(permisosRepositorioProvider).delViaje(viajeId),
);

final lugaresRepositorioProvider = Provider<LugaresRepositorio>(
  (ref) => LugaresRepositorio(ref.watch(clienteApiProvider)),
);

/// Cómo está la ubicación de cada lugar. Si no se puede consultar, la pantalla no ofrece fijarla.
final estadosUbicacionProvider = FutureProvider.autoDispose<Map<String, EstadoUbicacionLugar>>(
  (ref) => ref.watch(lugaresRepositorioProvider).estados(),
);
