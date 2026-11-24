import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/errores/error_api.dart';
import '../../../core/tema/tema.dart';
import '../../auth/presentation/auth_controller.dart';
import '../../catalogos/presentation/catalogos_controller.dart';
import '../domain/muelles.dart';
import '../domain/viaje.dart';
import '../providers.dart';

/// Quién puede cambiar el muelle y el tráiler desde la app: el inspector del viaje (también quien reclamó el
/// cargue, que el servidor deja como inspector del viaje) mientras la entrega del cargue no se haya cerrado. El
/// coordinador lo hace desde el portal; el conductor y TI, nunca.
bool puedeCambiarCargue(Viaje viaje, {required String? usuarioId, required String? rol}) =>
    rol == 'inspector' && usuarioId != null && viaje.inspectorId == usuarioId && !viaje.cargueCerrado && viaje.admiteTrabajo;

/// Los botones para cambiar el tráiler y elegir el muelle del viaje en el cargue (S8-HU-03.2 CA-1 y CA-2).
///
/// Van directo al servidor, como «no sale»: necesitan señal y quien decide se entera ahí mismo. El servidor
/// responde con el viaje ya cambiado y la pantalla vuelve a pedirlo para mostrarlo.
class CambiosDelCargue extends ConsumerStatefulWidget {
  const CambiosDelCargue({super.key, required this.viaje});

  final Viaje viaje;

  @override
  ConsumerState<CambiosDelCargue> createState() => _EstadoCambiosDelCargue();
}

class _EstadoCambiosDelCargue extends ConsumerState<CambiosDelCargue> {
  bool _enviando = false;

  Viaje get _viaje => widget.viaje;

  Future<void> _enviar(Future<Viaje> Function() cambio, String hecho) async {
    setState(() => _enviando = true);
    final mensajes = ScaffoldMessenger.of(context);
    final contenedor = ProviderScope.containerOf(context, listen: false);
    try {
      await cambio();
      contenedor.invalidate(viajeDeEtapaProvider(_viaje.id));
      mensajes.showSnackBar(SnackBar(content: Text(hecho)));
    } catch (e) {
      // El 409 `cargue_firmado` trae su propio mensaje: es lo único que explica por qué ya no se puede.
      mensajes.showSnackBar(SnackBar(
        content: Text(e is ErrorApi ? e.mensajeDetallado : 'No se pudo guardar el cambio. Revisa la señal.'),
      ));
      if (e is ErrorApi && e.status == 409) contenedor.invalidate(viajeDeEtapaProvider(_viaje.id));
    } finally {
      if (mounted) setState(() => _enviando = false);
    }
  }

  Future<void> _elegirMuelle() async {
    final muelles = muellesDelLugar(ref.read(catalogosProvider), _viaje.lugarCargueId);
    // Un valor que no es ningún id: es «quitar el muelle».
    const ninguno = '';
    final elegido = await showModalBottomSheet<String>(
      context: context,
      showDragHandle: true,
      builder: (contexto) => SafeArea(
        child: ListView(
          shrinkWrap: true,
          children: [
            for (final muelle in muelles)
              ListTile(
                title: Text(muelle.nombre),
                trailing: muelle.id == _viaje.muelleId ? const Icon(Icons.check, color: Colores.primario) : null,
                onTap: () => Navigator.of(contexto).pop(muelle.id),
              ),
            if (_viaje.muelleId != null)
              ListTile(title: const Text('Quitar el muelle'), onTap: () => Navigator.of(contexto).pop(ninguno)),
          ],
        ),
      ),
    );
    if (elegido == null || elegido == _viaje.muelleId || !mounted) return;
    final repositorio = ref.read(viajesRepositorioProvider);
    if (elegido == ninguno) {
      await _enviar(() => repositorio.cambiarCargue(_viaje.id, quitarMuelle: true), 'El viaje quedó sin muelle');
    } else {
      final nombre = muelles.firstWhere((m) => m.id == elegido).nombre;
      await _enviar(() => repositorio.cambiarCargue(_viaje.id, muelleId: elegido), '$nombre asignado al viaje');
    }
  }

  Future<void> _cambiarTrailer() async {
    final nuevo = await showDialog<String>(
      context: context,
      builder: (_) => _DialogoTrailer(actual: _viaje.trailer ?? ''),
    );
    if (nuevo == null || nuevo == (_viaje.trailer ?? '') || !mounted) return;
    await _enviar(
      () => ref.read(viajesRepositorioProvider).cambiarCargue(_viaje.id, trailer: nuevo),
      nuevo.isEmpty ? 'El viaje quedó sin tráiler' : 'Tráiler cambiado a $nuevo',
    );
  }

  @override
  Widget build(BuildContext context) {
    final sesion = ref.watch(authControllerProvider.select((e) => e.sesion?.usuario));
    if (!puedeCambiarCargue(_viaje, usuarioId: sesion?.id, rol: sesion?.rol)) return const SizedBox.shrink();
    final hayMuelles = muellesDelLugar(ref.watch(catalogosProvider), _viaje.lugarCargueId).isNotEmpty;
    return Wrap(
      spacing: Espacio.sm,
      runSpacing: Espacio.sm,
      children: [
        OutlinedButton.icon(
          onPressed: _enviando ? null : _cambiarTrailer,
          icon: const Icon(Icons.edit_outlined, size: 18),
          label: const Text('Cambiar tráiler'),
        ),
        // Un lugar sin muelles no los pide: ni selector ni bloqueo.
        if (hayMuelles)
          FilledButton.tonalIcon(
            onPressed: _enviando ? null : _elegirMuelle,
            icon: const Icon(Icons.warehouse_outlined, size: 18),
            label: Text(_viaje.muelleId == null ? 'Elegir muelle' : 'Cambiar muelle'),
          ),
      ],
    );
  }
}

/// Pide el tráiler nuevo. Es dueño de su campo de texto: el diálogo sigue en pantalla mientras se cierra, y
/// liberar el campo desde fuera en ese momento lo rompe.
class _DialogoTrailer extends StatefulWidget {
  const _DialogoTrailer({required this.actual});

  final String actual;

  @override
  State<_DialogoTrailer> createState() => _EstadoDialogoTrailer();
}

class _EstadoDialogoTrailer extends State<_DialogoTrailer> {
  late final _texto = TextEditingController(text: widget.actual);

  @override
  void dispose() {
    _texto.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => AlertDialog(
        title: const Text('Tráiler del viaje'),
        content: TextField(
          controller: _texto,
          autofocus: true,
          textCapitalization: TextCapitalization.characters,
          decoration: const InputDecoration(labelText: 'Tráiler', hintText: 'Vacío lo borra'),
        ),
        actions: [
          TextButton(onPressed: () => Navigator.of(context).pop(), child: const Text('Cancelar')),
          TextButton(onPressed: () => Navigator.of(context).pop(_texto.text.trim()), child: const Text('Guardar')),
        ],
      );
}
