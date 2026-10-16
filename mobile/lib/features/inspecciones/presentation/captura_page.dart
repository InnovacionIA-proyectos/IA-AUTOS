import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:uuid/uuid.dart';

import '../../../app/proveedores.dart';
import '../../../core/dispositivo/servicios.dart';
import '../../../core/errores/error_api.dart';
import '../../../core/tema/tema.dart';
import '../../sync/presentation/sync_controller.dart';
import '../../vehiculos/domain/coords_diagrama.dart';
import '../../vehiculos/domain/etiquetas.dart';
import '../../vehiculos/presentation/diagrama_vehiculo.dart';
import '../domain/foto_local.dart';
import '../domain/hallazgo_local.dart';
import '../domain/reglas_informe.dart';
import 'camara_page.dart';
import 'selector_dano.dart';

/// Captura de un informe: datos del transporte, novedades sobre el diagrama, fotos selladas y notas.
///
/// Funciona sin señal: al cerrar, el informe completo se guarda en la cola persistente y se envía cuando la conexión
/// sea estable.
class CapturaPage extends ConsumerStatefulWidget {
  const CapturaPage({super.key, required this.vin, required this.etapaId, required this.proceso});

  final String vin;
  final String etapaId;
  final String proceso;

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

  final _conductor = TextEditingController();
  final _placa = TextEditingController();
  final _trailer = TextEditingController();
  final _notas = TextEditingController();

  @override
  void initState() {
    super.initState();
    _buscarUbicacion();
  }

