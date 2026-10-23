import '../../vehiculos/domain/coords_diagrama.dart';
import 'foto_local.dart';

/// Novedad registrada sobre una parte del vehículo.
///
/// Siempre lleva su propia foto: un daño sin foto es la palabra de alguien contra la del siguiente, y el
/// servidor lo rechaza con 400. Va en la misma llamada que el hallazgo.
class HallazgoLocal {
  const HallazgoLocal({required this.parte, required this.clase, required this.tamano, this.coords, this.foto});

  factory HallazgoLocal.fromJson(Map<String, dynamic> json) => HallazgoLocal(
        parte: json['parte'] as String,
        clase: json['clase'] as String,
        tamano: json['tamano'] as String,
        coords: json['coords'] == null ? null : CoordsDiagrama.fromJson(json['coords'] as Map<String, dynamic>),
        // Los hallazgos que encoló una versión anterior no la traen.
        foto: json['foto'] == null ? null : FotoLocal.fromJson(json['foto'] as Map<String, dynamic>),
      );

  /// Etiqueta de parte y posición, por ejemplo "Guardafango delantero derecho".
  final String parte;
  final String clase;
  final String tamano;
  final CoordsDiagrama? coords;

  /// La foto del daño. Nula solo en los hallazgos encolados antes de que fuera obligatoria.
  final FotoLocal? foto;

  HallazgoLocal conFoto(FotoLocal nueva) =>
      HallazgoLocal(parte: parte, clase: clase, tamano: tamano, coords: coords, foto: nueva);

  Map<String, dynamic> toJson() =>
      {'parte': parte, 'clase': clase, 'tamano': tamano, 'coords': coords?.toJson(), 'foto': foto?.toJson()};
}
