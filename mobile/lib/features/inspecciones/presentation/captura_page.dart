import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:uuid/uuid.dart';

import '../../../app/proveedores.dart';
import '../../../core/dispositivo/servicios.dart';
import '../../../core/errores/error_api.dart';
import '../../../core/http/urls_api.dart';
import '../../../core/tema/tema.dart';
import '../../../core/ui/widgets.dart';
import '../../auth/presentation/auth_controller.dart';
import '../../sync/presentation/sync_controller.dart';
import '../../sync/providers.dart';
import '../../catalogos/domain/catalogos.dart';
import '../../catalogos/presentation/catalogos_controller.dart';
import '../../dibujo_dano/domain/dibujo_dano.dart';
import '../../dibujo_dano/presentation/dibujo_dano_controller.dart';
import '../../dibujo_dano/presentation/selector_zona_dano.dart';
import '../../vehiculos/domain/etiquetas.dart';
import '../../viajes/domain/viaje.dart';
import '../../viajes/providers.dart';
import '../domain/acomodacion.dart';
import '../domain/checklist_vistas.dart';
import '../domain/extra_local.dart';
import '../domain/foto_local.dart';
import '../domain/hallazgo_local.dart';
import '../domain/hallazgo_previo.dart';
import '../domain/reglas_informe.dart';
import '../providers.dart';
import 'aviso_preexistente.dart';
import 'camara_page.dart';
import 'filas_captura.dart';
import 'nota_foto.dart';
import 'seccion_acomodacion.dart';
import 'seccion_extras.dart';
import 'sellado.dart';
import 'selector_dano.dart';

/// La selección del dibujo de daños de esta pantalla (ver [seleccionZonaProvider]).
const _contextoDibujo = 'captura';

/// Lo que se puede hacer con un daño ya registrado al tocar su zona.
enum _AccionDano { corregir, quitar }

/// Captura de un informe: datos del transporte, novedades sobre el dibujo de daños, fotos selladas y notas.
///
/// Funciona sin señal: al cerrar, el informe completo se guarda en la cola persistente y se envía cuando la conexión
/// sea estable.
class CapturaPage extends ConsumerStatefulWidget {
  const CapturaPage({
    super.key,
    required this.vin,
    required this.etapaId,
    required this.proceso,
    required this.momento,
    this.viajeId,
  });

  final String vin;
  final String etapaId;
  final String proceso;

  /// `salida` o `llegada`: son dos inspecciones distintas por etapa y el servidor no acepta otra cosa.
  final String momento;

  /// Viaje del que cuelga la etapa. De él salen la placa, el tráiler, la unidad y el conductor.
  final String? viajeId;

  @override
  ConsumerState<CapturaPage> createState() => _EstadoCapturaPage();
}

class _EstadoCapturaPage extends ConsumerState<CapturaPage> {
  Posicion? _gps;
  UbicacionNoDisponible? _errorGps;
  bool _buscandoGps = true;

  final _fotos = <FotoLocal>[];
  final _hallazgos = <HallazgoLocal>[];

  /// Los daños que ya traía el vehículo, para proponer un daño nuevo como «ya estaba registrado». Vacía mientras
  /// se cargan, y también cuando no hay copia guardada y no hay señal: entonces no se propone nada.
  var _previos = const <HallazgoPrevio>[];
  final _extras = <ExtraLocal>[];

  /// Solo en la segunda inspección del cargue: las unidades del viaje (null mientras no se conozcan), el lugar que
  /// cada una tiene y el que ya tenía el viaje al abrir esta pantalla.
  List<ViajeVehiculo>? _unidades;
  final _posiciones = <String, int>{};
  var _posicionesPrevias = const <String, int>{};
  bool _guardandoFoto = false;
  bool _cerrando = false;

  final _notas = TextEditingController();

