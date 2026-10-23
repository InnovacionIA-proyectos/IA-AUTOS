import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/tema/tema.dart';
import '../../../core/ui/widgets.dart';
import '../../../core/util/formato.dart';
import '../../auth/presentation/auth_controller.dart';
import '../domain/job.dart';
import '../providers.dart';
import 'sync_controller.dart';

/// Todo lo que hay en el dispositivo. Lo de otra persona también se ve: solo se envía lo propio, y si lo ajeno no
/// apareciera, quien usa el equipo no tendría cómo saber por qué hay trabajo que nunca sale.
final _jobsProvider = FutureProvider.autoDispose<List<Job>>((ref) async {
  // Se vuelve a leer cada vez que cambian los contadores de la cola.
  ref.watch(syncControllerProvider.select((s) => (s.pendientes, s.fallidos, s.sincronizando)));
  return ref.watch(procesadorColaProvider).enElDispositivo();
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
    final usuarioId = ref.watch(authControllerProvider).sesion?.usuario.id;

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
                  // Lo de otra persona se muestra pero no se toca: borrarlo seria tirar su evidencia, y
                  // reintentarlo lo mandaria con el token equivocado.
                  ajeno: usuarioId != null && !lista[i].esDe(usuarioId) && !lista[i].esHuerfano,
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
  const _FilaJob({
    required this.job,
    required this.ajeno,
    required this.sincronizando,
    required this.onReintentar,
    required this.onDescartar,
  });

  final Job job;

  /// Lo encoló otra persona: se ve, pero no se envía con esta sesión ni se puede descartar.
  final bool ajeno;
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
              if (ajeno || job.esProhibido)
                const Insignia('De otro usuario', tono: TonoInsignia.neutro)
              else if (job.codigoError == 'VALIDATION_ERROR')
                // No es un fallo de envío: al informe le falta algo, y el detalle de abajo dice qué.
                const Insignia('Captura incompleta', tono: TonoInsignia.advertencia)
              else if (job.fallido)
                const Insignia('Con error', tono: TonoInsignia.peligro)
              else
                Insignia(sincronizando ? 'Enviando' : 'En cola', tono: TonoInsignia.info),
            ],
          ),
          Text(formatearFecha(creado), style: tema.bodySmall),
          if (job.intentos > 0) Text('Intentos: ${job.intentos}', style: tema.bodySmall),
          if (job.error != null) Padding(padding: const EdgeInsets.only(top: Espacio.xs), child: Text(job.error!, style: tema.bodySmall?.copyWith(color: Colores.peligro))),
          if (ajeno)
            Padding(
              padding: const EdgeInsets.only(top: Espacio.xs),
              child: Text('Lo dejó otra persona en este equipo. Se enviará cuando entre con su usuario.', style: tema.bodySmall),
            )
          // El servidor lo rechazó por permisos: este informe lo empezó otra persona y solo ella puede
          // terminarlo. No se ofrece descartarlo, que seria borrar fotos que no son de quien está mirando.
          else if (job.esProhibido)
            Padding(
              padding: const EdgeInsets.only(top: Espacio.xs),
              child: Text(
                'Este informe lo empezó otra persona; solo ella puede terminarlo desde su usuario.',
                style: tema.bodySmall,
              ),
            ),
          if (job.fallido && !ajeno && !job.esProhibido) ...[
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
