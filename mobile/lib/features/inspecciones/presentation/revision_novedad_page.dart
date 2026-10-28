import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../app/proveedores.dart';
import '../../../core/errores/error_api.dart';
import '../../../core/http/urls_api.dart';
import '../../../core/tema/tema.dart';
import '../../../core/ui/widgets.dart';
import '../../../core/util/formato.dart';
import '../../auth/presentation/auth_controller.dart';
import '../../catalogos/domain/catalogos.dart';
import '../../catalogos/presentation/catalogos_controller.dart';
import '../../vehiculos/domain/etiquetas.dart';
import '../../dibujo_dano/domain/dibujo_dano.dart';
import '../../dibujo_dano/presentation/selector_zona_dano.dart';
import '../domain/hallazgo_previo.dart';
import '../domain/novedad_pendiente.dart';
import '../providers.dart';
import 'aviso_preexistente.dart';
import 'selector_dano.dart';

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

class _EstadoRevisionNovedadPage extends ConsumerState<RevisionNovedadPage> {
  bool _enviando = false;
  var _previos = const <HallazgoPrevio>[];

  @override
  void initState() {
    super.initState();
    _cargarPrevios();
  }

  Future<void> _cargarPrevios() async {
    final previos = await ref.read(hallazgosPreviosRepositorioProvider).de(widget.novedad.vin);
    if (mounted) setState(() => _previos = previos);
  }

  Map<String, String>? get _encabezados {
    final token = ref.read(authControllerProvider).sesion?.token;
    return token == null ? null : {'Authorization': 'Bearer $token'};
  }

  String? _url(String? relativa) => resolverUrlImagen(ref.read(entornoProvider).apiUrl, relativa);

  /// Ubicada la zona, se pide la clase, el tamaño y el nivel, y se pregunta si el daño ya venía de antes.
  Future<void> _revisar(ZonaDano zona) async {
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
      titulo: zona.nombreCompleto,
      subtitulo: zona.descripcion,
      clases: catalogos.seleccionables(Catalogo.clasesDano),
      tamanos: catalogos.seleccionables(Catalogo.tamanosDano),
      niveles: niveles,
      // Lo que eligió el conductor llega cargado y se puede corregir; el nivel lo pone quien revisa.
      inicial: (clase: widget.novedad.clase, tamano: widget.novedad.tamano, nivel: null, nota: widget.novedad.nota),
    );
    if (dano == null || !mounted) return;

    final previo = previoDe(_previos, clave: zona.clave, clase: dano.clase);
    var esPreexistente = false;
    if (previo != null) {
      final respuesta = await preguntarPreexistente(
        context,
        previo: previo,
        catalogos: catalogos,
        urlFoto: _url(previo.fotoUrl),
        encabezados: _encabezados,
      );
      if (respuesta == null || !mounted) return;
      esPreexistente = respuesta;
    }

    final avisos = ScaffoldMessenger.of(context);
    final navegador = Navigator.of(context);
    setState(() => _enviando = true);
    try {
      await ref.read(novedadesRepositorioProvider).revisar(
            widget.novedad.id,
            cuadranteNumero: zona.numero,
            lado: zona.lado,
            clase: dano.clase,
            nivel: dano.nivel!,
            tamano: dano.tamano,
            nota: dano.nota,
            esPreexistente: esPreexistente,
            hallazgoOrigenId: esPreexistente ? previo!.id : null,
          );
      navegador.pop(true);
    } on ErrorApi catch (e) {
      // Un 409 es que alguien ya la revisó, o que la novedad no es del conductor: no se reintenta. Se avisa y se
      // vuelve, que recarga la lista con lo que haya quedado.
      avisos.showSnackBar(SnackBar(content: Text(e.mensajeDetallado)));
      navegador.pop(true);
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

    return Scaffold(
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
            if (url != null) ...[
              const SizedBox(height: Espacio.md),
              ClipRRect(
                borderRadius: BorderRadius.circular(6),
                child: Image.network(
                  url,
                  height: 220,
                  fit: BoxFit.cover,
                  headers: _encabezados,
                  semanticLabel: 'Foto de la novedad del conductor',
                  errorBuilder: (_, _, _) => Container(
                    height: 220,
                    color: Colores.borde,
                    alignment: Alignment.center,
                    child: const Text('La foto no se pudo cargar'),
                  ),
                ),
              ),
            ],
            const SizedBox(height: Espacio.lg),
            Text('¿Dónde está el daño?', style: tema.titleMedium),
            Text('Tócalo en el dibujo y después dile qué es y de qué nivel.', style: tema.bodySmall),
            const SizedBox(height: Espacio.md),
            SelectorZonaDano(
              contexto: 'revision-${novedad.id}',
              danos: const {},
              alRegistrar: _enviando ? null : _revisar,
            ),
          ],
        ),
      ),
    );
  }
}
