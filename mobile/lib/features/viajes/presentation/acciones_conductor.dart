import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:uuid/uuid.dart';

import '../../../app/proveedores.dart';
import '../../../core/dispositivo/servicios.dart';
import '../../../core/tema/tema.dart';
import '../../../core/ui/widgets.dart';
import '../../auth/presentation/auth_controller.dart';
import '../../catalogos/domain/catalogos.dart';
import '../../catalogos/presentation/catalogos_controller.dart';
import '../../inspecciones/presentation/camara_page.dart';
import '../../inspecciones/presentation/sellado.dart';
import '../../sync/domain/desenlace_envio.dart';
import '../../sync/domain/job.dart';
import '../../sync/presentation/sync_controller.dart';
import '../../sync/providers.dart';
import '../../vehiculos/domain/etiquetas.dart';
import '../domain/viaje.dart';
import 'mis_viajes_controller.dart';

/// Dónde está el teléfono ahora, o null si todavía no hay señal de GPS. «Llegué» no se habilita sin él.
///
/// Se vuelve a preguntar sola en cuanto el GPS se enciende: quien está en la carretera lo activa y espera que
/// el botón se habilite, no que haya que buscar un «Reintentar» en la pantalla.
final ubicacionActualProvider = FutureProvider.autoDispose<Posicion?>((ref) async {
  final servicio = ref.read(servicioUbicacionProvider);
  final suscripcion = servicio.servicioActivo.listen((encendido) {
    if (encendido) ref.invalidateSelf();
  });
  ref.onDispose(suscripcion.cancel);
  try {
    return await servicio.obtenerActual();
  } on UbicacionNoDisponible {
    return null;
  }
});

/// Lo que el conductor hace con cada unidad de su viaje en ruta: reportar una novedad y confirmar que llegó.
///
/// Las dos acciones van por la cola: sin señal se guardan con su fecha y su lugar y se envían al volver.
class AccionesConductor extends ConsumerWidget {
  const AccionesConductor({super.key, required this.viaje, required this.unidad});

  final Viaje viaje;
  final ViajeVehiculo unidad;

