import 'extra_local.dart';
import 'foto_local.dart';
import 'hallazgo_local.dart';

/// Lo capturado de un informe que todavía no se cerró, guardado en el teléfono mientras se hace (S8-HU-11.2 CA-4).
///
/// Salir de la captura ya no pierde nada: al volver se ofrece continuar con esto o empezar de nuevo. Guarda solo lo
/// que el job de la cola ya guarda al cerrar —fotos con su sello, daños, extras y notas— y ningún dato personal
/// más (CA-9).
class BorradorCaptura {
  const BorradorCaptura({
    this.fotos = const [],
    this.hallazgos = const [],
    this.extras = const [],
    this.notas = '',
    this.archivos = const {},
  });

  factory BorradorCaptura.fromJson(Map<String, dynamic> json) => BorradorCaptura(
        fotos: [for (final f in json['fotos'] as List<dynamic>? ?? const []) FotoLocal.fromJson(f as Map<String, dynamic>)],
        hallazgos: [
          for (final h in json['hallazgos'] as List<dynamic>? ?? const []) HallazgoLocal.fromJson(h as Map<String, dynamic>),
        ],
        extras: [for (final e in json['extras'] as List<dynamic>? ?? const []) ExtraLocal.fromJson(e as Map<String, dynamic>)],
        notas: json['notas'] as String? ?? '',
        archivos: {for (final a in json['archivos'] as List<dynamic>? ?? const []) a as String},
      );

  /// Las fotos del checklist y de la acomodación.
  final List<FotoLocal> fotos;
  final List<HallazgoLocal> hallazgos;
  final List<ExtraLocal> extras;
  final String notas;

  /// Todo archivo de foto que se tomó en esta captura, también el de un daño o un extra que después se quitó: es
  /// lo que se borra al empezar de nuevo, para que no queden fotos sin dueño en el teléfono (CA-5).
  final Set<String> archivos;

  /// Lo que se cuenta como fotos al ofrecer continuar: las del checklist y la acomodación, y las de los extras.
  int get cuantasFotos => fotos.length + extras.fold(0, (n, e) => n + e.fotos.length);

  int get cuantosDanos => hallazgos.length;

  /// Sin nada capturado no hay nada que ofrecer continuar.
  bool get vacio => fotos.isEmpty && hallazgos.isEmpty && extras.isEmpty && notas.trim().isEmpty;

  Map<String, dynamic> toJson() => {
        'fotos': [for (final f in fotos) f.toJson()],
        'hallazgos': [for (final h in hallazgos) h.toJson()],
        'extras': [for (final e in extras) e.toJson()],
        'notas': notas,
        'archivos': archivos.toList(),
      };
}

/// «Continuar lo guardado (4 fotos, 1 daño)».
String etiquetaContinuar(BorradorCaptura borrador) {
  final fotos = borrador.cuantasFotos == 1 ? '1 foto' : '${borrador.cuantasFotos} fotos';
  final danos = borrador.cuantosDanos == 1 ? '1 daño' : '${borrador.cuantosDanos} daños';
  return 'Continuar lo guardado ($fotos, $danos)';
}

/// Un borrador por persona y por momento: el de la salida del cargue de un VIN no es el de su llegada, ni el de
/// una corrección es el de la primera captura, ni el de Ana es el de Diana en el mismo teléfono.
String claveBorrador({
  required String usuarioId,
  required String vin,
  required String etapaId,
  required String momento,
  String? corrigeInspeccionId,
}) =>
    'borrador-captura:$usuarioId:$vin:$etapaId:$momento:${corrigeInspeccionId ?? ''}';

/// Dónde se guardan los borradores de captura en el teléfono.
abstract interface class AlmacenBorradores {
  Future<BorradorCaptura?> leer(String clave);

  Future<void> guardar(String clave, BorradorCaptura borrador);

  Future<void> borrar(String clave);
}
