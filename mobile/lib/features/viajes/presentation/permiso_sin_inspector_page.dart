import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/errores/error_api.dart';
import '../../../core/tema/tema.dart';
import '../../../core/ui/widgets.dart';
import '../domain/permiso_sin_inspector.dart';
import '../providers.dart';

/// Cada cuánto se le vuelve a preguntar al servidor cómo va el permiso mientras esta pantalla está abierta.
///
/// Es una comodidad, no el mecanismo: quien concede por tiempo es un proceso del servidor, así que el permiso
/// avanza igual con la pantalla cerrada, la app en segundo plano o el teléfono apagado.
const refrescoPermiso = Duration(seconds: 20);

/// La espera del permiso de la excepción «No hay inspector», en un punto de cargue que sí tiene inspectores.
///
/// Enseña cuánto falta para que lo conceda el reloj y dice las dos salidas que tiene la espera: que responda un
/// coordinador o que venza el plazo. Concedido, de aquí se sigue al cargue sin volver a preguntar el motivo.
class PermisoSinInspectorPage extends ConsumerStatefulWidget {
  const PermisoSinInspectorPage({super.key, required this.viajeId, required this.alContinuar});

  final String viajeId;

  /// Qué hacer cuando el permiso ya está concedido: seguir con la salida del cargue, con el motivo que se pidió.
  final void Function(PermisoSinInspector permiso) alContinuar;

  @override
  ConsumerState<PermisoSinInspectorPage> createState() => _EstadoPermisoSinInspector();
}

class _EstadoPermisoSinInspector extends ConsumerState<PermisoSinInspectorPage> {
  Timer? _reloj;

  /// El segundero de la cuenta atrás. Solo mueve lo que se ve; lo que decide es lo que responde el servidor.
  DateTime _ahora = DateTime.now();

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
              ? const EstadoMensaje(
                  icono: Icons.hourglass_disabled_outlined,
                  titulo: 'Todavía no hay una solicitud',
                  descripcion: 'Vuelve a la ficha del vehículo y pide el permiso desde la salida del cargue.',
                )
              : _Contenido(permiso: p, ahora: _ahora, alRefrescar: _refrescar, alContinuar: widget.alContinuar),
        ),
      ),
    );
  }
}

class _Contenido extends StatelessWidget {
  const _Contenido({required this.permiso, required this.ahora, required this.alRefrescar, required this.alContinuar});

  final PermisoSinInspector permiso;
  final DateTime ahora;
  final VoidCallback alRefrescar;
  final void Function(PermisoSinInspector permiso) alContinuar;

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
          child: const Text('Continuar el cargue'),
        ),
        const SizedBox(height: Espacio.sm),
        Text(
          'Sigues donde ibas: el motivo que elegiste ya está guardado y no se vuelve a preguntar.',
          style: tema.bodySmall,
        ),
      ];

  List<Widget> _esperando(BuildContext context, TextTheme tema) {
    final falta = permiso.faltaPara(ahora);
    final vencido = falta == Duration.zero;
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
                    ? 'Ya se cumplió el plazo: el permiso se concede de un momento a otro.'
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
