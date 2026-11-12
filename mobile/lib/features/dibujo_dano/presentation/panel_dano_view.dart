import 'dart:math' as math;

import 'package:flutter/material.dart';

import '../../../core/tema/tema.dart';
import '../domain/dibujo_dano.dart';
import '../domain/toque.dart';
import 'pintor_regiones.dart';

/// Un panel del dibujo: la imagen, los polígonos encima, el zoom y la resolución del toque.
///
/// Reglas que no se pueden perder (vienen de la guía del prototipo):
/// - `AspectRatio` con `ancho_ref / alto_ref` y la imagen con `BoxFit.fill`: los puntos son proporciones de esa
///   imagen exacta, y si se recorta o se deforma quedan corridos.
/// - El `GestureDetector` va **dentro** del `InteractiveViewer`: Flutter entrega la posición del toque en las
///   coordenadas del dibujo sin zoom, y la tolerancia se divide por la escala (eso lo hace [resolverToque]).
/// - Sin zoom el panel no se arrastra, para no pelear con el desplazamiento de la pantalla que lo contiene.
class PanelDanoView extends StatelessWidget {
  const PanelDanoView({
    super.key,
    required this.panel,
    required this.regiones,
    required this.danos,
    required this.seleccionada,
    required this.controlador,
    required this.alTocar,
    this.alturaMaxima = 420,
    this.cuadricula = true,
  });

  final PanelDano panel;
  final List<RegionDano> regiones;
  /// Clave de la zona con daño al nivel de ese daño (nulo si nadie lo clasificó).
  final Map<String, String?> danos;
  final String? seleccionada;

  /// El zoom de este panel. Lo crea y lo libera quien contiene el panel, para que cada vista conserve el suyo al
  /// cambiar de pestaña.
  final TransformationController controlador;

  /// Recibe el toque ya resuelto. [ToqueAcercar] no llega: lo resuelve el panel acercando la vista.
  final void Function(ResultadoToque toque) alTocar;

  final double alturaMaxima;
  final bool cuadricula;

  static bool estaAcercado(TransformationController c) => c.value.getMaxScaleOnAxis() > 1.01;

  void _tocar(Offset local, Size tamano) {
    if (tamano.isEmpty) return;
    final escala = controlador.value.getMaxScaleOnAxis();
    final resultado = resolverToque(
      regiones: regiones,
      punto: (x: local.dx / tamano.width * panel.anchoRef, y: local.dy / tamano.height * panel.altoRef),
      pantallaPorRef: escala * tamano.width / panel.anchoRef,
      acercado: estaAcercado(controlador),
    );
    if (resultado is ToqueAcercar) {
      _acercar(local, tamano);
    } else {
      alTocar(resultado);
    }
  }

  /// Acerca [MedidasToque.acercamiento] veces dejando el punto tocado en el centro, sin salirse del dibujo.
  void _acercar(Offset local, Size tamano) {
    const k = MedidasToque.acercamiento;
    final tx = math.max(tamano.width * (1 - k), math.min(0.0, tamano.width / 2 - local.dx * k));
    final ty = math.max(tamano.height * (1 - k), math.min(0.0, tamano.height / 2 - local.dy * k));
    controlador.value = Matrix4.identity()
      ..setEntry(0, 0, k)
      ..setEntry(1, 1, k)
      ..setEntry(0, 3, tx)
      ..setEntry(1, 3, ty);
  }

  Widget _imagen() {
    final ruta = panel.imagen.rutaAsset;
    return ruta != null
        ? Image.asset(ruta, fit: BoxFit.fill, gaplessPlayback: true, excludeFromSemantics: true)
        : Image.memory(panel.imagen.bytes!, fit: BoxFit.fill, gaplessPlayback: true, excludeFromSemantics: true);
  }

  @override
  Widget build(BuildContext context) {
    final conDanoAqui = regiones.where((r) => danos.containsKey(r.clave)).length;
    return Semantics(
      label: conDanoAqui == 0
          ? 'Vista ${panel.nombre} del vehículo, sin daños'
          : 'Vista ${panel.nombre} del vehículo, $conDanoAqui cuadrantes con daño',
      child: Center(
        child: ConstrainedBox(
          constraints: BoxConstraints(maxHeight: alturaMaxima),
          child: AspectRatio(
            aspectRatio: panel.proporcion,
            child: DecoratedBox(
              decoration: BoxDecoration(color: Colors.white, border: Border.all(color: Colores.borde)),
              child: ClipRect(
                child: ValueListenableBuilder<Matrix4>(
                  valueListenable: controlador,
                  builder: (context, matriz, _) {
                    final escala = matriz.getMaxScaleOnAxis();
                    final acercado = escala > 1.01;
                    return Stack(
                      fit: StackFit.expand,
                      children: [
                        InteractiveViewer(
                          transformationController: controlador,
                          minScale: 1,
                          maxScale: 5,
                          panEnabled: acercado,
                          child: LayoutBuilder(
                            builder: (context, caja) {
                              final tamano = caja.biggest;
                              return GestureDetector(
                                behavior: HitTestBehavior.opaque,
                                onTapUp: (detalle) => _tocar(detalle.localPosition, tamano),
                                child: Stack(
                                  fit: StackFit.expand,
                                  children: [
                                    _imagen(),
                                    CustomPaint(
                                      painter: PintorRegiones(
                                        panel: panel,
                                        regiones: regiones,
                                        danos: danos,
                                        seleccionada: seleccionada,
                                        escala: escala,
                                        cuadricula: cuadricula,
                                      ),
                                    ),
                                  ],
                                ),
                              );
                            },
                          ),
                        ),
                        if (acercado)
                          Positioned(
                            top: Espacio.sm,
                            right: Espacio.sm,
                            child: FilledButton(
                              // El tema hace los botones de ancho completo; posicionado en una esquina, ese ancho es
                              // infinito y el panel deja de responder a los toques.
                              style: FilledButton.styleFrom(
                                backgroundColor: ColoresDibujo.seleccion,
                                minimumSize: const Size(0, alturaTactil),
                              ),
                              onPressed: () => controlador.value = Matrix4.identity(),
                              child: const Text('Ver todo'),
                            ),
                          ),
                      ],
                    );
                  },
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}
