import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../app/proveedores.dart';
import '../../../core/tema/tema.dart';
import '../../../core/ui/widgets.dart';
import '../../../core/util/formato.dart';
import '../domain/job.dart';
import 'sync_controller.dart';

final _jobsProvider = FutureProvider.autoDispose<List<Job>>((ref) {
  // Se vuelve a leer cada vez que cambian los contadores de la cola.
  ref.watch(syncControllerProvider.select((s) => (s.pendientes, s.fallidos, s.sincronizando)));
  return ref.watch(colaRepositorioProvider).listar();
});

/// Lista de lo que está en cola de envío. Los elementos con error no se pierden en silencio: se muestran aquí con su
/// motivo y se pueden reintentar a mano o descartar.
class ColaPage extends ConsumerWidget {
  const ColaPage({super.key});

  Future<void> _descartar(BuildContext context, WidgetRef ref, Job job) async {
    final confirmar = await showDialog<bool>(
      context: context,
      builder: (contexto) => AlertDialog(
        title: const Text('Descartar envío'),
        content: Text(
          '¿Descartar "${job.etiqueta}"? Se borrará del dispositivo y no se enviará. '
          '${job is InformeJob && job.fotos.isNotEmpty ? 'También se borrarán sus fotos. ' : ''}Esta acción no se puede deshacer.',
        ),
        actions: [
          TextButton(onPressed: () => Navigator.of(contexto).pop(false), child: const Text('Cancelar')),
          TextButton(onPressed: () => Navigator.of(contexto).pop(true), child: const Text('Descartar', style: TextStyle(color: Colores.peligro))),
        ],
      ),
    );
    if (confirmar == true) await ref.read(syncControllerProvider.notifier).descartar(job.id);
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final jobs = ref.watch(_jobsProvider);
    final sync = ref.watch(syncControllerProvider);

    return Scaffold(
      appBar: AppBar(title: const Text('Envíos pendientes')),
      body: jobs.when(
        loading: () => const EstadoCargando(),
        error: (_, _) => const EstadoMensaje(icono: Icons.warning_amber_rounded, colorIcono: Colores.peligro, titulo: 'No se pudo leer la cola'),
        data: (lista) => lista.isEmpty
            ? const EstadoMensaje(
                icono: Icons.cloud_done_outlined,
                titulo: 'Todo enviado',
                descripcion: 'No hay informes ni firmas esperando señal.',
              )
            : ListView.separated(
                padding: const EdgeInsets.all(Espacio.lg),
                itemCount: lista.length,
                separatorBuilder: (_, _) => const SizedBox(height: Espacio.sm),
                itemBuilder: (_, i) => _FilaJob(
                  job: lista[i],
                  sincronizando: sync.sincronizando,
                  onReintentar: () => ref.read(syncControllerProvider.notifier).reintentar(lista[i].id),
                  onDescartar: () => _descartar(context, ref, lista[i]),
                ),
              ),
      ),
    );
  }
}

class _FilaJob extends StatelessWidget {
  const _FilaJob({required this.job, required this.sincronizando, required this.onReintentar, required this.onDescartar});

  final Job job;
  final bool sincronizando;
  final VoidCallback onReintentar;
  final VoidCallback onDescartar;

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context).textTheme;
    final creado = DateTime.fromMillisecondsSinceEpoch(job.creadoEn);
    return Tarjeta(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Expanded(child: Text(job.etiqueta, style: tema.titleMedium?.copyWith(fontSize: 15))),
              if (job.fallido)
                const Insignia('Con error', tono: TonoInsignia.peligro)
              else
                Insignia(sincronizando ? 'Enviando' : 'En cola', tono: TonoInsignia.info),
            ],
          ),
          Text(formatearFecha(creado), style: tema.bodySmall),
          if (job.intentos > 0) Text('Intentos: ${job.intentos}', style: tema.bodySmall),
          if (job.error != null) Padding(padding: const EdgeInsets.only(top: Espacio.xs), child: Text(job.error!, style: tema.bodySmall?.copyWith(color: Colores.peligro))),
          if (job.fallido) ...[
            const SizedBox(height: Espacio.md),
            Row(
              children: [
                Expanded(child: FilledButton(onPressed: onReintentar, child: const Text('Reintentar'))),
                const SizedBox(width: Espacio.md),
                Expanded(child: OutlinedButton(onPressed: onDescartar, child: const Text('Descartar'))),
              ],
            ),
          ],
        ],
      ),
    );
  }
}
