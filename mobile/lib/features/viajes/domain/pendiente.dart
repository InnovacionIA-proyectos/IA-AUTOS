/// Un camión pendiente en uno de los puntos del inspector: un cargue programado para hoy o una entrega por revisar.
///
/// Es lo que lista `GET /api/viajes/pendientes`. Sobre él se reclama («revisar este») y se libera.
class Pendiente {
  const Pendiente({
    required this.viajeId,
    required this.proceso,
    required this.lugar,
    required this.estado,
    required this.unidades,
    this.codigoMtrack,
    this.placa,
    this.fechaProgramada,
    this.reclamadoPorNombre,
  });

  factory Pendiente.fromJson(Map<String, dynamic> json) => Pendiente(
        viajeId: json['viaje_id'] as String,
        proceso: json['proceso'] as String? ?? procesoCargue,
        lugar: json['lugar'] as String? ?? '',
        estado: EstadoPendiente.desde(json['estado'] as String?),
        unidades: json['unidades'] as int? ?? 0,
        codigoMtrack: json['codigo_mtrack'] as String?,
        placa: json['placa'] as String?,
        fechaProgramada: switch (json['fecha_programada']) { final String f => DateTime.tryParse(f), _ => null },
        reclamadoPorNombre: json['reclamado_por_nombre'] as String?,
      );

  final String viajeId;

  /// `cargue` o `entrega`. El mismo camión puede salir en las dos.
  final String proceso;
  final String lugar;
  final EstadoPendiente estado;
  final int unidades;
  final String? codigoMtrack;
  final String? placa;
  final DateTime? fechaProgramada;

  /// Quién lo tiene, cuando [estado] es `enRevision`.
  final String? reclamadoPorNombre;

  bool get esEntrega => proceso == procesoEntrega;
}

const procesoCargue = 'cargue';
const procesoEntrega = 'entrega';

enum EstadoPendiente {
  /// Nadie lo reclamó.
  disponible,

  /// Lo tiene otro inspector.
  enRevision,

  /// Lo reclamó quien mira.
  mio;

  /// Un estado que la app no conoce se trata como disponible: el servidor decide al reclamar, y un 409 explica.
  static EstadoPendiente desde(String? valor) => switch (valor) {
        'en_revision' => enRevision,
        'mio' => mio,
        _ => disponible,
      };
}
