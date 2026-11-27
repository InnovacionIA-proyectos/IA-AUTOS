import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/errores/error_api.dart';
import '../../../core/tema/tema.dart';
import '../../../core/ui/widgets.dart';
import '../../sync/presentation/sync_controller.dart';
import '../domain/permiso_sin_inspector.dart';
import '../providers.dart';

/// Cada cuánto se le vuelve a preguntar al servidor cómo va el permiso mientras esta pantalla está abierta.
///
/// Es una comodidad, no el mecanismo: quien concede por tiempo es un proceso del servidor, así que el permiso
/// avanza igual con la pantalla cerrada, la app en segundo plano o el teléfono apagado.
const refrescoPermiso = Duration(seconds: 20);

/// Cuánto se deja de ofrecer «Continuar el cargue» tras un «espere» del servidor con la cuenta en cero
/// (S8-HU-09.2 CA-2). La cuenta la lleva el reloj del teléfono: si va adelantado, el teléfono dice cero y el servidor
/// todavía no; volver a ofrecer seguir solo repetiría el mismo rechazo. Pasado esto, o con el permiso ya concedido
/// por el servidor, se vuelve a ofrecer.
const margenTrasRechazo = Duration(minutes: 2);

/// La espera del permiso de la excepción «No hay inspector», en un punto de cargue que sí tiene inspectores.
///
/// Enseña cuánto falta para que lo conceda el reloj y dice las dos salidas que tiene la espera: que responda un
/// coordinador o que venza el plazo. Concedido, de aquí se sigue al cargue sin volver a preguntar el motivo.
class PermisoSinInspectorPage extends ConsumerStatefulWidget {
  const PermisoSinInspectorPage({
    super.key,
    required this.viajeId,
    required this.alContinuar,
    this.enLaLlegada = false,
    this.trasRechazo = false,
  });

  final String viajeId;

  /// Se abre porque el servidor acaba de responder que espere, con lo capturado ya en la cola (S8-HU-09.2 CA-2).
  final bool trasRechazo;

  /// Qué hacer cuando el permiso ya está concedido: seguir con la revisión del cargue, con el motivo que se pidió.
  final void Function(PermisoSinInspector permiso) alContinuar;

  /// Se abrió desde la llegada del cargue (S8-HU-07.3 CA-7): los textos hablan de esa revisión, la misma que en la
  /// ficha dice «Capturar», y no de la salida.
  final bool enLaLlegada;

  @override
  ConsumerState<PermisoSinInspectorPage> createState() => _EstadoPermisoSinInspector();
}

class _EstadoPermisoSinInspector extends ConsumerState<PermisoSinInspectorPage> {
  Timer? _reloj;

  /// El segundero de la cuenta atrás. Solo mueve lo que se ve; lo que decide es lo que responde el servidor.
  DateTime _ahora = DateTime.now();

  /// Cuándo se abrió: tras un rechazo, de aquí se cuenta [margenTrasRechazo].
  final DateTime _abierta = DateTime.now();

  @override
  void initState() {
    super.initState();
    _reloj = Timer.periodic(const Duration(seconds: 1), (_) {
      if (!mounted) return;
      setState(() => _ahora = DateTime.now());
      // Al cumplirse el plazo el servidor concede en la siguiente consulta: no hay por qué hacer esperar otro
      // ciclo completo.
      final segundos = _ahora.difference(_ultimaConsulta).inSeconds;
      if (segundos >= refrescoPermiso.inSeconds) _refrescar();
    });
  }

  DateTime _ultimaConsulta = DateTime.now();

  void _refrescar() {
    _ultimaConsulta = DateTime.now();
    ref.invalidate(permisoSinInspectorProvider(widget.viajeId));
  }

  @override
  void dispose() {
    _reloj?.cancel();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final permiso = ref.watch(permisoSinInspectorProvider(widget.viajeId));
    final retenido = widget.trasRechazo && _ahora.difference(_abierta) < margenTrasRechazo;
    // Cuándo vuelve a salir solo lo que quedó en la cola: es lo que se le dice a quien espera tras el rechazo.
    final proximoIntento = widget.trasRechazo ? ref.watch(syncControllerProvider.select((s) => s.proximoIntento)) : null;
    return Scaffold(
      appBar: AppBar(title: const Text('Permiso para el cargue')),
      body: SafeArea(
        child: permiso.when(
          loading: () => const EstadoCargando(),
          error: (error, _) => EstadoMensaje(
            icono: Icons.warning_amber_rounded,
            colorIcono: Colores.peligro,
            titulo: 'No se pudo consultar el permiso',
            descripcion: mensajeDeError(error, 'El permiso se consulta con señal.'),
            accion: FilledButton(onPressed: _refrescar, child: const Text('Reintentar')),
          ),
          data: (p) => p == null
              ? EstadoMensaje(
                  icono: Icons.hourglass_disabled_outlined,
                  titulo: 'Todavía no hay una solicitud',
                  descripcion: 'Vuelve a la ficha del vehículo y pide el permiso desde la '
                      '${widget.enLaLlegada ? 'llegada' : 'salida'} del cargue.',
                )
              : _Contenido(
                  permiso: p,
                  ahora: _ahora,
                  alRefrescar: _refrescar,
                  alContinuar: widget.alContinuar,
                  enLaLlegada: widget.enLaLlegada,
                  retenido: retenido,
                  proximoIntento: proximoIntento,
                ),
        ),
      ),
    );
  }
}

class _Contenido extends StatelessWidget {
  const _Contenido({
    required this.permiso,
    required this.ahora,
    required this.alRefrescar,
    required this.alContinuar,
    this.enLaLlegada = false,
    this.retenido = false,
    this.proximoIntento,
  });

