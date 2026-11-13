import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../core/tema/tema.dart';
import '../domain/dibujo_dano.dart';
import '../domain/toque.dart';
import 'dibujo_dano_controller.dart';
import 'panel_dano_view.dart';
import 'pintor_regiones.dart';

/// El dibujo de daños completo: las pestañas de los paneles, el panel visible y, debajo, la ficha de la zona elegida
/// o la pregunta «¿cuál cuadrante tocaste?».
///
/// Comportamiento del prototipo:
/// - Tocar una zona la resalta; tocarla otra vez o «Deseleccionar» la suelta.
/// - Un toque en el borde entre dos o tres zonas pregunta cuál.
/// - Donde las piezas se ven más chicas que un dedo, el primer toque acerca la vista.
/// - Con una zona elegida, las pestañas de los paneles donde también se ve llevan un punto amarillo.
/// - Las zonas con daño se ven en rojo en todos los paneles, y cada pestaña dice cuántas tiene.
/// - **Un daño por zona:** tocar una zona que ya tiene daño abre ese registro ([alAbrirDano]) en vez de crear otro.
///
/// Sin [alRegistrar] es de solo lectura (la página del VIN): se puede tocar para ver el nombre, nada más.
class SelectorZonaDano extends ConsumerStatefulWidget {
  const SelectorZonaDano({
    super.key,
    required this.contexto,
    required this.danos,
    this.alRegistrar,
    this.alAbrirDano,
    this.alturaMaxima = 420,
    this.anteriores = const {},
  });

  /// Separa esta selección de las demás que haya en pantalla (ver [seleccionZonaProvider]).
  final String contexto;

  /// Las zonas que ya tienen daño («26D», «54») con el nivel de severidad de cada una, que es el color con el que
  /// se pintan. El nivel va nulo cuando nadie lo ha clasificado todavía.
  final Map<String, String?> danos;

  /// Las zonas que ya traían un daño de una inspección **anterior** del vehículo. Se pintan rayadas y con el borde
  /// discontinuo, y no se pueden abrir ni editar: son de otro informe, ya cerrado.
  final Set<String> anteriores;

  /// Se llama con la zona elegida cuando se pulsa «Registrar daño».
  final void Function(ZonaDano zona)? alRegistrar;

  /// Se llama al tocar una zona que ya tiene daño.
  final void Function(ZonaDano zona)? alAbrirDano;

  final double alturaMaxima;

  @override
  ConsumerState<SelectorZonaDano> createState() => _EstadoSelectorZonaDano();
}

class _EstadoSelectorZonaDano extends ConsumerState<SelectorZonaDano> {
  /// Un zoom por panel: al volver a una pestaña se encuentra como se dejó.
  final _zoom = <String, TransformationController>{};

  TransformationController _controladorDe(String codigo) => _zoom.putIfAbsent(codigo, TransformationController.new);

  @override
  void dispose() {
    for (final c in _zoom.values) {
      c.dispose();
    }
    super.dispose();
  }

  SeleccionZonaController get _seleccion => ref.read(seleccionZonaProvider(widget.contexto).notifier);

  /// Después de elegir, si la zona ya tiene daño se abre ese registro: un daño por zona.
  void _despuesDeElegir(DibujoDano dibujo, String clave) {
    final elegida = ref.read(seleccionZonaProvider(widget.contexto)).seleccionada;
    if (elegida != clave || !widget.danos.containsKey(clave)) return;
    final zona = dibujo.zona(clave);
    if (zona != null) widget.alAbrirDano?.call(zona);
  }

  void _alTocar(DibujoDano dibujo, ResultadoToque toque) {
    _seleccion.aplicar(toque);
    if (toque is ToqueElegir) _despuesDeElegir(dibujo, toque.clave);
  }

