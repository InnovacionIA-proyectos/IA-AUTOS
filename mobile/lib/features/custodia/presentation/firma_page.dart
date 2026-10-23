import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:uuid/uuid.dart';

import '../../../core/errores/error_api.dart';
import '../../../core/tema/tema.dart';
import '../../../core/ui/widgets.dart';
import '../../auth/domain/sesion.dart';
import '../../auth/presentation/auth_controller.dart';
import '../../inspecciones/domain/reglas_informe.dart';
import '../../sync/domain/job.dart';
import '../../sync/presentation/sync_controller.dart';
import '../../sync/providers.dart';
import '../../vehiculos/domain/etiquetas.dart';
import '../providers.dart';
import 'lienzo_firma.dart';

final _usuariosProvider = FutureProvider.autoDispose<List<Usuario>>((ref) => ref.watch(usuariosRepositorioProvider).listar());

/// Firma de la transferencia: quien entrega y quien recibe, cada uno con su trazo. Se puede firmar solo una parte;
/// la otra firma desde su dispositivo. El servidor valida que quien firma sea el usuario autenticado.
class FirmaPage extends ConsumerStatefulWidget {
  const FirmaPage({super.key, required this.vin, required this.inspeccionId, required this.proceso, required this.soloFirma});

  final String vin;
  final String inspeccionId;
  final String proceso;
  final bool soloFirma;

  @override
  ConsumerState<FirmaPage> createState() => _EstadoFirmaPage();
}

class _EstadoFirmaPage extends ConsumerState<FirmaPage> {
  String? _entregaId;
  String? _recibeId;
  String? _firmaEntrega;
  String? _firmaRecibe;
  bool _guardando = false;
  bool _seleccionInicial = false;

  /// Por defecto firma entrega quien está en sesión y recibe la primera otra persona de la lista.
  void _elegirPorDefecto(List<Usuario> usuarios, String? yoId) {
    if (_seleccionInicial || usuarios.isEmpty) return;
    _seleccionInicial = true;
    _entregaId = usuarios.any((u) => u.id == yoId) ? yoId : usuarios.first.id;
    _recibeId = usuarios.where((u) => u.id != _entregaId).firstOrNull?.id;
  }

  bool get _hayFirma => _firmaEntrega != null || _firmaRecibe != null;
  bool get _puede => firmaValida(entregaId: _entregaId, recibeId: _recibeId, hayFirma: _hayFirma) && !_guardando;