  @override
  void initState() {
    super.initState();
    _buscarUbicacion();
    _cargarPrevios();
    final viajeId = widget.viajeId;
    if (_esAcomodacion && viajeId != null) {
      ref.listenManual(viajeDeEtapaProvider(viajeId), (_, viaje) => viaje.whenData(_alLlegarElViaje), fireImmediately: true);
    }
  }

  /// Trae los hallazgos previos del VIN. No bloquea la captura: si fallan, simplemente no se propone nada.
  Future<void> _cargarPrevios() async {
    final previos = await ref.read(hallazgosPreviosRepositorioProvider).de(widget.vin);
    if (mounted) setState(() => _previos = previos);
  }

  bool get _esAcomodacion => esSegundaInspeccionDelCargue(proceso: widget.proceso, momento: widget.momento);

  /// Las unidades del viaje y los lugares que ya tienen, la primera vez que el viaje llega. Si no se pueden
  /// traer, la sección lo dice y el cierre queda bloqueado: sin saber cuáles son no se puede comprobar que cada
  /// una tenga su lugar. Lo que ya se eligió no se pisa si el viaje vuelve a llegar.
  void _alLlegarElViaje(Viaje? viaje) {
    if (viaje == null || _unidades != null) return;
    setState(() {
      _unidades = viaje.vehiculos;
      _posicionesPrevias = posicionesDelViaje(viaje);
      _posiciones.addAll(_posicionesPrevias);
    });
  }

  @override
  void dispose() {
    _notas.dispose();
    super.dispose();
  }

  Future<void> _buscarUbicacion() async {
    setState(() {
      _buscandoGps = true;
      _errorGps = null;
    });
    try {
      final pos = await ref.read(servicioUbicacionProvider).obtenerActual();
      if (mounted) setState(() => _gps = pos);
    } on UbicacionNoDisponible catch (e) {
      if (mounted) setState(() => _errorGps = e);
    } finally {
      if (mounted) setState(() => _buscandoGps = false);
    }
  }

  /// Toma la foto de una vista del checklist.
  ///
  /// Cada foto lleva su PROPIO sello de fecha y ubicación (el de cuando se tomó), no el del cierre del informe.
  /// Repetir una vista no borra lo anterior: se agrega otra foto de la misma vista y la última es la que cuenta.
  Future<void> _tomarFoto(VistaChecklist vista) async {
    final temporal = await Navigator.of(context).push<String>(
      MaterialPageRoute(builder: (_) => CamaraPage(titulo: vista.nombre, guia: guiaDe(vista.codigo))),
    );
    if (temporal == null || !mounted) return;

    // Las adicionales exigen nota: sin ella el servidor responde 400 y el informe moriría al enviarse.
    String? nota;
    if (!vista.obligatoria) {
      nota = await pedirNotaDeFoto(context, vista: vista.nombre);
      if (nota == null || !mounted) return;
    }

    final foto = await _sellar(temporal, vistaId: vista.id, nota: nota);
    if (foto != null && mounted) setState(() => _fotos.add(foto));
  }

  /// Toma una foto de la acomodación: no cubre ninguna vista ni hallazgo, y cuenta para el mínimo del cierre.
  Future<void> _tomarFotoAcomodacion() async {
    final temporal = await Navigator.of(context).push<String>(
      MaterialPageRoute(builder: (_) => const CamaraPage(titulo: 'Foto de la acomodación')),
    );
    if (temporal == null || !mounted) return;
    final foto = await _sellar(temporal);
    if (foto != null && mounted) setState(() => _fotos.add(foto));
  }

  int get _fotosAcomodacionMinimas =>
      ref.read(entornoProvider).acomodacionFotosMinimas ?? acomodacionFotosMinimasPorDefecto;

