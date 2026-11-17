import 'dart:io';

import 'package:flutter/material.dart';

import '../../../core/tema/tema.dart';
import '../../../core/ui/visor_imagen.dart';
import '../../../core/ui/widgets.dart';
import '../../catalogos/domain/catalogos.dart';
import '../domain/extra_local.dart';
import 'hoja_dano.dart';

/// Lo que se elige en la hoja de un extra. Las fotos se toman después, desde su fila.
typedef DatosExtra = ({String tipo, String nombre, String? textoOtro, String? observacion, DanoExtra? dano});

/// Pide el tipo de extra, su observación y, si está dañado, los tres datos del daño. Null si se cancela.
///
/// Los tipos salen del catálogo (solo los activos, en su orden) y siempre se ofrece «Otro», que exige decir qué es:
/// un elemento que nadie configuró se reporta igual, sin esperar a que Calidad lo agregue.
///
/// El daño se pide aquí mismo y no en otra hoja encima: son tres fichas más, y partirlo en dos pasos dejaría un
/// extra guardado a medias si se cancela el segundo.
///
/// [puedeMarcarDano] en falso esconde el interruptor: es el caso del conductor. En el daño de un **extra** el
/// servidor exige los tres —clase, tamaño y **nivel**— sin excepción por rol, al revés que en el daño de una
/// zona, donde el conductor no manda nivel. Así que o se piden los tres o no se marca el daño; dejarlo a medias
/// manda un cuerpo que el servidor rechaza con 400 y tumba el informe entero.
Future<DatosExtra?> pedirExtra(
  BuildContext context, {
  required List<ValorCatalogo> tipos,
  required List<ValorCatalogo> clases,
  required List<ValorCatalogo> tamanos,
  required List<ValorCatalogo> niveles,
  bool puedeMarcarDano = true,
}) {
  return showModalBottomSheet<DatosExtra>(
    context: context,
    isScrollControlled: true,
    showDragHandle: true,
    builder: (_) => _HojaExtra(
      tipos: [...tipos.where((t) => t.codigo != tipoExtraOtro)],
      clases: clases,
      tamanos: tamanos,
      niveles: niveles,
      puedeMarcarDano: puedeMarcarDano,
    ),
  );
}

class _HojaExtra extends StatefulWidget {
  const _HojaExtra({
    required this.tipos,
    required this.clases,
    required this.tamanos,
    required this.niveles,
    required this.puedeMarcarDano,
  });

  final List<ValorCatalogo> tipos;
  final List<ValorCatalogo> clases;
  final List<ValorCatalogo> tamanos;
  final List<ValorCatalogo> niveles;
  final bool puedeMarcarDano;

  @override
  State<_HojaExtra> createState() => _EstadoHojaExtra();
}

class _EstadoHojaExtra extends State<_HojaExtra> {
  String? _tipo;
  final _textoOtro = TextEditingController();
  final _observacion = TextEditingController();
  bool _danado = false;
  String? _clase;
  String? _tamano;
  String? _nivel;

  @override
  void dispose() {
    _textoOtro.dispose();
    _observacion.dispose();
    super.dispose();
  }

  bool get _esOtro => _tipo == tipoExtraOtro;

  /// Qué falta por elegir, o null si ya se puede agregar. Se dice de uno en uno, junto al botón: un extra
  /// marcado como dañado al que le falta el nivel no se guarda a medias, y el servidor lo rechazaría.
  String? get _loQueFalta {
    if (_tipo == null) return null; // todavía no ha elegido nada: no hay nada que reclamar
    if (_esOtro && _textoOtro.text.trim().isEmpty) return 'Escribe qué es.';
    if (_danado) {
      if (_clase == null) return 'Elige la clase del daño.';
      if (_tamano == null) return 'Elige el tamaño del daño.';
      // El nivel es obligatorio siempre: en un extra el servidor lo exige a todo el mundo.
      if (_nivel == null) return 'Elige el nivel de severidad.';
    }
    return null;
  }

  bool get _completo => _tipo != null && _loQueFalta == null;