  void _alElegirCandidato(DibujoDano dibujo, String clave) {
    _seleccion.elegir(clave);
    _despuesDeElegir(dibujo, clave);
  }

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context).textTheme;
    return ref.watch(dibujoDanoProvider).when(
          loading: () => Padding(
            padding: const EdgeInsets.symmetric(vertical: Espacio.lg),
            child: Row(children: [
              const SizedBox(width: 18, height: 18, child: CircularProgressIndicator(strokeWidth: 2)),
              const SizedBox(width: Espacio.sm),
              Text('Cargando el dibujo del vehículo…', style: tema.bodySmall),
            ]),
          ),
          error: (_, _) => Text('No se pudo cargar el dibujo del vehículo.', style: tema.bodySmall?.copyWith(color: Colores.peligro)),
          data: (dibujo) => _contenido(context, dibujo),
        );
  }

  Widget _contenido(BuildContext context, DibujoDano dibujo) {
    final tema = Theme.of(context).textTheme;
    final estado = ref.watch(seleccionZonaProvider(widget.contexto));
    // Si el dibujo cambió y trae menos paneles, se vuelve al primero.
    final indice = estado.panel < dibujo.paneles.length ? estado.panel : 0;
    final panel = dibujo.paneles[indice];
    final regiones = dibujo.regionesDe(panel.codigo);
    final elegida = estado.seleccionada == null ? null : dibujo.zona(estado.seleccionada!);
    final controlador = _controladorDe(panel.codigo);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        _Pestanas(
          dibujo: dibujo,
          indice: indice,
          elegida: estado.seleccionada,
          danos: widget.danos,
          alElegir: _seleccion.verPanel,
        ),
        const SizedBox(height: Espacio.sm),
        PanelDanoView(
          key: ValueKey(panel.codigo),
          panel: panel,
          regiones: regiones,
          danos: widget.danos,
          anteriores: widget.anteriores,
          seleccionada: estado.seleccionada,
          controlador: controlador,
          alturaMaxima: widget.alturaMaxima,
          alTocar: (toque) => _alTocar(dibujo, toque),
        ),
        const SizedBox(height: Espacio.xs),
        ValueListenableBuilder<Matrix4>(
          valueListenable: controlador,
          builder: (context, _, _) => Text(
            _pista(estado, regiones, PanelDanoView.estaAcercado(controlador)),
            textAlign: TextAlign.center,
            style: tema.bodySmall,
          ),
        ),
        const SizedBox(height: Espacio.md),
        if (estado.hayCandidatos)
          _Candidatos(
            zonas: [for (final c in estado.candidatos) ?dibujo.zona(c)],
            alElegir: (clave) => _alElegirCandidato(dibujo, clave),
          )
        else if (elegida != null)
          _FichaZona(
            zona: elegida,
            seVeEn: [for (final p in dibujo.panelesDondeSeVe(elegida.clave)) p.nombre],
            tieneDano: widget.danos.containsKey(elegida.clave),
            tieneDanoAnterior: widget.anteriores.contains(elegida.clave),
            alRegistrar: widget.alRegistrar,
            alAbrirDano: widget.alAbrirDano,
            alSoltar: _seleccion.deseleccionar,
          ),
      ],
    );
  }

  String _pista(EstadoSeleccionZona estado, List<RegionDano> regiones, bool acercado) {
    final elegida = estado.seleccionada;
    if (elegida != null && !regiones.any((r) => r.clave == elegida)) return 'El cuadrante elegido no se ve en esta vista';
    if (elegida != null) return 'Toca de nuevo el cuadrante para soltarlo';
    if (widget.alRegistrar == null) return 'Toca un cuadrante para ver cuál es';
    if (acercado) return 'Toca el cuadrante del daño. Arrastra para moverte.';
    return 'Toca el cuadrante donde está el daño';
  }
}

/// Las pestañas de los paneles, desplazables en horizontal. Llevan el punto amarillo si la zona elegida se ve ahí y
/// el número de zonas con daño.
class _Pestanas extends StatelessWidget {
  const _Pestanas({
    required this.dibujo,
    required this.indice,
    required this.elegida,
    required this.danos,
    required this.alElegir,
  });

  final DibujoDano dibujo;
  final int indice;
  final String? elegida;
  final Map<String, String?> danos;
  final void Function(int indice) alElegir;

  @override
  Widget build(BuildContext context) {
    return SingleChildScrollView(
      scrollDirection: Axis.horizontal,
      // Espacio arriba para que el número rojo no se corte.
      padding: const EdgeInsets.only(top: Espacio.sm, right: Espacio.sm),
      child: Row(
        children: [
          for (final (i, panel) in dibujo.paneles.indexed)
            Padding(
              padding: const EdgeInsets.only(right: Espacio.sm),
              child: Badge(
                isLabelVisible: dibujo.conDanoEn(panel.codigo, danos) > 0,
                label: Text('${dibujo.conDanoEn(panel.codigo, danos)}'),
                backgroundColor: ColoresDibujo.dano,
                child: ChoiceChip(
                  label: Text(panel.nombre),
                  selected: i == indice,
                  showCheckmark: false,
                  materialTapTargetSize: MaterialTapTargetSize.padded,
                  avatar: elegida != null && dibujo.regionesDe(panel.codigo).any((r) => r.clave == elegida)
                      ? Semantics(
                          label: 'El cuadrante elegido se ve aquí',
                          child: Container(
                            width: 10,
                            height: 10,
                            decoration: BoxDecoration(
                              color: ColoresDibujo.puntoAmarillo,
                              shape: BoxShape.circle,
                              border: Border.all(color: ColoresDibujo.seleccion, width: 1.5),
                            ),
                          ),
                        )
                      : null,
                  onSelected: (_) => alElegir(i),
                ),
              ),
            ),
        ],
      ),
    );
  }
}