  /// Guarda la foto y le pone su sello: fecha, ubicación, precisión y equipo.
  ///
  /// Devuelve null si no se pudo guardar. El sello es de cuando se tomó, y sin GPS **no se hereda** el del
  /// informe: la foto queda marcada «sin ubicación».
  Future<FotoLocal?> _sellar(String temporal, {String? vistaId, String? nota}) async {
    setState(() => _guardandoFoto = true);
    try {
      final foto = await sellarFoto(ref, temporal, vistaId: vistaId, nota: nota);
      if (foto == null && mounted) {
        ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('No se pudo guardar la foto. Inténtalo de nuevo.')));
      }
      return foto;
    } finally {
      if (mounted) setState(() => _guardandoFoto = false);
    }
  }

  // Quitar una foto ya no existe: una vista se repite, y repetirla conserva la anterior (la última cuenta). Lo
  // que se tomó en el patio no se borra desde aquí.

  /// Hay con que registrar una novedad. Si no, no se puede cerrar el informe: uno cerrado sin hallazgos dice
  /// que el vehiculo venia sano.
  /// El conductor no asigna el nivel de severidad: lo asigna el inspector (el servidor responde 400 si lo envía).
  bool get _esConductor => ref.read(authControllerProvider).sesion?.usuario.rol == 'conductor';

  /// Niveles que se ofrecen en la hoja del daño; vacío para el conductor.
  List<ValorCatalogo> _niveles(Catalogos catalogos) =>
      _esConductor ? const [] : catalogos.seleccionables(Catalogo.nivelesSeveridad);

  bool get _catalogosListos {
    final catalogos = ref.read(catalogosProvider);
    return catalogos.seleccionables(Catalogo.clasesDano).isNotEmpty &&
        catalogos.seleccionables(Catalogo.tamanosDano).isNotEmpty &&
        (_esConductor || catalogos.seleccionables(Catalogo.nivelesSeveridad).isNotEmpty);
  }

  int _indiceDe(ZonaDano zona) => _hallazgos.indexWhere((h) => h.clave == zona.clave);

  void _soltarZona() => ref.read(seleccionZonaProvider(_contextoDibujo).notifier).deseleccionar();

  /// Registra el daño de la zona elegida en el dibujo: clase, tamaño y nivel, y su foto.
  ///
  /// Un daño por zona: si la zona ya tiene uno, se abre ese registro en vez de crear otro.
  Future<void> _registrarDano(ZonaDano zona) async {
    if (_indiceDe(zona) >= 0) return _abrirDano(zona);
    final catalogos = ref.read(catalogosProvider);
    final clases = catalogos.seleccionables(Catalogo.clasesDano);
    final tamanos = catalogos.seleccionables(Catalogo.tamanosDano);
    final niveles = _niveles(catalogos);
    if (!_catalogosListos) {
      // Mejor avisar que abrir una hoja sin opciones: los catálogos se descargan al entrar, con señal.
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Los catálogos de daño aún no se han descargado. Conéctate e inténtalo de nuevo.')),
      );
      return;
    }

    final dano = await pedirDano(
      context,
      titulo: zona.nombreCompleto,
      subtitulo: zona.descripcion,
      clases: clases,
      tamanos: tamanos,
      niveles: niveles,
    );
    if (dano == null || !mounted) return;

    // ¿Es el mismo daño que ya traía el vehículo? Solo se pregunta si hay uno previo de esa zona y esa clase. El
    // conductor no responde esto: marcar un daño como preexistente es criterio de quien inspecciona, y la API
    // tampoco le deja ver los hallazgos previos.
    final previo = _esConductor ? null : previoDe(_previos, clave: zona.clave, clase: dano.clase);
    var esPreexistente = false;
    if (previo != null) {
      final respuesta = await _preguntarPreexistente(previo);
      if (respuesta == null || !mounted) return;
      esPreexistente = respuesta;
    }

    // Un daño sin foto es la palabra de alguien contra la del siguiente, y el servidor lo rechaza con 400. Se
    // pide aquí, con la novedad recién marcada, y si no se toma no se registra nada.
    final temporal = await Navigator.of(context).push<String>(
      MaterialPageRoute(builder: (_) => CamaraPage(titulo: 'Foto del daño: ${zona.nombreCompleto}')),
    );
    if (temporal == null || !mounted) return;
    final foto = await _sellar(temporal);
    if (foto == null || !mounted) return;

    setState(() => _hallazgos.add(
          HallazgoLocal(
            cuadranteNumero: zona.numero,
            lado: zona.lado,
            zona: zona.nombreCompleto,
            clase: dano.clase,
            tamano: dano.tamano,
            nivel: dano.nivel,
            nota: dano.nota,
            esPreexistente: esPreexistente,
            hallazgoOrigenId: esPreexistente ? previo!.id : null,
            foto: foto,
          ),
        ));
    _soltarZona();
  }

  /// Muestra el daño previo con su foto y deja decidir si es el mismo. Null si se sale sin responder.
  Future<bool?> _preguntarPreexistente(HallazgoPrevio previo) {
    return preguntarPreexistente(
      context,
      previo: previo,
      catalogos: ref.read(catalogosProvider),
      urlFoto: resolverUrlImagen(ref.read(entornoProvider).apiUrl, previo.fotoUrl),
      // Guardados para el patio, sus enlaces casi siempre llegan vencidos: se vuelven a pedir.
      alVencerFoto: _cargarPrevios,
      sinSenal: !ref.read(syncControllerProvider).online,
    );
  }

  /// El daño ya registrado en una zona: se corrige su clase, tamaño, nivel u observación, o se quita. La foto se
  /// conserva.
  Future<void> _abrirDano(ZonaDano zona) async {
    final i = _indiceDe(zona);
    if (i < 0) return;
    final catalogos = ref.read(catalogosProvider);
    final hallazgo = _hallazgos[i];
    final accion = await showModalBottomSheet<_AccionDano>(
      context: context,
      showDragHandle: true,
      builder: (contexto) => SafeArea(
        child: Padding(
          padding: const EdgeInsets.fromLTRB(Espacio.lg, 0, Espacio.lg, Espacio.lg),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Text(zona.nombreCompleto, style: Theme.of(contexto).textTheme.titleMedium),
              Text('${zona.descripcion}. Ya tiene un daño registrado.', style: Theme.of(contexto).textTheme.bodySmall),
              const SizedBox(height: Espacio.md),
              FilaHallazgo(
                hallazgo: hallazgo,
                clase: catalogos.etiqueta(Catalogo.clasesDano, hallazgo.clase),
                tamano: catalogos.etiqueta(Catalogo.tamanosDano, hallazgo.tamano),
                nivel: hallazgo.nivel == null ? null : catalogos.etiqueta(Catalogo.nivelesSeveridad, hallazgo.nivel!),
                alQuitar: () => Navigator.of(contexto).pop(_AccionDano.quitar),
              ),
              const SizedBox(height: Espacio.md),
              OutlinedButton(
                onPressed: () => Navigator.of(contexto).pop(_AccionDano.corregir),
                child: const Text('Cambiar clase, tamaño, nivel u observación'),
              ),
            ],
          ),
        ),
      ),
    );
    if (!mounted) return;
    if (accion == null) {
      _soltarZona();
      return;
    }
    switch (accion) {
      case _AccionDano.quitar:
        setState(() => _hallazgos.removeAt(i));
      case _AccionDano.corregir:
        final dano = await pedirDano(
          context,
          titulo: zona.nombreCompleto,
          subtitulo: zona.descripcion,
          clases: catalogos.seleccionables(Catalogo.clasesDano),
          tamanos: catalogos.seleccionables(Catalogo.tamanosDano),
          niveles: _niveles(catalogos),
          inicial: (clase: hallazgo.clase, tamano: hallazgo.tamano, nivel: hallazgo.nivel, nota: hallazgo.nota),
        );
        // Se busca de nuevo: mientras la hoja estaba abierta la lista pudo cambiar.
        final j = _indiceDe(zona);
        if (dano == null || !mounted || j < 0) break;
        setState(() => _hallazgos[j] = _hallazgos[j].copiar(
              clase: dano.clase,
              tamano: dano.tamano,
              nivel: () => dano.nivel,
              nota: () => dano.nota,
            ));
    }
    if (mounted) _soltarZona();
  }

  /// Agrega un extra (tipo y observación) y pide de una vez su primera foto: sin foto no se puede cerrar.
  Future<void> _agregarExtra() async {
    final datos = await pedirExtra(context, tipos: ref.read(catalogosProvider).seleccionables(Catalogo.tiposExtra));
    if (datos == null || !mounted) return;
    setState(() => _extras.add(ExtraLocal(
          tipo: datos.tipo,
          nombre: datos.nombre,
          textoOtro: datos.textoOtro,
          observacion: datos.observacion,
        )));
    await _tomarFotoExtra(_extras.length - 1);
  }

  Future<void> _tomarFotoExtra(int i) async {
    final extra = _extras[i];
    final temporal = await Navigator.of(context).push<String>(
      MaterialPageRoute(builder: (_) => CamaraPage(titulo: 'Foto del extra: ${extra.nombre}')),
    );
    if (temporal == null || !mounted) return;
    final foto = await _sellar(temporal);
    // Se busca de nuevo: mientras la cámara estaba abierta el extra pudo quitarse.
    final j = _extras.indexOf(extra);
    if (foto == null || !mounted || j < 0) return;
    setState(() => _extras[j] = _extras[j].conFoto(foto));
  }

  /// Las vistas que pide esta etapa. Transporte no pide ninguna.
  List<VistaChecklist> get _checklist =>
      pideChecklist(widget.proceso) && !_esAcomodacion ? checklistDeVistas(ref.read(catalogosProvider)) : const [];

  BloqueoCierre? get _bloqueo => bloqueoDeCierre(
        hayUbicacion: _gps != null,
        fotos: _fotos,
        hallazgos: _hallazgos,
        extras: _extras,
        checklist: _checklist,
        catalogosListos: _catalogosListos,
        acomodacion: _esAcomodacion
            ? Acomodacion(unidades: _unidades, posiciones: _posiciones, fotosMinimas: _fotosAcomodacionMinimas)
            : null,
      );

  Future<void> _cerrar() async {
    final gps = _gps;
    // El job se marca con quién lo encoló: la cola sobrevive al cierre de sesión y nadie debe enviar lo de otro.
    final usuarioId = ref.read(authControllerProvider).sesion?.usuario.id;
    if (gps == null || usuarioId == null || _bloqueo != null) return;
    setState(() => _cerrando = true);
    try {
      final ahora = DateTime.now();
      final job = construirInformeJob(
        id: const Uuid().v4(),
        creadoEn: ahora.millisecondsSinceEpoch,
        usuarioId: usuarioId,
        momento: widget.momento,
        vin: widget.vin,
        proceso: ref.read(catalogosProvider).etiqueta(Catalogo.etapas, widget.proceso),
        etapaId: widget.etapaId,
        fecha: ahora,
        lat: gps.lat,
        lng: gps.lng,
        notas: _notas.text,
        fotos: _fotos,
        hallazgos: _hallazgos,
        extras: _extras,
        viajeId: _esAcomodacion ? widget.viajeId : null,
        posiciones: _esAcomodacion ? {..._posiciones} : const {},
        posicionesPrevias: _esAcomodacion ? _posicionesPrevias : const {},
      );
      await ref.read(colaRepositorioProvider).agregar(job);
      final sync = ref.read(syncControllerProvider.notifier);
      await sync.refrescar();
      await sync.sincronizar();
      // Se mira ESTE job y no el contador de pendientes de la cola, que excluye los fallidos: con eso, un
      // informe que el servidor acababa de rechazar se anunciaba como «enviado y cerrado».
      final enCola = (await ref.read(colaRepositorioProvider).listar()).where((j) => j.id == job.id).firstOrNull;
      if (!mounted) return;
      // El color dice el desenlace de un vistazo, que es como se lee algo en el patio: verde si quedó hecho,
      // ámbar si falta que salga, rojo si lo rechazaron.
      final (titulo, mensaje, icono, color) = switch (enCola) {
        null => ('Informe enviado', 'Quedó cerrado en el servidor.', Icons.check_circle, Colores.exito),
        // No se arregla reintentando: quien está en el patio tiene que saberlo ahora, no descubrirlo después.
        final j when j.fallido => (
            'No se pudo enviar',
            'El servidor rechazó el informe${j.error == null ? '' : ': ${j.error}'}. Nada se perdió: quedó '
                'guardado en Inspecciones › Cola, para reintentarlo o descartarlo.',
            Icons.error_outline,
            Colores.peligro,
          ),
        _ => (
            'Informe guardado',
            'Quedó en cola; se enviará automáticamente cuando la señal sea estable.',
            Icons.schedule,
            Colores.advertencia,
          ),
      };
      await showDialog<void>(
        context: context,
        builder: (contexto) => AlertDialog(
          icon: Icon(icono, color: color, size: 40),
          title: Text(titulo, style: TextStyle(color: color)),
          content: Text(mensaje),
          actions: [TextButton(onPressed: () => Navigator.of(contexto).pop(), child: const Text('OK'))],
        ),
      );
      if (mounted) context.pop();
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
      if (mounted) setState(() => _cerrando = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context).textTheme;
    final bloqueo = _bloqueo;
    final catalogos = ref.watch(catalogosProvider);
    final descargandoCatalogos = ref.watch(catalogosControllerProvider).fase == FaseCatalogos.cargando;
    final checklist = _checklist;
    return Scaffold(
      // `proceso` es el código (con él se decide el checklist); lo que se lee sale del catálogo, y si todavía
      // no está descargado cae al propio código.
      appBar: AppBar(
        title: Text('${etiquetar(etiquetaMomento, widget.momento)} · ${catalogos.etiqueta(Catalogo.etapas, widget.proceso)}'),
      ),
      body: SafeArea(
        child: Stack(
          children: [
            ListView(
              keyboardDismissBehavior: ScrollViewKeyboardDismissBehavior.onDrag,
              padding: const EdgeInsets.all(Espacio.lg),
              children: [
                Text(widget.vin, style: tema.bodySmall?.copyWith(fontFamily: 'monospace')),
                const SizedBox(height: Espacio.md),
                Text('Datos del transporte', style: tema.titleMedium),
                const SizedBox(height: Espacio.md),
                // Ya no se escriben: salen del viaje de la etapa y el servidor los toma de ahí.
                _DatosDelViaje(viajeId: widget.viajeId),
                const SizedBox(height: Espacio.md),
                _FilaUbicacion(gps: _gps, error: _errorGps, buscando: _buscandoGps, alReintentar: _buscarUbicacion, alAbrirAjustes: ref.read(servicioUbicacionProvider).abrirAjustes),
                const SizedBox(height: Espacio.lg),
                Text('Novedades', style: tema.titleMedium),
                Text('Toca el cuadrante donde está el daño. Si no hay novedades, puedes cerrar sin fotos.', style: tema.bodySmall),
                const SizedBox(height: Espacio.md),
                SelectorZonaDano(
                  contexto: _contextoDibujo,
                  danos: {for (final h in _hallazgos) ?h.clave: h.nivel},
                  alRegistrar: _registrarDano,
                  alAbrirDano: _abrirDano,
                ),
                if (_hallazgos.isNotEmpty) ...[
                  const SizedBox(height: Espacio.md),
                  for (var i = 0; i < _hallazgos.length; i++)
                    FilaHallazgo(
                      hallazgo: _hallazgos[i],
                      clase: catalogos.etiqueta(Catalogo.clasesDano, _hallazgos[i].clase),
                      tamano: catalogos.etiqueta(Catalogo.tamanosDano, _hallazgos[i].tamano),
                      nivel: switch (_hallazgos[i].nivel) {
                        final n? => catalogos.etiqueta(Catalogo.nivelesSeveridad, n),
                        null => null,
                      },
                      alQuitar: () => setState(() => _hallazgos.removeAt(i)),
                    ),
                ],
                if (ref.watch(entornoProvider).extrasHabilitados) ...[
                  const SizedBox(height: Espacio.lg),
                  SeccionExtras(
                    extras: _extras,
                    alAgregar: _guardandoFoto ? null : _agregarExtra,
                    alTomarFoto: _guardandoFoto ? null : _tomarFotoExtra,
                    alQuitar: (i) => setState(() => _extras.removeAt(i)),
                  ),
                ],
                const SizedBox(height: Espacio.lg),
                if (_esAcomodacion)
                  SeccionAcomodacion(
                    unidades: _unidades,
                    posiciones: _posiciones,
                    fotos: _fotos,
                    fotosMinimas: _fotosAcomodacionMinimas,
                    alElegir: (vin, posicion) => setState(() {
                      if (posicion == null) {
                        _posiciones.remove(vin);
                      } else {
                        _posiciones[vin] = posicion;
                      }
                    }),
                    alTomarFoto: _guardandoFoto ? null : _tomarFotoAcomodacion,
                    alReintentar: () => ref.invalidate(viajeDeEtapaProvider(widget.viajeId!)),
                    precisionBaja: ref.watch(entornoProvider).precisionBajaMetros,
                  )
                else if (checklist.isEmpty)
                  Text(
                    'Esta etapa no pide fotos por vista; basta la ubicación del registro.',
                    style: tema.bodySmall,
                  )
                else ...[
                  Text('Fotos de la inspección', style: tema.titleMedium),
                  Text(
                    'Las obligatorias se piden siempre. Las adicionales, solo si hay faltante o daño, y con su nota.',
                    style: tema.bodySmall,
                  ),
                  const SizedBox(height: Espacio.md),
                  for (final vista in checklist) ...[
                    FilaVista(
                      vista: vista,
                      foto: _fotos.ultimaDeLaVista(vista.id),
                      alTomar: _guardandoFoto ? null : () => _tomarFoto(vista),
                      precisionBaja: ref.watch(entornoProvider).precisionBajaMetros,
                    ),
                    const SizedBox(height: Espacio.sm),
                  ],
                ],
                const SizedBox(height: Espacio.lg),
                TextField(
                  controller: _notas,
                  maxLines: 4,
                  maxLength: 1000,
                  decoration: const InputDecoration(labelText: 'Notas adicionales (opcional)', hintText: 'Detalles del hallazgo u observaciones', alignLabelWithHint: true),
                ),
                const SizedBox(height: Espacio.md),
                FilledButton(
                  onPressed: bloqueo == null && !_cerrando ? _cerrar : null,
                  child: _cerrando ? const SizedBox(width: 22, height: 22, child: CircularProgressIndicator(strokeWidth: 2.5, color: Colors.white)) : const Text('Cerrar informe'),
                ),
                if (bloqueo != null && !_cerrando) ...[
                  Padding(padding: const EdgeInsets.only(top: Espacio.sm), child: Text(bloqueo.mensaje, textAlign: TextAlign.center, style: tema.bodySmall)),
                  if (bloqueo is SinCatalogos)
                    TextButton(
                      // Mientras hay una descarga en curso el botón lo dice, para no pulsarlo a ciegas.
                      onPressed: descargandoCatalogos
                          ? null
                          : () => ref.read(catalogosControllerProvider.notifier).reintentar(),
                      child: Text(descargandoCatalogos ? 'Descargando…' : 'Reintentar descarga'),
                    ),
                ],
              ],
            ),
            if (_guardandoFoto)
              Positioned.fill(
                child: Container(
                  color: Colors.white70,
                  child: const Center(child: Column(mainAxisSize: MainAxisSize.min, children: [CircularProgressIndicator(), SizedBox(height: Espacio.sm), Text('Guardando foto…')])),
                ),
              ),
          ],
        ),
      ),
    );
  }
}

