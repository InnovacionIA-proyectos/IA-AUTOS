/// Un viaje: la unidad transportadora, su placa y quién lo conduce e inspecciona.
///
/// Es el eje del trabajo de campo: cada etapa y cada informe cuelgan de un viaje, y de él salen la placa, el
/// tráiler, la unidad y el conductor que antes se escribían a mano en la captura.
class Viaje {
  const Viaje({
    required this.id,
    required this.codigoMtrack,
    required this.placa,
    required this.estado,
    this.unidad,
    this.trailer,
    this.conductorNombre,
    this.inspectorId,
    this.inspectorNombre,
    this.origen,
    this.destino,
    this.vehiculos = const [],
  });

  factory Viaje.fromJson(Map<String, dynamic> json) => Viaje(
        id: json['id'] as String,
        codigoMtrack: json['codigo_mtrack'] as String? ?? '',
        placa: json['placa'] as String? ?? '',
        estado: json['estado'] as String? ?? '',
        unidad: json['unidad'] as String?,
        trailer: json['trailer'] as String?,
        conductorNombre: json['conductor_nombre'] as String?,
        inspectorId: json['inspector_id'] as String?,
        inspectorNombre: json['inspector_nombre'] as String?,
        origen: json['origen'] as String?,
        destino: json['destino'] as String?,
        vehiculos: [
          for (final v in (json['vehiculos'] as List<dynamic>? ?? []))
            ViajeVehiculo.fromJson(v as Map<String, dynamic>),
        ],
      );

  final String id;
  final String codigoMtrack;
  final String placa;
  final String estado;

  /// Nombre del tipo de unidad transportadora (niñera, nodriza...), ya resuelto por la API.
  final String? unidad;
  final String? trailer;
  final String? conductorNombre;

  /// Quién lo inspecciona. Con él se sabe si quien mira puede marcar que una unidad no sale.
  final String? inspectorId;
  final String? inspectorNombre;

  /// Lugares de cargue y descargue, ya resueltos por la API.
  final String? origen;
  final String? destino;

  final List<ViajeVehiculo> vehiculos;

  /// Si todavía se puede trabajar en él. Un viaje terminado o cancelado conserva sus etapas y sus informes, pero
  /// el servidor no admite inspecciones nuevas: ofrecerlas sería mandar a alguien a capturar siete fotos para
  /// que el envío muera con un 409.
  bool get admiteTrabajo => estado == 'programado' || estado == 'en_curso';

  Map<String, dynamic> toJson() => {
        'id': id,
        'codigo_mtrack': codigoMtrack,
        'placa': placa,
        'estado': estado,
        'unidad': unidad,
        'trailer': trailer,
        'conductor_nombre': conductorNombre,
        'inspector_id': inspectorId,
        'inspector_nombre': inspectorNombre,
        'origen': origen,
        'destino': destino,
        'vehiculos': [for (final v in vehiculos) v.toJson()],
      };
}

/// Un VIN dentro de un viaje.
class ViajeVehiculo {
  const ViajeVehiculo({
    required this.vin,
    this.marca,
    this.modelo,
    this.color,
    this.operacion,
    this.estadoUnidad = estadoUnidadActiva,
    this.noSale,
    this.posicion,
    this.estadoTransporte = EstadoTransporte.sinRegistro,
    this.novedades = 0,
  });

  factory ViajeVehiculo.fromJson(Map<String, dynamic> json) => ViajeVehiculo(
        vin: json['vin'] as String,
        marca: json['marca'] as String?,
        modelo: json['modelo'] as String?,
        color: json['color'] as String?,
        operacion: json['operacion'] as String?,
        estadoUnidad: json['estado_unidad'] as String? ?? estadoUnidadActiva,
        posicion: json['posicion'] as int?,
        estadoTransporte: json['estado_transporte'] as String? ?? EstadoTransporte.sinRegistro,
        novedades: json['novedades'] as int? ?? 0,
        noSale: switch (json['no_sale']) {
          final Map<String, dynamic> m => NoSale.fromJson(m),
          _ => null,
        },
      );

  final String vin;
  final String? marca;
  final String? modelo;
  final String? color;
  final String? operacion;

  /// `activa` o `no_sale`. Una unidad que no sale conserva sus etapas y su vínculo con el viaje; lo que cambia
  /// es que deja de contar y no se captura más sobre ella.
  final String estadoUnidad;

  /// Por qué no sale y quién lo decidió. Null mientras la unidad esté activa.
  final NoSale? noSale;

  /// El lugar (1 a 7) que la unidad tiene en la niñera, o null mientras no se haya registrado.
  final int? posicion;

  /// Qué ha registrado el conductor de esta unidad en el transporte, y cuántas novedades.
  final String estadoTransporte;
  final int novedades;

  bool get sale => estadoUnidad == estadoUnidadActiva;

  Map<String, dynamic> toJson() => {
        'vin': vin,
        'marca': marca,
        'modelo': modelo,
        'color': color,
        'operacion': operacion,
        'estado_unidad': estadoUnidad,
        'no_sale': noSale?.toJson(),
        'posicion': posicion,
        'estado_transporte': estadoTransporte,
        'novedades': novedades,
      };
}

/// Los valores de `estado_transporte` de una unidad.
abstract final class EstadoTransporte {
  /// El conductor no ha registrado nada de esa unidad.
  static const sinRegistro = 'sin_registro';

  /// Tiene novedades y todavía no confirmó la llegada.
  static const conNovedad = 'con_novedad';

  /// Confirmó la llegada y no tiene novedades.
  static const sinNovedad = 'sin_novedad';

  /// Confirmó la llegada y tiene novedades.
  static const llegadaConNovedad = 'llegada_con_novedad';
}

/// Valor de `estado_unidad` de una unidad que sigue en el viaje.
const estadoUnidadActiva = 'activa';

/// Por qué una unidad dejó de salir en el viaje.
class NoSale {
  const NoSale({required this.motivo, this.hallazgoId, this.marcadoPorNombre, this.fecha, this.vinReemplazo});

  factory NoSale.fromJson(Map<String, dynamic> json) => NoSale(
        motivo: json['motivo'] as String? ?? '',
        hallazgoId: json['hallazgo_id'] as String?,
        marcadoPorNombre: json['marcado_por_nombre'] as String?,
        fecha: switch (json['fecha']) { final String f => DateTime.tryParse(f), _ => null },
        vinReemplazo: json['vin_reemplazo'] as String?,
      );

  final String motivo;
  final String? hallazgoId;
  final String? marcadoPorNombre;
  final DateTime? fecha;

  /// El VIN que entró al viaje en su lugar, si ya lo había cuando se marcó.
  final String? vinReemplazo;

  Map<String, dynamic> toJson() => {
        'motivo': motivo,
        'hallazgo_id': hallazgoId,
        'marcado_por_nombre': marcadoPorNombre,
        'fecha': fecha?.toIso8601String(),
        'vin_reemplazo': vinReemplazo,
      };
}
