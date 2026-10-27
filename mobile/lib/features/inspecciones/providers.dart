import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../app/proveedores.dart';
import '../../core/db/cache_lecturas.dart';
import 'data/inspecciones_repositorio.dart';

final cacheLecturasProvider = Provider<CacheLecturasRepo>((ref) => CacheLecturasRepo(ref.watch(appDatabaseProvider)));

final inspeccionesRepositorioProvider = Provider<InspeccionesRepositorio>(
  (ref) => InspeccionesRepositorio(ref.watch(clienteApiProvider), ref.watch(cacheLecturasProvider)),
);
