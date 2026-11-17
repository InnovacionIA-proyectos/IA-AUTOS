import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../auth/presentation/auth_controller.dart';
import '../domain/desenlace_envio.dart';
import '../providers.dart';

class EstadoSync {
  const EstadoSync({
    this.online = true,
    this.sincronizando = false,
    this.pendientes = 0,
    this.fallidos = 0,
    this.proximoIntento,
    this.entregados = 0,
    this.pausa,
  });

  /// Conectado por wifi, datos o ethernet.
  final bool online;
  final bool sincronizando;

  /// Elementos en cola que se reintentarán solos.
  final int pendientes;

  /// Elementos que necesitan atención del usuario (reintento manual o descarte).
  final int fallidos;

  /// Cuándo volverá a intentarlo solo. Null si no hay nada pendiente o si no hay con qué intentarlo.
  final DateTime? proximoIntento;

  /// Cuántos envíos ha aceptado el servidor desde que arrancó la app. Solo sube, y solo importa que **cambie**:
  /// es la señal de que lo que hay en pantalla quedó viejo. Hasta ahora la cola entregaba en silencio y las
  /// pantallas seguían mostrando lo de antes hasta que alguien refrescaba a mano.
  final int entregados;

  /// Por qué la última pasada no tocó la cola, si no la tocó.
  final MotivoPausa? pausa;

  EstadoSync copiar({
    bool? online,
    bool? sincronizando,
    int? pendientes,
    int? fallidos,
    DateTime? proximoIntento,
    bool sinProximoIntento = false,
    int? entregados,
    MotivoPausa? pausa,
    bool sinPausa = false,
  }) =>
      EstadoSync(
        online: online ?? this.online,
        sincronizando: sincronizando ?? this.sincronizando,
        pendientes: pendientes ?? this.pendientes,
        fallidos: fallidos ?? this.fallidos,
        proximoIntento: sinProximoIntento ? null : proximoIntento ?? this.proximoIntento,
        entregados: entregados ?? this.entregados,
        pausa: sinPausa ? null : pausa ?? this.pausa,
      );
}

/// Cuántos envíos lleva aceptados el servidor. Observarlo es la forma de que una pantalla se entere de que lo
/// que muestra quedó viejo, sin que la cola tenga que saber qué pantallas existen ni qué muestran.
final entregadosProvider = Provider<int>((ref) => ref.watch(syncControllerProvider.select((s) => s.entregados)));

/// Orquesta la cola: sincroniza al volver una señal estable, al iniciar sesión, por tiempo y a demanda. Solo
/// trabaja con sesión iniciada (sin token todo fallaría con 401 y gastaría intentos).
class SyncController extends Notifier<EstadoSync> {
  Timer? _reloj;

  /// Pasadas seguidas que no llegaron a tocar la cola. Cuenta aparte de los intentos de cada job: que el servidor
  /// esté caído no es culpa del envío y no debe gastarle su presupuesto de intentos.
  int _pausasSeguidas = 0;

  @override
  EstadoSync build() {
    final red = ref.watch(estadoRedProvider);
    final suscripcion = red.cambios.listen((estable) {
      state = state.copiar(online: estable);
      if (estable) unawaited(sincronizar());
    });
    ref.onDispose(suscripcion.cancel);
    ref.onDispose(_cancelarReloj);

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
      _cancelarReloj();
      state = state.copiar(pendientes: 0, fallidos: 0, sinProximoIntento: true);
      return;
    }
    final procesador = ref.read(procesadorColaProvider);
    final pendientes = await procesador.contarPendientes(usuario);
    final fallidos = await procesador.contarFallidos(usuario);
    state = state.copiar(pendientes: pendientes, fallidos: fallidos);
    await _programarReintento(usuario);
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
      // Una pasada que sí recorrió la cola pone el contador a cero: la espera vuelve a salir de los intentos.
      _pausasSeguidas = r.pausa == null ? 0 : _pausasSeguidas + 1;
      state = state.copiar(
        pendientes: r.pendientes,
        fallidos: r.fallidos,
        // Solo sube cuando el servidor aceptó algo: quien lo observe recarga únicamente si el mundo cambió.
        entregados: state.entregados + r.enviados,
        pausa: r.pausa,
        sinPausa: r.pausa == null,
      );
    } finally {
      state = state.copiar(sincronizando: false);
      await _programarReintento(usuario);
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

  /// Vuelve a intentarlo solo dentro de un tiempo acotado y anunciado.
  ///
  /// Hasta ahora la cola solo despertaba al cambiar la señal, al entrar o a mano: con la red conectada y el
  /// servidor caído no se movía nunca, y la pantalla no podía prometer nada. La espera sale de los intentos que
  /// lleva el envío más adelantado y se detiene a los diez minutos, así que la jornada de patio no se pasa
  /// despertando el teléfono. Sin señal no se programa nada: de eso ya se encarga la escucha de la red.
  Future<void> _programarReintento(String usuarioId) async {
    _cancelarReloj();
    if (!state.online || !ref.read(authControllerProvider).puedeOperar) {
      state = state.copiar(sinProximoIntento: true);
      return;
    }
    final pendientes = (await ref.read(procesadorColaProvider).visibles(usuarioId)).where((j) => !j.fallido);
    if (pendientes.isEmpty) {
      state = state.copiar(sinProximoIntento: true);
      return;
    }
    final porIntentos = pendientes.map((j) => esperaTrasIntentos(j.intentos)).reduce((a, b) => a < b ? a : b);
    // Con el servidor caído la pasada se detiene antes de tocar ningún job, así que `intentos` no sube y la
    // espera se quedaba en quince segundos para siempre: una jornada entera serían miles de despertares, justo
    // lo que la escala quiere evitar. Las pausas seguidas hacen crecer la espera igual que los intentos, sin
    // gastar el presupuesto de intentos, que es para lo que el servidor rechaza y no para que esté caído.
    final porPausas = _pausasSeguidas > 0 ? esperaTrasIntentos(_pausasSeguidas - 1) : Duration.zero;
    final espera = porPausas > porIntentos ? porPausas : porIntentos;
    state = state.copiar(proximoIntento: DateTime.now().add(espera));
    _reloj = Timer(espera, () => unawaited(sincronizar()));
  }

  void _cancelarReloj() {
    _reloj?.cancel();
    _reloj = null;
  }
}

final syncControllerProvider = NotifierProvider<SyncController, EstadoSync>(SyncController.new);
