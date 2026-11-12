import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:uuid/uuid.dart';

import '../../../core/errores/error_api.dart';
import '../../../core/tema/tema.dart';
import '../../../core/ui/widgets.dart';
import '../../auth/presentation/auth_controller.dart';
import '../../custodia/presentation/lienzo_firma.dart';
import '../../sync/domain/job.dart';
import '../../sync/domain/desenlace_envio.dart';
import '../../sync/presentation/sync_controller.dart';
import '../../sync/providers.dart';
import '../domain/entrega_punto.dart';
import '../providers.dart';
import 'fijar_ubicacion.dart';

/// Lo que admite el servidor en el motivo de un cierre con una sola firma.
const motivoUnaFirmaMax = 300;

/// Lo que admite el servidor en el nombre y la cédula de quien recibe sin ser usuario. Los mínimos también son
/// suyos: comprobarlos aquí evita firmar, encolar y recibir un 400 con el carro ya entregado.
const nombreExternoMin = 3;
const nombreExternoMax = 150;
const cedulaExternaMax = 30;

/// Las entregas de un viaje, una por lugar de destino: «Aquí entregas estas N unidades».
class EntregasViajePage extends ConsumerWidget {
  const EntregasViajePage({super.key, required this.viajeId, this.viajeSinInspector = false});

  final String viajeId;

  /// El viaje no tiene inspector asignado: entonces nadie del sistema puede recibir y la entrega la firma quien
  /// esté en el punto. Viene de quien abre la pantalla, que ya tiene el viaje en la mano.
  final bool viajeSinInspector;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final tema = Theme.of(context).textTheme;
    final puntos = ref.watch(entregasDelViajeProvider(viajeId));
    return Scaffold(
      appBar: AppBar(title: const Text('Entrega por punto')),
      body: SafeArea(
        child: puntos.when(
          loading: () => const EstadoCargando(),
          error: (error, _) => EstadoMensaje(
            icono: Icons.warning_amber_rounded,
            colorIcono: Colores.peligro,
            titulo: 'No se pudieron cargar las entregas',
            descripcion: mensajeDeError(error, 'Las entregas se consultan con señal.'),
            accion: FilledButton(
              onPressed: () => ref.invalidate(entregasDelViajeProvider(viajeId)),
              child: const Text('Reintentar'),
            ),
          ),
          data: (lista) => lista.isEmpty
              ? const EstadoMensaje(
                  icono: Icons.local_shipping_outlined,
                  titulo: 'Este viaje no tiene puntos de entrega',
                  descripcion: 'Las unidades no tienen un destino asignado todavía.',
                )
              : RefreshIndicator(
                  onRefresh: () async => ref.invalidate(entregasDelViajeProvider(viajeId)),
                  child: ListView(
                    padding: const EdgeInsets.all(Espacio.lg),
                    children: [
                      for (final punto in lista) ...[
                        _TarjetaPunto(
                          punto: punto,
                          alFirmar: () async {
                            await Navigator.of(context).push<void>(
                              MaterialPageRoute(
                                builder: (_) =>
                                    FirmaEntregaPuntoPage(punto: punto, viajeSinInspector: viajeSinInspector),
                              ),
                            );
                            ref.invalidate(entregasDelViajeProvider(viajeId));
                          },
                        ),
                        const SizedBox(height: Espacio.md),
                      ],
                      Text(
                        'En cada punto se firma una sola vez para todas las unidades que se entregan allí.',
                        style: tema.bodySmall,
                      ),
                    ],
                  ),
                ),
        ),
      ),
    );
  }
}

class _TarjetaPunto extends ConsumerWidget {
  const _TarjetaPunto({required this.punto, required this.alFirmar});

