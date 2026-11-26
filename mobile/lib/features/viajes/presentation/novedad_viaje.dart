import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:uuid/uuid.dart';

import '../../../app/proveedores.dart';
import '../../../core/dispositivo/servicios.dart';
import '../../../core/tema/tema.dart';
import '../../auth/presentation/auth_controller.dart';
import '../../catalogos/domain/catalogos.dart';
import '../../catalogos/presentation/catalogos_controller.dart';
import '../../inspecciones/domain/foto_local.dart';
import '../../inspecciones/presentation/camara_page.dart';
import '../../inspecciones/presentation/sellado.dart';
import '../../sync/domain/desenlace_envio.dart';
import '../../sync/domain/job.dart';
import '../../sync/presentation/sync_controller.dart';
import '../../sync/providers.dart';
import '../domain/novedad_viaje.dart';
import 'acciones_conductor.dart';

/// Lo que se dice cuando no hay de dónde sacar los tipos: sin ellos no se puede elegir uno y el servidor lo exige.
const mensajeSinCatalogos = 'Los catálogos aún no se han descargado. Conéctate e inténtalo de nuevo.';

/// Lo que dice la hoja cuando el celular no tiene GPS: la novedad se envía igual (C14).
const avisoSinUbicacion = 'Se registrará sin ubicación';

/// «Novedad del viaje» (S8-HU-07.3 CA-1): el conductor la ve en la tarjeta de su viaje y el inspector en la del
/// camión que reclamó. Coordinador, administrador y super administrador no la ven: el servidor les responde 403.
///
/// [proceso] y [lugarId] son del inspector: el proceso de su reclamo y, en la entrega, la parada que tomó. Sin ellos
/// el servidor no sabe en qué participa y responde 400; al conductor, en cambio, le responde 400 si los manda.
class BotonNovedadViaje extends ConsumerWidget {
  const BotonNovedadViaje({super.key, required this.viajeId, this.proceso, this.lugarId, this.texto = 'Novedad del viaje'});

  final String viajeId;
  final String? proceso;
  final String? lugarId;

  /// Lo que dice el botón. Con dos reclamos en el mismo viaje, nombra de cuál es.
  final String texto;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final rol = ref.watch(authControllerProvider.select((e) => e.sesion?.usuario.rol));
    if (!reportaNovedadViaje(rol)) return const SizedBox.shrink();
    // Se observa para tener la copia lista al tocarlo: leída recién en el toque, la primera vez todavía no está.
    ref.watch(catalogosProvider);
    return OutlinedButton.icon(
      onPressed: () => reportarNovedadViaje(context, viajeId: viajeId, proceso: proceso, lugarId: lugarId),
      icon: const Icon(Icons.report_outlined, size: 20),
      label: Text(texto),
    );
  }
}

/// Abre la hoja, guarda la novedad en la cola y dice cómo quedó **ese** envío (S8-HU-07.3 CA-2 y CA-3).
///
/// Va por la cola aunque haya señal: se reporta en la vía, y lo que vale es la hora y el GPS de cuando se guardó, no
/// los de cuando por fin sale.
Future<void> reportarNovedadViaje(
  BuildContext context, {
  required String viajeId,
  String? proceso,
  String? lugarId,
}) async {
  // El contenedor y no `ref`: la tarjeta que tiene el botón puede reconstruirse mientras la hoja está abierta.
  final contenedor = ProviderScope.containerOf(context, listen: false);
  final mensajes = ScaffoldMessenger.of(context);
  final rol = contenedor.read(authControllerProvider).sesion?.usuario.rol;
  final tipos = tiposNovedadViajePara(contenedor.read(catalogosProvider), rol);
  if (tipos.isEmpty) {
    // Sin el catálogo no hay qué elegir: abrir la hoja sería dejar escribir una novedad que no se puede guardar.
    mensajes.showSnackBar(const SnackBar(content: Text(mensajeSinCatalogos)));
    return;
  }
  final datos = await pedirNovedadViaje(context, tipos: tipos);
  final usuarioId = contenedor.read(authControllerProvider).sesion?.usuario.id;
  if (datos == null || usuarioId == null) return;
  final jobId = const Uuid().v4();
  await contenedor.read(colaRepositorioProvider).agregar(NovedadViajeJob(
        id: jobId,
        etiqueta: 'Novedad del viaje: ${datos.tipoNombre}',
        creadoEn: DateTime.now().millisecondsSinceEpoch,
        usuarioId: usuarioId,
        viajeId: viajeId,
        tipoNovedad: datos.tipo,
        texto: datos.texto,
        fecha: datos.fecha,
        lat: datos.lat,
        lng: datos.lng,
        foto: datos.foto,
        proceso: proceso,
        lugarId: lugarId,
      ));
  final sync = contenedor.read(syncControllerProvider.notifier);
  await sync.refrescar();
  await sync.sincronizar();
  final desenlace = desenlaceDe(
    await contenedor.read(colaRepositorioProvider).listar(),
    jobId,
    pausa: contenedor.read(syncControllerProvider).pausa,
  );
  mensajes.showSnackBar(SnackBar(
    duration: desenlace.salio ? const Duration(seconds: 4) : const Duration(seconds: 8),
    content: Text(textoDesenlaceNovedadViaje(desenlace)),
  ));
}

