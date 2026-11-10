import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../app/proveedores.dart';
import '../../../core/http/urls_api.dart';
import '../../../core/tema/tema.dart';
import '../../../core/ui/foto_evidencia.dart';
import '../../../core/ui/widgets.dart';
import '../../../core/util/formato.dart';
import '../../catalogos/domain/catalogos.dart';
import '../../catalogos/presentation/catalogos_controller.dart';
import '../../sync/presentation/sync_controller.dart';
import '../../vehiculos/domain/etiquetas.dart';
import '../domain/hallazgo_previo.dart';

/// Con qué venía el vehículo: los daños de sus inspecciones cerradas anteriores, en lectura.
///
/// Es la contraparte en texto de lo que el dibujo pinta rayado. Las dos cosas hacen falta: el dibujo dice **dónde**
/// de un vistazo, y esta lista dice **qué** era y **de dónde viene**, que es lo que permite decidir si el daño que
/// se tiene delante es el mismo o uno nuevo.
///
/// No se edita ni se borra: cada uno pertenece a un informe ya cerrado, que es inmutable. Por eso no hay acciones.
class ListaHallazgosPrevios extends ConsumerWidget {
  const ListaHallazgosPrevios({
    super.key,
    required this.previos,
    this.cargando = false,
    this.alVencerFoto,
    this.titulo = 'Daños anteriores',
  });

  final List<HallazgoPrevio> previos;

  /// Mientras se traen. Se distingue de «no hay» a propósito: decir «sin daños anteriores» antes de saberlo sería
  /// afirmar algo que todavía no consta.
  final bool cargando;

  /// Los enlaces de las fotos vencen; al vencer se vuelve a pedir la lista, que trae unos nuevos.
  final VoidCallback? alVencerFoto;

  final String titulo;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final tema = Theme.of(context).textTheme;
    final catalogos = ref.watch(catalogosProvider);
    final entorno = ref.watch(entornoProvider);
    final sinSenal = !ref.watch(syncControllerProvider).online;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Row(
          children: [
            Expanded(child: Text(titulo, style: tema.titleMedium)),
            if (previos.isNotEmpty)
              Text(
                previos.length == 1 ? '1 daño' : '${previos.length} daños',
                style: tema.bodySmall?.copyWith(color: Colores.textoSecundario),
              ),
          ],
        ),
        const SizedBox(height: Espacio.xs),
        if (cargando)
          Text('Buscando con qué venía el vehículo…', style: tema.bodySmall)
        else if (previos.isEmpty)
          // Un hueco en blanco se lee como «esto no cargó». Decirlo cierra la pregunta —pero solo se puede
          // afirmar que no hay si de verdad se preguntó: sin señal y sin copia, la lista vacía no significa
          // que el vehículo venga sano, y decir que viene sano sería lo peor que puede decir esta pantalla.
          Text(
            sinSenal
                ? 'Sin señal: no se pudo comprobar si trae daños anteriores.'
                : 'Sin daños anteriores registrados.',
            style: tema.bodySmall?.copyWith(color: sinSenal ? Colores.advertencia : null),
          )
        else ...[
          Text(
            'Son de informes ya cerrados: se muestran para comparar, no se pueden cambiar.',
            style: tema.bodySmall?.copyWith(color: Colores.textoSecundario),
          ),
          const SizedBox(height: Espacio.sm),
          for (final previo in previos) ...[
            _FilaPrevio(
              previo: previo,
              catalogos: catalogos,
              urlFoto: resolverUrlImagen(entorno.apiUrl, previo.fotoUrl),
              encabezadosFoto: entorno.encabezadosEnrutamiento,
              alVencerFoto: alVencerFoto,
              sinSenal: sinSenal,
            ),
            const SizedBox(height: Espacio.sm),
          ],
        ],
      ],
    );
  }
}

/// «Izquierdo 7 · Espejo retrovisor» con su clase, tamaño y nivel, y de qué viaje y etapa viene.
class _FilaPrevio extends StatelessWidget {
  const _FilaPrevio({
    required this.previo,
    required this.catalogos,
    required this.urlFoto,
    required this.encabezadosFoto,
    required this.alVencerFoto,
    required this.sinSenal,
  });

  final HallazgoPrevio previo;
  final Catalogos catalogos;
  final String? urlFoto;
  final Map<String, String> encabezadosFoto;
  final VoidCallback? alVencerFoto;
  final bool sinSenal;

  /// Clase, tamaño y nivel, saltando lo que no se sabe. Una novedad del conductor sin revisar no tiene nivel, y
  /// poner «sin nivel» junto a los que sí lo tienen es más claro que dejar el renglón a medias.
  String get _clasificacion => [
        if (previo.clase case final c?) catalogos.etiqueta(Catalogo.clasesDano, c),
        if (previo.tamano case final t?) catalogos.etiqueta(Catalogo.tamanosDano, t),
        previo.nivel == null ? 'Sin calificar' : catalogos.etiqueta(Catalogo.nivelesSeveridad, previo.nivel!),
      ].join(' · ');

  /// De dónde viene: el viaje por su código MTrack, la etapa y el momento. El código MTrack es como el negocio
  /// nombra un viaje; sin él queda «Viaje anterior», que al menos no finge precisión.
  String get _origen => [
        previo.codigoMtrack ?? 'Viaje anterior',
        catalogos.etiqueta(Catalogo.etapas, previo.proceso),
        etiquetar(etiquetaMomento, previo.momento),
        if (previo.cerradaEn != null) formatearFecha(previo.cerradaEn),
      ].join(' · ');

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context).textTheme;
    return Tarjeta(
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          FotoEvidencia(
            url: urlFoto,
            encabezados: encabezadosFoto,
            ancho: 64,
            alto: 56,
            venceEn: previo.fotoVenceEn,
            alVencer: alVencerFoto,
            sinSenal: sinSenal,
            semanticLabel: 'Foto del daño anterior en ${previo.zona}',
          ),
          const SizedBox(width: Espacio.sm),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  previo.zona.isEmpty ? 'Zona sin registrar' : previo.zona,
                  style: tema.bodyMedium?.copyWith(color: Colores.textoPrimario, fontWeight: FontWeight.w600),
                ),
                Text(_clasificacion, style: tema.bodySmall),
                Text(_origen, style: tema.bodySmall?.copyWith(color: Colores.textoSecundario)),
              ],
            ),
          ),
        ],
      ),
    );
  }
}
