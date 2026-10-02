/// Un punto marcado sobre el diagrama del vehículo, con coordenadas normalizadas (0..1).
class CoordsDiagrama {
  const CoordsDiagrama(this.x, this.y);

  factory CoordsDiagrama.fromJson(Map<String, dynamic> json) =>
      CoordsDiagrama((json['x'] as num).toDouble(), (json['y'] as num).toDouble());

  final double x;
  final double y;

  Map<String, dynamic> toJson() => {'x': x, 'y': y};

  @override
  bool operator ==(Object other) => other is CoordsDiagrama && other.x == x && other.y == y;

  @override
  int get hashCode => Object.hash(x, y);
}

/// Foto de evidencia guardada como archivo local. Lleva su PROPIO sello de fecha y ubicación (el de
/// cuando se tomó), no el del cierre del informe.
class FotoLocal {
  const FotoLocal({
    required this.ruta,
    required this.selloFecha,
    this.selloLat,
    this.selloLng,
    this.storagePath,
    this.enviada = false,
  });

  factory FotoLocal.fromJson(Map<String, dynamic> json) => FotoLocal(
        ruta: json['ruta'] as String,
        selloFecha: json['sello_fecha'] as String,
        selloLat: (json['sello_lat'] as num?)?.toDouble(),
        selloLng: (json['sello_lng'] as num?)?.toDouble(),
        storagePath: json['storage_path'] as String?,
        enviada: json['enviada'] as bool? ?? false,
      );

  final String ruta;
  final String selloFecha;
  final double? selloLat;
  final double? selloLng;

  /// Se asigna al subir la imagen.
  final String? storagePath;

  /// Se asigna al crear la evidencia en la API.
  final bool enviada;

  FotoLocal copyWith({String? storagePath, bool? enviada}) => FotoLocal(
        ruta: ruta,
        selloFecha: selloFecha,
        selloLat: selloLat,
        selloLng: selloLng,
        storagePath: storagePath ?? this.storagePath,
        enviada: enviada ?? this.enviada,
      );

  Map<String, dynamic> toJson() => {
        'ruta': ruta,
        'sello_fecha': selloFecha,
        'sello_lat': selloLat,
        'sello_lng': selloLng,
        'storage_path': storagePath,
        'enviada': enviada,
      };
}

/// Novedad registrada sobre una parte del vehículo.
class HallazgoLocal {
  const HallazgoLocal({required this.parte, required this.clase, required this.tamano, this.coords});

  factory HallazgoLocal.fromJson(Map<String, dynamic> json) => HallazgoLocal(
        parte: json['parte'] as String,
        clase: json['clase'] as String,
        tamano: json['tamano'] as String,
        coords: json['coords'] == null ? null : CoordsDiagrama.fromJson(json['coords'] as Map<String, dynamic>),
      );

  /// Etiqueta de parte y posición, por ejemplo "Guardafango delantero derecho".
  final String parte;
  final String clase;
  final String tamano;
  final CoordsDiagrama? coords;

  Map<String, dynamic> toJson() => {'parte': parte, 'clase': clase, 'tamano': tamano, 'coords': coords?.toJson()};
}

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
    this.conductorNombre,
    this.placa,
    this.trailer,
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
        conductorNombre: json['conductor_nombre'] as String?,
        placa: json['placa'] as String?,
        trailer: json['trailer'] as String?,
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
  final String? conductorNombre;
  final String? placa;
  final String? trailer;
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
        conductorNombre: conductorNombre,
        placa: placa,
        trailer: trailer,
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
        conductorNombre: conductorNombre,
        placa: placa,
        trailer: trailer,
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
        'conductor_nombre': conductorNombre,
        'placa': placa,
        'trailer': trailer,
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