/// Cómo quedó el envío de la novedad del viaje, dicho donde se originó. Un rechazo lleva el mensaje del servidor, no
/// un código: «Ese tipo de novedad no es para tu rol» le dice a quien la escribió qué pasó (CA-3).
String textoDesenlaceNovedadViaje(DesenlaceEnvio desenlace) => switch (desenlace.fase) {
      FaseEnvio.registrado => 'Novedad del viaje registrada',
      FaseEnvio.enCola => 'En cola: se enviará cuando haya señal',
      FaseEnvio.enEspera =>
        'En cola${desenlace.motivo == null ? '' : ': ${desenlace.motivo}'}. Se reintenta sola ${enPalabras(desenlace.espera!)}.',
      FaseEnvio.rechazado =>
        'No se registró la novedad del viaje${desenlace.motivo == null ? '' : ': ${desenlace.motivo}'}. Quedó en Inspecciones › Cola.',
    };

/// Lo que se elige y se escribe en la hoja, con la hora y el GPS del momento en que se guardó.
typedef DatosNovedadViaje = ({
  String tipo,
  String tipoNombre,
  String texto,
  FotoLocal? foto,
  String fecha,
  double? lat,
  double? lng,
});

/// La hoja de la novedad del viaje: el tipo (de [tipos], por su nombre), el texto obligatorio con su contador y una
/// foto opcional sellada. Null si se cancela.
Future<DatosNovedadViaje?> pedirNovedadViaje(BuildContext context, {required List<ValorCatalogo> tipos}) {
  return showModalBottomSheet<DatosNovedadViaje>(
    context: context,
    isScrollControlled: true,
    showDragHandle: true,
    builder: (_) => _HojaNovedadViaje(tipos: tipos),
  );
}

class _HojaNovedadViaje extends ConsumerStatefulWidget {
  const _HojaNovedadViaje({required this.tipos});

  final List<ValorCatalogo> tipos;

  @override
  ConsumerState<_HojaNovedadViaje> createState() => _EstadoHojaNovedadViaje();
}

class _EstadoHojaNovedadViaje extends ConsumerState<_HojaNovedadViaje> {
  ValorCatalogo? _tipo;
  FotoLocal? _foto;
  bool _tomandoFoto = false;
  final _texto = TextEditingController();

  @override
  void dispose() {
    _texto.dispose();
    super.dispose();
  }

  bool get _completa => _tipo != null && _texto.text.trim().isNotEmpty;

