import 'dart:io';

import 'package:flutter/material.dart';

import '../../../core/tema/tema.dart';
import '../../../core/ui/visor_imagen.dart';
import '../domain/checklist_vistas.dart';
import '../domain/foto_local.dart';
import '../domain/hallazgo_local.dart';
import '../domain/reglas_informe.dart';

/// Las dos filas de la captura. Viven aparte de la pantalla para poder probarlas: lo que se captura tiene que
/// verse, y eso solo se comprueba montándolas.

/// Una fila del checklist: qué vista es, si ya tiene foto y cómo está esa foto.
class FilaVista extends StatelessWidget {
  const FilaVista({super.key, required this.vista, required this.foto, required this.alTomar});

  final VistaChecklist vista;

  /// La última foto de la vista; null si todavía no hay.
  final FotoLocal? foto;
  final VoidCallback? alTomar;

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context).textTheme;
    final actual = foto;
    return Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        if (actual == null)
          Container(
            width: 56,
            height: 48,
            decoration: BoxDecoration(color: Colores.superficie, borderRadius: BorderRadius.circular(8)),
            child: const Icon(Icons.photo_camera_outlined, size: 20, color: Colores.textoSecundario),
          )
        else
          // Se puede abrir: a 56 px no se ve si la foto salió movida u oscura.
          GestureDetector(
            onTap: () => mostrarVisorImagen(context, imagen: FileImage(File(actual.ruta)), titulo: vista.nombre),
            child: ClipRRect(
              borderRadius: BorderRadius.circular(8),
              child: Image.file(File(actual.ruta), width: 56, height: 48, fit: BoxFit.cover, semanticLabel: 'Foto de ${vista.nombre}'),
            ),
          ),
        const SizedBox(width: Espacio.sm),
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(vista.nombre, style: tema.bodyMedium?.copyWith(color: Colores.textoPrimario)),
              // Cada cosa en su línea, no una excluyendo a las otras: la nota de una foto adicional quedaba
              // tapada por el aviso de precisión, que bajo techo sale siempre.
              if (!vista.obligatoria && actual == null) Text('Solo si hay faltante o daño', style: tema.bodySmall),
              if (actual?.nota case final nota? when nota.isNotEmpty)
                Text(nota, style: tema.bodySmall?.copyWith(color: Colores.textoPrimario)),
              if (actual != null && actual.sinUbicacion)
                // El sello es lo que da valor a la evidencia: sin ubicación hay que repetirla antes de cerrar.
                Text('Sin ubicación: repítela', style: tema.bodySmall?.copyWith(color: Colores.peligro))
              else if (actual != null && (actual.precision ?? 0) > precisionBajaMetros)
                // Avisa, no bloquea: en un patio techado el GPS siempre es malo.
                Text(
                  'Ubicación poco precisa (±${actual.precision!.round()} m)',
                  style: tema.bodySmall?.copyWith(color: Colores.advertencia),
                ),
            ],
          ),
        ),
        const SizedBox(width: Espacio.sm),
        TextButton(onPressed: alTomar, child: Text(actual == null ? 'Tomar' : 'Repetir')),
      ],
    );
  }
}

/// Una novedad ya registrada, con su foto.
class FilaHallazgo extends StatelessWidget {
  const FilaHallazgo({
    super.key,
    required this.hallazgo,
    required this.clase,
    required this.tamano,
    this.nivel,
    required this.alQuitar,
  });

  final HallazgoLocal hallazgo;

  /// Nombres ya resueltos con el catálogo por quien construye la fila.
  final String clase;
  final String tamano;

  /// Nombre del nivel («V2»); null si no se asignó (el conductor no lo asigna).
  final String? nivel;
  final VoidCallback alQuitar;

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context).textTheme;
    // Su propio aire arriba y abajo: con varias novedades las fotos quedaban pegadas una a otra y la lista se
    // leía como un bloque, sin saber dónde termina una novedad y empieza la siguiente.
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: Espacio.xs),
      child: Row(
        children: [
          // La foto del daño se ve: si no, quien la tomó no tiene cómo comprobar que quedó bien y se entera
          // cuando ya no está delante del vehículo.
          if (hallazgo.foto case final foto?)
            Padding(
              padding: const EdgeInsets.only(right: Espacio.sm),
              child: GestureDetector(
                onTap: () => mostrarVisorImagen(
                  context,
                  imagen: FileImage(File(foto.ruta)),
                  titulo: 'Novedad en ${hallazgo.zona}',
                ),
                child: ClipRRect(
                  borderRadius: BorderRadius.circular(8),
                  child: Image.file(
                    File(foto.ruta),
                    // 48 de alto: es una zona táctil desde que la foto se puede abrir.
                    width: 56,
                    height: 48,
                    fit: BoxFit.cover,
                    semanticLabel: 'Foto de la novedad en ${hallazgo.zona}',
                  ),
                ),
              ),
            ),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  '${hallazgo.zona}: $clase ($tamano)${nivel == null ? '' : ' · $nivel'}',
                  style: tema.bodySmall?.copyWith(color: Colores.textoPrimario),
                ),
                if (hallazgo.nota case final nota?) Text(nota, style: tema.bodySmall),
              ],
            ),
          ),
          TextButton(
            onPressed: alQuitar,
            child: Semantics(label: 'Quitar hallazgo ${hallazgo.zona}', child: const Text('Quitar', style: TextStyle(color: Colores.peligro))),
          ),
        ],
      ),
    );
  }
}
