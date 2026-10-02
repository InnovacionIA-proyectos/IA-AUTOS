import 'package:flutter/material.dart';

import '../../../core/tema/tema.dart';
import '../../sync/domain/job.dart';
import '../domain/partes.dart';

/// Proporción de la imagen del diagrama (704 x 1536).
const _relacionAspecto = 704 / 1536;

/// Diagrama de cuadrantes del vehículo con puntos rojos en las zonas con novedad. En modo editable muestra además un
/// botón por parte para registrar hallazgos.
class DiagramaVehiculo extends StatelessWidget {
  const DiagramaVehiculo({super.key, required this.marcas, this.onElegir});

  /// Coordenadas normalizadas (0..1) de los hallazgos registrados.
  final List<CoordsDiagrama> marcas;

  /// Si se indica, el diagrama es editable: se llama con la etiqueta final y la coordenada de la zona elegida.
  final void Function(String etiqueta, CoordsDiagrama coords)? onElegir;

  Future<void> _elegirPosicion(BuildContext context, ParteDef parte) async {
    final opcion = await showModalBottomSheet<OpcionPosicion>(
      context: context,
      showDragHandle: true,
      builder: (contexto) => SafeArea(
        child: Padding(
          padding: const EdgeInsets.fromLTRB(Espacio.lg, 0, Espacio.lg, Espacio.lg),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(parte.base, style: Theme.of(contexto).textTheme.titleMedium),
              Text('¿En qué posición?', style: Theme.of(contexto).textTheme.bodySmall),
              const SizedBox(height: Espacio.md),
              Wrap(
                spacing: Espacio.sm,
                runSpacing: Espacio.sm,
                children: [
                  for (final p in parte.posiciones)
                    ActionChip(
                      label: Text(p.etiqueta),
                      materialTapTargetSize: MaterialTapTargetSize.padded,
                      onPressed: () => Navigator.of(contexto).pop(p),
                    ),
                ],
              ),
            ],
          ),
        ),
      ),
    );
    if (opcion != null) onElegir?.call(etiquetaParte(parte.base, opcion.etiqueta), opcion.coords);
  }

  void _elegir(BuildContext context, ParteDef parte) {
    if (parte.posiciones.isNotEmpty) {
      _elegirPosicion(context, parte);
    } else if (parte.coords != null) {
      onElegir?.call(etiquetaParte(parte.base), parte.coords!);
    }
  }

  @override
  Widget build(BuildContext context) {
    return Column(
      children: [
        Semantics(
          label: marcas.isEmpty ? 'Diagrama del vehículo sin novedades' : 'Diagrama del vehículo con ${marcas.length} novedades marcadas',
          child: SizedBox(
            width: 190,
            child: AspectRatio(
              aspectRatio: _relacionAspecto,
              child: DecoratedBox(
                decoration: BoxDecoration(
                  color: Colors.white,
                  border: Border.all(color: Colores.borde),
                  borderRadius: BorderRadius.circular(10),
                ),
                child: LayoutBuilder(
                  builder: (context, caja) => Stack(
                    children: [
                      Positioned.fill(child: Image.asset('assets/diagrama-novedades.png', fit: BoxFit.contain)),
                      for (final m in marcas)
                        Positioned(
                          left: m.x * caja.maxWidth - 7,
                          top: m.y * caja.maxHeight - 7,
                          child: Container(
                            width: 14,
                            height: 14,
                            decoration: BoxDecoration(
                              color: Colores.peligro,
                              shape: BoxShape.circle,
                              border: Border.all(color: Colors.white, width: 2),
                            ),
                          ),
                        ),
                    ],
                  ),
                ),
              ),
            ),
          ),
        ),
        if (onElegir != null) ...[
          const SizedBox(height: Espacio.md),
          Wrap(
            alignment: WrapAlignment.center,
            spacing: Espacio.sm,
            runSpacing: Espacio.sm,
            children: [
              for (final parte in catalogoPartes)
                Semantics(
                  button: true,
                  label: 'Registrar novedad en ${parte.base}',
                  child: ActionChip(
                    label: Text(parte.posiciones.isEmpty ? parte.base : '${parte.base} ▾'),
                    materialTapTargetSize: MaterialTapTargetSize.padded,
                    onPressed: () => _elegir(context, parte),
                  ),
                ),
            ],
          ),
        ],
      ],
    );
  }
}
