import 'dart:async';
import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:uuid/uuid.dart';

import '../../../app/proveedores.dart';
import '../../../core/dispositivo/servicios.dart';
import '../../../core/errores/error_api.dart';
import '../../../core/http/urls_api.dart';
import '../../../core/tema/tema.dart';
import '../../../core/ui/foto_evidencia.dart';
import '../../../core/ui/widgets.dart';
import '../../auth/presentation/auth_controller.dart';
import '../../catalogos/domain/catalogos.dart';
import '../../catalogos/presentation/catalogos_controller.dart';
import '../../custodia/presentation/firma_ninera_page.dart';
import '../../custodia/presentation/lienzo_firma.dart';
import '../../inspecciones/domain/foto_local.dart';
import '../../inspecciones/domain/novedad_pendiente.dart';
import '../../inspecciones/presentation/camara_page.dart';
import '../../inspecciones/presentation/hoja_sin_inspector.dart';
import '../../inspecciones/presentation/revision_novedad_page.dart';
import '../../inspecciones/presentation/sellado.dart';
import '../../inspecciones/providers.dart';
import '../../sync/domain/desenlace_envio.dart';
import '../../sync/domain/job.dart';
import '../../sync/presentation/sync_controller.dart';
import '../../sync/providers.dart';
import '../../vehiculos/presentation/vin_page.dart' show VinPage;
import '../domain/entrega_punto.dart';
import '../domain/espera_del_punto.dart';
import '../providers.dart';
import 'fijar_ubicacion.dart';
import 'llegada_parada.dart';
import 'mis_viajes_controller.dart';

/// Lo que admite el servidor en el texto del motivo de un cierre con una sola firma.
const motivoUnaFirmaMax = 300;

/// Lo que admite el servidor en el nombre y la cédula de quien recibe sin ser usuario, y del testigo. Los mínimos
/// también son suyos: comprobarlos aquí evita firmar, encolar y recibir un 400 con el carro ya entregado.
const nombreExternoMin = 3;
const nombreExternoMax = 150;
const cedulaExternaMax = 30;

/// Cómo se toma una foto del cierre con una sola firma: la cámara y el sello del momento. Las pruebas la reemplazan.
typedef TomarFotoDeCierre = Future<FotoLocal?> Function(BuildContext context, WidgetRef ref);

final tomarFotoDeCierreProvider = Provider<TomarFotoDeCierre>((ref) => _tomarConCamara);

Future<FotoLocal?> _tomarConCamara(BuildContext context, WidgetRef ref) async {
  final temporal = await Navigator.of(context).push<String>(
    MaterialPageRoute(builder: (_) => const CamaraPage(titulo: 'Foto del cierre')),
  );
  if (temporal == null || !context.mounted) return null;
  return sellarFoto(ref, temporal);
}

/// Lo que este teléfono tiene en la cola de una parada: la activación de «No hay inspector» que espera señal, con la
/// que se cuenta el reloj (S8-HU-05.2 CA-3), y la entrega que el servidor rechazó después, que se explica aquí y en
/// «Cola» (CA-6).
///
/// `activacionRechazada` es la activación que el servidor rechazó después: el reloj desaparece y la pantalla dice por
/// qué.
typedef EnviosDeLaParada = ({
  SinInspectorParadaJob? activacion,
  SinInspectorParadaJob? activacionRechazada,
  EntregaPuntoJob? rechazada,
});

const EnviosDeLaParada sinEnvios = (activacion: null, activacionRechazada: null, rechazada: null);

/// Se vuelve a leer cada vez que cambian los contadores de la cola: al enviarse la activación, el reloj pasa a ser el
/// del servidor sin que la pantalla haga nada.
final enviosDeLaParadaProvider =
    FutureProvider.autoDispose.family<EnviosDeLaParada, ({String viajeId, String lugarId})>((ref, parada) async {
  ref.watch(syncControllerProvider.select((s) => (s.pendientes, s.fallidos, s.retirados, s.sincronizando)));
  final usuarioId = ref.watch(authControllerProvider.select((e) => e.sesion?.usuario.id));
  if (usuarioId == null) return sinEnvios;
  final jobs = await ref.watch(procesadorColaProvider).enElDispositivo();
  bool deLaParada(Job job, String viajeId, String lugarId) =>
      job.esDe(usuarioId) && viajeId == parada.viajeId && lugarId == parada.lugarId;
  return (
    activacion: [
      for (final j in jobs.whereType<SinInspectorParadaJob>())
        if (deLaParada(j, j.viajeId, j.lugarId) && !j.fallido) j,
    ].lastOrNull,
    activacionRechazada: [
      for (final j in jobs.whereType<SinInspectorParadaJob>())
        if (deLaParada(j, j.viajeId, j.lugarId) && j.fallido) j,
    ].lastOrNull,
    rechazada: [
      for (final j in jobs.whereType<EntregaPuntoJob>())
        if (deLaParada(j, j.viajeId, j.lugarId) && j.fallido && !j.retirado) j,
    ].lastOrNull,
  );
});

/// S8-HU-05.2 CA-3: cuando la activación de «No hay inspector» sale de la cola —sola, en segundo plano—, la parada
/// que se ve quedó vieja: no trae la espera. Se vuelve a pedir, para que el reloj pase a ser el del servidor. La usan
/// la pantalla de la parada y la ficha del carro.
void refrescarAlSalirLaActivacion(WidgetRef ref, ({String viajeId, String lugarId}) parada) {
  ref.listen(enviosDeLaParadaProvider(parada), (antes, ahora) {
    if (antes?.value?.activacion != null && ahora.hasValue && ahora.value?.activacion == null) {
      ref.invalidate(entregaDeParadaProvider(parada));
    }
  });
}

/// La hora y los minutos, en la hora del teléfono: «08:05».
String horaCorta(DateTime momento) {
  final local = momento.toLocal();
  return '${local.hour.toString().padLeft(2, '0')}:${local.minute.toString().padLeft(2, '0')}';
}

/// «1 carro» o «3 carros».
String _carros(int n) => n == 1 ? '1 carro' : '$n carros';

/// «el carro» o «los 3 carros», según cuántos se entreguen en la parada.
String _deLosCarros(int n) => n == 1 ? 'el carro' : 'los $n carros';

/// Cómo va la parada, en la insignia de la lista (S8-HU-04.2 CA-8, C24).
String etiquetaDeParada(EntregaPunto punto) {
  final rechazados = punto.rechazadas.length;
  final enRechazo = rechazados == 1 ? '1 carro rechazado' : '$rechazados carros rechazados';
  return switch (punto.estado) {
    EstadoParada.pendiente when punto.receptor == ReceptorParada.sinReceptor => 'Sin receptor',
    EstadoParada.pendiente => 'Pendiente',
    EstadoParada.entregada => 'Entregada',
    EstadoParada.entregadaConRechazos => 'Entregada · $enRechazo',
    EstadoParada.firmadaTodosRechazados => 'Firmada · $enRechazo',
  };
}

/// Lo que dice el botón de la firma de la parada (CA-4): cuántos se entregan y cuántos se rechazan. Si se rechazan
/// todos, la parada igual se firma como constancia (C24).
String textoDelBotonDeEntrega({required int carros, required int rechazados}) {
  final entregados = carros - rechazados;
  if (rechazados == 0) return carros == 1 ? 'Entregar el carro' : 'Entregar los $carros carros';
  final enRechazo = rechazados == 1 ? '1 rechazado' : '$rechazados rechazados';
  if (entregados == 0) {
    return 'Firmar: ${rechazados == 1 ? '1 carro rechazado' : '$rechazados carros rechazados'}, ninguno entregado';
  }
  return 'Entregar ${_carros(entregados)} · $enRechazo';
}

/// El botón que abre la entrega de una parada. Lo usan el viaje y «Pendientes» del inspector, que solo lo muestran
/// en la parada que tomó (S8-HU-04.2 CA-1).
class BotonEntregarEnParada extends StatelessWidget {
  const BotonEntregarEnParada({super.key, required this.viajeId, required this.lugarId, this.lugar});

  final String viajeId;
  final String lugarId;
  final String? lugar;

  @override
  Widget build(BuildContext context) => FilledButton(
        onPressed: () async {
          final contenedor = ProviderScope.containerOf(context, listen: false);
          await Navigator.of(context).push<void>(
            MaterialPageRoute(builder: (_) => EntregaParadaPage(viajeId: viajeId, lugarId: lugarId)),
          );
          // Lo que se firmó cambia lo que queda por tomar y lo que hay en el viaje.
          contenedor.invalidate(pendientesProvider);
          await contenedor.read(misViajesControllerProvider.notifier).cargar();
        },
        child: Text(lugar == null || lugar!.isEmpty ? 'Entregar en esta parada' : 'Entregar en esta parada · $lugar'),
      );
}

/// Las paradas de un viaje, con el estado de cada una (S8-HU-04.2 CA-8). Es la entrada del conductor: en cada
/// parada marca «Llegué» y, ya llegado, entrega.
class EntregasViajePage extends ConsumerWidget {
  const EntregasViajePage({super.key, required this.viajeId});

