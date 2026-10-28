import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../app/proveedores.dart';
import '../../../core/errores/error_api.dart';
import '../../../core/http/urls_api.dart';
import '../../../core/tema/tema.dart';
import '../../../core/ui/visor_imagen.dart';
import '../../../core/ui/widgets.dart';
import '../../../core/util/formato.dart';
import '../../auth/presentation/auth_controller.dart';
import '../../catalogos/domain/catalogos.dart';
import '../../catalogos/presentation/catalogos_controller.dart';
import '../../dibujo_dano/domain/dibujo_dano.dart';
import '../../inspecciones/domain/novedad_pendiente.dart';
import '../../inspecciones/presentation/revision_novedad_page.dart';
import '../../inspecciones/providers.dart';
import '../../dibujo_dano/presentation/selector_zona_dano.dart';
import '../../viajes/presentation/mis_viajes_controller.dart';
import '../../viajes/providers.dart';
import '../domain/etiquetas.dart';
import '../domain/inventario.dart';
import '../domain/no_sale.dart';
import 'detalle_hallazgo.dart';
import 'hoja_no_sale.dart';
import '../providers.dart';

final inventarioProvider = FutureProvider.autoDispose.family<Inventario, String>(
  (ref, vin) => ref.watch(vehiculosRepositorioProvider).inventario(vin),
);

class VinPage extends ConsumerWidget {
  const VinPage({super.key, required this.vin});

  final String vin;

  Future<void> _ir(BuildContext context, WidgetRef ref, String ruta) async {
    await context.push(ruta);
    // Al volver de capturar o firmar, el inventario puede haber cambiado.
    ref.invalidate(inventarioProvider(vin));
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final inventario = ref.watch(inventarioProvider(vin));
    return Scaffold(
      appBar: AppBar(title: TextoMono(vin)),
      body: inventario.when(
        loading: () => const EstadoCargando(),
        error: (error, _) => EstadoMensaje(
          icono: Icons.warning_amber_rounded,
          colorIcono: Colores.peligro,
          titulo: 'No se pudo cargar el VIN',
          descripcion: error is ErrorApi && error.status == 404 ? 'Este VIN no está registrado. Pide al administrador que lo cargue.' : mensajeDeError(error),
          accion: FilledButton(onPressed: () => ref.invalidate(inventarioProvider(vin)), child: const Text('Reintentar')),
        ),
        data: (inv) => RefreshIndicator(
          onRefresh: () => ref.refresh(inventarioProvider(vin).future),
          child: _Detalle(inventario: inv, alIr: (ruta) => _ir(context, ref, ruta)),
        ),
      ),
    );
  }
}

class _Detalle extends ConsumerWidget {
  const _Detalle({required this.inventario, required this.alIr});

  final Inventario inventario;
  final void Function(String ruta) alIr;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final tema = Theme.of(context).textTheme;
    final catalogos = ref.watch(catalogosProvider);
    final ultima = inventario.ultimaInspeccion;
    final vinRuta = Uri.encodeComponent(inventario.vin);
    final rol = ref.watch(authControllerProvider.select((e) => e.sesion?.usuario.rol)) ?? '';

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
        _NovedadesPorRevisar(inventario: inventario),
        const SizedBox(height: Espacio.lg),
        Text('Último informe', style: tema.titleMedium),
        const SizedBox(height: Espacio.sm),
        if (ultima == null)
          Tarjeta(child: Text('Este VIN aún no tiene informes.', style: tema.bodySmall))
        else
          _UltimoInforme(etapa: ultima.etapa, inspeccion: ultima.inspeccion),
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
              capturaAjena: !puedeCapturar(etapa, rol: rol),
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

/// Las novedades que registró el conductor y que nadie ha clasificado todavía, con su foto, para revisarlas.
///
/// Solo se le muestran a quien puede revisarlas. Mientras estén aquí, el hallazgo existe pero le falta lo que
/// decide qué pasa con el vehículo: su zona, su clase y su nivel.
class _NovedadesPorRevisar extends ConsumerWidget {
  const _NovedadesPorRevisar({required this.inventario});

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
    if (!puedeRevisarNovedades(rol)) return const SizedBox.shrink();

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

/// «Llanta trasera derecha: Golpe (De 1 a 5 cm) · V2». La clase y el nivel faltan en una novedad del conductor
/// que todavía no revisa un inspector.
String _lineaHallazgo(HallazgoVista h, Catalogos catalogos) {
  final clase = h.clase == null ? 'Sin clase' : catalogos.etiqueta(Catalogo.clasesDano, h.clase!);
  final nivel = h.nivel == null ? '' : ' · ${catalogos.etiqueta(Catalogo.nivelesSeveridad, h.nivel!)}';
  return '${h.zona}: $clase (${catalogos.etiqueta(Catalogo.tamanosDano, h.tamano)})$nivel';
}

class _UltimoInforme extends ConsumerWidget {
  const _UltimoInforme({required this.etapa, required this.inspeccion});

  final EtapaVista etapa;
  final InspeccionVista inspeccion;

