import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../core/tema/tema.dart';
import '../domain/desenlace_envio.dart';
import 'sync_controller.dart';

/// Lo que falta para el próximo intento, nunca negativo: un reloj que ya venció se dice como «ahora», no como
/// un tiempo en pasado.
///
/// Se redondea al segundo en vez de truncar: una espera de quince segundos se dibuja unos milisegundos después
/// de programarse, y truncando salía «en 14 segundos» desde el primer instante.
Duration _faltan(DateTime cuando) {
  final resto = cuando.difference(DateTime.now());
  return resto.isNegative ? Duration.zero : Duration(seconds: (resto.inMilliseconds / 1000).round());
}

/// Barra de sincronización: muestra conectividad y capturas en cola, avisa de los envíos con error y permite forzar
/// el envío. Tocarla abre la lista de pendientes. Refuerza la confianza ("¿quedó guardado?").
class BarraSync extends ConsumerWidget {
  const BarraSync({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final sync = ref.watch(syncControllerProvider);
    final tema = Theme.of(context).textTheme;

    // Cuándo se vuelve a intentar solo. Es la única pantalla donde el conductor ve su cola —no tiene la pestaña
    // de Inspecciones—, así que «N en cola» a secas lo dejaba sin saber si eso iba a moverse alguna vez.
    final cuando = sync.proximoIntento == null ? '' : ' · se reintenta ${enPalabras(_faltan(sync.proximoIntento!))}';

    final (icono, color, texto) = switch (sync) {
      EstadoSync(fallidos: > 0) => (Icons.error_outline, Colores.peligro, '${sync.fallidos} con error · toca para revisar'),
      EstadoSync(online: false) => (
          Icons.cloud_off_outlined,
          Colores.advertencia,
          'Sin señal estable${sync.pendientes > 0 ? ' · ${sync.pendientes} en cola' : ''}',
        ),
      EstadoSync(pendientes: > 0, sincronizando: true) => (Icons.cloud_sync_outlined, Colores.info, '${sync.pendientes} en cola · sincronizando…'),
      // La señal está bien y el servidor no contesta: decirlo evita mandar a revisar el wifi, que está perfecto.
      EstadoSync(pendientes: > 0, pausa: MotivoPausa.apiCaida) => (
          Icons.cloud_off_outlined,
          Colores.advertencia,
          'El servidor no responde · ${sync.pendientes} en cola$cuando',
        ),
      EstadoSync(pendientes: > 0) => (Icons.cloud_upload_outlined, Colores.info, '${sync.pendientes} en cola$cuando'),
      _ => (Icons.cloud_done_outlined, Colores.info, 'Todo sincronizado'),
    };

    return Material(
      color: !sync.online ? const Color(0xFFFCF0DE) : Colores.superficie,
      child: InkWell(
        onTap: () => context.push('/inspecciones/cola'),
        child: Container(
          constraints: const BoxConstraints(minHeight: 44),
          padding: const EdgeInsets.symmetric(horizontal: Espacio.lg, vertical: Espacio.sm),
          decoration: const BoxDecoration(
            border: Border.symmetric(horizontal: BorderSide(color: Colores.borde, width: 0.5)),
          ),
          child: Row(
            children: [
              Icon(icono, size: 18, color: color),
              const SizedBox(width: Espacio.sm),
              Expanded(child: Semantics(liveRegion: true, child: Text(texto, style: tema.bodySmall))),
              if (sync.online && sync.pendientes > 0)
                sync.sincronizando
                    ? const SizedBox(width: 18, height: 18, child: CircularProgressIndicator(strokeWidth: 2))
                    : TextButton.icon(
                        onPressed: () => ref.read(syncControllerProvider.notifier).sincronizar(),
                        icon: const Icon(Icons.refresh, size: 16),
                        label: const Text('Sincronizar'),
                      ),
            ],
          ),
        ),
      ),
    );
  }
}