  final String viajeId;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final tema = Theme.of(context).textTheme;
    final entregas = ref.watch(entregasDelViajeProvider(viajeId));
    // Cuando la cola entrega algo —un «Llegué» que esperaba señal, por ejemplo—, lo que se ve de las paradas quedó
    // viejo: la parada seguiría pidiendo «Llegué» y no ofrecería entregar (S8-HU-04.2 CA-1 y CA-9).
    ref.listen(syncControllerProvider.select((s) => s.pendientes), (antes, ahora) {
      if (antes != null && ahora < antes) ref.invalidate(entregasDelViajeProvider(viajeId));
    });
    return Scaffold(
      appBar: AppBar(
        title: const Text('Paradas del viaje'),
        actions: [
          IconButton(
            tooltip: 'Actualizar las paradas',
            onPressed: () => ref.invalidate(entregasDelViajeProvider(viajeId)),
            icon: const Icon(Icons.refresh),
          ),
        ],
      ),
      body: SafeArea(
        child: entregas.when(
          loading: () => const EstadoCargando(),
          error: (error, _) => EstadoMensaje(
            icono: Icons.warning_amber_rounded,
            colorIcono: Colores.peligro,
            titulo: 'No se pudieron cargar las paradas',
            descripcion: mensajeDeError(error, 'Las paradas se consultan con señal.'),
            accion: FilledButton(
              onPressed: () => ref.invalidate(entregasDelViajeProvider(viajeId)),
              child: const Text('Reintentar'),
            ),
          ),
          data: (datos) => datos.puntos.isEmpty && datos.unidadesSinDestino == 0
              ? const EstadoMensaje(
                  icono: Icons.local_shipping_outlined,
                  titulo: 'Este viaje no tiene paradas',
                  descripcion: 'Las unidades no tienen un destino asignado todavía.',
                )
              : RefreshIndicator(
                  onRefresh: () async => ref.invalidate(entregasDelViajeProvider(viajeId)),
                  child: ListView(
                    padding: const EdgeInsets.all(Espacio.lg),
                    children: [
                      for (final punto in datos.puntos) ...[
                        _TarjetaParada(
                          punto: punto,
                          alEntregar: () async {
                            await Navigator.of(context).push<void>(
                              MaterialPageRoute(
                                builder: (_) => EntregaParadaPage(viajeId: punto.viajeId, lugarId: punto.lugarId),
                              ),
                            );
                            ref.invalidate(entregasDelViajeProvider(viajeId));
                          },
                        ),
                        const SizedBox(height: Espacio.md),
                      ],
                      // CA-8: los carros sin destino no son de ninguna parada; sin esto no se ven en ninguna parte.
                      if (datos.unidadesSinDestino > 0) ...[
                        Tarjeta(
                          child: Text(
                            '${datos.unidadesSinDestino == 1 ? '1 carro no tiene' : '${datos.unidadesSinDestino} carros no tienen'} '
                            'destino. Pídele al coordinador que le asigne un destino.',
                            style: tema.bodyMedium?.copyWith(color: Colores.advertencia),
                          ),
                        ),
                        const SizedBox(height: Espacio.md),
                      ],
                      Text(
                        'En cada parada se firma una sola vez para todos los carros que se entregan allí.',
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

class _TarjetaParada extends ConsumerWidget {
  const _TarjetaParada({required this.punto, required this.alEntregar});

  final EntregaPunto punto;
  final VoidCallback alEntregar;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final esConductor = ref.watch(authControllerProvider).sesion?.usuario.rol == 'conductor';
    final tema = Theme.of(context).textTheme;
    final lugar = [punto.lugar, punto.ciudad].whereType<String>().where((p) => p.isNotEmpty).join(' · ');
    final porEntregar = punto.porEntregar;
    return Tarjeta(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              Expanded(child: Text(lugar, style: tema.titleSmall)),
              Insignia(
                etiquetaDeParada(punto),
                tono: switch (punto.estado) {
                  EstadoParada.pendiente when punto.receptor == ReceptorParada.sinReceptor => TonoInsignia.advertencia,
                  EstadoParada.pendiente => TonoInsignia.info,
                  EstadoParada.entregada => TonoInsignia.exito,
                  _ => TonoInsignia.advertencia,
                },
              ),
            ],
          ),
          const SizedBox(height: Espacio.xs),
          Text(
            porEntregar.isEmpty
                ? 'Ya no quedan carros por entregar en esta parada.'
                : 'Aquí entregas ${porEntregar.length == 1 ? 'este carro' : 'estos ${porEntregar.length} carros'}',
            style: tema.bodyMedium?.copyWith(color: Colores.textoPrimario),
          ),
          const SizedBox(height: Espacio.sm),
          for (final unidad in punto.unidades) _FilaUnidad(unidad),
          if (esConductor) ...[
            const SizedBox(height: Espacio.sm),
            FijarUbicacionLugar(lugarId: punto.lugarId),
          ],
          // CA-1 y CA-9: el conductor entrega en la parada donde ya marcó «Llegué»; antes, lo que tiene es «Llegué».
          if (esConductor && porEntregar.isNotEmpty) ...[
            const SizedBox(height: Espacio.sm),
            if (punto.conductorLlego)
              FilledButton(onPressed: alEntregar, child: const Text('Entregar en esta parada'))
            else
              LlegueAParada(viajeId: punto.viajeId, lugar: punto.lugar, vines: [for (final u in porEntregar) u.vin]),
          ],
        ],
      ),
    );
  }
}

class _FilaUnidad extends StatelessWidget {
  const _FilaUnidad(this.unidad);

  final UnidadEntrega unidad;

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context).textTheme;
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
                // S8-HU-05: lo entregado con el plan B lo dice.
                EstadoEntrega.cerrada => Insignia(
                    unidad.sinInspectorDelPunto ? 'Entregado sin inspector del punto' : 'Entregado',
                    tono: TonoInsignia.exito,
                  ),
                EstadoEntrega.pendiente => const Insignia('Por firmar', tono: TonoInsignia.info),
                EstadoEntrega.sinInspeccion => const Insignia('Falta la revisión de llegada', tono: TonoInsignia.advertencia),
                EstadoEntrega.rechazado => const Insignia('Rechazado', tono: TonoInsignia.peligro),
              },
            ],
          ),
          // CA-8: el carro rechazado sigue en el camión, a cargo del conductor, hasta que el coordinador le dé otro
          // destino. Decirlo aquí es lo que evita que alguien lo baje en la parada.
          if (unidad.estado == EstadoEntrega.rechazado)
            Text(
              'Rechazado por novedad: espera el nuevo destino del coordinador',
              style: tema.bodySmall?.copyWith(color: Colores.peligro),
            ),
          // Quién recibió, cuando no fue un usuario del sistema. Es lo que hay que poder mirar después: una
          // entrega cerrada sin un nombre al lado no dice a quién se le entregó el carro.
          if (unidad.recibioExterno case final nombre?) Text('Recibió: $nombre', style: tema.bodySmall),
        ],
      ),
    );
  }
}

/// «Entregar en esta parada»: carga la parada y abre su firma. Se puede actualizar sin perder lo escrito, que es
/// como el inspector ve activarse «Firmar» cuando el conductor marca «Llegué» (S8-HU-04.2 CA-2).
class EntregaParadaPage extends ConsumerWidget {
  const EntregaParadaPage({super.key, required this.viajeId, required this.lugarId});

  final String viajeId;
  final String lugarId;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final parada = (viajeId: viajeId, lugarId: lugarId);
    final valor = ref.watch(entregaDeParadaProvider(parada));
    // Las revisiones de llegada también: una que se cerró después de abrir la pantalla deja de bloquear la firma.
    void actualizar() {
      ref.invalidate(entregaDeParadaProvider(parada));
      ref.invalidate(revisionDeLlegadaProvider);
    }
    if (valor.value case final punto?) {
      return FirmaEntregaPuntoPage(
        punto: punto,
        alActualizar: actualizar,
        actualizando: valor.isLoading,
        errorAlActualizar: valor.hasError ? mensajeDeError(valor.error!, 'No se pudo actualizar la parada.') : null,
      );
    }
    return Scaffold(
      appBar: AppBar(title: const Text('Entregar en esta parada')),
      body: SafeArea(
        child: valor.hasError
            ? EstadoMensaje(
                icono: Icons.warning_amber_rounded,
                colorIcono: Colores.peligro,
                titulo: 'No se pudo cargar la parada',
                descripcion: mensajeDeError(valor.error!, 'La parada se consulta con señal.'),
                accion: FilledButton(onPressed: actualizar, child: const Text('Reintentar')),
              )
            : const EstadoCargando(),
      ),
    );
  }
}

/// Qué parte de la firma de la parada le toca a quien mira.
enum _Papel {
  /// El conductor del viaje. Donde no hay inspectores, además registra a la persona del lugar.
  entrega,

  /// El inspector que tomó la entrega de esta parada.
  recibe,

  /// Cualquier otro: no firma ninguna parada (CA-1).
  ninguno,
}

/// Cómo cierra el conductor sin inspector del punto, cuando el plan B ya se habilitó (S8-HU-05.2 CA-4).
enum _PlanB {
  /// «Recibe una persona del lugar»: su nombre, su cédula y su firma, como en una parada sin inspectores.
  personaDelLugar,

  /// «Cerrar con mi firma»: el cierre con una sola firma con el motivo «Sin inspector del punto».
  miFirma,
}

/// La firma de la entrega de una parada (S8-HU-04.2): una para todos sus carros, con los rechazos por novedad y las
/// reservas de quien recibe, o el cierre con una sola firma con su motivo y su evidencia. Quién recibe lo dice el
/// servidor y la pantalla no lo cambia: no hay interruptor (C9 y C15).
///
/// Se encola con la hora y el GPS del momento: sin señal se envía al volver, y repetirla no duplica nada.
class FirmaEntregaPuntoPage extends ConsumerStatefulWidget {
  const FirmaEntregaPuntoPage({
    super.key,
    required this.punto,
    this.alActualizar,
    this.actualizando = false,
    this.errorAlActualizar,
  });

  final EntregaPunto punto;

  /// Vuelve a pedir la parada al servidor. Null si quien abre la pantalla no la sabe pedir.
  final VoidCallback? alActualizar;
  final bool actualizando;

  /// Lo que salió mal en la última actualización; la parada que se ve es la anterior.
  final String? errorAlActualizar;

  @override
  ConsumerState<FirmaEntregaPuntoPage> createState() => _EstadoFirmaEntregaPunto();
}

class _EstadoFirmaEntregaPunto extends ConsumerState<FirmaEntregaPuntoPage> {
  String? _firma;
  bool _guardando = false;

  /// La persona del lugar, en una parada sin inspectores.
  final _nombreRecibe = TextEditingController();
  final _cedulaRecibe = TextEditingController();
  String? _firmaRecibe;

  /// Lo que quien recibe decide de cada carro: lo recibe con reservas o lo rechaza por un daño (CA-4 y CA-5).
  final Map<String, String> _reservas = {};
  final Map<String, DanoDeLlegada> _rechazos = {};

  /// El cierre con una sola firma (CA-6).
  bool _unaSolaFirma = false;
  String? _motivoCodigo;
  final _motivoTexto = TextEditingController();
  final _testigoNombre = TextEditingController();
  final _testigoCedula = TextEditingController();
  final List<FotoLocal> _fotos = [];

  /// S8-HU-05.2 CA-4: cómo cierra el conductor sin inspector del punto. Null mientras no elija.
  _PlanB? _planB;

  /// Se está guardando «No hay inspector».
  bool _activando = false;

  /// Repinta la espera. Solo mueve lo que se ve: el reloj se calcula con la hora de activación guardada (CA-3).
  Timer? _repintar;

