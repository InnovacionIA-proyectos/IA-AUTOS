import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../auth/presentation/auth_controller.dart';
import '../../sync/presentation/sync_controller.dart';
import '../domain/inspeccion_resumen.dart';
import '../providers.dart';

enum FaseLista { cargando, listo, error, sinConexion }

class EstadoLista {
  const EstadoLista(this.fase, [this.items = const []]);

  final FaseLista fase;
  final List<InspeccionResumen> items;
}

/// Carga el listado mostrando primero la caché local (sirve sin señal) y refrescando después si hay conexión.
/// Sin conexión se conserva lo cacheado; no se borra la lista.
class ListaInspeccionesController extends Notifier<EstadoLista> {
  @override
  EstadoLista build() => const EstadoLista(FaseLista.cargando);

  Future<void> cargar() async {
    final usuarioId = ref.read(authControllerProvider).sesion?.usuario.id;
    if (usuarioId == null) return;
    final repo = ref.read(inspeccionesRepositorioProvider);
    final online = ref.read(syncControllerProvider).online;

    final cache = await repo.leerCache(usuarioId);
    if (cache != null) {
      state = EstadoLista(FaseLista.listo, cache);
    } else if (!online) {
      state = const EstadoLista(FaseLista.sinConexion);
      return;
    } else {
      state = const EstadoLista(FaseLista.cargando);
    }
    if (!online) return;

    try {
      state = EstadoLista(FaseLista.listo, await repo.listarRemoto(usuarioId));
    } catch (_) {
      if (cache == null) state = const EstadoLista(FaseLista.error);
    }
  }
}

final listaInspeccionesControllerProvider = NotifierProvider<ListaInspeccionesController, EstadoLista>(ListaInspeccionesController.new);
