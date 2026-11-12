/// La entrega de un punto: las unidades del viaje que se entregan en un mismo lugar de destino.
///
/// Es lo que responde `GET /api/viajes/{id}/entregas`. En cada punto se firma una vez para todas.
class EntregaPunto {
  const EntregaPunto({
    required this.viajeId,
    required this.lugarId,
    required this.lugar,
    required this.unidades,
    required this.pendientes,
    required this.entregada,
    this.ciudad,
  });

  factory EntregaPunto.fromJson(Map<String, dynamic> json) {
    final unidades = [
      for (final u in json['unidades'] as List<dynamic>? ?? const []) UnidadEntrega.fromJson(u as Map<String, dynamic>),
    ];
    return EntregaPunto(
      viajeId: json['viaje_id'] as String,
      lugarId: json['lugar_id'] as String,
      lugar: json['lugar'] as String? ?? '',
      ciudad: json['ciudad'] as String?,
      unidades: unidades,
      pendientes: json['unidades_pendientes'] as int? ?? unidades.where((u) => !u.cerrada).length,
      entregada: json['entregada'] as bool? ?? false,
    );
  }

  final String viajeId;
  final String lugarId;
  final String lugar;
  final String? ciudad;
  final List<UnidadEntrega> unidades;

  /// «Aquí entregas estas N unidades».
  final int pendientes;
  final bool entregada;

  /// Las que se entregan en este punto y todavía no están cerradas: sobre ellas va la firma.
  List<UnidadEntrega> get porEntregar => [...unidades.where((u) => !u.cerrada)];

  /// Alguna todavía no tiene su inspección de llegada del Descargue, que es donde se firma: el servidor rechaza la
  /// firma del punto hasta que todas la tengan.
  List<UnidadEntrega> get sinInspeccion => [...porEntregar.where((u) => u.estado == EstadoEntrega.sinInspeccion)];
}

enum EstadoEntrega {
  /// Aún no tiene la inspección de llegada del Descargue.
  sinInspeccion,
  pendiente,
  cerrada;

  static EstadoEntrega desde(String? valor) => switch (valor) {
        'cerrada' => cerrada,
        'pendiente' => pendiente,
        _ => sinInspeccion,
      };
}

class UnidadEntrega {
  const UnidadEntrega({
    required this.vin,
    required this.estado,
    this.tipoCierre,
    this.entregaFirmada = false,
    this.recibeFirmada = false,
  });

  factory UnidadEntrega.fromJson(Map<String, dynamic> json) => UnidadEntrega(
        vin: json['vin'] as String,
        estado: EstadoEntrega.desde(json['estado'] as String?),
        tipoCierre: json['tipo_cierre'] as String?,
        entregaFirmada: json['entrega_firmada_at'] != null,
        recibeFirmada: json['recibe_firmada_at'] != null,
      );

  final String vin;
  final EstadoEntrega estado;
  final String? tipoCierre;
  final bool entregaFirmada;
  final bool recibeFirmada;

  bool get cerrada => estado == EstadoEntrega.cerrada;
}