  /// La última activación que se vio en la cola. Cuando sale, la parada se vuelve a pedir; mientras llega la copia
  /// nueva —o si no hay señal para pedirla— el reloj sigue con ella (CA-3).
  SinInspectorParadaJob? _activacionVista;

  /// Cuándo se le preguntó al servidor por el aviso que su espera ya debía haber dado (CA-3).
  DateTime? _avisoPreguntado;

  @override
  void initState() {
    super.initState();
    _repintar = Timer.periodic(const Duration(seconds: 15), (_) {
      if (!mounted) return;
      final reloj = _relojEspera;
      if (reloj == null) return;
      setState(() {});
      // Con la espera del servidor, el coordinador fue avisado cuando el servidor lo dice: pasada la hora, se le
      // pregunta, sin insistir más de una vez por minuto.
      final ahora = DateTime.now();
      final preguntado = _avisoPreguntado;
      if (reloj.avisoPorConfirmar(_ahora) &&
          !widget.actualizando &&
          (preguntado == null || ahora.difference(preguntado) >= const Duration(minutes: 1))) {
        _avisoPreguntado = ahora;
        widget.alActualizar?.call();
      }
    });
  }

  @override
  void didUpdateWidget(FirmaEntregaPuntoPage antes) {
    super.didUpdateWidget(antes);
    // Llegó la parada del servidor: ella dice cómo va la espera, y la activación que ya salió deja de contar.
    if (!identical(widget.punto, antes.punto) && !widget.punto.copiaSinSenal) _activacionVista = null;
    // Si al actualizar la parada el plan B ya no vale (un inspector tomó la entrega), lo elegido se olvida —también
    // los rechazos y las reservas que se decidieron con la persona del lugar— y la pantalla vuelve a la firma con
    // él (CA-5).
    if (_planB != null && !_planBHabilitado) _olvidarElPlanB();
  }

  /// CA-4 y CA-11: lo que se decidió para cerrar sin inspector del punto ya no vale.
  void _olvidarElPlanB() {
    if (_planB == _PlanB.miFirma) {
      _unaSolaFirma = false;
      _motivoCodigo = null;
    }
    _planB = null;
    _rechazos.clear();
    _reservas.clear();
  }

  @override
  void dispose() {
    _repintar?.cancel();
    _nombreRecibe.dispose();
    _cedulaRecibe.dispose();
    _motivoTexto.dispose();
    _testigoNombre.dispose();
    _testigoCedula.dispose();
    super.dispose();
  }

  EntregaPunto get _punto => widget.punto;

  /// Se lee, no se observa: lo usan también el envío y los motivos del botón. El `build` observa la sesión.
  ///
  /// El inspector recibe solo si el servidor dice que la parada la tomó él (C11): el rol solo no alcanza. El servidor
  /// manda el nombre de quien la tomó y no su id, así que se compara el nombre; si no lo manda (un servidor de antes),
  /// se le deja firmar y el servidor decide.
  _Papel get _papel {
    final usuario = ref.read(authControllerProvider).sesion?.usuario;
    final tomadaPor = _punto.receptorNombre;
    return switch (usuario?.rol) {
      'conductor' => _Papel.entrega,
      'inspector' when _punto.receptor == ReceptorParada.inspectorDelPunto && (tomadaPor == null || tomadaPor == usuario?.nombre) =>
        _Papel.recibe,
      _ => _Papel.ninguno,
    };
  }

  /// Por qué quien mira no firma esta parada.
  String get _porQueNoFirma => switch (_punto.receptor) {
        _ when ref.read(authControllerProvider).sesion?.usuario.rol != 'inspector' =>
          'La parada la firman el conductor del viaje y quien recibe en ella.',
        ReceptorParada.sinReceptor => 'Toma la entrega de esta parada en «Pendientes» antes de firmar.',
        ReceptorParada.personaDelLugar => 'Esta parada no tiene inspectores: recibe la persona del lugar.',
        ReceptorParada.inspectorDelPunto =>
          'La entrega de esta parada la tomó ${_punto.receptorNombre ?? 'otro inspector'}: firma él.',
      };

  /// Por qué ya no se puede cerrar con una sola firma, o null si se puede (CA-6). El servidor lo rechaza con 409 si la
  /// otra parte ya firmó algún carro de la parada: entonces lo que queda es firmar la parada.
  String? get _porQueNoUnaFirma {
    final carros = _punto.porEntregar;
    if (_papel == _Papel.entrega && !_enElLugar && carros.any((u) => u.recibeFirmada)) {
      return '${_punto.receptorNombre ?? 'Quien recibe'} ya firmó: la entrega no puede cerrarse con una sola firma. Firma la parada.';
    }
    if (_papel == _Papel.recibe && carros.any((u) => u.entregaFirmada)) {
      return 'El conductor ya firmó: la entrega no puede cerrarse con una sola firma. Firma la parada.';
    }
    return null;
  }

  /// La parada no tiene inspectores, o el conductor cierra el plan B con la persona del lugar: recibe ella, aquí mismo.
  bool get _enElLugar => _punto.receptor == ReceptorParada.personaDelLugar || _conPersonaDelLugar;

  ({String viajeId, String lugarId}) get _parada => (viajeId: _punto.viajeId, lugarId: _punto.lugarId);

  EnviosDeLaParada get _envios => ref.read(enviosDeLaParadaProvider(_parada)).value ?? sinEnvios;

  DateTime get _ahora => ref.read(horaDelTelefonoProvider)();

  /// La espera que corre en esta parada (S8-HU-05.2 CA-3), o null si no hay ninguna.
  RelojEspera? get _relojEspera => switch (_envios.activacion) {
        final enCola? => relojDeLaParada(_punto, activacion: enCola),
        // Recién enviada: mientras la parada no la trae, se sigue contando con ella. Si la rechazaron, no.
        null when _envios.activacionRechazada == null && _activacionVista != null =>
          relojDeLaParada(_punto, activacion: _activacionVista, sinEnviar: false),
        null => relojDeLaParada(_punto),
      };

  /// El conductor está en una parada con inspectores donde nadie ha tomado la entrega.
  bool get _sinQuienReciba => _papel == _Papel.entrega && _punto.receptor == ReceptorParada.sinReceptor;

  /// CA-4: el plan B se habilitó para el conductor: ya puede cerrar sin inspector del punto.
  bool get _planBHabilitado => _sinQuienReciba && (_relojEspera?.planBHabilitado(_ahora) ?? false);

  bool get _conPersonaDelLugar => _planBHabilitado && _planB == _PlanB.personaDelLugar;

  bool get _conMiFirma => _planBHabilitado && _planB == _PlanB.miFirma;

  /// Quien decide qué carros se rechazan y cuáles se reciben con reservas: el inspector que tomó la parada o, donde
  /// no hay inspectores, el conductor a pedido de la persona del lugar.
  bool get _decideQuienRecibe => _papel == _Papel.recibe || (_papel == _Papel.entrega && _enElLugar);

  /// Ya firmó todos los carros por entregar de la parada: falta la otra parte.
  bool get _yaFirme {
    final carros = _punto.porEntregar;
    if (carros.isEmpty || _enElLugar) return false;
    return switch (_papel) {
      _Papel.entrega => carros.every((u) => u.entregaFirmada),
      _Papel.recibe => carros.every((u) => u.recibeFirmada),
      _Papel.ninguno => false,
    };
  }

  /// Los carros por entregar cuya revisión de llegada no está cerrada: los que no la tienen y, si se pudo
  /// consultar, los que la tienen abierta. Sin señal no se inventa nada: el servidor lo comprueba al firmar.
  List<UnidadEntrega> _sinRevisionCerrada() => [
        for (final u in _punto.porEntregar)
          if (u.estado == EstadoEntrega.sinInspeccion ||
              (u.inspeccionId != null && ref.read(revisionDeLlegadaProvider(u.inspeccionId!)).value?.cerrada == false))
            u,
      ];

  /// Los rechazos que siguen valiendo: de carros que todavía están por entregar, y solo si quien mira es quien los
  /// decide. Si un inspector tomó la parada mientras el conductor cerraba con la persona del lugar, los que él marcó
  /// ya no van: en una parada con inspector solo los manda el inspector (S8-HU-05.2 CA-11).
  Map<String, DanoDeLlegada> get _rechazosVigentes {
    if (!_decideQuienRecibe) return const {};
    final vines = {for (final u in _punto.porEntregar) u.vin};
    return {for (final e in _rechazos.entries) if (vines.contains(e.key)) e.key: e.value};
  }

