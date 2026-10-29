import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:uuid/uuid.dart';

import '../../../core/errores/error_api.dart';
import '../../../core/tema/tema.dart';
import '../../../core/ui/widgets.dart';
import '../../auth/presentation/auth_controller.dart';
import '../../sync/domain/job.dart';
import '../../sync/presentation/sync_controller.dart';
import '../../sync/providers.dart';
import 'lienzo_firma.dart';

/// «Firmar recibo sin entrega»: el conductor hizo la salida del cargue porque no había inspector, así que nadie de la
/// empresa entrega la unidad. En vez de esperar una firma que no llega, cierra la transferencia solo con la suya.
///
/// Va por la cola con su dueño: sin señal se envía al volver.
class ReciboSinEntregaPage extends ConsumerStatefulWidget {
  const ReciboSinEntregaPage({super.key, required this.vin, required this.inspeccionId});

  final String vin;

  /// La salida del cargue que hizo el conductor: el servidor solo admite este recibo sobre ella.
  final String inspeccionId;

  @override
  ConsumerState<ReciboSinEntregaPage> createState() => _EstadoReciboSinEntrega();
}

class _EstadoReciboSinEntrega extends ConsumerState<ReciboSinEntregaPage> {
  String? _firma;
  bool _guardando = false;

  Future<void> _registrar() async {
    final usuarioId = ref.read(authControllerProvider).sesion?.usuario.id;
    final firma = _firma;
    if (usuarioId == null || firma == null || _guardando) return;
    setState(() => _guardando = true);
    try {
      await ref.read(colaRepositorioProvider).agregar(ReciboSinEntregaJob(
            id: const Uuid().v4(),
            etiqueta: 'Recibo sin entrega ${widget.vin}',
            creadoEn: DateTime.now().millisecondsSinceEpoch,
            usuarioId: usuarioId,
            inspeccionId: widget.inspeccionId,
            firma: firma,
          ));
      final sync = ref.read(syncControllerProvider.notifier);
      await sync.refrescar();
      await sync.sincronizar();
      if (!mounted) return;
      final estado = ref.read(syncControllerProvider);
      final titulo = estado.fallidos > 0 ? 'No se pudo enviar' : (estado.pendientes > 0 ? 'Recibo guardado' : 'Recibo sin entrega');
      final mensaje = estado.fallidos > 0
          ? 'El servidor rechazó el recibo. Quedó en Inspecciones › Cola para reintentarlo o descartarlo.'
          : estado.pendientes > 0
              ? 'Quedó en cola; se enviará cuando la señal sea estable.'
              : 'Quedó registrado: la transferencia se cerró sin entrega.';
      await showDialog<void>(
        context: context,
        builder: (contexto) => AlertDialog(
          title: Text(titulo),
          content: Text(mensaje),
          actions: [TextButton(onPressed: () => Navigator.of(contexto).pop(), child: const Text('OK'))],
        ),
      );
      if (mounted) Navigator.of(context).pop();
    } catch (e) {
      if (mounted) {
        await showDialog<void>(
          context: context,
          builder: (contexto) => AlertDialog(
            title: const Text('No se pudo guardar'),
            content: Text(mensajeDeError(e)),
            actions: [TextButton(onPressed: () => Navigator.of(contexto).pop(), child: const Text('OK'))],
          ),
        );
      }
    } finally {
      if (mounted) setState(() => _guardando = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context).textTheme;
    return Scaffold(
      appBar: AppBar(title: const Text('Recibo sin entrega')),
      body: SafeArea(
        child: ListView(
          padding: const EdgeInsets.all(Espacio.lg),
          children: [
            TextoMono(widget.vin),
            const SizedBox(height: Espacio.md),
            Text(
              'Hiciste la salida del cargue porque no había inspector, así que nadie entrega la unidad. '
              'Firmas tú como quien recibe y la transferencia queda cerrada sin entrega.',
              style: tema.bodyMedium,
            ),
            const SizedBox(height: Espacio.lg),
            LienzoFirma(etiqueta: 'Tu firma', onCambio: (p) => setState(() => _firma = p)),
            const SizedBox(height: Espacio.lg),
            FilledButton(
              onPressed: _firma != null && !_guardando ? _registrar : null,
              child: _guardando
                  ? const SizedBox(width: 22, height: 22, child: CircularProgressIndicator(strokeWidth: 2.5, color: Colors.white))
                  : const Text('Firmar recibo sin entrega'),
            ),
          ],
        ),
      ),
    );
  }
}
