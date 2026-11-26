import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:uuid/uuid.dart';

import '../../../app/proveedores.dart';
import '../../../core/dispositivo/servicios.dart';
import '../../../core/tema/tema.dart';
import '../../auth/presentation/auth_controller.dart';
import '../../sync/domain/desenlace_envio.dart';
import '../../sync/domain/job.dart';
import '../../sync/presentation/sync_controller.dart';
import '../../sync/providers.dart';
import '../domain/viaje.dart';
import '../providers.dart';
import 'acciones_conductor.dart';
import 'mis_viajes_controller.dart';

/// Una parada del viaje vista desde la copia de «Mis viajes»: el lugar y sus carros activos. Sirve sin señal.
typedef ParadaDelViaje = ({String clave, String lugar, List<ViajeVehiculo> carros});

/// El conductor ya marcó «Llegué» para este carro (lo dice el servidor en su estado de transporte).
bool yaLlego(ViajeVehiculo carro) =>
    carro.estadoTransporte == EstadoTransporte.sinNovedad || carro.estadoTransporte == EstadoTransporte.llegadaConNovedad;

/// Las paradas del viaje, en el orden en que aparecen sus carros: los carros activos juntados por su lugar de destino.
///
/// Un carro que no sale o se bajó no se entrega y no cuenta. Un carro sin destino no es de ninguna parada: sale
/// aparte, para que el coordinador le asigne uno (S8-HU-04.2 CA-8).
List<ParadaDelViaje> paradasDelViaje(Viaje viaje) {
  final paradas = <String, ParadaDelViaje>{};
  for (final carro in viaje.vehiculos) {
    if (!carro.sale) continue;
    final clave = carro.lugarDestinoId ?? carro.lugarDestino;
    if (clave == null || clave.isEmpty) continue;
    final parada = paradas[clave] ??= (clave: clave, lugar: carro.lugarDestino ?? 'Sin nombre', carros: <ViajeVehiculo>[]);
    parada.carros.add(carro);
  }
  return [...paradas.values];
}

/// Los «Llegué» que esta persona tiene en la cola sin enviar, como «viaje|VIN» → por qué esperan (null si nadie los ha
/// intentado: esperan señal). Con esto el botón no se ofrece otra vez mientras la llegada espera: repetirla no
/// duplicaría nada en el servidor, pero llenaría la cola.
final llegadasEnColaProvider = FutureProvider.autoDispose<Map<String, String?>>((ref) async {
  // Se vuelve a leer cada vez que la cola cambia de tamaño: cuando la llegada sale, el botón deja de esperar.
  ref.watch(syncControllerProvider.select((s) => s.pendientes));
  final usuarioId = ref.watch(authControllerProvider.select((e) => e.sesion?.usuario.id));
  final jobs = await ref.watch(colaRepositorioProvider).listar();
  return {
    for (final j in jobs)
      if (j is LlegadaJob && !j.fallido && (usuarioId == null || j.esDe(usuarioId))) '${j.viajeId}|${j.vin}': j.error,
  };
});

/// «Llegué a esta parada» (S8-HU-04.2 CA-9): un solo toque manda por la cola la llegada de cada carro de la parada,
/// con la hora y el GPS de ese momento.
///
/// Cada carro es un envío propio: el que el servidor no deja marcar (su cargue sin cerrar, por ejemplo) sale aparte
/// con su motivo y no bloquea a los demás.
class LlegueAParada extends ConsumerStatefulWidget {
  const LlegueAParada({super.key, required this.viajeId, required this.lugar, required this.vines, this.sinRuta = const []});

  final String viajeId;
  final String lugar;

  /// Los carros de la parada que todavía no tienen su «Llegué» y que el servidor deja registrar.
  final List<String> vines;

  /// Los carros de la parada sin su «Llegué» que todavía no se pueden registrar (`puede_registrar_ruta` en falso,
  /// S8-HU-07.3 CA-5): no van en la llegada de la parada, como tampoco se ofrece su «Llegué» propio, y se dice por qué.
  final List<ViajeVehiculo> sinRuta;

  @override
  ConsumerState<LlegueAParada> createState() => _EstadoLlegueAParada();
}

