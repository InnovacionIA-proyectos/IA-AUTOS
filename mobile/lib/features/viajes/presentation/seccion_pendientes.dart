import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/errores/error_api.dart';
import '../../../core/tema/tema.dart';
import '../../../core/ui/widgets.dart';
import '../domain/pendiente.dart';
import '../providers.dart';
import 'mis_viajes_controller.dart';
import 'tomados.dart';

/// «Por tomar»: los camiones de los puntos del inspector que todavía no son suyos, con el botón «Tomar».
///
/// Lo que el inspector ya tomó no sale aquí: pasa a «Mis viajes», que es lo que tiene que inspeccionar, y desde ahí
/// se libera. Lo que tiene otro inspector se ve en gris y sin botón, para que nadie lo busque.
///
/// Tomar y liberar van directo a la API: lo decide el servidor, y si otro inspector se adelantó se dice ahí mismo y
/// la lista se actualiza sin perder nada de lo que había.
class SeccionPendientes extends ConsumerWidget {
  const SeccionPendientes({super.key});

  Future<void> _tomar(BuildContext context, WidgetRef ref, Pendiente pendiente) async {
    final mensajes = ScaffoldMessenger.of(context);
    // El contenedor y no `ref`: al tomarlo, la tarjeta sale de la lista y este widget puede ya no estar.
    final contenedor = ProviderScope.containerOf(context, listen: false);
    try {
      await contenedor.read(reclamosRepositorioProvider).reclamar(pendiente.viajeId, pendiente.proceso);
    } catch (e) {
      // El motivo del servidor, tal cual («Ya fue tomado por otro inspector»): es lo único que explica por qué no.
      mensajes.showSnackBar(SnackBar(content: Text(mensajeDeError(e, 'No se pudo tomar. Inténtalo de nuevo.'))));
      contenedor.invalidate(pendientesProvider);
      return;
    }
    contenedor.invalidate(pendientesProvider);
    mensajes.showSnackBar(SnackBar(content: Text('${nombreDelCamion(pendiente)} quedó en Mis viajes')));
    // Tomar el cargue hace al inspector inspector del viaje: el viaje y sus unidades llegan con «Mis viajes».
    await contenedor.read(misViajesControllerProvider.notifier).cargar();
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
            Expanded(child: Text('Por tomar', style: tema.titleMedium)),
            IconButton(
              tooltip: 'Actualizar lo que hay por tomar',
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
                  'No se pudo consultar lo que hay por tomar. Se consulta con señal porque cada camión lo toma una sola persona.',
                  style: tema.bodySmall,
                ),
                TextButton(onPressed: () => ref.invalidate(pendientesProvider), child: const Text('Reintentar')),
              ],
            ),
          ),
          // S8-HU-10.2: dos listas, siempre visibles. Cada pendiente es un (viaje, proceso), así que cae en una sola:
          // lo que no es entrega se carga (un proceso desconocido también, como lo lee `Pendiente.fromJson`).
          data: (lista) {
            final porTomar = [for (final p in lista) if (p.estado != EstadoPendiente.mio) p];
            return Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                _ListaPendientes(
                  titulo: 'Por recibir',
                  vacia: 'Nada por recibir',
                  pendientes: [for (final p in porTomar) if (p.esEntrega) p],
                  alTomar: (p) => _tomar(context, ref, p),
                ),
                const SizedBox(height: Espacio.md),
                _ListaPendientes(
                  titulo: 'Por cargar',
                  vacia: 'Nada por cargar',
                  pendientes: [for (final p in porTomar) if (!p.esEntrega) p],
                  alTomar: (p) => _tomar(context, ref, p),
                ),
              ],
            );
          },
        ),
      ],
    );
  }
}

/// Una de las dos listas del punto, con su conteo. Vacía no desaparece: dice que no hay nada.
///
/// Primero lo que se puede tomar; debajo, en gris, lo que ya tiene otro inspector.
class _ListaPendientes extends StatelessWidget {
  const _ListaPendientes({required this.titulo, required this.vacia, required this.pendientes, required this.alTomar});

  final String titulo;
  final String vacia;
  final List<Pendiente> pendientes;
  final void Function(Pendiente) alTomar;

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context).textTheme;
    final ordenados = [
      for (final p in pendientes) if (p.estado == EstadoPendiente.disponible) p,
      for (final p in pendientes) if (p.estado != EstadoPendiente.disponible) p,
    ];
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
          for (final pendiente in ordenados) ...[
            _TarjetaPendiente(pendiente: pendiente, alTomar: () => alTomar(pendiente)),
            const SizedBox(height: Espacio.sm),
          ],
      ],
    );
  }
}

class _TarjetaPendiente extends StatelessWidget {
  const _TarjetaPendiente({required this.pendiente, required this.alTomar});

  final Pendiente pendiente;
  final VoidCallback alTomar;

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context).textTheme;
    final libre = pendiente.estado == EstadoPendiente.disponible;
    final tarjeta = Tarjeta(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              Expanded(child: TextoMono(pendiente.placa ?? pendiente.codigoMtrack ?? 'Sin placa')),
              if (libre)
                const Insignia('Disponible', tono: TonoInsignia.exito)
              else
                Insignia('Lo tiene ${pendiente.reclamadoPorNombre ?? 'otro inspector'}', tono: TonoInsignia.neutro),
            ],
          ),
          if (pendiente.placa != null && pendiente.codigoMtrack != null) Text(pendiente.codigoMtrack!, style: tema.bodySmall),
          Text(detalleDelPendiente(pendiente), style: tema.bodySmall),
          if (libre) ...[
            const SizedBox(height: Espacio.sm),
            FilledButton(onPressed: alTomar, child: const Text('Tomar')),
          ],
        ],
      ),
    );
    return libre ? tarjeta : Opacity(opacity: 0.6, child: tarjeta);
  }
}
