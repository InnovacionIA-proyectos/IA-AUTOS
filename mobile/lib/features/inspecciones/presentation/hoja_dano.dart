import 'package:flutter/material.dart';

import '../../../core/tema/tema.dart';
import '../../catalogos/domain/catalogos.dart';

/// Lo que se elige en la hoja: clase, tamaño, el nivel si quien registra es inspector o administrador, y una
/// observación opcional (null si se deja vacía).
typedef DatosDano = ({String clase, String tamano, String? nivel, String? nota});

/// Con qué abre la hoja. Es [DatosDano] con todo opcional: al revisar la novedad de un conductor llega el tamaño
/// que él eligió y puede que la clase, pero nunca el nivel, que es justo lo que se viene a poner.
typedef DatosDanoIniciales = ({String? clase, String? tamano, String? nivel, String? nota});

/// Lo mismo que admite el servidor en la nota del hallazgo.
const notaDanoMax = 500;

/// Pide la clase, el tamaño y el nivel del daño de la zona elegida. Devuelve null si se cancela.
///
/// Las opciones salen de los catálogos descargados (solo las activas, en su orden), nunca de listas fijas: TI y el
/// coordinador de calidad las administran desde el portal.
///
/// [niveles] vacío quiere decir que no se pide nivel: es el caso del conductor, que no lo asigna (el servidor
/// responde 400 si lo envía). Con [inicial] la hoja abre con lo que ya tenía el hallazgo, para corregirlo.
Future<DatosDano?> pedirDano(
  BuildContext context, {
  required String titulo,
  String? subtitulo,
  required List<ValorCatalogo> clases,
  required List<ValorCatalogo> tamanos,
  List<ValorCatalogo> niveles = const [],
  DatosDanoIniciales? inicial,
}) {
  return showModalBottomSheet<DatosDano>(
    context: context,
    isScrollControlled: true,
    showDragHandle: true,
    builder: (_) => _HojaDano(
      titulo: titulo,
      subtitulo: subtitulo,
      clases: clases,
      tamanos: tamanos,
      niveles: niveles,
      inicial: inicial,
    ),
  );
}

/// Un grupo de opciones del catálogo: su título y sus fichas. Lo usan la hoja del daño de una zona y la del
/// extra dañado, que piden exactamente lo mismo y tienen que verse igual.
class GrupoDeOpciones extends StatelessWidget {
  const GrupoDeOpciones({
    super.key,
    required this.titulo,
    required this.opciones,
    required this.elegido,
    required this.alElegir,
    this.tooltip,
  });

  final String titulo;
  final List<ValorCatalogo> opciones;
  final String? elegido;
  final ValueChanged<String> alElegir;

  /// Lo que se lee al dejar el dedo encima; en los niveles es su acción («Se rechaza el vehículo»).
  final String? Function(ValorCatalogo)? tooltip;

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context).textTheme;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(titulo, style: tema.titleMedium?.copyWith(fontSize: 15)),
        const SizedBox(height: Espacio.sm),
        Wrap(
          spacing: Espacio.sm,
          runSpacing: Espacio.sm,
          children: [
            // Una opcion sin codigo no se puede enviar al servidor, asi que no se ofrece: antes se dejaba ver
            // y al tocarla no pasaba nada.
            for (final o in opciones)
              if (o.codigo case final codigo?)
                ChoiceChip(
                  label: Text(o.nombre),
                  tooltip: tooltip?.call(o),
                  selected: elegido == codigo,
                  onSelected: (_) => alElegir(codigo),
                ),
          ],
        ),
      ],
    );
  }
}

class _HojaDano extends StatefulWidget {
  const _HojaDano({
    required this.titulo,
    required this.subtitulo,
    required this.clases,
    required this.tamanos,
    required this.niveles,
    required this.inicial,
  });

  final String titulo;
  final String? subtitulo;
  final List<ValorCatalogo> clases;
  final List<ValorCatalogo> tamanos;
  final List<ValorCatalogo> niveles;
  final DatosDanoIniciales? inicial;

  @override
  State<_HojaDano> createState() => _EstadoHojaDano();
}

class _EstadoHojaDano extends State<_HojaDano> {
  late String? _clase = widget.inicial?.clase;
  late String? _tamano = widget.inicial?.tamano;
  late String? _nivel = widget.inicial?.nivel;
  late final _nota = TextEditingController(text: widget.inicial?.nota);

  @override
  void dispose() {
    _nota.dispose();
    super.dispose();
  }

  String? get _notaLimpia {
    final texto = _nota.text.trim();
    return texto.isEmpty ? null : texto;
  }

  bool get _completo => _clase != null && _tamano != null && (widget.niveles.isEmpty || _nivel != null);

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
              if (widget.subtitulo case final subtitulo?) Text(subtitulo, style: tema.bodySmall),
              Flexible(
                child: SingleChildScrollView(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      const SizedBox(height: Espacio.md),
                      GrupoDeOpciones(
                        titulo: 'Clase de daño',
                        opciones: widget.clases,
                        elegido: _clase,
                        alElegir: (codigo) => setState(() => _clase = codigo),
                      ),
                      const SizedBox(height: Espacio.lg),
                      GrupoDeOpciones(
                        titulo: 'Tamaño',
                        opciones: widget.tamanos,
                        elegido: _tamano,
                        alElegir: (codigo) => setState(() => _tamano = codigo),
                      ),
                      if (widget.niveles.isNotEmpty) ...[
                        const SizedBox(height: Espacio.lg),
                        GrupoDeOpciones(
                          titulo: 'Nivel de severidad',
                          opciones: widget.niveles,
                          elegido: _nivel,
                          alElegir: (codigo) => setState(() => _nivel = codigo),
                          // La acción del nivel («Se rechaza el vehículo») ayuda a no elegirlo a la ligera.
                          tooltip: (n) => n.campo<String>('accion'),
                        ),
                      ],
                      const SizedBox(height: Espacio.lg),
                      TextField(
                        controller: _nota,
                        maxLength: notaDanoMax,
                        minLines: 1,
                        maxLines: 3,
                        textCapitalization: TextCapitalization.sentences,
                        decoration: const InputDecoration(labelText: 'Observación (opcional)'),
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
                      onPressed: _completo
                          ? () => Navigator.of(context).pop((clase: _clase!, tamano: _tamano!, nivel: _nivel, nota: _notaLimpia))
                          : null,
                      child: Text(widget.inicial == null ? 'Agregar' : 'Guardar cambios'),
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
