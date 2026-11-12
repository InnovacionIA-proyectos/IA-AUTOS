import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../core/errores/error_api.dart';
import '../../../core/tema/tema.dart';
import '../../../core/ui/widgets.dart';
import '../domain/pendiente.dart';
import '../domain/viaje.dart';
import '../providers.dart';

/// El camión que el inspector reclamó: dónde es, qué unidades trae y cómo soltarlo.
///
/// Sirve igual para el cargue y para la entrega: lo que cambia es el punto que se muestra.
class DetallePendientePage extends ConsumerStatefulWidget {
  const DetallePendientePage({super.key, required this.pendiente});

  final Pendiente pendiente;

  @override
  ConsumerState<DetallePendientePage> createState() => _EstadoDetallePendiente();
}

class _EstadoDetallePendiente extends ConsumerState<DetallePendientePage> {
  bool _liberando = false;

  Pendiente get _pendiente => widget.pendiente;

  Future<void> _liberar() async {
    final soltar = await showDialog<bool>(
      context: context,
      builder: (contexto) => AlertDialog(
        title: const Text('Liberar el camión'),
        content: const Text(
          'Vuelve a quedar pendiente para los demás inspectores. Lo que ya capturaste se conserva a tu nombre.',
        ),
        actions: [
          TextButton(onPressed: () => Navigator.of(contexto).pop(false), child: const Text('Cancelar')),
          TextButton(onPressed: () => Navigator.of(contexto).pop(true), child: const Text('Liberar')),
        ],
      ),
    );
    if (soltar != true || !mounted) return;

    setState(() => _liberando = true);
    final mensajes = ScaffoldMessenger.of(context);
    try {
      await ref.read(reclamosRepositorioProvider).liberar(_pendiente.viajeId, _pendiente.proceso);
      if (mounted) Navigator.of(context).pop();
    } catch (e) {
      mensajes.showSnackBar(SnackBar(content: Text(mensajeDeError(e, 'No se pudo liberar. Inténtalo de nuevo.'))));
    } finally {
      if (mounted) setState(() => _liberando = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context).textTheme;
    final viaje = ref.watch(viajeDePendienteProvider(_pendiente.viajeId));
    final hora = _pendiente.fechaProgramada?.toLocal();

    return Scaffold(
      appBar: AppBar(title: Text(_pendiente.esEntrega ? 'Entrega por revisar' : 'Cargue por revisar')),
      body: SafeArea(
        child: ListView(
          padding: const EdgeInsets.all(Espacio.lg),
          children: [
            Tarjeta(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  TextoMono(_pendiente.placa ?? _pendiente.codigoMtrack ?? 'Sin placa'),
                  if (_pendiente.codigoMtrack != null) Text(_pendiente.codigoMtrack!, style: tema.bodySmall),
                  const SizedBox(height: Espacio.sm),
                  Text(_pendiente.esEntrega ? 'Punto de entrega' : 'Punto de cargue', style: tema.bodySmall),
                  Text(_pendiente.lugar, style: tema.titleSmall),
                  if (hora != null)
                    Text(
                      'Programado ${hora.hour.toString().padLeft(2, '0')}:${hora.minute.toString().padLeft(2, '0')}',
                      style: tema.bodySmall,
                    ),
                ],
              ),
            ),
            const SizedBox(height: Espacio.lg),
            Text('Unidades', style: tema.titleMedium),
            const SizedBox(height: Espacio.sm),
            viaje.when(
              loading: () => const Padding(padding: EdgeInsets.all(Espacio.lg), child: Center(child: CircularProgressIndicator())),
              error: (_, _) => Tarjeta(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text('No se pudieron cargar las unidades. Se consultan con señal.', style: tema.bodySmall),
                    TextButton(
                      onPressed: () => ref.invalidate(viajeDePendienteProvider(_pendiente.viajeId)),
                      child: const Text('Reintentar'),
                    ),
                  ],
                ),
              ),
              data: (datos) => _Unidades(viaje: datos),
            ),
            const SizedBox(height: Espacio.xl),
            OutlinedButton(
              onPressed: _liberando ? null : _liberar,
              child: _liberando
                  ? const SizedBox(width: 22, height: 22, child: CircularProgressIndicator(strokeWidth: 2.5))
                  : const Text('Liberar mi reclamo'),
            ),
          ],
        ),
      ),
    );
  }
}

class _Unidades extends StatelessWidget {
  const _Unidades({required this.viaje});

  final Viaje viaje;

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context).textTheme;
    if (viaje.vehiculos.isEmpty) return Tarjeta(child: Text('Este viaje todavía no tiene unidades.', style: tema.bodySmall));
    return Tarjeta(
      child: Column(
        children: [
          for (final unidad in viaje.vehiculos)
            InkWell(
              onTap: () => context.push(
                '/inspecciones/vin/${Uri.encodeComponent(unidad.vin)}?viajeId=${Uri.encodeQueryComponent(viaje.id)}',
              ),
              child: Padding(
                padding: const EdgeInsets.symmetric(vertical: Espacio.sm),
                child: Row(
                  children: [
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          TextoMono(unidad.vin),
                          Text([unidad.marca, unidad.modelo].whereType<String>().join(' '), style: tema.bodySmall),
                        ],
                      ),
                    ),
                    const Icon(Icons.chevron_right, color: Colores.textoSecundario),
                  ],
                ),
              ),
            ),
        ],
      ),
    );
  }
}
