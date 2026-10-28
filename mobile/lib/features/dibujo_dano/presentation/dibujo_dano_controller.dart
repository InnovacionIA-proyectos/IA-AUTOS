import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../catalogos/presentation/catalogos_controller.dart';
import '../domain/dibujo_dano.dart';
import '../domain/toque.dart';
import '../providers.dart';

/// El dibujo vigente para las pantallas. Se recalcula cuando cambian los catálogos: si el servidor ya publica un
/// dibujo completo se usa ese (y sus imágenes quedan guardadas); si no, la copia de los assets.
///
/// Mientras se recalcula, Riverpod conserva el valor anterior, así que la pantalla no parpadea al revalidar.
final dibujoDanoProvider = FutureProvider<DibujoDano>((ref) async {
  final catalogos = ref.watch(catalogosProvider);
  return ref.watch(dibujoDanoRepositorioProvider).vigente(catalogos);
});

/// Lo que el inspector tiene elegido en el dibujo. Es el estado del prototipo (`S.view`, `S.sel`, `S.cands`); el
/// zoom de cada panel no va aquí, lo lleva el `TransformationController` del widget.
class EstadoSeleccionZona {
  const EstadoSeleccionZona({this.panel = 0, this.seleccionada, this.candidatos = const []});

  /// Índice de la pestaña visible.
  final int panel;

  /// Clave de la zona elegida («26D»), o null.
  final String? seleccionada;

  /// Si el último toque fue ambiguo, las zonas entre las que hay que elegir. Mientras hay candidatos no hay
  /// selección.
  final List<String> candidatos;

  bool get hayCandidatos => candidatos.isNotEmpty;

  EstadoSeleccionZona copyWith({int? panel, String? Function()? seleccionada, List<String>? candidatos}) =>
      EstadoSeleccionZona(
        panel: panel ?? this.panel,
        seleccionada: seleccionada == null ? this.seleccionada : seleccionada(),
        candidatos: candidatos ?? this.candidatos,
      );
}

/// Una selección por pantalla: [contexto] separa la de la captura de las de cada informe en la página del VIN.
class SeleccionZonaController extends Notifier<EstadoSeleccionZona> {
  SeleccionZonaController(this.contexto);

  final String contexto;

  @override
  EstadoSeleccionZona build() => const EstadoSeleccionZona();

  /// Cambiar de pestaña conserva la selección (para ver el punto amarillo en las otras) y descarta la pregunta.
  void verPanel(int indice) => state = state.copyWith(panel: indice, candidatos: const []);

  /// Elegir la que ya está elegida la suelta, como en el prototipo.
  void elegir(String clave) => state = state.copyWith(
        seleccionada: () => state.seleccionada == clave ? null : clave,
        candidatos: const [],
      );

  void deseleccionar() => state = state.copyWith(seleccionada: () => null, candidatos: const []);

  /// Aplica el resultado de un toque. [ToqueAcercar] no cambia la selección: lo resuelve el widget con el zoom.
  void aplicar(ResultadoToque toque) {
    switch (toque) {
      case ToqueElegir(:final clave):
        elegir(clave);
      case ToqueAmbiguo(:final candidatos):
        state = state.copyWith(seleccionada: () => null, candidatos: candidatos);
      case ToqueFuera():
        deseleccionar();
      case ToqueAcercar():
        break;
    }
  }

  /// Lleva a la zona desde la lista de daños: si no se ve en la pestaña actual, va a la primera donde se ve.
  void enfocar(DibujoDano dibujo, String clave) {
    final actual = state.panel < dibujo.paneles.length ? dibujo.paneles[state.panel].codigo : null;
    final seVeAqui = actual != null && dibujo.regionesDe(actual).any((r) => r.clave == clave);
    final primero = dibujo.paneles.indexWhere((p) => dibujo.regionesDe(p.codigo).any((r) => r.clave == clave));
    state = EstadoSeleccionZona(panel: seVeAqui || primero < 0 ? state.panel : primero, seleccionada: clave);
  }
}

final seleccionZonaProvider =
    NotifierProvider.autoDispose.family<SeleccionZonaController, EstadoSeleccionZona, String>(SeleccionZonaController.new);