  bool get _llego =>
      unidad.estadoTransporte == EstadoTransporte.sinNovedad || unidad.estadoTransporte == EstadoTransporte.llegadaConNovedad;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final tema = Theme.of(context).textTheme;
    final gps = ref.watch(ubicacionActualProvider).value;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Row(
          children: [
            Insignia(
              etiquetar(etiquetaEstadoTransporte, unidad.estadoTransporte),
              tono: switch (unidad.estadoTransporte) {
                EstadoTransporte.sinNovedad => TonoInsignia.exito,
                EstadoTransporte.conNovedad || EstadoTransporte.llegadaConNovedad => TonoInsignia.advertencia,
                _ => TonoInsignia.neutro,
              },
            ),
            if (unidad.novedades > 0) ...[
              const SizedBox(width: Espacio.sm),
              Text(unidad.novedades == 1 ? '1 novedad' : '${unidad.novedades} novedades', style: tema.bodySmall),
            ],
          ],
        ),
        if (!_llego) ...[
          const SizedBox(height: Espacio.sm),
          // El botón grande de la pantalla del conductor: una acción por pantalla y con un dedo que puede ir con guante.
          OutlinedButton(
            onPressed: () => _reportarNovedad(context, ref),
            child: const Text('Reportar novedad'),
          ),
          const SizedBox(height: Espacio.sm),
          FilledButton(
            onPressed: gps == null ? null : () => _llegue(context, ref, gps),
            child: const Text('Llegué'),
          ),
          if (gps == null)
            Padding(
              padding: const EdgeInsets.only(top: Espacio.xs),
              child: Row(
                children: [
                  Expanded(
                    child: Text('Sin ubicación: espera la señal del GPS', style: tema.bodySmall?.copyWith(color: Colores.advertencia)),
                  ),
                  TextButton(onPressed: () => ref.invalidate(ubicacionActualProvider), child: const Text('Reintentar')),
                ],
              ),
            ),
        ],
      ],
    );
  }

  Future<void> _llegue(BuildContext context, WidgetRef ref, Posicion posicion) async {
    final tieneNovedades = unidad.novedades > 0 || unidad.estadoTransporte == EstadoTransporte.conNovedad;
    final confirmar = await showDialog<bool>(
      context: context,
      builder: (contexto) => AlertDialog(
        title: Text('Llegué: ${unidad.vin}'),
        content: Text(
          tieneNovedades
              ? 'Se confirmará «Llegada con novedad»: la unidad tiene '
                  '${unidad.novedades == 1 ? '1 novedad registrada' : '${unidad.novedades} novedades registradas'}.'
              : 'Se confirmará «Sin novedad».',
        ),
        actions: [
          TextButton(onPressed: () => Navigator.of(contexto).pop(false), child: const Text('Cancelar')),
          TextButton(onPressed: () => Navigator.of(contexto).pop(true), child: const Text('Confirmar')),
        ],
      ),
    );
    if (confirmar != true || !context.mounted) return;
    final usuarioId = ref.read(authControllerProvider).sesion?.usuario.id;
    if (usuarioId == null) return;
    // La fecha y el lugar son los de ahora, que es cuando llegó: el envío puede salir horas después.
    final jobId = const Uuid().v4();
    await ref.read(colaRepositorioProvider).agregar(LlegadaJob(
          id: jobId,
          etiqueta: 'Llegada ${unidad.vin}',
          creadoEn: DateTime.now().millisecondsSinceEpoch,
          usuarioId: usuarioId,
          viajeId: viaje.id,
          vin: unidad.vin,
          fecha: DateTime.now().toUtc().toIso8601String(),
          lat: posicion.lat,
          lng: posicion.lng,
        ));
    if (context.mounted) await _enviar(context, ref, 'Llegada', jobId);
  }

  Future<void> _reportarNovedad(BuildContext context, WidgetRef ref) async {
    final tamanos = ref.read(catalogosProvider).seleccionables(Catalogo.tamanosDano);
    if (tamanos.isEmpty) {
      // Sin la lista de tamaños no se puede elegir uno, y el servidor exige el tamaño.
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Los catálogos aún no se han descargado. Conéctate e inténtalo de nuevo.')),
      );
      return;
    }
    final temporal = await Navigator.of(context).push<String>(
      MaterialPageRoute(builder: (_) => CamaraPage(titulo: 'Foto de la novedad')),
    );
    if (temporal == null || !context.mounted) return;
    final foto = await sellarFoto(ref, temporal);
    if (!context.mounted) return;
    if (foto == null || foto.sinUbicacion) {
      // Sin lugar el servidor no puede cerrar el registro: se repite con GPS en vez de perder la novedad después.
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text(foto == null ? 'No se pudo guardar la foto. Inténtalo de nuevo.' : 'La foto quedó sin ubicación: espera la señal del GPS y repítela.'),
        ),
      );
      return;
    }
    final datos = await pedirDatosNovedad(context, tamanos: tamanos);
    if (datos == null || !context.mounted) return;
    final usuarioId = ref.read(authControllerProvider).sesion?.usuario.id;
    final lat = foto.selloLat;
    final lng = foto.selloLng;
    if (usuarioId == null || lat == null || lng == null) return;
    final jobId = const Uuid().v4();
    await ref.read(colaRepositorioProvider).agregar(NovedadJob(
          id: jobId,
          etiqueta: 'Novedad ${unidad.vin}',
          creadoEn: DateTime.now().millisecondsSinceEpoch,
          usuarioId: usuarioId,
          viajeId: viaje.id,
          vin: unidad.vin,
          tipoNovedad: datos.tipo,
          tamanoDano: datos.tamano,
          // La fecha y el lugar de la foto: es cuando el conductor la registró.
          fecha: foto.selloFecha,
          lat: lat,
          lng: lng,
          foto: foto,
        ));
    if (context.mounted) await _enviar(context, ref, 'Novedad', jobId);
  }

  /// Intenta enviar ya y dice cómo quedó **este** envío: registrado, en cola, en espera o rechazado.
  ///
  /// Mira el job y no el contador de la cola: con otra novedad pendiente de otra unidad, el contador hacía que
  /// una llegada que el servidor acababa de aceptar se anunciara como «se enviará cuando haya señal», y que una
  /// novedad rechazada se anunciara como registrada. El motivo del rechazo se dice aquí, donde se originó, sin
  /// tener que entrar a la cola a averiguarlo.
  Future<void> _enviar(BuildContext context, WidgetRef ref, String que, String jobId) async {
    final mensajes = ScaffoldMessenger.of(context);
    final sync = ref.read(syncControllerProvider.notifier);
    await sync.refrescar();
    await sync.sincronizar();
    final desenlace = desenlaceDe(
      await ref.read(colaRepositorioProvider).listar(),
      jobId,
      pausa: ref.read(syncControllerProvider).pausa,
    );
    mensajes.showSnackBar(SnackBar(
      duration: desenlace.salio ? const Duration(seconds: 4) : const Duration(seconds: 8),
      content: Text(switch (desenlace.fase) {
        FaseEnvio.registrado => '$que registrada.',
        FaseEnvio.rechazado =>
          '$que rechazada por el servidor${desenlace.motivo == null ? '' : ': ${desenlace.motivo}'}. Quedó en Inspecciones › Cola.',
        FaseEnvio.enEspera =>
          '$que en espera${desenlace.motivo == null ? '' : ': ${desenlace.motivo}'}. Se reintenta sola ${enPalabras(desenlace.espera!)}.',
        FaseEnvio.enCola => '$que guardada; se enviará cuando la señal sea estable.',
      }),
    ));
    await ref.read(misViajesControllerProvider.notifier).cargar();
  }
}

