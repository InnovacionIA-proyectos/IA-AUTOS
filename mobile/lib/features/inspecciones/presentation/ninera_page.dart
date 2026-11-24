import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../core/errores/error_api.dart';
import '../../../core/tema/tema.dart';
import '../../../core/ui/widgets.dart';
import '../../auth/presentation/auth_controller.dart';
import '../../custodia/domain/firma_ninera.dart';
import '../../custodia/presentation/firma_ninera_page.dart';
import '../../sync/domain/job.dart';
import '../../vehiculos/domain/inventario.dart';
import '../../vehiculos/providers.dart';
import '../../viajes/domain/viaje.dart';
import '../../viajes/providers.dart';
import '../domain/acomodacion.dart';
import '../domain/inspeccion_resumen.dart';
import '../domain/puestos_guardados.dart';
import '../providers.dart';
import 'seccion_acomodacion.dart';

/// La pantalla de la niñera (S8-HU-01.3 CA-1 y CA-2): los puestos con la disposición del servidor y el carro de
/// cada uno.
///
/// El inspector del viaje pone cada carro en su puesto desde aquí; los puestos se guardan en el teléfono (CA-4) y se
/// mandan con la acomodación del primer carro que se cierre. Tocar un puesto ocupado abre la salida del cargue de
/// ese carro, con su dibujo: un daño marcado ahí queda en el informe de ese VIN y no de otro.
class NineraPage extends ConsumerStatefulWidget {
  const NineraPage({super.key, required this.viajeId});

  final String viajeId;

  @override
  ConsumerState<NineraPage> createState() => _EstadoNinera();
}

class _EstadoNinera extends ConsumerState<NineraPage> {
  /// Los puestos tal como se ven aquí: los del servidor con lo elegido en el teléfono encima.
  Map<String, int>? _posiciones;

  /// Los del servidor cuando se leyó el viaje: contra ellos se guarda lo elegido.
  Map<String, int> _delServidor = const {};
  bool _abriendo = false;

  String? get _clave {
    final usuarioId = ref.read(authControllerProvider).sesion?.usuario.id;
    return usuarioId == null ? null : clavePuestos(usuarioId: usuarioId, viajeId: widget.viajeId);
  }

  /// Lee el viaje y lo guardado, y arma los puestos que se ven. Se repite al volver de un carro: su acomodación
  /// pudo cerrarse y mandar los puestos.
  Future<void> _cargarPuestos(Viaje viaje) async {
    final clave = _clave;
    final guardados = clave == null ? null : await ref.read(almacenPuestosProvider).leer(clave);
    if (!mounted) return;
    final servidor = posicionesDelViaje(viaje);
    setState(() {
      _delServidor = servidor;
      _posiciones = guardados?.sobre(servidor) ?? {...servidor};
    });
  }

  Future<void> _guardar() async {
    final clave = _clave;
    final posiciones = _posiciones;
    if (clave == null || posiciones == null) return;
    final puestos = PuestosGuardados.entre(servidor: _delServidor, elegidos: posiciones);
    final almacen = ref.read(almacenPuestosProvider);
    await (puestos.vacio ? almacen.borrar(clave) : almacen.guardar(clave, puestos));
  }

  void _poner(String vin, int? puesto) {
    setState(() {
      final posiciones = {...?_posiciones};
      if (puesto == null) {
        posiciones.remove(vin);
      } else {
        posiciones[vin] = puesto;
      }
      _posiciones = posiciones;
    });
    _guardar();
  }

  /// Abre la salida del cargue de ese carro (su dibujo y sus fotos), o su ficha si esa salida ya se cerró.
  Future<void> _abrirCarro(Viaje viaje, String vin) async {
    if (_abriendo) return;
    setState(() => _abriendo = true);
    final mensajes = ScaffoldMessenger.of(context);
    final contenedor = ProviderScope.containerOf(context, listen: false);
    final vinRuta = Uri.encodeComponent(vin);
    final viajeRuta = Uri.encodeQueryComponent(viaje.id);
    String ruta = '/inspecciones/vin/$vinRuta?viajeId=$viajeRuta';
    try {
      final inventario = await contenedor.read(vehiculosRepositorioProvider).inventario(vin);
      final etapa = inventario.etapas.where((e) => e.proceso == 'cargue' && e.viajeId == viaje.id).firstOrNull;
      final salidaCerrada = etapa?.inspecciones.any((i) => i.momento == Momento.salida && i.cerrada) ?? true;
      if (etapa != null && !salidaCerrada) {
        ruta = '/inspecciones/vin/$vinRuta/captura/${etapa.id}?proceso=cargue&momento=${Momento.salida}&viajeId=$viajeRuta';
      }
    } catch (e) {
      // Sin el inventario se abre la ficha, que funciona con su copia guardada.
      if (e is! ErrorApi) mensajes.showSnackBar(const SnackBar(content: Text('Se abre la ficha del carro.')));
    }
    if (!mounted) return;
    setState(() => _abriendo = false);
    await context.push<void>(ruta);
    if (!mounted) return;
    ref.invalidate(viajeDeEtapaProvider(viaje.id));
    ref.invalidate(informesDelViajeProvider((viajeId: viaje.id, proceso: 'cargue')));
  }

