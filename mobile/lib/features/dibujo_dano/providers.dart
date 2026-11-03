import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../app/proveedores.dart';
import '../../core/db/cache_lecturas.dart';
import 'data/dibujo_dano_repositorio.dart';

/// Los assets se inyectan para poder darle a las pruebas un `AssetBundle` propio.
final assetsDibujoProvider = Provider<AssetBundle>((ref) => rootBundle);

final almacenImagenesPanelProvider = Provider<AlmacenImagenesPanel>(
  (ref) => AlmacenImagenesEnCache(CacheLecturasRepo(ref.watch(appDatabaseProvider))),
);

final dibujoDanoRepositorioProvider = Provider<DibujoDanoRepositorio>(
  (ref) => DibujoDanoRepositorio(
    assets: ref.watch(assetsDibujoProvider),
    api: ref.watch(clienteApiProvider),
    imagenes: ref.watch(almacenImagenesPanelProvider),
  ),
);
