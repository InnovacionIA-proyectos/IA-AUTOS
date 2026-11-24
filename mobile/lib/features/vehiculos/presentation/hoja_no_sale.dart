import 'package:flutter/material.dart';

import '../../../core/tema/tema.dart';
import '../../catalogos/domain/catalogos.dart';
import '../domain/inventario.dart';

/// Lo que se decide en la hoja: el motivo de la lista, su texto si lo pide, el daño que lo causa si lo exige, y qué
/// VIN entra en su lugar si ya lo hay.
typedef DatosNoSale = ({String motivoCodigo, String? motivoTexto, String? hallazgoId, String? vinReemplazo});

/// Lo que el servidor admite en el texto del motivo.
const motivoNoSaleMax = 300;

/// Los códigos de `motivos-no-sale` que la app trata aparte: los que piden un daño concreto.
const motivoDanoV1Plus = 'dano_v1_plus';
const motivoDanoConductor = 'dano_reportado_conductor';

/// El VIN como lo guarda el servidor: en mayúsculas y sin espacios.
///
/// Se escribe a mano, 17 caracteres, en un teclado que sugiere y mete espacios; el servidor compara exacto y un
/// espacio de más responde «No existe en el inventario», que parece un problema del dato y es del tecleo.
String normalizarVin(String texto) => texto.replaceAll(RegExp(r'\s'), '').toUpperCase();

/// Pide los datos para marcar que la unidad no sale del viaje (S8-HU-02.2 CA-1). Devuelve null si se cancela.
///
/// Los motivos salen del catálogo del servidor. [hallazgosV1Plus] son los V1+ de inspecciones cerradas y vigentes
/// del viaje: sin uno, «Daño V1+» se ve pero no se elige. [danosDelConductor] son los daños del conductor con foto
/// y sin calificar; «Daño reportado por el conductor» además necesita que el lugar de cargue admita el cargue sin
/// inspector ([admiteSinInspector]), y hoy ninguno lo admite.
Future<DatosNoSale?> pedirNoSale(
  BuildContext context, {
  required Catalogos catalogos,
  List<HallazgoVista> hallazgosV1Plus = const [],
  List<HallazgoVista> danosDelConductor = const [],
  bool admiteSinInspector = false,
}) {
  return showModalBottomSheet<DatosNoSale>(
    context: context,
    isScrollControlled: true,
    showDragHandle: true,
    builder: (_) => _HojaNoSale(
      catalogos: catalogos,
      hallazgosV1Plus: hallazgosV1Plus,
      danosDelConductor: danosDelConductor,
      admiteSinInspector: admiteSinInspector,
    ),
  );
}

class _HojaNoSale extends StatefulWidget {
  const _HojaNoSale({
    required this.catalogos,
    required this.hallazgosV1Plus,
    required this.danosDelConductor,
    required this.admiteSinInspector,
  });

  final Catalogos catalogos;
  final List<HallazgoVista> hallazgosV1Plus;
  final List<HallazgoVista> danosDelConductor;
  final bool admiteSinInspector;

  @override
  State<_HojaNoSale> createState() => _EstadoHojaNoSale();
}

class _EstadoHojaNoSale extends State<_HojaNoSale> {
  String? _codigo;
  String? _hallazgoId;
  final _texto = TextEditingController();
  final _reemplazo = TextEditingController();

  List<ValorCatalogo> get _motivos => widget.catalogos.seleccionables(Catalogo.motivosNoSale);

  ValorCatalogo? get _elegido => _motivos.where((m) => m.codigo == _codigo).firstOrNull;

  @override
  void initState() {
    super.initState();
    _texto.addListener(() => setState(() {}));
  }

  @override
  void dispose() {
    _texto.dispose();
    _reemplazo.dispose();
    super.dispose();
  }

  /// Los daños que puede adjuntar el motivo, o null si no pide ninguno.
  List<HallazgoVista>? _danosDe(ValorCatalogo motivo) => switch (motivo.codigo) {
        motivoDanoV1Plus => widget.hallazgosV1Plus,
        motivoDanoConductor => widget.danosDelConductor,
        _ when motivo.campo<bool>('exige_hallazgo') ?? false => const [],
        _ => null,
      };

  /// Por qué no se puede elegir este motivo, o null si se puede.
  String? _porQueNo(ValorCatalogo motivo) {
    if (motivo.codigo == motivoDanoConductor || (motivo.campo<bool>('solo_sin_inspector') ?? false)) {
      if (!widget.admiteSinInspector) return 'Este lugar de cargue no lo admite';
      if (widget.danosDelConductor.isEmpty) return 'No hay un daño del conductor sin calificar en este carro';
    }
    if (motivo.codigo == motivoDanoV1Plus && widget.hallazgosV1Plus.isEmpty) {
      return 'No hay un daño V1+ en un informe cerrado de este carro';
    }
    if (_danosDe(motivo) case final danos? when danos.isEmpty) return 'No hay un daño que adjuntar';
    return null;
  }