  Future<void> _tocar(Viaje viaje, PuestoNinera puesto, String? vin, {required bool acomoda}) async {
    final libres = [
      for (final c in carrosDeLaFirma(viaje))
        if (!(_posiciones ?? const {}).containsKey(c.vin)) c,
    ];
    if (vin != null && !acomoda) return _abrirCarro(viaje, vin);
    if (vin == null && (!acomoda || libres.isEmpty)) return;
    final eleccion = await showModalBottomSheet<String>(
      context: context,
      showDragHandle: true,
      builder: (contexto) => SafeArea(
        child: ListView(
          shrinkWrap: true,
          children: [
            Padding(
              padding: const EdgeInsets.symmetric(horizontal: Espacio.lg),
              child: Text('Puesto ${puesto.etiqueta}', style: Theme.of(contexto).textTheme.titleMedium),
            ),
            if (vin != null) ...[
              ListTile(
                leading: const Icon(Icons.directions_car_outlined),
                title: Text('Abrir $vin'),
                subtitle: const Text('Su dibujo y sus fotos de la salida del cargue'),
                onTap: () => Navigator.of(contexto).pop('abrir'),
              ),
              ListTile(
                leading: const Icon(Icons.remove_circle_outline),
                title: const Text('Quitar de este puesto'),
                onTap: () => Navigator.of(contexto).pop('quitar'),
              ),
            ] else
              for (final carro in libres)
                ListTile(
                  title: TextoMono(carro.vin),
                  subtitle: Text([carro.marca, carro.modelo, carro.color].whereType<String>().join(' ')),
                  onTap: () => Navigator.of(contexto).pop(carro.vin),
                ),
          ],
        ),
      ),
    );
    if (eleccion == null || !mounted) return;
    switch (eleccion) {
      case 'abrir':
        await _abrirCarro(viaje, vin!);
      case 'quitar':
        _poner(vin!, null);
      default:
        _poner(eleccion, puesto.numero);
    }
  }

  @override
  Widget build(BuildContext context) {
    ref.listen(viajeDeEtapaProvider(widget.viajeId), (_, siguiente) {
      if (siguiente case AsyncData(value: final viaje?)) _cargarPuestos(viaje);
    });
    final viaje = ref.watch(viajeDeEtapaProvider(widget.viajeId));
    return Scaffold(
      appBar: AppBar(title: const Text('Niñera')),
      body: SafeArea(
        child: switch (viaje) {
          AsyncData(value: final v?) => _cuerpo(context, v),
          AsyncLoading() => const EstadoCargando(),
          _ => EstadoMensaje(
              icono: Icons.wifi_off_rounded,
              colorIcono: Colores.advertencia,
              titulo: 'No se pudo cargar el viaje',
              descripcion: 'Conéctate e inténtalo de nuevo.',
              accion: FilledButton(
                onPressed: () => ref.invalidate(viajeDeEtapaProvider(widget.viajeId)),
                child: const Text('Reintentar'),
              ),
            ),
        },
      ),
    );
  }

