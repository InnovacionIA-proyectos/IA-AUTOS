import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../app/proveedores.dart';
import '../../../core/errores/error_api.dart';
import '../../../core/http/urls_api.dart';
import '../../../core/tema/tema.dart';
import '../../../core/ui/foto_evidencia.dart';
import '../../../core/ui/widgets.dart';
import '../../../core/util/formato.dart';
import '../../catalogos/domain/catalogos.dart';
import '../../catalogos/presentation/catalogos_controller.dart';
import '../../vehiculos/domain/etiquetas.dart';
import '../../dibujo_dano/domain/dibujo_dano.dart';
import '../../sync/presentation/sync_controller.dart';
import '../../dibujo_dano/presentation/selector_zona_dano.dart';
import '../domain/hallazgo_previo.dart';
import '../domain/novedad_pendiente.dart';
import '../providers.dart';
import 'aviso_preexistente.dart';
import 'hoja_dano.dart';

/// «Golpe · 1 a 5 cm · Piedra»: la clase que eligió el conductor, el tamaño y, si lo dijo, qué causó la novedad.
///
/// La clase y el tipo de novedad no son lo mismo: la clase dice qué quedó en la lata y el tipo, qué pasó en la
/// vía. El conductor puede no haber elegido clase; quien revisa la pone.
String etiquetaNovedad(NovedadPendiente novedad, Catalogos catalogos) => [
      switch (novedad.clase) {
        final clase? => catalogos.etiqueta(Catalogo.clasesDano, clase),
        _ => 'Sin clase',
      },
      catalogos.etiqueta(Catalogo.tamanosDano, novedad.tamano),
      if (novedad.tipoNovedad case final tipo?) etiquetar(etiquetaTipoNovedad, tipo),
    ].join(' · ');

/// Cuándo y dónde la tomó, y quién. Sin GPS lo dice, en vez de callarlo.
String detalleNovedad(NovedadPendiente novedad, {String? conductor}) {
  final foto = novedad.foto;
  return [
    foto?.tomadaEn == null ? 'Sin hora' : formatearFecha(foto!.tomadaEn),
    foto != null && foto.tieneUbicacion
        ? '${foto.lat!.toStringAsFixed(5)}, ${foto.lng!.toStringAsFixed(5)}'
        : 'Sin ubicación',
    ?conductor,
  ].join(' · ');
}

/// Revisar la novedad que registró un conductor: ubicarla en el dibujo y ponerle clase, nivel y el tamaño ya
/// corregido.
///
/// El conductor marca el daño y lo fotografía, pero no lo clasifica (DEC-003). Aquí se completa, con su foto
/// delante para poder juzgarla sin tener el vehículo al lado.
class RevisionNovedadPage extends ConsumerStatefulWidget {
  const RevisionNovedadPage({super.key, required this.novedad});

  final NovedadPendiente novedad;

  @override
  ConsumerState<RevisionNovedadPage> createState() => _EstadoRevisionNovedadPage();
}

/// Lo que quien revisa ya eligió y el servidor no aceptó: se conserva para mandarlo otra vez o corregirlo.
///
/// [cuadrante] null es «sin mandar zona»: el daño conserva la suya, o es de un extra y no lleva (S8-HU-08.2 CA-4).
typedef _Calificacion = ({
  int? cuadrante,
  String? lado,
  String nombreZona,
  DatosDano dano,
  bool esPreexistente,
  String? hallazgoOrigenId,
});

class _EstadoRevisionNovedadPage extends ConsumerState<RevisionNovedadPage> {
  bool _enviando = false;
  var _previos = const <HallazgoPrevio>[];

  /// S8-HU-08.2 CA-6: la última calificación rechazada y el motivo del servidor. Con ella la pantalla no se cierra:
  /// muestra el motivo, deja mandarla otra vez y la hoja se abre con lo ya elegido.
  _Calificacion? _rechazada;
  String? _motivoRechazo;

  @override
  void initState() {
    super.initState();
    _cargarPrevios();
  }

  Future<void> _cargarPrevios() async {
    final previos = await ref.read(hallazgosPreviosRepositorioProvider).de(widget.novedad.vin);
    if (mounted) setState(() => _previos = previos);
  }

  String? _url(String? relativa) => resolverUrlImagen(ref.read(entornoProvider).apiUrl, relativa);

  /// Ubicada la zona en el dibujo, se pide la clase, el tamaño y el nivel.
  Future<void> _revisar(ZonaDano zona) => _calificar(
        cuadrante: zona.numero,
        lado: zona.lado,
        nombre: zona.nombreCompleto,
        subtitulo: zona.descripcion,
        clave: zona.clave,
      );

