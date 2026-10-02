import 'package:flutter/material.dart';

import '../../../core/tema/tema.dart';
import '../../vehiculos/domain/etiquetas.dart';

/// Valores de catálogo aceptados por la API (en el mismo orden que la app original).
const clasesDano = ['deformacion', 'rotura', 'cortado', 'perforacion', 'golpe', 'mancha', 'raya_pintar', 'desconchado', 'particulas', 'faltante'];
const tamanosDano = ['1_5cm', '5_10cm', '10_20cm', 'mas_20cm', 'requiere_reemplazo', 'mas_10_particulas'];

/// Pide la clase y el tamaño del daño de la zona elegida. Devuelve null si se cancela.
Future<({String clase, String tamano})?> pedirDano(BuildContext context, {required String titulo}) {
  return showModalBottomSheet<({String clase, String tamano})>(
    context: context,
    isScrollControlled: true,
    showDragHandle: true,
    builder: (_) => _HojaDano(titulo: titulo),
  );
}

class _HojaDano extends StatefulWidget {
  const _HojaDano({required this.titulo});

  final String titulo;

  @override
  State<_HojaDano> createState() => _EstadoHojaDano();
}

class _EstadoHojaDano extends State<_HojaDano> {
  String? _clase;
  String? _tamano;

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context).textTheme;
    return ConstrainedBox(
      constraints: BoxConstraints(maxHeight: MediaQuery.sizeOf(context).height * 0.85),
      child: SafeArea(
        child: Padding(
          padding: const EdgeInsets.fromLTRB(Espacio.lg, 0, Espacio.lg, Espacio.lg),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(widget.titulo, style: tema.titleMedium),
              Flexible(
                child: SingleChildScrollView(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      const SizedBox(height: Espacio.md),
                      Text('Clase de daño', style: tema.titleMedium?.copyWith(fontSize: 15)),
                      const SizedBox(height: Espacio.sm),
                      Wrap(
                        spacing: Espacio.sm,
                        runSpacing: Espacio.sm,
                        children: [
                          for (final c in clasesDano)
                            ChoiceChip(
                              label: Text(etiquetar(etiquetaClaseDano, c)),
                              selected: _clase == c,
                              onSelected: (_) => setState(() => _clase = c),
                            ),
                        ],
                      ),
                      const SizedBox(height: Espacio.lg),
                      Text('Tamaño', style: tema.titleMedium?.copyWith(fontSize: 15)),
                      const SizedBox(height: Espacio.sm),
                      Wrap(
                        spacing: Espacio.sm,
                        runSpacing: Espacio.sm,
                        children: [
                          for (final t in tamanosDano)
                            ChoiceChip(
                              label: Text(etiquetar(etiquetaTamanoDano, t)),
                              selected: _tamano == t,
                              onSelected: (_) => setState(() => _tamano = t),
                            ),
                        ],
                      ),
                    ],
                  ),
                ),
              ),
              const SizedBox(height: Espacio.lg),
              Row(
                children: [
                  Expanded(child: OutlinedButton(onPressed: () => Navigator.of(context).pop(), child: const Text('Cancelar'))),
                  const SizedBox(width: Espacio.md),
                  Expanded(
                    child: FilledButton(
                      onPressed: _clase != null && _tamano != null ? () => Navigator.of(context).pop((clase: _clase!, tamano: _tamano!)) : null,
                      child: const Text('Agregar'),
                    ),
                  ),
                ],
              ),
            ],
          ),
        ),
      ),
    );
  }
}
