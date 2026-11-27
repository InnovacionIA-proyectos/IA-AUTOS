import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/db/cache_lecturas.dart';
import '../../auth/presentation/auth_controller.dart';

import '../../../core/errores/error_api.dart';
import '../../../core/tema/tema.dart';
import '../../../core/ui/widgets.dart';
import '../domain/pendiente.dart';
import '../providers.dart';
import 'detalle_pendiente_page.dart';
import 'entrega_punto_page.dart';
import 'mis_viajes_controller.dart';
import 'novedad_viaje.dart';

/// Lo que el inspector tomó de «Por tomar» y ahora le toca inspeccionar: cómo se nombra, qué papel tiene en el viaje
/// y cómo se libera.

/// Dónde se guarda en el teléfono lo último que el inspector tomó: «Por tomar» solo se consulta con señal, y sin
/// esto una entrega tomada desaparecía de «Mis viajes» al abrir la app sin señal. Las pruebas lo reemplazan.
abstract interface class AlmacenTomados {
  Future<String?> leer(String clave);
  Future<void> escribir(String clave, String valor);
}

class _TomadosEnCache implements AlmacenTomados {
  const _TomadosEnCache(this._cache);

  final CacheLecturasRepo _cache;

  @override
  Future<String?> leer(String clave) => _cache.leer(clave);

  @override
  Future<void> escribir(String clave, String valor) => _cache.escribir(clave, valor);
}

final almacenTomadosProvider = Provider<AlmacenTomados>((ref) => _TomadosEnCache(ref.watch(cacheViajesProvider)));

/// Lo tomado, por persona: en un equipo compartido nadie ve lo de otro.
String claveTomados(String usuarioId) => 'tomados.$usuarioId';

/// Lo último que se supo que esta persona tenía tomado. Vacío si nunca se guardó o la copia está dañada.
final tomadosGuardadosProvider = FutureProvider.autoDispose<List<Pendiente>>((ref) async {
  final usuarioId = ref.watch(authControllerProvider.select((e) => e.sesion?.usuario.id));
  if (usuarioId == null) return const [];
  final crudo = await ref.watch(almacenTomadosProvider).leer(claveTomados(usuarioId));
  if (crudo == null) return const [];
  try {
    return [for (final p in jsonDecode(crudo) as List<dynamic>) Pendiente.fromJson(p as Map<String, dynamic>)];
  } catch (_) {
    return const [];
  }
});

/// Guarda lo que esta persona tiene tomado según la última consulta de «Por tomar».
Future<void> guardarTomados(AlmacenTomados almacen, String usuarioId, List<Pendiente> pendientes) => almacen.escribir(
      claveTomados(usuarioId),
      jsonEncode([for (final p in pendientes) if (p.estado == EstadoPendiente.mio) p.toJson()]),
    );

/// La placa del camión, o su código si no trae placa.
String nombreDelCamion(Pendiente pendiente) => pendiente.placa ?? pendiente.codigoMtrack ?? 'El camión';

/// «Entrega · CEDI Envigado · 08:30 · 2 unidades».
String detalleDelPendiente(Pendiente pendiente) {
  final hora = pendiente.fechaProgramada?.toLocal();
  return [
    pendiente.esEntrega ? 'Entrega' : 'Cargue',
    pendiente.lugar,
    if (hora != null) '${hora.hour.toString().padLeft(2, '0')}:${hora.minute.toString().padLeft(2, '0')}',
    if (pendiente.unidades > 0) '${pendiente.unidades} ${pendiente.unidades == 1 ? 'unidad' : 'unidades'}',
  ].where((p) => p.isNotEmpty).join(' · ');
}

/// «Tú cargas», «Tú recibes» o «Tú cargas y recibes» según lo que el inspector tomó del viaje; null si no tomó nada.
///
/// Mientras el servidor no mande el papel en el viaje (S8-HU-10.1), se lee de lo que tomó en «Por tomar».
String? etiquetaDelPapel(Iterable<Pendiente> tomados) {
  final recibe = tomados.any((p) => p.esEntrega);
  final carga = tomados.any((p) => !p.esEntrega);
  return switch ((carga, recibe)) {
    (true, true) => 'Tú cargas y recibes',
    (true, false) => 'Tú cargas',
    (false, true) => 'Tú recibes',
    _ => null,
  };
}

/// Pregunta antes de liberar: el camión vuelve a «Por tomar» para todos.
Future<bool> confirmarLiberar(BuildContext context) async =>
    await showDialog<bool>(
      context: context,
      builder: (contexto) => AlertDialog(
        title: const Text('Liberar el camión'),
        content: const Text(
          'Vuelve a quedar pendiente para los demás inspectores. Lo que ya capturaste se conserva a tu nombre.',
        ),
        actions: [
          TextButton(onPressed: () => Navigator.of(contexto).pop(false), child: const Text('Cancelar')),
          TextButton(onPressed: () => Navigator.of(contexto).pop(true), child: const Text('Liberar')),
        ],
      ),
    ) ==
    true;

