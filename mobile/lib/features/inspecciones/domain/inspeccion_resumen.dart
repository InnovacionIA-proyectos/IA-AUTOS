/// Fila del listado "Mis inspecciones" (GET /api/inspecciones).
class InspeccionResumen {
  const InspeccionResumen({
    required this.id,
    this.vin,
    required this.momento,
    required this.momentoNombre,
    required this.fecha,
    required this.estado,
    this.version = 1,
    this.corrigeInspeccionId,
  });

  factory InspeccionResumen.fromJson(Map<String, dynamic> json) => InspeccionResumen(
        id: json['id'] as String,
        vin: json['vin'] as String?,
        momento: json['momento'] as String,
        momentoNombre: json['momento_nombre'] as String? ?? json['momento'] as String,
        fecha: json['fecha'] as String,
        estado: json['estado'] as String,
        version: json['version'] as int? ?? 1,
        corrigeInspeccionId: json['corrige_inspeccion_id'] as String?,
      );

  final String id;
  final String? vin;
  final String momento;

  /// Texto listo para mostrar que manda la API («Inspección de salida»). No es un catalogo administrable.
  final String momentoNombre;

  final String fecha;
  final String estado;

  /// La cadena de versiones: 1 en la original; 2, 3… en las correcciones, que referencian la que corrigen.
  final int version;
  final String? corrigeInspeccionId;

  Map<String, dynamic> toJson() => {
        'id': id,
        'vin': vin,
        'momento': momento,
        'momento_nombre': momentoNombre,
        'fecha': fecha,
        'estado': estado,
        'version': version,
        'corrige_inspeccion_id': corrigeInspeccionId,
      };

  DateTime? get fechaLocal => DateTime.tryParse(fecha)?.toLocal();
}