/// «¿Cuál cuadrante tocaste?»: el toque quedó en el borde entre varias zonas.
class _Candidatos extends StatelessWidget {
  const _Candidatos({required this.zonas, required this.alElegir});

  final List<ZonaDano> zonas;
  final void Function(String clave) alElegir;

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context).textTheme;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Text('¿Cuál cuadrante tocaste?', style: tema.titleMedium),
        Text('El toque quedó en el borde entre varios cuadrantes.', style: tema.bodySmall),
        const SizedBox(height: Espacio.sm),
        for (final zona in zonas)
          Padding(
            padding: const EdgeInsets.only(bottom: Espacio.sm),
            child: OutlinedButton(
              style: OutlinedButton.styleFrom(
                alignment: Alignment.centerLeft,
                minimumSize: const Size.fromHeight(48),
                side: const BorderSide(color: ColoresDibujo.puntoAmarillo, width: 2),
              ),
              onPressed: () => alElegir(zona.clave),
              child: Padding(
                padding: const EdgeInsets.symmetric(vertical: Espacio.sm),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(zona.nombreCompleto, style: tema.bodyMedium?.copyWith(fontWeight: FontWeight.w600)),
                    Text(zona.descripcion, style: tema.bodySmall),
                  ],
                ),
              ),
            ),
          ),
      ],
    );
  }
}

/// La zona elegida: su nombre completo, su descripción (número, lado y área AIAG) y en qué vistas se ve, con lo
/// que se puede hacer con ella.
class _FichaZona extends StatelessWidget {
  const _FichaZona({
    required this.zona,
    required this.seVeEn,
    required this.tieneDano,
    required this.alRegistrar,
    required this.alAbrirDano,
    required this.alSoltar,
    this.tieneDanoAnterior = false,
  });

  final ZonaDano zona;
  final List<String> seVeEn;
  final bool tieneDano;
  final bool tieneDanoAnterior;
  final void Function(ZonaDano zona)? alRegistrar;
  final void Function(ZonaDano zona)? alAbrirDano;
  final VoidCallback alSoltar;

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context).textTheme;
    // El AIAG ya va en la descripción de la zona: repetirlo aquí lo mostraba dos veces seguidas.
    final detalle = [
      zona.descripcion,
      if (seVeEn.isNotEmpty) 'Se ve en: ${seVeEn.join(', ')}',
    ].join('. ');
    final accion = switch ((tieneDano, alRegistrar, alAbrirDano)) {
      (true, _, final abrir?) => ('Ver daño', () => abrir(zona)),
      (false, final registrar?, _) => ('Registrar daño', () => registrar(zona)),
      _ => null,
    };

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Text(zona.nombreCompleto, style: tema.titleMedium),
        Text('$detalle.', style: tema.bodySmall),
        // Al tocar una zona rayada, quien inspecciona espera abrir ese daño, como con los de este informe. Hay que
        // decirle por qué no se abre: es de un informe anterior y ya está cerrado. Registrar uno nuevo aquí sí se
        // puede, y si resulta ser el mismo, el aviso de «ya estaba registrado» lo enlaza.
        if (tieneDanoAnterior && !tieneDano) ...[
          const SizedBox(height: Espacio.sm),
          // En gris de texto secundario se leía como una nota al pie y la gente seguía tocando la zona esperando
          // que abriera. Va en ámbar, con icono y fondo tenue: es la explicación de por qué no abre, no un adorno.
          Container(
            padding: const EdgeInsets.all(Espacio.sm),
            decoration: BoxDecoration(
              color: Colores.advertencia.withValues(alpha: 0.10),
              border: Border.all(color: Colores.advertencia.withValues(alpha: 0.45)),
              borderRadius: BorderRadius.circular(8),
            ),
            child: Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                const Icon(Icons.history, size: 18, color: Colores.advertencia),
                const SizedBox(width: Espacio.sm),
                Expanded(
                  child: Text(
                    'Esta zona ya traía un daño de una inspección anterior. No se puede cambiar desde aquí: es de un '
                    'informe ya cerrado. Si el daño sigue ahí, regístralo y marca que ya estaba.',
                    style: tema.bodySmall?.copyWith(color: Colores.advertencia),
                  ),
                ),
              ],
            ),
          ),
        ],
        const SizedBox(height: Espacio.md),
        Row(
          children: [
            if (accion case (final texto, final hacer)) ...[
              Expanded(child: FilledButton(onPressed: hacer, child: Text(texto))),
              const SizedBox(width: Espacio.md),
            ],
            Expanded(child: OutlinedButton(onPressed: alSoltar, child: const Text('Deseleccionar'))),
          ],
        ),
      ],
    );
  }
}
