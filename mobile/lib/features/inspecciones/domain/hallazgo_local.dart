import '../../dibujo_dano/domain/dibujo_dano.dart';
import 'foto_local.dart';

/// Novedad registrada sobre una zona del vehículo: cuadrante + lado, elegidos en el dibujo de daños.
///
/// Siempre lleva su propia foto: un daño sin foto es la palabra de alguien contra la del siguiente, y el
/// servidor lo rechaza con 400. Va en la misma llamada que el hallazgo.
class HallazgoLocal {
  const HallazgoLocal({
    required this.cuadranteNumero,
    required this.lado,
    required this.zona,
    required this.clase,
    required this.tamano,
    this.nivel,
    this.nota,
    this.foto,
  });

  /// Lee lo que guardó la cola. Un hallazgo encolado por la versión anterior (con `parte` y `coords`, sin
  /// cuadrante) se lee igual, con [cuadranteNumero] nulo: el servidor lo rechaza con 400 y el informe queda en
  /// Inspecciones › Cola para descartarlo. Si fallara al leerse, la cola lo saltaría sin mostrarlo.
  factory HallazgoLocal.fromJson(Map<String, dynamic> json) => HallazgoLocal(
        cuadranteNumero: (json['cuadrante_numero'] as num?)?.toInt(),
        lado: json['lado'] as String?,
        zona: json['zona'] as String? ?? json['parte'] as String? ?? '',
        clase: json['clase'] as String,
        tamano: json['tamano'] as String,
        nivel: json['nivel'] as String?,
        nota: json['nota'] as String?,
        // Los hallazgos que encoló una versión anterior no la traen.
        foto: json['foto'] == null ? null : FotoLocal.fromJson(json['foto'] as Map<String, dynamic>),
      );

  /// Número del cuadrante. Nulo solo en lo que encoló la versión anterior de la app.
  final int? cuadranteNumero;

  /// `izquierdo`, `derecho` o null si la zona es central. Sale del catálogo, nunca del número.
  final String? lado;

  /// Nombre completo de la zona para mostrar («Llanta trasera derecha»). No viaja a la API: el servidor arma el
  /// suyo a partir del cuadrante y el lado.
  final String zona;

  final String clase;
  final String tamano;

  /// Nivel de severidad (`v1_plus`, `v1`, `v2`, `v3`). Obligatorio para el inspector y el administrador; el
  /// conductor no lo envía (el servidor responde 400 si lo hace).
  final String? nivel;

  /// Observación de quien registra («rayón en la compuerta trasera»). Opcional; el servidor admite hasta 500
  /// caracteres.
  final String? nota;

  /// La foto del daño. Nula solo en los hallazgos encolados antes de que fuera obligatoria.
  final FotoLocal? foto;

  /// La clave de la zona («26D»), la misma del dibujo: con ella se cumple «un daño por cuadrante».
  String? get clave => cuadranteNumero == null ? null : claveDeZona(cuadranteNumero!, lado);

  HallazgoLocal copiar({
    String? clase,
    String? tamano,
    String? Function()? nivel,
    String? Function()? nota,
    FotoLocal? foto,
  }) =>
      HallazgoLocal(
        cuadranteNumero: cuadranteNumero,
        lado: lado,
        zona: zona,
        clase: clase ?? this.clase,
        tamano: tamano ?? this.tamano,
        nivel: nivel == null ? this.nivel : nivel(),
        nota: nota == null ? this.nota : nota(),
        foto: foto ?? this.foto,
      );

  HallazgoLocal conFoto(FotoLocal nueva) => copiar(foto: nueva);

  Map<String, dynamic> toJson() => {
        'cuadrante_numero': cuadranteNumero,
        'lado': lado,
        'zona': zona,
        'clase': clase,
        'tamano': tamano,
        'nivel': nivel,
        'nota': nota,
        'foto': foto?.toJson(),
      };
}
