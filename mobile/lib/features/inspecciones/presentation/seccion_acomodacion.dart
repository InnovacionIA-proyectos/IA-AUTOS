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
    this.viaje,
    this.alReintentar,
    this.precisionBaja,
  });

  /// El viaje tal como lo manda el servidor: de él salen los puestos que se dibujan y cuántos hay. Null mientras
  /// no se haya podido traer.
  final Viaje? viaje;

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
        Text(
          'Elige el lugar de cada unidad. Los puestos son los de esta niñera: ${capacidadDe(viaje)} en total.',
          style: tema.bodySmall,
        ),
        const SizedBox(height: Espacio.md),
        _Esquema(posiciones: posiciones, puestos: puestosDe(viaje)),
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
              puestos: puestosDe(viaje),
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

/// La niñera dibujada con quién ocupa cada puesto.
///
/// Los puestos y su orden llegan del servidor: la app no lleva escrita ninguna disposición. Con una niñera de
/// nueve se dibujan nueve; con un viaje antiguo que no trae disposición, tantos como su capacidad.
class _Esquema extends StatelessWidget {
  const _Esquema({required this.posiciones, required this.puestos});

  final Map<String, int> posiciones;
  final List<PuestoNinera> puestos;

  @override
  Widget build(BuildContext context) {
    final tema = Theme.of(context).textTheme;
    return Semantics(
      label: 'Niñera de ${puestos.length} puestos: ${puestos.map((p) => p.etiqueta).join(', ')}',
      child: Tarjeta(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            // Se envuelven en vez de repartirse en filas fijas: la disposición puede traer cualquier cantidad y
            // forzarla a un alto y un ancho inventados aquí es volver al esquema escrito en el código.
            Wrap(
              spacing: Espacio.xs,
              runSpacing: Espacio.xs,
              children: [
                for (final puesto in puestos)
                  SizedBox(
                    width: 80,
                    child: _Lugar(puesto: puesto, vin: ocupante(posiciones, puesto.numero)),
                  ),
              ],
            ),
            const SizedBox(height: Espacio.xs),
            Text('${puestos.length} puestos, en el orden de la Orden de Cargue.', style: tema.bodySmall),
          ],
        ),
      ),
    );
  }
}

class _Lugar extends StatelessWidget {
  const _Lugar({required this.puesto, required this.vin});

  final PuestoNinera puesto;
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
          Text(puesto.etiqueta, style: tema.titleMedium),
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
  const _FilaUnidad({required this.unidad, required this.posiciones, required this.puestos, required this.alElegir});

  final ViajeVehiculo unidad;
  final Map<String, int> posiciones;
  final List<PuestoNinera> puestos;
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
                // Solo los puestos que la niñera tiene: uno mayor que la capacidad lo rechaza el servidor
                // nombrando cuántos hay, así que ni se ofrece.
                for (final puesto in puestos)
                  _ChipPosicion(
                    puesto: puesto,
                    seleccionada: actual == puesto.numero,
                    // Una posición que ya tiene otra unidad no se ofrece: el servidor la rechazaría con 409.
                    ocupadaPor: ocupante(posiciones, puesto.numero, except: unidad.vin),
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
  const _ChipPosicion({required this.puesto, required this.seleccionada, required this.ocupadaPor, required this.alElegir});

  final PuestoNinera puesto;
  final bool seleccionada;
  final String? ocupadaPor;
  final void Function(int? posicion) alElegir;

  @override
  Widget build(BuildContext context) {
    return Semantics(
      label: ocupadaPor == null ? 'Puesto ${puesto.etiqueta}' : 'Puesto ${puesto.etiqueta}, ocupado por $ocupadaPor',
      child: ChoiceChip(
        label: SizedBox(width: 32, child: Text(puesto.etiqueta, textAlign: TextAlign.center)),
        selected: seleccionada,
        onSelected: ocupadaPor != null ? null : (elegida) => alElegir(elegida ? puesto.numero : null),
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
    // Sin precisión no es «precisa»: es que la lectura no era de ese momento y nadie la comprobó.
    final sinComprobar = !sinUbicacion && foto.precision == null;
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
          else if (sinComprobar)
            Text('Sin comprobar', style: tema.labelSmall?.copyWith(color: Colores.advertencia))
          else if (imprecisa)
            Text('±${foto.precision!.round()} m', style: tema.labelSmall?.copyWith(color: Colores.advertencia)),
        ],
      ),
    );
  }
}
