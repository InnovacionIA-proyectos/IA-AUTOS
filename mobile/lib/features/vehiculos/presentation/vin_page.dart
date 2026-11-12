import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:uuid/uuid.dart';

import '../../../app/proveedores.dart';
import '../../../core/dispositivo/servicios.dart';
import '../../../core/errores/error_api.dart';
import '../../../core/http/urls_api.dart';
import '../../../core/tema/tema.dart';
import '../../../core/ui/foto_evidencia.dart';
import '../../../core/ui/visor_imagen.dart';
import '../../../core/ui/widgets.dart';
import '../../../core/util/formato.dart';
import '../../auth/domain/sesion.dart';
import '../../auth/presentation/auth_controller.dart';
import '../../catalogos/domain/catalogos.dart';
import '../../catalogos/presentation/catalogos_controller.dart';
import '../../dibujo_dano/domain/dibujo_dano.dart';
import '../../custodia/presentation/recibo_sin_entrega_page.dart';
import '../../inspecciones/domain/acomodacion.dart';
import '../../inspecciones/domain/novedad_pendiente.dart';
import '../../inspecciones/presentation/hoja_sin_inspector.dart';
import '../../inspecciones/presentation/revision_novedad_page.dart';
import '../../inspecciones/domain/nota_aclaratoria.dart';
import '../../inspecciones/presentation/lista_hallazgos_previos.dart';
import '../../inspecciones/presentation/nota_aclaratoria_hoja.dart';
import '../../inspecciones/providers.dart';
import '../../sync/domain/job.dart';
import '../../sync/providers.dart';
import '../../dibujo_dano/presentation/selector_zona_dano.dart';
import '../../viajes/presentation/mis_viajes_controller.dart';
import '../../sync/presentation/sync_controller.dart';
import '../../viajes/providers.dart';
import '../domain/etiquetas.dart';
import '../domain/consulta.dart';
import '../domain/inventario.dart';
import '../domain/no_sale.dart';
import 'detalle_hallazgo.dart';
import 'hoja_no_sale.dart';
import '../providers.dart';

final inventarioProvider = FutureProvider.autoDispose.family<Inventario, String>(
  (ref, vin) => ref.watch(vehiculosRepositorioProvider).inventario(vin),
);

class VinPage extends ConsumerWidget {
  const VinPage({super.key, required this.vin, this.viajeId});

  final String vin;

  /// Desde qué viaje se abrió la unidad. Un VIN puede haber andado en varios y su inventario los trae todos; con
  /// esto se ven solo las etapas del viaje en el que se está. Null cuando se llega sin saberlo.
  final String? viajeId;

  Future<void> _ir(BuildContext context, WidgetRef ref, String ruta) async {
    await context.push(ruta);
    // Al volver de capturar o firmar, el inventario puede haber cambiado. Y el viaje también: la acomodación
    // escribe la posición de cada unidad, que vive en el viaje y no en el inventario.
    ref.invalidate(inventarioProvider(vin));
    final viajeId = ref.read(inventarioProvider(vin)).value?.etapas.map((e) => e.viajeId).nonNulls.firstOrNull;
    if (viajeId != null) ref.invalidate(viajeDeEtapaProvider(viajeId));
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final inventario = ref.watch(inventarioProvider(vin));
    final rol = ref.watch(authControllerProvider.select((e) => e.sesion?.usuario.rol));
    return Scaffold(
      appBar: AppBar(title: TextoMono(vin)),
      body: inventario.when(
        loading: () => const EstadoCargando(),
        error: (error, _) => EstadoMensaje(
          icono: Icons.warning_amber_rounded,
          colorIcono: Colores.peligro,
          titulo: esFueraDeMisViajes(error) ? mensajeSinAcceso(rol) : 'No se pudo cargar el VIN',
          descripcion: switch (error) {
            _ when esFueraDeMisViajes(error) => detalleSinAcceso(rol),
            ErrorApi(status: 404) => 'Este VIN no está registrado. Pide al administrador que lo cargue.',
            _ => mensajeDeError(error),
          },
          // Reintentar un 403 da otro 403: no es un tropiezo de la red, es que esa unidad no es suya.
          accion: esFueraDeMisViajes(error)
              ? null
              : FilledButton(onPressed: () => ref.invalidate(inventarioProvider(vin)), child: const Text('Reintentar')),
        ),
        data: (inv) => RefreshIndicator(
          // Tirar de la pantalla es pedir a mano que se reintente, así que las fotos que no estaban vuelven a
          // tener su oportunidad: el almacén pudo estar caído un momento.
          onRefresh: () {
            olvidarFotosQueFaltan();
            return ref.refresh(inventarioProvider(vin).future);
          },
          child: _Detalle(inventario: inv.delViaje(viajeId), alIr: (ruta) => _ir(context, ref, ruta)),
        ),
      ),
    );
  }
}

class _Detalle extends ConsumerWidget {
  const _Detalle({required this.inventario, required this.alIr});

  final Inventario inventario;
  final void Function(String ruta) alIr;