  final EntregaPunto punto;
  final VoidCallback alFirmar;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final esConductor = ref.watch(authControllerProvider).sesion?.usuario.rol == 'conductor';
    final tema = Theme.of(context).textTheme;
    final lugar = [punto.lugar, punto.ciudad].whereType<String>().where((p) => p.isNotEmpty).join(' · ');
    return Tarjeta(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              Expanded(child: Text(lugar, style: tema.titleSmall)),
              if (punto.entregada) const Insignia('Entregada', tono: TonoInsignia.exito),
            ],
          ),
          const SizedBox(height: Espacio.xs),
          Text(
            punto.entregada
                ? 'Se entregaron las ${punto.unidades.length} unidades de este punto.'
                : 'Aquí entregas ${punto.pendientes == 1 ? 'esta unidad' : 'estas ${punto.pendientes} unidades'}',
            style: tema.bodyMedium?.copyWith(color: Colores.textoPrimario),
          ),
          const SizedBox(height: Espacio.sm),
          for (final unidad in punto.unidades) _FilaUnidad(unidad),
          if (esConductor) ...[
            const SizedBox(height: Espacio.sm),
            FijarUbicacionLugar(lugarId: punto.lugarId),
          ],
          if (!punto.entregada) ...[
            const SizedBox(height: Espacio.sm),
            FilledButton(onPressed: alFirmar, child: const Text('Firmar la entrega')),
          ],
        ],
      ),
    );
  }
}

/// «la unidad» o «las N unidades», según cuántas se entreguen en el punto.
String _deLaUnidad(int n) => n == 1 ? 'la unidad' : 'las $n unidades';

class _FilaUnidad extends StatelessWidget {
  const _FilaUnidad(this.unidad);

  final UnidadEntrega unidad;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: Espacio.xs),
      // Un VIN son diecisiete caracteres en monoespaciada y no se parte por ninguna parte; la insignia más larga
      // se lleva otro tanto. En un teléfono angosto no caben lado a lado, y en una fila se comían el VIN. Así la
      // insignia baja a la línea siguiente cuando no cabe, y cuando cabe se queda a la derecha como siempre.
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Wrap(
            alignment: WrapAlignment.spaceBetween,
            crossAxisAlignment: WrapCrossAlignment.center,
            spacing: Espacio.sm,
            runSpacing: Espacio.xs,
            children: [
              TextoMono(unidad.vin),
              switch (unidad.estado) {
                EstadoEntrega.cerrada => const Insignia('Cerrada', tono: TonoInsignia.exito),
                EstadoEntrega.pendiente => const Insignia('Por firmar', tono: TonoInsignia.info),
                EstadoEntrega.sinInspeccion =>
                  const Insignia('Sin inspección de llegada', tono: TonoInsignia.advertencia),
              },
            ],
          ),
          // Quién recibió, cuando no fue un usuario del sistema. Es lo que hay que poder mirar después: una
          // entrega cerrada sin un nombre al lado no dice a quién se le entregó el carro.
          if (unidad.recibioExterno case final nombre?)
            Text('Recibió: $nombre', style: Theme.of(context).textTheme.bodySmall),
        ],
      ),
    );
  }
}

/// Firma de la entrega de un punto: una para todas las unidades, o el cierre con una sola firma cuando la otra
/// parte no está. Se encola: sin señal se envía al volver, y repetirla no duplica nada.
class FirmaEntregaPuntoPage extends ConsumerStatefulWidget {
  const FirmaEntregaPuntoPage({super.key, required this.punto, this.viajeSinInspector = false});

  final EntregaPunto punto;

  /// Sin inspector en el viaje no hay a quién preguntarle: recibe quien esté en el punto.
  final bool viajeSinInspector;

  @override
  ConsumerState<FirmaEntregaPuntoPage> createState() => _EstadoFirmaEntregaPunto();
}

class _EstadoFirmaEntregaPunto extends ConsumerState<FirmaEntregaPuntoPage> {
  String? _firma;
  bool _unaSolaFirma = false;
  bool _guardando = false;
  final _motivo = TextEditingController();

  /// Recibe alguien del punto que no es usuario de la app. Lo dice quien está allí: el contrato no trae si el
  /// punto tiene inspector asignado, eso solo lo sabe el servidor —y rechaza con 400 los datos de un externo en
  /// un punto que sí lo tiene—.
  ///
  /// Sin inspector en el viaje empieza encendido, porque es lo normal; pero se puede apagar. Dejarlo fijo
  /// encerraba al conductor de un viaje sin inspector que entrega en un punto que sí tiene inspectores de
  /// planta: allí el servidor rechaza los datos del externo y no había cómo quitarlos.
  late bool _recibeExterno = widget.viajeSinInspector;
  final _nombreRecibe = TextEditingController();
  final _cedulaRecibe = TextEditingController();
  String? _firmaRecibe;