  /// El daño ya trae su zona, o es de un extra: solo falta lo que no tiene (CA-4). La zona no viaja.
  Future<void> _calificarComoViene() {
    final novedad = widget.novedad;
    return _calificar(
      cuadrante: null,
      lado: null,
      nombre: novedad.zona ?? (novedad.esDeExtra ? 'Daño del extra' : 'Daño'),
      subtitulo: novedad.esDeExtra ? 'Daño de un extra: no tiene zona' : null,
      clave: novedad.tieneZona ? claveDeZona(novedad.cuadranteNumero!, novedad.lado) : null,
    );
  }

  /// Pide la clase, el tamaño y el nivel, y pregunta si el daño ya venía de antes.
  Future<void> _calificar({
    required int? cuadrante,
    required String? lado,
    required String nombre,
    required String? subtitulo,
    required String? clave,
  }) async {
    final catalogos = ref.read(catalogosProvider);
    final niveles = catalogos.seleccionables(Catalogo.nivelesSeveridad);
    if (niveles.isEmpty) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text('Los niveles de severidad aún no se han descargado. Conéctate e inténtalo de nuevo.'),
        ),
      );
      return;
    }

    final dano = await pedirDano(
      context,
      titulo: nombre,
      subtitulo: subtitulo,
      clases: catalogos.seleccionables(Catalogo.clasesDano),
      tamanos: catalogos.seleccionables(Catalogo.tamanosDano),
      niveles: niveles,
      // Lo que eligió el conductor llega cargado y se puede corregir; el nivel lo pone quien revisa. Si el servidor
      // ya rechazó una calificación, la hoja vuelve con lo que se había elegido, nivel incluido.
      inicial: switch (_rechazada) {
        final r? => (clase: r.dano.clase, tamano: r.dano.tamano, nivel: r.dano.nivel, nota: r.dano.nota),
        null => (clase: widget.novedad.clase, tamano: widget.novedad.tamano, nivel: null, nota: widget.novedad.nota),
      },
    );
    if (dano == null || !mounted) return;

    final previo = clave == null ? null : previoDe(_previos, clave: clave, clase: dano.clase);
    var esPreexistente = false;
    if (previo != null) {
      final respuesta = await preguntarPreexistente(
        context,
        previo: previo,
        catalogos: catalogos,
        urlFoto: _url(previo.fotoUrl),
        encabezadosFoto: ref.read(entornoProvider).encabezadosEnrutamiento,
        alVencerFoto: _cargarPrevios,
        sinSenal: !ref.read(syncControllerProvider).online,
      );
      if (respuesta == null || !mounted) return;
      esPreexistente = respuesta;
    }

    await _enviar((
      cuadrante: cuadrante,
      lado: lado,
      nombreZona: nombre,
      dano: dano,
      esPreexistente: esPreexistente,
      hallazgoOrigenId: esPreexistente ? previo!.id : null,
    ));
  }

  Future<void> _enviar(_Calificacion calificacion) async {
    final navegador = Navigator.of(context);
    setState(() => _enviando = true);
    try {
      await ref.read(novedadesRepositorioProvider).revisar(
            widget.novedad.id,
            cuadranteNumero: calificacion.cuadrante,
            lado: calificacion.lado,
            clase: calificacion.dano.clase,
            nivel: calificacion.dano.nivel!,
            tamano: calificacion.dano.tamano,
            nota: calificacion.dano.nota,
            esPreexistente: calificacion.esPreexistente,
            hallazgoOrigenId: calificacion.hallazgoOrigenId,
          );
      // Si mientras tanto se salió con «atrás», esta pantalla ya no está: cerrar aquí sacaría también la de abajo.
      if (mounted) navegador.pop(true);
    } catch (e) {
      // Un 403 (el daño no es de un viaje suyo) o un 409 (alguien ya lo calificó) explican por qué no; se dicen tal
      // cual y no se pierde lo elegido. Sin señal, lo mismo con un texto propio.
      if (!mounted) return;
      setState(() {
        _rechazada = calificacion;
        _motivoRechazo = switch (e) {
          final ErrorApi api => api.mensajeDetallado,
          _ => 'No se pudo enviar la calificación. Revisa la señal e inténtalo de nuevo.',
        };
      });
    } finally {
      if (mounted) setState(() => _enviando = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context).textTheme;
    final catalogos = ref.watch(catalogosProvider);
    final novedad = widget.novedad;
    final url = _url(novedad.foto?.url);

    // Tras un rechazo, salir le avisa a quien abrió la pantalla que recargue: un 409 suele ser que otro ya lo
    // calificó, y la lista tiene que dejar de ofrecerlo.
    // Mientras se envía no se sale: si el servidor la acepta después, la lista de abajo ya no se enteraría y
    // seguiría ofreciendo un daño calificado.
    return PopScope<bool>(
      canPop: _rechazada == null && !_enviando,
      onPopInvokedWithResult: (salio, _) {
        if (!salio && !_enviando) Navigator.of(context).pop(true);
      },
      child: Scaffold(
        appBar: AppBar(title: const Text('Revisar la novedad')),
        body: AbsorbPointer(
          absorbing: _enviando,
          child: ListView(
            padding: const EdgeInsets.all(Espacio.lg),
            children: [
              Tarjeta(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      etiquetaNovedad(novedad, catalogos),
                      style: tema.bodyMedium?.copyWith(fontWeight: FontWeight.w600),
                    ),
                    Text(detalleNovedad(novedad), style: tema.bodySmall),
                    if (novedad.nota case final nota?)
                      Text(nota, style: tema.bodySmall?.copyWith(color: Colores.textoPrimario)),
                  ],
                ),
              ),
              // Sin `if (url != null)`: cuando la novedad llegaba sin enlace no se pintaba nada y quedaba un hueco
              // mudo, justo donde quien revisa espera la prueba de lo que va a calificar. `FotoEvidencia` ya sabe
              // decir «Sin foto», «Sin conexión» o «La foto no se pudo cargar», cada uno en su marco.
              const SizedBox(height: Espacio.md),
              FotoEvidencia(
                url: url,
                encabezados: ref.watch(entornoProvider).encabezadosEnrutamiento,
                alto: 220,
                venceEn: novedad.foto?.venceEn,
                // Al vencer, los enlaces nuevos vienen con la lista de novedades.
                alVencer: () => ref.invalidate(novedadesPorRevisarProvider(novedad.vin)),
                sinSenal: !ref.watch(syncControllerProvider).online,
                semanticLabel: 'Foto de la novedad del conductor',
              ),
              if ((_rechazada, _motivoRechazo) case (final rechazada?, final motivo?)) ...[
                const SizedBox(height: Espacio.md),
                Tarjeta(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      Text('No se registró la calificación', style: tema.titleSmall?.copyWith(color: Colores.peligro)),
                      Text(motivo, style: tema.bodySmall?.copyWith(color: Colores.textoPrimario)),
                      Text(
                        '${rechazada.nombreZona} · '
                        '${catalogos.etiqueta(Catalogo.clasesDano, rechazada.dano.clase)} · '
                        '${catalogos.etiqueta(Catalogo.nivelesSeveridad, rechazada.dano.nivel!)}',
                        style: tema.bodySmall,
                      ),
                      const SizedBox(height: Espacio.sm),
                      FilledButton(onPressed: _enviando ? null : () => _enviar(rechazada), child: const Text('Enviar de nuevo')),
                    ],
                  ),
                ),
              ],
              const SizedBox(height: Espacio.lg),
              // S8-HU-08.2 CA-4: el daño de un extra no tiene zona y no se toca en el dibujo; el que ya tiene zona
              // llega con ella y solo pide lo que falta (se puede mover a otra si está mal ubicado).
              if (novedad.esDeExtra) ...[
                Text('Daño del extra «${novedad.zona ?? 'extra'}»', style: tema.titleMedium),
                Text('No tiene zona en el dibujo: solo falta decir qué es y de qué nivel.', style: tema.bodySmall),
                const SizedBox(height: Espacio.md),
                FilledButton(onPressed: _enviando ? null : _calificarComoViene, child: const Text('Calificar')),
              ] else ...[
                if (novedad.tieneZona) ...[
                  Text('Zona: ${novedad.zona ?? 'marcada por el conductor'}', style: tema.titleMedium),
                  const SizedBox(height: Espacio.sm),
                  FilledButton(onPressed: _enviando ? null : _calificarComoViene, child: const Text('Calificar en esta zona')),
                  const SizedBox(height: Espacio.md),
                  Text('Si está mal ubicado, tócalo en otra zona del dibujo.', style: tema.bodySmall),
                ] else ...[
                  Text('¿Dónde está el daño?', style: tema.titleMedium),
                  Text('Tócalo en el dibujo y después dile qué es y de qué nivel.', style: tema.bodySmall),
                ],
                const SizedBox(height: Espacio.md),
                SelectorZonaDano(
                  contexto: 'revision-${novedad.id}',
                  danos: const {},
                  alRegistrar: _enviando ? null : _revisar,
                ),
              ],
            ],
          ),
        ),
      ),
    );
  }
}
