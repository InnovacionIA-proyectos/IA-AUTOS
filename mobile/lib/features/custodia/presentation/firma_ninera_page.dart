import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:uuid/uuid.dart';

import '../../../app/proveedores.dart';
import '../../../core/dispositivo/servicios.dart';
import '../../../core/errores/error_api.dart';
import '../../../core/tema/tema.dart';
import '../../../core/ui/widgets.dart';
import '../../auth/presentation/auth_controller.dart';
import '../../inspecciones/providers.dart';
import '../../sync/domain/job.dart';
import '../../sync/presentation/anuncio_desenlace.dart';
import '../../sync/presentation/sync_controller.dart';
import '../../sync/providers.dart';
import '../../vehiculos/domain/inventario.dart';
import '../../vehiculos/providers.dart';
import '../../viajes/domain/viaje.dart';
import '../../viajes/presentation/mis_viajes_controller.dart';
import '../../viajes/providers.dart';
import '../domain/firma_ninera.dart';
import 'lienzo_firma.dart';
import 'no_firma_ninera_page.dart';

/// El inventario de un carro: de él sale la transferencia del cargue, con quién firmó y sus reservas.
final _inventarioDelCarroProvider = FutureProvider.autoDispose.family<Inventario, String>(
  (ref, vin) => ref.watch(vehiculosRepositorioProvider).inventario(vin),
);

/// Si esta persona ya dejó su firma de la niñera en la cola y todavía no salió: firmar otra vez mandaría otra con
/// otra hora y otro GPS, y la segunda pisaría a la primera.
final _firmaEnColaProvider =
    FutureProvider.autoDispose.family<bool, ({String viajeId, String parte, String usuarioId})>((ref, clave) async {
  final jobs = await ref.watch(colaRepositorioProvider).listar();
  return jobs.any(
    (j) => j is FirmaNineraJob && j.viajeId == clave.viajeId && j.parte == clave.parte && j.esDe(clave.usuarioId) && !j.fallido,
  );
});

/// La firma del cargue de toda la niñera (S8-HU-01.3): una sola vez por parte para todos los carros activos. El
/// inspector del viaje firma la entrega y el conductor el recibo, cada uno desde su sesión.
///
/// Firmar sale gris con lo que falta mientras algún carro activo no tenga la llegada y la salida del cargue
/// cerradas (CA-6). Quien recibe puede dejar reservas en algunos carros (CA-13), y si la otra parte se niega,
/// la que sí está deja la constancia de toda la niñera (CA-14). Todo va por la cola con la hora y el GPS del
/// momento en que se firmó: sin señal se envía después y conserva el momento del patio.
class FirmaNineraPage extends ConsumerStatefulWidget {
  const FirmaNineraPage({super.key, required this.viajeId});

  final String viajeId;

  @override
  ConsumerState<FirmaNineraPage> createState() => _EstadoFirmaNinera();
}

class _EstadoFirmaNinera extends ConsumerState<FirmaNineraPage> {
  String? _firma;
  bool _guardando = false;

  /// Las reservas de quien recibe, por VIN.
  final Map<String, String> _reservas = {};

  Future<void> _reservar(String vin) async {
    final texto = await showDialog<String>(
      context: context,
      builder: (_) => DialogoReserva(vin: vin, actual: _reservas[vin] ?? ''),
    );
    if (texto == null || !mounted) return;
    setState(() => texto.isEmpty ? _reservas.remove(vin) : _reservas[vin] = texto);
  }

