import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:uuid/uuid.dart';

import '../../../app/proveedores.dart';
import '../../../core/dispositivo/servicios.dart';
import '../../../core/errores/error_api.dart';
import '../../../core/tema/tema.dart';
import '../../../core/ui/widgets.dart';
import '../../auth/domain/sesion.dart';
import '../../auth/presentation/auth_controller.dart';
import '../../inspecciones/domain/foto_local.dart';
import '../../inspecciones/presentation/camara_page.dart';
import '../../inspecciones/presentation/sellado.dart';
import '../../sync/domain/job.dart';
import '../../sync/presentation/anuncio_desenlace.dart';
import '../../sync/presentation/sync_controller.dart';
import '../../sync/providers.dart';
import '../../vehiculos/domain/etiquetas.dart';
import '../../viajes/domain/viaje.dart';
import '../domain/firma_ninera.dart';
import '../providers.dart';

/// Quién puede ser testigo de la constancia de la niñera: cualquiera menos el inspector y el conductor del viaje.
final _testigosDeLaNineraProvider =
    FutureProvider.autoDispose.family<List<Usuario>, ({String? inspectorId, String? conductorId})>((ref, partes) async {
  final todos = await ref.watch(usuariosRepositorioProvider).listar();
  return [for (final u in todos) if (u.id != partes.inspectorId && u.id != partes.conductorId) u];
});

/// La constancia de no firma de toda la niñera (S8-HU-01.3 CA-14).
///
/// La deja la parte que sí está, con el motivo, un testigo y una foto, y cierra la entrega del cargue de cada
/// carro activo: no hay constancia para solo algunos carros, para eso están las reservas.
class NoFirmaNineraPage extends ConsumerStatefulWidget {
  const NoFirmaNineraPage({
    super.key,
    required this.viaje,
    required this.parteQueSeNego,
    @visibleForTesting this.fotoDePrueba,
  });

  final Viaje viaje;

  /// `entrega` o `recibe`: la que se negó.
  final String parteQueSeNego;

  /// Solo para pruebas: la foto ya tomada. La cámara no se abre en una prueba de widget, y sin foto no se llega a
  /// registrar la constancia ni a ver qué dice su envío.
  final FotoLocal? fotoDePrueba;

  @override
  ConsumerState<NoFirmaNineraPage> createState() => _EstadoNoFirmaNinera();
}

class _EstadoNoFirmaNinera extends ConsumerState<NoFirmaNineraPage> {
  final _motivo = TextEditingController();
  String? _testigoId;
  FotoLocal? _foto;
  bool _guardando = false;

  @override
  void initState() {
    super.initState();
    _foto = widget.fotoDePrueba;
  }

  @override
  void dispose() {
    _motivo.dispose();
    super.dispose();
  }

  String get _quienSeNego => widget.parteQueSeNego == ParteTransferencia.recibe
      ? (widget.viaje.conductorNombre ?? 'El conductor')
      : (widget.viaje.inspectorNombre ?? 'El inspector');

  /// Lo que falta para registrar, dicho por su nombre.
  List<String> get _faltan => [
        if (_motivo.text.trim().isEmpty) 'el motivo',
        if (_testigoId == null) 'el testigo',
        if (_foto == null) 'la foto',
      ];

