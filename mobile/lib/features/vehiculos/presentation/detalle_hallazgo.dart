import 'package:flutter/material.dart';

import '../../../core/tema/tema.dart';
import '../../../core/ui/foto_evidencia.dart';
import '../../../core/ui/visor_imagen.dart';
import '../../catalogos/domain/catalogos.dart';
import '../domain/inventario.dart';

/// Abre el detalle de un daño ya registrado: dónde está, qué es, con qué nivel, si ya venía de antes y su foto.
///
/// Es solo lectura: desde la pantalla del VIN nadie corrige un informe cerrado.
Future<void> mostrarDetalleHallazgo(
  BuildContext context, {
  required HallazgoVista hallazgo,
  required Catalogos catalogos,
  String? urlFoto,
  DateTime? venceFoto,
  VoidCallback? alVencerFoto,
  bool sinSenal = false,
}) {
  return showModalBottomSheet<void>(
    context: context,
    isScrollControlled: true,
    showDragHandle: true,
    builder: (_) => _DetalleHallazgo(
      hallazgo: hallazgo,
      catalogos: catalogos,
      urlFoto: urlFoto,
      venceFoto: venceFoto,
      alVencerFoto: alVencerFoto,
      sinSenal: sinSenal,
    ),
  );
}

class _DetalleHallazgo extends StatelessWidget {
  const _DetalleHallazgo({
    required this.hallazgo,
    required this.catalogos,
    required this.urlFoto,
    required this.venceFoto,
    required this.alVencerFoto,
    required this.sinSenal,
  });

  final HallazgoVista hallazgo;
  final Catalogos catalogos;
  final String? urlFoto;
  final DateTime? venceFoto;
  final VoidCallback? alVencerFoto;
  final bool sinSenal;

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context).textTheme;
    final clase = hallazgo.clase == null ? 'Sin clase' : catalogos.etiqueta(Catalogo.clasesDano, hallazgo.clase!);
    // Sin nivel es la novedad de un conductor que todavía no revisa un inspector.
    final nivel = hallazgo.nivel == null ? 'Por revisar' : catalogos.etiqueta(Catalogo.nivelesSeveridad, hallazgo.nivel!);

    return SafeArea(
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
                    Text(hallazgo.zona, style: tema.titleMedium),
                    const SizedBox(height: Espacio.xs),
                    Text(
                      '$clase · ${catalogos.etiqueta(Catalogo.tamanosDano, hallazgo.tamano)} · $nivel',
                      style: tema.bodySmall?.copyWith(color: Colores.textoPrimario),
                    ),
                    if (hallazgo.loRegistroElConductor)
                      Text('Origen: conductor', style: tema.bodySmall?.copyWith(color: Colores.textoSecundario)),
                    if (hallazgo.esPreexistente)
                      Text('Ya venía de antes', style: tema.bodySmall?.copyWith(color: Colores.textoSecundario)),
                    if (urlFoto != null) ...[
                      const SizedBox(height: Espacio.md),
                      GestureDetector(
                        // El enlace va firmado: el visor lo abre igual, sin encabezados.
                        onTap: () => mostrarVisorImagen(
                          context,
                          imagen: NetworkImage(urlFoto!),
                          titulo: hallazgo.zona,
                        ),
                        child: FotoEvidencia(
                          url: urlFoto,
                          alto: 200,
                          venceEn: venceFoto,
                          alVencer: alVencerFoto,
                          sinSenal: sinSenal,
                          semanticLabel: 'Foto del daño en ${hallazgo.zona}',
                        ),
                      ),
                    ],
                  ],
                ),
              ),
            ),
            // La ficha necesita salida propia: arrastrarla hacia abajo no se adivina.
            Align(
              alignment: Alignment.centerRight,
              child: TextButton(onPressed: () => Navigator.of(context).pop(), child: const Text('Cerrar')),
            ),
          ],
        ),
      ),
    );
  }
}
