import '../../vehiculos/domain/coords_diagrama.dart';

/// Novedad registrada sobre una parte del vehículo.
class HallazgoLocal {
  const HallazgoLocal({required this.parte, required this.clase, required this.tamano, this.coords});

  factory HallazgoLocal.fromJson(Map<String, dynamic> json) => HallazgoLocal(
        parte: json['parte'] as String,
        clase: json['clase'] as String,
        tamano: json['tamano'] as String,
        coords: json['coords'] == null ? null : CoordsDiagrama.fromJson(json['coords'] as Map<String, dynamic>),
      );

  /// Etiqueta de parte y posición, por ejemplo "Guardafango delantero derecho".
  final String parte;
  final String clase;
  final String tamano;
  final CoordsDiagrama? coords;

  Map<String, dynamic> toJson() => {'parte': parte, 'clase': clase, 'tamano': tamano, 'coords': coords?.toJson()};
}