  DatosExtra _datos() {
    final observacion = _observacion.text.trim();
    final textoOtro = _textoOtro.text.trim();
    return (
      tipo: _tipo!,
      nombre: _esOtro ? 'Otro: $textoOtro' : widget.tipos.firstWhere((t) => t.codigo == _tipo).nombre,
      textoOtro: _esOtro ? textoOtro : null,
      observacion: observacion.isEmpty ? null : observacion,
      dano: _danado ? DanoExtra(clase: _clase!, tamano: _tamano!, nivel: _nivel) : null,
    );
  }

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context).textTheme;
    return ConstrainedBox(
      constraints: BoxConstraints(maxHeight: MediaQuery.sizeOf(context).height * 0.85),
      child: SafeArea(
        child: Padding(
          // El teclado no tapa los campos de texto.
          padding: EdgeInsets.fromLTRB(Espacio.lg, 0, Espacio.lg, Espacio.lg + MediaQuery.viewInsetsOf(context).bottom),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text('Agregar extra', style: tema.titleMedium),
              Text('Lo que el dibujo no trae. No se marca en el dibujo.', style: tema.bodySmall),
              Flexible(
                child: SingleChildScrollView(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      const SizedBox(height: Espacio.md),
                      Wrap(
                        spacing: Espacio.sm,
                        runSpacing: Espacio.sm,
                        children: [
                          for (final t in widget.tipos)
                            ChoiceChip(
                              label: Text(t.nombre),
                              selected: _tipo == t.codigo,
                              onSelected: (_) => setState(() => _tipo = t.codigo),
                            ),
                          ChoiceChip(
                            label: const Text('Otro'),
                            selected: _esOtro,
                            onSelected: (_) => setState(() => _tipo = tipoExtraOtro),
                          ),
                        ],
                      ),
                      if (_esOtro) ...[
                        const SizedBox(height: Espacio.md),
                        TextField(
                          controller: _textoOtro,
                          autofocus: true,
                          maxLength: 100,
                          textCapitalization: TextCapitalization.sentences,
                          onChanged: (_) => setState(() {}),
                          decoration: const InputDecoration(labelText: '¿Qué es?', hintText: 'Por ejemplo: portabicicletas'),
                        ),
                      ],
                      const SizedBox(height: Espacio.md),
                      // El golpe de un platón es un golpe del carro: con sus tres datos cuenta en las
                      // estadísticas como cualquier otro. Sin ellos solo quedaba una foto y un texto.
                      if (widget.puedeMarcarDano)
                        SwitchListTile(
                          value: _danado,
                          onChanged: (v) => setState(() => _danado = v),
                          title: const Text('Está dañado'),
                          subtitle: Text(
                            'Cuenta como un daño del carro. No se marca en el dibujo: el extra no tiene zona.',
                            style: tema.bodySmall,
                          ),
                          contentPadding: EdgeInsets.zero,
                        )
                      else
                        // El servidor exige el nivel para el daño de un extra, y el conductor no asigna niveles:
                        // ofrecérselo sería dejarle armar un envío que vuelve rechazado.
                        Text(
                          'Si está dañado, dilo en la observación: el daño lo califica quien inspecciona.',
                          style: tema.bodySmall,
                        ),
                      if (_danado) ...[
                        const SizedBox(height: Espacio.sm),
                        GrupoDeOpciones(
                          titulo: 'Clase de daño',
                          opciones: widget.clases,
                          elegido: _clase,
                          alElegir: (codigo) => setState(() => _clase = codigo),
                        ),
                        const SizedBox(height: Espacio.lg),
                        GrupoDeOpciones(
                          titulo: 'Tamaño',
                          opciones: widget.tamanos,
                          elegido: _tamano,
                          alElegir: (codigo) => setState(() => _tamano = codigo),
                        ),
                        if (widget.niveles.isNotEmpty) ...[
                          const SizedBox(height: Espacio.lg),
                          GrupoDeOpciones(
                            titulo: 'Nivel de severidad',
                            opciones: widget.niveles,
                            elegido: _nivel,
                            alElegir: (codigo) => setState(() => _nivel = codigo),
                            tooltip: (n) => n.campo<String>('accion'),
                          ),
                        ],
                      ],
                      const SizedBox(height: Espacio.md),
                      TextField(
                        controller: _observacion,
                        maxLength: observacionExtraMax,
                        minLines: 1,
                        maxLines: 3,
                        textCapitalization: TextCapitalization.sentences,
                        decoration: const InputDecoration(labelText: 'Observación (opcional)'),
                      ),
                    ],
                  ),
                ),
              ),
              if (_loQueFalta case final falta?)
                Padding(
                  padding: const EdgeInsets.only(top: Espacio.sm),
                  child: Semantics(
                    liveRegion: true,
                    child: Text(falta, style: tema.bodySmall?.copyWith(color: Colores.peligro)),
                  ),
                ),
              const SizedBox(height: Espacio.md),
              Row(
                children: [
                  Expanded(child: OutlinedButton(onPressed: () => Navigator.of(context).pop(), child: const Text('Cancelar'))),
                  const SizedBox(width: Espacio.md),
                  Expanded(
                    child: FilledButton(
                      onPressed: _completo ? () => Navigator.of(context).pop(_datos()) : null,
                      child: const Text('Agregar'),
                    ),
                  ),
                ],
              ),
            ],
          ),
        ),
      ),
    );
  }
}

