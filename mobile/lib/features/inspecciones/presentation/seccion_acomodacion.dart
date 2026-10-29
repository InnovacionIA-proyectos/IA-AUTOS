import 'dart:io';

import 'package:flutter/material.dart';

import '../../../core/tema/tema.dart';
import '../../../core/ui/visor_imagen.dart';
import '../../../core/ui/widgets.dart';
import '../../viajes/domain/viaje.dart';
import '../domain/acomodacion.dart';
import '../domain/foto_local.dart';
import '../domain/reglas_informe.dart';

/// La acomodación de la segunda inspección del cargue: el lugar de cada unidad en la niñera y las fotos de cómo
/// quedaron.
///
/// [unidades] es null mientras no se conozca el viaje. [alElegir] recibe el lugar elegido, o null cuando se suelta.
class SeccionAcomodacion extends StatelessWidget {
  const SeccionAcomodacion({
    super.key,
    required this.unidades,
    required this.posiciones,
    required this.fotos,
    required this.fotosMinimas,
    required this.alElegir,
    required this.alTomarFoto,
    this.alReintentar,
    this.precisionBaja,
  });

  final List<ViajeVehiculo>? unidades;
  final Map<String, int> posiciones;
  final List<FotoLocal> fotos;
  final int fotosMinimas;
  final void Function(String vin, int? posicion) alElegir;
  final VoidCallback? alTomarFoto;

  /// Vuelve a pedir el viaje cuando no se pudo traer.
  final VoidCallback? alReintentar;
  final double? precisionBaja;

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context).textTheme;
    final lista = unidades;
    final fotosAcomodacion = fotosDeAcomodacion(fotos);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text('Acomodación en la niñera', style: tema.titleMedium),
        Text('Elige el lugar de cada unidad según el esquema de la Orden de Cargue.', style: tema.bodySmall),
        const SizedBox(height: Espacio.md),
        _Esquema(posiciones: posiciones),
        const SizedBox(height: Espacio.md),
        if (lista == null)
          Tarjeta(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text('No se pudieron cargar las unidades del viaje.', style: tema.bodySmall),
                if (alReintentar != null) TextButton(onPressed: alReintentar, child: const Text('Reintentar')),
              ],
            ),
          )
        else
          for (final unidad in lista) ...[
            _FilaUnidad(
              unidad: unidad,
              posiciones: posiciones,
              alElegir: (posicion) => alElegir(unidad.vin, posicion),
            ),
            const SizedBox(height: Espacio.sm),
          ],
        const SizedBox(height: Espacio.md),
        Text('Fotos de la acomodación', style: tema.titleSmall),
        Text(
          fotosMinimas > 0
              ? 'Se piden al menos $fotosMinimas. Llevan fecha y ubicación como las demás.'
              : 'Llevan fecha y ubicación como las demás.',
          style: tema.bodySmall,
        ),
        const SizedBox(height: Espacio.sm),
        if (fotosAcomodacion.isNotEmpty)
          Wrap(
            spacing: Espacio.sm,
            runSpacing: Espacio.sm,
            children: [
              for (var i = 0; i < fotosAcomodacion.length; i++)
                _Miniatura(foto: fotosAcomodacion[i], numero: i + 1, precisionBaja: precisionBaja),
            ],
          ),
        const SizedBox(height: Espacio.sm),
        OutlinedButton.icon(
          onPressed: alTomarFoto,
          icon: const Icon(Icons.photo_camera_outlined),
          label: Text(fotosAcomodacion.isEmpty ? 'Tomar foto de la acomodación' : 'Otra foto de la acomodación'),
        ),
      ],
    );
  }
}

/// El esquema de la Orden de Cargue con quién ocupa cada lugar: arriba 4-3-2-1 (A1) y abajo 7-6-5 (B1).
class _Esquema extends StatelessWidget {
  const _Esquema({required this.posiciones});

  final Map<String, int> posiciones;

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context).textTheme;
    return Semantics(
      label: 'Esquema de la niñera: arriba 4, 3, 2, 1 y abajo 7, 6, 5',
      child: Tarjeta(
        child: Column(
          children: [
            for (final fila in esquemaNinera) ...[
              Row(
                children: [
                  for (final posicion in fila) Expanded(child: _Lugar(posicion: posicion, vin: ocupante(posiciones, posicion))),
                ],
              ),
              const SizedBox(height: Espacio.xs),
            ],
            Text('Arriba (A1): 4-3-2-1 · Abajo (B1): 7-6-5', style: tema.bodySmall),
          ],
        ),
      ),
    );
  }
}