  /// «No hay inspector»: el conductor elige el motivo, se toma la fecha y el lugar de ese momento, y sigue la captura
  /// de la salida del cargue. Esos tres datos viajan con la inspección: la novedad de proceso los guarda tal cual,
  /// aunque el informe se envíe horas después.
  Future<void> _sinInspector(BuildContext context, WidgetRef ref, EtapaVista etapa, String vinRuta) async {
    final motivos = ref.read(catalogosProvider).seleccionables(Catalogo.motivosSinInspector);
    final motivo = await pedirMotivoSinInspector(context, motivos: motivos);
    if (motivo == null || !context.mounted) return;
    final Posicion posicion;
    try {
      posicion = await ref.read(servicioUbicacionProvider).obtenerActual();
    } on UbicacionNoDisponible catch (e) {
      if (context.mounted) ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(e.motivo)));
      return;
    }
    if (!context.mounted) return;
    final consulta = [
      'proceso=${Uri.encodeQueryComponent(etapa.proceso)}',
      'momento=${Momento.salida}',
      if (etapa.viajeId != null) 'viajeId=${Uri.encodeQueryComponent(etapa.viajeId!)}',
      'motivo=${Uri.encodeQueryComponent(motivo.codigo)}',
      if (motivo.texto != null) 'motivoTexto=${Uri.encodeQueryComponent(motivo.texto!)}',
      'fechaMotivo=${Uri.encodeQueryComponent(DateTime.now().toUtc().toIso8601String())}',
      'latMotivo=${posicion.lat}',
      'lngMotivo=${posicion.lng}',
    ].join('&');
    alIr('/inspecciones/vin/$vinRuta/captura/${etapa.id}?$consulta');
  }

  /// La salida del cargue que hizo el conductor sin inspector, cuando todavía falta cerrar su transferencia: ahí no
  /// hay quien entregue, y quien recibe firma solo.
  InspeccionVista? _reciboSinEntrega(EtapaVista etapa, String rol) {
    if (rol != 'conductor' || etapa.proceso != 'cargue') return null;
    final salida = etapa.inspecciones.where((i) => i.momento == Momento.salida && i.fueSinInspector && i.cerrada).lastOrNull;
    return salida == null || (salida.transferencia?.cerrada ?? false) ? null : salida;
  }

  Future<void> _firmarReciboSinEntrega(BuildContext context, WidgetRef ref, EtapaVista etapa) async {
    final salida = _reciboSinEntrega(etapa, 'conductor');
    if (salida == null) return;
    await Navigator.of(context).push<void>(
      MaterialPageRoute(builder: (_) => ReciboSinEntregaPage(vin: inventario.vin, inspeccionId: salida.id)),
    );
    ref.invalidate(inventarioProvider(inventario.vin));
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final tema = Theme.of(context).textTheme;
    final catalogos = ref.watch(catalogosProvider);
    final ultima = inventario.ultimaInspeccion;
    final vinRuta = Uri.encodeComponent(inventario.vin);
    final rol = ref.watch(authControllerProvider.select((e) => e.sesion?.usuario.rol)) ?? '';
    // Un viaje terminado conserva sus etapas, pero el servidor ya no admite inspecciones nuevas: ofrecerlas
    // sería mandar a capturar siete fotos para que el envío muera con un 409. Mientras no se sepa el estado del
    // viaje —sin señal y sin copia— se deja trabajar, que es lo normal.
    final viajeId = inventario.etapas.map((e) => e.viajeId).nonNulls.firstOrNull;
    final viaje = viajeId == null ? null : ref.watch(viajeDeEtapaProvider(viajeId)).value;
    final viajeTerminado = viaje != null && !viaje.admiteTrabajo;
    // Quien consulta no captura ni firma: el servidor le responde 403 y la ruta de captura ni se abre. Se dice en
    // la tarjeta, en vez de ofrecer botones muertos.
    final puedeTrabajar =
        ref.watch(authControllerProvider.select((e) => e.sesion?.usuario.puede(Capacidad.registrarInspecciones))) ??
            false;

    return ListView(
      padding: const EdgeInsets.all(Espacio.lg),
      children: [
        Tarjeta(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              TextoMono(inventario.vin),
              Text(
                '${inventario.marca}${inventario.modelo != null ? ' ${inventario.modelo}' : ''} · ${etiquetar(etiquetaOperacion, inventario.operacion)}',
                style: tema.bodySmall,
              ),
            ],
          ),
        ),
        const SizedBox(height: Espacio.md),
        _NoSale(inventario: inventario),
        _NovedadesPorRevisar(inventario: inventario, viajeTerminado: viajeTerminado),
        const SizedBox(height: Espacio.lg),
        Text('Último informe', style: tema.titleMedium),
        const SizedBox(height: Espacio.sm),
        if (ultima == null)
          // Con el viaje terminado no es que «aún» falten: ya no va a haber ninguno, y decirlo como un
          // pendiente manda a buscar un trabajo que nadie puede hacer.
          Tarjeta(
            child: Text(
              viajeTerminado ? 'En este viaje no se registró ningún informe.' : 'Este VIN aún no tiene informes.',
              style: tema.bodySmall,
            ),
          )
        else
          _UltimoInforme(
            vin: inventario.vin,
            etapa: ultima.etapa,
            inspeccion: ultima.inspeccion,
            anteriores: ultima.etapa.versionesAnteriores(ultima.inspeccion),
            viajeTerminado: viajeTerminado,
          ),
        // El historial completo del vehículo, no solo el último informe: un daño que trae de un viaje anterior,
        // con otra tripulación, no está en las etapas que esta ficha muestra. Al conductor no se le ofrece: la
        // API le responde 403 a los previos.
        if (rol != 'conductor') ...[
          const SizedBox(height: Espacio.lg),
          _HistorialDeDanos(vin: inventario.vin),
        ],
        const SizedBox(height: Espacio.lg),
        Text('Cambio de responsabilidad', style: tema.titleMedium),
        const SizedBox(height: Espacio.sm),
        if (inventario.etapas.isEmpty)
          Tarjeta(child: Text('Este VIN no tiene ruta de custodia. El administrador debe crear las etapas.', style: tema.bodySmall))
        else
          for (final etapa in inventario.etapas) ...[
            _TarjetaEtapa(
              etapa: etapa,
              procesoNombre: catalogos.etiqueta(Catalogo.etapas, etapa.proceso),
              momentos: momentosDeEtapa(etapa, inventario),
              viajeTerminado: viajeTerminado,
              motivoSoloLectura: switch (etapa) {
                _ when viajeTerminado => mensajeViajeTerminado,
                _ when !puedeTrabajar => mensajeSoloConsulta,
                _ => null,
              },
              sinInspector: (momento) => conductorUsaExcepcion(etapa, rol: rol, momento: momento),
              alSinInspector: () => _sinInspector(context, ref, etapa, vinRuta),
              reciboSinEntrega: _reciboSinEntrega(etapa, rol),
              alReciboSinEntrega: () => _firmarReciboSinEntrega(context, ref, etapa),
              accion: accionDeEtapa(etapa, inventario),
              // El bloqueo nombra la etapa anterior, y el nombre sale del catálogo: el dominio solo da su código.
              nombreDeEtapa: (codigo) => catalogos.etiqueta(Catalogo.etapas, codigo),
              alCapturar: (momento) => alIr(
                // El **código** del proceso, no su etiqueta: con él se decide si la etapa pide checklist. El
                // nombre para la persona lo resuelve la captura con el mismo catálogo.
                '/inspecciones/vin/$vinRuta/captura/${etapa.id}?proceso=${Uri.encodeQueryComponent(etapa.proceso)}'
                '&momento=$momento'
                '${etapa.viajeId == null ? '' : '&viajeId=${Uri.encodeQueryComponent(etapa.viajeId!)}'}',
              ),
              alAbrirInforme: (momento) => _mostrarInforme(
                context,
                vin: inventario.vin,
                etapa: etapa,
                inspeccion: momento.inspeccion!,
                viajeTerminado: viajeTerminado,
              ),
              alFirmar: (accion) => alIr(
                '/inspecciones/vin/$vinRuta/firma/${accion.inspeccionId}?proceso=${Uri.encodeQueryComponent(catalogos.etiqueta(Catalogo.etapas, etapa.proceso))}'
                '&soloFirma=${accion.soloFirma ? 1 : 0}'
                // El viaje decide quiénes pueden firmar: sin él la pantalla no tiene a quién ofrecer.
                '${etapa.viajeId == null ? '' : '&viajeId=${Uri.encodeQueryComponent(etapa.viajeId!)}'}',
              ),
            ),
            const SizedBox(height: Espacio.sm),
          ],
      ],
    );
  }
}

