import 'dart:io';

import 'package:flutter/material.dart';

import '../../../core/tema/tema.dart';
import '../../../core/ui/visor_imagen.dart';
import '../../../core/ui/widgets.dart';
import '../../catalogos/domain/catalogos.dart';
import '../domain/extra_local.dart';

/// Lo que se elige en la hoja de un extra. Las fotos se toman después, desde su fila.
typedef DatosExtra = ({String tipo, String nombre, String? textoOtro, String? observacion});

/// Pide el tipo de extra y su observación. Devuelve null si se cancela.
///
/// Los tipos salen del catálogo (solo los activos, en su orden) y siempre se ofrece «Otro», que exige decir qué es:
/// un elemento que nadie configuró se reporta igual, sin esperar a que Calidad lo agregue.
Future<DatosExtra?> pedirExtra(BuildContext context, {required List<ValorCatalogo> tipos}) {
  return showModalBottomSheet<DatosExtra>(
    context: context,
    isScrollControlled: true,
    showDragHandle: true,
    builder: (_) => _HojaExtra(tipos: [...tipos.where((t) => t.codigo != tipoExtraOtro)]),
  );
}

class _HojaExtra extends StatefulWidget {
  const _HojaExtra({required this.tipos});

  final List<ValorCatalogo> tipos;

  @override
  State<_HojaExtra> createState() => _EstadoHojaExtra();
}

class _EstadoHojaExtra extends State<_HojaExtra> {
  String? _tipo;
  final _textoOtro = TextEditingController();
  final _observacion = TextEditingController();

  @override
  void dispose() {
    _textoOtro.dispose();
    _observacion.dispose();
    super.dispose();
  }

  bool get _esOtro => _tipo == tipoExtraOtro;

  bool get _completo => _tipo != null && (!_esOtro || _textoOtro.text.trim().isNotEmpty);

