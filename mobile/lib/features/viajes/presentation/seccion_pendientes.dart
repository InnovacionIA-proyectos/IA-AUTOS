import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../app/proveedores.dart';
import '../../../core/dispositivo/servicios.dart';
import '../../../core/errores/error_api.dart';
import '../../../core/tema/tema.dart';
import '../../../core/ui/widgets.dart';
import '../domain/entrega_punto.dart';
import '../domain/pendiente.dart';
import '../providers.dart';
import 'mis_viajes_controller.dart';
import 'tomados.dart';

/// La hora en Colombia (UTC-5, sin horario de verano), que es la que dice el servidor en sus mensajes: «09:20».
String horaDeColombia(DateTime momento) {
  final colombia = momento.toUtc().subtract(const Duration(hours: 5));
  return '${colombia.hour.toString().padLeft(2, '0')}:${colombia.minute.toString().padLeft(2, '0')}';
}

/// Lo que se le dice al inspector cuando no pudo tomar un camión.
///
/// S8-HU-05.2 CA-7 (ESC-08): si el conductor ya cerró la parada sin inspector del punto, el cierre es definitivo y se
/// dice a qué hora, con `details.cerrada_at`. Lo demás («Ya fue tomado por otro inspector») es el motivo del servidor,
/// tal cual: es lo único que explica por qué no.
String mensajeDelReclamo(Object error) {
  if (error case ErrorApi(status: 409, details: final Map<dynamic, dynamic> detalles)
      when detalles['motivo'] == MotivoParada.entregaCerradaSinInspector) {
    if (detalles['cerrada_at'] case final String cerrada) {
      if (DateTime.tryParse(cerrada) case final hora?) {
        return 'Esta entrega se cerró sin inspector del punto a las ${horaDeColombia(hora)}: el cierre es definitivo';
      }
    }
    return error.message;
  }
  return mensajeDeError(error, 'No se pudo tomar. Inténtalo de nuevo.');
}

/// Lo que se dice al tomar una entrega sin GPS en ese instante (S8-HU-09.2 CA-4).
const mensajeTomadoSinUbicacion = 'Se tomó sin ubicación: el coordinador no sabrá si estabas en el lugar';

/// Lo más que se espera al GPS antes de tomar la entrega sin él. La lectura rápida ya se rinde sola a los pocos
/// segundos; esto es solo para que ningún teléfono raro deje el botón colgado.
const esperaGpsAlReclamar = Duration(seconds: 6);

/// La posición de este momento para tomar una entrega, o null si el GPS no la da ya (S8-HU-09.2 CA-4).
///
/// Solo una lectura **de ahora** (`delMomento`): la última conocida puede ser de la entrada al patio o de otra
/// ciudad, y mandarla diría que quien tomó la entrega estaba donde no estaba. Sin GPS, sin permiso o tardando
/// demasiado devuelve null y el reclamo sale sin ubicación: nunca se bloquea por el GPS.
Future<Posicion?> posicionParaReclamar(ServicioUbicacion gps) async {
  try {
    final posicion = await gps.obtenerRapida().timeout(esperaGpsAlReclamar);
    return posicion != null && posicion.delMomento() ? posicion : null;
  } catch (_) {
    return null;
  }
}

/// «Por tomar»: los camiones de los puntos del inspector que todavía no son suyos, con el botón «Tomar».
///
/// Lo que el inspector ya tomó no sale aquí: pasa a «Mis viajes», que es lo que tiene que inspeccionar, y desde ahí
/// se libera. Lo que tiene otro inspector se ve en gris y sin botón, para que nadie lo busque.
///
/// Tomar y liberar van directo a la API: lo decide el servidor, y si otro inspector se adelantó se dice ahí mismo y
/// la lista se actualiza sin perder nada de lo que había.
class SeccionPendientes extends ConsumerWidget {
  const SeccionPendientes({super.key});

