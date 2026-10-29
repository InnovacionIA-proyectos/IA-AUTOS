import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:uuid/uuid.dart';

import '../../../core/errores/error_api.dart';
import '../../../core/tema/tema.dart';
import '../../../core/ui/widgets.dart';
import '../../auth/presentation/auth_controller.dart';
import '../../custodia/presentation/lienzo_firma.dart';
import '../../sync/domain/job.dart';
import '../../sync/presentation/sync_controller.dart';
import '../../sync/providers.dart';
import '../domain/entrega_punto.dart';
import '../providers.dart';

/// Lo que admite el servidor en el motivo de un cierre con una sola firma.
const motivoUnaFirmaMax = 300;

/// Las entregas de un viaje, una por lugar de destino: «Aquí entregas estas N unidades».
class EntregasViajePage extends ConsumerWidget {
  const EntregasViajePage({super.key, required this.viajeId});

  final String viajeId;

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
                              MaterialPageRoute(builder: (_) => FirmaEntregaPuntoPage(punto: punto)),
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

class _TarjetaPunto extends StatelessWidget {
  const _TarjetaPunto({required this.punto, required this.alFirmar});

  final EntregaPunto punto;
  final VoidCallback alFirmar;

  @override
  Widget build(BuildContext context) {
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
      child: Row(
        children: [
          Expanded(child: TextoMono(unidad.vin)),
          switch (unidad.estado) {
            EstadoEntrega.cerrada => const Insignia('Cerrada', tono: TonoInsignia.exito),
            EstadoEntrega.pendiente => const Insignia('Por firmar', tono: TonoInsignia.info),
            EstadoEntrega.sinInspeccion => const Insignia('Sin inspección de llegada', tono: TonoInsignia.advertencia),
          },
        ],
      ),
    );
  }
}

/// Firma de la entrega de un punto: una para todas las unidades, o el cierre con una sola firma cuando la otra
/// parte no está. Se encola: sin señal se envía al volver, y repetirla no duplica nada.
class FirmaEntregaPuntoPage extends ConsumerStatefulWidget {
  const FirmaEntregaPuntoPage({super.key, required this.punto});

  final EntregaPunto punto;

  @override
  ConsumerState<FirmaEntregaPuntoPage> createState() => _EstadoFirmaEntregaPunto();
}

class _EstadoFirmaEntregaPunto extends ConsumerState<FirmaEntregaPuntoPage> {
  String? _firma;
  bool _unaSolaFirma = false;
  bool _guardando = false;
  final _motivo = TextEditingController();

  @override
  void dispose() {
    _motivo.dispose();
    super.dispose();
  }

  EntregaPunto get _punto => widget.punto;

  bool get _puedeEnviar =>
      !_guardando &&
      _punto.sinInspeccion.isEmpty &&
      _firma != null &&
      (!_unaSolaFirma || _motivo.text.trim().isNotEmpty);

  Future<void> _registrar() async {
    final usuarioId = ref.read(authControllerProvider).sesion?.usuario.id;
    final firma = _firma;
    if (usuarioId == null || firma == null || !_puedeEnviar) return;
    setState(() => _guardando = true);
    try {
      await ref.read(colaRepositorioProvider).agregar(EntregaPuntoJob(
            id: const Uuid().v4(),
            etiqueta: 'Entrega ${_punto.lugar}',
            creadoEn: DateTime.now().millisecondsSinceEpoch,
            usuarioId: usuarioId,
            viajeId: _punto.viajeId,
            lugarId: _punto.lugarId,
            firma: firma,
            motivo: _unaSolaFirma ? _motivo.text.trim() : null,
          ));
      final sync = ref.read(syncControllerProvider.notifier);
      await sync.refrescar();
      await sync.sincronizar();
      if (!mounted) return;
      final enCola = ref.read(syncControllerProvider).pendientes > 0;
      final fallida = ref.read(syncControllerProvider).fallidos > 0;
      await showDialog<void>(
        context: context,
        builder: (contexto) => AlertDialog(
          title: Text(fallida ? 'No se pudo enviar' : (enCola ? 'Entrega guardada' : 'Entrega registrada')),
          content: Text(
            fallida
                ? 'El servidor rechazó la entrega. Quedó en Inspecciones › Cola para reintentarla o descartarla.'
                : enCola
                    ? 'Quedó en cola; se enviará cuando la señal sea estable.'
                    : 'Quedó registrada para ${_deLaUnidad(_punto.porEntregar.length)} del punto.',
          ),
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

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context).textTheme;
    final sinInspeccion = _punto.sinInspeccion;
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
                  'Aún no tienen la inspección de llegada del descargue: ${sinInspeccion.map((u) => u.vin).join(', ')}. '
                      'Hazla antes de firmar.',
                  style: tema.bodySmall?.copyWith(color: Colores.peligro),
                ),
              ),
            ],
            const SizedBox(height: Espacio.lg),
            LienzoFirma(etiqueta: 'Tu firma', onCambio: (p) => setState(() => _firma = p)),
            const SizedBox(height: Espacio.sm),
            Text(
              'Firmas tú desde tu sesión; la otra parte firma desde la suya. La firma queda en ${_deLaUnidad(_punto.porEntregar.length)}.',
              style: tema.bodySmall,
            ),
            const SizedBox(height: Espacio.lg),
            if (!_unaSolaFirma) ...[
              FilledButton(
                onPressed: _puedeEnviar ? _registrar : null,
                child: _guardando
                    ? const SizedBox(width: 22, height: 22, child: CircularProgressIndicator(strokeWidth: 2.5, color: Colors.white))
                    : const Text('Registrar mi firma'),
              ),
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
                onPressed: _puedeEnviar ? _registrar : null,
                child: _guardando
                    ? const SizedBox(width: 22, height: 22, child: CircularProgressIndicator(strokeWidth: 2.5, color: Colors.white))
                    : const Text('Cerrar con una sola firma'),
              ),
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
