import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:uuid/uuid.dart';

import '../../../app/proveedores.dart';
import '../../../core/dispositivo/servicios.dart';
import '../../../core/errores/error_api.dart';
import '../../../core/tema/tema.dart';
import '../../../core/ui/widgets.dart';
import '../../auth/presentation/auth_controller.dart';
import '../../sync/presentation/sync_controller.dart';
import '../../sync/providers.dart';
import '../../catalogos/domain/catalogos.dart';
import '../../catalogos/presentation/catalogos_controller.dart';
import '../../vehiculos/domain/coords_diagrama.dart';
import '../../vehiculos/domain/etiquetas.dart';
import '../../vehiculos/presentation/diagrama_vehiculo.dart';
import '../../viajes/providers.dart';
import '../domain/checklist_vistas.dart';
import '../domain/foto_local.dart';
import '../domain/hallazgo_local.dart';
import '../domain/reglas_informe.dart';
import 'camara_page.dart';
import 'filas_captura.dart';
import 'nota_foto.dart';
import 'selector_dano.dart';

/// Captura de un informe: datos del transporte, novedades sobre el diagrama, fotos selladas y notas.
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
  bool _guardandoFoto = false;
  bool _cerrando = false;

  final _notas = TextEditingController();

  @override
  void initState() {
    super.initState();
    _buscarUbicacion();
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

  /// Guarda la foto y le pone su sello: fecha, ubicación, precisión y equipo.
  ///
  /// Devuelve null si no se pudo guardar. El sello es de cuando se tomó, y sin GPS **no se hereda** el del
  /// informe: la foto queda marcada «sin ubicación».
  Future<FotoLocal?> _sellar(String temporal, {String? vistaId, String? nota}) async {
    setState(() => _guardandoFoto = true);
    try {
      final ruta = await ref.read(servicioFotosProvider).guardar(temporal);
      final pos = await ref.read(servicioUbicacionProvider).obtenerRapida();
      final dispositivo = await ref.read(servicioDispositivoProvider).id();
      return FotoLocal(
        ruta: ruta,
        selloFecha: DateTime.now().toUtc().toIso8601String(),
        selloLat: pos?.lat,
        selloLng: pos?.lng,
        vistaId: vistaId,
        nota: nota,
        precision: pos?.precision,
        dispositivoId: dispositivo,
      );
    } catch (_) {
      if (mounted) ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('No se pudo guardar la foto. Inténtalo de nuevo.')));
      return null;
    } finally {
      if (mounted) setState(() => _guardandoFoto = false);
    }
  }

  // Quitar una foto ya no existe: una vista se repite, y repetirla conserva la anterior (la última cuenta). Lo
  // que se tomó en el patio no se borra desde aquí.

  /// Hay con que registrar una novedad. Si no, no se puede cerrar el informe: uno cerrado sin hallazgos dice
  /// que el vehiculo venia sano.
  bool get _catalogosListos {
    final catalogos = ref.read(catalogosProvider);
    return catalogos.seleccionables(Catalogo.clasesDano).isNotEmpty && catalogos.seleccionables(Catalogo.tamanosDano).isNotEmpty;
  }

  Future<void> _elegirNovedad(String etiqueta, CoordsDiagrama coords) async {
    final catalogos = ref.read(catalogosProvider);
    final clases = catalogos.seleccionables(Catalogo.clasesDano);
    final tamanos = catalogos.seleccionables(Catalogo.tamanosDano);
    if (clases.isEmpty || tamanos.isEmpty) {
      // Mejor avisar que abrir una hoja sin opciones: los catálogos se descargan al entrar, con señal.
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Los catálogos de daño aún no se han descargado. Conéctate e inténtalo de nuevo.')),
      );
      return;
    }

    final dano = await pedirDano(context, titulo: etiqueta, clases: clases, tamanos: tamanos);
    if (dano == null || !mounted) return;

    // Un daño sin foto es la palabra de alguien contra la del siguiente, y el servidor lo rechaza con 400. Se
    // pide aquí, con la novedad recién marcada, y si no se toma no se registra nada.
    final temporal = await Navigator.of(context).push<String>(
      MaterialPageRoute(builder: (_) => CamaraPage(titulo: 'Foto de la novedad: $etiqueta')),
    );
    if (temporal == null || !mounted) return;
    final foto = await _sellar(temporal);
    if (foto == null || !mounted) return;

    setState(() => _hallazgos.add(
          HallazgoLocal(parte: etiqueta, clase: dano.clase, tamano: dano.tamano, coords: coords, foto: foto),
        ));
  }

  /// Las vistas que pide esta etapa. Transporte no pide ninguna.
  List<VistaChecklist> get _checklist =>
      pideChecklist(widget.proceso) ? checklistDeVistas(ref.read(catalogosProvider)) : const [];

  BloqueoCierre? get _bloqueo => bloqueoDeCierre(
        hayUbicacion: _gps != null,
        fotos: _fotos,
        hallazgos: _hallazgos,
        checklist: _checklist,
        catalogosListos: _catalogosListos,
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
                Text('Toca una parte para registrar un daño. Si no hay novedades, puedes cerrar sin fotos.', style: tema.bodySmall),
                const SizedBox(height: Espacio.md),
                DiagramaVehiculo(marcas: [for (final h in _hallazgos) if (h.coords != null) h.coords!], onElegir: _elegirNovedad),
                if (_hallazgos.isNotEmpty) ...[
                  const SizedBox(height: Espacio.md),
                  for (var i = 0; i < _hallazgos.length; i++)
                    FilaHallazgo(
                      hallazgo: _hallazgos[i],
                      clase: catalogos.etiqueta(Catalogo.clasesDano, _hallazgos[i].clase),
                      tamano: catalogos.etiqueta(Catalogo.tamanosDano, _hallazgos[i].tamano),
                      alQuitar: () => setState(() => _hallazgos.removeAt(i)),
                    ),
                ],
                const SizedBox(height: Espacio.lg),
                if (checklist.isEmpty)
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