  Future<void> _registrar() async {
    final usuarioId = ref.read(authControllerProvider).sesion?.usuario.id;
    if (!_puede || usuarioId == null) return;
    setState(() => _guardando = true);
    try {
      await ref.read(colaRepositorioProvider).agregar(FirmaJob(
            id: const Uuid().v4(),
            etiqueta: 'Firma ${widget.vin} · ${widget.proceso}',
            creadoEn: DateTime.now().millisecondsSinceEpoch,
            usuarioId: usuarioId,
            inspeccionId: widget.inspeccionId,
            entregaId: _entregaId!,
            recibeId: _recibeId!,
            firmaEntrega: _firmaEntrega,
            firmaRecibe: _firmaRecibe,
          ));
      final sync = ref.read(syncControllerProvider.notifier);
      await sync.refrescar();
      await sync.sincronizar();
      if (!mounted) return;
      final enCola = ref.read(syncControllerProvider).pendientes > 0;
      await showDialog<void>(
        context: context,
        builder: (contexto) => AlertDialog(
          title: const Text('Firma registrada'),
          content: Text(enCola ? 'Quedó en cola; se enviará cuando la señal sea estable.' : 'Cambio de responsabilidad registrado.'),
          actions: [TextButton(onPressed: () => Navigator.of(contexto).pop(), child: const Text('OK'))],
        ),
      );
      if (mounted) context.pop();
    } catch (e) {
      if (mounted) {
        await showDialog<void>(
          context: context,
          builder: (contexto) => AlertDialog(
            title: const Text('No se pudo registrar la firma'),
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
    final usuarios = ref.watch(_usuariosProvider);
    final yoId = ref.watch(authControllerProvider.select((e) => e.sesion?.usuario.id));

    return Scaffold(
      appBar: AppBar(title: Text('Firma · ${widget.proceso}')),
      body: usuarios.when(
        loading: () => const EstadoCargando(),
        error: (error, _) => EstadoMensaje(
          icono: Icons.warning_amber_rounded,
          colorIcono: Colores.peligro,
          titulo: 'No se pudo cargar la lista de usuarios',
          descripcion: mensajeDeError(error),
          accion: FilledButton(onPressed: () => ref.invalidate(_usuariosProvider), child: const Text('Reintentar')),
        ),
        data: (lista) {
          _elegirPorDefecto(lista, yoId);
          return _formulario(context, lista);
        },
      ),
    );
  }

  Widget _formulario(BuildContext context, List<Usuario> lista) {
    final tema = Theme.of(context).textTheme;
    final distintos = _entregaId != null && _recibeId != null && _entregaId != _recibeId;
    return SafeArea(
      child: ListView(
        padding: const EdgeInsets.all(Espacio.lg),
        children: [
          Text('${widget.vin} · ${widget.proceso}${widget.soloFirma ? ' · firma sobre el último informe' : ''}', style: tema.bodySmall),
          const SizedBox(height: Espacio.lg),
          _SelectorPersona(etiqueta: 'Quien entrega', usuarios: lista, valor: _entregaId, alElegir: (id) => setState(() => _entregaId = id)),
          const SizedBox(height: Espacio.sm),
          LienzoFirma(etiqueta: 'Firma de quien entrega', onCambio: (p) => setState(() => _firmaEntrega = p)),
          const SizedBox(height: Espacio.lg),
          _SelectorPersona(etiqueta: 'Quien recibe', usuarios: lista, valor: _recibeId, alElegir: (id) => setState(() => _recibeId = id)),
          const SizedBox(height: Espacio.sm),
          LienzoFirma(etiqueta: 'Firma de quien recibe', onCambio: (p) => setState(() => _firmaRecibe = p)),
          const SizedBox(height: Espacio.md),
          if (!distintos)
            Semantics(
              liveRegion: true,
              child: Text('Quien entrega y quien recibe deben ser distintos.', style: tema.bodySmall?.copyWith(color: Colores.peligro)),
            ),
          Text('Puedes firmar solo una parte; la otra puede firmarse desde su dispositivo.', style: tema.bodySmall),
          const SizedBox(height: Espacio.lg),
          FilledButton(
            onPressed: _puede ? _registrar : null,
            child: _guardando ? const SizedBox(width: 22, height: 22, child: CircularProgressIndicator(strokeWidth: 2.5, color: Colors.white)) : const Text('Registrar firma(s)'),
          ),
        ],
      ),
    );
  }
}

class _SelectorPersona extends StatelessWidget {
  const _SelectorPersona({required this.etiqueta, required this.usuarios, required this.valor, required this.alElegir});

  final String etiqueta;
  final List<Usuario> usuarios;
  final String? valor;
  final ValueChanged<String> alElegir;

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(etiqueta, style: Theme.of(context).textTheme.titleMedium?.copyWith(fontSize: 15)),
        const SizedBox(height: Espacio.sm),
        Wrap(
          spacing: Espacio.sm,
          runSpacing: Espacio.xs,
          children: [
            for (final u in usuarios)
              Semantics(
                label: '$etiqueta: ${u.nombre}',
                child: ChoiceChip(
                  label: Text(u.nombre),
                  tooltip: etiquetar(etiquetaRol, u.rol),
                  selected: valor == u.id,
                  materialTapTargetSize: MaterialTapTargetSize.padded,
                  onSelected: (_) => alElegir(u.id),
                ),
              ),
          ],
        ),
      ],
    );
  }
}