  DatosExtra _datos() {
    final observacion = _observacion.text.trim();
    final textoOtro = _textoOtro.text.trim();
    return (
      tipo: _tipo!,
      nombre: _esOtro ? 'Otro: $textoOtro' : widget.tipos.firstWhere((t) => t.codigo == _tipo).nombre,
      textoOtro: _esOtro ? textoOtro : null,
      observacion: observacion.isEmpty ? null : observacion,
    );
  }

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context).textTheme;
    return ConstrainedBox(
      constraints: BoxConstraints(maxHeight: MediaQuery.sizeOf(context).height * 0.85),
      child: SafeArea(
        child: Padding(
          // El teclado no tapa los campos de texto.
          padding: EdgeInsets.fromLTRB(Espacio.lg, 0, Espacio.lg, Espacio.lg + MediaQuery.viewInsetsOf(context).bottom),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text('Agregar extra', style: tema.titleMedium),
              Text('Lo que el dibujo no trae. No se marca en el dibujo.', style: tema.bodySmall),
              Flexible(
                child: SingleChildScrollView(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      const SizedBox(height: Espacio.md),
                      Wrap(
                        spacing: Espacio.sm,
                        runSpacing: Espacio.sm,
                        children: [
                          for (final t in widget.tipos)
                            ChoiceChip(
                              label: Text(t.nombre),
                              selected: _tipo == t.codigo,
                              onSelected: (_) => setState(() => _tipo = t.codigo),
                            ),
                          ChoiceChip(
                            label: const Text('Otro'),
                            selected: _esOtro,
                            onSelected: (_) => setState(() => _tipo = tipoExtraOtro),
                          ),
                        ],
                      ),
                      if (_esOtro) ...[
                        const SizedBox(height: Espacio.md),
                        TextField(
                          controller: _textoOtro,
                          autofocus: true,
                          maxLength: 100,
                          textCapitalization: TextCapitalization.sentences,
                          onChanged: (_) => setState(() {}),
                          decoration: const InputDecoration(labelText: '¿Qué es?', hintText: 'Por ejemplo: portabicicletas'),
                        ),
                      ],
                      const SizedBox(height: Espacio.md),
                      TextField(
                        controller: _observacion,
                        maxLength: observacionExtraMax,
                        minLines: 1,
                        maxLines: 3,
                        textCapitalization: TextCapitalization.sentences,
                        decoration: const InputDecoration(labelText: 'Observación (opcional)'),
                      ),
                    ],
                  ),
                ),
              ),
              const SizedBox(height: Espacio.md),
              Row(
                children: [
                  Expanded(child: OutlinedButton(onPressed: () => Navigator.of(context).pop(), child: const Text('Cancelar'))),
                  const SizedBox(width: Espacio.md),
                  Expanded(
                    child: FilledButton(
                      onPressed: _completo ? () => Navigator.of(context).pop(_datos()) : null,
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

/// Los extras del informe: cada uno con su observación, sus fotos y la opción de tomar otra o quitarlo.
class SeccionExtras extends StatelessWidget {
  const SeccionExtras({
    super.key,
    required this.extras,
    required this.alAgregar,
    required this.alTomarFoto,
    required this.alQuitar,
  });

  final List<ExtraLocal> extras;

  /// Null mientras se guarda una foto: no se abre otra cámara encima.
  final VoidCallback? alAgregar;
  final void Function(int indice)? alTomarFoto;
  final void Function(int indice) alQuitar;

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context).textTheme;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Text('Extras del vehículo', style: tema.titleMedium),
        Text('Barras de techo, volco, platón u otro elemento que el dibujo no trae, con sus fotos.', style: tema.bodySmall),
        for (var i = 0; i < extras.length; i++) ...[
          const SizedBox(height: Espacio.sm),
          _FilaExtra(
            extra: extras[i],
            alTomarFoto: alTomarFoto == null ? null : () => alTomarFoto!(i),
            alQuitar: () => alQuitar(i),
          ),
        ],
        const SizedBox(height: Espacio.sm),
        OutlinedButton.icon(onPressed: alAgregar, icon: const Icon(Icons.add), label: const Text('Agregar extra')),
      ],
    );
  }
}

class _FilaExtra extends StatelessWidget {
  const _FilaExtra({required this.extra, required this.alTomarFoto, required this.alQuitar});

  final ExtraLocal extra;
  final VoidCallback? alTomarFoto;
  final VoidCallback alQuitar;

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context).textTheme;
    return Tarjeta(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Expanded(child: Text(extra.nombre, style: tema.bodyMedium?.copyWith(color: Colores.textoPrimario))),
              TextButton(
                onPressed: alQuitar,
                child: Semantics(label: 'Quitar extra ${extra.nombre}', child: const Text('Quitar', style: TextStyle(color: Colores.peligro))),
              ),
            ],
          ),
          if (extra.observacion case final observacion?) Text(observacion, style: tema.bodySmall),
          const SizedBox(height: Espacio.sm),
          Wrap(
            spacing: Espacio.sm,
            runSpacing: Espacio.sm,
            crossAxisAlignment: WrapCrossAlignment.center,
            children: [
              for (final foto in extra.fotos)
                GestureDetector(
                  onTap: () => mostrarVisorImagen(context, imagen: FileImage(File(foto.ruta)), titulo: extra.nombre),
                  child: ClipRRect(
                    borderRadius: BorderRadius.circular(8),
                    child: Image.file(
                      File(foto.ruta),
                      width: 56,
                      height: 48,
                      fit: BoxFit.cover,
                      semanticLabel: 'Foto del extra ${extra.nombre}',
                    ),
                  ),
                ),
              TextButton.icon(
                onPressed: alTomarFoto,
                icon: const Icon(Icons.photo_camera_outlined, size: 18),
                label: Text(extra.fotos.isEmpty ? 'Tomar foto' : 'Otra foto'),
              ),
            ],
          ),
          if (extra.fotos.isEmpty)
            Text('Sin foto no se puede cerrar el informe.', style: tema.bodySmall?.copyWith(color: Colores.advertencia)),
        ],
      ),
    );
  }
}
