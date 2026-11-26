import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../core/errores/error_api.dart';
import '../../../core/tema/tema.dart';
import '../../../core/ui/widgets.dart';
import '../../auth/presentation/auth_controller.dart';
import '../../vehiculos/domain/consulta.dart';
import '../../vehiculos/domain/inventario.dart';
import '../../vehiculos/providers.dart';
import 'mis_viajes_controller.dart';

/// Busca un carro, de dos formas según quién pregunte.
///
/// El inspector y el conductor buscan **dentro de sus viajes**: solo ven esas unidades, la API responde `403` con
/// cualquier otra, y como la búsqueda sale de la copia guardada funciona sin señal.
///
/// Quien consulta los carros de su cliente —el coordinador de calidad, el comercial, los administradores— no puede
/// buscar ahí: puede no tener ningún viaje, y entonces la copia local está vacía. Su búsqueda la contesta el
/// servidor, que ya recorta lo que cada quien ve.
class BuscarVinPage extends ConsumerStatefulWidget {
  const BuscarVinPage({super.key});

  @override
  ConsumerState<BuscarVinPage> createState() => _EstadoBuscarVinPage();
}

class _EstadoBuscarVinPage extends ConsumerState<BuscarVinPage> {
  final _filtro = TextEditingController();

  /// Lo último que se mandó a buscar al servidor. No es lo que se está escribiendo: una petición por tecla sería
  /// quince peticiones por VIN, y ninguna de las catorce primeras acota nada.
  String _enviado = '';

  @override
  void dispose() {
    _filtro.dispose();
    super.dispose();
  }

  void _buscar() => setState(() => _enviado = _filtro.text.trim());

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context).textTheme;
    final rol = ref.watch(authControllerProvider.select((e) => e.sesion?.usuario.rol));
    final enElServidor = buscaEnElServidor(rol);

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
                    enElServidor
                        ? 'Busca por VIN o marca entre los carros de tu cliente.'
                        : 'Busca entre las unidades de tus viajes por VIN, marca, modelo o código del viaje.',
                    style: tema.bodyMedium?.copyWith(color: Colores.textoSecundario),
                  ),
                  const SizedBox(height: Espacio.md),
                  TextField(
                    controller: _filtro,
                    autofocus: true,
                    autocorrect: false,
                    enableSuggestions: false,
                    textCapitalization: TextCapitalization.characters,
                    textInputAction: enElServidor ? TextInputAction.search : TextInputAction.done,
                    decoration: InputDecoration(
                      labelText: enElServidor ? 'VIN o marca' : 'VIN, marca, modelo o código',
                      prefixIcon: const Icon(Icons.search),
                      suffixIcon: enElServidor
                          ? IconButton(
                              onPressed: _buscar,
                              icon: const Icon(Icons.arrow_forward_rounded),
                              tooltip: 'Buscar',
                            )
                          : null,
                    ),
                    // En el servidor se busca al enviar; en los viajes propios, mientras se escribe: ahí no hay
                    // ninguna petición de por medio.
                    onChanged: enElServidor ? null : (_) => setState(() {}),
                    onSubmitted: enElServidor ? (_) => _buscar() : null,
                  ),
                ],
              ),
            ),
            Expanded(child: enElServidor ? _delCliente(tema) : _deMisViajes(tema)),
          ],
        ),
      ),
    );
  }

  /// Lo que contesta el servidor. Una búsqueda no se guarda, así que sin señal no hay nada que mostrar: se dice y
  /// se ofrece reintentar, en vez de dejar la lista vacía como si el cliente no tuviera carros.
  Widget _delCliente(TextTheme tema) {
    if (_enviado.length < 3) {
      return const EstadoMensaje(
        icono: Icons.search_rounded,
        titulo: 'Escribe al menos 3 caracteres',
        descripcion: 'El VIN completo, un pedazo de él o la marca. Después toca buscar.',
      );
    }
    return switch (ref.watch(busquedaVehiculosProvider(_enviado))) {
      AsyncLoading() => const EstadoCargando(),
      AsyncError(:final error) => EstadoMensaje(
          icono: Icons.wifi_off_rounded,
          colorIcono: Colores.advertencia,
          titulo: 'No se pudo buscar',
          descripcion: mensajeDeError(error),
          accion: FilledButton(
            onPressed: () => ref.invalidate(busquedaVehiculosProvider(_enviado)),
            child: const Text('Reintentar'),
          ),
        ),
      AsyncData(value: final carros) when carros.isEmpty => const EstadoMensaje(
          icono: Icons.search_off_rounded,
          titulo: 'Ningún carro coincide',
          descripcion: 'Revisa lo que escribiste. Solo se buscan los carros de tu cliente.',
        ),
      AsyncData(value: final carros) => ListView.separated(
          padding: const EdgeInsets.fromLTRB(Espacio.lg, 0, Espacio.lg, Espacio.lg),
          itemCount: carros.length,
          separatorBuilder: (_, _) => const SizedBox(height: Espacio.sm),
          itemBuilder: (_, i) => _tarjetaCarro(carros[i], tema),
        ),
    };
  }

  /// Sin `viajeId`: quien consulta no entra por un viaje, y la ficha le muestra toda la historia del carro.
  Widget _tarjetaCarro(VehiculoBusqueda carro, TextTheme tema) => Tarjeta(
        onTap: () => context.push('/inspecciones/vin/${Uri.encodeComponent(carro.vin)}'),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            TextoMono(carro.vin),
            if (carro.descripcion.isNotEmpty) Text(carro.descripcion, style: tema.bodySmall),
            // Un carro dado de baja conserva su historial y se consulta igual; se dice para no confundirlo.
            if (!carro.activo)
              Text('Dado de baja', style: tema.bodySmall?.copyWith(color: Colores.advertencia)),
          ],
        ),
      );

  /// Las unidades de mis viajes, de la copia guardada: funciona sin señal.
  Widget _deMisViajes(TextTheme tema) {
    final estado = ref.watch(misViajesControllerProvider);
    final resultados = estado.buscar(_filtro.text);
    return switch ((estado.viajes.isEmpty, resultados.isEmpty)) {
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
              onTap: () => context.push(
                '/inspecciones/vin/${Uri.encodeComponent(vehiculo.vin)}'
                '?viajeId=${Uri.encodeQueryComponent(viaje.id)}',
              ),
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
    };
  }
}