  final PermisoSinInspector permiso;
  final DateTime ahora;
  final VoidCallback alRefrescar;
  final void Function(PermisoSinInspector permiso) alContinuar;
  final bool enLaLlegada;

  /// El servidor acaba de decir que espere: con la cuenta en cero no se ofrece seguir hasta que lo conceda él o pase
  /// [margenTrasRechazo].
  final bool retenido;

  /// Cuándo la cola vuelve a intentarlo sola. Null si no hay nada programado (sin señal).
  final DateTime? proximoIntento;

  /// «El servidor aún no lo concede; se reintenta solo en 45 s.»
  String get _textoDelReintento => switch (proximoIntento?.difference(ahora)) {
        null => 'El servidor aún no lo concede; se reintenta solo en cuanto haya señal.',
        final f when f <= Duration.zero => 'El servidor aún no lo concede; se reintenta solo en un momento.',
        final f => 'El servidor aún no lo concede; se reintenta solo en ${reloj(f)}.',
      };

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context).textTheme;
    return ListView(
      padding: const EdgeInsets.all(Espacio.lg),
      children: [
        Tarjeta(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(permiso.lugar ?? 'El punto de cargue', style: tema.titleMedium),
              const SizedBox(height: Espacio.xs),
              Text(
                'Motivo: ${permiso.motivo ?? permiso.motivoCodigo}'
                '${permiso.motivoTexto == null ? '' : ' · ${permiso.motivoTexto}'}',
                style: tema.bodySmall,
              ),
            ],
          ),
        ),
        const SizedBox(height: Espacio.lg),
        if (permiso.concedido) ..._concedido(context, tema) else ..._esperando(context, tema),
      ],
    );
  }

  List<Widget> _concedido(BuildContext context, TextTheme tema) => [
        Semantics(
          liveRegion: true,
          child: Tarjeta(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  children: [
                    const Icon(Icons.check_circle_outline, color: Colores.exito),
                    const SizedBox(width: Espacio.sm),
                    Expanded(child: Text('Permiso concedido', style: tema.titleMedium)),
                  ],
                ),
                const SizedBox(height: Espacio.xs),
                Text(
                  permiso.porTiempo
                      ? 'Nadie respondió en el plazo, así que lo concedió el tiempo. Queda registrado así.'
                      : 'Lo concedió ${permiso.concedidoPorNombre ?? 'el coordinador'}.',
                  style: tema.bodySmall,
                ),
              ],
            ),
          ),
        ),
        const SizedBox(height: Espacio.lg),
        FilledButton(
          onPressed: () => alContinuar(permiso),
          // El mismo texto que el botón de ese momento en la ficha (`etiquetaExcepcion`).
          child: Text(enLaLlegada ? 'Capturar' : 'Continuar el cargue'),
        ),
        const SizedBox(height: Espacio.sm),
        Text(
          'Sigues donde ibas: el motivo que elegiste ya está guardado y no se vuelve a preguntar.',
          style: tema.bodySmall,
        ),
      ];

  List<Widget> _esperando(BuildContext context, TextTheme tema) {
    final falta = permiso.faltaPara(ahora);
    final vencido = permiso.vencido(ahora);
    return [
      Semantics(
        liveRegion: true,
        child: Tarjeta(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text('Esperando respuesta', style: tema.titleMedium),
              const SizedBox(height: Espacio.xs),
              Text(
                vencido
                    ? 'Ya se cumplió el plazo: el permiso se concede por tiempo al seguir con el cargue.'
                    : 'Faltan ${reloj(falta)} para que se conceda por tiempo.',
                style: tema.titleLarge,
              ),
              const SizedBox(height: Espacio.sm),
              Text(
                'Puede llegar de dos maneras: que lo conceda un coordinador —y entonces sigues de inmediato— o '
                'que se cumpla el plazo y lo conceda el tiempo.',
                style: tema.bodySmall,
              ),
            ],
          ),
        ),
      ),
      const SizedBox(height: Espacio.lg),
      // S8-HU-09.2 CA-2: en cero ya se sigue, sin esperar a ver el permiso concedido: el servidor lo concede en la
      // misma petición que crea la salida del cargue (ESC-03). Si aun así respondiera que espere, la captura vuelve a
      // traer esta pantalla y lo capturado queda en la cola.
      if (vencido && retenido) ...[
        Semantics(
          liveRegion: true,
          child: Text(_textoDelReintento, style: tema.bodyMedium?.copyWith(color: Colores.advertencia)),
        ),
        const SizedBox(height: Espacio.sm),
      ] else if (vencido) ...[
        FilledButton(onPressed: () => alContinuar(permiso), child: const Text('Continuar el cargue')),
        const SizedBox(height: Espacio.sm),
      ],
      OutlinedButton(onPressed: alRefrescar, child: const Text('Actualizar')),
      const SizedBox(height: Espacio.sm),
      Text(
        'Puedes cerrar la app o apagar la pantalla: la espera corre en el servidor, no aquí. Al volver, esta '
        'pantalla te dice cómo quedó.',
        style: tema.bodySmall,
      ),
    ];
  }
}

/// La cuenta atrás como la lee una persona: «9:04», y en segundos cuando ya queda menos de un minuto.
String reloj(Duration falta) {
  if (falta.inMinutes < 1) return '${falta.inSeconds} s';
  final segundos = falta.inSeconds % 60;
  return '${falta.inMinutes}:${segundos.toString().padLeft(2, '0')} min';
}