  /// Por qué no se puede firmar todavía, o null si sí se puede. Un botón gris sin motivo deja a quien está en la
  /// parada adivinando.
  String? _motivoSinFirmar() {
    final carros = _punto.porEntregar;
    if (_papel == _Papel.ninguno) return _porQueNoFirma;
    if (carros.isEmpty) return 'Ya no quedan carros por entregar en esta parada.';
    // CA-2: el inspector firma después del «Llegué» del conductor. Va antes que las revisiones: sin el «Llegué» el
    // servidor no deja ni crear la revisión de llegada (`sin_llegue`), así que «falta la revisión» sería engañoso.
    if (_papel == _Papel.recibe && !_punto.conductorLlego && !_unaSolaFirma) {
      return 'El conductor aún no marca «Llegué» en esta parada';
    }
    final faltan = _sinRevisionCerrada();
    if (faltan.isNotEmpty) {
      return faltan.length == 1
          ? 'Falta la revisión de llegada del VIN ${faltan.single.vin}: termínala antes de firmar.'
          : 'Falta la revisión de llegada de ${faltan.length} carros: termínalas antes de firmar.';
    }
    // S8-HU-05.2 CA-10 (C19): el inspector que llegó califica antes de firmar los daños que marcó el conductor con el
    // plan B. También antes del cierre con una sola firma: el servidor rechaza los dos con `danos_sin_calificar`.
    if (_papel == _Papel.recibe && _punto.danosPorCalificar.isNotEmpty) {
      final faltan = _punto.danosPorCalificar.length;
      return 'Califica los daños que marcó el conductor: ${faltan == 1 ? 'falta 1' : 'faltan $faltan'}';
    }
    // El servidor también rechaza la firma del conductor con `danos_sin_calificar` mientras el inspector que tomó la
    // parada no califique los daños que él marcó: no se ofrece algo que va a fallar. Solo cuántos, nunca el nivel.
    if (_papel == _Papel.entrega &&
        _punto.receptor == ReceptorParada.inspectorDelPunto &&
        _punto.danosPorCalificar.isNotEmpty) {
      return _faltaQueCalifique;
    }
    // S8-HU-05.2 CA-4: con la espera, ningún cierre sin inspector del punto hasta el plazo.
    if (_sinQuienReciba && !_conPersonaDelLugar && !_conMiFirma) {
      return switch (_relojEspera) {
        final reloj? when !reloj.planBHabilitado(_ahora) => reloj.textoPlanB(_ahora),
        final _? => 'Elige cómo cerrar sin inspector del punto.',
        null => 'Este punto tiene inspector: pídele a un inspector del punto que tome la entrega en su app',
      };
    }
    if (_unaSolaFirma) {
      if (_porQueNoUnaFirma case final porQue?) return porQue;
      if (_motivoCodigo == null) return 'Elige el motivo.';
      if (_motivoCodigo == MotivoUnaFirma.seNegoAFirmar && _fotos.isEmpty) return 'Con este motivo la foto es obligatoria';
      if (_faltaDelTestigo() case final falta?) return falta;
      if (_firma == null && !_yaFirme) return 'Dibuja tu firma arriba.';
      return null;
    }
    if (_firma == null) return 'Dibuja tu firma arriba.';
    // Los tres datos de quien recibe van juntos o no va ninguno: el servidor los exige juntos y rechaza el envío
    // entero. Se reclaman de uno en uno, en el orden en que están en pantalla.
    if (_papel == _Papel.entrega && _enElLugar) {
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

  /// Lo que espera el conductor mientras el inspector que llegó no califica los daños que él marcó (CA-10).
  String get _faltaQueCalifique {
    final faltan = _punto.danosPorCalificar.length;
    return 'El inspector del punto tiene que calificar antes los daños que marcaste: '
        '${faltan == 1 ? 'falta 1' : 'faltan $faltan'}';
  }

  /// El testigo es opcional, pero va completo: con nombre y cédula juntos, como lo exige el servidor.
  String? _faltaDelTestigo() {
    final nombre = _testigoNombre.text.trim();
    final cedula = _testigoCedula.text.trim();
    if (nombre.isEmpty && cedula.isEmpty) return null;
    if (nombre.isEmpty) return 'Escribe el nombre del testigo, o borra su cédula.';
    if (cedula.isEmpty) return 'Escribe la cédula del testigo, o borra su nombre.';
    if (nombre.length < nombreExternoMin) return 'El nombre del testigo es muy corto: escríbelo completo.';
    if (!cedula.contains(RegExp('[0-9]'))) return 'La cédula del testigo tiene que llevar números.';
    return null;
  }

  bool get _puedeEnviar => !_guardando && _motivoSinFirmar() == null;

  Future<void> _reservar(String vin) async {
    final texto = await showDialog<String>(
      context: context,
      builder: (_) => DialogoReserva(vin: vin, actual: _reservas[vin] ?? ''),
    );
    if (texto == null || !mounted) return;
    setState(() => texto.isEmpty ? _reservas.remove(vin) : _reservas[vin] = texto);
  }

  /// CA-4: rechazar exige elegir uno de los daños con foto de la revisión de llegada del carro en esta parada.
  Future<void> _rechazar(String vin, List<DanoDeLlegada> danos) async {
    final catalogos = ref.read(catalogosProvider);
    final elegido = await showModalBottomSheet<DanoDeLlegada>(
      context: context,
      showDragHandle: true,
      isScrollControlled: true,
      builder: (contexto) => SafeArea(
        child: SingleChildScrollView(
          padding: const EdgeInsets.fromLTRB(Espacio.lg, 0, Espacio.lg, Espacio.lg),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Text('Rechazar por novedad', style: Theme.of(contexto).textTheme.titleMedium),
              TextoMono(vin),
              const SizedBox(height: Espacio.sm),
              Text(
                'Elige el daño de su revisión de llegada que lo motiva. El carro sigue en el camión, a cargo del conductor.',
                style: Theme.of(contexto).textTheme.bodySmall,
              ),
              const SizedBox(height: Espacio.sm),
              for (final dano in danos)
                ListTile(
                  contentPadding: EdgeInsets.zero,
                  leading: const Icon(Icons.photo_camera_outlined),
                  title: Text(dano.zona),
                  subtitle: dano.clase == null ? null : Text(catalogos.etiqueta(Catalogo.clasesDano, dano.clase!)),
                  onTap: () => Navigator.of(contexto).pop(dano),
                ),
            ],
          ),
        ),
      ),
    );
    if (elegido == null || !mounted) return;
    setState(() {
      _rechazos[vin] = elegido;
      // Un carro que no se recibe no lleva reservas.
      _reservas.remove(vin);
    });
  }