  Future<void> _tomarFoto() async {
    setState(() => _tomandoFoto = true);
    try {
      final temporal = await Navigator.of(context).push<String>(
        MaterialPageRoute(builder: (_) => const CamaraPage(titulo: 'Foto de la novedad del viaje')),
      );
      if (temporal == null || !mounted) return;
      // Sellada con su propia hora y su GPS; sin GPS queda «sin ubicación» y el servidor la acepta igual.
      final foto = await sellarFoto(ref, temporal);
      if (!mounted) return;
      if (foto == null) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('No se pudo guardar la foto. Inténtalo de nuevo.')),
        );
        return;
      }
      setState(() => _foto = foto);
    } finally {
      if (mounted) setState(() => _tomandoFoto = false);
    }
  }

  /// Mientras se pide la lectura del GPS de este momento, al guardar.
  bool _tomandoUbicacion = false;

  /// Al guardar no llegó la lectura del momento y nadie había visto el aviso: ya se mostró, y el siguiente toque la
  /// guarda sin ubicación.
  bool _sinGpsAlGuardar = false;

  /// La hora y el GPS son los de ahora, cuando se guarda: el envío puede salir horas después, sin señal.
  ///
  /// La lectura que muestra la hoja puede ser de horas antes (en «Mis viajes» queda viva toda la jornada) o no haber
  /// llegado todavía; por eso se pide la de este momento con un límite corto. Si no llega, la novedad se guarda sin
  /// ubicación (C14), pero nunca sin haberlo dicho: si el aviso no estaba a la vista, se muestra y se espera otro
  /// toque.
  Future<void> _guardar({required bool avisoVisible}) async {
    Posicion? posicion;
    if (!_sinGpsAlGuardar) {
      setState(() => _tomandoUbicacion = true);
      posicion = await posicionDelMomento(
        ref.read(servicioUbicacionProvider),
        conocida: ref.read(ubicacionActualProvider).value,
      );
      if (!mounted) return;
      setState(() => _tomandoUbicacion = false);
      if (posicion == null && !avisoVisible) {
        setState(() => _sinGpsAlGuardar = true);
        return;
      }
    }
    Navigator.of(context).pop((
      tipo: _tipo!.codigo!,
      tipoNombre: _tipo!.nombre,
      texto: _texto.text.trim(),
      foto: _foto,
      fecha: DateTime.now().toUtc().toIso8601String(),
      lat: posicion?.lat,
      lng: posicion?.lng,
    ));
  }

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context).textTheme;
    final ubicacion = ref.watch(ubicacionActualProvider);
    // C14: sin GPS se envía igual, y se dice antes de guardar para que nadie espere una señal que no hace falta.
    final sinUbicacion = (!ubicacion.isLoading && ubicacion.value == null) || _sinGpsAlGuardar;
    return Padding(
      padding: EdgeInsets.fromLTRB(Espacio.lg, 0, Espacio.lg, MediaQuery.viewInsetsOf(context).bottom + Espacio.lg),
      child: SingleChildScrollView(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Text('Novedad del viaje', style: tema.titleMedium),
            Text('Es del viaje, no de un carro: no suma novedades ni daños a ninguno.', style: tema.bodySmall),
            const SizedBox(height: Espacio.md),
            Text('¿Qué pasó?', style: tema.titleSmall),
            const SizedBox(height: Espacio.sm),
            Wrap(
              spacing: Espacio.sm,
              runSpacing: Espacio.xs,
              children: [
                for (final tipo in widget.tipos)
                  ChoiceChip(
                    label: Text(tipo.nombre),
                    selected: _tipo?.codigo == tipo.codigo,
                    materialTapTargetSize: MaterialTapTargetSize.padded,
                    onSelected: (_) => setState(() => _tipo = tipo),
                  ),
              ],
            ),
            const SizedBox(height: Espacio.md),
            TextField(
              controller: _texto,
              onChanged: (_) => setState(() {}),
              // El servidor responde 400 «Escribe máximo 300 caracteres» con más: el contador lo dice antes.
              maxLength: novedadViajeTextoMax,
              minLines: 2,
              maxLines: 5,
              textCapitalization: TextCapitalization.sentences,
              decoration: const InputDecoration(labelText: 'Qué pasó', hintText: 'Escribe qué pasó'),
            ),
            const SizedBox(height: Espacio.sm),
            if (_foto == null)
              OutlinedButton.icon(
                onPressed: _tomandoFoto ? null : _tomarFoto,
                icon: const Icon(Icons.photo_camera_outlined, size: 20),
                label: const Text('Agregar foto (opcional)'),
              )
            else
              Row(
                children: [
                  const Icon(Icons.check_circle_outline, color: Colores.exito, size: 20),
                  const SizedBox(width: Espacio.xs),
                  Expanded(
                    child: Text(
                      _foto!.sinUbicacion ? 'Foto agregada, sin ubicación' : 'Foto agregada',
                      style: tema.bodySmall,
                    ),
                  ),
                  TextButton(onPressed: () => setState(() => _foto = null), child: const Text('Quitar')),
                ],
              ),
            if (sinUbicacion) ...[
              const SizedBox(height: Espacio.sm),
              Semantics(
                liveRegion: true,
                child: Text(avisoSinUbicacion, style: tema.bodySmall?.copyWith(color: Colores.advertencia)),
              ),
            ],
            const SizedBox(height: Espacio.md),
            FilledButton(
              onPressed: _completa && !_tomandoFoto && !_tomandoUbicacion
                  ? () => _guardar(avisoVisible: sinUbicacion)
                  : null,
              child: Text(switch ((_tomandoUbicacion, _sinGpsAlGuardar)) {
                (true, _) => 'Tomando la ubicación…',
                (_, true) => 'Guardar sin ubicación',
                _ => 'Guardar novedad',
              }),
            ),
          ],
        ),
      ),
    );
  }
}
