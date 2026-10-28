/// Los hallazgos que ya traía el vehículo cuando empieza una inspección nueva, para poder marcar un daño como
/// «ya estaba registrado» en vez de contarlo dos veces.
///
/// La API los publica en `/api/inspecciones/{id}/hallazgos-previos`, pero esa ruta pide una inspección ya creada y
/// aquí no la hay: el informe se arma entero en el equipo y se crea al cerrarlo, para poder capturar sin señal. Por
/// eso se sacan del inventario del VIN, que es la misma historia del vehículo y ya está descargada al entrar.
library;

import '../../dibujo_dano/domain/dibujo_dano.dart';
import '../../vehiculos/domain/inventario.dart';

/// Un daño de una inspección anterior del mismo vehículo.
class HallazgoPrevio {
  const HallazgoPrevio({
    required this.id,
    required this.zona,
    required this.proceso,
    required this.momento,
    this.cuadranteNumero,
    this.lado,
    this.clase,
    this.tamano,
    this.cerradaEn,
    this.fotoUrl,
  });

  factory HallazgoPrevio.fromJson(Map<String, dynamic> json) => HallazgoPrevio(
        id: json['id'] as String,
        zona: json['zona'] as String,
        proceso: json['proceso'] as String,
        momento: json['momento'] as String,
        cuadranteNumero: (json['cuadrante_numero'] as num?)?.toInt(),
        lado: json['lado'] as String?,
        clase: json['clase'] as String?,
        tamano: json['tamano'] as String?,
        cerradaEn: switch (json['cerrada_en']) { final String f => DateTime.tryParse(f), _ => null },
        fotoUrl: json['foto_url'] as String?,
      );

  /// El que viaja como `hallazgo_origen_id` cuando se confirma que es el mismo daño.
  final String id;

  /// El texto que armó el servidor: «Izquierdo 7 · Espejo retrovisor».
  final String zona;

  /// Código de la etapa donde se registró (cargue, transporte, descargue).
  final String proceso;
  final String momento;

  final int? cuadranteNumero;
  final String? lado;
  final String? clase;
  final String? tamano;
  final DateTime? cerradaEn;

  /// La foto del daño, para compararla con el vehículo que se tiene delante.
  final String? fotoUrl;

  /// La clave de la zona («7I»), la misma del dibujo. Null en un hallazgo histórico que nunca se ubicó.
  String? get clave => cuadranteNumero == null ? null : claveDeZona(cuadranteNumero!, lado);

  Map<String, dynamic> toJson() => {
        'id': id,
        'zona': zona,
        'proceso': proceso,
        'momento': momento,
        'cuadrante_numero': cuadranteNumero,
        'lado': lado,
        'clase': clase,
        'tamano': tamano,
        'cerrada_en': cerradaEn?.toIso8601String(),
        'foto_url': fotoUrl,
      };
}

/// Los hallazgos previos que se leen del [inventario], del más reciente al más antiguo.
///
/// Solo cuentan las inspecciones **cerradas**: un borrador todavía no es historia del vehículo, y el servidor usa
/// el mismo corte.
List<HallazgoPrevio> hallazgosPreviosDe(Inventario inventario) {
  final previos = <({DateTime orden, HallazgoPrevio hallazgo})>[];
  for (final etapa in inventario.etapas) {
    for (final inspeccion in etapa.inspecciones) {
      if (!inspeccion.cerrada) continue;
      final fotos = {
        for (final e in inspeccion.evidencias)
          if (e.hallazgoId != null) e.hallazgoId!: e.url,
      };
      for (final h in inspeccion.hallazgos) {
        previos.add((
          orden: inspeccion.cerradaEn ?? inspeccion.fecha,
          hallazgo: HallazgoPrevio(
            id: h.id,
            zona: h.zona,
            proceso: etapa.proceso,
            momento: inspeccion.momento,
            cuadranteNumero: h.cuadranteNumero,
            lado: h.lado,
            clase: h.clase,
            tamano: h.tamano,
            cerradaEn: inspeccion.cerradaEn,
            fotoUrl: fotos[h.id],
          ),
        ));
      }
    }
  }
  previos.sort((a, b) => b.orden.compareTo(a.orden));
  return [for (final p in previos) p.hallazgo];
}

/// El daño previo que se propone para una zona y una clase: el más reciente de los que coinciden en **las dos**
/// cosas. Null si no hay ninguno, que es lo normal.
///
/// Zona y clase juntas, porque es lo que el servidor exige para aceptar `hallazgo_origen_id`: un golpe no es la
/// continuación de un rayón aunque estén en la misma pieza.
HallazgoPrevio? previoDe(List<HallazgoPrevio> previos, {required String? clave, required String clase}) {
  if (clave == null) return null;
  return previos.where((p) => p.clave == clave && p.clase == clase).firstOrNull;
}