/// Con qué venía el vehículo, en la ficha: los daños de todas sus inspecciones cerradas, de cualquier viaje.
///
/// Es lo que el «Último informe» no puede dar. Esa tarjeta muestra una inspección; esto muestra la historia, que
/// es lo que responde «¿este golpe ya venía?» cuando el golpe se registró en un viaje que esta ficha ni enumera.
class _HistorialDeDanos extends ConsumerWidget {
  const _HistorialDeDanos({required this.vin});

  final String vin;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    // Cuando la cola entrega algo, esto quedó viejo: tras corregir un informe se seguían viendo los daños de la
    // versión anterior hasta refrescar a mano. Solo cuando el contador **sube**: se reinicia al rearmarse la
    // cola, y reaccionar a eso dejaba la pantalla parpadeando entre «buscando» y el resultado.
    ref.listen(entregadosProvider, (anterior, actual) {
      if (anterior != null && actual > anterior) ref.invalidate(hallazgosPreviosProvider(vin));
    });
    final previos = ref.watch(hallazgosPreviosProvider(vin));
    return ListaHallazgosPrevios(
      titulo: 'Historial de daños',
      previos: previos.value ?? const [],
      cargando: previos.isLoading,
      alVencerFoto: () => ref.invalidate(hallazgosPreviosProvider(vin)),
    );
  }
}

/// Las novedades que registró el conductor y que nadie ha clasificado todavía, con su foto, para revisarlas.
///
/// Solo se le muestran a quien puede revisarlas. Mientras estén aquí, el hallazgo existe pero le falta lo que
/// decide qué pasa con el vehículo: su zona, su clase y su nivel.
class _NovedadesPorRevisar extends ConsumerWidget {
  const _NovedadesPorRevisar({required this.inventario, this.viajeTerminado = false});

  /// Se piden por VIN, no por viaje: en un viaje terminado las que salen son de **otro** viaje, el que sigue
  /// en curso, y nadie las va a revisar desde aquí.
  final bool viajeTerminado;

  final Inventario inventario;

  String get vin => inventario.vin;

  Future<void> _revisar(BuildContext context, WidgetRef ref, NovedadPendiente novedad) async {
    final revisada = await Navigator.of(context).push<bool>(
      MaterialPageRoute(builder: (_) => RevisionNovedadPage(novedad: novedad)),
    );
    if (revisada != true) return;
    // Al revisarla deja de estar pendiente y pasa a ser un hallazgo del informe: las dos listas cambian.
    ref.invalidate(novedadesPorRevisarProvider(vin));
    ref.invalidate(inventarioProvider(vin));
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final rol = ref.watch(authControllerProvider.select((e) => e.sesion?.usuario.rol));
    if (viajeTerminado || !puedeRevisarNovedades(rol)) return const SizedBox.shrink();

    final novedades = ref.watch(novedadesPorRevisarProvider(vin)).value ?? const <NovedadPendiente>[];
    if (novedades.isEmpty) return const SizedBox.shrink();

    final tema = Theme.of(context).textTheme;
    final catalogos = ref.watch(catalogosProvider);
    // Quién las registró: todas son del conductor del viaje, así que sale de ahí y no de cada novedad.
    final viajeId = inventario.etapas.map((e) => e.viajeId).nonNulls.firstOrNull;
    final conductor = viajeId == null ? null : ref.watch(viajeDeEtapaProvider(viajeId)).value?.conductorNombre;
    return Padding(
      padding: const EdgeInsets.only(top: Espacio.lg),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text('Novedades por revisar', style: tema.titleMedium),
          Text('Las registró el conductor. Les falta la zona, la clase y el nivel.', style: tema.bodySmall),
          const SizedBox(height: Espacio.sm),
          for (final novedad in novedades)
            Tarjeta(
              child: Row(
                children: [
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          etiquetaNovedad(novedad, catalogos),
                          style: tema.bodyMedium?.copyWith(color: Colores.textoPrimario),
                        ),
                        Text(detalleNovedad(novedad, conductor: conductor), style: tema.bodySmall),
                      ],
                    ),
                  ),
                  FilledButton(
                    // El tema les da un ancho mínimo infinito para que ocupen la fila solos; dentro de una fila
                    // hay que soltarlo o el cálculo del tamaño revienta.
                    style: FilledButton.styleFrom(minimumSize: const Size(0, alturaTactil)),
                    onPressed: () => _revisar(context, ref, novedad),
                    child: const Text('Revisar'),
                  ),
                ],
              ),
            ),
        ],
      ),
    );
  }
}

/// Marcar que la unidad no sale del viaje, o decir que ya no sale.
///
/// Solo se le ofrece al inspector de ese viaje y solo cuando hay un hallazgo V1+ en una inspección cerrada del
/// viaje: son las mismas condiciones que el servidor exige, y sin ellas responde 403 o 409.
class _NoSale extends ConsumerStatefulWidget {
  const _NoSale({required this.inventario});

  final Inventario inventario;

  @override
  ConsumerState<_NoSale> createState() => _EstadoNoSale();
}

class _EstadoNoSale extends ConsumerState<_NoSale> {
  bool _enviando = false;

