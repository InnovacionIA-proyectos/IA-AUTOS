import '../../../core/errores/error_api.dart';
import '../../inspecciones/domain/foto_local.dart';
import '../../inspecciones/domain/hallazgo_local.dart';

/// Trabajo de la cola de sincronización. Se guarda en SQLite y es reanudable: cada paso completado se
/// persiste, de modo que un corte a mitad no duplica nada al reintentar.
sealed class Job {
  const Job({
    required this.id,
    required this.etiqueta,
    required this.creadoEn,
    this.intentos = 0,
    this.error,
    this.codigoError,
    this.fallido = false,
    this.usuarioId,
  });

  final String id;
  final String etiqueta;

  /// Milisegundos desde epoch.
  final int creadoEn;
  final int intentos;
  final String? error;

  /// Código del último error de la API (`FORBIDDEN`, `VALIDATION_ERROR`...). Se guarda además del mensaje porque
  /// la pantalla necesita decidir según el fallo, y comparar textos se rompe en cuanto el servidor los reescribe.
  final String? codigoError;

  /// Un job fallido deja de reintentarse por sí solo y queda visible hasta que se reintente a mano o se descarte.
  final bool fallido;

  /// Lo rechazó el servidor por permisos: lo empezó otra persona, o el rol no alcanza. No lo arregla nadie más
  /// que su dueño, y por eso esta sesión no puede ni reintentarlo ni descartarlo.
  bool get esProhibido => fallido && codigoError == codigoProhibido;

  /// Quién lo encoló. La cola sobrevive al cierre de sesión y el servidor atribuye el informe al dueño del token,
  /// así que sin esto, en un equipo compartido, lo de una persona salía a nombre de la siguiente.
  ///
  /// Es nulo en los jobs que encoló una versión anterior de la app: esos quedan huérfanos y no se envían solos.
  final String? usuarioId;

  bool get esHuerfano => usuarioId == null;

  bool esDe(String usuario) => usuarioId == usuario;

  /// Lo hace propio: al reintentar a mano un job huérfano, quien lo reintenta se hace cargo de él.
  Job conUsuario(String usuario);

  String get tipo;

  Job conIntento({required int intentos, required String? error, String? codigo, required bool fallido});

  Map<String, dynamic> toJson();

  static Job fromJson(Map<String, dynamic> json) => switch (json['kind']) {
        'informe' => InformeJob.fromJson(json),
        'firma' => FirmaJob.fromJson(json),
        final otro => throw FormatException('Tipo de job desconocido: $otro'),
      };
}

class InformeJob extends Job {
  const InformeJob({
    required super.id,
    required super.etiqueta,
    required super.creadoEn,
    super.intentos,
    super.error,
    super.codigoError,
    super.fallido,
    super.usuarioId,
    required this.etapaId,
    required this.momento,
    required this.fecha,
    required this.lat,
    required this.lng,
    this.notas,
    this.fotos = const [],
    this.hallazgos = const [],
    this.inspeccionId,
    this.hallazgosEnviados = 0,
    this.cerrada = false,
  });

  factory InformeJob.fromJson(Map<String, dynamic> json) => InformeJob(
        id: json['id'] as String,
        etiqueta: json['etiqueta'] as String,
        creadoEn: json['creado_en'] as int,
        intentos: json['intentos'] as int? ?? 0,
        error: json['error'] as String?,
        codigoError: json['codigo_error'] as String?,
        fallido: json['fallido'] as bool? ?? false,
        usuarioId: json['usuario_id'] as String?,
        etapaId: json['etapa_id'] as String,
        momento: json['momento'] as String,
        fecha: json['fecha'] as String,
        lat: (json['lat'] as num).toDouble(),
        lng: (json['lng'] as num).toDouble(),
        notas: json['notas'] as String?,
        fotos: [for (final f in json['fotos'] as List<dynamic>) FotoLocal.fromJson(f as Map<String, dynamic>)],
        hallazgos: [for (final h in json['hallazgos'] as List<dynamic>) HallazgoLocal.fromJson(h as Map<String, dynamic>)],
        inspeccionId: json['inspeccion_id'] as String?,
        hallazgosEnviados: json['hallazgos_enviados'] as int? ?? 0,
        cerrada: json['cerrada'] as bool? ?? false,
      );

  final String etapaId;
  final String momento;
  final String fecha;
  final double lat;
  final double lng;
  final String? notas;
  final List<FotoLocal> fotos;
  final List<HallazgoLocal> hallazgos;

  // Progreso reanudable.
  final String? inspeccionId;
  final int hallazgosEnviados;
  final bool cerrada;

  @override
  String get tipo => 'informe';

  InformeJob conProgreso({String? inspeccionId, List<FotoLocal>? fotos, int? hallazgosEnviados, bool? cerrada}) => InformeJob(
        id: id,
        etiqueta: etiqueta,
        creadoEn: creadoEn,
        intentos: intentos,
        error: error,
        codigoError: codigoError,
        fallido: fallido,
        usuarioId: usuarioId,
        etapaId: etapaId,
        momento: momento,
        fecha: fecha,
        lat: lat,
        lng: lng,
        notas: notas,
        fotos: fotos ?? this.fotos,
        hallazgos: hallazgos,
        inspeccionId: inspeccionId ?? this.inspeccionId,
        hallazgosEnviados: hallazgosEnviados ?? this.hallazgosEnviados,
        cerrada: cerrada ?? this.cerrada,
      );