  bool _exigeTexto(ValorCatalogo motivo) => motivo.campo<bool>('exige_texto') ?? false;

  bool get _listo {
    final motivo = _elegido;
    if (motivo == null || _porQueNo(motivo) != null) return false;
    if (_exigeTexto(motivo) && _texto.text.trim().isEmpty) return false;
    if (_danosDe(motivo) != null && _hallazgoId == null) return false;
    return true;
  }

  void _elegir(String? codigo) {
    final motivo = _motivos.where((m) => m.codigo == codigo).firstOrNull;
    if (motivo == null || _porQueNo(motivo) != null) return;
    setState(() {
      _codigo = codigo;
      // Con un solo daño posible no hay nada que elegir: va ese.
      final danos = _danosDe(motivo);
      _hallazgoId = danos != null && danos.length == 1 ? danos.single.id : null;
    });
  }

  String _lineaDano(HallazgoVista h) {
    final clase = h.clase == null ? 'Sin clase' : widget.catalogos.etiqueta(Catalogo.clasesDano, h.clase!);
    return '${h.zona}: $clase';
  }

  DatosNoSale _datos() {
    final motivo = _elegido!;
    final texto = _texto.text.trim();
    return (
      motivoCodigo: motivo.codigo!,
      motivoTexto: _exigeTexto(motivo) && texto.isNotEmpty ? texto : null,
      hallazgoId: _danosDe(motivo) == null ? null : _hallazgoId,
      vinReemplazo: switch (normalizarVin(_reemplazo.text)) {
        '' => null,
        final vin => vin,
      },
    );
  }

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context).textTheme;
    final motivos = _motivos;
    final elegido = _elegido;
    final danos = elegido == null ? null : _danosDe(elegido);
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
                      Text('El viaje sigue sin ella y su puesto queda libre. ¿Por qué no sale?', style: tema.bodySmall),
                      const SizedBox(height: Espacio.sm),
                      if (motivos.isEmpty)
                        Text(
                          'No se han descargado los motivos. Conéctate para descargarlos.',
                          style: tema.bodySmall?.copyWith(color: Colores.advertencia),
                        )
                      else
                        RadioGroup<String>(
                          groupValue: _codigo,
                          onChanged: _elegir,
                          child: Column(
                            mainAxisSize: MainAxisSize.min,
                            children: [
                              for (final motivo in motivos)
                                RadioListTile<String>(
                                  value: motivo.codigo!,
                                  enabled: _porQueNo(motivo) == null,
                                  contentPadding: EdgeInsets.zero,
                                  title: Text(motivo.nombre, style: tema.bodyMedium),
                                  subtitle: switch (_porQueNo(motivo)) {
                                    final porque? => Text(porque, style: tema.bodySmall),
                                    null => null,
                                  },
                                ),
                            ],
                          ),
                        ),
                      if (danos != null && danos.length > 1) ...[
                        const SizedBox(height: Espacio.sm),
                        Text('¿Cuál daño lo causa?', style: tema.bodySmall),
                        RadioGroup<String>(
                          groupValue: _hallazgoId,
                          onChanged: (v) => setState(() => _hallazgoId = v),
                          child: Column(
                            mainAxisSize: MainAxisSize.min,
                            children: [
                              for (final h in danos)
                                RadioListTile<String>(
                                  value: h.id,
                                  contentPadding: EdgeInsets.zero,
                                  title: Text(_lineaDano(h), style: tema.bodySmall),
                                ),
                            ],
                          ),
                        ),
                      ] else if (danos != null && danos.length == 1)
                        Text(
                          'Por: ${_lineaDano(danos.single)}',
                          style: tema.bodySmall?.copyWith(color: Colores.textoPrimario),
                        ),
                      if (elegido != null && _exigeTexto(elegido)) ...[
                        const SizedBox(height: Espacio.sm),
                        TextField(
                          controller: _texto,
                          maxLines: 3,
                          maxLength: motivoNoSaleMax,
                          decoration: const InputDecoration(labelText: 'Motivo', hintText: 'Por qué no sale'),
                        ),
                      ],
                      const SizedBox(height: Espacio.sm),
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
                onPressed: _listo ? () => Navigator.of(context).pop(_datos()) : null,
                child: const Text('Marcar que no sale'),
              ),
            ],
          ),
        ),
      ),
    );
  }
}
