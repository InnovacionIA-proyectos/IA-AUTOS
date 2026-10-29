/// Un aviso de la bandeja: lo que el sistema le dice a una persona sin que tenga que revisar listados (una novedad del
/// conductor, la respuesta del coordinador, una captura incompleta...). Es lo que lista `GET /api/notificaciones`.
class Aviso {
  const Aviso({
    required this.id,
    required this.tipo,
    required this.urgencia,
    required this.titulo,
    required this.mensaje,
    required this.creadoEn,
    this.viajeId,
    this.codigoMtrack,
    this.vin,
    this.leidoEn,
    this.respuesta,
  });

  factory Aviso.fromJson(Map<String, dynamic> json) => Aviso(
        id: json['id'] as String,
        tipo: json['tipo'] as String? ?? '',
        urgencia: json['urgencia'] as String? ?? UrgenciaAviso.media,
        titulo: json['titulo'] as String? ?? '',
        mensaje: json['mensaje'] as String? ?? '',
        creadoEn: DateTime.tryParse(json['created_at'] as String? ?? '') ?? DateTime.fromMillisecondsSinceEpoch(0),
        viajeId: json['viaje_id'] as String?,
        codigoMtrack: json['codigo_mtrack'] as String?,
        vin: json['vin'] as String?,
        leidoEn: switch (json['leida_at']) { final String f => DateTime.tryParse(f), _ => null },
        respuesta: switch (json['respuesta']) {
          final Map<String, dynamic> r => RespuestaAviso.fromJson(r),
          _ => null,
        },
      );

  final String id;

  /// `novedad_conductor`, `respuesta_conductor`, `hallazgo_critico`, `umbral_patron`, `captura_incompleta`,
  /// `transferencia_pendiente`, `novedad_proceso`, `reclamo_sin_actividad` o `reporte_diario`.
  final String tipo;

  /// `alta` o `media`.
  final String urgencia;
  final String titulo;
  final String mensaje;
  final DateTime creadoEn;
  final String? viajeId;
  final String? codigoMtrack;
  final String? vin;

  /// Null mientras nadie lo ha abierto.
  final DateTime? leidoEn;

  /// Lo que se contestó a una novedad del conductor. Null mientras nadie conteste.
  final RespuestaAviso? respuesta;

  bool get leido => leidoEn != null;
  bool get esAlta => urgencia == UrgenciaAviso.alta;

  /// Una novedad del conductor que todavía nadie contestó: la única que el coordinador puede responder.
  bool get sePuedeResponder => tipo == tipoNovedadConductor && respuesta == null;

  Aviso copiar({DateTime? leidoEn, RespuestaAviso? respuesta}) => Aviso(
        id: id,
        tipo: tipo,
        urgencia: urgencia,
        titulo: titulo,
        mensaje: mensaje,
        creadoEn: creadoEn,
        viajeId: viajeId,
        codigoMtrack: codigoMtrack,
        vin: vin,
        leidoEn: leidoEn ?? this.leidoEn,
        respuesta: respuesta ?? this.respuesta,
      );
}

const tipoNovedadConductor = 'novedad_conductor';

abstract final class UrgenciaAviso {
  static const alta = 'alta';
  static const media = 'media';
}

/// La respuesta del coordinador a una novedad: el texto y por qué medio se dio.
class RespuestaAviso {
  const RespuestaAviso({required this.texto, required this.medio, this.usuarioNombre, this.fecha});

  factory RespuestaAviso.fromJson(Map<String, dynamic> json) => RespuestaAviso(
        texto: json['texto'] as String? ?? '',
        medio: json['medio'] as String? ?? MedioRespuesta.app,
        usuarioNombre: json['usuario_nombre'] as String?,
        fecha: switch (json['fecha']) { final String f => DateTime.tryParse(f), _ => null },
      );

  final String texto;
  final String medio;
  final String? usuarioNombre;
  final DateTime? fecha;
}

/// Por dónde se contestó: una respuesta escrita en la app, o una llamada.
abstract final class MedioRespuesta {
  static const app = 'app';
  static const llamada = 'llamada';
}

/// Una página de la bandeja, con cuántos avisos hay sin leer en total.
class PaginaAvisos {
  const PaginaAvisos({required this.avisos, required this.total, required this.noLeidos});

  factory PaginaAvisos.fromJson(Map<String, dynamic> json) => PaginaAvisos(
        avisos: [for (final a in json['items'] as List<dynamic>? ?? const []) Aviso.fromJson(a as Map<String, dynamic>)],
        total: json['total'] as int? ?? 0,
        noLeidos: json['no_leidas'] as int? ?? 0,
      );

  static const vacia = PaginaAvisos(avisos: [], total: 0, noLeidos: 0);

  final List<Aviso> avisos;
  final int total;
  final int noLeidos;
}