class _EstadoLlegueAParada extends ConsumerState<LlegueAParada> {
  bool _enviando = false;

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context).textTheme;
    final gps = ref.watch(ubicacionActualProvider).value;
    final enCola = ref.watch(llegadasEnColaProvider).value ?? const <String, String?>{};
    final faltan = [for (final vin in widget.vines) if (!enCola.containsKey('${widget.viajeId}|$vin')) vin];
    if (widget.vines.isEmpty && widget.sinRuta.isEmpty) return const SizedBox.shrink();
    // Por qué hay carros que no entran en esta llegada, agrupados por motivo: «Falta cerrar la entrega del cargue: VIN…».
    final porMotivo = <String, List<String>>{};
    for (final carro in widget.sinRuta) {
      (porMotivo[textoSinRuta(carro.motivoSinRuta)] ??= []).add(carro.vin);
    }
    final avisoSinRuta = [
      for (final MapEntry(key: motivo, value: vines) in porMotivo.entries)
        Padding(
          padding: const EdgeInsets.only(top: Espacio.xs),
          child: Text('$motivo: ${vines.join(', ')}', style: tema.bodySmall?.copyWith(color: Colores.advertencia)),
        ),
    ];
    if (widget.vines.isEmpty) {
      // Ninguno se puede registrar todavía: el botón gris, como el «Llegué» de cada uno, y el motivo debajo.
      return Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [const FilledButton(onPressed: null, child: Text('Llegué a esta parada')), ...avisoSinRuta],
      );
    }
    if (faltan.isEmpty) {
      // Lo que de verdad la detiene: puede ser la señal o algo del servidor, como el cargue sin cerrar
      // (`ETAPA_PREVIA_SIN_CIERRE`). Culpar a la señal en ese caso manda a buscar el problema donde no está.
      final motivos = {
        for (final vin in widget.vines) ?enCola['${widget.viajeId}|$vin'],
      };
      return Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text(
            motivos.isEmpty
                ? 'La llegada a esta parada está guardada: se enviará cuando haya señal.'
                : 'La llegada a esta parada está guardada y espera: ${motivos.join('; ')}. Se reintenta sola.',
            style: tema.bodySmall?.copyWith(color: Colores.advertencia),
          ),
          ...avisoSinRuta,
        ],
      );
    }
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        FilledButton(
          onPressed: gps == null || _enviando ? null : () => _llegue(gps, faltan),
          child: const Text('Llegué a esta parada'),
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
        ...avisoSinRuta,
      ],
    );
  }

  Future<void> _llegue(Posicion ultima, List<String> vines) async {
    final confirmar = await showDialog<bool>(
      context: context,
      builder: (contexto) => AlertDialog(
        title: Text('Llegué a ${widget.lugar}'),
        content: Text(
          'Se confirmará la llegada de ${vines.length == 1 ? 'este carro' : 'estos ${vines.length} carros'} con la hora '
          'y el lugar de ahora:\n${vines.join('\n')}',
        ),
        actions: [
          TextButton(onPressed: () => Navigator.of(contexto).pop(false), child: const Text('Cancelar')),
          TextButton(onPressed: () => Navigator.of(contexto).pop(true), child: const Text('Confirmar')),
        ],
      ),
    );
    if (confirmar != true || !mounted) return;
    // Lo que hace falta se toma ahora, mientras el botón existe: después de enviar, «Mis viajes» se recarga y este
    // widget puede ya no estar. Con el contenedor no se usa `ref` de un widget desmontado.
    final contenedor = ProviderScope.containerOf(context, listen: false);
    final usuarioId = contenedor.read(authControllerProvider).sesion?.usuario.id;
    if (usuarioId == null) return;
    setState(() => _enviando = true);
    try {
      // La hora y el lugar de este toque, los mismos para todos los carros: es cuando el camión llegó a la parada.
      final momento = DateTime.now();
      // La lectura que habilitó el botón solo vale si es de ahora; si no, se pide una nueva. Antes, si fallaba, se
      // usaba esa misma, que podía ser de horas antes y de otro sitio (revisión de S8-HU-07.3).
      final posicion = await posicionDelMomento(contenedor.read(servicioUbicacionProvider), conocida: ultima);
      if (posicion == null) {
        contenedor.invalidate(ubicacionActualProvider);
        if (mounted) ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text(mensajeLlegadaSinGps)));
        return;
      }
      final cola = contenedor.read(colaRepositorioProvider);
      final envios = <String, String>{};
      for (final vin in vines) {
        final id = const Uuid().v4();
        envios[vin] = id;
        await cola.agregar(LlegadaJob(
          id: id,
          etiqueta: 'Llegada $vin',
          creadoEn: momento.millisecondsSinceEpoch,
          usuarioId: usuarioId,
          viajeId: widget.viajeId,
          vin: vin,
          fecha: momento.toUtc().toIso8601String(),
          lat: posicion.lat,
          lng: posicion.lng,
        ));
      }
      contenedor.invalidate(llegadasEnColaProvider);
      final sync = contenedor.read(syncControllerProvider.notifier);
      await sync.refrescar();
      await sync.sincronizar();
      // Con la llegada en el servidor, la parada ya ofrece «Entregar en esta parada»: lo que se veía de ella quedó
      // viejo (S8-HU-04.2 CA-1).
      contenedor.invalidate(entregasDelViajeProvider(widget.viajeId));
      contenedor.invalidate(entregaDeParadaProvider);
      final jobs = await cola.listar();
      final pausa = contenedor.read(syncControllerProvider).pausa;
      final registrados = <String>[];
      final enEspera = <String>[];
      final aparte = <(String, String)>[];
      for (final MapEntry(key: vin, value: id) in envios.entries) {
        final desenlace = desenlaceDe(jobs, id, pausa: pausa);
        switch (desenlace.fase) {
          case FaseEnvio.registrado:
            registrados.add(vin);
          case FaseEnvio.enCola:
            enEspera.add(vin);
          case FaseEnvio.enEspera || FaseEnvio.rechazado:
            aparte.add((vin, desenlace.motivo ?? 'sin motivo'));
        }
      }
      if (mounted) {
        await showDialog<void>(
          context: context,
          builder: (contexto) => AlertDialog(
            title: Text(registrados.length == vines.length ? 'Llegada registrada' : 'Llegada a ${widget.lugar}'),
            content: Text([
              if (registrados.isNotEmpty)
                registrados.length == 1 ? 'Quedó la llegada de 1 carro.' : 'Quedó la llegada de ${registrados.length} carros.',
              if (enEspera.isNotEmpty)
                '${enEspera.length == 1 ? '1 carro queda' : '${enEspera.length} carros quedan'} en cola: se enviará cuando la '
                    'señal sea estable.',
              if (aparte.isNotEmpty) ...[
                'Estos carros no se pudieron marcar, y los demás no esperan por ellos:',
                for (final (vin, motivo) in aparte) '· $vin: $motivo',
              ],
            ].join('\n')),
            actions: [TextButton(onPressed: () => Navigator.of(contexto).pop(), child: const Text('OK'))],
          ),
        );
      }
      await contenedor.read(misViajesControllerProvider.notifier).cargar();
    } finally {
      if (mounted) setState(() => _enviando = false);
    }
  }
}
