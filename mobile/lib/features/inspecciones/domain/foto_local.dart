/// Foto de evidencia guardada como archivo local. Lleva su PROPIO sello de fecha y ubicación (el de
/// cuando se tomó), no el del cierre del informe.
class FotoLocal {
  const FotoLocal({
    required this.ruta,
    required this.selloFecha,
    this.selloLat,
    this.selloLng,
    this.vistaId,
    this.nota,
    this.precision,
    this.dispositivoId,
    this.hash,
    this.storagePath,
    this.enviada = false,
  });

  factory FotoLocal.fromJson(Map<String, dynamic> json) => FotoLocal(
        ruta: json['ruta'] as String,
        selloFecha: json['sello_fecha'] as String,
        selloLat: (json['sello_lat'] as num?)?.toDouble(),
        selloLng: (json['sello_lng'] as num?)?.toDouble(),
        vistaId: json['vista_id'] as String?,
        nota: json['nota'] as String?,
        precision: (json['precision'] as num?)?.toDouble(),
        dispositivoId: json['dispositivo_id'] as String?,
        hash: json['hash_sha256'] as String?,
        storagePath: json['storage_path'] as String?,
        enviada: json['enviada'] as bool? ?? false,
      );

  final String ruta;
  final String selloFecha;
  final double? selloLat;
  final double? selloLng;

  /// Qué vista del checklist cubre esta foto. La API la exige en la evidencia.
  final String? vistaId;

  /// Nota de la foto. Obligatoria en las vistas adicionales (baúl y cojinería): sin ella el servidor responde 400.
  final String? nota;

  /// Se tomó sin señal de GPS. No se le pone una ubicación anterior: queda marcada y hay que repetirla antes de
  /// cerrar, porque el sello es lo que da valor a la evidencia.
  bool get sinUbicacion => selloLat == null || selloLng == null;

  /// Radio de error del GPS en metros cuando se tomó.
  final double? precision;

  /// Desde qué equipo se tomó.
  final String? dispositivoId;

  /// Huella que devuelve el servidor al subir el archivo. Se reenvía con la evidencia: si no coincide, el
  /// archivo llegó dañado y el informe no debe darse por bueno.
  final String? hash;

  /// Se asigna al subir la imagen.
  final String? storagePath;

  /// Se asigna al crear la evidencia en la API.
  final bool enviada;

  FotoLocal copyWith({String? storagePath, String? hash, bool? enviada, String? nota}) => FotoLocal(
        ruta: ruta,
        selloFecha: selloFecha,
        selloLat: selloLat,
        selloLng: selloLng,
        vistaId: vistaId,
        nota: nota ?? this.nota,
        precision: precision,
        dispositivoId: dispositivoId,
        hash: hash ?? this.hash,
        storagePath: storagePath ?? this.storagePath,
        enviada: enviada ?? this.enviada,
      );

  Map<String, dynamic> toJson() => {
        'ruta': ruta,
        'sello_fecha': selloFecha,
        'sello_lat': selloLat,
        'sello_lng': selloLng,
        'vista_id': vistaId,
        'nota': nota,
        'precision': precision,
        'dispositivo_id': dispositivoId,
        'hash_sha256': hash,
        'storage_path': storagePath,
        'enviada': enviada,
      };
}
