import 'package:flutter/material.dart';

import '../tema/tema.dart';

/// Abre una foto a pantalla completa, con zoom. Al cerrar, la pantalla de atrás queda igual.
///
/// Las miniaturas de la app son de 44 a 88 px: a ese tamaño no se distingue si una foto salió movida, oscura o
/// recortada, y eso solo se descubre cuando ya no se tiene el vehículo delante. Por eso se puede abrir.
Future<void> mostrarVisorImagen(BuildContext context, {required ImageProvider imagen, required String titulo}) =>
    showDialog<void>(
      context: context,
      // Fondo oscuro de verdad: una foto de un vehículo se juzga mejor sin una pantalla blanca alrededor.
      barrierColor: Colors.black87,
      builder: (_) => _VisorImagen(imagen: imagen, titulo: titulo),
    );

class _VisorImagen extends StatelessWidget {
  const _VisorImagen({required this.imagen, required this.titulo});

  final ImageProvider imagen;
  final String titulo;

  @override
  Widget build(BuildContext context) {
    return Dialog.fullscreen(
      backgroundColor: Colors.transparent,
      child: Stack(
        children: [
          Positioned.fill(
            child: InteractiveViewer(
              minScale: 1,
              maxScale: 5,
              // El toque va DENTRO del visor: puesto detrás, el InteractiveViewer se lo queda y nunca llega.
              // Un toque cierra —es lo primero que se intenta— y el pellizco sigue acercando, porque es otro
              // gesto.
              child: GestureDetector(
                onTap: () => Navigator.of(context).pop(),
                behavior: HitTestBehavior.opaque,
                child: Center(
                  child: Image(
                    image: imagen,
                    fit: BoxFit.contain,
                    semanticLabel: titulo,
                    errorBuilder: (_, _, _) => const Icon(Icons.broken_image_outlined, size: 56, color: Colors.white54),
                  ),
                ),
              ),
            ),
          ),
          Positioned(
            top: Espacio.md,
            right: Espacio.md,
            child: SafeArea(
              child: Material(
                color: Colors.black54,
                shape: const CircleBorder(),
                child: IconButton(
                  tooltip: 'Cerrar',
                  icon: const Icon(Icons.close, color: Colors.white),
                  onPressed: () => Navigator.of(context).pop(),
                ),
              ),
            ),
          ),
          Positioned(
            left: Espacio.lg,
            right: 72,
            top: Espacio.md,
            child: SafeArea(
              child: Text(
                titulo,
                style: const TextStyle(color: Colors.white, fontSize: 15, fontWeight: FontWeight.w600),
                maxLines: 2,
                overflow: TextOverflow.ellipsis,
              ),
            ),
          ),
          Positioned(
            left: 0,
            right: 0,
            bottom: Espacio.lg,
            child: SafeArea(
              child: Center(
                child: Text('Pellizca para acercar · toca para cerrar', style: TextStyle(color: Colors.white.withValues(alpha: 0.7), fontSize: 13)),
              ),
            ),
          ),
        ],
      ),
    );
  }
}
