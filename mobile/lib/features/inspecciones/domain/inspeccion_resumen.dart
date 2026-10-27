/// Fila del listado "Mis inspecciones" (GET /api/inspecciones).
class InspeccionResumen {
  const InspeccionResumen({
    required this.id,
    this.vin,
    required this.momento,
    required this.momentoNombre,
    required this.fecha,
    required this.estado,
  });

  factory InspeccionResumen.fromJson(Map<String, dynamic> json) => InspeccionResumen(
        id: json['id'] as String,
        vin: json['vin'] as String?,
        momento: json['momento'] as String,
        momentoNombre: json['momento_nombre'] as String? ?? json['momento'] as String,
        fecha: json['fecha'] as String,
        estado: json['estado'] as String,
      );

  final String id;
  final String? vin;
  final String momento;

  /// Texto listo para mostrar que manda la API («Inspección de salida»). No es un catalogo administrable.
  final String momentoNombre;

  final String fecha;
  final String estado;

  Map<String, dynamic> toJson() =>
      {'id': id, 'vin': vin, 'momento': momento, 'momento_nombre': momentoNombre, 'fecha': fecha, 'estado': estado};

  DateTime? get fechaLocal => DateTime.tryParse(fecha)?.toLocal();
}
