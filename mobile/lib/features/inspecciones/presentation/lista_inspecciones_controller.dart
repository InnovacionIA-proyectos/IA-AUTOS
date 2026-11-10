import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/errores/error_api.dart';
import '../../auth/presentation/auth_controller.dart';
import '../../sync/presentation/sync_controller.dart';
import '../domain/inspeccion_resumen.dart';
import '../providers.dart';

enum FaseLista { cargando, listo, error, sinConexion }

class EstadoLista {
  const EstadoLista(this.fase, [this.items = const [], this.mensaje]);

  final FaseLista fase;
  final List<InspeccionResumen> items;

  /// Por qué no se pudo cargar, cuando la fase es `error`. Va aquí y no fijo en la pantalla porque no siempre es
  /// la red: una respuesta que la app no supo leer también cae en esta fase, y decir «revisa tu conexión» con la
  /// API respondiendo 200 manda a buscar el problema donde no está.
  final String? mensaje;
}

/// Carga el listado mostrando primero la caché local (sirve sin señal) y refrescando después si hay conexión.
/// Sin conexión se conserva lo cacheado; no se borra la lista.
class ListaInspeccionesController extends Notifier<EstadoLista> {
  @override
  EstadoLista build() {
    // Cuando la cola entrega algo, lo que está en pantalla quedó viejo: el informe recién enviado no aparecía
    // hasta que alguien bajaba a refrescar, y una corrección dejaba viéndose la versión anterior.
    // Solo cuando sube: el contador se reinicia si la cola se rearma, y recargar por eso no aporta nada.
    ref.listen(entregadosProvider, (anterior, actual) {
      if (anterior != null && actual > anterior) unawaited(cargar());
    });
    return const EstadoLista(FaseLista.cargando);
  }

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
    } catch (e) {
      if (cache == null) state = EstadoLista(FaseLista.error, const [], mensajeDeError(e));
    }
  }
}

final listaInspeccionesControllerProvider = NotifierProvider<ListaInspeccionesController, EstadoLista>(ListaInspeccionesController.new);
