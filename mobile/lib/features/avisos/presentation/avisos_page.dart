import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../core/errores/error_api.dart';
import '../../../core/tema/tema.dart';
import '../../../core/ui/widgets.dart';
import '../../../core/util/formato.dart';
import '../../auth/presentation/auth_controller.dart';
import '../domain/aviso.dart';
import '../providers.dart';

/// Lo que máximo admite el servidor en la respuesta a una novedad.
const respuestaMax = 1000;

/// Quién puede contestar una novedad del conductor: el coordinador de calidad y los administradores.
bool puedeResponder(String? rol) => rol == 'coordinador_calidad' || rol == 'admin' || rol == 'super_admin';

/// La bandeja «Avisos»: lo que el sistema le dice a quien mira, con su urgencia, y que se marca como leído al abrirlo.
///
/// Cada persona ve solo los suyos; el servidor decide cuáles (el inspector, los operativos; el coordinador, las
/// alertas preventivas de su cliente). Se consulta con señal.
class AvisosPage extends ConsumerWidget {
  const AvisosPage({super.key});

  Future<void> _abrir(BuildContext context, WidgetRef ref, Aviso aviso) async {
    final rol = ref.read(authControllerProvider).sesion?.usuario.rol;
    // Se lee al abrirlo. Si la marca falla (sin señal) el aviso se abre igual: leerlo no puede depender de la red.
    if (!aviso.leido) {
      try {
        await ref.read(avisosRepositorioProvider).marcarLeido(aviso.id);
      } catch (_) {
        // El aviso se muestra igual; la marca se reintenta la próxima vez que se abra.
      }
      ref.invalidate(avisosProvider);
    }
    if (!context.mounted) return;
    await showModalBottomSheet<void>(
      context: context,
      isScrollControlled: true,
      showDragHandle: true,
      builder: (_) => _HojaAviso(aviso: aviso, puedeResponder: puedeResponder(rol), alResponder: () => ref.invalidate(avisosProvider)),
    );
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final tema = Theme.of(context).textTheme;
    final pagina = ref.watch(avisosProvider);
    return Scaffold(
      body: SafeArea(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Padding(
              padding: const EdgeInsets.fromLTRB(Espacio.lg, Espacio.sm, Espacio.lg, Espacio.md),
              child: Row(
                children: [
                  Expanded(child: Text('Avisos', style: tema.headlineMedium)),
                  IconButton(
                    tooltip: 'Actualizar avisos',
                    onPressed: () => ref.invalidate(avisosProvider),
                    icon: const Icon(Icons.refresh),
                  ),
                ],
              ),
            ),
            Expanded(
              child: pagina.when(
                skipLoadingOnRefresh: true,
                loading: () => const EstadoCargando(),
                error: (error, _) => EstadoMensaje(
                  icono: Icons.warning_amber_rounded,
                  colorIcono: Colores.peligro,
                  titulo: 'No pudimos cargar tus avisos',
                  descripcion: error is ErrorApi && error.status == 0
                      ? 'Los avisos se consultan con señal. Revisa tu conexión e inténtalo de nuevo.'
                      : mensajeDeError(error),
                  accion: FilledButton(onPressed: () => ref.invalidate(avisosProvider), child: const Text('Reintentar')),
                ),
                data: (datos) => datos.avisos.isEmpty
                    ? const EstadoMensaje(
                        icono: Icons.notifications_none,
                        titulo: 'No tienes avisos',
                        descripcion: 'Cuando haya algo que requiera tu atención aparecerá aquí.',
                      )
                    : RefreshIndicator(
                        onRefresh: () async => ref.invalidate(avisosProvider),
                        child: ListView.separated(
                          padding: const EdgeInsets.fromLTRB(Espacio.lg, 0, Espacio.lg, Espacio.xl),
                          itemCount: datos.avisos.length + 1,
                          separatorBuilder: (_, _) => const SizedBox(height: Espacio.sm),
                          itemBuilder: (_, i) => i == 0
                              ? Text(
                                  datos.noLeidos == 0
                                      ? 'Todo leído'
                                      : datos.noLeidos == 1
                                          ? '1 aviso sin leer'
                                          : '${datos.noLeidos} avisos sin leer',
                                  style: tema.bodySmall,
                                )
                              : _TarjetaAviso(aviso: datos.avisos[i - 1], alTocar: () => _abrir(context, ref, datos.avisos[i - 1])),
                        ),
                      ),
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _TarjetaAviso extends StatelessWidget {
  const _TarjetaAviso({required this.aviso, required this.alTocar});

  final Aviso aviso;
  final VoidCallback alTocar;

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context).textTheme;
    return Tarjeta(
      onTap: alTocar,
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          // El color dice la urgencia de un vistazo; el punto, que falta leerlo.
          Container(
            width: 6,
            height: 56,
            margin: const EdgeInsets.only(right: Espacio.md),
            decoration: BoxDecoration(
              color: aviso.esAlta ? Colores.peligro : Colores.advertencia,
              borderRadius: BorderRadius.circular(3),
            ),
          ),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  children: [
                    Expanded(
                      child: Text(
                        aviso.titulo,
                        style: tema.titleSmall?.copyWith(fontWeight: aviso.leido ? FontWeight.w500 : FontWeight.w700),
                      ),
                    ),
                    if (!aviso.leido)
                      Semantics(label: 'Sin leer', child: const Icon(Icons.circle, size: 10, color: Colores.info)),
                  ],
                ),
                Text(aviso.mensaje, maxLines: 2, overflow: TextOverflow.ellipsis, style: tema.bodySmall),
                const SizedBox(height: Espacio.xs),
                Row(
                  children: [
                    Insignia(aviso.esAlta ? 'Urgente' : 'Media', tono: aviso.esAlta ? TonoInsignia.peligro : TonoInsignia.advertencia),
                    const SizedBox(width: Espacio.sm),
                    Expanded(child: Text(formatearFecha(aviso.creadoEn), style: tema.bodySmall)),
                    if (aviso.respuesta != null) const Insignia('Respondida', tono: TonoInsignia.exito),
                  ],
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

/// El aviso completo: su mensaje, la unidad a la que lleva y, si es una novedad sin contestar y quien mira puede,
/// el formulario para responderla.
class _HojaAviso extends ConsumerStatefulWidget {
  const _HojaAviso({required this.aviso, required this.puedeResponder, required this.alResponder});

  final Aviso aviso;
  final bool puedeResponder;
  final VoidCallback alResponder;

  @override
  ConsumerState<_HojaAviso> createState() => _EstadoHojaAviso();
}

class _EstadoHojaAviso extends ConsumerState<_HojaAviso> {
  late Aviso _aviso = widget.aviso;
  bool _respondiendo = false;
  bool _enviando = false;
  String _medio = MedioRespuesta.app;
  final _texto = TextEditingController();

  @override
  void dispose() {
    _texto.dispose();
    super.dispose();
  }

  Future<void> _enviar() async {
    final texto = _texto.text.trim();
    if (texto.isEmpty || _enviando) return;
    setState(() => _enviando = true);
    final mensajes = ScaffoldMessenger.of(context);
    try {
      final actualizado = await ref.read(avisosRepositorioProvider).responder(_aviso.id, texto: texto, medio: _medio);
      widget.alResponder();
      if (mounted) {
        setState(() {
          _aviso = actualizado;
          _respondiendo = false;
        });
      }
    } catch (e) {
      // El motivo del servidor, tal cual: si otra persona ya la respondió, es lo que explica por qué esta no se guardó.
      mensajes.showSnackBar(SnackBar(content: Text(mensajeDeError(e, 'No se pudo enviar la respuesta. Inténtalo de nuevo.'))));
      if (e is ErrorApi && e.status == 409) widget.alResponder();
    } finally {
      if (mounted) setState(() => _enviando = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context).textTheme;
    final respuesta = _aviso.respuesta;
    return Padding(
      padding: EdgeInsets.fromLTRB(Espacio.lg, 0, Espacio.lg, MediaQuery.viewInsetsOf(context).bottom + Espacio.lg),
      child: SingleChildScrollView(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Text(_aviso.titulo, style: tema.titleMedium),
            const SizedBox(height: Espacio.xs),
            Text(formatearFecha(_aviso.creadoEn), style: tema.bodySmall),
            const SizedBox(height: Espacio.md),
            Text(_aviso.mensaje, style: tema.bodyMedium),
            if (_aviso.codigoMtrack != null || _aviso.vin != null) ...[
              const SizedBox(height: Espacio.md),
              Row(
                children: [
                  if (_aviso.codigoMtrack != null) Expanded(child: Text('Viaje ${_aviso.codigoMtrack}', style: tema.bodySmall)),
                  if (_aviso.vin != null) TextoMono(_aviso.vin!, estilo: tema.bodySmall),
                ],
              ),
            ],
            if (respuesta != null) ...[
              const SizedBox(height: Espacio.md),
              Tarjeta(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      [
                        'Respuesta',
                        if (respuesta.usuarioNombre != null) 'de ${respuesta.usuarioNombre}',
                        respuesta.medio == MedioRespuesta.llamada ? 'por llamada' : 'por la app',
                      ].join(' '),
                      style: tema.titleSmall,
                    ),
                    Text(respuesta.texto, style: tema.bodyMedium),
                  ],
                ),
              ),
            ],
            if (_aviso.vin != null && _aviso.viajeId != null) ...[
              const SizedBox(height: Espacio.md),
              OutlinedButton(
                onPressed: () {
                  Navigator.of(context).pop();
                  context.push(
                    '/inspecciones/vin/${Uri.encodeComponent(_aviso.vin!)}?viajeId=${Uri.encodeQueryComponent(_aviso.viajeId!)}',
                  );
                },
                child: const Text('Ver la unidad'),
              ),
            ],
            if (widget.puedeResponder && _aviso.sePuedeResponder) ...[
              const SizedBox(height: Espacio.md),
              if (!_respondiendo)
                FilledButton(onPressed: () => setState(() => _respondiendo = true), child: const Text('Responder'))
              else ...[
                TextField(
                  controller: _texto,
                  onChanged: (_) => setState(() {}),
                  minLines: 2,
                  maxLines: 4,
                  maxLength: respuestaMax,
                  textCapitalization: TextCapitalization.sentences,
                  decoration: const InputDecoration(labelText: 'Respuesta', hintText: 'Qué debe hacer el conductor'),
                ),
                Wrap(
                  spacing: Espacio.sm,
                  children: [
                    ChoiceChip(
                      label: const Text('Por la app'),
                      selected: _medio == MedioRespuesta.app,
                      materialTapTargetSize: MaterialTapTargetSize.padded,
                      onSelected: (_) => setState(() => _medio = MedioRespuesta.app),
                    ),
                    ChoiceChip(
                      label: const Text('Por llamada'),
                      selected: _medio == MedioRespuesta.llamada,
                      materialTapTargetSize: MaterialTapTargetSize.padded,
                      onSelected: (_) => setState(() => _medio = MedioRespuesta.llamada),
                    ),
                  ],
                ),
                const SizedBox(height: Espacio.md),
                FilledButton(
                  onPressed: _texto.text.trim().isEmpty || _enviando ? null : _enviar,
                  child: _enviando
                      ? const SizedBox(width: 22, height: 22, child: CircularProgressIndicator(strokeWidth: 2.5, color: Colors.white))
                      : const Text('Enviar respuesta'),
                ),
              ],
            ],
          ],
        ),
      ),
    );
  }
}
