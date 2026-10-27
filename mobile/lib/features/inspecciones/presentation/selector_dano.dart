import 'package:flutter/material.dart';

import '../../../core/tema/tema.dart';
import '../../catalogos/domain/catalogos.dart';

/// Pide la clase y el tamaño del daño de la zona elegida. Devuelve null si se cancela.
///
/// Las opciones salen de los catálogos descargados (solo las activas, en su orden), nunca de listas fijas: TI y el
/// coordinador de calidad las administran desde el portal.
Future<({String clase, String tamano})?> pedirDano(
  BuildContext context, {
  required String titulo,
  required List<ValorCatalogo> clases,
  required List<ValorCatalogo> tamanos,
}) {
  return showModalBottomSheet<({String clase, String tamano})>(
    context: context,
    isScrollControlled: true,
    showDragHandle: true,
    builder: (_) => _HojaDano(titulo: titulo, clases: clases, tamanos: tamanos),
  );
}

class _HojaDano extends StatefulWidget {
  const _HojaDano({required this.titulo, required this.clases, required this.tamanos});

  final String titulo;
  final List<ValorCatalogo> clases;
  final List<ValorCatalogo> tamanos;

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
                          for (final c in widget.clases)
                            ChoiceChip(
                              label: Text(c.nombre),
                              selected: _clase == c.codigo,
                              onSelected: (_) => setState(() => _clase = c.codigo),
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
                          for (final t in widget.tamanos)
                            ChoiceChip(
                              label: Text(t.nombre),
                              selected: _tamano == t.codigo,
                              onSelected: (_) => setState(() => _tamano = t.codigo),
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