  Future<void> _firmar(Viaje viaje, String parte) async {
    final usuarioId = ref.read(authControllerProvider).sesion?.usuario.id;
    final firma = _firma;
    if (usuarioId == null || firma == null || _guardando) return;
    setState(() => _guardando = true);
    final mensajes = ScaffoldMessenger.of(context);
    try {
      // La hora y el lugar de AHORA: es lo que prueba dónde y cuándo se firmó, aunque llegue al servidor después.
      final momento = DateTime.now();
      final Posicion posicion;
      try {
        posicion = await ref.read(servicioUbicacionProvider).obtenerActual();
      } catch (_) {
        mensajes.showSnackBar(
          const SnackBar(content: Text('Sin ubicación no se puede firmar: activa el GPS e inténtalo de nuevo.')),
        );
        return;
      }
      final jobId = const Uuid().v4();
      await ref.read(colaRepositorioProvider).agregar(FirmaNineraJob(
            id: jobId,
            etiqueta: '${parte == ParteTransferencia.entrega ? 'Entrega' : 'Recibo'} de la niñera ${viaje.codigoMtrack}',
            creadoEn: momento.millisecondsSinceEpoch,
            usuarioId: usuarioId,
            viajeId: viaje.id,
            parte: parte,
            firma: firma,
            fechaMomento: isoConZona(momento),
            lat: posicion.lat,
            lng: posicion.lng,
            precisionM: posicion.precision,
            reservas: parte == ParteTransferencia.recibe
                ? [for (final e in _reservas.entries) ReservaCarro(vin: e.key, observaciones: e.value)]
                : const [],
          ));
      final sync = ref.read(syncControllerProvider.notifier);
      await sync.refrescar();
      await sync.sincronizar();
      if (!mounted) return;
      final carros = carrosDeLaFirma(viaje).length;
      await anunciarDesenlace(context, ref, jobId, (
        registrado: 'Firma de la niñera registrada',
        detalleRegistrado: 'Quedó para ${carros == 1 ? 'el carro' : 'los $carros carros'} de la niñera.',
        noRegistrado: 'No se pudo registrar la firma',
      ));
      if (!mounted) return;
      _refrescarViaje(viaje.id);
      Navigator.of(context).pop();
    } catch (e) {
      mensajes.showSnackBar(SnackBar(content: Text(mensajeDeError(e, 'No se pudo guardar la firma. Inténtalo de nuevo.'))));
    } finally {
      if (mounted) setState(() => _guardando = false);
    }
  }

  /// Con la firma o la constancia el cargue puede quedar cerrado: se vuelve a leer el viaje y también Mis viajes, que
  /// tiene su propia copia y por eso su botón seguía diciendo «Niñera y firma del cargue».
  void _refrescarViaje(String viajeId) {
    ref.invalidate(viajeDeEtapaProvider(viajeId));
    ref.read(misViajesControllerProvider.notifier).cargar().ignore();
  }

  @override
  Widget build(BuildContext context) {
    final viaje = ref.watch(viajeDeEtapaProvider(widget.viajeId));
    return Scaffold(
      appBar: AppBar(title: const Text('Firma de la niñera')),
      body: SafeArea(
        child: switch (viaje) {
          AsyncData(value: final v?) => _cuerpo(context, v),
          AsyncLoading() => const EstadoCargando(),
          _ => EstadoMensaje(
              icono: Icons.wifi_off_rounded,
              colorIcono: Colores.advertencia,
              titulo: 'No se pudo cargar el viaje',
              descripcion: 'Conéctate e inténtalo de nuevo.',
              accion: FilledButton(
                onPressed: () => ref.invalidate(viajeDeEtapaProvider(widget.viajeId)),
                child: const Text('Reintentar'),
              ),
            ),
        },
      ),
    );
  }