/// El tipo y el tamaño de la novedad: los dos se eligen de una lista y el conductor no escribe nada. Null si cancela.
Future<({String tipo, String tamano})?> pedirDatosNovedad(BuildContext context, {required List<ValorCatalogo> tamanos}) {
  return showModalBottomSheet<({String tipo, String tamano})>(
    context: context,
    isScrollControlled: true,
    showDragHandle: true,
    builder: (_) => _HojaNovedad(tamanos: tamanos),
  );
}

class _HojaNovedad extends StatefulWidget {
  const _HojaNovedad({required this.tamanos});

  final List<ValorCatalogo> tamanos;

  @override
  State<_HojaNovedad> createState() => _EstadoHojaNovedad();
}

class _EstadoHojaNovedad extends State<_HojaNovedad> {
  String? _tipo;
  String? _tamano;

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context).textTheme;
    return Padding(
      padding: EdgeInsets.fromLTRB(Espacio.lg, 0, Espacio.lg, MediaQuery.viewInsetsOf(context).bottom + Espacio.lg),
      child: SingleChildScrollView(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Text('Reportar novedad', style: tema.titleMedium),
            const SizedBox(height: Espacio.md),
            Text('¿Qué pasó?', style: tema.titleSmall),
            const SizedBox(height: Espacio.sm),
            Wrap(
              spacing: Espacio.sm,
              runSpacing: Espacio.xs,
              children: [
                for (final e in etiquetaTipoNovedad.entries)
                  ChoiceChip(
                    label: Text(e.value),
                    selected: _tipo == e.key,
                    materialTapTargetSize: MaterialTapTargetSize.padded,
                    onSelected: (_) => setState(() => _tipo = e.key),
                  ),
              ],
            ),
            const SizedBox(height: Espacio.md),
            Text('Tamaño del daño', style: tema.titleSmall),
            const SizedBox(height: Espacio.sm),
            Wrap(
              spacing: Espacio.sm,
              runSpacing: Espacio.xs,
              children: [
                for (final t in widget.tamanos)
                  ChoiceChip(
                    label: Text(t.nombre),
                    selected: _tamano == t.codigo,
                    materialTapTargetSize: MaterialTapTargetSize.padded,
                    onSelected: (_) => setState(() => _tamano = t.codigo),
                  ),
              ],
            ),
            const SizedBox(height: Espacio.lg),
            FilledButton(
              onPressed: _tipo != null && _tamano != null ? () => Navigator.of(context).pop((tipo: _tipo!, tamano: _tamano!)) : null,
              child: const Text('Guardar novedad'),
            ),
          ],
        ),
      ),
    );
  }
}
