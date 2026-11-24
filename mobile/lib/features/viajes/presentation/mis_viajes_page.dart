import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../core/tema/tema.dart';
import '../../../core/ui/widgets.dart';
import '../../auth/presentation/auth_controller.dart';
import '../../vehiculos/domain/consulta.dart';
import '../../inspecciones/presentation/sin_calificar_page.dart';
import '../../inspecciones/providers.dart';
import '../../sync/presentation/barra_sync.dart';
import '../../sync/presentation/sync_controller.dart';
import '../../vehiculos/domain/etiquetas.dart';
import '../domain/pendiente.dart';
import '../domain/viaje.dart';
import '../providers.dart';
import 'acciones_conductor.dart';
import 'destinos_vistos.dart';
import 'entrega_punto_page.dart';
import 'mis_viajes_controller.dart';
import 'seccion_pendientes.dart';
import 'tomados.dart';

/// Entrada principal del trabajo de campo: los viajes donde la persona es conductor o inspector, con sus VIN.
///
/// Funciona sin señal con la última copia descargada.
class MisViajesPage extends ConsumerStatefulWidget {
  const MisViajesPage({super.key});

  @override
  ConsumerState<MisViajesPage> createState() => _EstadoMisViajesPage();
}

class _EstadoMisViajesPage extends ConsumerState<MisViajesPage> {
  /// Los terminados se miran aparte: no se guardan y no se trabajan, solo se consultan.
  bool _verTerminados = false;

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
    final rol = ref.watch(authControllerProvider.select((e) => e.sesion?.usuario.rol));

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
            Padding(
              padding: const EdgeInsets.fromLTRB(Espacio.lg, Espacio.sm, Espacio.lg, 0),
              child: SegmentedButton<bool>(
                segments: const [
                  ButtonSegment(value: false, label: Text('Activos')),
                  ButtonSegment(value: true, label: Text('Terminados')),
                ],
                selected: {_verTerminados},
                onSelectionChanged: (s) => setState(() => _verTerminados = s.first),
              ),
            ),
            Expanded(child: _verTerminados ? _terminados() : _cuerpo(estado)),
          ],
        ),
      ),
      // Quien consulta los carros de su cliente puede no tener ningún viaje: esconderle la búsqueda lo dejaría sin
      // ninguna entrada a la ficha, que es justo el caso del coordinador de calidad.
      floatingActionButton: estado.viajes.isEmpty && !buscaEnElServidor(rol)
          ? null
          : FloatingActionButton(
              tooltip: 'Buscar VIN',
              onPressed: () => context.push('/viajes/buscar'),
              child: const Icon(Icons.search, size: 28),
            ),
    );
  }

  /// Los viajes ya terminados. Solo con señal: no hay copia guardada de ellos a propósito.
  Widget _terminados() {
    return switch (ref.watch(viajesTerminadosProvider)) {
      AsyncLoading() => const EstadoCargando(),
      AsyncError() => EstadoMensaje(
          icono: Icons.wifi_off_rounded,
          colorIcono: Colores.advertencia,
          titulo: 'No pudimos cargar los terminados',
          descripcion: 'Los viajes terminados solo se consultan con señal; no se guardan en este dispositivo.',
          accion: FilledButton(
            onPressed: () => ref.invalidate(viajesTerminadosProvider),
            child: const Text('Reintentar'),
          ),
        ),
      AsyncData(value: final viajes) when viajes.isEmpty => const EstadoMensaje(
          icono: Icons.inventory_2_outlined,
          titulo: 'No tienes viajes terminados',
          descripcion: 'Aquí van quedando los viajes que ya se cerraron.',
        ),
      AsyncData(value: final viajes) => ListView.separated(
          padding: const EdgeInsets.fromLTRB(Espacio.lg, Espacio.md, Espacio.lg, 88),
          itemCount: viajes.length,
          separatorBuilder: (_, _) => const SizedBox(height: Espacio.sm),
          itemBuilder: (_, i) => _TarjetaViaje(viajes[i]),
        ),
    };
  }

  Widget _cuerpo(EstadoViajes estado) {
    final cargar = ref.read(misViajesControllerProvider.notifier).cargar;
    // Solo el inspector reclama camiones: a los demás la API les responde 403. Con la lista lista, los pendientes de
    // su punto van arriba, también cuando todavía no tiene viajes propios, que es justo cuando más los necesita.
    final inspector = ref.watch(authControllerProvider.select((e) => e.sesion?.usuario.rol)) == 'inspector';
    if (inspector && estado.fase == FaseViajes.listo) {
      final tema = Theme.of(context).textTheme;
      // Lo que tomó de «Por tomar» sale de esa lista y se trabaja aquí. Un cargue tomado ya llega como viaje suyo;
      // una entrega todavía no (S8-HU-10.1), así que se pinta con lo que trae «Por tomar» para que no desaparezca.
      final tomados = [
        for (final p in ref.watch(pendientesProvider).value ?? const <Pendiente>[])
          if (p.estado == EstadoPendiente.mio) p,
      ];
      final idsMios = {for (final v in estado.viajes) v.id};
      final sueltos = [for (final p in tomados) if (!idsMios.contains(p.viajeId)) p];
      return RefreshIndicator(
        onRefresh: () async {
          ref.invalidate(pendientesProvider);
          await cargar();
        },
        child: ListView(
          padding: const EdgeInsets.fromLTRB(Espacio.lg, Espacio.md, Espacio.lg, 88),
          children: [
            const SeccionPendientes(),
            const SizedBox(height: Espacio.lg),
            Text('Mis viajes', style: tema.titleMedium),
            const SizedBox(height: Espacio.sm),
            if (estado.viajes.isEmpty && sueltos.isEmpty)
              Text('Cuando tomes un camión de «Por tomar» o se te asigne un viaje, aparecerá aquí con sus unidades.', style: tema.bodySmall)
            else ...[
              for (final viaje in estado.viajes) ...[
                _TarjetaViaje(viaje, tomados: [for (final p in tomados) if (p.viajeId == viaje.id) p]),
                const SizedBox(height: Espacio.sm),
              ],
              for (final pendiente in sueltos) ...[TarjetaTomada(pendiente: pendiente), const SizedBox(height: Espacio.sm)],
            ],
          ],
        ),
      );
    }
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

class _TarjetaViaje extends ConsumerWidget {
  const _TarjetaViaje(this.viaje, {this.tomados = const []});

  final Viaje viaje;

  /// Lo que quien mira tomó de este viaje en «Por tomar»: dice si carga o recibe y desde aquí se libera.
  final List<Pendiente> tomados;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
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
                [viaje.unidad, viaje.trailer, viaje.muelle].whereType<String>().where((v) => v.isNotEmpty).map((v) => ' · $v').join(),
                style: tema.bodySmall,
              ),
            ],
          ),
          if (ruta.isNotEmpty) Text(ruta, style: tema.bodySmall),
          if (etiquetaDelPapel(tomados) case final papel?) ...[
            const SizedBox(height: Espacio.xs),
            Insignia(papel, tono: TonoInsignia.info),
          ],
          const SizedBox(height: Espacio.sm),
          // Un carro bajado ya no es parte del viaje (S8-HU-02.2 CA-6): no se muestra, tampoco en la copia guardada.
          if (viaje.vehiculos.every((v) => v.bajado))
            Text('Este viaje todavía no tiene unidades.', style: tema.bodySmall)
          else
            for (final vehiculo in viaje.vehiculos)
              if (!vehiculo.bajado) _FilaVehiculo(vehiculo, viaje: viaje),
          // Los daños que el conductor registró y nadie ha calificado. Solo al inspector del viaje: es el único
          // que puede ponerles clase y nivel —a otro, el listado le responde 200 con la lista vacía y la
          // revisión le responde 403—. Con cero también se ofrece (S8-HU-08.2 CA-3): «Sin calificar (0)» dice
          // que no falta nada, y la lista lo confirma.
          if (_sinCalificar(ref) case final cuantos?) ...[
            const SizedBox(height: Espacio.sm),
            OutlinedButton(
              onPressed: () => Navigator.of(context).push<void>(
                MaterialPageRoute(
                  builder: (_) => SinCalificarPage(viajeId: viaje.id, codigoMtrack: viaje.codigoMtrack),
                ),
              ),
              child: Text(cuantos == 1 ? 'Sin calificar (1)' : 'Sin calificar ($cuantos)'),
            ),
          ],
          // Entregar es lo que se hace con un viaje en ruta: en cada punto se firma una vez para todas sus unidades.
          if (viaje.estado == 'en_curso' && viaje.vehiculos.isNotEmpty) ...[
            const SizedBox(height: Espacio.sm),
            OutlinedButton(
              onPressed: () => Navigator.of(context).push<void>(
                MaterialPageRoute(
                  builder: (_) => EntregasViajePage(viajeId: viaje.id),
                ),
              ),
              child: const Text('Entrega por punto'),
            ),
          ],
          if (tomados.isNotEmpty) ...[
            const SizedBox(height: Espacio.sm),
            BotonesLiberar(tomados: tomados),
          ],
        ],
      ),
    );
  }
}

