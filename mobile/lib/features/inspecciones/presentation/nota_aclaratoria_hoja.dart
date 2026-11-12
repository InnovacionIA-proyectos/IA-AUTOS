import 'package:flutter/material.dart';

import '../../../core/tema/tema.dart';
import '../domain/nota_aclaratoria.dart';

/// Pide el texto de una nota aclaratoria. Devuelve null si se cancela.
///
/// Es obligatorio: una nota en blanco no aclara nada, y el servidor la rechaza.
Future<String?> pedirNotaAclaratoria(BuildContext context, {required String sobre}) {
  return showModalBottomSheet<String>(
    context: context,
    isScrollControlled: true,
    showDragHandle: true,
    builder: (_) => _HojaNota(sobre: sobre),
  );
}

class _HojaNota extends StatefulWidget {
  const _HojaNota({required this.sobre});

  /// Qué se está aclarando, para que quien escribe sepa a qué queda pegada la nota.
  final String sobre;

  @override
  State<_HojaNota> createState() => _EstadoHojaNota();
}

class _EstadoHojaNota extends State<_HojaNota> {
  final _texto = TextEditingController();

  @override
  void initState() {
    super.initState();
    _texto.addListener(() => setState(() {}));
  }

  @override
  void dispose() {
    _texto.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context).textTheme;
    return Padding(
      // Sube con el teclado: si no, el campo queda tapado justo mientras se escribe.
      padding: EdgeInsets.only(bottom: MediaQuery.viewInsetsOf(context).bottom),
      child: SafeArea(
        child: Padding(
          padding: const EdgeInsets.fromLTRB(Espacio.lg, 0, Espacio.lg, Espacio.lg),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Text('Nota aclaratoria', style: tema.titleMedium),
              const SizedBox(height: Espacio.xs),
              Text(
                'Sobre ${widget.sobre}. El informe no cambia: la nota queda al lado, con tu nombre y la fecha, y '
                'después nadie puede borrarla.',
                style: tema.bodySmall,
              ),
              const SizedBox(height: Espacio.md),
              TextField(
                controller: _texto,
                autofocus: true,
                minLines: 3,
                maxLines: 6,
                maxLength: notaAclaratoriaMax,
                textCapitalization: TextCapitalization.sentences,
                decoration: const InputDecoration(labelText: 'Qué hay que aclarar'),
              ),
              const SizedBox(height: Espacio.sm),
              FilledButton(
                onPressed: _texto.text.trim().isEmpty ? null : () => Navigator.of(context).pop(_texto.text.trim()),
                child: const Text('Agregar la nota'),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

/// «Ana Pérez · 29 oct 2026, 2:30 p. m.» — quién la escribió y cuándo, que es lo que le da valor.
class FilaNotaAclaratoria extends StatelessWidget {
  const FilaNotaAclaratoria({super.key, required this.nota, required this.cuando});

  final NotaAclaratoria nota;

  /// La fecha ya formateada por quien construye la fila.
  final String cuando;

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context).textTheme;
    return Padding(
      padding: const EdgeInsets.only(top: Espacio.sm),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(nota.texto, style: tema.bodySmall?.copyWith(color: Colores.textoPrimario)),
          Text('${nota.autorNombre ?? 'Alguien'} · $cuando', style: tema.bodySmall),
        ],
      ),
    );
  }
}