/// Libera lo tomado desde «Mis viajes»: confirma, va a la API y refresca las dos listas. El camión vuelve a «Por tomar».
/// Devuelve cuando terminó, para que el botón no se pueda tocar otra vez mientras tanto.
Future<void> liberarTomado(BuildContext context, Pendiente pendiente) async {
  if (!await confirmarLiberar(context) || !context.mounted) return;
  final mensajes = ScaffoldMessenger.of(context);
  // El contenedor y no `ref`: al liberarlo, la tarjeta sale de «Mis viajes» y quien la pintó puede ya no estar.
  final contenedor = ProviderScope.containerOf(context, listen: false);
  try {
    await contenedor
        .read(reclamosRepositorioProvider)
        .liberar(pendiente.viajeId, pendiente.proceso, lugarId: pendiente.parada);
  } catch (e) {
    mensajes.showSnackBar(SnackBar(content: Text(mensajeDeError(e, 'No se pudo liberar. Inténtalo de nuevo.'))));
    return;
  }
  contenedor.invalidate(pendientesProvider);
  mensajes.showSnackBar(SnackBar(content: Text('${nombreDelCamion(pendiente)} volvió a «Por tomar»')));
  await contenedor.read(misViajesControllerProvider.notifier).cargar();
}

/// Los botones «Liberar» de lo tomado en un viaje: uno por proceso (y en la entrega, uno por parada), y si son
/// varios dicen cuál es cuál.
class BotonesLiberar extends StatefulWidget {
  const BotonesLiberar({super.key, required this.tomados});

  final List<Pendiente> tomados;

  @override
  State<BotonesLiberar> createState() => _EstadoBotonesLiberar();
}

class _EstadoBotonesLiberar extends State<BotonesLiberar> {
  /// Mientras se libera, no se libera otra vez: el segundo toque recibía un 404 y mostraba un error falso.
  bool _liberando = false;

  Future<void> _liberar(Pendiente tomado) async {
    setState(() => _liberando = true);
    try {
      await liberarTomado(context, tomado);
    } finally {
      if (mounted) setState(() => _liberando = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final tomados = widget.tomados;
    // Dos paradas del mismo viaje son dos entregas: el botón nombra la parada para no liberar la que no era.
    final variasEntregas = tomados.where((t) => t.esEntrega).length > 1;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        for (final tomado in tomados)
          OutlinedButton(
            onPressed: _liberando ? null : () => _liberar(tomado),
            child: Text(
              switch (tomado) {
                _ when tomados.length == 1 => 'Liberar',
                Pendiente(esEntrega: true) when variasEntregas => 'Liberar la entrega · ${tomado.lugar}',
                Pendiente(esEntrega: true) => 'Liberar la entrega',
                _ => 'Liberar el cargue',
              },
            ),
          ),
      ],
    );
  }
}

/// Un camión tomado cuyo viaje todavía no llega en «Mis viajes»: hoy, la entrega, hasta que el servidor la incluya
/// (S8-HU-10.1). Se pinta con lo que trae «Por tomar», y sus unidades se ven en el detalle.
class TarjetaTomada extends StatelessWidget {
  const TarjetaTomada({super.key, required this.pendiente});

  final Pendiente pendiente;

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context).textTheme;
    // Tomada por quien mira: la misma franja azul de sus viajes, que la separa de lo que está en «Por tomar».
    return Tarjeta(
      franja: Colores.primario,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              Expanded(child: TextoMono(pendiente.codigoMtrack ?? pendiente.placa ?? 'Sin código')),
              Insignia(etiquetaDelPapel([pendiente])!, tono: TonoInsignia.info),
            ],
          ),
          if (pendiente.placa != null && pendiente.codigoMtrack != null) TextoMono(pendiente.placa!, estilo: tema.bodySmall),
          Text(detalleDelPendiente(pendiente), style: tema.bodySmall),
          const SizedBox(height: Espacio.sm),
          FilledButton(
            onPressed: () async {
              final contenedor = ProviderScope.containerOf(context, listen: false);
              await Navigator.of(context).push<void>(
                MaterialPageRoute(builder: (_) => DetallePendientePage(pendiente: pendiente)),
              );
              contenedor.invalidate(pendientesProvider);
              await contenedor.read(misViajesControllerProvider.notifier).cargar();
            },
            child: const Text('Ver unidades'),
          ),
          // S8-HU-04.2 CA-1: el inspector entrega solo en la parada que tomó.
          if (pendiente.esEntrega && pendiente.lugarId != null) ...[
            const SizedBox(height: Espacio.sm),
            BotonEntregarEnParada(viajeId: pendiente.viajeId, lugarId: pendiente.lugarId!, lugar: pendiente.lugar),
          ],
          // S8-HU-07.3 CA-1: la novedad del viaje del camión que tomó, con el proceso de su reclamo y, en la entrega,
          // la parada (sin ella el servidor responde 400).
          if (!pendiente.esEntrega || pendiente.lugarId != null) ...[
            const SizedBox(height: Espacio.sm),
            BotonNovedadViaje(viajeId: pendiente.viajeId, proceso: pendiente.proceso, lugarId: pendiente.parada),
          ],
          const SizedBox(height: Espacio.sm),
          BotonesLiberar(tomados: [pendiente]),
        ],
      ),
    );
  }
}