  /// Si el servidor ya corrigió una vez quién recibe en este punto. A la segunda, corrigiendo al revés, no hay
  /// camino: es el viaje sin inspector entregando donde sí lo hay, y entonces se dice en vez de volver a girar.
  bool _puntoYaCorregido = false;

  @override
  void dispose() {
    _motivo.dispose();
    _nombreRecibe.dispose();
    _cedulaRecibe.dispose();
    super.dispose();
  }

  EntregaPunto get _punto => widget.punto;

  /// Por qué no se puede firmar todavía, o null si sí se puede.
  ///
  /// Un botón gris sin motivo deja a quien está en el punto adivinando. La cuenta va primero porque es lo que
  /// se necesita para decidir qué hacer ahora: dos unidades por descargar es un rato, ocho es otra cosa.
  String? _motivoSinFirmar({required bool externo}) {
    final faltan = _punto.sinInspeccion.length;
    if (faltan > 0) {
      return faltan == 1
          ? 'Falta 1 unidad por descargar: hazle la inspección de llegada antes de firmar.'
          : 'Faltan $faltan unidades por descargar: hazles la inspección de llegada antes de firmar.';
    }
    if (_firma == null) return 'Dibuja tu firma arriba.';
    if (_unaSolaFirma) {
      return _motivo.text.trim().isEmpty ? 'Escribe por qué no firma la otra parte.' : null;
    }
    // Los tres datos de quien recibe van juntos o no va ninguno: el servidor los exige juntos y rechaza el
    // envío entero. Se reclaman de uno en uno, en el orden en que están en pantalla.
    if (externo) {
      final nombre = _nombreRecibe.text.trim();
      final cedula = _cedulaRecibe.text.trim();
      if (nombre.isEmpty) return 'Escribe el nombre de quien recibe.';
      if (nombre.length < nombreExternoMin) return 'El nombre de quien recibe es muy corto: escríbelo completo.';
      if (cedula.isEmpty) return 'Escribe la cédula de quien recibe.';
      // Lo mismo que exige el servidor: puede llevar puntos, guiones o espacios, pero algún número tiene que
      // haber. Sin esto se firma, se encola y vuelve un 400 con el carro ya entregado.
      if (!cedula.contains(RegExp('[0-9]'))) return 'La cédula de quien recibe tiene que llevar números.';
      if (_firmaRecibe == null) return 'Falta la firma de quien recibe.';
    }
    return null;
  }

  /// Una sola regla: se envía cuando no queda nada que reclamar. Tenerla por duplicado era tener dos que se
  /// desalinean, y entonces el botón se habilita con el motivo todavía en pantalla.
  bool _puedeEnviar({required bool externo}) => !_guardando && _motivoSinFirmar(externo: externo) == null;

