class OrdenSofasa {
  const OrdenSofasa({
    required this.id,
    this.vin,
    this.estado,
    this.modelo,
    this.orden,
    this.origen,
    this.destino,
    this.direccion,
    this.fechaCargue,
    this.cita,
    this.muelle,
    this.peso,
  });

  factory OrdenSofasa.fromJson(Map<String, dynamic> json) => OrdenSofasa(
        id: json['id'] as String,
        vin: json['vin'] as String?,
        estado: json['status_code'] as String?,
        modelo: json['model_description'] as String?,
        orden: (json['orden_number'] as String?) ?? (json['orden_sap'] as String?),
        origen: json['origin_cedi'] as String?,
        destino: json['destination_city'] as String?,
        direccion: json['destination_address'] as String?,
        fechaCargue: _fecha(json['loading_date']),
        cita: _fecha(json['appointment']),
        muelle: json['loading_dock'] as String?,
        peso: (json['weight'] as num?)?.toDouble(),
      );

  final String id;
  final String? vin;
  final String? estado;
  final String? modelo;
  final String? orden;
  final String? origen;
  final String? destino;
  final String? direccion;
  final DateTime? fechaCargue;
  final DateTime? cita;
  final String? muelle;
  final double? peso;

  static DateTime? _fecha(Object? valor) => valor is String ? DateTime.tryParse(valor)?.toLocal() : null;
}
