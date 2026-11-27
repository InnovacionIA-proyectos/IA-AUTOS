import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/tema/tema.dart';
import '../domain/desenlace_envio.dart';
import '../providers.dart';
import 'sync_controller.dart';

/// Los textos propios de cada envío: cómo se llama lo que se registró y qué quedó hecho.
typedef TextosDesenlace = ({String registrado, String detalleRegistrado, String noRegistrado});

/// Cuenta en un diálogo cómo quedó **el envío [jobId]**, no la cola entera (S8-HU-11.2 CA-6 y CA-7).
///
/// Mirar el contador hacía que otro envío rechazado, de otra unidad, se anunciara como el rechazo de este, y que
/// uno guardado sin señal se diera por registrado. Si el servidor lo rechazó, el diálogo ofrece «Reintentar» y
/// vuelve a decir cómo quedó.
Future<void> anunciarDesenlace(BuildContext context, WidgetRef ref, String jobId, TextosDesenlace textos) async {
  while (context.mounted) {
    final desenlace = desenlaceDe(
      await ref.read(colaRepositorioProvider).listar(),
      jobId,
      pausa: ref.read(syncControllerProvider).pausa,
    );
    if (!context.mounted) return;
    final motivo = desenlace.motivo == null ? '' : ': ${desenlace.motivo}';
    final (titulo, mensaje) = switch (desenlace.fase) {
      FaseEnvio.registrado => (textos.registrado, textos.detalleRegistrado),
      FaseEnvio.enCola => (
          'Quedó en cola: se enviará cuando haya señal',
          'Está guardado en este teléfono y sale solo cuando la señal sea estable.',
        ),
      FaseEnvio.enEspera => (
          'Quedó en cola: todavía no se envió',
          'No se pudo enviar$motivo. Se reintenta solo ${enPalabras(desenlace.espera!)}.',
        ),
      FaseEnvio.rechazado when desenlace.retirado => (
          textos.noRegistrado,
          'Se retiró$motivo. Ya no se puede enviar: quítalo desde Inspecciones › Cola.',
        ),
      FaseEnvio.rechazado => (
          textos.noRegistrado,
          'El servidor lo rechazó$motivo. Puedes reintentarlo aquí o desde Inspecciones › Cola.',
        ),
    };
    final rechazado = desenlace.fase == FaseEnvio.rechazado && !desenlace.retirado;
    final reintentar = await showDialog<bool>(
      context: context,
      builder: (contexto) => AlertDialog(
        icon: desenlace.salio ? const Icon(Icons.assignment_turned_in_outlined, color: Colores.exito, size: 40) : null,
        title: Text(titulo, style: desenlace.salio ? const TextStyle(color: Colores.exito) : null),
        content: Text(mensaje),
        actions: [
          if (rechazado) TextButton(onPressed: () => Navigator.of(contexto).pop(true), child: const Text('Reintentar')),
          TextButton(onPressed: () => Navigator.of(contexto).pop(false), child: const Text('OK')),
        ],
      ),
    );
    if (reintentar != true || !context.mounted) return;
    await ref.read(syncControllerProvider.notifier).reintentar(jobId, esperar: true);
  }
}
