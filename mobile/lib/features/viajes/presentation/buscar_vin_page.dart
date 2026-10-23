import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../core/tema/tema.dart';
import '../../../core/ui/widgets.dart';
import 'mis_viajes_controller.dart';

/// Busca un VIN **dentro de mis viajes**.
///
/// Antes se digitaban los 17 caracteres de cualquier VIN. Ya no tiene sentido: un inspector o un conductor solo
/// ven las unidades de sus viajes y la API responde `403` con cualquier otra, así que se busca sobre lo que hay.
/// Y como busca sobre la copia guardada, funciona sin señal.
class BuscarVinPage extends ConsumerStatefulWidget {
  const BuscarVinPage({super.key});

  @override
  ConsumerState<BuscarVinPage> createState() => _EstadoBuscarVinPage();
}

class _EstadoBuscarVinPage extends ConsumerState<BuscarVinPage> {
  final _filtro = TextEditingController();

  @override
  void dispose() {
    _filtro.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context).textTheme;
    final estado = ref.watch(misViajesControllerProvider);
    final resultados = estado.buscar(_filtro.text);

    return Scaffold(
      appBar: AppBar(title: const Text('Buscar VIN')),
      body: SafeArea(
        child: Column(
          children: [
            Padding(
              padding: const EdgeInsets.all(Espacio.lg),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  Text(
                    'Busca entre las unidades de tus viajes por VIN, marca, modelo o código del viaje.',
                    style: tema.bodyMedium?.copyWith(color: Colores.textoSecundario),
                  ),
                  const SizedBox(height: Espacio.md),
                  TextField(
                    controller: _filtro,
                    autofocus: true,
                    autocorrect: false,
                    enableSuggestions: false,
                    textCapitalization: TextCapitalization.characters,
                    decoration: const InputDecoration(labelText: 'VIN, marca, modelo o código', prefixIcon: Icon(Icons.search)),
                    onChanged: (_) => setState(() {}),
                  ),
                ],
              ),
            ),
            Expanded(
              child: switch ((estado.viajes.isEmpty, resultados.isEmpty)) {
                (true, _) => const EstadoMensaje(
                    icono: Icons.local_shipping_outlined,
                    titulo: 'No tienes viajes asignados',
                    descripcion: 'Solo puedes abrir las unidades de tus viajes. Cuando se te asigne uno aparecerá aquí.',
                  ),
                (false, true) => const EstadoMensaje(
                    icono: Icons.search_off_rounded,
                    titulo: 'Ninguna unidad coincide',
                    descripcion: 'Revisa lo que escribiste. Solo se buscan las unidades de tus viajes.',
                  ),
                (false, false) => ListView.separated(
                    padding: const EdgeInsets.fromLTRB(Espacio.lg, 0, Espacio.lg, Espacio.lg),
                    itemCount: resultados.length,
                    separatorBuilder: (_, _) => const SizedBox(height: Espacio.sm),
                    itemBuilder: (_, i) {
                      final (viaje, vehiculo) = resultados[i];
                      final descripcion = [vehiculo.marca, vehiculo.modelo, vehiculo.color].whereType<String>().join(' ');
                      return Tarjeta(
                        onTap: () => context.push('/inspecciones/vin/${Uri.encodeComponent(vehiculo.vin)}'),
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            TextoMono(vehiculo.vin),
                            if (descripcion.isNotEmpty) Text(descripcion, style: tema.bodySmall),
                            Row(
                              children: [
                                Text('${viaje.codigoMtrack} · ', style: tema.bodySmall),
                                TextoMono(viaje.placa, estilo: tema.bodySmall),
                              ],
                            ),
                          ],
                        ),
                      );
                    },
                  ),
              },
            ),
          ],
        ),
      ),
    );
  }
}
