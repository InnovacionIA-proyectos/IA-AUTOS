/// Los hallazgos que ya traía el vehículo cuando empieza una inspección nueva, para poder marcar un daño como
/// «ya estaba registrado» en vez de contarlo dos veces.
///
/// Salen de `/api/vehiculos/{vin}/hallazgos-previos`, que es toda la historia del vehículo venga del viaje que
/// venga. Antes se deducían del inventario del VIN, pero ese va recortado a los viajes propios: un daño que el
/// vehículo traía de un viaje anterior, con otra tripulación, no aparecía, y es justo el que hay que proponer.
library;

import '../../dibujo_dano/domain/dibujo_dano.dart';

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
    this.nivel,
    this.codigoMtrack,
    this.cerradaEn,
    this.fotoUrl,
    this.fotoVenceEn,
  });

  /// Un hallazgo previo como lo manda la API. Su foto es la primera de `evidencias`: la de cuando se registró, que
  /// es la que sirve para compararla con el vehículo que se tiene delante.
  factory HallazgoPrevio.fromApi(Map<String, dynamic> json) {
    final foto = switch (json['evidencias']) {
      [final Map<String, dynamic> primera, ...] => primera,
      _ => const <String, dynamic>{},
    };
    return HallazgoPrevio(
      id: json['id'] as String,
      zona: json['zona'] as String? ?? '',
      proceso: json['proceso'] as String,
      momento: json['momento'] as String,
      cuadranteNumero: (json['cuadrante_numero'] as num?)?.toInt(),
      lado: json['lado'] as String?,
      clase: json['clase_dano'] as String?,
      tamano: json['tamano_dano'] as String?,
      nivel: json['nivel_severidad'] as String?,
      codigoMtrack: json['codigo_mtrack'] as String?,
      cerradaEn: switch (json['cerrada_at']) { final String f => DateTime.tryParse(f), _ => null },
      fotoUrl: foto['url'] as String?,
      fotoVenceEn: switch (foto['url_expira_at']) { final String f => DateTime.tryParse(f), _ => null },
    );
  }

  factory HallazgoPrevio.fromJson(Map<String, dynamic> json) => HallazgoPrevio(
        id: json['id'] as String,
        zona: json['zona'] as String,
        proceso: json['proceso'] as String,
        momento: json['momento'] as String,
        cuadranteNumero: (json['cuadrante_numero'] as num?)?.toInt(),
        lado: json['lado'] as String?,
        clase: json['clase'] as String?,
        tamano: json['tamano'] as String?,
        nivel: json['nivel'] as String?,
        codigoMtrack: json['codigo_mtrack'] as String?,
        cerradaEn: switch (json['cerrada_en']) { final String f => DateTime.tryParse(f), _ => null },
        fotoUrl: json['foto_url'] as String?,
        fotoVenceEn: switch (json['foto_vence_en']) { final String f => DateTime.tryParse(f), _ => null },
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

  /// Nivel de severidad con que quedó clasificado. Va nulo en una novedad del conductor que nadie revisó.
  final String? nivel;

  /// El código MTrack del viaje donde se registró: es como el negocio nombra un viaje, y sin él «de qué viaje
  /// viene» no se puede responder en pantalla.
  final String? codigoMtrack;

  final DateTime? cerradaEn;

  /// La foto del daño, para compararla con el vehículo que se tiene delante.
  final String? fotoUrl;

  /// Hasta cuándo vale su enlace. Guardados para usarlos sin señal, casi siempre llegan vencidos: entonces se
  /// vuelven a pedir en vez de mostrar un hueco.
  final DateTime? fotoVenceEn;

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
        'nivel': nivel,
        'codigo_mtrack': codigoMtrack,
        'cerrada_en': cerradaEn?.toIso8601String(),
        'foto_url': fotoUrl,
        'foto_vence_en': fotoVenceEn?.toIso8601String(),
      };
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
