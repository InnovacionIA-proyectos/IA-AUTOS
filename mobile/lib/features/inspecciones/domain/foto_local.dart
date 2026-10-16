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
