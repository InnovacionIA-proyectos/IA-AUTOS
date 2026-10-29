import 'package:flutter/material.dart';

import '../../../core/tema/tema.dart';
import '../../../core/ui/foto_evidencia.dart';
import '../../catalogos/domain/catalogos.dart';
import '../domain/hallazgo_previo.dart';

/// Pregunta si el daño que se acaba de marcar es el que ya traía el vehículo.
///
/// Devuelve `true` si quien inspecciona confirma que es el mismo (el hallazgo viaja con `es_preexistente` y el
/// `hallazgo_origen_id` del previo), `false` si dice que es nuevo, y null si se sale sin responder, que cancela el
/// registro: dejarlo pasar sin respuesta sería decidir por esa persona.
Future<bool?> preguntarPreexistente(
  BuildContext context, {
  required HallazgoPrevio previo,
  required Catalogos catalogos,
  String? urlFoto,
  VoidCallback? alVencerFoto,
  bool sinSenal = false,
}) {
  return showModalBottomSheet<bool>(
    context: context,
    // Con la foto dentro, la hoja no cabe en el alto que se da por defecto y los botones quedan fuera de la
    // pantalla. Con esto pide el alto que necesita, y el cuerpo se desplaza si aun asi no alcanza.
    isScrollControlled: true,
    showDragHandle: true,
    builder: (_) => _HojaPreexistente(
      previo: previo,
      catalogos: catalogos,
      urlFoto: urlFoto,
      alVencerFoto: alVencerFoto,
      sinSenal: sinSenal,
    ),
  );
}

/// «Ya estaba registrado en Cargue (Izquierdo 7 · Espejo retrovisor, Golpe)».
String textoPreexistente(HallazgoPrevio previo, Catalogos catalogos) {
  final proceso = catalogos.etiqueta(Catalogo.etapas, previo.proceso);
  final clase = previo.clase == null ? 'sin clase' : catalogos.etiqueta(Catalogo.clasesDano, previo.clase!);
  return 'Ya estaba registrado en $proceso (${previo.zona}, $clase)';
}

class _HojaPreexistente extends StatelessWidget {
  const _HojaPreexistente({
    required this.previo,
    required this.catalogos,
    required this.urlFoto,
    required this.alVencerFoto,
    required this.sinSenal,
  });

  final HallazgoPrevio previo;
  final Catalogos catalogos;
  final String? urlFoto;
  final VoidCallback? alVencerFoto;
  final bool sinSenal;

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context).textTheme;
    return SafeArea(
      child: Padding(
        padding: const EdgeInsets.fromLTRB(Espacio.lg, 0, Espacio.lg, Espacio.lg),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            // Los botones van fuera del desplazamiento: son la decisión, y siempre tienen que estar a la vista.
            Flexible(
              child: SingleChildScrollView(
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    Text(textoPreexistente(previo, catalogos), style: tema.titleMedium),
                    const SizedBox(height: Espacio.xs),
                    Text(
                      'Compara con el vehículo: si es el mismo daño, confírmalo y el informe no lo cuenta como nuevo.',
                      style: tema.bodySmall,
                    ),
                    if (urlFoto != null) ...[
                      const SizedBox(height: Espacio.md),
                      FotoEvidencia(
                        url: urlFoto,
                        alto: 180,
                        venceEn: previo.fotoVenceEn,
                        alVencer: alVencerFoto,
                        sinSenal: sinSenal,
                        semanticLabel: 'Foto del daño registrado antes',
                      ),
                    ],
                  ],
                ),
              ),
            ),
            const SizedBox(height: Espacio.lg),
            FilledButton(
              onPressed: () => Navigator.of(context).pop(true),
              child: const Text('Es el mismo daño'),
            ),
            const SizedBox(height: Espacio.sm),
            OutlinedButton(
              onPressed: () => Navigator.of(context).pop(false),
              child: const Text('Es nuevo'),
            ),
          ],
        ),
      ),
    );
  }
}