/// Los extras del informe: cada uno con su observación, sus fotos y la opción de tomar otra o quitarlo.
class SeccionExtras extends StatelessWidget {
  const SeccionExtras({
    super.key,
    required this.extras,
    required this.alAgregar,
    required this.alTomarFoto,
    required this.alQuitar,
    this.catalogos,
  });

  final List<ExtraLocal> extras;

  /// Null mientras se guarda una foto: no se abre otra cámara encima.
  final VoidCallback? alAgregar;
  final void Function(int indice)? alTomarFoto;
  final void Function(int indice) alQuitar;

  /// Para leer el daño de cada extra con los nombres del catálogo.
  final Catalogos? catalogos;

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context).textTheme;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Text('Extras del vehículo', style: tema.titleMedium),
        Text('Barras de techo, volco, platón u otro elemento que el dibujo no trae, con sus fotos.', style: tema.bodySmall),
        for (var i = 0; i < extras.length; i++) ...[
          const SizedBox(height: Espacio.sm),
          _FilaExtra(
            extra: extras[i],
            catalogos: catalogos,
            alTomarFoto: alTomarFoto == null ? null : () => alTomarFoto!(i),
            alQuitar: () => alQuitar(i),
          ),
        ],
        const SizedBox(height: Espacio.sm),
        OutlinedButton.icon(onPressed: alAgregar, icon: const Icon(Icons.add), label: const Text('Agregar extra')),
      ],
    );
  }
}

class _FilaExtra extends StatelessWidget {
  const _FilaExtra({required this.extra, required this.alTomarFoto, required this.alQuitar, this.catalogos});

  /// Para leer el daño con sus nombres. Null en las pruebas que no lo necesitan.
  final Catalogos? catalogos;

  final ExtraLocal extra;
  final VoidCallback? alTomarFoto;
  final VoidCallback alQuitar;

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context).textTheme;
    return Tarjeta(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Expanded(child: Text(extra.nombre, style: tema.bodyMedium?.copyWith(color: Colores.textoPrimario))),
              TextButton(
                onPressed: alQuitar,
                child: Semantics(label: 'Quitar extra ${extra.nombre}', child: const Text('Quitar', style: TextStyle(color: Colores.peligro))),
              ),
            ],
          ),
          // El daño, para poder releer lo que se marcó. Sin esto, quien lo puso no tenía dónde comprobarlo y la
          // única salida era quitar el extra entero y volver a empezar.
          if (extra.dano case final dano?)
            Text(
              catalogos == null
                  ? 'Dañado'
                  : 'Dañado · ${catalogos!.etiqueta(Catalogo.clasesDano, dano.clase)} '
                      '(${catalogos!.etiqueta(Catalogo.tamanosDano, dano.tamano)})'
                      '${dano.nivel == null ? '' : ' · ${catalogos!.etiqueta(Catalogo.nivelesSeveridad, dano.nivel!)}'}',
              style: tema.bodySmall?.copyWith(color: Colores.advertencia),
            ),
          if (extra.observacion case final observacion?) Text(observacion, style: tema.bodySmall),
          const SizedBox(height: Espacio.sm),
          Wrap(
            spacing: Espacio.sm,
            runSpacing: Espacio.sm,
            crossAxisAlignment: WrapCrossAlignment.center,
            children: [
              for (final foto in extra.fotos)
                GestureDetector(
                  onTap: () => mostrarVisorImagen(context, imagen: FileImage(File(foto.ruta)), titulo: extra.nombre),
                  child: ClipRRect(
                    borderRadius: BorderRadius.circular(8),
                    child: Image.file(
                      File(foto.ruta),
                      width: 56,
                      height: 48,
                      fit: BoxFit.cover,
                      semanticLabel: 'Foto del extra ${extra.nombre}',
                    ),
                  ),
                ),
              TextButton.icon(
                onPressed: alTomarFoto,
                icon: const Icon(Icons.photo_camera_outlined, size: 18),
                label: Text(extra.fotos.isEmpty ? 'Tomar foto' : 'Otra foto'),
              ),
            ],
          ),
          if (extra.fotos.isEmpty)
            Text('Sin foto no se puede cerrar el informe.', style: tema.bodySmall?.copyWith(color: Colores.advertencia)),
        ],
      ),
    );
  }
}