  /// El viaje al que pertenece esta unidad hoy. Las etapas de un VIN cuelgan todas del mismo.
  String? get _viajeId => widget.inventario.etapas.map((e) => e.viajeId).nonNulls.firstOrNull;

  Future<void> _marcar(String viajeId, List<HallazgoVista> impiden) async {
    final datos = await pedirNoSale(context, hallazgos: impiden, catalogos: ref.read(catalogosProvider));
    if (datos == null || !mounted) return;

    // El aviso se toma antes de los `await`: después de ellos el widget puede ya no estar en pantalla.
    final avisos = ScaffoldMessenger.of(context);
    setState(() => _enviando = true);
    try {
      await ref.read(viajesRepositorioProvider).marcarNoSale(
            viajeId,
            widget.inventario.vin,
            motivo: datos.motivo,
            hallazgoId: datos.hallazgoId,
            vinReemplazo: datos.vinReemplazo,
          );
      // El viaje cambió para todos: la unidad deja de contar y el reemplazo entra con sus etapas.
      await ref.read(misViajesControllerProvider.notifier).cargar();
      ref.invalidate(viajeDeEtapaProvider(viajeId));
      ref.invalidate(inventarioProvider(widget.inventario.vin));
      avisos.showSnackBar(const SnackBar(content: Text('La unidad quedó marcada como que no sale.')));
    } on ErrorApi catch (e) {
      // Un 409 no se arregla reintentando (el hallazgo no es V1+, su inspección no está cerrada, ya no salía):
      // se muestra tal cual lo explica el servidor.
      avisos.showSnackBar(SnackBar(content: Text(e.mensajeDetallado)));
    } finally {
      if (mounted) setState(() => _enviando = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context).textTheme;
    final viajeId = _viajeId;
    if (viajeId == null) return const SizedBox.shrink();

    final viaje = ref.watch(viajeDeEtapaProvider(viajeId)).value;
    if (viaje == null) return const SizedBox.shrink();

    final unidad = viaje.vehiculos.where((v) => v.vin == widget.inventario.vin).firstOrNull;
    if (unidad != null && !unidad.sale) {
      return Tarjeta(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text('Esta unidad no sale', style: tema.titleSmall?.copyWith(color: Colores.peligro)),
            if (unidad.noSale case final no?) ...[
              Text(no.motivo, style: tema.bodySmall?.copyWith(color: Colores.textoPrimario)),
              if (no.vinReemplazo case final reemplazo?) Text('La reemplaza $reemplazo', style: tema.bodySmall),
            ],
          ],
        ),
      );
    }

    // En un viaje que ya cerró tampoco se marca: el servidor responde 409.
    if (!viaje.admiteTrabajo) return const SizedBox.shrink();

    final impiden = hallazgosQueImpidenSalir(widget.inventario, viajeId: viajeId);
    final puede = puedeMarcarNoSale(
      rol: ref.watch(authControllerProvider.select((e) => e.sesion?.usuario.rol)),
      esInspectorDelViaje: viaje.inspectorId != null &&
          viaje.inspectorId == ref.watch(authControllerProvider.select((e) => e.sesion?.usuario.id)),
      hayHallazgoV1Plus: impiden.isNotEmpty,
    );
    if (!puede) return const SizedBox.shrink();

    return OutlinedButton(
      onPressed: _enviando ? null : () => _marcar(viajeId, impiden),
      child: Text(
        _enviando ? 'Marcando...' : 'La unidad no sale',
        style: const TextStyle(color: Colores.peligro),
      ),
    );
  }
}

/// Abre un informe sin salir de la ficha: el mismo contenido de «Último informe», con su dibujo, sus fotos, su
/// cadena de versiones y lo que explique por qué no se corrige.
///
/// Hace falta porque la ficha solo le da tarjeta al informe vigente. Los demás —el del cargue que ya se firmó, la
/// llegada de una etapa anterior— no tenían dónde abrirse, así que su mensaje no se leía nunca por mucho que el
/// código lo pusiera.
Future<void> _mostrarInforme(
  BuildContext context, {
  required String vin,
  required EtapaVista etapa,
  required InspeccionVista inspeccion,
  required bool viajeTerminado,
}) =>
    showModalBottomSheet<void>(
      context: context,
      isScrollControlled: true,
      showDragHandle: true,
      builder: (_) => SafeArea(
        child: SingleChildScrollView(
          padding: const EdgeInsets.fromLTRB(Espacio.lg, 0, Espacio.lg, Espacio.lg),
          child: _UltimoInforme(
            vin: vin,
            etapa: etapa,
            inspeccion: inspeccion,
            anteriores: etapa.versionesAnteriores(inspeccion),
            viajeTerminado: viajeTerminado,
            enHoja: true,
          ),
        ),
      ),
    );

/// «Llanta trasera derecha: Golpe (De 1 a 5 cm) · V2». La clase y el nivel faltan en una novedad del conductor
/// que todavía no revisa un inspector.
///
/// El daño de un extra no tiene zona —no está en el dibujo—, así que en su lugar va el extra: «Platón: Golpe…».
/// Decir «Sin zona» ahí seria esconder de qué se está hablando.
String _lineaHallazgo(HallazgoVista h, Catalogos catalogos, {List<ExtraVista> extras = const []}) {
  final clase = h.clase == null ? 'Sin clase' : catalogos.etiqueta(Catalogo.clasesDano, h.clase!);
  // Un daño sin nivel no se deja en blanco: se dice que está sin calificar. En blanco parecía un daño leve, y
  // es justo al revés: es uno del que todavía no se sabe qué tan grave es.
  final nivel = h.nivel == null ? ' · Sin calificar' : ' · ${catalogos.etiqueta(Catalogo.nivelesSeveridad, h.nivel!)}';
  final donde = extras.where((e) => e.id == h.extraId).firstOrNull?.nombre ?? h.zona;
  return '$donde: $clase (${catalogos.etiqueta(Catalogo.tamanosDano, h.tamano)})$nivel';
}

class _UltimoInforme extends ConsumerWidget {
  const _UltimoInforme({
    required this.vin,
    required this.etapa,
    required this.inspeccion,
    this.anteriores = const [],
    this.viajeTerminado = false,
    this.enHoja = false,
  });

  /// Un viaje finalizado o cancelado no admite correcciones: el servidor responde 409.
  final bool viajeTerminado;