  @override
  InformeJob conIntento({required int intentos, required String? error, String? codigo, required bool fallido}) => InformeJob(
        id: id,
        etiqueta: etiqueta,
        creadoEn: creadoEn,
        intentos: intentos,
        error: error,
        codigoError: codigo,
        fallido: fallido,
        usuarioId: usuarioId,
        etapaId: etapaId,
        momento: momento,
        fecha: fecha,
        lat: lat,
        lng: lng,
        notas: notas,
        fotos: fotos,
        hallazgos: hallazgos,
        inspeccionId: inspeccionId,
        hallazgosEnviados: hallazgosEnviados,
        cerrada: cerrada,
      );

  @override
  InformeJob conUsuario(String usuario) => InformeJob(
        id: id,
        etiqueta: etiqueta,
        creadoEn: creadoEn,
        intentos: intentos,
        error: error,
        codigoError: codigoError,
        fallido: fallido,
        usuarioId: usuario,
        etapaId: etapaId,
        momento: momento,
        fecha: fecha,
        lat: lat,
        lng: lng,
        notas: notas,
        fotos: fotos,
        hallazgos: hallazgos,
        inspeccionId: inspeccionId,
        hallazgosEnviados: hallazgosEnviados,
        cerrada: cerrada,
      );

  @override
  Map<String, dynamic> toJson() => {
        'kind': 'informe',
        'id': id,
        'etiqueta': etiqueta,
        'creado_en': creadoEn,
        'intentos': intentos,
        'error': error,
        'codigo_error': codigoError,
        'fallido': fallido,
        'usuario_id': usuarioId,
        'etapa_id': etapaId,
        'momento': momento,
        'fecha': fecha,
        'lat': lat,
        'lng': lng,
        'notas': notas,
        'fotos': [for (final f in fotos) f.toJson()],
        'hallazgos': [for (final h in hallazgos) h.toJson()],
        'inspeccion_id': inspeccionId,
        'hallazgos_enviados': hallazgosEnviados,
        'cerrada': cerrada,
      };
}

class FirmaJob extends Job {
  const FirmaJob({
    required super.id,
    required super.etiqueta,
    required super.creadoEn,
    super.intentos,
    super.error,
    super.codigoError,
    super.fallido,
    super.usuarioId,
    required this.inspeccionId,
    required this.entregaId,
    required this.recibeId,
    this.firmaEntrega,
    this.firmaRecibe,
  });

  factory FirmaJob.fromJson(Map<String, dynamic> json) => FirmaJob(
        id: json['id'] as String,
        etiqueta: json['etiqueta'] as String,
        creadoEn: json['creado_en'] as int,
        intentos: json['intentos'] as int? ?? 0,
        error: json['error'] as String?,
        codigoError: json['codigo_error'] as String?,
        fallido: json['fallido'] as bool? ?? false,
        usuarioId: json['usuario_id'] as String?,
        inspeccionId: json['inspeccion_id'] as String,
        entregaId: json['entrega_id'] as String,
        recibeId: json['recibe_id'] as String,
        firmaEntrega: json['firma_entrega'] as String?,
        firmaRecibe: json['firma_recibe'] as String?,
      );

  final String inspeccionId;
  final String entregaId;
  final String recibeId;

  /// Trazo de la firma como path SVG (formato que la web sabe dibujar).
  final String? firmaEntrega;
  final String? firmaRecibe;

  @override
  String get tipo => 'firma';

  @override
  FirmaJob conIntento({required int intentos, required String? error, String? codigo, required bool fallido}) => FirmaJob(
        id: id,
        etiqueta: etiqueta,
        creadoEn: creadoEn,
        intentos: intentos,
        error: error,
        codigoError: codigo,
        fallido: fallido,
        usuarioId: usuarioId,
        inspeccionId: inspeccionId,
        entregaId: entregaId,
        recibeId: recibeId,
        firmaEntrega: firmaEntrega,
        firmaRecibe: firmaRecibe,
      );

  @override
  FirmaJob conUsuario(String usuario) => FirmaJob(
        id: id,
        etiqueta: etiqueta,
        creadoEn: creadoEn,
        intentos: intentos,
        error: error,
        codigoError: codigoError,
        fallido: fallido,
        usuarioId: usuario,
        inspeccionId: inspeccionId,
        entregaId: entregaId,
        recibeId: recibeId,
        firmaEntrega: firmaEntrega,
        firmaRecibe: firmaRecibe,
      );

  @override
  Map<String, dynamic> toJson() => {
        'kind': 'firma',
        'id': id,
        'etiqueta': etiqueta,
        'creado_en': creadoEn,
        'intentos': intentos,
        'error': error,
        'codigo_error': codigoError,
        'fallido': fallido,
        'usuario_id': usuarioId,
        'inspeccion_id': inspeccionId,
        'entrega_id': entregaId,
        'recibe_id': recibeId,
        'firma_entrega': firmaEntrega,
        'firma_recibe': firmaRecibe,
      };
}