  Future<void> _tomarFoto() async {
    final foto = await ref.read(tomarFotoDeCierreProvider)(context, ref);
    if (!mounted) return;
    if (foto == null) {
      ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('No se guardó la foto. Inténtalo de nuevo.')));
      return;
    }
    setState(() => _fotos.add(foto));
  }

  Future<void> _registrar() async {
    final usuarioId = ref.read(authControllerProvider).sesion?.usuario.id;
    final papel = _papel;
    if (usuarioId == null || !_puedeEnviar) return;
    setState(() => _guardando = true);
    try {
      // CA-7: la hora y el lugar de AHORA, que es lo que prueba cuándo y dónde se firmó aunque llegue después. Sin
      // GPS se firma igual: el servidor la deja «sin ubicación» y la pantalla lo dice.
      final momento = DateTime.now();
      Posicion? posicion;
      try {
        posicion = await ref.read(servicioUbicacionProvider).obtenerActual();
      } catch (_) {
        posicion = null;
      }
      final unaFirma = _unaSolaFirma;
      final carros = [for (final u in _punto.porEntregar) u.vin];
      final rechazos = unaFirma ? const <String, DanoDeLlegada>{} : _rechazosVigentes;
      final externo = !unaFirma && papel == _Papel.entrega && _enElLugar;
      final testigo = unaFirma && _testigoNombre.text.trim().isNotEmpty;
      final planB = _conPersonaDelLugar || _conMiFirma;
      final jobId = const Uuid().v4();
      await ref.read(colaRepositorioProvider).agregar(EntregaPuntoJob(
            id: jobId,
            etiqueta: 'Entrega ${_punto.lugar}',
            creadoEn: momento.millisecondsSinceEpoch,
            usuarioId: usuarioId,
            viajeId: _punto.viajeId,
            lugarId: _punto.lugarId,
            firma: _firma,
            fechaMomento: isoConZona(momento),
            lat: posicion?.lat,
            lng: posicion?.lng,
            precisionM: posicion?.precision,
            rechazos: [for (final e in rechazos.entries) RechazoCarro(vin: e.key, hallazgoId: e.value.hallazgoId)],
            reservas: unaFirma || !_decideQuienRecibe
                ? const []
                : [
                    for (final e in _reservas.entries)
                      if (carros.contains(e.key) && !rechazos.containsKey(e.key))
                        ReservaCarro(vin: e.key, observaciones: e.value),
                  ],
            recibeNombre: externo ? _nombreRecibe.text.trim() : null,
            recibeCedula: externo ? _cedulaRecibe.text.trim() : null,
            firmaRecibe: externo ? _firmaRecibe : null,
            motivoCodigo: unaFirma ? _motivoCodigo : null,
            motivoTexto: unaFirma && _motivoTexto.text.trim().isNotEmpty ? _motivoTexto.text.trim() : null,
            fotos: unaFirma ? [..._fotos] : const [],
            vines: unaFirma && _fotos.isNotEmpty ? carros : const [],
            testigoNombre: testigo ? _testigoNombre.text.trim() : null,
            testigoCedula: testigo ? _testigoCedula.text.trim() : null,
          ));
      final sync = ref.read(syncControllerProvider.notifier);
      await sync.refrescar();
      await sync.sincronizar();
      final cola = await ref.read(colaRepositorioProvider).listar();
      final enviado = cola.where((j) => j.id == jobId).firstOrNull;
      if (!mounted) return;
      // CA-3: el servidor rechazó la entrega (nadie tomó la parada, el conductor no ha llegado, falta una revisión,
      // el daño elegido no sirve, la otra parte ya firmó…). Reintentar manda lo mismo y recibe lo mismo, así que el
      // envío se descarta, se explica —por su motivo cuando lo trae, no por el texto— y lo escrito, la firma y las
      // fotos se quedan aquí para corregir y volver a firmar. Dejarlo fallido en la cola los atrapaba allí.
      if (enviado != null && enviado.fallido) {
        await ref.read(colaRepositorioProvider).eliminar(jobId);
        await sync.refrescar();
        if (!mounted) return;
        await _explicarMotivo(enviado.codigoError ?? '', enviado.error);
        widget.alActualizar?.call();
        return;
      }
      final desenlace = desenlaceDe(cola, jobId, pausa: ref.read(syncControllerProvider).pausa);
      final entregados = carros.length - rechazos.length;
      final resumen = rechazos.isEmpty
          ? 'Quedó registrada para ${_deLosCarros(carros.length)} de la parada.'
          : 'Quedó registrada: ${_carros(entregados)} ${entregados == 1 ? 'entregado' : 'entregados'} y '
              '${_carros(rechazos.length)} ${rechazos.length == 1 ? 'rechazado' : 'rechazados'}.';
      final (titulo, mensaje) = switch (desenlace.fase) {
        // S8-HU-05.2 CA-4: los carros quedan «Entregada sin inspector del punto».
        FaseEnvio.registrado when planB => (
            'Entrega cerrada sin inspector del punto',
            unaFirma
                ? 'Se entregaron ${_deLosCarros(carros.length)} de la parada con tu firma.'
                : 'Recibió ${_nombreRecibe.text.trim()}. $resumen',
          ),
        FaseEnvio.registrado when unaFirma => (
            'Parada cerrada con una sola firma',
            'Se cerraron ${_deLosCarros(carros.length)} de la parada. El coordinador recibe el aviso con el motivo.',
          ),
        // Con la firma de quien recibe no falta nadie más: las dos firmas viajaron juntas y la entrega cierra.
        FaseEnvio.registrado when externo => ('Entrega cerrada', 'Recibió ${_nombreRecibe.text.trim()}. $resumen'),
        FaseEnvio.registrado => ('Entrega registrada', resumen),
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
        FaseEnvio.enCola => ('Entrega guardada', 'Quedó en cola con la hora de ahora; se enviará cuando la señal sea estable.'),
      };
      await showDialog<void>(
        context: context,
        builder: (contexto) => AlertDialog(
          title: Text(titulo),
          content: Text([
            mensaje,
            if (posicion == null) 'Se registró sin ubicación: el teléfono no dio el GPS al firmar.',
          ].join('\n\n')),
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

  /// Explica un rechazo de la parada por su motivo. Cuando el servidor nombra el carro (la revisión abierta, el
  /// carro sin daño para rechazarlo), se muestra su mensaje: es lo único que dice cuál es. Un rechazo sin motivo
  /// propio (un 400 con lo que falta, un 409 de la transferencia) se muestra con todo lo que dijo el servidor.
  Future<void> _explicarMotivo(String motivo, String? delServidor) {
    final servidor = delServidor?.split('\n').first;
    const seQueda = 'Lo que escribiste se queda aquí.';
    final (titulo, texto) = switch (motivo) {
      MotivoParada.sinReceptor => (
          'Nadie ha tomado la entrega de esta parada',
          'Este punto tiene inspector: pídele a un inspector del punto que tome la entrega en su app. $seQueda',
        ),
      MotivoParada.conductorSinLlegada => (
          'El conductor aún no marca «Llegué»',
          'El conductor aún no marca «Llegué» en esta parada. Cuando lo haga, actualiza y vuelve a firmar. $seQueda',
        ),
      MotivoParada.llegadaAbierta => (
          'Falta la revisión de llegada',
          '${servidor ?? 'Algún carro de la parada no tiene su revisión de llegada cerrada'}. $seQueda',
        ),
      MotivoParada.sinNovedadParaRechazar => (
          'Este carro no se puede rechazar',
          '${servidor ?? 'El carro no tiene un daño con foto que justifique el rechazo'}. $seQueda',
        ),
      // S8-HU-05.2 CA-5: al conductor, un inspector del punto le ganó al plan B: lo que queda es firmar con él.
      MotivoParada.entregaTomada when _papel == _Papel.entrega => (
          'Un inspector del punto tomó la entrega',
          'Un inspector del punto tomó la entrega: firma con él. $seQueda',
        ),
      // CA-4: el plan B todavía no: el servidor dice cuántos minutos faltan.
      MotivoParada.planBNoHabilitado => (
          'Aún no se puede cerrar sin inspector del punto',
          '${servidor ?? 'Todavía no puedes cerrar sin inspector del punto'}. $seQueda',
        ),
      // CA-10: el servidor dice cuántos daños faltan por calificar.
      MotivoParada.danosSinCalificar => (
          'Faltan daños por calificar',
          '${servidor ?? 'Antes de firmar, califica los daños que marcó el conductor en esta parada'}. $seQueda',
        ),
      MotivoParada.entregaCerradaSinInspector => (
          'Esta entrega ya se cerró',
          'Esta entrega ya se cerró sin inspector del punto.',
        ),
      MotivoParada.entregaTomada => (
          'Esta entrega la tomó otro inspector',
          'Solo firma la parada el inspector que tomó su entrega.',
        ),
      MotivoParada.noEsSuPunto => (
          'Esta parada no es de tus puntos',
          'La firman el conductor del viaje y el inspector de la parada que tomó su entrega.',
        ),
      _ => ('El servidor no aceptó la entrega', '${delServidor ?? 'No dijo por qué'}\n\n$seQueda'),
    };
    return showDialog<void>(
      context: context,
      builder: (contexto) => AlertDialog(
        title: Text(titulo),
        content: Text(texto),
        actions: [TextButton(onPressed: () => Navigator.of(contexto).pop(), child: const Text('Entendido'))],
      ),
    );
  }

  /// S8-HU-05.2 CA-2: «No hay inspector». Pide el motivo con la hoja de `motivos-sin-inspector`, toma la hora y el GPS
  /// del celular y va por la cola: sin señal se guarda y se envía sola, y el reloj cuenta desde ahora con los X e Y
  /// de la parada. Reenviarla no crea otra espera.
  Future<void> _activarSinInspector() async {
    final usuarioId = ref.read(authControllerProvider).sesion?.usuario.id;
    if (usuarioId == null || _activando) return;
    final motivo = await pedirMotivoSinInspector(
      context,
      motivos: ref.read(catalogosProvider).seleccionables(Catalogo.motivosSinInspector),
      detalle: 'Elige por qué: desde ahora cuenta la espera del inspector del punto.',
      aviso: 'Si nadie toma la entrega, avisaremos al coordinador en ${_punto.esperaAvisoMinutos} min.',
      accion: 'Empezar la espera',
    );
    if (motivo == null || !mounted) return;
    setState(() => _activando = true);
    try {
      final momento = _ahora;
      Posicion? posicion;
      try {
        posicion = await ref.read(servicioUbicacionProvider).obtenerActual();
      } catch (_) {
        // Sin GPS se activa igual: la hora es lo que cuenta para el reloj.
        posicion = null;
      }
      final jobId = const Uuid().v4();
      final cola = ref.read(colaRepositorioProvider);
      final activacion = SinInspectorParadaJob(
        id: jobId,
        etiqueta: 'No hay inspector · ${_punto.lugar}',
        creadoEn: DateTime.now().millisecondsSinceEpoch,
        usuarioId: usuarioId,
        viajeId: _punto.viajeId,
        lugarId: _punto.lugarId,
        motivo: motivo.codigo,
        motivoTexto: motivo.texto,
        fechaMomento: isoConZona(momento),
        lat: posicion?.lat,
        lng: posicion?.lng,
        precisionM: posicion?.precision,
        avisoMinutos: _punto.esperaAvisoMinutos,
        planBMinutos: _punto.esperaPlanBMinutos,
      );
      await cola.agregar(activacion);
      // CA-3: la hora y los plazos quedan también en la copia sin señal de la parada, para que el reloj no dependa
      // de la cola ni de la pantalla.
      await ref.read(entregasRepositorioProvider).anotarActivacion(activacion);
      _activacionVista = activacion;
      final sync = ref.read(syncControllerProvider.notifier);
      await sync.refrescar();
      await sync.sincronizar();
      final enviado = (await cola.listar()).where((j) => j.id == jobId).firstOrNull;
      if (!mounted) return;
      // Rechazada por algo de la parada (no tiene inspectores, falta el «Llegué», ya la tomaron): reintentar daría lo
      // mismo. Se quita de la cola y se explica por su motivo.
      if (enviado != null && enviado.fallido) {
        await cola.eliminar(jobId);
        await sync.refrescar();
        if (!mounted) return;
        setState(() => _activacionVista = null);
        await showDialog<void>(
          context: context,
          builder: (contexto) => AlertDialog(
            title: const Text('No se activó «No hay inspector»'),
            content: Text(explicacionDeLaActivacionRechazada(enviado.codigoError)),
            actions: [TextButton(onPressed: () => Navigator.of(contexto).pop(), child: const Text('Entendido'))],
          ),
        );
        widget.alActualizar?.call();
        return;
      }
      ref.invalidate(enviosDeLaParadaProvider(_parada));
      final salio = enviado == null;
      ScaffoldMessenger.of(context).showSnackBar(SnackBar(
        content: Text(
          salio
              ? 'Quedó registrado: la espera cuenta desde las ${horaCorta(momento)}.'
              : 'Sin señal: quedó guardado con la hora de ahora y se enviará solo. La espera ya cuenta.',
        ),
      ));
      if (salio) widget.alActualizar?.call();
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(mensajeDeError(e, 'No se pudo guardar.'))));
      }
    } finally {
      if (mounted) setState(() => _activando = false);
    }
  }

  /// CA-6: la entrega que el servidor rechazó después y que llegó tarde porque un inspector tomó la parada. La firma
  /// se conserva: vuelve a la cola sola, para firmar con él.
  Future<void> _firmarConElInspector(EntregaPuntoJob rechazada) async {
    final sync = ref.read(syncControllerProvider.notifier);
    // Las fotos del cierre con una sola firma no viajan con la firma sola: se borran del teléfono, que si no
    // quedarían sin dueño.
    final archivos = ref.read(archivosLocalesProvider);
    for (final foto in rechazada.fotos) {
      try {
        await archivos.borrar(foto.ruta);
      } catch (_) {
        // Una foto que no se pudo borrar no detiene la firma.
      }
    }
    await ref.read(colaRepositorioProvider).guardar(rechazada.soloMiFirma());
    await sync.refrescar();
    await sync.sincronizar();
    if (!mounted) return;
    ref.invalidate(enviosDeLaParadaProvider(_parada));
    widget.alActualizar?.call();
  }

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context).textTheme;
    ref.watch(authControllerProvider.select((e) => (e.sesion?.usuario.rol, e.sesion?.usuario.nombre)));
    // Lo que la cola tiene de esta parada y la hora: con ellos se cuenta la espera (S8-HU-05.2 CA-3).
    final envios = ref.watch(enviosDeLaParadaProvider(_parada)).value ?? sinEnvios;
    if (envios.activacion case final activacion?) _activacionVista = activacion;
    refrescarAlSalirLaActivacion(ref, _parada);
    final ahora = ref.watch(horaDelTelefonoProvider)();
    // Lo que se sabe de cada revisión de llegada decide el motivo del botón: al llegar, la pantalla se vuelve a pintar.
    for (final u in _punto.porEntregar) {
      if (u.inspeccionId case final id?) ref.watch(revisionDeLlegadaProvider(id));
    }
    final papel = _papel;
    final carros = _punto.porEntregar;
    final rechazos = _rechazosVigentes;
    final motivo = _motivoSinFirmar();
    final yaFirme = _yaFirme;
    // CA-5: si quien recibe dejó reservas después de que el conductor firmó, el servidor retira la firma del
    // conductor en esos carros y los deja esperándola otra vez, mientras los demás cierran con las dos firmas. Eso es
    // lo que se ve: carros ya cerrados (o con la firma del conductor) junto a carros con reservas, la firma de quien
    // recibe y sin la del conductor.
    final firmoAntes = _punto.unidades.any((u) => u.cerrada || u.entregaFirmada);
    final volverAFirmar = papel == _Papel.entrega && !_enElLugar && firmoAntes
        ? [for (final u in carros) if (!u.entregaFirmada && u.recibeFirmada && u.reservas != null) u.vin]
        : const <String>[];
    final porQueNoUnaFirma = _porQueNoUnaFirma;
    final reloj = _relojEspera;
    final rol = ref.read(authControllerProvider).sesion?.usuario.rol;
    final ofreceNoHay = ofreceNoHayInspector(_punto, rol: rol, reloj: reloj);
    // Los motivos de la hoja salen de los catálogos: se observan desde ya para tenerlos al tocar el botón.
    if (ofreceNoHay) ref.watch(catalogosProvider);
    return Scaffold(
      appBar: AppBar(
        title: Text('Entrega · ${_punto.lugar}'),
        actions: [
          if (widget.alActualizar != null)
            IconButton(
              tooltip: 'Actualizar la parada',
              onPressed: widget.actualizando || _guardando ? null : widget.alActualizar,
              icon: const Icon(Icons.refresh),
            ),
        ],
      ),
      body: SafeArea(
        child: ListView(
          padding: const EdgeInsets.all(Espacio.lg),
          children: [
            if (widget.actualizando) const LinearProgressIndicator(),
            if (widget.errorAlActualizar case final error?)
              Text(error, style: tema.bodySmall?.copyWith(color: Colores.peligro)),
            Text(
              carros.isEmpty
                  ? 'Ya no quedan carros por entregar en esta parada.'
                  : 'Aquí entregas ${carros.length == 1 ? 'este carro' : 'estos ${carros.length} carros'}',
              style: tema.titleMedium,
            ),
            const SizedBox(height: Espacio.sm),
            _QuienRecibe(punto: _punto, papel: papel, planB: _planBHabilitado, conPersonaDelLugar: _conPersonaDelLugar),
            if (_punto.copiaSinSenal) ...[
              const SizedBox(height: Espacio.xs),
              Text(
                'Sin señal: ves la última copia de la parada. La espera sigue contando.',
                style: tema.bodySmall?.copyWith(color: Colores.advertencia),
              ),
            ],
            // CA-6: lo que el servidor rechazó después, por su motivo, con lo que se puede hacer.
            if (envios.rechazada case final rechazada? when papel == _Papel.entrega) ...[
              const SizedBox(height: Espacio.md),
              _RechazoTardio(
                job: rechazada,
                // CA-10: mientras falte calificar los daños que marcó el conductor, su firma también se rechaza.
                faltaQueCalifique: _punto.danosPorCalificar.isEmpty ? null : _faltaQueCalifique,
                alFirmarConEl: rechazada.firma == null ? null : () => _firmarConElInspector(rechazada),
                alReintentar: () => ref.read(syncControllerProvider.notifier).reintentar(rechazada.id),
                alQuitar: () => ref.read(syncControllerProvider.notifier).descartar(rechazada.id),
              ),
            ],
            // CA-3 y CA-6: la activación que el servidor rechazó después: el reloj se fue y se dice por qué.
            if (envios.activacionRechazada case final rechazada? when papel == _Papel.entrega) ...[
              const SizedBox(height: Espacio.md),
              _ActivacionRechazada(
                job: rechazada,
                alReintentar: () => ref.read(syncControllerProvider.notifier).reintentar(rechazada.id),
                alQuitar: () => ref.read(syncControllerProvider.notifier).descartar(rechazada.id),
              ),
            ],
            // CA-3: el reloj de la espera, solo al conductor.
            if (reloj != null && papel == _Papel.entrega) ...[
              const SizedBox(height: Espacio.md),
              _TarjetaEspera(reloj: reloj, ahora: ahora),
            ],
            // CA-1: «No hay inspector», solo al conductor del viaje, después de su «Llegué» y mientras nadie la tome.
            if (ofreceNoHay) ...[
              const SizedBox(height: Espacio.md),
              OutlinedButton(
                onPressed: _activando || _guardando ? null : _activarSinInspector,
                child: _activando
                    ? const SizedBox(width: 22, height: 22, child: CircularProgressIndicator(strokeWidth: 2.5))
                    : const Text('No hay inspector'),
              ),
              Text(
                'Si el inspector del punto no llega, tócalo: empieza a contar la espera y, pasado el plazo, podrás cerrar '
                'sin él.',
                style: tema.bodySmall,
              ),
            ],
            // S8-HU-09.2 CA-3: el inspector que tomó la parada está en el lugar, como el conductor: si el lugar no
            // tiene ubicación, la deja desde aquí. El conductor la sigue fijando desde la lista de paradas.
            if (papel == _Papel.recibe) ...[
              const SizedBox(height: Espacio.sm),
              FijarUbicacionLugar(lugarId: _punto.lugarId),
            ],
            const SizedBox(height: Espacio.md),
            if (carros.isNotEmpty)
              Tarjeta(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    for (final u in carros)
                      _FilaCarroDeLaParada(
                        unidad: u,
                        // Quien entrega en una parada con inspector lee las reservas que dejó quien recibe (CA-5).
                        reserva: _decideQuienRecibe ? _reservas[u.vin] : u.reservas,
                        rechazo: rechazos[u.vin],
                        decide: _decideQuienRecibe && !_unaSolaFirma && !yaFirme,
                        alReservar: () => _reservar(u.vin),
                        alRechazar: (danos) => _rechazar(u.vin, danos),
                        alQuitarRechazo: () => setState(() => _rechazos.remove(u.vin)),
                        // CA-10: el inspector que llegó ve como revisados los carros que el conductor ya revisó y
                        // captura los que faltan.
                        etiquetaRevisado: papel == _Papel.recibe ? 'Revisado' : 'Por firmar',
                        alCapturar: papel == _Papel.recibe
                            ? () => Navigator.of(context).push<void>(
                                  MaterialPageRoute(builder: (_) => VinPage(vin: u.vin, viajeId: _punto.viajeId)),
                                )
                            : null,
                      ),
                  ],
                ),
              ),
            // CA-10: los daños sin nivel que marcó el conductor; el conductor no ve esta lista.
            if (papel == _Papel.recibe && _punto.danosPorCalificar.isNotEmpty) ...[
              const SizedBox(height: Espacio.md),
              _PorCalificar(
                punto: _punto,
                alCalificado: () {
                  ref.invalidate(sinCalificarDelViajeProvider(_punto.viajeId));
                  widget.alActualizar?.call();
                },
              ),
            ],
            for (final u in _punto.rechazadas) ...[
              const SizedBox(height: Espacio.sm),
              _FilaUnidad(u),
            ],
            if (volverAFirmar.isNotEmpty) ...[
              const SizedBox(height: Espacio.md),
              Semantics(
                liveRegion: true,
                child: Text(
                  'Quien recibe dejó reservas después de tu firma: firma otra vez ${volverAFirmar.length == 1 ? 'este carro' : 'estos carros'}: '
                  '${volverAFirmar.join(', ')}.',
                  style: tema.bodyMedium?.copyWith(color: Colores.advertencia),
                ),
              ),
            ],
            if (carros.isNotEmpty && papel != _Papel.ninguno) ...[
              const SizedBox(height: Espacio.lg),
              if (_planBHabilitado && _planB == null)
                ..._eleccionDelPlanB(tema)
              else if (yaFirme && !_unaSolaFirma)
                Tarjeta(
                  child: Text(
                    papel == _Papel.entrega
                        ? 'Ya firmaste la entrega de esta parada. Falta la firma de ${_punto.receptorNombre ?? 'quien recibe'}.'
                        : 'Ya firmaste el recibo de esta parada. Falta la firma del conductor.',
                    style: tema.titleSmall?.copyWith(color: Colores.exito),
                  ),
                )
              else if (!_unaSolaFirma)
                ..._firmaDeLasPartes(tema, papel, carros.length, rechazos.length, motivo)
              else
                ..._cierreConUnaFirma(tema, papel, motivo, yaFirme),
              // CA-4: donde nadie tomó la entrega no hay otro cierre sin inspector del punto que el plan B.
              if (!_unaSolaFirma && !_sinQuienReciba) ...[
                const SizedBox(height: Espacio.sm),
                TextButton(
                  onPressed: _guardando || porQueNoUnaFirma != null ? null : () => setState(() => _unaSolaFirma = true),
                  child: const Text('La otra parte no está: cerrar con una sola firma'),
                ),
                if (porQueNoUnaFirma != null) _MotivoDelBoton(porQueNoUnaFirma),
              ],
              if (_conPersonaDelLugar) ...[
                const SizedBox(height: Espacio.sm),
                TextButton(
                  onPressed: _guardando ? null : () => setState(_olvidarElPlanB),
                  child: const Text('Volver: elegir cómo cerrar'),
                ),
              ],
            ] else if (papel == _Papel.ninguno) ...[
              const SizedBox(height: Espacio.lg),
              Text(_porQueNoFirma, style: tema.bodySmall),
            ],
          ],
        ),
      ),
    );
  }

  /// CA-4: con el plan B habilitado, el conductor elige cómo cierra sin inspector del punto.
  List<Widget> _eleccionDelPlanB(TextTheme tema) => [
        Text('Cerrar sin inspector del punto', style: tema.titleMedium),
        const SizedBox(height: Espacio.xs),
        Text(
          'La revisión de llegada de cada carro tiene que estar cerrada. La hora y el lugar de ahora quedan con la entrega.',
          style: tema.bodySmall,
        ),
        const SizedBox(height: Espacio.md),
        FilledButton(
          onPressed: _guardando ? null : () => setState(() => _planB = _PlanB.personaDelLugar),
          child: const Text('Recibe una persona del lugar'),
        ),
        const SizedBox(height: Espacio.sm),
        OutlinedButton(
          onPressed: _guardando
              ? null
              : () => setState(() {
                    _planB = _PlanB.miFirma;
                    _unaSolaFirma = true;
                    _motivoCodigo = MotivoUnaFirma.sinInspectorDelPunto;
                  }),
          child: const Text('Cerrar con mi firma'),
        ),
      ];

  List<Widget> _firmaDeLasPartes(TextTheme tema, _Papel papel, int carros, int rechazados, String? motivo) => [
        LienzoFirma(etiqueta: 'Tu firma', onCambio: (p) => setState(() => _firma = p)),
        const SizedBox(height: Espacio.sm),
        Text(
          _enElLugar && papel == _Papel.entrega
              ? 'Firmas tú y firma quien recibe, aquí mismo. Las firmas quedan en ${_deLosCarros(carros)}.'
              : 'Firmas tú desde tu sesión; la otra parte firma desde la suya. La firma queda en ${_deLosCarros(carros)}.',
          style: tema.bodySmall,
        ),
        // CA-3: el formulario de la persona del lugar sale solo donde la parada no tiene inspectores.
        if (_enElLugar && papel == _Papel.entrega) ...[
          const SizedBox(height: Espacio.md),
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
            // No es solo números: el servidor admite puntos, guiones y espacios, y hay documentos que los llevan.
            keyboardType: TextInputType.text,
            decoration: const InputDecoration(labelText: 'Cédula de quien recibe', counterText: ''),
          ),
          const SizedBox(height: Espacio.md),
          LienzoFirma(etiqueta: 'Firma de quien recibe', onCambio: (p) => setState(() => _firmaRecibe = p)),
          const SizedBox(height: Espacio.sm),
          Text(
            'Con las dos firmas la entrega queda cerrada: no falta nadie más. Su nombre y su cédula quedan registrados '
            'con la entrega.',
            style: tema.bodySmall,
          ),
        ],
        const SizedBox(height: Espacio.lg),
        FilledButton(
          onPressed: _puedeEnviar ? _registrar : null,
          child: _guardando
              ? const SizedBox(width: 22, height: 22, child: CircularProgressIndicator(strokeWidth: 2.5, color: Colors.white))
              : Text(textoDelBotonDeEntrega(carros: carros, rechazados: rechazados)),
        ),
        if (motivo != null) _MotivoDelBoton(motivo),
      ];

  List<Widget> _cierreConUnaFirma(TextTheme tema, _Papel papel, String? motivo, bool yaFirme) => [
        Text(_conMiFirma ? 'Cerrar con mi firma' : 'Cerrar con una sola firma', style: tema.titleMedium),
        const SizedBox(height: Espacio.xs),
        // C28: el rechazo por novedad necesita a quien recibe; con una sola firma no hay a quién.
        Text(
          _conMiFirma
              ? 'Con tu sola firma se entregan todos los carros: para rechazar uno, cierra con la persona del lugar'
              : 'Con una sola firma se cierran todos los carros de la parada: para rechazar uno, firma la parada con quien recibe',
          style: tema.bodySmall?.copyWith(color: Colores.advertencia),
        ),
        const SizedBox(height: Espacio.md),
        Text('Motivo', style: tema.titleSmall),
        const SizedBox(height: Espacio.xs),
        // CA-4: el plan B tiene un solo motivo, que no se elige.
        if (_conMiFirma)
          Text('Sin inspector del punto', style: tema.bodyMedium?.copyWith(color: Colores.textoPrimario))
        else
        Wrap(
          spacing: Espacio.sm,
          runSpacing: Espacio.xs,
          children: [
            ChoiceChip(
              label: Text(papel == _Papel.recibe ? 'El conductor se negó a firmar' : 'Quien recibe se negó a firmar'),
              selected: _motivoCodigo == MotivoUnaFirma.seNegoAFirmar,
              materialTapTargetSize: MaterialTapTargetSize.padded,
              onSelected: (_) => setState(() => _motivoCodigo = MotivoUnaFirma.seNegoAFirmar),
            ),
            // Solo donde la parada no tiene inspectores: donde los tiene, el servidor responde `sin_receptor`.
            if (_enElLugar)
              ChoiceChip(
                label: const Text('No había nadie para recibir'),
                selected: _motivoCodigo == MotivoUnaFirma.noHabiaNadie,
                materialTapTargetSize: MaterialTapTargetSize.padded,
                onSelected: (_) => setState(() => _motivoCodigo = MotivoUnaFirma.noHabiaNadie),
              ),
          ],
        ),
        if (_motivoCodigo == MotivoUnaFirma.noHabiaNadie) ...[
          const SizedBox(height: Espacio.xs),
          // C23: la evidencia ya está en la revisión de llegada de cada carro.
          Text('No hace falta otra foto: valen las fotos de la revisión de llegada de cada carro.', style: tema.bodySmall),
        ],
        const SizedBox(height: Espacio.md),
        Text('Fotos', style: tema.titleSmall),
        Text(
          _motivoCodigo == MotivoUnaFirma.seNegoAFirmar
              ? 'De la entrega y de quien no firma. Con este motivo la foto es obligatoria'
              : 'Opcionales.',
          style: tema.bodySmall,
        ),
        const SizedBox(height: Espacio.sm),
        Wrap(
          spacing: Espacio.sm,
          runSpacing: Espacio.sm,
          crossAxisAlignment: WrapCrossAlignment.center,
          children: [
            for (final foto in _fotos)
              ClipRRect(
                borderRadius: BorderRadius.circular(6),
                child: Image.file(
                  File(foto.ruta),
                  width: 56,
                  height: 48,
                  fit: BoxFit.cover,
                  semanticLabel: 'Foto del cierre',
                  errorBuilder: (_, _, _) => const SizedBox(width: 56, height: 48, child: Icon(Icons.photo_outlined)),
                ),
              ),
            BotonEnFila(texto: _fotos.isEmpty ? 'Tomar foto' : 'Otra foto', onPressed: _guardando ? null : _tomarFoto),
          ],
        ),
        const SizedBox(height: Espacio.md),
        Text('Testigo (opcional)', style: tema.titleSmall),
        Text('Alguien que no es usuario, como el vigilante: su nombre y su cédula van juntos.', style: tema.bodySmall),
        TextField(
          controller: _testigoNombre,
          onChanged: (_) => setState(() {}),
          maxLength: nombreExternoMax,
          textCapitalization: TextCapitalization.words,
          decoration: const InputDecoration(labelText: 'Nombre del testigo', counterText: ''),
        ),
        TextField(
          controller: _testigoCedula,
          onChanged: (_) => setState(() {}),
          maxLength: cedulaExternaMax,
          keyboardType: TextInputType.text,
          decoration: const InputDecoration(labelText: 'Cédula del testigo', counterText: ''),
        ),
        const SizedBox(height: Espacio.sm),
        TextField(
          controller: _motivoTexto,
          onChanged: (_) => setState(() {}),
          minLines: 2,
          maxLines: 4,
          maxLength: motivoUnaFirmaMax,
          textCapitalization: TextCapitalization.sentences,
          decoration: const InputDecoration(labelText: 'Algo más (opcional)', hintText: 'Qué pasó'),
        ),
        if (!yaFirme) ...[
          const SizedBox(height: Espacio.sm),
          LienzoFirma(etiqueta: 'Tu firma', onCambio: (p) => setState(() => _firma = p)),
        ],
        Semantics(
          liveRegion: true,
          child: Text(
            'Quedará registrado como novedad para el coordinador, con el motivo, las fotos y la hora y el lugar de ahora.',
            style: tema.bodySmall?.copyWith(color: Colores.advertencia),
          ),
        ),
        const SizedBox(height: Espacio.md),
        FilledButton(
          onPressed: _puedeEnviar ? _registrar : null,
          child: _guardando
              ? const SizedBox(width: 22, height: 22, child: CircularProgressIndicator(strokeWidth: 2.5, color: Colors.white))
              : Text(_conMiFirma ? 'Cerrar sin inspector del punto' : 'Cerrar con una sola firma'),
        ),
        if (motivo != null) _MotivoDelBoton(motivo),
        const SizedBox(height: Espacio.sm),
        if (_conMiFirma)
          TextButton(
            onPressed: _guardando ? null : () => setState(_olvidarElPlanB),
            child: const Text('Volver: elegir cómo cerrar'),
          )
        else
          TextButton(
            onPressed: _guardando ? null : () => setState(() => _unaSolaFirma = false),
            child: const Text('Volver: firman las dos partes'),
          ),
      ];
}

