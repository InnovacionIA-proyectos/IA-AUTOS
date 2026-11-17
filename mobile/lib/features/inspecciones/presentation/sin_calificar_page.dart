import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/errores/error_api.dart';
import '../../../core/tema/tema.dart';
import '../../../core/ui/widgets.dart';
import '../../catalogos/presentation/catalogos_controller.dart';
import '../../vehiculos/presentation/vin_page.dart' show inventarioProvider;
import '../domain/novedad_pendiente.dart';
import '../providers.dart';
import 'revision_novedad_page.dart';

/// Los daños del viaje que todavía nadie calificó, del más antiguo al más reciente.
///
/// Sin nivel solo quedan las novedades que registró el conductor: él marca el daño y su tamaño, pero la zona, la
/// clase y el nivel los pone quien inspecciona. Hasta entonces el hallazgo existe, cuenta y se ve marcado, pero
/// no decide nada sobre el vehículo.
///
/// Van de lo más viejo a lo más nuevo a propósito: lo que lleva más tiempo sin resolverse es lo que más urge, y
/// es lo contrario del orden de la ficha, que muestra lo último que pasó.
class SinCalificarPage extends ConsumerWidget {
  const SinCalificarPage({super.key, required this.viajeId, required this.codigoMtrack});

  final String viajeId;

  /// Para el título: quien llega aquí viene de un viaje concreto y tiene que saber de cuál.
  final String codigoMtrack;

  Future<void> _calificar(BuildContext context, WidgetRef ref, NovedadPendiente novedad) async {
    final revisada = await Navigator.of(context).push<bool>(
      MaterialPageRoute(builder: (_) => RevisionNovedadPage(novedad: novedad)),
    );
    if (revisada != true) return;
    // Solo lo de ese daño: la pantalla no se rearma entera, el calificado simplemente deja de estar pendiente y
    // desaparece de aquí. La ficha del VIN también cambia —el hallazgo pasa a tener nivel— y puede estar viva en
    // otra pestaña del shell, así que se invalida igual que hace la ficha al revisar desde ella.
    ref.invalidate(sinCalificarDelViajeProvider(viajeId));
    ref.invalidate(novedadesPorRevisarProvider(novedad.vin));
    ref.invalidate(inventarioProvider(novedad.vin));
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final tema = Theme.of(context).textTheme;
    return Scaffold(
      appBar: AppBar(title: Text('Sin calificar · $codigoMtrack')),
      body: SafeArea(
        child: switch (ref.watch(sinCalificarDelViajeProvider(viajeId))) {
          AsyncLoading() => const EstadoCargando(),
          AsyncError(:final error) => EstadoMensaje(
              icono: Icons.warning_amber_rounded,
              colorIcono: Colores.peligro,
              titulo: 'No se pudieron cargar',
              descripcion: mensajeDeError(error, 'Los daños sin calificar se consultan con señal.'),
              accion: FilledButton(
                onPressed: () => ref.invalidate(sinCalificarDelViajeProvider(viajeId)),
                child: const Text('Reintentar'),
              ),
            ),
          AsyncData(value: final novedades) when novedades.isEmpty => const EstadoMensaje(
              icono: Icons.check_circle_outline,
              colorIcono: Colores.exito,
              titulo: 'Nada sin calificar',
              descripcion: 'Todos los daños de las unidades de este viaje ya tienen su clase y su nivel.',
            ),
          AsyncData(value: final novedades) => RefreshIndicator(
              onRefresh: () async => ref.invalidate(sinCalificarDelViajeProvider(viajeId)),
              child: ListView.separated(
                padding: const EdgeInsets.all(Espacio.lg),
                itemCount: novedades.length + 1,
                separatorBuilder: (_, _) => const SizedBox(height: Espacio.sm),
                itemBuilder: (_, i) => i == 0
                    ? Padding(
                        padding: const EdgeInsets.only(bottom: Espacio.sm),
                        child: Text(
                          novedades.length == 1
                              ? 'Falta calificar 1 daño. Lo registró el conductor: le falta la zona, la clase y el nivel.'
                              : 'Faltan calificar ${novedades.length} daños. Los registró el conductor: les falta la '
                                  'zona, la clase y el nivel.',
                          style: tema.bodySmall,
                        ),
                      )
                    : _Fila(
                        novedad: novedades[i - 1],
                        alCalificar: () => _calificar(context, ref, novedades[i - 1]),
                      ),
              ),
            ),
        },
      ),
    );
  }
}

class _Fila extends ConsumerWidget {
  const _Fila({required this.novedad, required this.alCalificar});

  final NovedadPendiente novedad;
  final VoidCallback alCalificar;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final tema = Theme.of(context).textTheme;
    final catalogos = ref.watch(catalogosProvider);
    return Tarjeta(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          // El VIN va primero: aquí se mira una lista de varias unidades, no la ficha de una.
          TextoMono(novedad.vin),
          Text(etiquetaNovedad(novedad, catalogos), style: tema.bodyMedium?.copyWith(color: Colores.textoPrimario)),
          Text(detalleNovedad(novedad), style: tema.bodySmall),
          const SizedBox(height: Espacio.sm),
          FilledButton(onPressed: alCalificar, child: const Text('Calificar')),
        ],
      ),
    );
  }
}