/// Placa, tráiler, unidad y conductor del viaje, en solo lectura.
///
/// Antes se escribían a mano en cada informe. Ahora salen del viaje —el servidor los toma de ahí y ya no los
/// acepta en el cuerpo—, así que aquí solo se muestran para que quien captura confirme que es el viaje correcto.
class _DatosDelViaje extends ConsumerWidget {
  const _DatosDelViaje({required this.viajeId});

  final String? viajeId;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final tema = Theme.of(context).textTheme;
    if (viajeId == null) {
      return Tarjeta(child: Text('Esta etapa no tiene viaje asociado.', style: tema.bodySmall));
    }

    return ref.watch(viajeDeEtapaProvider(viajeId!)).when(
          loading: () => Tarjeta(child: Text('Cargando los datos del viaje…', style: tema.bodySmall)),
          // No impide capturar: el servidor igual toma los datos del viaje al recibir el informe.
          error: (_, _) => Tarjeta(child: Text('No se pudieron cargar los datos del viaje.', style: tema.bodySmall)),
          data: (viaje) => viaje == null
              ? Tarjeta(child: Text('No se pudieron cargar los datos del viaje.', style: tema.bodySmall))
              : Tarjeta(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      for (final (etiqueta, valor) in [
                        ('Placa', viaje.placa),
                        ('Tráiler', viaje.trailer ?? '—'),
                        ('Unidad', viaje.unidad ?? '—'),
                        ('Conductor', viaje.conductorNombre ?? '—'),
                      ])
                        Padding(
                          padding: const EdgeInsets.only(bottom: Espacio.sm),
                          child: Row(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: [
                              SizedBox(width: 96, child: Text(etiqueta, style: tema.bodySmall)),
                              // La placa en monoespaciada, como el VIN: son códigos que se leen carácter a
                              // carácter y se comparan contra algo que se tiene delante.
                              Expanded(
                                child: etiqueta == 'Placa'
                                    ? TextoMono(valor, estilo: tema.bodySmall)
                                    : Text(valor, style: tema.bodySmall?.copyWith(color: Colores.textoPrimario)),
                              ),
                            ],
                          ),
                        ),
                    ],
                  ),
                ),
        );
  }
}