/// Quién recibe en la parada, como lo dice el servidor (CA-3). No se cambia desde aquí.
class _QuienRecibe extends StatelessWidget {
  const _QuienRecibe({required this.punto, required this.papel, this.planB = false, this.conPersonaDelLugar = false});

  final EntregaPunto punto;
  final _Papel papel;

  /// S8-HU-05.2 CA-4: el plan B se habilitó, y el conductor eligió (o no) cerrar con la persona del lugar.
  final bool planB;
  final bool conPersonaDelLugar;

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context).textTheme;
    final (titulo, detalle, aviso) = switch (punto.receptor) {
      ReceptorParada.personaDelLugar => (
          'Aquí recibe la persona del lugar',
          'Este punto no tiene inspector: su nombre, su cédula y su firma van aquí mismo.',
          false,
        ),
      ReceptorParada.sinReceptor when conPersonaDelLugar => (
          'Aquí recibe la persona del lugar',
          'Nadie tomó la entrega: su nombre, su cédula y su firma van aquí mismo.',
          false,
        ),
      ReceptorParada.sinReceptor when planB => (
          'Aquí firma el inspector del punto',
          'Nadie tomó la entrega: ya puedes cerrar sin inspector del punto.',
          false,
        ),
      ReceptorParada.sinReceptor => (
          'Aquí firma el inspector del punto',
          'Este punto tiene inspector: pídele a un inspector del punto que tome la entrega en su app',
          true,
        ),
      ReceptorParada.inspectorDelPunto when papel != _Papel.recibe => (
          'Aquí firma el inspector del punto',
          switch ((punto.receptorNombre, papel)) {
            // S8-HU-05.2 CA-5: la espera, si la había, desapareció: el reloj nunca le quita la entrega al inspector.
            (final nombre?, _Papel.entrega) => '$nombre tomó la entrega: firma con él',
            (final nombre?, _) => 'Tomó la entrega $nombre.',
            (null, _) => 'Firma desde su app.',
          },
          false,
        ),
      ReceptorParada.inspectorDelPunto => (
          'Recibes tú',
          punto.conductorLlego
              ? 'El conductor ya llegó a esta parada. Él firma desde su app.'
              : 'El conductor aún no marca «Llegué» en esta parada',
          !punto.conductorLlego,
        ),
    };
    return Tarjeta(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(titulo, style: tema.titleSmall),
          Text(detalle, style: tema.bodySmall?.copyWith(color: aviso ? Colores.advertencia : null)),
        ],
      ),
    );
  }
}

