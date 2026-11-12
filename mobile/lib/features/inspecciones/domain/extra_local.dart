import 'foto_local.dart';

/// Código del tipo «Otro»: el que no está en el catálogo. Exige el texto que dice qué es.
const tipoExtraOtro = 'otro';

/// Lo que admite la observación de un extra, igual que la nota de un hallazgo.
const observacionExtraMax = 500;

/// El daño de un extra: los mismos tres datos de un hallazgo de zona. Lo que no tiene es zona, y por eso no
/// pone ningún punto en el dibujo.
///
/// Va completo o no va: un extra marcado como dañado sin clase o sin tamaño no es un daño a medias, es un
/// extra que no se terminó de describir, y el servidor lo rechaza. El nivel lo pone quien puede ponerlo: al
/// conductor no se le pide, igual que en un hallazgo de zona.
class DanoExtra {
  const DanoExtra({required this.clase, required this.tamano, this.nivel});

  factory DanoExtra.fromJson(Map<String, dynamic> json) => DanoExtra(
        clase: json['clase'] as String,
        tamano: json['tamano'] as String,
        nivel: json['nivel'] as String?,
      );

  final String clase;
  final String tamano;
  final String? nivel;

  Map<String, dynamic> toJson() => {'clase': clase, 'tamano': tamano, 'nivel': nivel};
}

/// Un elemento del vehículo que el dibujo estándar no trae (barras de techo, volco, platón). Se reporta con sus
/// fotos selladas y una observación, sin inventarle zonas al dibujo.
class ExtraLocal {
  const ExtraLocal({
    required this.tipo,
    required this.nombre,
    this.textoOtro,
    this.observacion,
    this.fotos = const [],
    this.extraId,
    this.dano,
  });

  factory ExtraLocal.fromJson(Map<String, dynamic> json) => ExtraLocal(
        tipo: json['tipo'] as String,
        nombre: json['nombre'] as String,
        textoOtro: json['texto_otro'] as String?,
        observacion: json['observacion'] as String?,
        fotos: [
          for (final f in json['fotos'] as List<dynamic>? ?? const []) FotoLocal.fromJson(f as Map<String, dynamic>),
        ],
        extraId: json['extra_id'] as String?,
        dano: switch (json['dano']) {
          final Map<String, dynamic> d => DanoExtra.fromJson(d),
          _ => null,
        },
      );

  /// Código del catálogo de tipos de extra, o [tipoExtraOtro].
  final String tipo;

  /// Para mostrar: «Platón», u «Otro: portabicicletas».
  final String nombre;

  /// Qué es, cuando el tipo es «Otro». Nulo en los demás.
  final String? textoOtro;

  final String? observacion;
  final List<FotoLocal> fotos;

  /// El id que le dio el servidor al crearlo. Mientras sea nulo, el extra no se ha enviado; con él, un corte entre la
  /// creación y sus fotos no lo crea dos veces.
  final String? extraId;

  /// Su daño, si se marcó como dañado. Null es un extra que simplemente está ahí.
  final DanoExtra? dano;

  bool get estaDanado => dano != null;

  ExtraLocal conFoto(FotoLocal foto) => ExtraLocal(
        tipo: tipo,
        nombre: nombre,
        textoOtro: textoOtro,
        observacion: observacion,
        fotos: [...fotos, foto],
        extraId: extraId,
        dano: dano,
      );

  ExtraLocal conId(String id) => ExtraLocal(
        tipo: tipo,
        nombre: nombre,
        textoOtro: textoOtro,
        observacion: observacion,
        fotos: fotos,
        extraId: id,
        dano: dano,
      );

  ExtraLocal conFotos(List<FotoLocal> nuevas) => ExtraLocal(
        tipo: tipo,
        nombre: nombre,
        textoOtro: textoOtro,
        observacion: observacion,
        fotos: nuevas,
        extraId: extraId,
        dano: dano,
      );

  Map<String, dynamic> toJson() => {
        'tipo': tipo,
        'nombre': nombre,
        'texto_otro': textoOtro,
        'observacion': observacion,
        'fotos': [for (final f in fotos) f.toJson()],
        'extra_id': extraId,
        'dano': dano?.toJson(),
      };
}
