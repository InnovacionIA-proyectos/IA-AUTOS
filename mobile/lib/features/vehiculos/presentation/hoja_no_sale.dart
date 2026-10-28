import 'package:flutter/material.dart';

import '../../../core/tema/tema.dart';
import '../../catalogos/domain/catalogos.dart';
import '../domain/inventario.dart';

/// Lo que se decide en la hoja: con qué hallazgo no sale, por qué, y qué VIN entra en su lugar si ya lo hay.
typedef DatosNoSale = ({String hallazgoId, String motivo, String? vinReemplazo});

/// Lo que el servidor admite en el motivo.
const motivoNoSaleMax = 500;

/// El VIN como lo guarda el servidor: en mayúsculas y sin espacios.
///
/// Se escribe a mano, 17 caracteres, en un teclado que sugiere y mete espacios; el servidor compara exacto y un
/// espacio de más responde «No existe en el inventario», que parece un problema del dato y es del tecleo.
String normalizarVin(String texto) => texto.replaceAll(RegExp(r'\s'), '').toUpperCase();

/// Pide los datos para marcar que la unidad no sale del viaje. Devuelve null si se cancela.
///
/// [hallazgos] son los V1+ de inspecciones cerradas del viaje: son los únicos que el servidor acepta como causa.
/// Con uno solo ya viene elegido, que es el caso normal.
Future<DatosNoSale?> pedirNoSale(
  BuildContext context, {
  required List<HallazgoVista> hallazgos,
  required Catalogos catalogos,
}) {
  return showModalBottomSheet<DatosNoSale>(
    context: context,
    isScrollControlled: true,
    showDragHandle: true,
    builder: (_) => _HojaNoSale(hallazgos: hallazgos, catalogos: catalogos),
  );
}

class _HojaNoSale extends StatefulWidget {
  const _HojaNoSale({required this.hallazgos, required this.catalogos});

  final List<HallazgoVista> hallazgos;
  final Catalogos catalogos;

  @override
  State<_HojaNoSale> createState() => _EstadoHojaNoSale();
}

class _EstadoHojaNoSale extends State<_HojaNoSale> {
  late String _hallazgoId = widget.hallazgos.first.id;
  final _motivo = TextEditingController();
  final _reemplazo = TextEditingController();

  @override
  void initState() {
    super.initState();
    _motivo.addListener(() => setState(() {}));
  }

  @override
  void dispose() {
    _motivo.dispose();
    _reemplazo.dispose();
    super.dispose();
  }

  bool get _listo => _motivo.text.trim().isNotEmpty;

  String _texto(HallazgoVista h) {
    final clase = h.clase == null ? 'Sin clase' : widget.catalogos.etiqueta(Catalogo.clasesDano, h.clase!);
    return '${h.zona}: $clase';
  }

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context).textTheme;
    return Padding(
      // Sube con el teclado: la hoja tiene dos campos de texto y sin esto el de abajo queda tapado.
      padding: EdgeInsets.only(bottom: MediaQuery.viewInsetsOf(context).bottom),
      child: SafeArea(
        child: Padding(
          padding: const EdgeInsets.fromLTRB(Espacio.lg, 0, Espacio.lg, Espacio.lg),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Flexible(
                child: SingleChildScrollView(
                  child: Column(
                    mainAxisSize: MainAxisSize.min,
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      Text('La unidad no sale', style: tema.titleMedium),
                      const SizedBox(height: Espacio.xs),
                      Text(
                        'El viaje sigue sin ella. Queda registrada con su motivo y el daño que lo causa.',
                        style: tema.bodySmall,
                      ),
                      const SizedBox(height: Espacio.md),
                      // Con un solo V1+ no hay nada que elegir: se dice cuál es y ya.
                      if (widget.hallazgos.length == 1)
                        Text('Por: ${_texto(widget.hallazgos.single)}',
                            style: tema.bodySmall?.copyWith(color: Colores.textoPrimario))
                      else ...[
                        Text('¿Cuál daño lo causa?', style: tema.bodySmall),
                        RadioGroup<String>(
                          groupValue: _hallazgoId,
                          onChanged: (v) => setState(() => _hallazgoId = v ?? _hallazgoId),
                          child: Column(
                            mainAxisSize: MainAxisSize.min,
                            children: [
                              for (final h in widget.hallazgos)
                                RadioListTile<String>(
                                  value: h.id,
                                  contentPadding: EdgeInsets.zero,
                                  title: Text(_texto(h), style: tema.bodySmall),
                                ),
                            ],
                          ),
                        ),
                      ],
                      const SizedBox(height: Espacio.md),
                      TextField(
                        controller: _motivo,
                        maxLines: 3,
                        maxLength: motivoNoSaleMax,
                        decoration: const InputDecoration(labelText: 'Motivo', hintText: 'Por qué no sale'),
                      ),
                      TextField(
                        controller: _reemplazo,
                        textCapitalization: TextCapitalization.characters,
                        autocorrect: false,
                        enableSuggestions: false,
                        decoration: const InputDecoration(
                          labelText: 'VIN de reemplazo (opcional)',
                          helperText: 'Si ya hay otra unidad que la reemplaza, entra al viaje con sus etapas',
                        ),
                      ),
                    ],
                  ),
                ),
              ),
              const SizedBox(height: Espacio.lg),
              FilledButton(
                onPressed: _listo
                    ? () => Navigator.of(context).pop((
                        hallazgoId: _hallazgoId,
                        motivo: _motivo.text.trim(),
                        vinReemplazo: switch (normalizarVin(_reemplazo.text)) {
                          '' => null,
                          final vin => vin,
                        },
                      ))
                    : null,
                child: const Text('Marcar que no sale'),
              ),
            ],
          ),
        ),
      ),
    );
  }
}
