import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../core/tema/tema.dart';
import '../../../core/ui/widgets.dart';
import '../../../core/util/formato.dart';
import '../../auth/domain/sesion.dart';
import '../../auth/presentation/auth_controller.dart';
import '../../catalogos/domain/catalogos.dart';
import '../../catalogos/presentation/catalogos_controller.dart';
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
  final _filtro = TextEditingController();

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) => ref.read(listaInspeccionesControllerProvider.notifier).cargar());
  }

  @override
  void dispose() {
    _filtro.dispose();
    super.dispose();
  }

  /// Vacío vuelve a la lista entera. Menos de tres caracteres no se manda: el VIN completo son diecisiete y una
  /// letra suelta trae todo el cliente página a página.
  void _buscar() {
    final termino = _filtro.text.trim();
    if (termino.isNotEmpty && termino.length < 3) return;
    ref.read(listaInspeccionesControllerProvider.notifier).buscar(termino);
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
    // Quien revisa los informes de su cliente no captura ninguno: ni la pestaña se llama «mis», ni el vacío lo
    // manda a capturar, ni la lista le cabe en una pantalla. Es el único que necesita buscar.
    final soloSuyas = soloSusViajes(ref.watch(authControllerProvider.select((e) => e.sesion?.usuario.rol)));

    return Scaffold(
      body: SafeArea(
        child: Column(
          children: [
            Padding(
              padding: const EdgeInsets.fromLTRB(Espacio.lg, Espacio.sm, Espacio.lg, Espacio.md),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(soloSuyas ? 'Mis inspecciones' : 'Inspecciones', style: tema.headlineMedium),
                  Row(
                    children: [
                      Expanded(child: Text(nombre, style: tema.bodySmall, overflow: TextOverflow.ellipsis)),
                      TextButton.icon(onPressed: _confirmarSalida, icon: const Icon(Icons.logout, size: 16), label: const Text('Cerrar sesión')),
                    ],
                  ),
                ],
              ),
            ),
            if (!soloSuyas) _buscador(),
            const BarraSync(),
            Expanded(child: _cuerpo(estado, soloSuyas)),
          ],
        ),
      ),
    );
  }

  /// El VIN se busca en el servidor. La copia local guarda solo la primera página, así que buscar en ella diría
  /// «ningún informe» con el informe esperando en la página tres.
  Widget _buscador() {
    final escrito = _filtro.text.trim();
    return Padding(
      padding: const EdgeInsets.fromLTRB(Espacio.lg, 0, Espacio.lg, Espacio.md),
      child: TextField(
        controller: _filtro,
        autocorrect: false,
        enableSuggestions: false,
        textCapitalization: TextCapitalization.characters,
        textInputAction: TextInputAction.search,
        decoration: InputDecoration(
          labelText: 'Buscar por VIN',
          helperText: escrito.isNotEmpty && escrito.length < 3 ? 'Escribe al menos 3 caracteres' : null,
          prefixIcon: const Icon(Icons.search),
          suffixIcon: IconButton(onPressed: _buscar, icon: const Icon(Icons.arrow_forward_rounded), tooltip: 'Buscar'),
        ),
        // Se busca al enviar, no al escribir: una petición por tecla serían diecisiete por VIN y las dieciséis
        // primeras no acotan nada. El `setState` es solo para la ayuda de los tres caracteres.
        onChanged: (_) => setState(() {}),
        onSubmitted: (_) => _buscar(),
      ),
    );
  }

  Widget _cuerpo(EstadoLista estado, bool soloSuyas) {
    final cargar = ref.read(listaInspeccionesControllerProvider.notifier).cargar;
    return switch (estado.fase) {
      FaseLista.cargando => const EstadoCargando(),
      FaseLista.error => EstadoMensaje(
          icono: Icons.warning_amber_rounded,
          colorIcono: Colores.peligro,
          titulo: 'No pudimos cargar las inspecciones',
          descripcion: estado.mensaje ?? 'Revisa tu conexión o que la API esté disponible e inténtalo de nuevo.',
          accion: FilledButton(onPressed: cargar, child: const Text('Reintentar')),
        ),
      FaseLista.sinConexion => EstadoMensaje(
          icono: Icons.wifi_off_rounded,
          colorIcono: Colores.advertencia,
          titulo: 'Sin conexión',
          descripcion: estado.mensaje ??
              'Aún no hay inspecciones guardadas en este dispositivo. Conéctate una vez para descargarlas; luego podrás verlas sin señal.',
        ),
      // Buscar y no encontrar no es lo mismo que no tener ninguna: decir «aún no tienes inspecciones» con un VIN
      // escrito manda a pensar que el informe no existe, cuando lo que no coincide es lo que se escribió.
      FaseLista.listo when estado.items.isEmpty && estado.busqueda.isNotEmpty => EstadoMensaje(
          icono: Icons.search_off_rounded,
          titulo: 'Ningún informe de ese VIN',
          descripcion: 'Revisa lo que escribiste. Solo se buscan los informes que ya puedes ver.',
          accion: FilledButton(
            onPressed: () {
              _filtro.clear();
              _buscar();
            },
            child: const Text('Ver todas'),
          ),
        ),
      FaseLista.listo when estado.items.isEmpty => EstadoMensaje(
          icono: Icons.assignment_outlined,
          titulo: soloSuyas ? 'Aún no tienes inspecciones' : 'Aún no hay inspecciones',
          descripcion: soloSuyas
              ? 'Cuando captures un informe aparecerá aquí. Para empezar uno, abre una unidad desde Mis viajes.'
              : 'Cuando alguien empiece un informe de tu cliente aparecerá aquí, cerrado o en borrador.',
          // A quien no tiene viajes, mandarlo a «Mis viajes» es mandarlo a otra pantalla vacía.
          accion: soloSuyas
              ? FilledButton(onPressed: () => context.go('/viajes'), child: const Text('Ir a Mis viajes'))
              : null,
        ),
      FaseLista.listo => RefreshIndicator(
          onRefresh: cargar,
          child: ListView.separated(
            padding: const EdgeInsets.fromLTRB(Espacio.lg, Espacio.md, Espacio.lg, 88),
            // Una fila más al final: la de traer la página siguiente.
            itemCount: estado.items.length + 1,
            separatorBuilder: (_, _) => const SizedBox(height: Espacio.sm),
            itemBuilder: (_, i) => i < estado.items.length ? _FilaInspeccion(estado.items[i]) : _pie(estado),
          ),
        ),
    };
  }

  /// La cola de la lista: trae la página siguiente. No se trae sola al llegar al final porque en el patio eso
  /// gasta datos sin que nadie lo haya pedido; se pide tocando, y mientras tanto se dice cuánto falta.
  Widget _pie(EstadoLista estado) {
    final tema = Theme.of(context).textTheme;
    if (!estado.hayMas) return const SizedBox.shrink();
    return Column(
      children: [
        if (estado.mensaje != null)
          Padding(
            padding: const EdgeInsets.only(bottom: Espacio.sm),
            child: Text(
              estado.mensaje!,
              style: tema.bodySmall?.copyWith(color: Colores.peligro),
              textAlign: TextAlign.center,
            ),
          ),
        if (estado.cargandoMas)
          const Padding(padding: EdgeInsets.all(Espacio.sm), child: CircularProgressIndicator())
        else
          OutlinedButton(
            onPressed: ref.read(listaInspeccionesControllerProvider.notifier).cargarMas,
            child: Text('Cargar más (${estado.items.length} de ${estado.total})'),
          ),
      ],
    );
  }
}

