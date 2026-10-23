import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../app/proveedores.dart';
import '../../../core/errores/error_api.dart';
import '../../../core/tema/tema.dart';
import '../../../core/ui/widgets.dart';
import '../../../core/util/formato.dart';
import '../../auth/presentation/auth_controller.dart';
import '../../catalogos/domain/catalogos.dart';
import '../../catalogos/presentation/catalogos_controller.dart';
import '../domain/etiquetas.dart';
import '../domain/inventario.dart';
import '../providers.dart';
import 'diagrama_vehiculo.dart';

final inventarioProvider = FutureProvider.autoDispose.family<Inventario, String>(
  (ref, vin) => ref.watch(vehiculosRepositorioProvider).inventario(vin),
);

/// URL absoluta de una imagen servida por la API (las evidencias vienen con ruta relativa).
String? resolverUrlImagen(String apiUrl, String? relativa) {
  if (relativa == null || relativa.isEmpty) return null;
  return relativa.startsWith('http') ? relativa : '$apiUrl$relativa';
}

class VinPage extends ConsumerWidget {
  const VinPage({super.key, required this.vin});

  final String vin;

  Future<void> _ir(BuildContext context, WidgetRef ref, String ruta) async {
    await context.push(ruta);
    // Al volver de capturar o firmar, el inventario puede haber cambiado.
    ref.invalidate(inventarioProvider(vin));
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final inventario = ref.watch(inventarioProvider(vin));
    return Scaffold(
      appBar: AppBar(title: TextoMono(vin)),
      body: inventario.when(
        loading: () => const EstadoCargando(),
        error: (error, _) => EstadoMensaje(
          icono: Icons.warning_amber_rounded,
          colorIcono: Colores.peligro,
          titulo: 'No se pudo cargar el VIN',
          descripcion: error is ErrorApi && error.status == 404 ? 'Este VIN no está registrado. Pide al administrador que lo cargue.' : mensajeDeError(error),
          accion: FilledButton(onPressed: () => ref.invalidate(inventarioProvider(vin)), child: const Text('Reintentar')),
        ),
        data: (inv) => RefreshIndicator(
          onRefresh: () => ref.refresh(inventarioProvider(vin).future),
          child: _Detalle(inventario: inv, alIr: (ruta) => _ir(context, ref, ruta)),
        ),
      ),
    );
  }
}

class _Detalle extends ConsumerWidget {
  const _Detalle({required this.inventario, required this.alIr});

  final Inventario inventario;
  final void Function(String ruta) alIr;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final tema = Theme.of(context).textTheme;
    final catalogos = ref.watch(catalogosProvider);
    final ultima = inventario.ultimaInspeccion;
    final vinRuta = Uri.encodeComponent(inventario.vin);
    final rol = ref.watch(authControllerProvider.select((e) => e.sesion?.usuario.rol)) ?? '';

    return ListView(
      padding: const EdgeInsets.all(Espacio.lg),
      children: [
        Tarjeta(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              TextoMono(inventario.vin),
              Text(
                '${inventario.marca}${inventario.modelo != null ? ' ${inventario.modelo}' : ''} · ${etiquetar(etiquetaOperacion, inventario.operacion)}',
                style: tema.bodySmall,
              ),
            ],
          ),
        ),
        const SizedBox(height: Espacio.lg),
        Text('Último informe', style: tema.titleMedium),
        const SizedBox(height: Espacio.sm),
        if (ultima == null)
          Tarjeta(child: Text('Este VIN aún no tiene informes.', style: tema.bodySmall))
        else
          _UltimoInforme(etapa: ultima.etapa, inspeccion: ultima.inspeccion),
        const SizedBox(height: Espacio.lg),
        Text('Cambio de responsabilidad', style: tema.titleMedium),
        const SizedBox(height: Espacio.sm),
        if (inventario.etapas.isEmpty)
          Tarjeta(child: Text('Este VIN no tiene ruta de custodia. El administrador debe crear las etapas.', style: tema.bodySmall))
        else
          for (final etapa in inventario.etapas) ...[
            _TarjetaEtapa(
              etapa: etapa,
              procesoNombre: catalogos.etiqueta(Catalogo.etapas, etapa.proceso),
              momentos: momentosDeEtapa(etapa, inventario),
              capturaAjena: !puedeCapturar(etapa, rol: rol),
              accion: accionDeEtapa(etapa, inventario),
              // El bloqueo nombra la etapa anterior, y el nombre sale del catálogo: el dominio solo da su código.
              nombreDeEtapa: (codigo) => catalogos.etiqueta(Catalogo.etapas, codigo),
              alCapturar: (momento) => alIr(
                '/inspecciones/vin/$vinRuta/captura/${etapa.id}?proceso=${Uri.encodeQueryComponent(catalogos.etiqueta(Catalogo.etapas, etapa.proceso))}'
                '&momento=$momento'
                '${etapa.viajeId == null ? '' : '&viajeId=${Uri.encodeQueryComponent(etapa.viajeId!)}'}',
              ),
              alFirmar: (accion) => alIr(
                '/inspecciones/vin/$vinRuta/firma/${accion.inspeccionId}?proceso=${Uri.encodeQueryComponent(catalogos.etiqueta(Catalogo.etapas, etapa.proceso))}&soloFirma=${accion.soloFirma ? 1 : 0}',
              ),
            ),
            const SizedBox(height: Espacio.sm),
          ],
      ],
    );
  }
}

