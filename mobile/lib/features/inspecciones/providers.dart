import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../app/proveedores.dart';
import '../../core/db/cache_lecturas.dart';
import '../vehiculos/providers.dart';
import 'data/hallazgos_previos_repositorio.dart';
import 'data/inspecciones_repositorio.dart';
import 'data/novedades_repositorio.dart';
import 'domain/novedad_pendiente.dart';

final cacheLecturasProvider = Provider<CacheLecturasRepo>((ref) => CacheLecturasRepo(ref.watch(appDatabaseProvider)));

final inspeccionesRepositorioProvider = Provider<InspeccionesRepositorio>(
  (ref) => InspeccionesRepositorio(ref.watch(clienteApiProvider), ref.watch(cacheLecturasProvider)),
);

final hallazgosPreviosRepositorioProvider = Provider<HallazgosPreviosRepositorio>(
  (ref) => HallazgosPreviosRepositorio(ref.watch(vehiculosRepositorioProvider), ref.watch(cacheLecturasProvider)),
);

final novedadesRepositorioProvider =
    Provider<NovedadesRepositorio>((ref) => NovedadesRepositorio(ref.watch(clienteApiProvider)));

/// Las novedades del conductor pendientes de revisión en un VIN. Se recarga al revisar una.
final novedadesPorRevisarProvider = FutureProvider.autoDispose.family<List<NovedadPendiente>, String>(
  (ref, vin) => ref.watch(novedadesRepositorioProvider).porRevisar(vin: vin),
);
