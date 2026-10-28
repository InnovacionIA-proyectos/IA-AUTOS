import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../core/tema/tema.dart';
import '../../../core/ui/widgets.dart';
import '../../auth/presentation/auth_controller.dart';
import '../../sync/presentation/barra_sync.dart';
import '../../sync/presentation/sync_controller.dart';
import '../../vehiculos/domain/etiquetas.dart';
import '../domain/viaje.dart';
import 'mis_viajes_controller.dart';

/// Entrada principal del trabajo de campo: los viajes donde la persona es conductor o inspector, con sus VIN.
///
/// Funciona sin señal con la última copia descargada.
class MisViajesPage extends ConsumerStatefulWidget {
  const MisViajesPage({super.key});

  @override
  ConsumerState<MisViajesPage> createState() => _EstadoMisViajesPage();
}

class _EstadoMisViajesPage extends ConsumerState<MisViajesPage> {
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) => ref.read(misViajesControllerProvider.notifier).cargar());
  }

  Future<void> _confirmarSalida() async {
    final pendientes = ref.read(syncControllerProvider).pendientes + ref.read(syncControllerProvider).fallidos;
    final salir = await showDialog<bool>(
      context: context,
      builder: (contexto) => AlertDialog(
        title: const Text('Cerrar sesión'),
        content: Text(
          pendientes > 0
              ? 'Tienes $pendientes elemento(s) sin enviar. Seguirán guardados en este dispositivo y se enviarán cuando haya señal con una sesión iniciada.'
              : '¿Salir de la app?',
        ),
        actions: [
          TextButton(onPressed: () => Navigator.of(contexto).pop(false), child: const Text('Cancelar')),
          TextButton(
            onPressed: () => Navigator.of(contexto).pop(true),
            child: const Text('Cerrar sesión', style: TextStyle(color: Colores.peligro)),
          ),
        ],
      ),
    );
    if (salir == true) await ref.read(authControllerProvider.notifier).cerrarSesion();
  }

  @override
  Widget build(BuildContext context) {
    // Recarga al recuperar la conexión.
    ref.listen(syncControllerProvider.select((s) => s.online), (anterior, actual) {
      if (actual && anterior == false) ref.read(misViajesControllerProvider.notifier).cargar();
    });

    final tema = Theme.of(context).textTheme;
    final estado = ref.watch(misViajesControllerProvider);
    final nombre = ref.watch(authControllerProvider.select((e) => e.sesion?.usuario.nombre)) ?? 'Usuario';

    return Scaffold(
      body: SafeArea(
        child: Column(
          children: [
            Padding(
              padding: const EdgeInsets.fromLTRB(Espacio.lg, Espacio.sm, Espacio.lg, Espacio.md),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text('Mis viajes', style: tema.headlineMedium),
                  Row(
                    children: [
                      Expanded(child: Text(nombre, style: tema.bodySmall, overflow: TextOverflow.ellipsis)),
                      TextButton.icon(
                        onPressed: _confirmarSalida,
                        icon: const Icon(Icons.logout, size: 16),
                        label: const Text('Cerrar sesión'),
                      ),
                    ],
                  ),
                ],
              ),
            ),
            const BarraSync(),
            Expanded(child: _cuerpo(estado)),
          ],
        ),
      ),
      floatingActionButton: estado.viajes.isEmpty
          ? null
          : FloatingActionButton(
              tooltip: 'Buscar VIN',
              onPressed: () => context.push('/viajes/buscar'),
              child: const Icon(Icons.search, size: 28),
            ),
    );
  }

  Widget _cuerpo(EstadoViajes estado) {
    final cargar = ref.read(misViajesControllerProvider.notifier).cargar;
    return switch (estado.fase) {
      FaseViajes.cargando => const EstadoCargando(),
      FaseViajes.error => EstadoMensaje(
          icono: Icons.warning_amber_rounded,
          colorIcono: Colores.peligro,
          titulo: 'No pudimos cargar tus viajes',
          descripcion: 'Revisa tu conexión o que la API esté disponible e inténtalo de nuevo.',
          accion: FilledButton(onPressed: cargar, child: const Text('Reintentar')),
        ),
      FaseViajes.sinConexion => const EstadoMensaje(
          icono: Icons.wifi_off_rounded,
          colorIcono: Colores.advertencia,
          titulo: 'Sin conexión',
          descripcion: 'Aún no hay viajes guardados en este dispositivo. Conéctate una vez para descargarlos; luego podrás verlos sin señal.',
        ),
      FaseViajes.listo when estado.viajes.isEmpty => const EstadoMensaje(
          icono: Icons.local_shipping_outlined,
          titulo: 'No tienes viajes asignados',
          descripcion: 'Cuando se te asigne un viaje como conductor o inspector aparecerá aquí con sus unidades.',
        ),
      FaseViajes.listo => RefreshIndicator(
          onRefresh: cargar,
          child: ListView.separated(
            padding: const EdgeInsets.fromLTRB(Espacio.lg, Espacio.md, Espacio.lg, 88),
            itemCount: estado.viajes.length,
            separatorBuilder: (_, _) => const SizedBox(height: Espacio.sm),
            itemBuilder: (_, i) => _TarjetaViaje(estado.viajes[i]),
          ),
        ),
    };
  }
}