class _UltimoInforme extends ConsumerWidget {
  const _UltimoInforme({required this.etapa, required this.inspeccion});

  final EtapaVista etapa;
  final InspeccionVista inspeccion;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final tema = Theme.of(context).textTheme;
    final catalogos = ref.watch(catalogosProvider);
    final apiUrl = ref.watch(entornoProvider).apiUrl;
    final token = ref.watch(authControllerProvider.select((e) => e.sesion?.token));
    final fotos = [for (final e in inspeccion.evidencias) if (resolverUrlImagen(apiUrl, e.url) != null) resolverUrlImagen(apiUrl, e.url)!];

    return Tarjeta(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Expanded(
                child: Text(
                  '${catalogos.etiqueta(Catalogo.etapas, etapa.proceso)} · ${inspeccion.momentoNombre}',
                  style: tema.titleMedium?.copyWith(fontSize: 15),
                ),
              ),
              Insignia(inspeccion.cerrada ? 'Cerrada' : 'Borrador', tono: inspeccion.cerrada ? TonoInsignia.exito : TonoInsignia.advertencia),
            ],
          ),
          Text(formatearFecha(inspeccion.fecha), style: tema.bodySmall),
          if (fotos.isNotEmpty) ...[
            const SizedBox(height: Espacio.md),
            SizedBox(
              height: 88,
              child: ListView.separated(
                scrollDirection: Axis.horizontal,
                itemCount: fotos.length,
                separatorBuilder: (_, _) => const SizedBox(width: Espacio.sm),
                itemBuilder: (_, i) => ClipRRect(
                  borderRadius: BorderRadius.circular(6),
                  child: Image.network(
                    fotos[i],
                    width: 88,
                    height: 88,
                    fit: BoxFit.cover,
                    // Si la API no sirve las imágenes públicamente, se piden con la sesión.
                    headers: token == null ? null : {'Authorization': 'Bearer $token'},
                    semanticLabel: 'Foto de evidencia del informe',
                    errorBuilder: (_, _, _) => Container(width: 88, height: 88, color: Colores.borde, child: const Icon(Icons.broken_image_outlined)),
                  ),
                ),
              ),
            ),
          ],
          const SizedBox(height: Espacio.md),
          Center(child: DiagramaVehiculo(marcas: [for (final h in inspeccion.hallazgos) if (h.coords != null) h.coords!])),
          const SizedBox(height: Espacio.md),
          if (inspeccion.hallazgos.isEmpty)
            Text('Sin hallazgos registrados.', style: tema.bodySmall)
          else
            for (final h in inspeccion.hallazgos)
              Text('• ${h.parte}: ${catalogos.etiqueta(Catalogo.clasesDano, h.clase)} (${catalogos.etiqueta(Catalogo.tamanosDano, h.tamano)})', style: tema.bodySmall?.copyWith(color: Colores.textoPrimario)),
          if (inspeccion.notas?.trim().isNotEmpty ?? false) ...[
            const SizedBox(height: Espacio.md),
            Text('Notas adicionales', style: tema.bodySmall),
            Text(inspeccion.notas!, style: tema.bodySmall?.copyWith(color: Colores.textoPrimario)),
          ],
        ],
      ),
    );
  }
}