  Future<void> _tomar(BuildContext context, WidgetRef ref, Pendiente pendiente) async {
    final mensajes = ScaffoldMessenger.of(context);
    // El contenedor y no `ref`: al tomarlo, la tarjeta sale de la lista y este widget puede ya no estar.
    final contenedor = ProviderScope.containerOf(context, listen: false);
    // S8-HU-09.2 CA-4: la entrega se toma con el GPS de este momento, para que el servidor sepa si quien la tomó
    // estaba en el lugar. Reclamar va directo a la API (necesita señal), así que la ubicación es la de ahora, no una
    // guardada. El cargue no la lleva: allá el servidor no la usa.
    final posicion = pendiente.esEntrega ? await posicionParaReclamar(contenedor.read(servicioUbicacionProvider)) : null;
    try {
      // La entrega se toma por parada (S8-HU-04.2 CA-10): si otro inspector ya tomó esta, el servidor dice «Ya fue
      // tomado por otro inspector» y eso es lo que se muestra.
      await contenedor
          .read(reclamosRepositorioProvider)
          .reclamar(pendiente.viajeId, pendiente.proceso, lugarId: pendiente.parada, posicion: posicion);
    } catch (e) {
      // El motivo del servidor, tal cual («Ya fue tomado por otro inspector»), o el cierre sin inspector del punto con
      // su hora (S8-HU-05.2 CA-7).
      mensajes.showSnackBar(SnackBar(content: Text(mensajeDelReclamo(e))));
      contenedor.invalidate(pendientesProvider);
      return;
    }
    contenedor.invalidate(pendientesProvider);
    mensajes.showSnackBar(
      SnackBar(
        content: Text(
          pendiente.esEntrega && posicion == null
              ? '${nombreDelCamion(pendiente)} quedó en Mis viajes. $mensajeTomadoSinUbicacion'
              : '${nombreDelCamion(pendiente)} quedó en Mis viajes',
        ),
      ),
    );
    // Tomar el cargue hace al inspector inspector del viaje: el viaje y sus unidades llegan con «Mis viajes».
    await contenedor.read(misViajesControllerProvider.notifier).cargar();
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final tema = Theme.of(context).textTheme;
    final pendientes = ref.watch(pendientesProvider);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Row(
          children: [
            Expanded(child: Text('Por tomar', style: tema.titleMedium)),
            IconButton(
              tooltip: 'Actualizar lo que hay por tomar',
              onPressed: () => ref.invalidate(pendientesProvider),
              icon: const Icon(Icons.refresh),
            ),
          ],
        ),
        // Orden visual: lo libre, con su franja verde, arriba; lo de otro inspector, en gris, aparte; lo tuyo, con la
        // franja azul, en «Mis viajes».
        Text(leyendaPorTomar, style: tema.bodySmall),
        const SizedBox(height: Espacio.sm),
        pendientes.when(
          loading: () => const Padding(padding: EdgeInsets.all(Espacio.lg), child: Center(child: CircularProgressIndicator())),
          error: (_, _) => Tarjeta(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  'No se pudo consultar lo que hay por tomar. Se consulta con señal porque cada camión lo toma una sola persona.',
                  style: tema.bodySmall,
                ),
                TextButton(onPressed: () => ref.invalidate(pendientesProvider), child: const Text('Reintentar')),
              ],
            ),
          ),
          // S8-HU-10.2: dos listas, siempre visibles. Cada pendiente es un (viaje, proceso), así que cae en una sola:
          // lo que no es entrega se carga (un proceso desconocido también, como lo lee `Pendiente.fromJson`).
          data: (lista) {
            final porTomar = [for (final p in lista) if (p.estado != EstadoPendiente.mio) p];
            return Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                _ListaPendientes(
                  titulo: 'Por recibir',
                  vacia: 'Nada por recibir',
                  pendientes: [for (final p in porTomar) if (p.esEntrega) p],
                  alTomar: (p) => _tomar(context, ref, p),
                ),
                const SizedBox(height: Espacio.md),
                _ListaPendientes(
                  titulo: 'Por cargar',
                  vacia: 'Nada por cargar',
                  pendientes: [for (final p in porTomar) if (!p.esEntrega) p],
                  alTomar: (p) => _tomar(context, ref, p),
                ),
              ],
            );
          },
        ),
      ],
    );
  }
}

/// Lo que dice «Por tomar» debajo del título: cómo se distingue cada cosa.
const leyendaPorTomar =
    'Las libres tienen franja verde y se toman aquí. Las que ya tomó otro inspector van aparte, en gris. Las tuyas '
    'pasan a «Mis viajes», con franja azul.';

