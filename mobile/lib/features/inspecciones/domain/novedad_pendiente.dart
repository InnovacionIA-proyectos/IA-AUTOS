/// Una novedad que registró el conductor y que todavía nadie ha clasificado.
///
/// El conductor marca el daño con su foto, pero no le pone zona, clase ni nivel: eso lo hace quien inspecciona,
/// al revisarla (DEC-003). Hasta entonces el hallazgo existe pero está incompleto.
library;

/// La foto de la novedad, con el sello de cuándo y dónde se tomó.
class FotoNovedad {
  const FotoNovedad({this.url, this.tomadaEn, this.lat, this.lng, this.sinUbicacion = false, this.venceEn});

  factory FotoNovedad.fromJson(Map<String, dynamic> json) => FotoNovedad(
        url: json['url'] as String?,
        tomadaEn: switch (json['sello_fecha']) { final String f => DateTime.tryParse(f), _ => null },
        lat: (json['sello_lat'] as num?)?.toDouble(),
        lng: (json['sello_lng'] as num?)?.toDouble(),
        sinUbicacion: json['sin_ubicacion'] as bool? ?? false,
        venceEn: switch (json['url_expira_at']) { final String f => DateTime.tryParse(f), _ => null },
      );

  final String? url;
  final DateTime? tomadaEn;
  final double? lat;
  final double? lng;

  /// La tomó sin GPS: el sello dice la hora pero no el lugar.
  final bool sinUbicacion;

  /// Hasta cuándo vale el enlace firmado de la foto.
  final DateTime? venceEn;

  bool get tieneUbicacion => !sinUbicacion && lat != null && lng != null;
}

class NovedadPendiente {
  const NovedadPendiente({
    required this.id,
    required this.vin,
    required this.proceso,
    required this.momento,
    required this.tamano,
    this.viajeId,
    this.tipoNovedad,
    this.clase,
    this.nota,
    this.foto,
    this.zona,
    this.cuadranteNumero,
    this.lado,
    this.extraId,
  });

  factory NovedadPendiente.fromJson(Map<String, dynamic> json) => NovedadPendiente(
        id: json['id'] as String,
        vin: json['vin'] as String? ?? '',
        proceso: json['proceso'] as String? ?? '',
        momento: json['momento'] as String? ?? '',
        // El conductor sí elige el tamaño; quien revisa lo corrige si hace falta.
        tamano: json['tamano_dano'] as String? ?? '',
        viajeId: json['viaje_id'] as String?,
        tipoNovedad: json['tipo_novedad'] as String?,
        clase: json['clase_dano'] as String?,
        nota: json['nota'] as String?,
        zona: json['zona'] as String?,
        cuadranteNumero: (json['cuadrante_numero'] as num?)?.toInt(),
        lado: json['lado'] as String?,
        extraId: json['extra_id'] as String?,
        foto: switch (json['evidencia']) {
          final Map<String, dynamic> e => FotoNovedad.fromJson(e),
          _ => null,
        },
      );

  final String id;
  final String vin;

  /// Dónde ocurrió: la etapa (cargue, transporte, descargue) y el momento.
  final String proceso;
  final String momento;

  final String tamano;
  final String? viajeId;

  /// Qué dijo el conductor que pasó (golpe en ruta, maniobra...). Es un catálogo distinto del de clases de daño.
  final String? tipoNovedad;

  /// Clase de daño; el conductor puede no haberla elegido.
  final String? clase;
  final String? nota;
  final FotoNovedad? foto;

  /// El texto de la zona («Izquierdo 7 · Puerta delantera») o, en un daño de extra, el nombre del extra.
  final String? zona;

  /// La zona que ya tiene el daño (el conductor lo marcó en el dibujo). Null en una novedad sin ubicar y en el
  /// daño de un extra.
  final int? cuadranteNumero;
  final String? lado;

  /// El extra del que es el daño: entonces no tiene zona y se califica sin ella (S8-HU-08.2 CA-4).
  final String? extraId;

  bool get esDeExtra => extraId != null;
  bool get tieneZona => cuadranteNumero != null;
}

/// Quién revisa las novedades del conductor: quien inspecciona y los administradores. El coordinador de calidad
/// también puede, pero lo hace desde su bandeja, que todavía no está en la app (S6-HU-02.11).
///
/// El conductor no revisa la suya: el nivel V lo pone otra persona, que es toda la razón de la revisión.
bool puedeRevisarNovedades(String? rol) => rol == 'inspector' || rol == 'admin' || rol == 'super_admin';