  Widget _cuerpo(BuildContext context, Viaje viaje) {
    final tema = Theme.of(context).textTheme;
    final posiciones = _posiciones;
    if (posiciones == null) {
      // La primera vez: el viaje ya estaba en la caché del proveedor y `listen` no avisa.
      WidgetsBinding.instance.addPostFrameCallback((_) {
        if (mounted && _posiciones == null) _cargarPuestos(viaje);
      });
      return const EstadoCargando();
    }
    final yo = ref.watch(authControllerProvider.select((e) => e.sesion?.usuario.id));
    // Acomoda el inspector del viaje mientras la entrega del cargue no esté cerrada; los demás miran y abren.
    final acomoda =
        parteEnLaNinera(viaje, yo) == ParteTransferencia.entrega && viaje.admiteTrabajo && !viaje.cargueCerrado;
    final carros = carrosDeLaFirma(viaje);
    final informes = ref.watch(informesDelViajeProvider((viajeId: viaje.id, proceso: 'cargue'))).value;
    final sinPuesto = [for (final c in carros) if (!posiciones.containsKey(c.vin)) c.vin];

    return Stack(
      children: [
        ListView(
          padding: const EdgeInsets.all(Espacio.lg),
          children: [
            TextoMono(viaje.codigoMtrack),
            Text([viaje.placa, viaje.unidad, viaje.muelle].whereType<String>().join(' · '), style: tema.bodySmall),
            const SizedBox(height: Espacio.md),
            Text(
              acomoda
                  ? 'Toca un puesto libre para poner un carro, o uno ocupado para abrir ese carro. Los puestos se '
                      'guardan en este teléfono y se envían al cerrar la acomodación de cualquier carro.'
                  : 'Toca un puesto ocupado para abrir ese carro.',
              style: tema.bodySmall,
            ),
            const SizedBox(height: Espacio.sm),
            EsquemaNinera(
              posiciones: posiciones,
              puestos: puestosDe(viaje),
              alTocar: (puesto, vin) => _tocar(viaje, puesto, vin, acomoda: acomoda),
            ),
            if (sinPuesto.isNotEmpty) ...[
              const SizedBox(height: Espacio.sm),
              Text(
                sinPuesto.length == 1 ? 'Falta el puesto de ${sinPuesto.single}' : 'Faltan ${sinPuesto.length} carros sin puesto',
                style: tema.bodySmall?.copyWith(color: Colores.advertencia),
              ),
            ],
            const SizedBox(height: Espacio.lg),
            Text('Carros', style: tema.titleMedium),
            const SizedBox(height: Espacio.sm),
            for (final carro in carros) ...[
              _FilaCarro(
                carro: carro,
                puesto: posiciones[carro.vin],
                estadoSalida: informes == null ? null : _estadoSalida(carro.vin, informes),
                alAbrir: () => _abrirCarro(viaje, carro.vin),
              ),
              const SizedBox(height: Espacio.xs),
            ],
            const SizedBox(height: Espacio.lg),
            if (parteEnLaNinera(viaje, yo) != null && !viaje.cargueCerrado)
              FilledButton(
                onPressed: () => Navigator.of(context).push<void>(
                  MaterialPageRoute(builder: (_) => FirmaNineraPage(viajeId: viaje.id)),
                ),
                child: const Text('Firma de la niñera'),
              ),
          ],
        ),
        if (_abriendo) const ModalBarrier(color: Colors.black12, dismissible: false),
      ],
    );
  }

  /// Cómo va la salida del cargue (la acomodación) de un carro: la última versión manda.
  String _estadoSalida(String vin, List<InspeccionResumen> informes) {
    InspeccionResumen? ultima;
    for (final i in informes) {
      if (i.vin != vin || i.momento != Momento.salida) continue;
      if (ultima == null || i.version > ultima.version) ultima = i;
    }
    if (ultima == null) return 'Sin acomodación';
    return ultima.estado == 'cerrada' ? 'Acomodación cerrada' : 'Acomodación en borrador';
  }
}

class _FilaCarro extends StatelessWidget {
  const _FilaCarro({required this.carro, required this.puesto, required this.estadoSalida, required this.alAbrir});

  final ViajeVehiculo carro;
  final int? puesto;
  final String? estadoSalida;
  final VoidCallback alAbrir;

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context).textTheme;
    return Tarjeta(
      child: InkWell(
        onTap: alAbrir,
        child: Row(
          children: [
            SizedBox(width: 72, child: Text(puesto == null ? 'Sin puesto' : 'Puesto $puesto', style: tema.bodySmall)),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  TextoMono(carro.vin, estilo: tema.bodySmall),
                  if (estadoSalida case final estado?) Text(estado, style: tema.bodySmall),
                ],
              ),
            ),
            const Icon(Icons.chevron_right, color: Colores.textoSecundario),
          ],
        ),
      ),
    );
  }
}