/// Cuántos daños sin calificar tiene este viaje, o null si no hay que ofrecerlo.
///
/// Null cuando quien mira no es el inspector del viaje (CA-4: a los demás ni se les muestra la lista) y mientras
/// la consulta no haya llegado: un botón que aparece y desaparece distrae más de lo que ayuda.
extension _SinCalificarDelViaje on _TarjetaViaje {
  int? _sinCalificar(WidgetRef ref) {
    final yo = ref.watch(authControllerProvider.select((e) => e.sesion?.usuario.id));
    if (yo == null || viaje.inspectorId != yo) return null;
    return ref.watch(sinCalificarDelViajeProvider(viaje.id)).value?.length;
  }
}

class _FilaVehiculo extends ConsumerWidget {
  const _FilaVehiculo(this.vehiculo, {required this.viaje});

  final ViajeVehiculo vehiculo;

  /// El viaje desde el que se abre: la unidad puede haber andado en varios y solo interesan sus etapas.
  final Viaje viaje;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final tema = Theme.of(context).textTheme;
    final descripcion = [vehiculo.marca, vehiculo.modelo, vehiculo.color].whereType<String>().join(' ');
    // Las acciones de la ruta son del conductor, mientras el viaje admita trabajo y la unidad siga saliendo. Un
    // viaje programado también: la primera novedad es la que lo pone en curso.
    final rol = ref.watch(authControllerProvider.select((e) => e.sesion?.usuario.rol));
    final conAcciones = rol == 'conductor' && viaje.admiteTrabajo && vehiculo.sale;
    // S8-HU-03.2 CA-5: al conductor se le resalta el destino que cambió desde la última vez que abrió el carro.
    final esConductor = rol == 'conductor';
    final nuevo = esConductor && destinoNuevo(ref.watch(destinosVistosProvider).value, viaje.id, vehiculo);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        InkWell(
          onTap: () {
            if (esConductor) ref.read(destinosVistosProvider.notifier).marcarVisto(viaje.id, vehiculo);
            context.push(
              '/inspecciones/vin/${Uri.encodeComponent(vehiculo.vin)}?viajeId=${Uri.encodeQueryComponent(viaje.id)}',
            );
          },
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
                      if (vehiculo.lugarDestino case final destino? when destino.isNotEmpty)
                        Row(
                          children: [
                            Flexible(
                              child: Text(
                                'Va a $destino',
                                style: nuevo
                                    ? tema.bodySmall?.copyWith(color: Colores.advertencia, fontWeight: FontWeight.w600)
                                    : tema.bodySmall,
                              ),
                            ),
                            if (nuevo) ...[
                              const SizedBox(width: Espacio.xs),
                              const Insignia('Destino nuevo', tono: TonoInsignia.advertencia),
                            ],
                          ],
                        ),
                      // La unidad sigue en la lista: conserva sus etapas y sus informes, pero ya no se captura
                      // sobre ella y no cuenta para que el viaje esté completo.
                      if (!vehiculo.sale)
                        Text(
                          [
                            switch (vehiculo.noSale) {
                              final no? => 'Este carro no sale: ${no.etiqueta}',
                              null => 'Este carro no sale',
                            },
                            if (vehiculo.noSale?.vinReemplazo case final reemplazo? when reemplazo.isNotEmpty)
                              'la reemplaza $reemplazo',
                          ].join(' · '),
                          style: tema.bodySmall?.copyWith(color: Colores.peligro),
                        ),
                    ],
                  ),
                ),
                const Icon(Icons.chevron_right, size: 20, color: Colores.textoSecundario),
              ],
            ),
          ),
        ),
        if (conAcciones) AccionesConductor(viaje: viaje, unidad: vehiculo),
      ],
    );
  }
}