class _TarjetaViaje extends StatelessWidget {
  const _TarjetaViaje(this.viaje);

  final Viaje viaje;

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context).textTheme;
    final ruta = [viaje.origen, viaje.destino].whereType<String>().where((l) => l.isNotEmpty).join(' → ');

    return Tarjeta(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Expanded(child: TextoMono(viaje.codigoMtrack)),
              // Por el catálogo del contrato y no por un ternario: hoy «mios» solo trae los activos, pero en
              // cuanto se consulten los terminados, un `finalizado` se mostraría como «Programado».
              Insignia(
                etiquetar(etiquetaEstadoViaje, viaje.estado),
                tono: viaje.estado == 'en_curso' ? TonoInsignia.info : TonoInsignia.neutro,
              ),
            ],
          ),
          // La placa en monoespaciada, como el VIN: se lee carácter a carácter contra la unidad que se tiene
          // delante. El resto es texto corriente.
          Row(
            children: [
              TextoMono(viaje.placa, estilo: tema.bodySmall),
              Text(
                [viaje.unidad, viaje.trailer].whereType<String>().where((v) => v.isNotEmpty).map((v) => ' · $v').join(),
                style: tema.bodySmall,
              ),
            ],
          ),
          if (ruta.isNotEmpty) Text(ruta, style: tema.bodySmall),
          const SizedBox(height: Espacio.sm),
          if (viaje.vehiculos.isEmpty)
            Text('Este viaje todavía no tiene unidades.', style: tema.bodySmall)
          else
            for (final vehiculo in viaje.vehiculos) _FilaVehiculo(vehiculo),
        ],
      ),
    );
  }
}

class _FilaVehiculo extends StatelessWidget {
  const _FilaVehiculo(this.vehiculo);

  final ViajeVehiculo vehiculo;

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context).textTheme;
    final descripcion = [vehiculo.marca, vehiculo.modelo, vehiculo.color].whereType<String>().join(' ');

    return InkWell(
      onTap: () => context.push('/inspecciones/vin/${Uri.encodeComponent(vehiculo.vin)}'),
      child: Padding(
        padding: const EdgeInsets.symmetric(vertical: Espacio.sm),
        child: Row(
          children: [
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  TextoMono(vehiculo.vin),
                  if (descripcion.isNotEmpty) Text(descripcion, style: tema.bodySmall),
                  // La unidad sigue en la lista: conserva sus etapas y sus informes, pero ya no se captura
                  // sobre ella y no cuenta para que el viaje esté completo.
                  if (!vehiculo.sale)
                    Text(
                      switch (vehiculo.noSale?.vinReemplazo) {
                        final reemplazo? when reemplazo.isNotEmpty => 'No sale · la reemplaza $reemplazo',
                        _ => 'No sale',
                      },
                      style: tema.bodySmall?.copyWith(color: Colores.peligro),
                    ),
                ],
              ),
            ),
            const Icon(Icons.chevron_right, size: 20, color: Colores.textoSecundario),
          ],
        ),
      ),
    );
  }
}