  /// Se está viendo dentro de la hoja que abre un momento, no en la ficha. Entonces hay que cerrarla antes de
  /// irse a otra pantalla: una hoja imperativa no se va sola y se queda encima de la cámara.
  final bool enHoja;

  /// El VIN del inventario: es la clave con la que se vuelve a pedir, cuando un enlace de foto vence.
  final String vin;

  final EtapaVista etapa;
  final InspeccionVista inspeccion;

  /// Las versiones que [inspeccion] dejó atrás, de la más reciente a la más antigua. Se pliegan debajo: no son el
  /// estado del carro, pero son lo que explica por qué este informe dice otra cosa que el anterior.
  final List<InspeccionVista> anteriores;

  /// El daño de esa zona, con su foto. La zona la da el dibujo; el hallazgo sale del informe que se está viendo.
  void _abrirDetalle(BuildContext context, WidgetRef ref, ZonaDano zona) {
    final hallazgo = inspeccion.hallazgos
        .where((h) => h.cuadranteNumero != null && claveDeZona(h.cuadranteNumero!, h.lado) == zona.clave)
        .firstOrNull;
    if (hallazgo == null) return;
    final foto = inspeccion.evidencias.where((e) => e.hallazgoId == hallazgo.id).firstOrNull;
    mostrarDetalleHallazgo(
      context,
      hallazgo: hallazgo,
      catalogos: ref.read(catalogosProvider),
      urlFoto: resolverUrlImagen(ref.read(entornoProvider).apiUrl, foto?.url),
      encabezadosFoto: ref.read(entornoProvider).encabezadosEnrutamiento,
      venceFoto: foto?.venceEn,
      alVencerFoto: () => ref.invalidate(inventarioProvider(vin)),
      sinSenal: !ref.read(syncControllerProvider).online,
    );
  }

  /// Escribe una nota sobre este informe y la deja en la cola. No se envía al momento a propósito: se escribe
  /// en el patio, donde puede no haber señal, y el informe que aclara ya no cambia.
  Future<void> _agregarNota(BuildContext context, WidgetRef ref) async {
    // El aviso se toma antes de abrir la hoja: después del await el widget puede ya no estar en pantalla.
    final avisos = ScaffoldMessenger.of(context);
    final texto = await pedirNotaAclaratoria(context, sobre: 'el informe de ${etapa.proceso}');
    if (texto == null) return;
    await ref.read(colaRepositorioProvider).agregar(NotaJob(
          id: const Uuid().v4(),
          etiqueta: 'Nota del informe ${etapa.proceso}',
          creadoEn: DateTime.now().millisecondsSinceEpoch,
          usuarioId: ref.read(authControllerProvider).sesion?.usuario.id,
          entidad: EntidadNota.inspeccion,
          entidadId: inspeccion.id,
          texto: texto,
        ));
    unawaited(ref.read(syncControllerProvider.notifier).sincronizar());
    avisos.showSnackBar(const SnackBar(content: Text('La nota quedó en la cola.')));
  }