class _FilaInspeccion extends ConsumerWidget {
  const _FilaInspeccion(this.item);

  final InspeccionResumen item;

  /// De qué viaje es el informe: la etapa con su nombre del catálogo, el código MTrack y el lugar.
  ///
  /// Se arma con lo que haya: en un informe sin viaje —una inspección suelta— no se enseña un renglón vacío ni
  /// separadores sueltos.
  String? _deQueViaje(String Function(String) nombreDeEtapa) {
    final partes = [
      if (item.proceso case final p?) nombreDeEtapa(p),
      if (item.codigoMtrack case final c? when c.isNotEmpty) c,
      if (item.lugar case final l? when l.isNotEmpty) l,
    ];
    return partes.isEmpty ? null : partes.join(' · ');
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final tema = Theme.of(context).textTheme;
    final catalogos = ref.watch(catalogosProvider);
    final deQueViaje = _deQueViaje((codigo) => catalogos.etiqueta(Catalogo.etapas, codigo));
    return Tarjeta(
      onTap: item.vin == null ? null : () => context.push('/inspecciones/vin/${Uri.encodeComponent(item.vin!)}'),
      child: Row(
        children: [
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                if (item.vin != null) TextoMono(item.vin!),
                // Primero de qué viaje es, que es lo que distingue una fila de la siguiente cuando se ven los
                // informes de todo el mundo; después el momento y la fecha.
                if (deQueViaje != null)
                  Text(deQueViaje, style: tema.bodySmall?.copyWith(color: Colores.textoPrimario)),
                Text('${item.momentoNombre} · ${formatearFechaIso(item.fecha)}', style: tema.bodySmall),
                if (item.version > 1)
                  Text('Versión ${item.version} · corrige la versión ${item.version - 1}', style: tema.bodySmall?.copyWith(color: Colores.advertencia)),
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
