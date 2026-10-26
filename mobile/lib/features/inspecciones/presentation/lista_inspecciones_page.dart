import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../core/tema/tema.dart';
import '../../../core/ui/widgets.dart';
import '../../../core/util/formato.dart';
import '../../auth/presentation/auth_controller.dart';
import '../../sync/presentation/barra_sync.dart';
import '../../sync/presentation/sync_controller.dart';
import '../../vehiculos/domain/etiquetas.dart';
import '../domain/inspeccion_resumen.dart';
import 'lista_inspecciones_controller.dart';

class ListaInspeccionesPage extends ConsumerStatefulWidget {
  const ListaInspeccionesPage({super.key});

  @override
  ConsumerState<ListaInspeccionesPage> createState() => _EstadoListaInspeccionesPage();
}

class _EstadoListaInspeccionesPage extends ConsumerState<ListaInspeccionesPage> {
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) => ref.read(listaInspeccionesControllerProvider.notifier).cargar());
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
      if (actual && anterior == false) ref.read(listaInspeccionesControllerProvider.notifier).cargar();
    });
    // Y al terminar de trabajar la cola: el informe que se acaba de cerrar ya no es un borrador, pero esta
    // pantalla no se reconstruye al volver a su pestaña, así que sin esto hay que arrastrarla para enterarse.
    ref.listen(syncControllerProvider.select((s) => s.sincronizando), (anterior, actual) {
      if (!actual && anterior == true) ref.read(listaInspeccionesControllerProvider.notifier).cargar();
    });

    final tema = Theme.of(context).textTheme;
    final estado = ref.watch(listaInspeccionesControllerProvider);
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
                  Text('Mis inspecciones', style: tema.headlineMedium),
                  Row(
                    children: [
                      Expanded(child: Text(nombre, style: tema.bodySmall, overflow: TextOverflow.ellipsis)),
                      TextButton.icon(onPressed: _confirmarSalida, icon: const Icon(Icons.logout, size: 16), label: const Text('Cerrar sesión')),
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
    );
  }

  Widget _cuerpo(EstadoLista estado) {
    final cargar = ref.read(listaInspeccionesControllerProvider.notifier).cargar;
    return switch (estado.fase) {
      FaseLista.cargando => const EstadoCargando(),
      FaseLista.error => EstadoMensaje(
          icono: Icons.warning_amber_rounded,
          colorIcono: Colores.peligro,
          titulo: 'No pudimos cargar tus inspecciones',
          descripcion: 'Revisa tu conexión o que la API esté disponible e inténtalo de nuevo.',
          accion: FilledButton(onPressed: cargar, child: const Text('Reintentar')),
        ),
      FaseLista.sinConexion => const EstadoMensaje(
          icono: Icons.wifi_off_rounded,
          colorIcono: Colores.advertencia,
          titulo: 'Sin conexión',
          descripcion: 'Aún no hay inspecciones guardadas en este dispositivo. Conéctate una vez para descargarlas; luego podrás verlas sin señal.',
        ),
      FaseLista.listo when estado.items.isEmpty => EstadoMensaje(
          icono: Icons.assignment_outlined,
          titulo: 'Aún no tienes inspecciones',
          descripcion: 'Cuando captures un informe aparecerá aquí. Para empezar uno, abre una unidad desde Mis viajes.',
          accion: FilledButton(onPressed: () => context.go('/viajes'), child: const Text('Ir a Mis viajes')),
        ),
      FaseLista.listo => RefreshIndicator(
          onRefresh: cargar,
          child: ListView.separated(
            padding: const EdgeInsets.fromLTRB(Espacio.lg, Espacio.md, Espacio.lg, 88),
            itemCount: estado.items.length,
            separatorBuilder: (_, _) => const SizedBox(height: Espacio.sm),
            itemBuilder: (_, i) => _FilaInspeccion(estado.items[i]),
          ),
        ),
    };
  }
}

class _FilaInspeccion extends StatelessWidget {
  const _FilaInspeccion(this.item);

  final InspeccionResumen item;

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context).textTheme;
    return Tarjeta(
      onTap: item.vin == null ? null : () => context.push('/inspecciones/vin/${Uri.encodeComponent(item.vin!)}'),
      child: Row(
        children: [
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                if (item.vin != null) TextoMono(item.vin!),
                Text('${item.momentoNombre} · ${formatearFechaIso(item.fecha)}', style: tema.bodySmall),
              ],
            ),
          ),
          Insignia(
            etiquetar(etiquetaEstadoInspeccion, item.estado),
            tono: item.estado == 'cerrada' ? TonoInsignia.exito : TonoInsignia.advertencia,
          ),
        ],
      ),
    );
  }
}