  Widget _cuerpo(BuildContext context, Viaje viaje) {
    final tema = Theme.of(context).textTheme;
    final yo = ref.watch(authControllerProvider.select((e) => e.sesion?.usuario.id));
    final parte = parteEnLaNinera(viaje, yo);
    final carros = carrosDeLaFirma(viaje);
    final informes = ref.watch(informesDelViajeProvider((viajeId: viaje.id, proceso: 'cargue')));
    // Sin señal no se puede comprobar qué falta: se deja firmar y el servidor lo comprueba al recibirla (CA-7).
    final faltante = informes.whenOrNull(data: (lista) => faltanteDelCargue(viaje, lista));
    final recibe = parte == ParteTransferencia.recibe;
    // Lo que ya está firmado en cada carro. Mientras no se sepa (sin señal) no se afirma nada: el servidor ignora
    // la misma firma reenviada.
    final inventarios = {for (final c in carros) c.vin: ref.watch(_inventarioDelCarroProvider(c.vin))};
    final transferencias = {
      for (final e in inventarios.entries) e.key: e.value.whenOrNull(data: (inv) => transferenciaDelCargue(inv, viaje.id)),
    };
    final seSabe = inventarios.values.every((v) => v.hasValue);
    final enCola = parte == null || yo == null
        ? false
        : ref.watch(_firmaEnColaProvider((viajeId: viaje.id, parte: parte, usuarioId: yo))).value ?? false;
    final firmados = [for (final c in carros) if (yaFirmo(transferencias[c.vin], parte ?? '')) c.vin];
    final yaFirmeTodo = seSabe && carros.isNotEmpty && firmados.length == carros.length;
    // SUP-004: si quien recibe dejó reservas nuevas, el servidor retira la firma de quien entrega en esos carros.
    final hayQueVolverAFirmar = !recibe && seSabe && firmados.isNotEmpty && firmados.length < carros.length;
    final reservasDelConductor = {
      for (final c in carros)
        if (transferencias[c.vin]?.reservasTexto case final r? when r.trim().isNotEmpty) c.vin: r,
    };
    final otraParte = recibe ? 'la entrega del inspector' : 'el recibo del conductor';

    return ListView(
      padding: const EdgeInsets.all(Espacio.lg),
      children: [
        Tarjeta(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              TextoMono(viaje.codigoMtrack),
              Text([viaje.placa, viaje.unidad, viaje.muelle].whereType<String>().join(' · '), style: tema.bodySmall),
              const SizedBox(height: Espacio.sm),
              Text(
                'Una sola firma de cada parte para los ${carros.length} carros de la niñera. '
                'Los carros que no salen no se firman.',
                style: tema.bodySmall,
              ),
            ],
          ),
        ),
        const SizedBox(height: Espacio.md),
        if (viaje.cargueCerrado)
          Tarjeta(
            child: Text(
              'La entrega del cargue de esta niñera ya está cerrada.',
              style: tema.titleSmall?.copyWith(color: Colores.exito),
            ),
          )
        else if (parte == null)
          Tarjeta(
            child: Text(
              'La niñera la firman el inspector y el conductor del viaje, cada uno desde su sesión.',
              style: tema.bodySmall,
            ),
          )
        else if (enCola)
          Tarjeta(
            child: Text(
              'Tu firma ya está guardada en este teléfono y se enviará cuando haya señal.',
              style: tema.titleSmall?.copyWith(color: Colores.advertencia),
            ),
          )
        else if (yaFirmeTodo)
          Tarjeta(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  recibe ? 'Ya firmaste el recibo de la niñera.' : 'Ya firmaste la entrega de la niñera.',
                  style: tema.titleSmall?.copyWith(color: Colores.exito),
                ),
                Text('Falta $otraParte.', style: tema.bodySmall),
                for (final e in reservasDelConductor.entries)
                  Text('Con reservas ${e.key}: ${e.value}', style: tema.bodySmall?.copyWith(color: Colores.advertencia)),
              ],
            ),
          )
        else ...[
          if (hayQueVolverAFirmar)
            Padding(
              padding: const EdgeInsets.only(bottom: Espacio.sm),
              child: Text(
                'El conductor dejó reservas nuevas y tu firma se retiró en esos carros: firma otra vez la entrega.',
                style: tema.bodyMedium?.copyWith(color: Colores.advertencia),
              ),
            ),
          Text('Carros', style: tema.titleMedium),
          if (recibe)
            Text(
              'Si recibes un carro con alguna observación, tócalo y marca «Recibir con reservas».',
              style: tema.bodySmall,
            ),
          const SizedBox(height: Espacio.sm),
          for (final carro in carros) ...[
            _FilaCarro(
              carro: carro,
              // El inspector ve las reservas que dejó el conductor antes de firmar (CA-13).
              reserva: recibe ? _reservas[carro.vin] : reservasDelConductor[carro.vin],
              alReservar: recibe ? () => _reservar(carro.vin) : null,
            ),
            const SizedBox(height: Espacio.xs),
          ],
          const SizedBox(height: Espacio.md),
          if (informes.hasError)
            Padding(
              padding: const EdgeInsets.only(bottom: Espacio.sm),
              child: Text(
                'Sin señal no se pudo comprobar que el cargue esté completo: se comprueba al enviar la firma.',
                style: tema.bodySmall?.copyWith(color: Colores.advertencia),
              ),
            ),
          LienzoFirma(etiqueta: 'Tu firma', onCambio: (p) => setState(() => _firma = p)),
          const SizedBox(height: Espacio.md),
          if (faltante != null)
            Padding(
              padding: const EdgeInsets.only(bottom: Espacio.sm),
              child: Text(faltante, style: tema.bodySmall?.copyWith(color: Colores.advertencia)),
            ),
          FilledButton(
            onPressed: _firma != null && faltante == null && !informes.isLoading && !_guardando
                ? () => _firmar(viaje, parte)
                : null,
            child: _guardando
                ? const SizedBox(width: 22, height: 22, child: CircularProgressIndicator(strokeWidth: 2.5, color: Colors.white))
                : Text(recibe ? 'Firmar el recibo de la niñera' : 'Firmar la entrega de la niñera'),
          ),
          const SizedBox(height: Espacio.lg),
          Text(
            'Si la otra parte se niega a firmar, deja la constancia de no firma. Es para toda la niñera: si el '
            'desacuerdo es solo con algunos carros, usa las reservas.',
            style: tema.bodySmall,
          ),
          const SizedBox(height: Espacio.sm),
          OutlinedButton(
            onPressed: faltante == null && !informes.isLoading
                ? () async {
                    await Navigator.of(context).push<void>(
                      MaterialPageRoute(
                        builder: (_) => NoFirmaNineraPage(
                          viaje: viaje,
                          parteQueSeNego: recibe ? ParteTransferencia.entrega : ParteTransferencia.recibe,
                        ),
                      ),
                    );
                    if (mounted) _refrescarViaje(viaje.id);
                  }
                : null,
            child: Text(recibe ? 'El inspector se negó a firmar' : 'El conductor se negó a firmar'),
          ),
        ],
      ],
    );
  }
}

