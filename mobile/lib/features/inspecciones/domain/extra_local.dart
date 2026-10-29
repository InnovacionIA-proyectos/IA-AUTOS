import 'foto_local.dart';

/// Código del tipo «Otro»: el que no está en el catálogo. Exige el texto que dice qué es.
const tipoExtraOtro = 'otro';

/// Lo que admite la observación de un extra, igual que la nota de un hallazgo.
const observacionExtraMax = 500;

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

  ExtraLocal conFoto(FotoLocal foto) => ExtraLocal(
        tipo: tipo,
        nombre: nombre,
        textoOtro: textoOtro,
        observacion: observacion,
        fotos: [...fotos, foto],
        extraId: extraId,
      );

  ExtraLocal conId(String id) => ExtraLocal(
        tipo: tipo,
        nombre: nombre,
        textoOtro: textoOtro,
        observacion: observacion,
        fotos: fotos,
        extraId: id,
      );

  ExtraLocal conFotos(List<FotoLocal> nuevas) => ExtraLocal(
        tipo: tipo,
        nombre: nombre,
        textoOtro: textoOtro,
        observacion: observacion,
        fotos: nuevas,
        extraId: extraId,
      );

  Map<String, dynamic> toJson() => {
        'tipo': tipo,
        'nombre': nombre,
        'texto_otro': textoOtro,
        'observacion': observacion,
        'fotos': [for (final f in fotos) f.toJson()],
        'extra_id': extraId,
      };
}
