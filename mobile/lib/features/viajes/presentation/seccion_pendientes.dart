import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/errores/error_api.dart';
import '../../../core/tema/tema.dart';
import '../../../core/ui/widgets.dart';
import '../domain/pendiente.dart';
import '../providers.dart';
import 'detalle_pendiente_page.dart';
import 'mis_viajes_controller.dart';

/// Los camiones pendientes de los puntos del inspector, con el botón «Revisar este».
///
/// Reclamar y liberar van directo a la API: lo decide el servidor, y si otro inspector se adelantó se dice ahí
/// mismo y la lista se actualiza sin perder nada de lo que había.
class SeccionPendientes extends ConsumerWidget {
  const SeccionPendientes({super.key});

  Future<void> _reclamar(BuildContext context, WidgetRef ref, Pendiente pendiente) async {
    final mensajes = ScaffoldMessenger.of(context);
    try {
      await ref.read(reclamosRepositorioProvider).reclamar(pendiente.viajeId, pendiente.proceso);
    } catch (e) {
      // El motivo del servidor, tal cual («Ya fue tomado por otro inspector»): es lo único que explica por qué no.
      mensajes.showSnackBar(SnackBar(content: Text(mensajeDeError(e, 'No se pudo reclamar. Inténtalo de nuevo.'))));
      ref.invalidate(pendientesProvider);
      return;
    }
    ref.invalidate(pendientesProvider);
    // Reclamar lo hace inspector del viaje: sus unidades pasan a «Mis viajes».
    await ref.read(misViajesControllerProvider.notifier).cargar();
    if (context.mounted) await _abrir(context, ref, pendiente);
  }

  Future<void> _abrir(BuildContext context, WidgetRef ref, Pendiente pendiente) async {
    await Navigator.of(context).push<void>(MaterialPageRoute(builder: (_) => DetallePendientePage(pendiente: pendiente)));
    ref.invalidate(pendientesProvider);
    await ref.read(misViajesControllerProvider.notifier).cargar();
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final tema = Theme.of(context).textTheme;
    final pendientes = ref.watch(pendientesProvider);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Row(
          children: [
            Expanded(child: Text('Pendientes de mi punto', style: tema.titleMedium)),
            IconButton(
              tooltip: 'Actualizar pendientes',
              onPressed: () => ref.invalidate(pendientesProvider),
              icon: const Icon(Icons.refresh),
            ),
          ],
        ),
        pendientes.when(
          loading: () => const Padding(padding: EdgeInsets.all(Espacio.lg), child: Center(child: CircularProgressIndicator())),
          error: (_, _) => Tarjeta(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  'No se pudieron consultar los pendientes. Se consultan con señal porque cada camión lo reclama una sola persona.',
                  style: tema.bodySmall,
                ),
                TextButton(onPressed: () => ref.invalidate(pendientesProvider), child: const Text('Reintentar')),
              ],
            ),
          ),
          // S8-HU-10.2: dos listas, siempre visibles. Cada pendiente es un (viaje, proceso), así que cae en una sola:
          // lo que no es entrega se carga (un proceso desconocido también, como lo lee `Pendiente.fromJson`).
          data: (lista) => Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              _ListaPendientes(
                titulo: 'Por recibir',
                vacia: 'Nada por recibir',
                pendientes: [for (final p in lista) if (p.esEntrega) p],
                alReclamar: (p) => _reclamar(context, ref, p),
                alAbrir: (p) => _abrir(context, ref, p),
              ),
              const SizedBox(height: Espacio.md),
              _ListaPendientes(
                titulo: 'Por cargar',
                vacia: 'Nada por cargar',
                pendientes: [for (final p in lista) if (!p.esEntrega) p],
                alReclamar: (p) => _reclamar(context, ref, p),
                alAbrir: (p) => _abrir(context, ref, p),
              ),
            ],
          ),
        ),
      ],
    );
  }
}

/// Una de las dos listas del punto, con su conteo. Vacía no desaparece: dice que no hay nada.
class _ListaPendientes extends StatelessWidget {
  const _ListaPendientes({
    required this.titulo,
    required this.vacia,
    required this.pendientes,
    required this.alReclamar,
    required this.alAbrir,
  });

  final String titulo;
  final String vacia;
  final List<Pendiente> pendientes;
  final void Function(Pendiente) alReclamar;
  final void Function(Pendiente) alAbrir;

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context).textTheme;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Padding(
          padding: const EdgeInsets.only(bottom: Espacio.sm),
          child: Text('$titulo (${pendientes.length})', style: tema.titleSmall),
        ),
        if (pendientes.isEmpty)
          Tarjeta(child: Text(vacia, style: tema.bodySmall))
        else
          for (final pendiente in pendientes) ...[
            _TarjetaPendiente(
              pendiente: pendiente,
              alReclamar: () => alReclamar(pendiente),
              alAbrir: () => alAbrir(pendiente),
            ),
            const SizedBox(height: Espacio.sm),
          ],
      ],
    );
  }
}

class _TarjetaPendiente extends StatelessWidget {
  const _TarjetaPendiente({required this.pendiente, required this.alReclamar, required this.alAbrir});

  final Pendiente pendiente;
  final VoidCallback alReclamar;
  final VoidCallback alAbrir;

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context).textTheme;
    final hora = pendiente.fechaProgramada?.toLocal();
    final detalle = [
      pendiente.esEntrega ? 'Entrega' : 'Cargue',
      pendiente.lugar,
      if (hora != null) '${hora.hour.toString().padLeft(2, '0')}:${hora.minute.toString().padLeft(2, '0')}',
      if (pendiente.unidades > 0) '${pendiente.unidades} ${pendiente.unidades == 1 ? 'unidad' : 'unidades'}',
    ].where((p) => p.isNotEmpty).join(' · ');

    return Tarjeta(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              Expanded(child: TextoMono(pendiente.placa ?? pendiente.codigoMtrack ?? 'Sin placa')),
              switch (pendiente.estado) {
                EstadoPendiente.disponible => const Insignia('Disponible', tono: TonoInsignia.exito),
                EstadoPendiente.enRevision => Insignia(
                    'En revisión por ${pendiente.reclamadoPorNombre ?? 'otro inspector'}',
                    tono: TonoInsignia.advertencia,
                  ),
                EstadoPendiente.mio => const Insignia('Reclamado por ti', tono: TonoInsignia.info),
              },
            ],
          ),
          if (pendiente.placa != null && pendiente.codigoMtrack != null) Text(pendiente.codigoMtrack!, style: tema.bodySmall),
          Text(detalle, style: tema.bodySmall),
          if (pendiente.estado != EstadoPendiente.enRevision) ...[
            const SizedBox(height: Espacio.sm),
            if (pendiente.estado == EstadoPendiente.disponible)
              FilledButton(onPressed: alReclamar, child: const Text('Revisar este'))
            else
              OutlinedButton(onPressed: alAbrir, child: const Text('Abrir')),
          ],
        ],
      ),
    );
  }
}
