import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../auth/presentation/auth_controller.dart';
import '../providers.dart';

class EstadoSync {
  const EstadoSync({this.online = true, this.sincronizando = false, this.pendientes = 0, this.fallidos = 0});

  /// Conectado por wifi, datos o ethernet.
  final bool online;
  final bool sincronizando;

  /// Elementos en cola que se reintentarán solos.
  final int pendientes;

  /// Elementos que necesitan atención del usuario (reintento manual o descarte).
  final int fallidos;

  EstadoSync copiar({bool? online, bool? sincronizando, int? pendientes, int? fallidos}) => EstadoSync(
        online: online ?? this.online,
        sincronizando: sincronizando ?? this.sincronizando,
        pendientes: pendientes ?? this.pendientes,
        fallidos: fallidos ?? this.fallidos,
      );
}

/// Orquesta la cola: sincroniza al volver una señal estable, al iniciar sesión y a demanda. Solo trabaja con
/// sesión iniciada (sin token todo fallaría con 401 y gastaría intentos).
class SyncController extends Notifier<EstadoSync> {
  @override
  EstadoSync build() {
    final red = ref.watch(estadoRedProvider);
    final suscripcion = red.cambios.listen((estable) {
      state = state.copiar(online: estable);
      if (estable) unawaited(sincronizar());
    });
    ref.onDispose(suscripcion.cancel);

    // Por `puedeOperar` y no por la fase: quien entra con un PIN temporal tiene sesión pero todavía no puede
    // enviar nada, y al cambiarlo esta misma escucha es la que arranca la cola.
    ref.listen(authControllerProvider.select((e) => e.puedeOperar), (anterior, actual) {
      if (actual && anterior != true) unawaited(sincronizar());
    });

    Future.microtask(() async {
      state = state.copiar(online: await red.redEstable());
      // Los jobs que encoló una versión anterior no tienen dueño. La sesión guardada es la última que se usó, así
      // que es casi con certeza quien los encoló: se los adopta antes de contar nada.
      final usuario = _usuario;
      if (usuario != null) await ref.read(procesadorColaProvider).adoptarHuerfanos(usuario);
      await refrescar();
      if (ref.read(authControllerProvider).puedeOperar) await sincronizar();
    });
    return const EstadoSync();
  }

  /// Quién tiene la sesión: de él son los envíos que se cuentan y se mandan.
  String? get _usuario => ref.read(authControllerProvider).sesion?.usuario.id;

  /// Vuelve a contar lo pendiente y lo fallido (tras encolar, reintentar o descartar).
  Future<void> refrescar() async {
    final usuario = _usuario;
    if (usuario == null) {
      // Sin sesión no se cuenta lo de nadie: la cola de quien salió no es asunto de la pantalla de ingreso.
      state = state.copiar(pendientes: 0, fallidos: 0);
      return;
    }
    final procesador = ref.read(procesadorColaProvider);
    final pendientes = await procesador.contarPendientes(usuario);
    final fallidos = await procesador.contarFallidos(usuario);
    state = state.copiar(pendientes: pendientes, fallidos: fallidos);
  }

  Future<void> sincronizar() async {
    if (state.sincronizando) return;
    // Única guarda de los cuatro disparadores (señal, ingreso, arranque y reintento manual): sin sesión todo
    // daría 401 y con un PIN temporal todo daría 403, y en ambos casos se gastarían intentos de la cola.
    final usuario = _usuario;
    if (!ref.read(authControllerProvider).puedeOperar || usuario == null) return;
    state = state.copiar(sincronizando: true);
    try {
      final r = await ref.read(procesadorColaProvider).procesar(usuario);
      state = state.copiar(pendientes: r.pendientes, fallidos: r.fallidos);
    } finally {
      state = state.copiar(sincronizando: false);
    }
  }

  Future<void> reintentar(String id) async {
    final usuario = _usuario;
    if (usuario == null) return;
    await ref.read(procesadorColaProvider).reintentar(id, usuario);
    await refrescar();
    unawaited(sincronizar());
  }

  Future<void> descartar(String id) async {
    await ref.read(procesadorColaProvider).descartar(id);
    await refrescar();
  }
}

final syncControllerProvider = NotifierProvider<SyncController, EstadoSync>(SyncController.new);
