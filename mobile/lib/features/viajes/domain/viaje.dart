import '../../inspecciones/domain/acomodacion.dart';

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
    this.conductorId,
    this.inspectorId,
    this.inspectorNombre,
    this.origen,
    this.destino,
    this.vehiculos = const [],
    this.capacidad,
    this.disposicion = const [],
    this.fotosAcomodacion = 0,
    this.cargueTieneInspector = false,
    this.cargueAdmiteSinInspector = false,
    this.lugarCargueId,
    this.muelleId,
    this.muelle,
    this.cargueCerrado = false,
    this.papel,
  });

  factory Viaje.fromJson(Map<String, dynamic> json) => Viaje(
        id: json['id'] as String,
        codigoMtrack: json['codigo_mtrack'] as String? ?? '',
        placa: json['placa'] as String? ?? '',
        estado: json['estado'] as String? ?? '',
        unidad: json['unidad'] as String?,
        trailer: json['trailer'] as String?,
        conductorNombre: json['conductor_nombre'] as String?,
        conductorId: json['conductor_id'] as String?,
        inspectorId: json['inspector_id'] as String?,
        inspectorNombre: json['inspector_nombre'] as String?,
        origen: json['origen'] as String?,
        destino: json['destino'] as String?,
        vehiculos: [
          for (final v in (json['vehiculos'] as List<dynamic>? ?? []))
            ViajeVehiculo.fromJson(v as Map<String, dynamic>),
        ],
        capacidad: (json['capacidad'] as num?)?.toInt(),
        disposicion: [
          for (final p in ((json['disposicion'] as Map<String, dynamic>?)?['puestos'] as List<dynamic>? ?? []))
            PuestoNinera.fromJson(p as Map<String, dynamic>),
        ],
        fotosAcomodacion: (json['fotos_acomodacion'] as List<dynamic>?)?.length ?? 0,
        cargueTieneInspector: json['lugar_cargue_tiene_inspector'] as bool? ?? false,
        // Falso si no viene: un servidor anterior no lo manda y ningún lugar lo admitía (S8-HU-07.3 CA-6).
        cargueAdmiteSinInspector: json['lugar_cargue_admite_sin_inspector'] as bool? ?? false,
        lugarCargueId: json['lugar_cargue_id'] as String?,
        muelleId: json['muelle_id'] as String?,
        muelle: json['muelle'] as String?,
        // La copia guardada lo trae ya resuelto; del servidor sale de las dos maneras de cerrar el cargue de la
        // niñera: con su firma o con la constancia de no firma.
        cargueCerrado: json['cargue_cerrado'] as bool? ??
            (json['cargue_firmado_por_ninera_at'] != null || json['cargue_no_firmado_at'] != null),
        papel: json['papel'] as String?,
      );

  final String id;
  final String codigoMtrack;

  /// Cuántos puestos tiene su niñera. Null en los viajes de antes de que el viaje guardara su capacidad; para
  /// esos se usa el valor por defecto, que es 7.
  final int? capacidad;

  /// Los puestos tal como los manda el servidor. Vacía en un viaje antiguo: entonces se dibuja una lista
  /// numerada hasta la capacidad, no un esquema inventado aquí.
  final List<PuestoNinera> disposicion;

  /// Cuántas fotos de la acomodación tiene ya el viaje. Cuelgan del viaje, no de la unidad: la acomodación se
  /// fotografía una vez, y las demás unidades no la vuelven a pedir.
  final int fotosAcomodacion;
  final String placa;
  final String estado;

  /// Nombre del tipo de unidad transportadora (niñera, nodriza...), ya resuelto por la API.
  final String? unidad;
  final String? trailer;
  final String? conductorNombre;

  /// Quién lo conduce. Es quien recibe en la firma de la niñera.
  final String? conductorId;

  /// Quién lo inspecciona. Con él se sabe si quien mira puede marcar que una unidad no sale.
  final String? inspectorId;
  final String? inspectorNombre;

  /// Lugares de cargue y descargue, ya resueltos por la API.
  final String? origen;
  final String? destino;

  final List<ViajeVehiculo> vehiculos;

  /// El punto de cargue tiene inspectores asignados, y por eso la excepción «No hay inspector» allí se pide y no
  /// se activa en el momento (S7-HU-22.1). Falso también cuando el viaje no tiene lugar de cargue.
  final bool cargueTieneInspector;

  /// El lugar de cargue admite que el conductor haga el cargue cuando no hay inspector (C1). Solo entonces se le
  /// ofrecen «No hay inspector» y el camino al permiso; donde no lo admite el servidor rechaza la salida, la llegada
  /// y el permiso con 409 `lugar_no_admite_cargue_sin_inspector`, así que ofrecerlos sería mandarlo a un rechazo
  /// seguro (S8-HU-07.3 CA-6). Falso también cuando el viaje no tiene lugar de cargue.
  final bool cargueAdmiteSinInspector;

  /// El lugar real del cargue. De él salen los muelles que se ofrecen.
  final String? lugarCargueId;

  /// El muelle donde se carga, y su nombre. Null mientras nadie lo asigna, y en los viajes de antes.
  final String? muelleId;
  final String? muelle;

  /// La entrega del cargue de la niñera ya se cerró (firmada o con constancia): desde ahí el inspector ya no
  /// cambia el muelle ni el tráiler.
  final bool cargueCerrado;

  /// Qué hace en el viaje el inspector que consulta (S8-HU-10.1): [PapelInspector.carga], [PapelInspector.recibe] o
  /// [PapelInspector.cargaYRecibe]. Null para el conductor, para la oficina y en los viajes de antes.
  final String? papel;

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
        'conductor_id': conductorId,
        'inspector_id': inspectorId,
        'inspector_nombre': inspectorNombre,
        'origen': origen,
        'destino': destino,
        'vehiculos': [for (final v in vehiculos) v.toJson()],
        // Se guardan con la copia: sin ellos, sin señal una niñera de nueve se dibujaba de siete y las unidades
        // de los puestos 8 y 9 se veían sin puesto marcado y sin decir por qué.
        'capacidad': capacidad,
        // Sin él, sin señal el botón del cargue volvía a ofrecer la activación directa en un punto que sí tiene
        // inspectores, y el envío moría con un 403.
        'lugar_cargue_tiene_inspector': cargueTieneInspector,
        // Sin él, sin señal la ficha dejaría de ofrecer la excepción en un lugar que sí la admite.
        'lugar_cargue_admite_sin_inspector': cargueAdmiteSinInspector,
        // El muelle también sin señal: se ve en el viaje y decide el bloqueo del cierre de la acomodación.
        'lugar_cargue_id': lugarCargueId,
        'muelle_id': muelleId,
        'muelle': muelle,
        'cargue_cerrado': cargueCerrado,
        // S8-HU-10.2 CA-6: sin señal, la tarjeta sigue diciendo si carga o recibe.
        'papel': papel,
        'disposicion': {'puestos': [for (final p in disposicion) p.toJson()]},
        'fotos_acomodacion': [for (var i = 0; i < fotosAcomodacion; i++) const <String, dynamic>{}],
      };
}