  /// El daño de esa zona, con su foto. La zona la da el dibujo; el hallazgo sale del informe que se está viendo.
  void _abrirDetalle(BuildContext context, WidgetRef ref, ZonaDano zona) {
    final hallazgo = inspeccion.hallazgos
        .where((h) => h.cuadranteNumero != null && claveDeZona(h.cuadranteNumero!, h.lado) == zona.clave)
        .firstOrNull;
    if (hallazgo == null) return;
    final foto = inspeccion.evidencias.where((e) => e.hallazgoId == hallazgo.id).firstOrNull;
    final token = ref.read(authControllerProvider).sesion?.token;
    mostrarDetalleHallazgo(
      context,
      hallazgo: hallazgo,
      catalogos: ref.read(catalogosProvider),
      urlFoto: resolverUrlImagen(ref.read(entornoProvider).apiUrl, foto?.url),
      encabezados: token == null ? null : {'Authorization': 'Bearer $token'},
    );
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final tema = Theme.of(context).textTheme;
    final catalogos = ref.watch(catalogosProvider);
    final apiUrl = ref.watch(entornoProvider).apiUrl;
    final token = ref.watch(authControllerProvider.select((e) => e.sesion?.token));
    final yo = ref.watch(authControllerProvider.select((e) => e.sesion?.usuario));
    final fotos = [for (final e in inspeccion.evidencias) if (resolverUrlImagen(apiUrl, e.url) != null) resolverUrlImagen(apiUrl, e.url)!];
    // El conductor recibe el vehículo y firma su recibo. Si antes ve lo que marcó el inspector, su recibo ya
    // no dice lo que él encontró, sino que confirma lo del otro. Su propio informe sí lo ve entero.
    final ajeno = yo?.rol == 'conductor' && inspeccion.usuarioId != null && inspeccion.usuarioId != yo?.id;

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
            ],
          ),
          Text(formatearFecha(inspeccion.fecha), style: tema.bodySmall),
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
                  onTap: () => mostrarVisorImagen(
                    context,
                    imagen: NetworkImage(fotos[i], headers: token == null ? null : {'Authorization': 'Bearer $token'}),
                    titulo: 'Evidencia ${i + 1} de ${fotos.length}',
                  ),
                  child: ClipRRect(
                    borderRadius: BorderRadius.circular(6),
                    child: Image.network(
                      fotos[i],
                      width: 88,
                      height: 88,
                      fit: BoxFit.cover,
                      // Si la API no sirve las imágenes públicamente, se piden con la sesión.
                      headers: token == null ? null : {'Authorization': 'Bearer $token'},
                      semanticLabel: 'Foto de evidencia del informe',
                      errorBuilder: (_, _, _) => Container(width: 88, height: 88, color: Colores.borde, child: const Icon(Icons.broken_image_outlined)),
                    ),
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
                Text('• ${_lineaHallazgo(h, catalogos)}', style: tema.bodySmall?.copyWith(color: Colores.textoPrimario)),
          ],
          if (!ajeno && (inspeccion.notas?.trim().isNotEmpty ?? false)) ...[
            const SizedBox(height: Espacio.md),
            Text('Notas adicionales', style: tema.bodySmall),
            Text(inspeccion.notas!, style: tema.bodySmall?.copyWith(color: Colores.textoPrimario)),
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
    required this.capturaAjena,
    required this.accion,
    required this.nombreDeEtapa,
    required this.alCapturar,
    required this.alFirmar,
  });

  final EtapaVista etapa;

  /// Nombre de la etapa segun el catalogo, ya resuelto por quien la construye.
  final String procesoNombre;
  final List<MomentoEtapa> momentos;

  /// La captura de esta etapa no le toca a quien está mirando.
  final bool capturaAjena;
  final AccionEtapa accion;
  final String Function(String codigoEtapa) nombreDeEtapa;
  final void Function(String momento) alCapturar;
  final void Function(FirmarTransferencia accion) alFirmar;

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
              Insignia(etapa.requiereInspeccion ? 'Requiere informe' : 'Solo firma', tono: etapa.requiereInspeccion ? TonoInsignia.info : TonoInsignia.neutro),
            ],
          ),
          if (capturaAjena)
            Padding(
              padding: const EdgeInsets.only(top: Espacio.sm),
              child: Text(mensajeCargueConApoyo, style: tema.bodySmall),
            )
          else
            for (final momento in momentos) ...[
              const SizedBox(height: Espacio.sm),
              _FilaMomento(momento: momento, nombreDeEtapa: nombreDeEtapa, alCapturar: () => alCapturar(momento.momento)),
            ],
          const SizedBox(height: Espacio.sm),
          switch (accion) {
            // Cerrada de cualquiera de las tres formas: ya no admite firmas, y ofrecerlas solo llevaría al 409
            // del servidor.
            FirmarTransferencia(cerrada: true, transferencia: final t?) => Insignia(
                etiquetar(etiquetaTipoCierre, t.tipoCierre!),
                tono: t.tipoCierre == TipoCierre.noFirma ? TonoInsignia.advertencia : TonoInsignia.exito,
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
  const _FilaMomento({required this.momento, required this.nombreDeEtapa, required this.alCapturar});

  final MomentoEtapa momento;
  final String Function(String codigoEtapa) nombreDeEtapa;
  final VoidCallback alCapturar;

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context).textTheme;
    final inspeccion = momento.inspeccion;
    // El nombre lo manda la API cuando la inspección existe; para la que todavía no, lo pone la app.
    final nombre = inspeccion?.momentoNombre ?? etiquetar(etiquetaMomento, momento.momento);

    return Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(nombre, style: tema.bodyMedium?.copyWith(color: Colores.textoPrimario)),
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
        else
          // Deshabilitado, no escondido: que se vea que falta y por qué.
          BotonEnFila(texto: 'Capturar', onPressed: momento.sePuedeCapturar ? alCapturar : null),
      ],
    );
  }
}