  Future<void> _tomarFoto() async {
    final temporal = await Navigator.of(context).push<String>(
      MaterialPageRoute(builder: (_) => const CamaraPage(titulo: 'Foto de la constancia')),
    );
    if (temporal == null || !mounted) return;
    final foto = await sellarFoto(ref, temporal);
    if (!mounted) return;
    if (foto == null) {
      ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('No se pudo guardar la foto. Inténtalo de nuevo.')));
      return;
    }
    setState(() => _foto = foto);
  }

  Future<void> _registrar() async {
    final usuarioId = ref.read(authControllerProvider).sesion?.usuario.id;
    final foto = _foto;
    if (_faltan.isNotEmpty || usuarioId == null || foto == null || _guardando) return;
    setState(() => _guardando = true);
    final mensajes = ScaffoldMessenger.of(context);
    try {
      final momento = DateTime.now();
      final Posicion posicion;
      try {
        posicion = await ref.read(servicioUbicacionProvider).obtenerActual();
      } catch (_) {
        mensajes.showSnackBar(
          const SnackBar(content: Text('Sin ubicación no se registra la constancia: activa el GPS e inténtalo de nuevo.')),
        );
        return;
      }
      final jobId = const Uuid().v4();
      await ref.read(colaRepositorioProvider).agregar(NoFirmaNineraJob(
            id: jobId,
            etiqueta: 'Constancia de la niñera ${widget.viaje.codigoMtrack}',
            creadoEn: momento.millisecondsSinceEpoch,
            usuarioId: usuarioId,
            viajeId: widget.viaje.id,
            parte: widget.parteQueSeNego,
            motivo: _motivo.text.trim(),
            testigoId: _testigoId!,
            foto: foto,
            fechaMomento: isoConZona(momento),
            lat: posicion.lat,
            lng: posicion.lng,
            precisionM: posicion.precision,
          ));
      final sync = ref.read(syncControllerProvider.notifier);
      await sync.refrescar();
      await sync.sincronizar();
      if (!mounted) return;
      final carros = carrosDeLaFirma(widget.viaje).length;
      await anunciarDesenlace(context, ref, jobId, (
        registrado: 'Constancia de la niñera registrada',
        detalleRegistrado: 'La entrega del cargue de ${carros == 1 ? 'el carro' : 'los $carros carros'} quedó cerrada con la constancia.',
        noRegistrado: 'No se pudo registrar la constancia',
      ));
      if (mounted) Navigator.of(context).pop();
    } catch (e) {
      mensajes.showSnackBar(SnackBar(content: Text(mensajeDeError(e, 'No se pudo guardar la constancia. Inténtalo de nuevo.'))));
    } finally {
      if (mounted) setState(() => _guardando = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context).textTheme;
    final testigos = ref.watch(
      _testigosDeLaNineraProvider((inspectorId: widget.viaje.inspectorId, conductorId: widget.viaje.conductorId)),
    );
    final faltan = _faltan;

    return Scaffold(
      appBar: AppBar(title: const Text('Constancia de no firma')),
      body: SafeArea(
        child: ListView(
          padding: const EdgeInsets.all(Espacio.lg),
          children: [
            Text(
              '$_quienSeNego se negó a firmar. La constancia es para toda la niñera: cierra la entrega del cargue de '
              'todos sus carros. Si el desacuerdo es solo con algunos carros, usa las reservas.',
              style: tema.bodyMedium,
            ),
            const SizedBox(height: Espacio.lg),
            TextField(
              controller: _motivo,
              onChanged: (_) => setState(() {}),
              minLines: 2,
              maxLines: 4,
              textCapitalization: TextCapitalization.sentences,
              decoration: const InputDecoration(labelText: 'Motivo', hintText: 'Qué dijo o qué pasó'),
            ),
            const SizedBox(height: Espacio.lg),
            Text('Testigo', style: tema.titleMedium?.copyWith(fontSize: 15)),
            Text('Una tercera persona: ni el inspector ni el conductor del viaje.', style: tema.bodySmall),
            const SizedBox(height: Espacio.sm),
            testigos.when(
              loading: () => const EstadoCargando(),
              error: (error, _) => Text(mensajeDeError(error), style: tema.bodySmall?.copyWith(color: Colores.peligro)),
              data: (lista) => lista.isEmpty
                  ? Text('No hay nadie más registrado que pueda atestiguar.', style: tema.bodySmall?.copyWith(color: Colores.peligro))
                  : Wrap(
                      spacing: Espacio.sm,
                      runSpacing: Espacio.xs,
                      children: [
                        for (final u in lista)
                          ChoiceChip(
                            label: Text(u.nombre),
                            tooltip: etiquetar(etiquetaRol, u.rol),
                            selected: _testigoId == u.id,
                            materialTapTargetSize: MaterialTapTargetSize.padded,
                            onSelected: (_) => setState(() => _testigoId = u.id),
                          ),
                      ],
                    ),
            ),
            const SizedBox(height: Espacio.lg),
            Text('Foto', style: tema.titleMedium?.copyWith(fontSize: 15)),
            Text('De la niñera y de quien no firma, como quede el momento. Una sola sirve para todos los carros.', style: tema.bodySmall),
            const SizedBox(height: Espacio.sm),
            Row(
              children: [
                if (_foto case final foto?)
                  Padding(
                    padding: const EdgeInsets.only(right: Espacio.sm),
                    child: ClipRRect(
                      borderRadius: BorderRadius.circular(6),
                      child: Image.file(File(foto.ruta), width: 56, height: 48, fit: BoxFit.cover, semanticLabel: 'Foto de la constancia'),
                    ),
                  ),
                BotonEnFila(texto: _foto == null ? 'Tomar foto' : 'Repetir', onPressed: _tomarFoto),
              ],
            ),
            const SizedBox(height: Espacio.lg),
            if (faltan.isNotEmpty)
              Semantics(
                liveRegion: true,
                child: Text('Falta ${faltan.join(', ')}.', style: tema.bodySmall?.copyWith(color: Colores.peligro)),
              ),
            const SizedBox(height: Espacio.sm),
            FilledButton(
              onPressed: faltan.isEmpty && !_guardando ? _registrar : null,
              child: _guardando
                  ? const SizedBox(width: 22, height: 22, child: CircularProgressIndicator(strokeWidth: 2.5, color: Colors.white))
                  : const Text('Registrar constancia de la niñera'),
            ),
          ],
        ),
      ),
    );
  }
}
