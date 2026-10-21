import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../auth/presentation/auth_controller.dart';
import '../../sync/providers.dart';
import '../domain/catalogos.dart';
import '../providers.dart';

enum FaseCatalogos {
  cargando,

  /// Hay una copia utilizable (de la red o del dispositivo).
  listo,

  /// Nunca se pudo descargar: sin copia no hay con qué llenar los selectores, así que la captura se bloquea.
  sinCatalogos,
}

class EstadoCatalogos {
  const EstadoCatalogos(this.fase, [this.catalogos]);

  final FaseCatalogos fase;
  final Catalogos? catalogos;

  bool get listo => fase == FaseCatalogos.listo && catalogos != null;
}

/// Mantiene la copia de los catálogos al día.
///
/// Sirve primero lo guardado y revalida después, igual que el listado de inspecciones: sin señal se conserva la
/// última copia y no se borra nada. Se revalida en los mismos momentos en que se sincroniza la cola —al abrir la
/// app, al iniciar sesión y al volver una señal estable— porque con `If-None-Match` la comprobación normal es un
/// `304` sin cuerpo y no cuesta nada.
class CatalogosController extends Notifier<EstadoCatalogos> {
  @override
  EstadoCatalogos build() {
    final red = ref.watch(estadoRedProvider);
    final suscripcion = red.cambios.listen((estable) {
      if (estable) unawaited(cargar());
    });
    ref.onDispose(suscripcion.cancel);

    // Por transición, no por estado: así la descarga ocurre cuando la sesión ya existe (el login siempre tiene
    // señal) y nunca durante FaseAuth.cargando, cuando todavía no hay token.
    ref.listen(authControllerProvider, (anterior, actual) {
      if (actual.fase == FaseAuth.autenticado && anterior?.fase != FaseAuth.autenticado) {
        unawaited(cargar());
      }
    });

    Future.microtask(cargar);
    return const EstadoCatalogos(FaseCatalogos.cargando);
  }

  /// Vuelve a intentarlo desde la pantalla de «sin catálogos».
  Future<void> reintentar() => cargar();

  bool _cargando = false;
  bool _pedidoDeNuevo = false;

  /// Los tres disparadores pueden solaparse: el arranque, la transición de sesión y la vuelta de la señal llegan
  /// casi a la vez. Sin esto se descargaría varias veces y una pasada vieja podría publicar su resultado después
  /// de una nueva.
  ///
  /// No basta con descartar la llamada que llega mientras hay una en curso: la primera puede haber salido sin
  /// sesión todavía y haber terminado sin hacer nada, y entonces se perdería la del login. Por eso se anota y se
  /// repite al terminar.
  Future<void> cargar() async {
    if (_cargando) {
      _pedidoDeNuevo = true;
      return;
    }
    _cargando = true;
    // Se conserva la copia que ya se esté mostrando: esto solo sirve para que la pantalla sepa que hay algo en
    // curso y el botón de reintentar dé señal de vida.
    state = EstadoCatalogos(FaseCatalogos.cargando, state.catalogos);
    try {
      do {
        _pedidoDeNuevo = false;
        await _cargar();
      } while (_pedidoDeNuevo);
    } finally {
      _cargando = false;
    }
  }

  Future<void> _cargar() async {
    // Sin sesión la API responde 401 y la petición no aporta nada; es la misma guarda que usa la cola.
    final autenticacion = ref.read(authControllerProvider);
    if (autenticacion.fase != FaseAuth.autenticado) return;
    final usuarioId = autenticacion.sesion?.usuario.id;
    if (usuarioId == null) return;

    final repositorio = ref.read(catalogosRepositorioProvider);
    final local = await repositorio.leerCache(usuarioId);
    if (local != null) state = EstadoCatalogos(FaseCatalogos.listo, local);

    if (!await ref.read(estadoRedProvider).redEstable()) {
      if (local == null) state = const EstadoCatalogos(FaseCatalogos.sinCatalogos);
      return;
    }

    try {
      state = EstadoCatalogos(FaseCatalogos.listo, await repositorio.sincronizar(usuarioId));
    } catch (_) {
      // Un fallo al revalidar no debe tocar lo que ya se está mostrando.
      if (local == null) state = const EstadoCatalogos(FaseCatalogos.sinCatalogos);
    }
  }
}

final catalogosControllerProvider =
    NotifierProvider<CatalogosController, EstadoCatalogos>(CatalogosController.new);

/// Los catálogos vigentes, para las pantallas. Mientras no haya copia devuelve uno vacío: `etiqueta()` responde el
/// código tal cual, igual que hacía `etiquetar()` con un valor desconocido, así que nada se rompe por no tenerlos.
final catalogosProvider = Provider<Catalogos>(
  (ref) => ref.watch(catalogosControllerProvider).catalogos ?? Catalogos.vacio,
);
