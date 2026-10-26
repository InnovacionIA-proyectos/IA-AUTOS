import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:uuid/uuid.dart';

import '../../../core/errores/error_api.dart';
import '../../../core/tema/tema.dart';
import '../../../core/ui/widgets.dart';
import '../../auth/domain/sesion.dart';
import '../../auth/presentation/auth_controller.dart';
import '../../inspecciones/domain/foto_local.dart';
import '../../inspecciones/presentation/camara_page.dart';
import '../../inspecciones/presentation/sellado.dart';
import '../../sync/domain/job.dart';
import '../../sync/presentation/sync_controller.dart';
import '../../sync/providers.dart';
import '../../vehiculos/domain/etiquetas.dart';
import '../providers.dart';

/// Quién puede ser testigo: cualquiera menos las dos partes. El servidor lo exige así, porque un testigo que
/// es una de las partes no atestigua nada.
final _testigosProvider = FutureProvider.autoDispose.family<List<Usuario>, ({String entregaId, String recibeId})>(
  (ref, partes) async {
    final todos = await ref.watch(usuariosRepositorioProvider).listar();
    return [for (final u in todos) if (u.id != partes.entregaId && u.id != partes.recibeId) u];
  },
);

/// Constancia de que la otra parte no firmó.
///
/// La registra quien sí está: por eso la parte que se negó no se elige, se deduce. Lo que hay que aportar es
/// el motivo, un testigo y una foto, y sin las tres no hay constancia.
class NoFirmaPage extends ConsumerStatefulWidget {
  const NoFirmaPage({
    super.key,
    required this.vin,
    required this.inspeccionId,
    required this.proceso,
    required this.entregaId,
    required this.recibeId,
    required this.nombreQueNoFirma,
  });

  final String vin;
  final String inspeccionId;
  final String proceso;
  final String entregaId;
  final String recibeId;

  /// Nombre de la parte que se negó, para decirlo con todas las letras.
  final String nombreQueNoFirma;

  @override
  ConsumerState<NoFirmaPage> createState() => _EstadoNoFirmaPage();
}

class _EstadoNoFirmaPage extends ConsumerState<NoFirmaPage> {
  final _motivo = TextEditingController();
  String? _testigoId;
  FotoLocal? _foto;
  bool _guardando = false;

  @override
  void dispose() {
    _motivo.dispose();
    super.dispose();
  }

  /// Quien se negó es la otra parte: si yo entrego, el que no firmó es quien recibe.
  String _parteQueNoFirma(String? yoId) =>
      yoId == widget.entregaId ? ParteTransferencia.recibe : ParteTransferencia.entrega;

  bool get _completa => _motivo.text.trim().isNotEmpty && _testigoId != null && _foto != null;

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
    if (!_completa || usuarioId == null || foto == null) return;
    setState(() => _guardando = true);
    try {
      await ref.read(colaRepositorioProvider).agregar(NoFirmaJob(
            id: const Uuid().v4(),
            etiqueta: 'No firma ${widget.vin} · ${widget.proceso}',
            creadoEn: DateTime.now().millisecondsSinceEpoch,
            usuarioId: usuarioId,
            inspeccionId: widget.inspeccionId,
            entregaId: widget.entregaId,
            recibeId: widget.recibeId,
            parte: _parteQueNoFirma(usuarioId),
            motivo: _motivo.text.trim(),
            testigoId: _testigoId!,
            foto: foto,
          ));
      final sync = ref.read(syncControllerProvider.notifier);
      await sync.refrescar();
      await sync.sincronizar();
      if (!mounted) return;
      final enCola = ref.read(syncControllerProvider).pendientes > 0;
      await showDialog<void>(
        context: context,
        builder: (contexto) => AlertDialog(
          icon: const Icon(Icons.assignment_turned_in_outlined, color: Colores.exito, size: 40),
          title: const Text('Constancia registrada', style: TextStyle(color: Colores.exito)),
          content: Text(
            enCola
                ? 'Quedó en cola; se enviará cuando la señal sea estable.'
                : 'La transferencia queda cerrada con la constancia.',
          ),
          actions: [TextButton(onPressed: () => Navigator.of(contexto).pop(), child: const Text('OK'))],
        ),
      );
      if (mounted) context.pop();
    } catch (e) {
      if (mounted) {
        await showDialog<void>(
          context: context,
          builder: (contexto) => AlertDialog(
            title: const Text('No se pudo registrar la constancia'),
            content: Text(mensajeDeError(e)),
            actions: [TextButton(onPressed: () => Navigator.of(contexto).pop(), child: const Text('OK'))],
          ),
        );
      }
    } finally {
      if (mounted) setState(() => _guardando = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context).textTheme;
    final testigos = ref.watch(_testigosProvider((entregaId: widget.entregaId, recibeId: widget.recibeId)));

    return Scaffold(
      appBar: AppBar(title: Text('No firma · ${widget.proceso}')),
      body: SafeArea(
        child: ListView(
          padding: const EdgeInsets.all(Espacio.lg),
          children: [
            Text(
              'Dejas constancia de que ${widget.nombreQueNoFirma} no firmó. Queda a tu nombre, con el motivo, '
              'un testigo y una foto.',
              style: tema.bodySmall,
            ),
            const SizedBox(height: Espacio.lg),
            TextField(
              controller: _motivo,
              onChanged: (_) => setState(() {}),
              minLines: 2,
              maxLines: 4,
              textCapitalization: TextCapitalization.sentences,
              decoration: const InputDecoration(
                labelText: 'Motivo',
                hintText: 'Qué dijo o qué pasó',
              ),
            ),
            const SizedBox(height: Espacio.lg),
            Text('Testigo', style: tema.titleMedium?.copyWith(fontSize: 15)),
            Text('Una tercera persona: ni quien entrega ni quien recibe.', style: tema.bodySmall),
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
            Text('De la unidad y de quien no firma, como quede el momento.', style: tema.bodySmall),
            const SizedBox(height: Espacio.sm),
            Row(
              children: [
                if (_foto case final foto?)
                  Padding(
                    padding: const EdgeInsets.only(right: Espacio.sm),
                    child: ClipRRect(
                      borderRadius: BorderRadius.circular(6),
                      child: Image.file(
                        File(foto.ruta),
                        width: 56,
                        height: 48,
                        fit: BoxFit.cover,
                        semanticLabel: 'Foto de la constancia',
                      ),
                    ),
                  ),
                BotonEnFila(texto: _foto == null ? 'Tomar foto' : 'Repetir', onPressed: _tomarFoto),
              ],
            ),
            const SizedBox(height: Espacio.lg),
            if (!_completa)
              Semantics(
                liveRegion: true,
                child: Text(
                  'Faltan el motivo, el testigo y la foto: sin los tres no es una constancia.',
                  style: tema.bodySmall?.copyWith(color: Colores.peligro),
                ),
              ),
            const SizedBox(height: Espacio.sm),
            FilledButton(
              onPressed: _completa && !_guardando ? _registrar : null,
              child: _guardando
                  ? const SizedBox(width: 22, height: 22, child: CircularProgressIndicator(strokeWidth: 2.5, color: Colors.white))
                  : const Text('Registrar constancia'),
            ),
          ],
        ),
      ),
    );
  }
}
