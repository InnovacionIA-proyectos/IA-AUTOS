import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../app/proveedores.dart';
import '../../core/db/cache_lecturas.dart';
import 'data/catalogos_repositorio.dart';

final cacheCatalogosProvider = Provider<CacheLecturasRepo>((ref) => CacheLecturasRepo(ref.watch(appDatabaseProvider)));

final catalogosRepositorioProvider = Provider<CatalogosRepositorio>(
  (ref) => CatalogosRepositorio(ref.watch(clienteApiProvider), ref.watch(cacheCatalogosProvider)),
);