class _TarjetaEtapa extends StatelessWidget {
  const _TarjetaEtapa({
    required this.etapa,
    required this.procesoNombre,
    required this.momentos,
    required this.capturaAjena,
    required this.accion,
    required this.nombreDeEtapa,
    required this.alCapturar,
    required this.alFirmar,
  });

  final EtapaVista etapa;

  /// Nombre de la etapa segun el catalogo, ya resuelto por quien la construye.
  final String procesoNombre;
  final List<MomentoEtapa> momentos;

  /// La captura de esta etapa no le toca a quien está mirando.
  final bool capturaAjena;
  final AccionEtapa accion;
  final String Function(String codigoEtapa) nombreDeEtapa;
  final void Function(String momento) alCapturar;
  final void Function(FirmarTransferencia accion) alFirmar;

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context).textTheme;
    return Tarjeta(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(procesoNombre, style: tema.titleMedium?.copyWith(fontSize: 15)),
                    // El lugar lo resuelve la API («CEDI 1»); no todas las etapas tienen uno.
                    if (etapa.lugar case final lugar? when lugar.isNotEmpty)
                      Text(lugar, style: tema.bodySmall),
                  ],
                ),
              ),
              Insignia(etapa.requiereInspeccion ? 'Requiere informe' : 'Solo firma', tono: etapa.requiereInspeccion ? TonoInsignia.info : TonoInsignia.neutro),
            ],
          ),
          if (capturaAjena)
            Padding(
              padding: const EdgeInsets.only(top: Espacio.sm),
              child: Text(mensajeCargueConApoyo, style: tema.bodySmall),
            )
          else
            for (final momento in momentos) ...[
              const SizedBox(height: Espacio.sm),
              _FilaMomento(momento: momento, nombreDeEtapa: nombreDeEtapa, alCapturar: () => alCapturar(momento.momento)),
            ],
          const SizedBox(height: Espacio.sm),
          switch (accion) {
            FirmarTransferencia(firmada: true) => const Insignia('Transferencia firmada', tono: TonoInsignia.exito),
            final FirmarTransferencia a => FilledButton(
                onPressed: () => alFirmar(a),
                child: Text(a.soloFirma ? 'Revisar y firmar' : 'Firmar transferencia'),
              ),
            SinInforme() => Text('No hay informe previo para firmar en esta etapa.', style: tema.bodySmall),
          },
        ],
      ),
    );
  }
}

/// Una de las dos filas de la etapa: «Inspección de salida» o «Inspección de llegada», con lo que hay hecho y lo
/// que se puede hacer.
class _FilaMomento extends StatelessWidget {
  const _FilaMomento({required this.momento, required this.nombreDeEtapa, required this.alCapturar});

  final MomentoEtapa momento;
  final String Function(String codigoEtapa) nombreDeEtapa;
  final VoidCallback alCapturar;

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context).textTheme;
    final inspeccion = momento.inspeccion;
    // El nombre lo manda la API cuando la inspección existe; para la que todavía no, lo pone la app.
    final nombre = inspeccion?.momentoNombre ?? etiquetar(etiquetaMomento, momento.momento);

    return Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(nombre, style: tema.bodyMedium?.copyWith(color: Colores.textoPrimario)),
              if (momento.cerrada && inspeccion?.cerradaEn != null)
                Text('Cerrada el ${formatearFecha(inspeccion!.cerradaEn)}', style: tema.bodySmall)
              else if (momento.enBorrador)
                Text('Empezada el ${formatearFecha(inspeccion!.fecha)}', style: tema.bodySmall)
              else if (momento.bloqueo case final bloqueo?)
                Text(
                  mensajeBloqueo(bloqueo.motivo, nombreDeEtapa(bloqueo.procesoPrevio)),
                  style: tema.bodySmall?.copyWith(color: Colores.advertencia),
                ),
            ],
          ),
        ),
        const SizedBox(width: Espacio.sm),
        if (momento.cerrada)
          const Insignia('Cerrada', tono: TonoInsignia.exito)
        else if (momento.enBorrador)
          const Insignia('Borrador', tono: TonoInsignia.info)
        else
          // Deshabilitado, no escondido: que se vea que falta y por qué.
          BotonEnFila(texto: 'Capturar', onPressed: momento.sePuedeCapturar ? alCapturar : null),
      ],
    );
  }
}
