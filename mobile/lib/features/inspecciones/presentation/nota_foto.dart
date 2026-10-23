import 'package:flutter/material.dart';

import '../../../core/tema/tema.dart';

/// Pide la nota de una foto adicional (baúl, cojinería). Devuelve null si se cancela.
///
/// Se pide aquí y no al cerrar porque el servidor la exige: una evidencia de vista no obligatoria sin nota
/// responde 400. Mejor pedirla con la foto recién tomada, cuando la persona todavía está viendo lo que fotografió,
/// que descubrirlo al final con todo el informe hecho.
Future<String?> pedirNotaDeFoto(BuildContext context, {required String vista}) => showDialog<String>(
      context: context,
      builder: (_) => _DialogoNota(vista: vista),
    );

class _DialogoNota extends StatefulWidget {
  const _DialogoNota({required this.vista});

  final String vista;

  @override
  State<_DialogoNota> createState() => _EstadoDialogoNota();
}

class _EstadoDialogoNota extends State<_DialogoNota> {
  final _texto = TextEditingController();

  @override
  void dispose() {
    _texto.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => AlertDialog(
        title: Text(widget.vista),
        content: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text('Esta foto necesita una nota: qué falta o qué daño se ve.', style: Theme.of(context).textTheme.bodySmall),
            const SizedBox(height: Espacio.md),
            TextField(
              controller: _texto,
              autofocus: true,
              maxLines: 3,
              maxLength: 500,
              decoration: const InputDecoration(labelText: 'Nota', hintText: 'Falta el gato / rayón en la cojinería'),
              onChanged: (_) => setState(() {}),
            ),
          ],
        ),
        actions: [
          TextButton(onPressed: () => Navigator.of(context).pop(), child: const Text('Cancelar')),
          FilledButton(
            // Sin nota no se guarda la foto: el servidor la rechazaría y el informe moriría al enviarse.
            onPressed: _texto.text.trim().isEmpty ? null : () => Navigator.of(context).pop(_texto.text.trim()),
            child: const Text('Guardar'),
          ),
        ],
      );
}