class _FilaCarro extends StatelessWidget {
  const _FilaCarro({required this.carro, required this.reserva, required this.alReservar});

  final ViajeVehiculo carro;
  final String? reserva;

  /// Null si quien mira no recibe: solo quien recibe deja reservas.
  final VoidCallback? alReservar;

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context).textTheme;
    return Tarjeta(
      child: InkWell(
        onTap: alReservar,
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            SizedBox(
              width: 72,
              child: Text(carro.posicion == null ? 'Sin puesto' : 'Puesto ${carro.posicion}', style: tema.bodySmall),
            ),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  TextoMono(carro.vin, estilo: tema.bodySmall),
                  if (reserva case final r?)
                    Text('Con reservas: $r', style: tema.bodySmall?.copyWith(color: Colores.advertencia)),
                ],
              ),
            ),
            if (alReservar != null)
              Text(reserva == null ? 'Recibir con reservas' : 'Cambiar', style: tema.bodySmall?.copyWith(color: Colores.primario)),
          ],
        ),
      ),
    );
  }
}

/// Pide la observación de la reserva de un carro. Vacía quita la reserva. Es dueño de su campo de texto: el
/// diálogo sigue en pantalla mientras se cierra. Lo usa también la entrega por parada (S8-HU-04.2 CA-5).
class DialogoReserva extends StatefulWidget {
  const DialogoReserva({super.key, required this.vin, required this.actual});

  final String vin;
  final String actual;

  @override
  State<DialogoReserva> createState() => _EstadoDialogoReserva();
}

class _EstadoDialogoReserva extends State<DialogoReserva> {
  late final _texto = TextEditingController(text: widget.actual)..addListener(() => setState(() {}));

  @override
  void dispose() {
    _texto.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => AlertDialog(
        title: const Text('Recibir con reservas'),
        content: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            TextoMono(widget.vin),
            TextField(
              controller: _texto,
              autofocus: true,
              maxLines: 4,
              maxLength: reservaMax,
              textCapitalization: TextCapitalization.sentences,
              decoration: const InputDecoration(labelText: 'Observación', hintText: 'Qué se recibe con reserva'),
            ),
          ],
        ),
        actions: [
          if (widget.actual.isNotEmpty)
            TextButton(onPressed: () => Navigator.of(context).pop(''), child: const Text('Quitar la reserva')),
          TextButton(onPressed: () => Navigator.of(context).pop(), child: const Text('Cancelar')),
          // Sin texto no hay reserva: no deja seguir (CA-13).
          TextButton(
            onPressed: _texto.text.trim().isEmpty ? null : () => Navigator.of(context).pop(_texto.text.trim()),
            child: const Text('Guardar'),
          ),
        ],
      );
}
