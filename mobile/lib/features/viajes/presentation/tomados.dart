import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/errores/error_api.dart';
import '../../../core/tema/tema.dart';
import '../../../core/ui/widgets.dart';
import '../domain/pendiente.dart';
import '../providers.dart';
import 'detalle_pendiente_page.dart';
import 'mis_viajes_controller.dart';

/// Lo que el inspector tomó de «Por tomar» y ahora le toca inspeccionar: cómo se nombra, qué papel tiene en el viaje
/// y cómo se libera.

/// La placa del camión, o su código si no trae placa.
String nombreDelCamion(Pendiente pendiente) => pendiente.placa ?? pendiente.codigoMtrack ?? 'El camión';

/// «Entrega · CEDI Envigado · 08:30 · 2 unidades».
String detalleDelPendiente(Pendiente pendiente) {
  final hora = pendiente.fechaProgramada?.toLocal();
  return [
    pendiente.esEntrega ? 'Entrega' : 'Cargue',
    pendiente.lugar,
    if (hora != null) '${hora.hour.toString().padLeft(2, '0')}:${hora.minute.toString().padLeft(2, '0')}',
    if (pendiente.unidades > 0) '${pendiente.unidades} ${pendiente.unidades == 1 ? 'unidad' : 'unidades'}',
  ].where((p) => p.isNotEmpty).join(' · ');
}

/// «Tú cargas», «Tú recibes» o «Tú cargas y recibes» según lo que el inspector tomó del viaje; null si no tomó nada.
///
/// Mientras el servidor no mande el papel en el viaje (S8-HU-10.1), se lee de lo que tomó en «Por tomar».
String? etiquetaDelPapel(Iterable<Pendiente> tomados) {
  final recibe = tomados.any((p) => p.esEntrega);
  final carga = tomados.any((p) => !p.esEntrega);
  return switch ((carga, recibe)) {
    (true, true) => 'Tú cargas y recibes',
    (true, false) => 'Tú cargas',
    (false, true) => 'Tú recibes',
    _ => null,
  };
}

/// Pregunta antes de liberar: el camión vuelve a «Por tomar» para todos.
Future<bool> confirmarLiberar(BuildContext context) async =>
    await showDialog<bool>(
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
    ) ==
    true;

/// Libera lo tomado desde «Mis viajes»: confirma, va a la API y refresca las dos listas. El camión vuelve a «Por tomar».
Future<void> liberarTomado(BuildContext context, Pendiente pendiente) async {
  if (!await confirmarLiberar(context) || !context.mounted) return;
  final mensajes = ScaffoldMessenger.of(context);
  // El contenedor y no `ref`: al liberarlo, la tarjeta sale de «Mis viajes» y quien la pintó puede ya no estar.
  final contenedor = ProviderScope.containerOf(context, listen: false);
  try {
    await contenedor.read(reclamosRepositorioProvider).liberar(pendiente.viajeId, pendiente.proceso);
  } catch (e) {
    mensajes.showSnackBar(SnackBar(content: Text(mensajeDeError(e, 'No se pudo liberar. Inténtalo de nuevo.'))));
    return;
  }
  contenedor.invalidate(pendientesProvider);
  mensajes.showSnackBar(SnackBar(content: Text('${nombreDelCamion(pendiente)} volvió a «Por tomar»')));
  await contenedor.read(misViajesControllerProvider.notifier).cargar();
}

/// Los botones «Liberar» de lo tomado en un viaje: uno por proceso, y si son dos dicen cuál es cuál.
class BotonesLiberar extends StatelessWidget {
  const BotonesLiberar({super.key, required this.tomados});

  final List<Pendiente> tomados;

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        for (final tomado in tomados)
          OutlinedButton(
            onPressed: () => liberarTomado(context, tomado),
            child: Text(
              tomados.length == 1 ? 'Liberar' : (tomado.esEntrega ? 'Liberar la entrega' : 'Liberar el cargue'),
            ),
          ),
      ],
    );
  }
}

/// Un camión tomado cuyo viaje todavía no llega en «Mis viajes»: hoy, la entrega, hasta que el servidor la incluya
/// (S8-HU-10.1). Se pinta con lo que trae «Por tomar», y sus unidades se ven en el detalle.
class TarjetaTomada extends StatelessWidget {
  const TarjetaTomada({super.key, required this.pendiente});

  final Pendiente pendiente;

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context).textTheme;
    return Tarjeta(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              Expanded(child: TextoMono(pendiente.codigoMtrack ?? pendiente.placa ?? 'Sin código')),
              Insignia(etiquetaDelPapel([pendiente])!, tono: TonoInsignia.info),
            ],
          ),
          if (pendiente.placa != null && pendiente.codigoMtrack != null) TextoMono(pendiente.placa!, estilo: tema.bodySmall),
          Text(detalleDelPendiente(pendiente), style: tema.bodySmall),
          const SizedBox(height: Espacio.sm),
          FilledButton(
            onPressed: () async {
              final contenedor = ProviderScope.containerOf(context, listen: false);
              await Navigator.of(context).push<void>(
                MaterialPageRoute(builder: (_) => DetallePendientePage(pendiente: pendiente)),
              );
              contenedor.invalidate(pendientesProvider);
              await contenedor.read(misViajesControllerProvider.notifier).cargar();
            },
            child: const Text('Ver unidades'),
          ),
          const SizedBox(height: Espacio.sm),
          BotonesLiberar(tomados: [pendiente]),
        ],
      ),
    );
  }
}