  Future<void> _registrar({required bool externo}) async {
    final usuarioId = ref.read(authControllerProvider).sesion?.usuario.id;
    final firma = _firma;
    if (usuarioId == null || firma == null || !_puedeEnviar(externo: externo)) return;
    // Solo cuando recibe de verdad una persona de fuera: en el cierre con una sola firma no hay receptor, y en
    // un punto con inspector el servidor rechaza estos campos con 400.
    final conExterno = externo && !_unaSolaFirma;
    setState(() => _guardando = true);
    try {
      final jobId = const Uuid().v4();
      await ref.read(colaRepositorioProvider).agregar(EntregaPuntoJob(
            id: jobId,
            etiqueta: 'Entrega ${_punto.lugar}',
            creadoEn: DateTime.now().millisecondsSinceEpoch,
            usuarioId: usuarioId,
            viajeId: _punto.viajeId,
            lugarId: _punto.lugarId,
            firma: firma,
            motivo: _unaSolaFirma ? _motivo.text.trim() : null,
            recibeNombre: conExterno ? _nombreRecibe.text.trim() : null,
            recibeCedula: conExterno ? _cedulaRecibe.text.trim() : null,
            firmaRecibe: conExterno ? _firmaRecibe : null,
          ));
      final sync = ref.read(syncControllerProvider.notifier);
      await sync.refrescar();
      await sync.sincronizar();
      // Se mira ESTE envío y no el contador de la cola: con dos pendientes de otra cosa, una entrega que el
      // servidor acababa de aceptar se anunciaba como «quedó en cola».
      final desenlace = desenlaceDe(
        await ref.read(colaRepositorioProvider).listar(),
        jobId,
        pausa: ref.read(syncControllerProvider).pausa,
      );
      if (!mounted) return;
      // CA-6: quién firma aquí lo decide el servidor **por punto de entrega**, y ese dato pudo cambiar desde que
      // se abrió la pantalla. El rechazo no es transitorio —reintentar manda exactamente lo mismo—, así que el
      // envío se descarta, el interruptor se pone como el servidor dice y se vuelve a firmar aquí, sin perder lo
      // escrito ni salir de la pantalla. Mientras `lugar_tiene_inspector` no llegue al contrato (S7-HU-22.1),
      // esta corrección es lo único con lo que la app se entera.
      if (_caminoDelPunto(desenlace) case final camino?) {
        await ref.read(colaRepositorioProvider).eliminar(jobId);
        await sync.refrescar();
        if (!mounted) return;
        final sinSalida = _puntoYaCorregido && camino != _recibeExterno;
        if (!sinSalida) {
          setState(() {
            _recibeExterno = camino;
            _puntoYaCorregido = true;
          });
        }
        await showDialog<void>(
          context: context,
          builder: (contexto) => AlertDialog(
            title: Text(sinSalida ? 'Esta entrega no la puedes cerrar' : 'Cambió quién recibe aquí'),
            content: Text(switch ((sinSalida, camino)) {
              (true, _) =>
                'Este punto tiene inspector asignado y el viaje no tiene inspector, así que no hay a quién poner '
                    'como quien recibe. La entrega la tiene que reclamar el inspector del punto: llámalo antes de '
                    'seguir. Lo que escribiste se queda aquí.',
              (false, true) =>
                'El servidor dice que este punto no tiene inspector: la entrega la firma quien esté allí. Escribe '
                    'su nombre y su cédula y pídele la firma. La tuya se conserva.',
              (false, false) =>
                'El servidor dice que este punto sí tiene inspector: la entrega la firma él, no una persona '
                    'externa. Apagué «Recibe una persona del punto»; lo que escribiste no se borra, solo deja de '
                    'enviarse. Tu firma se conserva.',
            }),
            actions: [TextButton(onPressed: () => Navigator.of(contexto).pop(), child: const Text('Entendido'))],
          ),
        );
        return;
      }
      final (titulo, mensaje) = switch (desenlace.fase) {
        // Con la firma de quien recibe no falta nadie más: las dos firmas viajaron juntas y la entrega cierra.
        FaseEnvio.registrado when conExterno => (
            'Entrega cerrada',
            'Recibió ${_nombreRecibe.text.trim()}. Quedó registrada para ${_deLaUnidad(_punto.porEntregar.length)} del punto.',
          ),
        FaseEnvio.registrado => (
            'Entrega registrada',
            'Quedó registrada para ${_deLaUnidad(_punto.porEntregar.length)} del punto.',
          ),
        FaseEnvio.rechazado => (
            'No se pudo enviar',
            'El servidor rechazó la entrega${desenlace.motivo == null ? '' : ': ${desenlace.motivo}'}. '
                'Quedó en Inspecciones › Cola para reintentarla o descartarla.',
          ),
        // Falló por algo pasajero: lo que hace falta saber es por qué y cuándo se vuelve a intentar solo.
        FaseEnvio.enEspera => (
            'Entrega en espera',
            'No se pudo enviar${desenlace.motivo == null ? '' : ': ${desenlace.motivo}'}. '
                'Se vuelve a intentar sola ${enPalabras(desenlace.espera!)}.',
          ),
        FaseEnvio.enCola => ('Entrega guardada', 'Quedó en cola; se enviará cuando la señal sea estable.'),
      };
      await showDialog<void>(
        context: context,
        builder: (contexto) => AlertDialog(
          title: Text(titulo),
          content: Text(mensaje),
          actions: [TextButton(onPressed: () => Navigator.of(contexto).pop(), child: const Text('OK'))],
        ),
      );
      if (mounted) Navigator.of(context).pop();
    } catch (e) {
      if (mounted) {
        await showDialog<void>(
          context: context,
          builder: (contexto) => AlertDialog(
            title: const Text('No se pudo guardar'),
            content: Text(mensajeDeError(e)),
            actions: [TextButton(onPressed: () => Navigator.of(contexto).pop(), child: const Text('OK'))],
          ),
        );
      }
    } finally {
      if (mounted) setState(() => _guardando = false);
    }
  }