/// Un carro por entregar en la parada, con lo que quien recibe decide de él: recibirlo con reservas o rechazarlo
/// por un daño de su revisión de llegada (CA-4 y CA-5).
class _FilaCarroDeLaParada extends ConsumerWidget {
  const _FilaCarroDeLaParada({
    required this.unidad,
    required this.reserva,
    required this.rechazo,
    required this.decide,
    required this.alReservar,
    required this.alRechazar,
    required this.alQuitarRechazo,
    this.etiquetaRevisado = 'Por firmar',
    this.alCapturar,
  });

  final UnidadEntrega unidad;
  final String? reserva;
  final DanoDeLlegada? rechazo;

  /// Lo que dice el carro con la revisión de llegada cerrada.
  final String etiquetaRevisado;

  /// S8-HU-05.2 CA-10: la revisión de llegada que falta la captura el inspector que tomó la parada. Null para los
  /// demás.
  final VoidCallback? alCapturar;

  /// Quien mira decide de este carro: es quien recibe y no está cerrando con una sola firma.
  final bool decide;
  final VoidCallback alReservar;
  final void Function(List<DanoDeLlegada> danos) alRechazar;
  final VoidCallback alQuitarRechazo;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final tema = Theme.of(context).textTheme;
    final revision = unidad.inspeccionId == null ? null : ref.watch(revisionDeLlegadaProvider(unidad.inspeccionId!));
    final abierta = unidad.estado == EstadoEntrega.sinInspeccion || revision?.value?.cerrada == false;
    final danos = revision?.value?.danosConFoto;
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: Espacio.xs),
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
              if (rechazo != null)
                const Insignia('Rechazado por novedad', tono: TonoInsignia.peligro)
              else if (abierta)
                const Insignia('Falta la revisión de llegada', tono: TonoInsignia.advertencia)
              else
                Insignia(etiquetaRevisado, tono: TonoInsignia.info),
            ],
          ),
          if (abierta && alCapturar != null && rechazo == null)
            Align(alignment: Alignment.centerLeft, child: BotonEnFila(texto: 'Capturar', onPressed: alCapturar)),
          if (rechazo case final r?)
            Text('Rechazado por novedad: ${r.zona}', style: tema.bodySmall?.copyWith(color: Colores.peligro)),
          if (reserva case final r?) Text('Con reservas: $r', style: tema.bodySmall?.copyWith(color: Colores.advertencia)),
          if (decide && !abierta) ...[
            Wrap(
              spacing: Espacio.sm,
              children: [
                if (rechazo == null)
                  TextButton(
                    onPressed: alReservar,
                    child: Text(reserva == null ? 'Recibir con reservas' : 'Cambiar la reserva'),
                  ),
                if (rechazo != null)
                  TextButton(onPressed: alQuitarRechazo, child: const Text('Quitar el rechazo'))
                else
                  TextButton(
                    onPressed: danos == null || danos.isEmpty ? null : () => alRechazar(danos),
                    child: const Text('Rechazar por novedad'),
                  ),
              ],
            ),
            if (rechazo == null && danos != null && danos.isEmpty)
              Text(
                'Para rechazarlo, registra el daño con su foto en la revisión de llegada',
                style: tema.bodySmall,
              ),
            if (rechazo == null && revision != null && revision.hasError && danos == null)
              Text('Sin señal no se ven los daños de su revisión de llegada.', style: tema.bodySmall),
          ],
        ],
      ),
    );
  }
}