/// Lo que hace en un viaje el inspector que lo consulta. Son los valores del campo `papel` del contrato.
abstract final class PapelInspector {
  /// Es el inspector del viaje o tiene el reclamo del cargue.
  static const carga = 'carga';

  /// Solo tomó la entrega de una o más paradas.
  static const recibe = 'recibe';

  /// El inspector del viaje que además tomó la entrega de una parada suya.
  static const cargaYRecibe = 'carga_y_recibe';
}

/// Cómo se dice el papel en la tarjeta del viaje. Null si no viene o si es un valor que la app no conoce: un código
/// crudo no se muestra (S8-HU-10.2 CA-9).
String? etiquetaPapel(String? papel) => switch (papel) {
      PapelInspector.carga => 'Tú cargas',
      PapelInspector.recibe => 'Tú recibes',
      PapelInspector.cargaYRecibe => 'Tú cargas y recibes',
      _ => null,
    };

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
    this.lugarDestino,
    this.lugarDestinoId,
    this.destinoCambiadoAt,
    this.puedeRegistrarRuta,
    this.motivoSinRuta,
  });

  factory ViajeVehiculo.fromJson(Map<String, dynamic> json) => ViajeVehiculo(
        // Null si no viene: un servidor anterior no lo manda y entonces los botones se comportan como antes.
        puedeRegistrarRuta: json['puede_registrar_ruta'] as bool?,
        motivoSinRuta: json['motivo_sin_ruta'] as String?,
        vin: json['vin'] as String,
        marca: json['marca'] as String?,
        modelo: json['modelo'] as String?,
        color: json['color'] as String?,
        operacion: json['operacion'] as String?,
        estadoUnidad: json['estado_unidad'] as String? ?? estadoUnidadActiva,
        posicion: json['posicion'] as int?,
        estadoTransporte: json['estado_transporte'] as String? ?? EstadoTransporte.sinRegistro,
        novedades: json['novedades'] as int? ?? 0,
        lugarDestino: json['lugar_destino'] as String?,
        lugarDestinoId: json['lugar_destino_id'] as String?,
        destinoCambiadoAt: switch (json['lugar_destino_cambiado_at']) { final String f => DateTime.tryParse(f), _ => null },
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

  /// El puesto que la unidad tiene en la niñera, o null mientras no se haya registrado.
  final int? posicion;

  /// Qué ha registrado el conductor de esta unidad en el transporte, y cuántas novedades.
  final String estadoTransporte;
  final int novedades;

  /// A dónde va este carro, ya resuelto por la API.
  final String? lugarDestino;

  /// La parada del carro: el lugar donde se entrega. Con él se juntan los carros de una parada para «Llegué a esta
  /// parada» y se abre su entrega (S8-HU-04.2). Null en los viajes de antes del destino por unidad.
  final String? lugarDestinoId;

  /// Cuándo se le cambió el destino por última vez; null si nunca.
  final DateTime? destinoCambiadoAt;

  /// Si el conductor ya puede registrar la ruta de este carro: «Reportar novedad» y «Llegué» (decisión 10). Lo
  /// calcula el servidor con **la misma regla** que aplica a esos dos envíos (la llegada del cargue cerrada y la
  /// entrega del cargue con un tipo de cierre), así que la app no la repite: solo pinta los botones con el dato, sin
  /// consultar a nadie (S8-HU-07.3 CA-5). Null si el servidor no lo manda: los botones se comportan como antes.
  final bool? puedeRegistrarRuta;

  /// Por qué todavía no puede (`entrega_del_cargue_sin_cerrar`, `unidad_no_sale`...); null si puede.
  final String? motivoSinRuta;

  /// El servidor dijo que todavía no: los botones de la ruta se ven grises con [textoSinRuta] debajo.
  bool get rutaBloqueada => puedeRegistrarRuta == false;

  bool get sale => estadoUnidad == estadoUnidadActiva;

  /// Se bajó del viaje (un cambio de pedido): ya no es parte de él y no se muestra en ningún lado del viaje.
  bool get bajado => estadoUnidad == estadoUnidadBajado;

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
        'lugar_destino': lugarDestino,
        'lugar_destino_id': lugarDestinoId,
        'lugar_destino_cambiado_at': destinoCambiadoAt?.toUtc().toIso8601String(),
        // S8-HU-07.3 CA-5: sin señal los botones siguen grises (o activos) como los dejó la última respuesta.
        'puede_registrar_ruta': puedeRegistrarRuta,
        'motivo_sin_ruta': motivoSinRuta,
      };
}