  /// Cuál es el camino bueno según el rechazo: `true` pedir los datos de quien recibe en el punto, `false`
  /// firmar entre las partes del viaje. Null si no hubo rechazo o si fue por otra cosa.
  ///
  /// El servidor decide por punto (`LugarTieneInspectoresAsync`), que es un dato distinto del inspector del
  /// viaje, y su 400 no trae un código propio: hay que reconocerlo por el motivo. Están los dos mensajes de
  /// `clsEntregasService` porque el rechazo puede caer de cualquiera de los dos lados: mandar los datos del
  /// externo donde hay inspector, o no mandarlos donde no lo hay.
  bool? _caminoDelPunto(DesenlaceEnvio desenlace) {
    if (desenlace.fase != FaseEnvio.rechazado) return null;
    final motivo = desenlace.motivo?.toLowerCase();
    if (motivo == null) return null;
    if (motivo.contains('tiene inspector asignado')) return false;
    if (motivo.contains('indique quién recibe')) return true;
    return null;
  }

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context).textTheme;
    final sinInspeccion = _punto.sinInspeccion;
    final sinInspector = widget.viajeSinInspector;
    final externo = _recibeExterno;
    return Scaffold(
      appBar: AppBar(title: Text('Entrega · ${_punto.lugar}')),
      body: SafeArea(
        child: ListView(
          padding: const EdgeInsets.all(Espacio.lg),
          children: [
            Text(
              'Aquí entregas ${_punto.porEntregar.length == 1 ? 'esta unidad' : 'estas ${_punto.porEntregar.length} unidades'}',
              style: tema.titleMedium,
            ),
            const SizedBox(height: Espacio.sm),
            Tarjeta(
              child: Column(children: [for (final u in _punto.porEntregar) _FilaUnidad(u)]),
            ),
            if (sinInspeccion.isNotEmpty) ...[
              const SizedBox(height: Espacio.md),
              Semantics(
                liveRegion: true,
                child: Text(
                  '${sinInspeccion.length == 1 ? 'Falta 1 unidad' : 'Faltan ${sinInspeccion.length} unidades'} por descargar. '
                      'Aún no tienen la inspección de llegada: ${sinInspeccion.map((u) => u.vin).join(', ')}.',
                  style: tema.bodySmall?.copyWith(color: Colores.peligro),
                ),
              ),
            ],
            const SizedBox(height: Espacio.lg),
            LienzoFirma(etiqueta: 'Tu firma', onCambio: (p) => setState(() => _firma = p)),
            const SizedBox(height: Espacio.sm),
            Text(
              externo && !_unaSolaFirma
                  ? 'Firmas tú y firma quien recibe, aquí mismo. Las firmas quedan en ${_deLaUnidad(_punto.porEntregar.length)}.'
                  : 'Firmas tú desde tu sesión; la otra parte firma desde la suya. La firma queda en ${_deLaUnidad(_punto.porEntregar.length)}.',
              style: tema.bodySmall,
            ),
            const SizedBox(height: Espacio.lg),
            if (!_unaSolaFirma) ...[
              // Quién está del otro lado lo sabe quien está allí: el contrato no dice si el punto tiene inspector
              // asignado. Si se marca por error, el servidor lo rechaza nombrando los tres campos; y si se deja
              // sin marcar donde hacía falta, la entrega se queda esperando una firma que no va a llegar.
              SwitchListTile(
                value: _recibeExterno,
                onChanged: _guardando ? null : (v) => setState(() => _recibeExterno = v),
                title: const Text('Recibe una persona del punto'),
                subtitle: Text(
                  sinInspector
                      ? 'Este viaje no tiene inspector, así que lo normal es que reciba quien esté en el punto.'
                      : 'Alguien que no es usuario de la app: firma aquí, con su nombre y su cédula.',
                  style: tema.bodySmall,
                ),
                contentPadding: EdgeInsets.zero,
              ),
              if (externo) ...[
                const SizedBox(height: Espacio.sm),
                TextField(
                  controller: _nombreRecibe,
                  onChanged: (_) => setState(() {}),
                  maxLength: nombreExternoMax,
                  textCapitalization: TextCapitalization.words,
                  decoration: const InputDecoration(labelText: 'Nombre de quien recibe', counterText: ''),
                ),
                const SizedBox(height: Espacio.sm),
                TextField(
                  controller: _cedulaRecibe,
                  onChanged: (_) => setState(() {}),
                  maxLength: cedulaExternaMax,
                  // No es solo números: el servidor admite puntos, guiones y espacios, y hay documentos que los
                  // llevan. El teclado se abre en números, que es lo normal, pero deja escribir el resto.
                  keyboardType: TextInputType.text,
                  decoration: const InputDecoration(labelText: 'Cédula de quien recibe', counterText: ''),
                ),
                const SizedBox(height: Espacio.md),
                LienzoFirma(etiqueta: 'Firma de quien recibe', onCambio: (p) => setState(() => _firmaRecibe = p)),
                const SizedBox(height: Espacio.sm),
                Text(
                  'Con las dos firmas la entrega queda cerrada: no falta nadie más. Su nombre y su cédula quedan '
                  'registrados con la entrega.',
                  style: tema.bodySmall,
                ),
              ],
              const SizedBox(height: Espacio.lg),
              FilledButton(
                onPressed: _puedeEnviar(externo: externo) ? () => _registrar(externo: externo) : null,
                child: _guardando
                    ? const SizedBox(width: 22, height: 22, child: CircularProgressIndicator(strokeWidth: 2.5, color: Colors.white))
                    : Text(externo ? 'Registrar la entrega' : 'Registrar mi firma'),
              ),
              if (_motivoSinFirmar(externo: externo) case final motivo?) _MotivoDelBoton(motivo),
              const SizedBox(height: Espacio.sm),
              TextButton(
                onPressed: _guardando ? null : () => setState(() => _unaSolaFirma = true),
                child: const Text('La otra parte no está: cerrar con una sola firma'),
              ),
            ] else ...[
              TextField(
                controller: _motivo,
                onChanged: (_) => setState(() {}),
                minLines: 2,
                maxLines: 4,
                maxLength: motivoUnaFirmaMax,
                textCapitalization: TextCapitalization.sentences,
                decoration: const InputDecoration(
                  labelText: 'Motivo',
                  hintText: 'Por qué no firma la otra parte',
                ),
              ),
              Semantics(
                liveRegion: true,
                child: Text(
                  'Quedará registrado como novedad para el coordinador.',
                  style: tema.bodySmall?.copyWith(color: Colores.advertencia),
                ),
              ),
              const SizedBox(height: Espacio.md),
              FilledButton(
                onPressed: _puedeEnviar(externo: externo) ? () => _registrar(externo: externo) : null,
                child: _guardando
                    ? const SizedBox(width: 22, height: 22, child: CircularProgressIndicator(strokeWidth: 2.5, color: Colors.white))
                    : const Text('Cerrar con una sola firma'),
              ),
              if (_motivoSinFirmar(externo: externo) case final motivo?) _MotivoDelBoton(motivo),
              const SizedBox(height: Espacio.sm),
              TextButton(
                onPressed: _guardando ? null : () => setState(() => _unaSolaFirma = false),
                child: const Text('Volver: firman las dos partes'),
              ),
            ],
          ],
        ),
      ),
    );
  }
}

/// Por qué el botón de arriba está gris. Va pegado a él: el aviso del principio de la pantalla ya no se ve
/// cuando se ha bajado hasta la firma.
class _MotivoDelBoton extends StatelessWidget {
  const _MotivoDelBoton(this.motivo);

  final String motivo;

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.only(top: Espacio.xs),
        child: Semantics(
          liveRegion: true,
          child: Text(
            motivo,
            textAlign: TextAlign.center,
            style: Theme.of(context).textTheme.bodySmall?.copyWith(color: Colores.advertencia),
          ),
        ),
      );
}