/// El reloj de la espera del inspector del punto (S8-HU-05.2 CA-3): cuánto falta para el aviso al coordinador y para
/// el plan B, contado desde la hora en que se tocó «No hay inspector».
class _TarjetaEspera extends StatelessWidget {
  const _TarjetaEspera({required this.reloj, required this.ahora});

  final RelojEspera reloj;
  final DateTime ahora;

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context).textTheme;
    final planB = reloj.planBHabilitado(ahora);
    return Semantics(
      liveRegion: true,
      child: Tarjeta(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text('Esperando al inspector del punto', style: tema.titleSmall),
            Text('Desde las ${horaCorta(reloj.activadaAt)}', style: tema.bodySmall),
            const SizedBox(height: Espacio.xs),
            Text(reloj.textoAviso(ahora), style: tema.bodyMedium?.copyWith(color: Colores.textoPrimario)),
            Text(
              reloj.textoPlanB(ahora),
              style: tema.bodyMedium?.copyWith(color: planB ? Colores.exito : Colores.advertencia, fontWeight: FontWeight.w600),
            ),
            if (reloj.sinEnviar) ...[
              const SizedBox(height: Espacio.xs),
              Text('Aún sin enviar: se envía sola con la señal. La espera cuenta desde que la activaste.', style: tema.bodySmall),
            ],
            const SizedBox(height: Espacio.xs),
            Text('Si un inspector del punto toma la entrega, firmas con él.', style: tema.bodySmall),
          ],
        ),
      ),
    );
  }
}

/// La entrega de la parada que el servidor rechazó después, desde la cola (S8-HU-05.2 CA-6), con su explicación y lo
/// que se puede hacer con ella. Nunca el texto crudo del servidor.
class _RechazoTardio extends StatelessWidget {
  const _RechazoTardio({
    required this.job,
    required this.alFirmarConEl,
    required this.alReintentar,
    required this.alQuitar,
    this.faltaQueCalifique,
  });

  final EntregaPuntoJob job;
  final VoidCallback? alFirmarConEl;

  /// Por qué la firma guardada todavía no puede ir con el inspector que tomó la parada; null si ya puede.
  final String? faltaQueCalifique;
  final VoidCallback alReintentar;
  final VoidCallback alQuitar;

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context).textTheme;
    final motivo = job.codigoError;
    return Tarjeta(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text('No se pudo enviar tu entrega', style: tema.titleSmall?.copyWith(color: Colores.peligro)),
          // Sin código del servidor falló en el teléfono: se dice lo que pasó, que no es un rechazo.
          Text(
            motivo == null ? job.error ?? 'No se pudo enviar.' : explicacionDelRechazoTardio(motivo),
            style: tema.bodyMedium?.copyWith(color: Colores.textoPrimario),
          ),
          const SizedBox(height: Espacio.sm),
          if (motivo == MotivoParada.entregaTomada && faltaQueCalifique != null)
            Text(faltaQueCalifique!, style: tema.bodySmall?.copyWith(color: Colores.advertencia))
          else if (motivo == MotivoParada.entregaTomada && alFirmarConEl != null)
            FilledButton(onPressed: alFirmarConEl, child: const Text('Firmar con él con mi firma'))
          else if (motivo == MotivoParada.planBNoHabilitado || motivo == codigoSinRespuesta || motivo == null)
            FilledButton(onPressed: alReintentar, child: const Text('Volver a intentar')),
          const SizedBox(height: Espacio.xs),
          OutlinedButton(onPressed: alQuitar, child: const Text('Quitar el envío')),
        ],
      ),
    );
  }
}

/// «No hay inspector» que el servidor rechazó después, desde la cola (S8-HU-05.2 CA-3 y CA-6): el reloj desaparece
/// y aquí se dice por qué, por su motivo.
class _ActivacionRechazada extends StatelessWidget {
  const _ActivacionRechazada({required this.job, required this.alReintentar, required this.alQuitar});

  final SinInspectorParadaJob job;
  final VoidCallback alReintentar;
  final VoidCallback alQuitar;

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context).textTheme;
    final motivo = job.codigoError;
    return Tarjeta(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text('No se activó «No hay inspector»', style: tema.titleSmall?.copyWith(color: Colores.peligro)),
          Text(
            motivo == null ? job.error ?? 'No se pudo enviar.' : explicacionDeLaActivacionRechazada(motivo),
            style: tema.bodyMedium?.copyWith(color: Colores.textoPrimario),
          ),
          const SizedBox(height: Espacio.sm),
          if (motivo == codigoSinRespuesta || motivo == null)
            FilledButton(onPressed: alReintentar, child: const Text('Volver a intentar')),
          const SizedBox(height: Espacio.xs),
          OutlinedButton(onPressed: alQuitar, child: const Text('Quitar el envío')),
        ],
      ),
    );
  }
}

/// S8-HU-05.2 CA-10 (C19): los daños sin nivel que el conductor marcó en sus revisiones de llegada con el plan B. El
/// inspector que tomó la parada los califica antes de firmar, con la zona y la clase que ya traen.
class _PorCalificar extends ConsumerWidget {
  const _PorCalificar({required this.punto, required this.alCalificado});

  final EntregaPunto punto;
  final VoidCallback alCalificado;

  /// La novedad completa (zona, clase, tamaño y foto) sale de los daños sin calificar del viaje; sin señal, lo que
  /// trae la parada.
  NovedadPendiente _novedad(DanoPorCalificar dano, List<NovedadPendiente> delViaje) =>
      delViaje.where((n) => n.id == dano.hallazgoId).firstOrNull ??
      NovedadPendiente(
        id: dano.hallazgoId,
        vin: dano.vin,
        proceso: 'descargue',
        momento: 'llegada',
        tamano: '',
        viajeId: punto.viajeId,
        clase: dano.clase,
        zona: dano.parte,
      );

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final tema = Theme.of(context).textTheme;
    final catalogos = ref.watch(catalogosProvider);
    final entorno = ref.watch(entornoProvider);
    final delViaje = ref.watch(sinCalificarDelViajeProvider(punto.viajeId)).value ?? const <NovedadPendiente>[];
    return Tarjeta(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text('Por calificar en esta parada', style: tema.titleSmall),
          Text(
            'Los daños que marcó el conductor en sus revisiones de llegada. Califícalos antes de firmar.',
            style: tema.bodySmall,
          ),
          for (final dano in punto.danosPorCalificar) ...[
            const SizedBox(height: Espacio.md),
            Builder(
              builder: (context) {
                final novedad = _novedad(dano, delViaje);
                return Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    TextoMono(dano.vin),
                    Text(
                      [
                        dano.parte ?? novedad.zona ?? 'Sin zona',
                        if ((dano.clase ?? novedad.clase) case final clase?) catalogos.etiqueta(Catalogo.clasesDano, clase),
                      ].join(' · '),
                      style: tema.bodyMedium?.copyWith(color: Colores.textoPrimario),
                    ),
                    const SizedBox(height: Espacio.xs),
                    FotoEvidencia(
                      url: resolverUrlImagen(entorno.apiUrl, novedad.foto?.url),
                      encabezados: entorno.encabezadosEnrutamiento,
                      alto: 140,
                      venceEn: novedad.foto?.venceEn,
                      alVencer: () => ref.invalidate(sinCalificarDelViajeProvider(punto.viajeId)),
                      sinSenal: !ref.watch(syncControllerProvider).online,
                      semanticLabel: 'Foto del daño que marcó el conductor',
                    ),
                    const SizedBox(height: Espacio.xs),
                    FilledButton(
                      onPressed: () async {
                        final calificada = await Navigator.of(context).push<bool>(
                          MaterialPageRoute(builder: (_) => RevisionNovedadPage(novedad: novedad)),
                        );
                        if (calificada == true) alCalificado();
                      },
                      child: const Text('Calificar'),
                    ),
                  ],
                );
              },
            ),
          ],
        ],
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