class _FilaUbicacion extends StatelessWidget {
  const _FilaUbicacion({required this.gps, required this.error, required this.buscando, required this.alReintentar, required this.alAbrirAjustes});

  final Posicion? gps;
  final UbicacionNoDisponible? error;
  final bool buscando;
  final VoidCallback alReintentar;
  final Future<void> Function() alAbrirAjustes;

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context).textTheme;
    if (gps != null) {
      return Row(children: [
        const Icon(Icons.place_outlined, size: 18, color: Colores.exito),
        const SizedBox(width: Espacio.sm),
        Text('Ubicación: ${gps!.lat.toStringAsFixed(5)}, ${gps!.lng.toStringAsFixed(5)}', style: tema.bodySmall),
      ]);
    }
    if (buscando) {
      return Row(children: [
        const SizedBox(width: 18, height: 18, child: CircularProgressIndicator(strokeWidth: 2)),
        const SizedBox(width: Espacio.sm),
        Text('Obteniendo ubicación…', style: tema.bodySmall),
      ]);
    }
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Row(children: [
          const Icon(Icons.location_off_outlined, size: 18, color: Colores.advertencia),
          const SizedBox(width: Espacio.sm),
          Expanded(child: Text(error?.motivo ?? 'No se pudo obtener la ubicación.', style: tema.bodySmall)),
        ]),
        Row(children: [
          TextButton(onPressed: alReintentar, child: const Text('Reintentar')),
          if (error?.permanente ?? false) TextButton(onPressed: alAbrirAjustes, child: const Text('Abrir ajustes')),
        ]),
      ],
    );
  }
}
