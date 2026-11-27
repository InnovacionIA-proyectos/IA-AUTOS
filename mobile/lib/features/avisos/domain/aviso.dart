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
    this.hallazgoId,
    this.datos,
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
        hallazgoId: json['hallazgo_id'] as String?,
        // S8-HU-07.3 CA-9 y S8-HU-09.2 CA-6: lo propio de cada tipo. Cualquier otra forma se ignora: el aviso se muestra
        // igual, con su título y su mensaje.
        datos: switch (json['datos']) { final Map<String, dynamic> d => d, _ => null },
        leidoEn: switch (json['leida_at']) { final String f => DateTime.tryParse(f), _ => null },
        respuesta: switch (json['respuesta']) {
          final Map<String, dynamic> r => RespuestaAviso.fromJson(r),
          _ => null,
        },
      );

  final String id;

  /// Uno de [tiposDeAviso]. Uno que la app no conoce (el servidor sumó otro) se pinta igual, con su título y su
  /// mensaje, y se queda en la bandeja: no lleva a ninguna parte (S8-HU-09.2 CA-6).
  final String tipo;

  /// `alta` o `media`.
  final String urgencia;
  final String titulo;
  final String mensaje;
  final DateTime creadoEn;
  final String? viajeId;
  final String? codigoMtrack;
  final String? vin;

  /// El hallazgo del aviso (una novedad del conductor, un hallazgo crítico), para llevar a él.
  final String? hallazgoId;

  /// Los campos propios del aviso (`datos` del contrato), para no tener que leer el mensaje: el lugar, quién, la
  /// distancia, la precisión... Null en los avisos que no los llevan.
  final Map<String, dynamic>? datos;

  /// Null mientras nadie lo ha abierto.
  final DateTime? leidoEn;

  /// Lo que se contestó a una novedad del conductor. Null mientras nadie conteste.
  final RespuestaAviso? respuesta;

  bool get leido => leidoEn != null;
  bool get esAlta => urgencia == UrgenciaAviso.alta;

  /// Una novedad del conductor que todavía nadie contestó: la única que el coordinador puede responder.
  bool get sePuedeResponder => tipo == tipoNovedadConductor && respuesta == null;

  /// Cómo se nombra el tipo en la bandeja; null si la app no lo nombra (el título ya lo dice).
  String? get etiquetaTipo => switch (tipo) {
        tipoNovedadViaje => 'Novedad del viaje',
        _ => null,
      };

  /// Los VIN del aviso único «Cargue sin inspector» del viaje (S8-HU-07.3 CA-9), de `datos.vins`. Null en cualquier
  /// otro aviso, o si `datos` no tiene esa forma: entonces el aviso se ve solo con su título y su mensaje.
  List<String>? get vinsDelCargueSinInspector {
    final d = datos;
    if (tipo != tipoNovedadProceso || d == null || d['hecho'] != hechoCargueSinInspector) return null;
    final vins = d['vins'];
    if (vins is! List) return null;
    return [for (final vin in vins) if (vin is String && vin.isNotEmpty) vin];
  }

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
        hallazgoId: hallazgoId,
        datos: datos,
        leidoEn: leidoEn ?? this.leidoEn,
        respuesta: respuesta ?? this.respuesta,
      );
}

const tipoNovedadConductor = 'novedad_conductor';
const tipoFueraDeRango = 'fuera_de_rango';
const tipoUbicacionFijada = 'ubicacion_fijada';
const tipoPropuestaUbicacion = 'propuesta_ubicacion';

/// Todos los tipos que el servidor emite hoy (`NotificacionDto.tipo`). Un tipo fuera de esta lista es uno nuevo que
/// esta versión de la app no sabe a dónde llevar.
const tiposDeAviso = {
  'novedad_conductor',
  'respuesta_conductor',
  'hallazgo_critico',
  'umbral_patron',
  'captura_incompleta',
  'transferencia_pendiente',
  'reporte_diario',
  'novedad_proceso',
  'reclamo_sin_actividad',
  'fuera_de_rango',
  'cargue_sin_reclamar',
  'propuesta_ubicacion',
  'permiso_sin_inspector',
  'firma_cargue_pendiente',
  'cambio_cargue_inspector',
  'cambio_destino_unidad',
  'carro_rechazado_en_destino',
  'ubicacion_fijada',
  'espera_inspector_punto',
  'viaje_sin_movimiento',
  'novedad_viaje',
};

/// El aviso de la novedad del viaje (S8-HU-07.3): con la urgencia de su tipo.
const tipoNovedadViaje = 'novedad_viaje';

/// Lo que pasó en el proceso; el «Cargue sin inspector» es uno por viaje y trae los VIN en `datos.vins`.
const tipoNovedadProceso = 'novedad_proceso';
const hechoCargueSinInspector = 'cargue_sin_inspector';

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
