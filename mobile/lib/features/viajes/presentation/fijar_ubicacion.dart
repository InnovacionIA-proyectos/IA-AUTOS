import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../app/proveedores.dart';
import '../../../core/dispositivo/servicios.dart';
import '../../../core/errores/error_api.dart';
import '../../../core/tema/tema.dart';
import '../../../core/ui/widgets.dart';
import '../../auth/presentation/auth_controller.dart';
import '../domain/ubicacion_lugar.dart';
import '../providers.dart';

/// Quién ve «Fijar ubicación aquí» (S8-HU-09.2 CA-3): quien está de verdad en el lugar. El inspector en el cargue que
/// reclamó y en la entrega de su parada, y el conductor en la entrega, como siempre. El coordinador y el
/// administrador no: el servidor se lo admite, pero desde la oficina lo que dejarían es la ubicación de la oficina;
/// ellos la cargan o la confirman en el catálogo.
bool puedeFijarUbicacion(String? rol) => rol == 'inspector' || rol == 'conductor';

/// «Fijar ubicación aquí» en un lugar que no tiene ubicación: quien está en el lugar deja la posición de su teléfono,
/// con la precisión del GPS, y el lugar queda pendiente de confirmar. Un lugar con ubicación no muestra nada, y la
/// app nunca avisa «fuera del rango»: eso lo resuelve el servidor sin bloquear el flujo.
///
/// Decide aquí mismo quién lo ve ([puedeFijarUbicacion]), para que ninguna pantalla que lo ponga se lo muestre a
/// quien no debe.
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
    // Antes de mirar el catálogo: a quien no lo ve no hay por qué pedirle el estado de los lugares.
    if (!puedeFijarUbicacion(ref.watch(authControllerProvider.select((e) => e.sesion?.usuario.rol)))) {
      return const SizedBox.shrink();
    }
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
