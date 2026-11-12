import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../app/proveedores.dart';
import '../../../core/dispositivo/servicios.dart';
import '../../../core/errores/error_api.dart';
import '../../../core/tema/tema.dart';
import '../../../core/ui/widgets.dart';
import '../domain/ubicacion_lugar.dart';
import '../providers.dart';

/// «Fijar ubicación aquí» en un lugar que no tiene ubicación: el conductor deja la posición de su teléfono y el
/// lugar queda pendiente de confirmar. Un lugar con ubicación no muestra nada, y la app nunca avisa «fuera del
/// rango»: eso lo resuelve el servidor sin bloquear el flujo.
class FijarUbicacionLugar extends ConsumerStatefulWidget {
  const FijarUbicacionLugar({super.key, required this.lugarId});

  final String lugarId;

  @override
  ConsumerState<FijarUbicacionLugar> createState() => _EstadoFijarUbicacionLugar();
}

class _EstadoFijarUbicacionLugar extends ConsumerState<FijarUbicacionLugar> {
  bool _enviando = false;
  String? _error;

  Future<void> _fijar() async {
    setState(() {
      _enviando = true;
      _error = null;
    });
    try {
      final posicion = await ref.read(servicioUbicacionProvider).obtenerActual();
      await ref.read(lugaresRepositorioProvider).fijar(widget.lugarId, posicion);
      ref.invalidate(estadosUbicacionProvider);
    } on UbicacionNoDisponible {
      _error = 'No hay señal de GPS. Activa la ubicación del teléfono e inténtalo de nuevo.';
    } on ErrorApi catch (e) {
      if (e.status == 409) {
        ref.invalidate(estadosUbicacionProvider);
      } else {
        _error = e.status == 0 ? 'No se pudo enviar la ubicación. Revisa tu señal e inténtalo de nuevo.' : e.message;
      }
    }
    if (mounted) setState(() => _enviando = false);
  }

  @override
  Widget build(BuildContext context) {
    final estado = ref.watch(estadosUbicacionProvider).value?[widget.lugarId];
    return switch (estado) {
      EstadoUbicacionLugar.sinUbicacion => Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            OutlinedButton.icon(
              onPressed: _enviando ? null : _fijar,
              icon: const Icon(Icons.my_location),
              label: Text(_enviando ? 'Enviando ubicación…' : 'Fijar ubicación aquí'),
            ),
            if (_error != null) ...[
              const SizedBox(height: Espacio.xs),
              Text(_error!, style: Theme.of(context).textTheme.bodySmall?.copyWith(color: Colores.peligro)),
            ],
          ],
        ),
      EstadoUbicacionLugar.pendienteConfirmar => const Align(
          alignment: Alignment.centerLeft,
          child: Insignia('Ubicación pendiente de confirmar', tono: TonoInsignia.info),
        ),
      _ => const SizedBox.shrink(),
    };
  }
}