  /// Abre la captura para rehacer este informe. Nada se crea en el servidor todavía: la versión siguiente se
  /// pide al enviar, porque el servidor no deja descartar una versión vacía y una corrección abandonada dejaría
  /// la etapa con un borrador que impide volver a corregirla.
  Future<void> _corregir(BuildContext context) async {
    // Lo que hay que rehacer no es lo mismo en todas: la salida del cargue es la acomodación, que no pide las
    // siete vistas sino volver a ubicar cada unidad en la niñera. Prometer «todas las fotos obligatorias» y
    // abrir otra cosa deja dudando de si se pulso lo que se quería.
    final esAcomodacion = esSegundaInspeccionDelCargue(proceso: etapa.proceso, momento: inspeccion.momento);
    final confirmar = await showDialog<bool>(
      context: context,
      builder: (contexto) => AlertDialog(
        title: const Text('Corregir este informe'),
        content: Text(
          'Vas a rehacer la inspección completa: hay que volver a '
          '${esAcomodacion ? 'ubicar todas las unidades en la niñera y a fotografiar la acomodación' : 'tomar todas las fotos obligatorias'}. '
          'La versión anterior no cambia: conserva sus fotos, sus hallazgos y su firma, y queda guardada '
          'como la versión anterior de esta.',
        ),
        actions: [
          TextButton(onPressed: () => Navigator.of(contexto).pop(false), child: const Text('Cancelar')),
          TextButton(onPressed: () => Navigator.of(contexto).pop(true), child: const Text('Corregir')),
        ],
      ),
    );
    if (confirmar != true || !context.mounted) return;
    final consulta = [
      'proceso=${Uri.encodeQueryComponent(etapa.proceso)}',
      'momento=${Uri.encodeQueryComponent(inspeccion.momento)}',
      if (etapa.viajeId != null) 'viajeId=${Uri.encodeQueryComponent(etapa.viajeId!)}',
      'corrige=${Uri.encodeQueryComponent(inspeccion.id)}',
    ].join('&');
    // Todo esto se toma **antes** de cerrar la hoja: después este widget ya no está montado y su `context` no
    // sirve ni para navegar ni para llegar a los proveedores.
    final enrutador = GoRouter.of(context);
    final contenedor = ProviderScope.containerOf(context, listen: false);
    if (enHoja) Navigator.of(context).pop();
    await enrutador.push<void>('/inspecciones/vin/${Uri.encodeComponent(vin)}/captura/${etapa.id}?$consulta');
    // Al volver de la captura, la ficha seguía mostrando la versión anterior como vigente, con su botón de
    // corregir. Si la corrección quedó en cola —sin señal— y alguien lo volvía a tocar, se encolaba una
    // segunda corrección de la misma inspección: el servidor la mata por el índice único, después de haber
    // repetido las siete fotos. Es la misma recarga que hace el resto de la pantalla al volver de capturar.
    contenedor.invalidate(inventarioProvider(vin));
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final tema = Theme.of(context).textTheme;
    final catalogos = ref.watch(catalogosProvider);
    final apiUrl = ref.watch(entornoProvider).apiUrl;
    // Enrutamiento de la pasarela, no autorización: sin esto IIS no sabe a qué servicio mandar la foto.
    final encabezadosFoto = ref.watch(entornoProvider).encabezadosEnrutamiento;
    final yo = ref.watch(authControllerProvider.select((e) => e.sesion?.usuario));
    final sinSenal = !ref.watch(syncControllerProvider).online;
    final fotos = [for (final e in inspeccion.evidencias) if (resolverUrlImagen(apiUrl, e.url) != null) e];
    // El conductor recibe el vehículo y firma su recibo. Si antes ve lo que marcó el inspector, su recibo ya
    // no dice lo que él encontró, sino que confirma lo del otro. Su propio informe sí lo ve entero.
    final ajeno = yo?.rol == 'conductor' && inspeccion.usuarioId != null && inspeccion.usuarioId != yo?.id;
    // El servidor solo acepta la nota de quien registró el informe y de los administradores: a los demás les
    // responde 403, así que a ellos no se les ofrece.
    final puedeAclarar =
        !ajeno && (inspeccion.usuarioId == yo?.id || yo?.rol == 'admin' || yo?.rol == 'super_admin');
    // Corregir es rehacer la inspección: la corrigen quien la hizo y los administradores, y solo mientras nadie
    // la haya firmado. Son las mismas reglas que el servidor hace cumplir desde el contrato 3.11.0 (403
    // CORRECCION_SOLO_AUTOR y 409 INSPECCION_YA_FIRMADA). Comprobarlas aquí no es duplicarlas: sin esto la
    // persona rehace las siete fotos obligatorias y el envío muere en el rechazo.
    final yaCorregida = etapa.fueCorregida(inspeccion);
    final suya = inspeccion.usuarioId != null && inspeccion.usuarioId == yo?.id;
    final esAdministrador = yo?.rol == 'admin' || yo?.rol == 'super_admin';
    // Cuenta cualquier firma y cualquiera de las cuatro formas de cierre. Una transferencia ya creada pero sin
    // ninguna firma no impide corregir.
    final transferencia = inspeccion.transferencia;
    final yaFirmada =
        transferencia != null && (transferencia.firmoEntrega || transferencia.firmoRecibe || transferencia.cerrada);
    final puedeCorregir = (suya || esAdministrador) &&
        inspeccion.cerrada &&
        !yaCorregida &&
        !yaFirmada &&
        !viajeTerminado;

    return Tarjeta(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Expanded(
                child: Text(
                  '${catalogos.etiqueta(Catalogo.etapas, etapa.proceso)} · ${inspeccion.momentoNombre}',
                  style: tema.titleMedium?.copyWith(fontSize: 15),
                ),
              ),
              Insignia(inspeccion.cerrada ? 'Cerrada' : 'Borrador', tono: inspeccion.cerrada ? TonoInsignia.exito : TonoInsignia.advertencia),
              // Dicho con una palabra, no solo con el color del texto de la versión: la pantalla se mira al sol
              // en el patio y el color es lo primero que se pierde.
              if (yaCorregida) ...[
                const SizedBox(width: Espacio.sm),
                const Insignia('Reemplazada', tono: TonoInsignia.advertencia),
              ],
            ],
          ),
          Text(formatearFecha(inspeccion.fecha), style: tema.bodySmall),
          if (textoDeVersion(inspeccion, corregida: etapa.fueCorregida(inspeccion)) case final version?)
            Text(version, style: tema.bodySmall?.copyWith(color: Colores.advertencia)),
          if (ajeno) ...[
            const SizedBox(height: Espacio.md),
            Text(
              'El detalle lo registra quien inspecciona. Al recibir, lo que tú encuentres queda en tu firma.',
              style: tema.bodySmall,
            ),
          ],
          if (!ajeno && fotos.isNotEmpty) ...[
            const SizedBox(height: Espacio.md),
            SizedBox(
              height: 88,
              child: ListView.separated(
                scrollDirection: Axis.horizontal,
                itemCount: fotos.length,
                separatorBuilder: (_, _) => const SizedBox(width: Espacio.sm),
                itemBuilder: (_, i) => GestureDetector(
                  // El enlace va firmado: el visor lo abre igual, sin encabezados.
                  onTap: () => mostrarVisorImagen(
                    context,
                    imagen: NetworkImage(resolverUrlImagen(apiUrl, fotos[i].url)!,
                        headers: encabezadosFoto.isEmpty ? null : encabezadosFoto),
                    titulo: 'Evidencia ${i + 1} de ${fotos.length}',
                  ),
                  child: FotoEvidencia(
                    url: resolverUrlImagen(apiUrl, fotos[i].url),
                    encabezados: encabezadosFoto,
                    ancho: 88,
                    alto: 88,
                    venceEn: fotos[i].venceEn,
                    sinSenal: sinSenal,
                    // Los enlaces nuevos vienen con el inventario: no sirve pedir otra vez la misma imagen.
                    alVencer: () => ref.invalidate(inventarioProvider(vin)),
                    semanticLabel: 'Foto de evidencia del informe',
                  ),
                ),
              ),
            ),
          ],
          if (!ajeno) ...[
            const SizedBox(height: Espacio.md),
            // Solo lectura: cada zona con daño va del color de su nivel y se toca para ver su detalle.
            SelectorZonaDano(
              contexto: 'vin-${inspeccion.id}',
              alturaMaxima: 300,
              danos: {
                for (final h in inspeccion.hallazgos)
                  if (h.cuadranteNumero case final numero?) claveDeZona(numero, h.lado): h.nivel,
              },
              alAbrirDano: (zona) => _abrirDetalle(context, ref, zona),
            ),
            const SizedBox(height: Espacio.md),
            if (inspeccion.hallazgos.isEmpty)
              Text('Sin hallazgos registrados.', style: tema.bodySmall)
            else
              for (final h in inspeccion.hallazgos)
                Text(
                  '• ${_lineaHallazgo(h, catalogos, extras: inspeccion.extras)}',
                  style: tema.bodySmall?.copyWith(color: Colores.textoPrimario),
                ),
          ],
          // Lo que una corrección cerrada dejó atrás. No suma al estado del carro, pero se sigue pudiendo abrir
          // con sus daños y sus fotos: es lo que responde «¿y los otros tres golpes que vi ayer?». Va plegado
          // para que lo primero que se lea sea la versión que vale.
          if (!ajeno && anteriores.isNotEmpty) ...[
            const SizedBox(height: Espacio.sm),
            Theme(
              data: Theme.of(context).copyWith(dividerColor: Colors.transparent),
              child: ExpansionTile(
                title: Text(
                  anteriores.length == 1
                      ? 'Ver la versión anterior, ya reemplazada'
                      : 'Ver las ${anteriores.length} versiones anteriores, ya reemplazadas',
                  style: tema.bodySmall?.copyWith(color: Colores.advertencia),
                ),
                tilePadding: EdgeInsets.zero,
                childrenPadding: EdgeInsets.zero,
                children: [
                  for (final previa in anteriores)
                    _UltimoInforme(vin: vin, etapa: etapa, inspeccion: previa, viajeTerminado: viajeTerminado),
                ],
              ),
            ),
          ],
          if (!ajeno && (inspeccion.notas?.trim().isNotEmpty ?? false)) ...[
            const SizedBox(height: Espacio.md),
            Text('Notas adicionales', style: tema.bodySmall),
            Text(inspeccion.notas!, style: tema.bodySmall?.copyWith(color: Colores.textoPrimario)),
          ],
          // Lo que se aclaró después de cerrarlo, debajo del informe que aclara. En un informe ajeno no se
          // muestran: una nota puede decir justo lo que al conductor se le oculta antes de firmar.
          if (!ajeno && inspeccion.notasAclaratorias.isNotEmpty) ...[
            const SizedBox(height: Espacio.md),
            Text('Notas aclaratorias', style: tema.bodySmall),
            for (final nota in inspeccion.notasAclaratorias)
              FilaNotaAclaratoria(nota: nota, cuando: formatearFecha(nota.creadoEn)),
          ],
          if (puedeAclarar) ...[
            const SizedBox(height: Espacio.sm),
            OutlinedButton(
              onPressed: () => _agregarNota(context, ref),
              child: const Text('Agregar nota aclaratoria'),
            ),
          ],
          if (puedeCorregir) ...[
            const SizedBox(height: Espacio.sm),
            OutlinedButton(onPressed: () => _corregir(context), child: const Text('Corregir')),
          ] else if (!ajeno && yaCorregida) ...[
            const SizedBox(height: Espacio.sm),
            Text(
              'Esta versión ya fue corregida: la que vale es la siguiente, que es la que estás viendo arriba.',
              style: tema.bodySmall?.copyWith(color: Colores.advertencia),
            ),
          ] else if (yaFirmada && inspeccion.cerrada && (suya || esAdministrador)) ...[
            const SizedBox(height: Espacio.sm),
            // El botón de la nota aclaratoria está justo encima y sus condiciones son las mismas: quien la hizo
            // o un administrador. Por eso se nombra sin repetirlo.
            Text(
              'Esta inspección ya fue firmada: ya no admite correcciones. Si hay algo que precisar, agrégalo como '
              'nota aclaratoria.',
              style: tema.bodySmall?.copyWith(color: Colores.advertencia),
            ),
          ] else if (!ajeno && inspeccion.cerrada && viajeTerminado) ...[
            const SizedBox(height: Espacio.sm),
            Text('El viaje ya terminó: este informe no admite correcciones.', style: tema.bodySmall),
          ],
        ],
      ),
    );
  }
}