/// El subtítulo del grupo de lo que ya tiene otro inspector, con cuántas son.
String tituloDeLoAjeno(int cuantas) => 'Las tiene otro inspector ($cuantas)';

/// Una de las dos listas del punto, con su conteo. Vacía no desaparece: dice que no hay nada.
///
/// Primero lo que se puede tomar, con su franja verde; debajo, aparte y en gris, lo que ya tiene otro inspector. Así
/// no se confunde lo que todavía nadie reclamó con lo que cada inspector ya fue tomando.
class _ListaPendientes extends StatelessWidget {
  const _ListaPendientes({required this.titulo, required this.vacia, required this.pendientes, required this.alTomar});

  final String titulo;
  final String vacia;
  final List<Pendiente> pendientes;
  final Future<void> Function(Pendiente) alTomar;

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context).textTheme;
    final libres = [for (final p in pendientes) if (p.estado == EstadoPendiente.disponible) p];
    final ajenos = [for (final p in pendientes) if (p.estado != EstadoPendiente.disponible) p];
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Padding(
          padding: const EdgeInsets.only(bottom: Espacio.sm),
          child: Text('$titulo (${pendientes.length})', style: tema.titleSmall),
        ),
        if (pendientes.isEmpty)
          Tarjeta(child: Text(vacia, style: tema.bodySmall))
        else ...[
          for (final pendiente in libres) ...[
            _TarjetaPendiente(pendiente: pendiente, alTomar: () => alTomar(pendiente)),
            const SizedBox(height: Espacio.sm),
          ],
          if (ajenos.isNotEmpty) ...[
            Padding(
              padding: const EdgeInsets.only(top: Espacio.xs, bottom: Espacio.xs),
              child: Row(
                children: [
                  const Icon(Icons.person_outline, size: 16, color: Colores.textoSecundario),
                  const SizedBox(width: Espacio.xs),
                  Text(tituloDeLoAjeno(ajenos.length), style: tema.bodySmall?.copyWith(fontWeight: FontWeight.w600)),
                ],
              ),
            ),
            for (final pendiente in ajenos) ...[
              _TarjetaPendiente(pendiente: pendiente, alTomar: () => alTomar(pendiente)),
              const SizedBox(height: Espacio.sm),
            ],
          ],
        ],
      ],
    );
  }
}

class _TarjetaPendiente extends StatefulWidget {
  const _TarjetaPendiente({required this.pendiente, required this.alTomar});

  final Pendiente pendiente;
  final Future<void> Function() alTomar;

  @override
  State<_TarjetaPendiente> createState() => _EstadoTarjetaPendiente();
}

class _EstadoTarjetaPendiente extends State<_TarjetaPendiente> {
  /// Mientras se toma (con la espera del GPS de la entrega, hasta unos segundos) el botón no se puede volver a tocar:
  /// un segundo toque mandaba otro reclamo (S8-HU-09.2 CA-4).
  bool _tomando = false;

  Future<void> _tomar() async {
    setState(() => _tomando = true);
    try {
      await widget.alTomar();
    } finally {
      if (mounted) setState(() => _tomando = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final pendiente = widget.pendiente;
    final tema = Theme.of(context).textTheme;
    final libre = pendiente.estado == EstadoPendiente.disponible;
    final tarjeta = Tarjeta(
      franja: libre ? Colores.exito : Colores.textoSecundario,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              Expanded(child: TextoMono(pendiente.placa ?? pendiente.codigoMtrack ?? 'Sin placa')),
              if (libre)
                const Insignia('Disponible', tono: TonoInsignia.exito)
              else
                Insignia('Lo tiene ${pendiente.reclamadoPorNombre ?? 'otro inspector'}', tono: TonoInsignia.neutro),
            ],
          ),
          if (pendiente.placa != null && pendiente.codigoMtrack != null) Text(pendiente.codigoMtrack!, style: tema.bodySmall),
          Text(detalleDelPendiente(pendiente), style: tema.bodySmall),
          if (libre) ...[
            const SizedBox(height: Espacio.sm),
            FilledButton(onPressed: _tomando ? null : _tomar, child: Text(_tomando ? 'Tomando…' : 'Tomar')),
          ],
        ],
      ),
    );
    return libre ? tarjeta : Opacity(opacity: 0.6, child: tarjeta);
  }
}
