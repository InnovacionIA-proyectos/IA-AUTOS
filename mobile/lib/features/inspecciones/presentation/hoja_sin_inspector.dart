import 'package:flutter/material.dart';

import '../../../core/tema/tema.dart';
import '../../catalogos/domain/catalogos.dart';

/// Lo que máximo admite el servidor en el texto del motivo.
const motivoTextoMax = 300;

/// El motivo elegido: el código del catálogo y, si el motivo lo pide, lo que se escribió.
typedef MotivoElegido = ({String codigo, String nombre, String? texto});

/// «No hay inspector»: el conductor elige por qué, en una lista del catálogo, y si el motivo lo pide («Otro») escribe
/// cuál. Devuelve null si se cancela.
///
/// No hay campo de texto libre salvo en el motivo que lo exige, y sin motivo no se sigue: es lo que el servidor pide
/// para dejarle crear la salida del cargue.
Future<MotivoElegido?> pedirMotivoSinInspector(BuildContext context, {required List<ValorCatalogo> motivos}) {
  return showModalBottomSheet<MotivoElegido>(
    context: context,
    isScrollControlled: true,
    showDragHandle: true,
    builder: (_) => _HojaMotivo(motivos: motivos),
  );
}

class _HojaMotivo extends StatefulWidget {
  const _HojaMotivo({required this.motivos});

  final List<ValorCatalogo> motivos;

  @override
  State<_HojaMotivo> createState() => _EstadoHojaMotivo();
}

class _EstadoHojaMotivo extends State<_HojaMotivo> {
  ValorCatalogo? _elegido;
  final _texto = TextEditingController();

  @override
  void dispose() {
    _texto.dispose();
    super.dispose();
  }

  bool get _pideTexto => _elegido?.campo<bool>('requiere_texto') ?? false;

  bool get _completo => _elegido != null && (!_pideTexto || _texto.text.trim().isNotEmpty);

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context).textTheme;
    return Padding(
      padding: EdgeInsets.fromLTRB(Espacio.lg, 0, Espacio.lg, MediaQuery.viewInsetsOf(context).bottom + Espacio.lg),
      child: SingleChildScrollView(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Text('No hay inspector', style: tema.titleMedium),
            Text('Elige por qué: la salida del cargue la haces tú.', style: tema.bodySmall),
            const SizedBox(height: Espacio.md),
            if (widget.motivos.isEmpty)
              Text(
                'Los motivos aún no se han descargado. Conéctate e inténtalo de nuevo.',
                style: tema.bodySmall?.copyWith(color: Colores.advertencia),
              )
            else
              RadioGroup<String>(
                groupValue: _elegido?.codigo,
                onChanged: (codigo) => setState(() {
                  _elegido = widget.motivos.firstWhere((m) => m.codigo == codigo);
                  if (!_pideTexto) _texto.clear();
                }),
                child: Column(
                  children: [
                    for (final motivo in widget.motivos)
                      RadioListTile<String>(
                        value: motivo.codigo!,
                        contentPadding: EdgeInsets.zero,
                        title: Text(motivo.nombre),
                      ),
                  ],
                ),
              ),
            if (_pideTexto) ...[
              const SizedBox(height: Espacio.sm),
              TextField(
                controller: _texto,
                onChanged: (_) => setState(() {}),
                maxLength: motivoTextoMax,
                minLines: 2,
                maxLines: 4,
                textCapitalization: TextCapitalization.sentences,
                decoration: const InputDecoration(labelText: 'Cuál', hintText: 'Escribe el motivo'),
              ),
            ],
            const SizedBox(height: Espacio.sm),
            Semantics(
              liveRegion: true,
              child: Text('Se registrará y se avisará al coordinador.', style: tema.bodySmall?.copyWith(color: Colores.advertencia)),
            ),
            const SizedBox(height: Espacio.md),
            FilledButton(
              onPressed: _completo
                  ? () => Navigator.of(context).pop((
                        codigo: _elegido!.codigo!,
                        nombre: _elegido!.nombre,
                        texto: _pideTexto ? _texto.text.trim() : null,
                      ))
                  : null,
              child: const Text('Continuar con la salida del cargue'),
            ),
          ],
        ),
      ),
    );
  }
}