class _TarjetaEtapa extends StatelessWidget {
  const _TarjetaEtapa({
    required this.etapa,
    required this.procesoNombre,
    required this.momentos,
    required this.motivoSoloLectura,
    required this.accion,
    required this.nombreDeEtapa,
    required this.alCapturar,
    required this.alFirmar,
    required this.sinInspector,
    required this.alSinInspector,
    required this.reciboSinEntrega,
    required this.alReciboSinEntrega,
    required this.alAbrirInforme,
    this.viajeTerminado = false,
  });

  final EtapaVista etapa;

  /// El viaje ya termino: esta etapa no espera nada de nadie.
  final bool viajeTerminado;

  /// Nombre de la etapa segun el catalogo, ya resuelto por quien la construye.
  final String procesoNombre;
  final List<MomentoEtapa> momentos;

  /// La captura de esta etapa no le toca a quien está mirando.
  /// Por qué esta etapa no se puede trabajar; null si sí se puede. Se dice en la tarjeta en vez de ofrecer
  /// acciones que el servidor va a rechazar.
  final String? motivoSoloLectura;
  final AccionEtapa accion;
  final String Function(String codigoEtapa) nombreDeEtapa;
  final void Function(String momento) alCapturar;
  final void Function(FirmarTransferencia accion) alFirmar;

  /// ¿En este momento el conductor no captura sino que activa «No hay inspector»?
  final bool Function(String momento) sinInspector;
  final VoidCallback alSinInspector;

  /// La salida sin inspector cuya transferencia espera la firma del conductor; null si no aplica.
  final InspeccionVista? reciboSinEntrega;
  final VoidCallback alReciboSinEntrega;

