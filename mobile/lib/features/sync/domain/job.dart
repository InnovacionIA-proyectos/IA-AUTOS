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
    this.fallido = false,
  });

  final String id;
  final String etiqueta;

  /// Milisegundos desde epoch.
  final int creadoEn;
  final int intentos;
  final String? error;

  /// Un job fallido deja de reintentarse por sí solo y queda visible hasta que se reintente a mano o se descarte.
  final bool fallido;

  String get tipo;

  Job conIntento({required int intentos, required String? error, required bool fallido});

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
    super.fallido,
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
        fallido: json['fallido'] as bool? ?? false,
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
        fallido: fallido,
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
  InformeJob conIntento({required int intentos, required String? error, required bool fallido}) => InformeJob(
        id: id,
        etiqueta: etiqueta,
        creadoEn: creadoEn,
        intentos: intentos,
        error: error,
        fallido: fallido,
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
        'fallido': fallido,
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
    super.fallido,
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
        fallido: json['fallido'] as bool? ?? false,
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
  FirmaJob conIntento({required int intentos, required String? error, required bool fallido}) => FirmaJob(
        id: id,
        etiqueta: etiqueta,
        creadoEn: creadoEn,
        intentos: intentos,
        error: error,
        fallido: fallido,
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
        'fallido': fallido,
        'inspeccion_id': inspeccionId,
        'entrega_id': entregaId,
        'recibe_id': recibeId,
        'firma_entrega': firmaEntrega,
        'firma_recibe': firmaRecibe,
      };
}