class _Lugar extends StatelessWidget {
  const _Lugar({required this.posicion, required this.vin});

  final int posicion;
  final String? vin;

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context).textTheme;
    final ocupante = vin;
    return Container(
      margin: const EdgeInsets.all(2),
      padding: const EdgeInsets.symmetric(vertical: Espacio.sm),
      decoration: BoxDecoration(
        color: ocupante != null ? Colores.exito.withValues(alpha: 0.12) : Colores.superficie,
        border: Border.all(color: ocupante != null ? Colores.exito : Colores.borde),
        borderRadius: BorderRadius.circular(8),
      ),
      child: Column(
        children: [
          Text('$posicion', style: tema.titleMedium),
          Text(
            ocupante == null ? 'libre' : ocupante.substring(ocupante.length > 6 ? ocupante.length - 6 : 0),
            style: tema.labelSmall,
            maxLines: 1,
          ),
        ],
      ),
    );
  }
}

class _FilaUnidad extends StatelessWidget {
  const _FilaUnidad({required this.unidad, required this.posiciones, required this.alElegir});

  final ViajeVehiculo unidad;
  final Map<String, int> posiciones;
  final void Function(int? posicion) alElegir;

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context).textTheme;
    final actual = posiciones[unidad.vin];
    final descripcion = [unidad.marca, unidad.modelo].whereType<String>().join(' ');
    return Tarjeta(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          TextoMono(unidad.vin),
          if (descripcion.isNotEmpty) Text(descripcion, style: tema.bodySmall),
          const SizedBox(height: Espacio.sm),
          if (!unidad.sale)
            Text('No sale del viaje: no lleva posición.', style: tema.bodySmall)
          else
            Wrap(
              spacing: Espacio.sm,
              runSpacing: Espacio.sm,
              children: [
                for (var p = posicionMinima; p <= posicionMaxima; p++)
                  _ChipPosicion(
                    posicion: p,
                    seleccionada: actual == p,
                    // Una posición que ya tiene otra unidad no se ofrece: el servidor la rechazaría con 409.
                    ocupadaPor: ocupante(posiciones, p, except: unidad.vin),
                    alElegir: alElegir,
                  ),
              ],
            ),
          if (unidad.sale && actual == null)
            Text('Sin posición todavía', style: tema.bodySmall?.copyWith(color: Colores.advertencia)),
        ],
      ),
    );
  }
}

class _ChipPosicion extends StatelessWidget {
  const _ChipPosicion({required this.posicion, required this.seleccionada, required this.ocupadaPor, required this.alElegir});

  final int posicion;
  final bool seleccionada;
  final String? ocupadaPor;
  final void Function(int? posicion) alElegir;

  @override
  Widget build(BuildContext context) {
    return Semantics(
      label: ocupadaPor == null ? 'Posición $posicion' : 'Posición $posicion, ocupada por $ocupadaPor',
      child: ChoiceChip(
        label: SizedBox(width: 24, child: Text('$posicion', textAlign: TextAlign.center)),
        selected: seleccionada,
        onSelected: ocupadaPor != null ? null : (elegida) => alElegir(elegida ? posicion : null),
      ),
    );
  }
}

class _Miniatura extends StatelessWidget {
  const _Miniatura({required this.foto, required this.numero, this.precisionBaja});

  final FotoLocal foto;
  final int numero;
  final double? precisionBaja;

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context).textTheme;
    final sinUbicacion = foto.sinUbicacion;
    final imprecisa = !sinUbicacion && (foto.precision ?? 0) > (precisionBaja ?? precisionBajaMetrosPorDefecto);
    return SizedBox(
      width: 88,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          GestureDetector(
            onTap: () => mostrarVisorImagen(context, imagen: FileImage(File(foto.ruta)), titulo: 'Acomodación $numero'),
            child: ClipRRect(
              borderRadius: BorderRadius.circular(8),
              child: Image.file(
                File(foto.ruta),
                width: 88,
                height: 66,
                fit: BoxFit.cover,
                semanticLabel: 'Foto $numero de la acomodación',
                errorBuilder: (_, _, _) => Container(
                  width: 88,
                  height: 66,
                  color: Colores.superficie,
                  child: const Icon(Icons.photo_outlined),
                ),
              ),
            ),
          ),
          if (sinUbicacion)
            Text('Sin ubicación: repítela', style: tema.labelSmall?.copyWith(color: Colores.peligro))
          else if (imprecisa)
            Text('±${foto.precision!.round()} m', style: tema.labelSmall?.copyWith(color: Colores.advertencia)),
        ],
      ),
    );
  }
}