  @override
  void dispose() {
    for (final c in [_conductor, _placa, _trailer, _notas]) {
      c.dispose();
    }
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

  /// Cada foto lleva su PROPIO sello de fecha y ubicación (el de cuando se tomó), no el del cierre del informe.
  Future<void> _agregarFoto() async {
    final temporal = await Navigator.of(context).push<String>(MaterialPageRoute(builder: (_) => const CamaraPage()));
    if (temporal == null || !mounted) return;
    setState(() => _guardandoFoto = true);
    try {
      final ruta = await ref.read(servicioFotosProvider).guardar(temporal);
      final pos = await ref.read(servicioUbicacionProvider).obtenerRapida() ?? _gps;
      if (!mounted) return;
      setState(() => _fotos.add(FotoLocal(ruta: ruta, selloFecha: DateTime.now().toUtc().toIso8601String(), selloLat: pos?.lat, selloLng: pos?.lng)));
    } catch (_) {
      if (mounted) ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('No se pudo guardar la foto. Inténtalo de nuevo.')));
    } finally {
      if (mounted) setState(() => _guardandoFoto = false);
    }
  }

  Future<void> _quitarFoto(int indice) async {
    final foto = _fotos[indice];
    setState(() => _fotos.removeAt(indice));
    try {
      await File(foto.ruta).delete();
    } catch (_) {
      // Un archivo huérfano no afecta al informe.
    }
  }

  Future<void> _elegirNovedad(String etiqueta, CoordsDiagrama coords) async {
    final dano = await pedirDano(context, titulo: etiqueta);
    if (dano == null || !mounted) return;
    setState(() => _hallazgos.add(HallazgoLocal(parte: etiqueta, clase: dano.clase, tamano: dano.tamano, coords: coords)));
  }

  BloqueoCierre? get _bloqueo => bloqueoDeCierre(
        hayUbicacion: _gps != null,
        conductor: _conductor.text,
        placa: _placa.text,
        cantidadHallazgos: _hallazgos.length,
        cantidadFotos: _fotos.length,
      );

  Future<void> _cerrar() async {
    final gps = _gps;
    if (gps == null || _bloqueo != null) return;
    setState(() => _cerrando = true);
    try {
      final ahora = DateTime.now();
      final job = construirInformeJob(
        id: const Uuid().v4(),
        creadoEn: ahora.millisecondsSinceEpoch,
        vin: widget.vin,
        proceso: widget.proceso,
        etapaId: widget.etapaId,
        fecha: ahora,
        lat: gps.lat,
        lng: gps.lng,
        conductor: _conductor.text,
        placa: _placa.text,
        trailer: _trailer.text,
        notas: _notas.text,
        fotos: _fotos,
        hallazgos: _hallazgos,
      );
      await ref.read(colaRepositorioProvider).agregar(job);
      final sync = ref.read(syncControllerProvider.notifier);
      await sync.refrescar();
      await sync.sincronizar();
      if (!mounted) return;
      final enCola = ref.read(syncControllerProvider).pendientes > 0;
      await showDialog<void>(
        context: context,
        builder: (contexto) => AlertDialog(
          title: const Text('Informe guardado'),
          content: Text(enCola ? 'Quedó en cola; se enviará automáticamente cuando la señal sea estable.' : 'Informe enviado y cerrado.'),
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

    return Scaffold(
      appBar: AppBar(title: Text('Informe · ${widget.proceso}')),
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
                TextField(
                  controller: _conductor,
                  textCapitalization: TextCapitalization.words,
                  decoration: const InputDecoration(labelText: 'Conductor', hintText: 'Nombre de quien realiza el transporte'),
                  onChanged: (_) => setState(() {}),
                ),
                const SizedBox(height: Espacio.md),
                TextField(
                  controller: _placa,
                  textCapitalization: TextCapitalization.characters,
                  autocorrect: false,
                  decoration: const InputDecoration(labelText: 'Placa', hintText: 'Placa de la niñera'),
                  onChanged: (_) => setState(() {}),
                ),
                const SizedBox(height: Espacio.md),
                TextField(
                  controller: _trailer,
                  textCapitalization: TextCapitalization.characters,
                  autocorrect: false,
                  decoration: const InputDecoration(labelText: 'Trailer (opcional)', hintText: 'Identificación del trailer'),
                ),
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
                    Row(
                      children: [
                        Expanded(
                          child: Text(
                            '• ${_hallazgos[i].parte}: ${etiquetar(etiquetaClaseDano, _hallazgos[i].clase)} (${etiquetar(etiquetaTamanoDano, _hallazgos[i].tamano)})',
                            style: tema.bodySmall?.copyWith(color: Colores.textoPrimario),
                          ),
                        ),
                        TextButton(
                          onPressed: () => setState(() => _hallazgos.removeAt(i)),
                          child: Semantics(label: 'Quitar hallazgo ${_hallazgos[i].parte}', child: const Text('Quitar', style: TextStyle(color: Colores.peligro))),
                        ),
                      ],
                    ),
                ],
                const SizedBox(height: Espacio.lg),
                Text('Fotos ${_hallazgos.isNotEmpty ? '(obligatorias por la novedad)' : '(opcionales)'}', style: tema.titleMedium),
                Text('Puedes tomar todas las que necesites; una novedad puede requerir varias.', style: tema.bodySmall),
                const SizedBox(height: Espacio.md),
                Wrap(
                  spacing: Espacio.sm,
                  runSpacing: Espacio.sm,
                  children: [
                    for (var i = 0; i < _fotos.length; i++)
                      Stack(
                        clipBehavior: Clip.none,
                        children: [
                          ClipRRect(borderRadius: BorderRadius.circular(10), child: Image.file(File(_fotos[i].ruta), width: 92, height: 80, fit: BoxFit.cover, semanticLabel: 'Foto ${i + 1}')),
                          Positioned(
                            top: -6,
                            right: -6,
                            child: Semantics(
                              button: true,
                              label: 'Quitar foto ${i + 1}',
                              child: GestureDetector(
                                onTap: () => _quitarFoto(i),
                                child: Container(
                                  width: 26,
                                  height: 26,
                                  decoration: const BoxDecoration(color: Colores.peligro, shape: BoxShape.circle),
                                  child: const Icon(Icons.close, size: 16, color: Colors.white),
                                ),
                              ),
                            ),
                          ),
                        ],
                      ),
                    Semantics(
                      button: true,
                      label: 'Agregar foto',
                      child: InkWell(
                        onTap: _guardandoFoto ? null : _agregarFoto,
                        borderRadius: BorderRadius.circular(10),
                        child: Container(
                          width: 92,
                          height: 80,
                          decoration: BoxDecoration(
                            color: Colores.superficie,
                            borderRadius: BorderRadius.circular(10),
                            border: Border.all(color: Colores.primario),
                          ),
                          child: const Column(
                            mainAxisAlignment: MainAxisAlignment.center,
                            children: [Icon(Icons.photo_camera_outlined, color: Colores.primario), Text('Agregar', style: TextStyle(color: Colores.primario, fontSize: 13))],
                          ),
                        ),
                      ),
                    ),
                  ],
                ),
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
                if (bloqueo != null && !_cerrando)
                  Padding(padding: const EdgeInsets.only(top: Espacio.sm), child: Text(bloqueo.mensaje, textAlign: TextAlign.center, style: tema.bodySmall)),
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
