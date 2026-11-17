import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/errores/error_api.dart';
import '../../auth/domain/sesion.dart';
import '../../auth/presentation/auth_controller.dart';
import '../../sync/presentation/sync_controller.dart';
import '../domain/inspeccion_resumen.dart';
import '../providers.dart';

enum FaseLista { cargando, listo, error, sinConexion }

class EstadoLista {
  const EstadoLista(
    this.fase, {
    this.items = const [],
    this.mensaje,
    this.total = 0,
    this.cargandoMas = false,
    this.busqueda = '',
    this.pagina = 1,
  });

  final FaseLista fase;
  final List<InspeccionResumen> items;

  /// Por qué no se pudo cargar, cuando la fase es `error`. Va aquí y no fijo en la pantalla porque no siempre es
  /// la red: una respuesta que la app no supo leer también cae en esta fase, y decir «revisa tu conexión» con la
  /// API respondiendo 200 manda a buscar el problema donde no está.
  ///
  /// También lo usa un fallo al traer la página siguiente, que no tumba la lista ya cargada: ahí la fase sigue
  /// siendo `listo` y el mensaje se dice junto al botón.
  final String? mensaje;

  /// Cuántos informes dice el servidor que hay con lo que se le pidió. Sirve para una sola cosa: saber si queda
  /// una página más.
  final int total;

  /// Está trayendo la página siguiente. La lista se queda en pantalla: cambiarla por el cargador perdería el
  /// sitio donde iba leyendo.
  final bool cargandoMas;

  /// El VIN que se está buscando; vacío cuando se ve la lista entera.
  final String busqueda;

  /// La última página traída. Se lleva aquí y no se deduce de cuántos elementos hay: una página incompleta
  /// —la última, o una que el servidor recortó— haría que la cuenta volviera a pedir la que ya está en pantalla.
  final int pagina;

  bool get hayMas => items.length < total;
}

/// Carga el listado mostrando primero la caché local (sirve sin señal) y refrescando después si hay conexión.
/// Sin conexión se conserva lo cacheado; no se borra la lista.
///
/// Qué lista es depende de quién pregunta: el inspector y el conductor piden solo sus informes, y el coordinador
/// de calidad los de su cliente. La caché, en cambio, es siempre de quien entró: en un dispositivo compartido
/// nadie abre la app y encuentra lo que descargó otro.
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

  /// Si se manda el filtro de autor. El servidor ya recorta cada respuesta; mandarlo de más es lo que dejaba al
  /// coordinador de calidad con la pestaña vacía, porque él revisa informes pero no captura ninguno.
  bool get _soloSuyas => soloSusViajes(ref.read(authControllerProvider).sesion?.usuario.rol);

  Future<void> cargar() async {
    final usuarioId = ref.read(authControllerProvider).sesion?.usuario.id;
    if (usuarioId == null) return;
    final repo = ref.read(inspeccionesRepositorioProvider);
    final online = ref.read(syncControllerProvider).online;
    final busqueda = state.busqueda;

    // Buscando no se mira la caché: lo guardado es la primera página sin filtro, y mostrarla como resultado
    // diría que esos informes son del VIN que se buscó.
    final cache = busqueda.isEmpty ? await repo.leerCache(usuarioId) : null;
    if (cache != null) {
      state = EstadoLista(FaseLista.listo, items: cache, total: cache.length);
    } else if (!online) {
      state = EstadoLista(
        FaseLista.sinConexion,
        busqueda: busqueda,
        mensaje: busqueda.isEmpty ? null : 'Buscar le pregunta al servidor. Conéctate e inténtalo de nuevo.',
      );
      return;
    } else {
      state = EstadoLista(FaseLista.cargando, busqueda: busqueda);
    }
    if (!online) return;

    try {
      final vin = busqueda.isEmpty ? null : busqueda;
      final respuesta = await repo.listarRemoto(usuarioId, soloSuyas: _soloSuyas, vin: vin);
      state = EstadoLista(FaseLista.listo, items: respuesta.items, total: respuesta.total, busqueda: busqueda);
    } catch (e) {
      if (cache == null) state = EstadoLista(FaseLista.error, mensaje: mensajeDeError(e), busqueda: busqueda);
    }
  }

  /// Busca un VIN en el servidor. Vacío vuelve a la lista entera.
  Future<void> buscar(String vin) async {
    state = EstadoLista(FaseLista.cargando, busqueda: vin.trim());
    await cargar();
  }

  /// Trae la página siguiente y la agrega al final. Lo que ya está en pantalla no se toca.
  Future<void> cargarMas() async {
    final usuarioId = ref.read(authControllerProvider).sesion?.usuario.id;
    final anterior = state;
    if (usuarioId == null || anterior.fase != FaseLista.listo || anterior.cargandoMas || !anterior.hayMas) return;
    // Sin señal no hay página siguiente que traer: solo está guardada la primera.
    if (!ref.read(syncControllerProvider).online) return;

    state = EstadoLista(
      FaseLista.listo,
      items: anterior.items,
      total: anterior.total,
      busqueda: anterior.busqueda,
      pagina: anterior.pagina,
      cargandoMas: true,
    );
    final siguiente = anterior.pagina + 1;
    try {
      final respuesta = await ref.read(inspeccionesRepositorioProvider).listarRemoto(
            usuarioId,
            soloSuyas: _soloSuyas,
            vin: anterior.busqueda.isEmpty ? null : anterior.busqueda,
            pagina: siguiente,
          );
      final items = [...anterior.items, ...respuesta.items];
      state = EstadoLista(
        FaseLista.listo,
        items: items,
        // Una página vacía con un total que promete más dejaría el botón pidiendo lo mismo para siempre.
        total: respuesta.items.isEmpty ? items.length : respuesta.total,
        busqueda: anterior.busqueda,
        pagina: siguiente,
      );
    } catch (e) {
      state = EstadoLista(
        FaseLista.listo,
        items: anterior.items,
        total: anterior.total,
        busqueda: anterior.busqueda,
        pagina: anterior.pagina,
        mensaje: mensajeDeError(e),
      );
    }
  }
}

final listaInspeccionesControllerProvider = NotifierProvider<ListaInspeccionesController, EstadoLista>(ListaInspeccionesController.new);