  /// Abre el informe de ese momento. Se ofrece en cualquier etapa, también en las que ya no se trabajan: leer lo
  /// que quedó registrado no es trabajar el carro.
  final void Function(MomentoEtapa momento) alAbrirInforme;

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context).textTheme;
    return Tarjeta(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(procesoNombre, style: tema.titleMedium?.copyWith(fontSize: 15)),
                    // El lugar lo resuelve la API («CEDI 1»); no todas las etapas tienen uno.
                    if (etapa.lugar case final lugar? when lugar.isNotEmpty)
                      Text(lugar, style: tema.bodySmall),
                  ],
                ),
              ),
              // Dice lo que la etapa espera. Con el viaje terminado ya no espera nada, y dejarla puesta se lee
              // como un pendiente: fue lo primero que se busco al abrir una unidad de un viaje cerrado.
              if (!viajeTerminado)
                Insignia(
                  etapa.requiereInspeccion ? 'Requiere informe' : 'Solo firma',
                  tono: etapa.requiereInspeccion ? TonoInsignia.info : TonoInsignia.neutro,
                ),
            ],
          ),
          // El motivo se dice, pero los momentos se siguen viendo: esconderlos dejaba sin forma de abrir lo que ya
          // está registrado, que es justo lo que viene a leer quien consulta y lo que queda de un viaje terminado.
          if (motivoSoloLectura case final motivo?)
            Padding(
              padding: const EdgeInsets.only(top: Espacio.sm),
              child: Text(motivo, style: tema.bodySmall),
            ),
          for (final momento in momentos) ...[
            const SizedBox(height: Espacio.sm),
            _FilaMomento(
              momento: momento,
              nombreDeEtapa: nombreDeEtapa,
              alCapturar: () => alCapturar(momento.momento),
              sinInspector: sinInspector(momento.momento),
              alSinInspector: alSinInspector,
              soloLectura: motivoSoloLectura != null,
              alAbrir: momento.inspeccion == null ? null : () => alAbrirInforme(momento),
            ),
          ],
          // Lo que pasó en el proceso alrededor de estas inspecciones: cargue sin inspector, recibo sin entrega.
          for (final inspeccion in etapa.inspecciones)
            for (final novedad in inspeccion.novedadesProceso)
              Padding(
                padding: const EdgeInsets.only(top: Espacio.sm),
                child: Text(
                  '${etiquetar(etiquetaNovedadProceso, novedad.tipo)} · ${novedad.resumen}',
                  style: tema.bodySmall?.copyWith(color: Colores.advertencia),
                ),
              ),
          const SizedBox(height: Espacio.sm),
          if (motivoSoloLectura == null)
            switch (accion) {
            // Cerrada de cualquiera de las tres formas: ya no admite firmas, y ofrecerlas solo llevaría al 409
            // del servidor.
            FirmarTransferencia(cerrada: true, transferencia: final t?, momentoNombre: final cual) => Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Insignia(
                    etiquetar(etiquetaTipoCierre, t.tipoCierre!),
                    tono: t.tipoCierre == TipoCierre.noFirma ? TonoInsignia.advertencia : TonoInsignia.exito,
                  ),
                  // Cuál, y no solo que sí: el cambio de responsabilidad se firma sobre un informe concreto. En
                  // una etapa con la llegada firmada y la salida sin firmar, la insignia sola se leía como si
                  // toda la etapa estuviera cerrada, y al lado seguía el «Corregir» de la otra.
                  if (cual != null) ...[
                    const SizedBox(height: Espacio.xs),
                    Text('Es la firma de la ${cual.toLowerCase()}.', style: tema.bodySmall),
                  ],
                ],
              ),
            final FirmarTransferencia a => Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  // Entre una firma y la otra pueden pasar horas, porque cada parte firma desde su sesión. Sin
                  // esto la tarjeta se ve igual antes y después de firmar.
                  if (a.transferencia?.faltaRecibe case final falta?) ...[
                    Text(
                      falta ? 'Firmó quien entrega; falta quien recibe.' : 'Firmó quien recibe; falta quien entrega.',
                      style: tema.bodySmall?.copyWith(color: Colores.advertencia),
                    ),
                    const SizedBox(height: Espacio.sm),
                  ],
                  if (reciboSinEntrega != null)
                    // Nadie de la empresa entrega: en vez de esperar una firma que no llega, el conductor cierra con la suya.
                    FilledButton(onPressed: alReciboSinEntrega, child: const Text('Firmar recibo sin entrega'))
                  else
                    FilledButton(
                      onPressed: () => alFirmar(a),
                      child: Text(a.soloFirma ? 'Revisar y firmar' : 'Firmar transferencia'),
                    ),
                ],
              ),
              SinInforme() => Text('No hay informe previo para firmar en esta etapa.', style: tema.bodySmall),
            },
        ],
      ),
    );
  }
}

/// Una de las dos filas de la etapa: «Inspección de salida» o «Inspección de llegada», con lo que hay hecho y lo
/// que se puede hacer.
class _FilaMomento extends StatelessWidget {
  const _FilaMomento({
    required this.momento,
    required this.nombreDeEtapa,
    required this.alCapturar,
    required this.sinInspector,
    required this.alSinInspector,
    required this.soloLectura,
    required this.alAbrir,
  });

  final MomentoEtapa momento;
  final String Function(String codigoEtapa) nombreDeEtapa;
  final VoidCallback alCapturar;

  /// La salida del cargue del conductor: en vez de «Capturar», «No hay inspector».
  final bool sinInspector;
  final VoidCallback alSinInspector;

  /// Esta etapa ya no se trabaja: se lee lo que hay, pero no se ofrece capturar.
  final bool soloLectura;

  /// Abre el informe de este momento; null cuando todavía no hay ninguno que abrir.
  final VoidCallback? alAbrir;

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context).textTheme;
    final inspeccion = momento.inspeccion;
    // El nombre lo manda la API cuando la inspección existe; para la que todavía no, lo pone la app.
    final nombre = inspeccion?.momentoNombre ?? etiquetar(etiquetaMomento, momento.momento);
    final versionTexto = inspeccion == null ? null : textoDeVersion(inspeccion);

    return InkWell(
      onTap: alAbrir,
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(nombre, style: tema.bodyMedium?.copyWith(color: Colores.textoPrimario)),
                if (versionTexto != null) Text(versionTexto, style: tema.bodySmall?.copyWith(color: Colores.advertencia)),
                if (momento.cerrada && inspeccion?.cerradaEn != null)
                  Text('Cerrada el ${formatearFecha(inspeccion!.cerradaEn)}', style: tema.bodySmall)
                else if (momento.enBorrador)
                  Text('Empezada el ${formatearFecha(inspeccion!.fecha)}', style: tema.bodySmall)
                else if (momento.bloqueo case final bloqueo?)
                  Text(
                    mensajeBloqueo(bloqueo.motivo, nombreDeEtapa(bloqueo.procesoPrevio)),
                    style: tema.bodySmall?.copyWith(color: Colores.advertencia),
                  ),
              ],
            ),
          ),
          const SizedBox(width: Espacio.sm),
          if (momento.cerrada)
            const Insignia('Cerrada', tono: TonoInsignia.exito)
          else if (momento.enBorrador)
            const Insignia('Borrador', tono: TonoInsignia.info)
          else if (!soloLectura)
            // Deshabilitado, no escondido: que se vea que falta y por qué.
            sinInspector
                ? BotonEnFila(texto: 'No hay inspector', onPressed: momento.sePuedeCapturar ? alSinInspector : null)
                : BotonEnFila(texto: 'Capturar', onPressed: momento.sePuedeCapturar ? alCapturar : null),
          // La punta de flecha es lo único que dice que la fila se abre; sin ella nadie la toca.
          if (alAbrir != null) const Icon(Icons.chevron_right, size: 20, color: Colores.textoSecundario),
        ],
      ),
    );
  }
}