/// El motivo que el servidor manda cuando la entrega del cargue de un carro todavía no se cerró.
const motivoEntregaDelCargueSinCerrar = 'entrega_del_cargue_sin_cerrar';

/// Lo que se dice debajo de «Reportar novedad» y «Llegué» grises (S8-HU-07.3 CA-5). Un código que la app no conoce
/// no se muestra crudo: se dice lo general.
String textoSinRuta(String? motivo) => switch (motivo) {
      motivoEntregaDelCargueSinCerrar => 'Falta cerrar la entrega del cargue',
      _ => 'Todavía no puedes registrar la ruta de este carro',
    };

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

/// Valor de `estado_unidad` de una unidad que se bajó del viaje.
const estadoUnidadBajado = 'bajado';

/// Por qué una unidad dejó de salir en el viaje.
class NoSale {
  const NoSale({
    required this.motivo,
    this.motivoCodigo,
    this.motivoTexto,
    this.hallazgoId,
    this.marcadoPorNombre,
    this.fecha,
    this.vinReemplazo,
  });

  factory NoSale.fromJson(Map<String, dynamic> json) => NoSale(
        motivo: json['motivo'] as String? ?? '',
        motivoCodigo: json['motivo_codigo'] as String?,
        motivoTexto: json['motivo_texto'] as String?,
        hallazgoId: json['hallazgo_id'] as String?,
        marcadoPorNombre: json['marcado_por_nombre'] as String?,
        fecha: switch (json['fecha']) { final String f => DateTime.tryParse(f), _ => null },
        vinReemplazo: json['vin_reemplazo'] as String?,
      );

  /// La etiqueta del motivo, tal como la manda el servidor para quien pregunta: al conductor «Daño que impide
  /// viajar» en vez de «Daño V1+». Por eso se muestra esta y no la del catálogo.
  final String motivo;

  /// El código de `motivos-no-sale`. Null en lo que se guardó antes del catálogo.
  final String? motivoCodigo;

  /// El texto que se escribió: obligatorio con «otro», opcional en los demás.
  final String? motivoTexto;
  final String? hallazgoId;
  final String? marcadoPorNombre;
  final DateTime? fecha;

  /// El VIN que entró al viaje en su lugar, si ya lo había cuando se marcó.
  final String? vinReemplazo;

  /// Lo que se le dice a quien mira: con «otro», lo que se escribió; en los demás, la etiqueta del motivo. Un código
  /// que la app no conoce se muestra igual, con la etiqueta del servidor (CA-7).
  String get etiqueta {
    final texto = motivoTexto?.trim() ?? '';
    if (motivoCodigo == 'otro' && texto.isNotEmpty) return texto;
    if (motivo.isNotEmpty) return motivo;
    return texto.isNotEmpty ? texto : 'Sin motivo';
  }

  Map<String, dynamic> toJson() => {
        'motivo': motivo,
        'motivo_codigo': motivoCodigo,
        'motivo_texto': motivoTexto,
        'hallazgo_id': hallazgoId,
        'marcado_por_nombre': marcadoPorNombre,
        'fecha': fecha?.toIso8601String(),
        'vin_reemplazo': vinReemplazo,
      };
}
