import '../../../core/errores/error_api.dart';
import '../../inspecciones/domain/foto_local.dart';
import '../../inspecciones/domain/extra_local.dart';
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

  /// El servidor ya no lo admite porque el carro dejó de ser parte del trabajo: no sale o se bajó del viaje
  /// (S8-HU-02.2 CA-4 y CA-6). No es un error de nadie ni se arregla reintentando: queda a la vista con su
  /// explicación hasta que alguien lo quite, y no cuenta como «con error».
  bool get retirado => fallido && codigoError == codigoRetirado;

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
        'no_firma' => NoFirmaJob.fromJson(json),
        'nota' => NotaJob.fromJson(json),
        'entrega_punto' => EntregaPuntoJob.fromJson(json),
        'sin_inspector_parada' => SinInspectorParadaJob.fromJson(json),
        'recibo_sin_entrega' => ReciboSinEntregaJob.fromJson(json),
        'novedad' => NovedadJob.fromJson(json),
        'llegada' => LlegadaJob.fromJson(json),
        'novedad_viaje' => NovedadViajeJob.fromJson(json),
        'firma_ninera' => FirmaNineraJob.fromJson(json),
        'no_firma_ninera' => NoFirmaNineraJob.fromJson(json),
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
    this.extras = const [],
    this.viajeId,
    this.posiciones = const {},
    this.posicionesPrevias = const {},
    this.posicionesEnviadas = false,
    this.motivoSinInspector,
    this.motivoSinInspectorTexto,
    this.bajoPermiso = false,
    this.corrigeInspeccionId,
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
        // Los informes encolados antes de que existieran los extras no traen la clave.
        extras: [
          for (final e in json['extras'] as List<dynamic>? ?? const []) ExtraLocal.fromJson(e as Map<String, dynamic>),
        ],
        viajeId: json['viaje_id'] as String?,
        // Los informes encolados antes de la acomodación no traen estas claves.
        posiciones: _posiciones(json['posiciones']),
        posicionesPrevias: _posiciones(json['posiciones_previas']),
        posicionesEnviadas: json['posiciones_enviadas'] as bool? ?? false,
        motivoSinInspector: json['motivo_sin_inspector'] as String?,
        motivoSinInspectorTexto: json['motivo_sin_inspector_texto'] as String?,
        bajoPermiso: json['bajo_permiso'] as bool? ?? false,
        // Los informes encolados antes de que se pudiera corregir desde la app no traen la clave.
        corrigeInspeccionId: json['corrige_inspeccion_id'] as String?,
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

  /// Lo que el dibujo estándar no trae. Se envían después de los hallazgos y antes de cerrar, porque el cierre exige
  /// la foto de cada extra.
  final List<ExtraLocal> extras;

  /// Viaje de la etapa. Solo lo necesita la segunda inspección del cargue, para registrar las posiciones.
  final String? viajeId;

  /// La posición (1 a 7) de cada unidad del viaje en la niñera, por VIN. Solo la segunda inspección del cargue.
  final Map<String, int> posiciones;

  /// Lo que el viaje traía al abrir la captura. Con él se sabe qué unidades cambiaron de lugar.
  final Map<String, int> posicionesPrevias;

  /// Solo en la salida del cargue que hace el conductor cuando no hay inspector: el código del motivo del catálogo
  /// `motivos-sin-inspector` y, si el motivo lo pide, lo que escribió. La fecha, lat y lng del informe son los del
  /// momento en que eligió el motivo.
  final String? motivoSinInspector;
  final String? motivoSinInspectorTexto;

  /// Se capturó siguiendo un permiso «No hay inspector» que el conductor pidió (S8-HU-09.2 CA-2): concedido o con la
  /// cuenta atrás en cero. Solo entonces un 403 `conductor_sin_permiso_de_excepcion` es «todavía no se ve concedido»
  /// y se reintenta solo. Sin permiso pedido (un lugar sin inspectores, una revisión sin motivo) ese mismo 403 no lo
  /// arregla el tiempo: es un rechazo, con el mensaje del servidor. Vale para la salida y para la llegada del cargue.
  final bool bajoPermiso;

  /// La inspección **cerrada** que este informe viene a corregir, cuando lo es.
  ///
  /// Con ella puesta, el envío no crea una inspección nueva en la etapa: pide al servidor la versión siguiente de
  /// esa, que nace vacía y en borrador. Es lo que hace que una corrección abandonada no deje nada en el servidor,
  /// porque hasta aquí la corrección solo existió en el teléfono.
  final String? corrigeInspeccionId;

  bool get esCorreccion => corrigeInspeccionId != null;

  // Progreso reanudable.
  final bool posicionesEnviadas;
  final String? inspeccionId;
  final int hallazgosEnviados;
  final bool cerrada;

  @override
  String get tipo => 'informe';

  InformeJob conProgreso({
    String? inspeccionId,
    List<FotoLocal>? fotos,
    List<HallazgoLocal>? hallazgos,
    int? hallazgosEnviados,
    bool? posicionesEnviadas,
    List<ExtraLocal>? extras,
    bool? cerrada,
  }) =>
      InformeJob(
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
        hallazgos: hallazgos ?? this.hallazgos,
        extras: extras ?? this.extras,
        viajeId: viajeId,
        posiciones: posiciones,
        posicionesPrevias: posicionesPrevias,
        posicionesEnviadas: posicionesEnviadas ?? this.posicionesEnviadas,
        motivoSinInspector: motivoSinInspector,
        motivoSinInspectorTexto: motivoSinInspectorTexto,
        bajoPermiso: bajoPermiso,
        corrigeInspeccionId: corrigeInspeccionId,
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
        extras: extras,
        viajeId: viajeId,
        posiciones: posiciones,
        posicionesPrevias: posicionesPrevias,
        posicionesEnviadas: posicionesEnviadas,
        motivoSinInspector: motivoSinInspector,
        motivoSinInspectorTexto: motivoSinInspectorTexto,
        bajoPermiso: bajoPermiso,
        corrigeInspeccionId: corrigeInspeccionId,
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
        extras: extras,
        viajeId: viajeId,
        posiciones: posiciones,
        posicionesPrevias: posicionesPrevias,
        posicionesEnviadas: posicionesEnviadas,
        motivoSinInspector: motivoSinInspector,
        motivoSinInspectorTexto: motivoSinInspectorTexto,
        bajoPermiso: bajoPermiso,
        corrigeInspeccionId: corrigeInspeccionId,
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
        'extras': [for (final e in extras) e.toJson()],
        'viaje_id': viajeId,
        'posiciones': posiciones,
        'posiciones_previas': posicionesPrevias,
        'posiciones_enviadas': posicionesEnviadas,
        'motivo_sin_inspector': motivoSinInspector,
        'motivo_sin_inspector_texto': motivoSinInspectorTexto,
        'bajo_permiso': bajoPermiso,
        'corrige_inspeccion_id': corrigeInspeccionId,
        'inspeccion_id': inspeccionId,
        'hallazgos_enviados': hallazgosEnviados,
        'cerrada': cerrada,
      };

  static Map<String, int> _posiciones(Object? crudo) =>
      {for (final e in (crudo as Map<String, dynamic>? ?? const {}).entries) e.key: e.value as int};
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
    required this.firma,
    this.reservas,
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
        // Los encolados antes de que la API pidiera una sola firma traen las dos; se manda la que haya.
        firma: (json['firma'] ?? json['firma_entrega'] ?? json['firma_recibe']) as String?,
        reservas: json['reservas'] as String?,
      );

  final String inspeccionId;
  final String entregaId;
  final String recibeId;

  /// Trazo de la firma como path SVG (formato que la web sabe dibujar). Es la de quien firmó: el servidor la
  /// atribuye al usuario del token.
  final String? firma;

  /// Observaciones de quien recibe. Si van, esto es un **recibo con reservas** y sale por otro endpoint: la
  /// transferencia queda cerrada como `con_reservas` y no como `firmada`. Solo quien recibe puede.
  final String? reservas;

  bool get conReservas => reservas != null;

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
        firma: firma,
        reservas: reservas,
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
        firma: firma,
        reservas: reservas,
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
        'firma': firma,
        'reservas': reservas,
      };
}

/// Constancia de que una de las partes se negó a firmar. La registra **la otra**, con el motivo, un testigo
/// que es una tercera persona y una foto sellada: sin esas tres cosas no es una constancia, es un dicho.
///
/// Cierra la transferencia como `no_firma`, que es un cierre válido: la etapa siguiente puede arrancar.
class NoFirmaJob extends Job {
  const NoFirmaJob({
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
    required this.parte,
    required this.motivo,
    required this.testigoId,
    required this.foto,
  });

  factory NoFirmaJob.fromJson(Map<String, dynamic> json) => NoFirmaJob(
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
        parte: json['parte'] as String,
        motivo: json['motivo'] as String,
        testigoId: json['testigo_id'] as String,
        foto: FotoLocal.fromJson(json['foto'] as Map<String, dynamic>),
      );

  final String inspeccionId;
  final String entregaId;
  final String recibeId;

  /// Quién se negó: `entrega` o `recibe`.
  final String parte;
  final String motivo;

  /// Una tercera persona, ni quien entrega ni quien recibe: el servidor lo exige así.
  final String testigoId;

  /// La foto de la constancia, con su sello. Se sube antes de registrar, como la de un hallazgo.
  final FotoLocal foto;

  @override
  String get tipo => 'no_firma';

  NoFirmaJob conFoto(FotoLocal nueva) => _copiar(foto: nueva);

  @override
  NoFirmaJob conIntento({required int intentos, required String? error, String? codigo, required bool fallido}) =>
      _copiar(intentos: intentos, error: error, codigoError: codigo, fallido: fallido);

  @override
  NoFirmaJob conUsuario(String usuario) => _copiar(usuarioId: usuario);

  NoFirmaJob _copiar({
    int? intentos,
    String? error,
    String? codigoError,
    bool? fallido,
    String? usuarioId,
    FotoLocal? foto,
  }) =>
      NoFirmaJob(
        id: id,
        etiqueta: etiqueta,
        creadoEn: creadoEn,
        intentos: intentos ?? this.intentos,
        error: error,
        codigoError: codigoError ?? this.codigoError,
        fallido: fallido ?? this.fallido,
        usuarioId: usuarioId ?? this.usuarioId,
        inspeccionId: inspeccionId,
        entregaId: entregaId,
        recibeId: recibeId,
        parte: parte,
        motivo: motivo,
        testigoId: testigoId,
        foto: foto ?? this.foto,
      );

  @override
  Map<String, dynamic> toJson() => {
        'kind': 'no_firma',
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
        'parte': parte,
        'motivo': motivo,
        'testigo_id': testigoId,
        'foto': foto.toJson(),
      };
}

/// Las dos partes de una transferencia, tal como las nombra la API.
abstract final class ParteTransferencia {
  static const entrega = 'entrega';
  static const recibe = 'recibe';
}

/// Una nota aclaratoria sobre un registro ya cerrado.
///
/// Va por la cola como todo lo demás: se escribe en el patio, donde puede no haber señal, y el informe al que
/// aclara ya no cambia, así que no hay prisa por que llegue. El servidor solo se la acepta a quien registró el
/// informe, al administrador y al coordinador de calidad; a los demás les responde 403 y el job queda fallido
/// con su mensaje.
class NotaJob extends Job {
  const NotaJob({
    required super.id,
    required super.etiqueta,
    required super.creadoEn,
    super.intentos,
    super.error,
    super.codigoError,
    super.fallido,
    super.usuarioId,
    required this.entidad,
    required this.entidadId,
    required this.texto,
  });

  factory NotaJob.fromJson(Map<String, dynamic> json) => NotaJob(
        id: json['id'] as String,
        etiqueta: json['etiqueta'] as String,
        creadoEn: json['creado_en'] as int,
        intentos: json['intentos'] as int? ?? 0,
        error: json['error'] as String?,
        codigoError: json['codigo_error'] as String?,
        fallido: json['fallido'] as bool? ?? false,
        usuarioId: json['usuario_id'] as String?,
        entidad: json['entidad'] as String,
        entidadId: json['entidad_id'] as String,
        texto: json['texto'] as String,
      );

  /// A qué se le aclara: `inspeccion`, `hallazgo` o `transferencia`. Decide la ruta.
  final String entidad;
  final String entidadId;
  final String texto;

  @override
  String get tipo => 'nota';

  @override
  NotaJob conIntento({required int intentos, required String? error, String? codigo, required bool fallido}) =>
      _copiar(intentos: intentos, error: error, codigoError: codigo, fallido: fallido);

  @override
  NotaJob conUsuario(String usuario) => _copiar(usuarioId: usuario);

  NotaJob _copiar({int? intentos, String? error, String? codigoError, bool? fallido, String? usuarioId}) => NotaJob(
        id: id,
        etiqueta: etiqueta,
        creadoEn: creadoEn,
        intentos: intentos ?? this.intentos,
        error: error,
        codigoError: codigoError ?? this.codigoError,
        fallido: fallido ?? this.fallido,
        usuarioId: usuarioId ?? this.usuarioId,
        entidad: entidad,
        entidadId: entidadId,
        texto: texto,
      );

  @override
  Map<String, dynamic> toJson() => {
        'kind': 'nota',
        'id': id,
        'etiqueta': etiqueta,
        'creado_en': creadoEn,
        'intentos': intentos,
        'error': error,
        'codigo_error': codigoError,
        'fallido': fallido,
        'usuario_id': usuarioId,
        'entidad': entidad,
        'entidad_id': entidadId,
        'texto': texto,
      };
}

/// Un carro que no se entrega en la parada por un daño de su revisión de llegada (S8-HU-04.2 CA-4, C20): su VIN y el
/// daño con foto que lo motiva.
class RechazoCarro {
  const RechazoCarro({required this.vin, required this.hallazgoId});

  factory RechazoCarro.fromJson(Map<String, dynamic> json) =>
      RechazoCarro(vin: json['vin'] as String, hallazgoId: json['hallazgo_id'] as String);

  final String vin;
  final String hallazgoId;

  Map<String, dynamic> toJson() => {'vin': vin, 'hallazgo_id': hallazgoId};

  @override
  bool operator ==(Object other) => other is RechazoCarro && other.vin == vin && other.hallazgoId == hallazgoId;

  @override
  int get hashCode => Object.hash(vin, hallazgoId);
}

/// La firma de la entrega de una parada: una sola para todos los carros que se entregan allí, o el cierre con una
/// sola firma cuando la otra parte no está. Va por la cola para que se envíe sola al volver la señal.
///
/// Lleva todo lo que se decidió al firmar (S8-HU-04.2 CA-7): la hora y el GPS de ese momento, los carros
/// rechazados con su daño, las reservas, y en el cierre con una sola firma el motivo, las fotos y el testigo.
///
/// Repetirla no duplica nada: el servidor guarda la misma firma en cada unidad pendiente, y cuando ya no queda
/// ninguna responde que no hay pendientes, que para el procesador es haber terminado. Las fotos se suben una vez y
/// su ruta queda guardada en el job.
class EntregaPuntoJob extends Job {
  const EntregaPuntoJob({
    required super.id,
    required super.etiqueta,
    required super.creadoEn,
    super.intentos,
    super.error,
    super.codigoError,
    super.fallido,
    super.usuarioId,
    required this.viajeId,
    required this.lugarId,
    this.firma,
    this.fechaMomento,
    this.lat,
    this.lng,
    this.precisionM,
    this.reservas = const [],
    this.rechazos = const [],
    this.recibeNombre,
    this.recibeCedula,
    this.firmaRecibe,
    this.motivoCodigo,
    this.motivoTexto,
    this.fotos = const [],
    this.vines = const [],
    this.testigoNombre,
    this.testigoCedula,
  });

  factory EntregaPuntoJob.fromJson(Map<String, dynamic> json) {
    // Un cierre encolado por una versión anterior traía solo un motivo escrito. Se conserva como cierre con una sola
    // firma (con el código `otro`): el servidor lo rechaza diciendo qué motivos admite, y queda a la vista en la
    // cola en vez de salir convertido en una firma de las dos partes, que sería otra cosa.
    final motivoViejo = json['motivo'] as String?;
    return EntregaPuntoJob(
      id: json['id'] as String,
      etiqueta: json['etiqueta'] as String,
      creadoEn: json['creado_en'] as int,
      intentos: json['intentos'] as int? ?? 0,
      error: json['error'] as String?,
      codigoError: json['codigo_error'] as String?,
      fallido: json['fallido'] as bool? ?? false,
      usuarioId: json['usuario_id'] as String?,
      viajeId: json['viaje_id'] as String,
      lugarId: json['lugar_id'] as String,
      firma: json['firma'] as String?,
      fechaMomento: json['fecha_momento'] as String?,
      lat: (json['lat'] as num?)?.toDouble(),
      lng: (json['lng'] as num?)?.toDouble(),
      precisionM: (json['precision_m'] as num?)?.toDouble(),
      reservas: [for (final r in (json['reservas'] as List<dynamic>? ?? [])) ReservaCarro.fromJson(r as Map<String, dynamic>)],
      rechazos: [for (final r in (json['rechazos'] as List<dynamic>? ?? [])) RechazoCarro.fromJson(r as Map<String, dynamic>)],
      recibeNombre: json['recibe_nombre'] as String?,
      recibeCedula: json['recibe_cedula'] as String?,
      firmaRecibe: json['firma_recibe'] as String?,
      motivoCodigo: json['motivo_codigo'] as String? ?? (motivoViejo == null ? null : 'otro'),
      motivoTexto: json['motivo_texto'] as String? ?? motivoViejo,
      fotos: [for (final f in (json['fotos'] as List<dynamic>? ?? [])) FotoLocal.fromJson(f as Map<String, dynamic>)],
      vines: [for (final v in (json['vines'] as List<dynamic>? ?? [])) v as String],
      testigoNombre: json['testigo_nombre'] as String?,
      testigoCedula: json['testigo_cedula'] as String?,
    );
  }

  final String viajeId;
  final String lugarId;

  /// El trazo (path SVG) de quien firma. Obligatorio salvo que quien cierra con una sola firma ya hubiera firmado.
  final String? firma;

  /// Cuándo y dónde se firmó, tomados al firmar y no al enviar ([isoConZona]). Sin GPS van nulos y el servidor deja
  /// la firma «sin ubicación».
  final String? fechaMomento;
  final double? lat;
  final double? lng;
  final double? precisionM;

  /// Las observaciones de quien recibe, por carro.
  final List<ReservaCarro> reservas;

  /// Los carros que no se entregan, cada uno con el daño de su revisión de llegada que lo motiva.
  final List<RechazoCarro> rechazos;

  /// Quién recibió en un punto sin inspector: una persona que no es usuario del sistema. Los tres van juntos
  /// —el servidor los exige juntos— y entonces [firma] es la de quien entrega.
  final String? recibeNombre;
  final String? recibeCedula;

  /// El trazo de esa persona. Es la otra firma: con ella la entrega queda cerrada en el mismo envío.
  final String? firmaRecibe;

  /// Con motivo es el cierre con una sola firma (`se_nego_a_firmar` o `no_habia_nadie`); sin él, la firma de una
  /// de las partes.
  final String? motivoCodigo;

  /// Lo que se quiso agregar al motivo. Opcional.
  final String? motivoTexto;

  /// Las fotos del cierre con una sola firma, con su sello. Cada una va a cada carro de [vines].
  final List<FotoLocal> fotos;

  /// Los carros de la parada a los que van las fotos del cierre: el servidor guarda cada foto en la revisión de
  /// llegada del carro que se nombra.
  final List<String> vines;

  /// El testigo, que no es un usuario (por ejemplo, el vigilante). Los dos datos van juntos o no va ninguno.
  final String? testigoNombre;
  final String? testigoCedula;

  bool get unaSolaFirma => motivoCodigo != null;

  /// Recibe alguien de fuera: se mira el nombre porque los tres viajan juntos o no viaja ninguno.
  bool get recibeExterno => recibeNombre != null;

  /// Se firmó sin GPS.
  bool get sinUbicacion => lat == null || lng == null;

  @override
  String get tipo => 'entrega_punto';

  EntregaPuntoJob conFotos(List<FotoLocal> nuevas) => _copiar(fotos: nuevas);

  /// S8-HU-05.2 CA-6: el plan B llegó tarde y un inspector del punto ya tomó la entrega. La firma de quien entrega
  /// se conserva y se manda sola, para firmar con él: sin la persona del lugar, sin rechazos ni reservas (en una
  /// parada con inspector los decide él) y sin el motivo del cierre con una sola firma. Vuelve a la cola como nueva.
  EntregaPuntoJob soloMiFirma() => EntregaPuntoJob(
        id: id,
        etiqueta: etiqueta,
        creadoEn: creadoEn,
        usuarioId: usuarioId,
        viajeId: viajeId,
        lugarId: lugarId,
        firma: firma,
        fechaMomento: fechaMomento,
        lat: lat,
        lng: lng,
        precisionM: precisionM,
      );

  @override
  EntregaPuntoJob conIntento({required int intentos, required String? error, String? codigo, required bool fallido}) =>
      _copiar(intentos: intentos, error: error, codigoError: codigo, fallido: fallido);

  @override
  EntregaPuntoJob conUsuario(String usuario) => _copiar(usuarioId: usuario);

  EntregaPuntoJob _copiar({
    int? intentos,
    String? error,
    String? codigoError,
    bool? fallido,
    String? usuarioId,
    List<FotoLocal>? fotos,
  }) =>
      EntregaPuntoJob(
        id: id,
        etiqueta: etiqueta,
        creadoEn: creadoEn,
        intentos: intentos ?? this.intentos,
        error: error,
        codigoError: codigoError ?? this.codigoError,
        fallido: fallido ?? this.fallido,
        usuarioId: usuarioId ?? this.usuarioId,
        viajeId: viajeId,
        lugarId: lugarId,
        firma: firma,
        fechaMomento: fechaMomento,
        lat: lat,
        lng: lng,
        precisionM: precisionM,
        reservas: reservas,
        rechazos: rechazos,
        recibeNombre: recibeNombre,
        recibeCedula: recibeCedula,
        firmaRecibe: firmaRecibe,
        motivoCodigo: motivoCodigo,
        motivoTexto: motivoTexto,
        fotos: fotos ?? this.fotos,
        vines: vines,
        testigoNombre: testigoNombre,
        testigoCedula: testigoCedula,
      );

  @override
  Map<String, dynamic> toJson() => {
        'kind': 'entrega_punto',
        'id': id,
        'etiqueta': etiqueta,
        'creado_en': creadoEn,
        'intentos': intentos,
        'error': error,
        'codigo_error': codigoError,
        'fallido': fallido,
        'usuario_id': usuarioId,
        'viaje_id': viajeId,
        'lugar_id': lugarId,
        'firma': firma,
        'fecha_momento': fechaMomento,
        'lat': lat,
        'lng': lng,
        'precision_m': precisionM,
        'reservas': [for (final r in reservas) r.toJson()],
        'rechazos': [for (final r in rechazos) r.toJson()],
        'recibe_nombre': recibeNombre,
        'recibe_cedula': recibeCedula,
        'firma_recibe': firmaRecibe,
        'motivo_codigo': motivoCodigo,
        'motivo_texto': motivoTexto,
        'fotos': [for (final f in fotos) f.toJson()],
        'vines': vines,
        'testigo_nombre': testigoNombre,
        'testigo_cedula': testigoCedula,
      };
}

/// «No hay inspector» en una parada con inspectores donde nadie tomó la entrega (S8-HU-05.2 CA-2): el motivo y la
/// hora y el GPS del celular en el momento de tocarlo. Va por la cola: sin señal se guarda y se envía sola.
///
/// Lleva además los X e Y que trajo la parada, porque mientras espera señal el reloj se cuenta aquí, con la hora de
/// activación guardada, y no depende de que la pantalla siga abierta ni de que la app siga viva (CA-3). Reenviarla
/// no crea otra espera: con una abierta el servidor devuelve esa misma, con su reloj.
class SinInspectorParadaJob extends Job {
  const SinInspectorParadaJob({
    required super.id,
    required super.etiqueta,
    required super.creadoEn,
    super.intentos,
    super.error,
    super.codigoError,
    super.fallido,
    super.usuarioId,
    required this.viajeId,
    required this.lugarId,
    required this.motivo,
    required this.fechaMomento,
    required this.avisoMinutos,
    required this.planBMinutos,
    this.motivoTexto,
    this.lat,
    this.lng,
    this.precisionM,
  });

  factory SinInspectorParadaJob.fromJson(Map<String, dynamic> json) => SinInspectorParadaJob(
        id: json['id'] as String,
        etiqueta: json['etiqueta'] as String,
        creadoEn: json['creado_en'] as int,
        intentos: json['intentos'] as int? ?? 0,
        error: json['error'] as String?,
        codigoError: json['codigo_error'] as String?,
        fallido: json['fallido'] as bool? ?? false,
        usuarioId: json['usuario_id'] as String?,
        viajeId: json['viaje_id'] as String,
        lugarId: json['lugar_id'] as String,
        motivo: json['motivo_sin_inspector'] as String,
        motivoTexto: json['motivo_sin_inspector_texto'] as String?,
        fechaMomento: json['fecha_momento'] as String,
        lat: (json['lat'] as num?)?.toDouble(),
        lng: (json['lng'] as num?)?.toDouble(),
        precisionM: (json['precision_m'] as num?)?.toDouble(),
        avisoMinutos: json['aviso_minutos'] as int,
        planBMinutos: json['plan_b_minutos'] as int,
      );

  final String viajeId;
  final String lugarId;

  /// El código del motivo de `motivos-sin-inspector`, y lo que se escribió si el motivo lo pide.
  final String motivo;
  final String? motivoTexto;

  /// Cuándo se tocó «No hay inspector» ([isoConZona]): desde ahí cuentan los plazos, aunque llegue después.
  final String fechaMomento;

  /// El GPS del celular al tocarlo. Sin GPS se activa igual.
  final double? lat;
  final double? lng;
  final double? precisionM;

  /// X e Y de la parada al activar: el reloj que se ve mientras la activación espera señal.
  final int avisoMinutos;
  final int planBMinutos;

  @override
  String get tipo => 'sin_inspector_parada';

  @override
  SinInspectorParadaJob conIntento({required int intentos, required String? error, String? codigo, required bool fallido}) =>
      _copiar(intentos: intentos, error: error, codigoError: codigo, fallido: fallido);

  @override
  SinInspectorParadaJob conUsuario(String usuario) => _copiar(usuarioId: usuario);

  SinInspectorParadaJob _copiar({int? intentos, String? error, String? codigoError, bool? fallido, String? usuarioId}) =>
      SinInspectorParadaJob(
        id: id,
        etiqueta: etiqueta,
        creadoEn: creadoEn,
        intentos: intentos ?? this.intentos,
        error: error,
        codigoError: codigoError ?? this.codigoError,
        fallido: fallido ?? this.fallido,
        usuarioId: usuarioId ?? this.usuarioId,
        viajeId: viajeId,
        lugarId: lugarId,
        motivo: motivo,
        motivoTexto: motivoTexto,
        fechaMomento: fechaMomento,
        lat: lat,
        lng: lng,
        precisionM: precisionM,
        avisoMinutos: avisoMinutos,
        planBMinutos: planBMinutos,
      );

  @override
  Map<String, dynamic> toJson() => {
        'kind': 'sin_inspector_parada',
        'id': id,
        'etiqueta': etiqueta,
        'creado_en': creadoEn,
        'intentos': intentos,
        'error': error,
        'codigo_error': codigoError,
        'fallido': fallido,
        'usuario_id': usuarioId,
        'viaje_id': viajeId,
        'lugar_id': lugarId,
        'motivo_sin_inspector': motivo,
        'motivo_sin_inspector_texto': motivoTexto,
        'fecha_momento': fechaMomento,
        'lat': lat,
        'lng': lng,
        'precision_m': precisionM,
        'aviso_minutos': avisoMinutos,
        'plan_b_minutos': planBMinutos,
      };
}

/// La firma del recibo sin entrega: el conductor hizo la salida del cargue sin inspector, nadie de la empresa
/// entrega, y él cierra la transferencia solo con su firma. Va por la cola con su dueño.
class ReciboSinEntregaJob extends Job {
  const ReciboSinEntregaJob({
    required super.id,
    required super.etiqueta,
    required super.creadoEn,
    super.intentos,
    super.error,
    super.codigoError,
    super.fallido,
    super.usuarioId,
    required this.inspeccionId,
    required this.firma,
  });

  factory ReciboSinEntregaJob.fromJson(Map<String, dynamic> json) => ReciboSinEntregaJob(
        id: json['id'] as String,
        etiqueta: json['etiqueta'] as String,
        creadoEn: json['creado_en'] as int,
        intentos: json['intentos'] as int? ?? 0,
        error: json['error'] as String?,
        codigoError: json['codigo_error'] as String?,
        fallido: json['fallido'] as bool? ?? false,
        usuarioId: json['usuario_id'] as String?,
        inspeccionId: json['inspeccion_id'] as String,
        firma: json['firma'] as String,
      );

  final String inspeccionId;
  final String firma;

  @override
  String get tipo => 'recibo_sin_entrega';

  @override
  ReciboSinEntregaJob conIntento({required int intentos, required String? error, String? codigo, required bool fallido}) =>
      _copiar(intentos: intentos, error: error, codigoError: codigo, fallido: fallido);

  @override
  ReciboSinEntregaJob conUsuario(String usuario) => _copiar(usuarioId: usuario);

  ReciboSinEntregaJob _copiar({int? intentos, String? error, String? codigoError, bool? fallido, String? usuarioId}) =>
      ReciboSinEntregaJob(
        id: id,
        etiqueta: etiqueta,
        creadoEn: creadoEn,
        intentos: intentos ?? this.intentos,
        error: error,
        codigoError: codigoError ?? this.codigoError,
        fallido: fallido ?? this.fallido,
        usuarioId: usuarioId ?? this.usuarioId,
        inspeccionId: inspeccionId,
        firma: firma,
      );

  @override
  Map<String, dynamic> toJson() => {
        'kind': 'recibo_sin_entrega',
        'id': id,
        'etiqueta': etiqueta,
        'creado_en': creadoEn,
        'intentos': intentos,
        'error': error,
        'codigo_error': codigoError,
        'fallido': fallido,
        'usuario_id': usuarioId,
        'inspeccion_id': inspeccionId,
        'firma': firma,
      };
}

/// Una novedad del conductor en ruta: la foto, el tipo y el tamaño, con la fecha y el lugar del momento en que la
/// registró (no del momento en que se envía). Es reanudable: primero sube la foto y guarda su ruta, después crea la
/// novedad, así un corte entre los dos pasos no vuelve a subir el archivo.
class NovedadJob extends Job {
  const NovedadJob({
    required super.id,
    required super.etiqueta,
    required super.creadoEn,
    super.intentos,
    super.error,
    super.codigoError,
    super.fallido,
    super.usuarioId,
    required this.viajeId,
    required this.vin,
    required this.tipoNovedad,
    required this.tamanoDano,
    required this.fecha,
    required this.lat,
    required this.lng,
    required this.foto,
  });

  factory NovedadJob.fromJson(Map<String, dynamic> json) => NovedadJob(
        id: json['id'] as String,
        etiqueta: json['etiqueta'] as String,
        creadoEn: json['creado_en'] as int,
        intentos: json['intentos'] as int? ?? 0,
        error: json['error'] as String?,
        codigoError: json['codigo_error'] as String?,
        fallido: json['fallido'] as bool? ?? false,
        usuarioId: json['usuario_id'] as String?,
        viajeId: json['viaje_id'] as String,
        vin: json['vin'] as String,
        tipoNovedad: json['tipo_novedad'] as String,
        tamanoDano: json['tamano_dano'] as String,
        fecha: json['fecha'] as String,
        lat: (json['lat'] as num).toDouble(),
        lng: (json['lng'] as num).toDouble(),
        foto: FotoLocal.fromJson(json['foto'] as Map<String, dynamic>),
      );

  final String viajeId;
  final String vin;

  /// `golpe`, `piedra`, `granizo` o `incidente_vial`: se elige de una lista, no se escribe.
  final String tipoNovedad;

  /// Código del catálogo `tamanos-dano`.
  final String tamanoDano;

  /// Cuándo y dónde la registró: se toman al guardarla, no al enviarla.
  final String fecha;
  final double lat;
  final double lng;
  final FotoLocal foto;

  @override
  String get tipo => 'novedad';

  NovedadJob conFoto(FotoLocal nueva) => _copiar(foto: nueva);

  @override
  NovedadJob conIntento({required int intentos, required String? error, String? codigo, required bool fallido}) =>
      _copiar(intentos: intentos, error: error, codigoError: codigo, fallido: fallido);

  @override
  NovedadJob conUsuario(String usuario) => _copiar(usuarioId: usuario);

  NovedadJob _copiar({int? intentos, String? error, String? codigoError, bool? fallido, String? usuarioId, FotoLocal? foto}) =>
      NovedadJob(
        id: id,
        etiqueta: etiqueta,
        creadoEn: creadoEn,
        intentos: intentos ?? this.intentos,
        error: error,
        codigoError: codigoError ?? this.codigoError,
        fallido: fallido ?? this.fallido,
        usuarioId: usuarioId ?? this.usuarioId,
        viajeId: viajeId,
        vin: vin,
        tipoNovedad: tipoNovedad,
        tamanoDano: tamanoDano,
        fecha: fecha,
        lat: lat,
        lng: lng,
        foto: foto ?? this.foto,
      );

  @override
  Map<String, dynamic> toJson() => {
        'kind': 'novedad',
        'id': id,
        'etiqueta': etiqueta,
        'creado_en': creadoEn,
        'intentos': intentos,
        'error': error,
        'codigo_error': codigoError,
        'fallido': fallido,
        'usuario_id': usuarioId,
        'viaje_id': viajeId,
        'vin': vin,
        'tipo_novedad': tipoNovedad,
        'tamano_dano': tamanoDano,
        'fecha': fecha,
        'lat': lat,
        'lng': lng,
        'foto': foto.toJson(),
      };
}

/// «Llegué»: la confirmación de que la unidad llegó, con la fecha y el lugar del momento en que el conductor la
/// pulsó (no del envío). El servidor cierra con ella el transporte de esa unidad.
class LlegadaJob extends Job {
  const LlegadaJob({
    required super.id,
    required super.etiqueta,
    required super.creadoEn,
    super.intentos,
    super.error,
    super.codigoError,
    super.fallido,
    super.usuarioId,
    required this.viajeId,
    required this.vin,
    required this.fecha,
    required this.lat,
    required this.lng,
  });

  factory LlegadaJob.fromJson(Map<String, dynamic> json) => LlegadaJob(
        id: json['id'] as String,
        etiqueta: json['etiqueta'] as String,
        creadoEn: json['creado_en'] as int,
        intentos: json['intentos'] as int? ?? 0,
        error: json['error'] as String?,
        codigoError: json['codigo_error'] as String?,
        fallido: json['fallido'] as bool? ?? false,
        usuarioId: json['usuario_id'] as String?,
        viajeId: json['viaje_id'] as String,
        vin: json['vin'] as String,
        fecha: json['fecha'] as String,
        lat: (json['lat'] as num).toDouble(),
        lng: (json['lng'] as num).toDouble(),
      );

  final String viajeId;
  final String vin;
  final String fecha;
  final double lat;
  final double lng;

  @override
  String get tipo => 'llegada';

  @override
  LlegadaJob conIntento({required int intentos, required String? error, String? codigo, required bool fallido}) =>
      _copiar(intentos: intentos, error: error, codigoError: codigo, fallido: fallido);

  @override
  LlegadaJob conUsuario(String usuario) => _copiar(usuarioId: usuario);

  LlegadaJob _copiar({int? intentos, String? error, String? codigoError, bool? fallido, String? usuarioId}) => LlegadaJob(
        id: id,
        etiqueta: etiqueta,
        creadoEn: creadoEn,
        intentos: intentos ?? this.intentos,
        error: error,
        codigoError: codigoError ?? this.codigoError,
        fallido: fallido ?? this.fallido,
        usuarioId: usuarioId ?? this.usuarioId,
        viajeId: viajeId,
        vin: vin,
        fecha: fecha,
        lat: lat,
        lng: lng,
      );

  @override
  Map<String, dynamic> toJson() => {
        'kind': 'llegada',
        'id': id,
        'etiqueta': etiqueta,
        'creado_en': creadoEn,
        'intentos': intentos,
        'error': error,
        'codigo_error': codigoError,
        'fallido': fallido,
        'usuario_id': usuarioId,
        'viaje_id': viajeId,
        'vin': vin,
        'fecha': fecha,
        'lat': lat,
        'lng': lng,
      };
}

/// La novedad del **viaje** (S8-HU-07.3 CA-3): lo que el conductor o el inspector reportan sobre el viaje y no sobre
/// un carro (una llanta reventada, un bloqueo de vía, un carro que no llegó al patio). Va por la cola porque se
/// reporta en la vía, donde puede no haber señal: guarda el tipo, el texto, la foto si la hay, la hora y el GPS del
/// momento en que se guardó (no del envío) y, del inspector, el proceso de su reclamo y la parada de la entrega.
///
/// Su `id` viaja como `id_envio`: si el primer envío llegó y solo se perdió la respuesta, el reenvío recibe la misma
/// novedad en vez de crear otra y otro aviso. Es reanudable como la novedad de un carro: la foto se sube primero y
/// queda anotada, así un corte entre los dos pasos no la vuelve a subir.
class NovedadViajeJob extends Job {
  const NovedadViajeJob({
    required super.id,
    required super.etiqueta,
    required super.creadoEn,
    super.intentos,
    super.error,
    super.codigoError,
    super.fallido,
    super.usuarioId,
    required this.viajeId,
    required this.tipoNovedad,
    required this.texto,
    required this.fecha,
    this.lat,
    this.lng,
    this.foto,
    this.proceso,
    this.lugarId,
  });

  factory NovedadViajeJob.fromJson(Map<String, dynamic> json) => NovedadViajeJob(
        id: json['id'] as String,
        etiqueta: json['etiqueta'] as String,
        creadoEn: json['creado_en'] as int,
        intentos: json['intentos'] as int? ?? 0,
        error: json['error'] as String?,
        codigoError: json['codigo_error'] as String?,
        fallido: json['fallido'] as bool? ?? false,
        usuarioId: json['usuario_id'] as String?,
        viajeId: json['viaje_id'] as String,
        tipoNovedad: json['tipo_novedad'] as String,
        texto: json['texto'] as String,
        fecha: json['fecha'] as String,
        lat: (json['lat'] as num?)?.toDouble(),
        lng: (json['lng'] as num?)?.toDouble(),
        foto: switch (json['foto']) {
          final Map<String, dynamic> f => FotoLocal.fromJson(f),
          _ => null,
        },
        proceso: json['proceso'] as String?,
        lugarId: json['lugar_id'] as String?,
      );

  final String viajeId;

  /// Código del catálogo `tipos-novedad-viaje`.
  final String tipoNovedad;

  /// Lo que pasó, de 1 a 300 caracteres.
  final String texto;

  /// Cuándo y dónde se guardó. Sin GPS van nulos los dos y el servidor la acepta igual, marcada «sin ubicación»
  /// (C14): una novedad del viaje no se pierde por falta de señal del GPS.
  final String fecha;
  final double? lat;
  final double? lng;

  /// La foto sellada, opcional.
  final FotoLocal? foto;

  /// Solo el inspector: `cargue` o `entrega`, el proceso de su reclamo. El conductor no lo manda (400 si lo hace).
  final String? proceso;

  /// Solo el inspector en la entrega: la parada que tomó.
  final String? lugarId;

  bool get sinUbicacion => lat == null || lng == null;

  @override
  String get tipo => 'novedad_viaje';

  NovedadViajeJob conFoto(FotoLocal nueva) => _copiar(foto: nueva);

  @override
  NovedadViajeJob conIntento({required int intentos, required String? error, String? codigo, required bool fallido}) =>
      _copiar(intentos: intentos, error: error, codigoError: codigo, fallido: fallido);

  @override
  NovedadViajeJob conUsuario(String usuario) => _copiar(usuarioId: usuario);

  NovedadViajeJob _copiar({int? intentos, String? error, String? codigoError, bool? fallido, String? usuarioId, FotoLocal? foto}) =>
      NovedadViajeJob(
        id: id,
        etiqueta: etiqueta,
        creadoEn: creadoEn,
        intentos: intentos ?? this.intentos,
        error: error,
        codigoError: codigoError ?? this.codigoError,
        fallido: fallido ?? this.fallido,
        usuarioId: usuarioId ?? this.usuarioId,
        viajeId: viajeId,
        tipoNovedad: tipoNovedad,
        texto: texto,
        fecha: fecha,
        lat: lat,
        lng: lng,
        foto: foto ?? this.foto,
        proceso: proceso,
        lugarId: lugarId,
      );

  @override
  Map<String, dynamic> toJson() => {
        'kind': 'novedad_viaje',
        'id': id,
        'etiqueta': etiqueta,
        'creado_en': creadoEn,
        'intentos': intentos,
        'error': error,
        'codigo_error': codigoError,
        'fallido': fallido,
        'usuario_id': usuarioId,
        'viaje_id': viajeId,
        'tipo_novedad': tipoNovedad,
        'texto': texto,
        'fecha': fecha,
        'lat': lat,
        'lng': lng,
        'foto': foto?.toJson(),
        'proceso': proceso,
        'lugar_id': lugarId,
      };
}

/// Lo que quien recibe deja dicho de un carro al firmar la niñera: lo recibe, pero con esta observación.
class ReservaCarro {
  const ReservaCarro({required this.vin, required this.observaciones});

  factory ReservaCarro.fromJson(Map<String, dynamic> json) =>
      ReservaCarro(vin: json['vin'] as String, observaciones: json['observaciones'] as String);

  final String vin;
  final String observaciones;

  Map<String, dynamic> toJson() => {'vin': vin, 'observaciones': observaciones};

  @override
  bool operator ==(Object other) => other is ReservaCarro && other.vin == vin && other.observaciones == observaciones;

  @override
  int get hashCode => Object.hash(vin, observaciones);
}

/// La hora del momento con su zona, como la pide el contrato: «2026-11-18T14:09:00-05:00».
///
/// La firma hecha sin señal se envía después, y lo que vale es cuándo se hizo en el patio, no cuándo llegó.
///
/// [desfase] es el del teléfono; se pasa a mano solo para probar zonas distintas de la de quien corre las pruebas.
String isoConZona(DateTime momento, {Duration? desfase}) {
  final zona = desfase ?? momento.toLocal().timeZoneOffset;
  final local = desfase == null ? momento.toLocal() : momento.toUtc().add(desfase);
  String dos(int n) => n.abs().toString().padLeft(2, '0');
  final desfaseFinal = zona;
  final signo = desfaseFinal.isNegative ? '-' : '+';
  final minutos = desfaseFinal.inMinutes.abs();
  return '${local.year.toString().padLeft(4, '0')}-${dos(local.month)}-${dos(local.day)}'
      'T${dos(local.hour)}:${dos(local.minute)}:${dos(local.second)}'
      '$signo${dos(minutos ~/ 60)}:${dos(minutos % 60)}';
}

/// La firma del cargue de toda la niñera (S8-HU-01.3 CA-5): una por parte para todos los carros activos del viaje,
/// con la hora y el GPS del momento en que se firmó. Quien recibe puede dejar reservas en algunos carros (CA-13).
///
/// Va por la cola: se firma en el patio, donde puede no haber señal. Reenviarla no duplica nada: el servidor
/// reconoce la misma firma sobre carros ya cerrados por esa parte.
class FirmaNineraJob extends Job {
  const FirmaNineraJob({
    required super.id,
    required super.etiqueta,
    required super.creadoEn,
    super.intentos,
    super.error,
    super.codigoError,
    super.fallido,
    super.usuarioId,
    required this.viajeId,
    required this.parte,
    required this.firma,
    required this.fechaMomento,
    required this.lat,
    required this.lng,
    this.precisionM,
    this.reservas = const [],
  });

  factory FirmaNineraJob.fromJson(Map<String, dynamic> json) => FirmaNineraJob(
        id: json['id'] as String,
        etiqueta: json['etiqueta'] as String,
        creadoEn: json['creado_en'] as int,
        intentos: json['intentos'] as int? ?? 0,
        error: json['error'] as String?,
        codigoError: json['codigo_error'] as String?,
        fallido: json['fallido'] as bool? ?? false,
        usuarioId: json['usuario_id'] as String?,
        viajeId: json['viaje_id'] as String,
        parte: json['parte'] as String,
        firma: json['firma'] as String,
        fechaMomento: json['fecha_momento'] as String,
        lat: (json['lat'] as num).toDouble(),
        lng: (json['lng'] as num).toDouble(),
        precisionM: (json['precision_m'] as num?)?.toDouble(),
        reservas: [for (final r in (json['reservas'] as List<dynamic>? ?? [])) ReservaCarro.fromJson(r as Map<String, dynamic>)],
      );

  final String viajeId;

  /// `entrega` (el inspector del viaje) o `recibe` (su conductor).
  final String parte;
  final String firma;

  /// Cuándo se firmó, con su zona ([isoConZona]).
  final String fechaMomento;
  final double lat;
  final double lng;
  final double? precisionM;

  /// Solo de quien recibe.
  final List<ReservaCarro> reservas;

  @override
  String get tipo => 'firma_ninera';

  @override
  FirmaNineraJob conIntento({required int intentos, required String? error, String? codigo, required bool fallido}) =>
      _copiar(intentos: intentos, error: error, codigoError: codigo, fallido: fallido);

  @override
  FirmaNineraJob conUsuario(String usuario) => _copiar(usuarioId: usuario);

  FirmaNineraJob _copiar({int? intentos, String? error, String? codigoError, bool? fallido, String? usuarioId}) =>
      FirmaNineraJob(
        id: id,
        etiqueta: etiqueta,
        creadoEn: creadoEn,
        intentos: intentos ?? this.intentos,
        error: error,
        codigoError: codigoError ?? this.codigoError,
        fallido: fallido ?? this.fallido,
        usuarioId: usuarioId ?? this.usuarioId,
        viajeId: viajeId,
        parte: parte,
        firma: firma,
        fechaMomento: fechaMomento,
        lat: lat,
        lng: lng,
        precisionM: precisionM,
        reservas: reservas,
      );

  @override
  Map<String, dynamic> toJson() => {
        'kind': 'firma_ninera',
        'id': id,
        'etiqueta': etiqueta,
        'creado_en': creadoEn,
        'intentos': intentos,
        'error': error,
        'codigo_error': codigoError,
        'fallido': fallido,
        'usuario_id': usuarioId,
        'viaje_id': viajeId,
        'parte': parte,
        'firma': firma,
        'fecha_momento': fechaMomento,
        'lat': lat,
        'lng': lng,
        'precision_m': precisionM,
        'reservas': [for (final r in reservas) r.toJson()],
      };
}

/// La constancia de no firma de toda la niñera (S8-HU-01.3 CA-14): la deja la parte que sí está cuando la otra se
/// niega, con el motivo, la foto y un testigo, y cierra la entrega del cargue de todos los carros activos.
class NoFirmaNineraJob extends Job {
  const NoFirmaNineraJob({
    required super.id,
    required super.etiqueta,
    required super.creadoEn,
    super.intentos,
    super.error,
    super.codigoError,
    super.fallido,
    super.usuarioId,
    required this.viajeId,
    required this.parte,
    required this.motivo,
    required this.testigoId,
    required this.foto,
    required this.fechaMomento,
    required this.lat,
    required this.lng,
    this.precisionM,
  });

  factory NoFirmaNineraJob.fromJson(Map<String, dynamic> json) => NoFirmaNineraJob(
        id: json['id'] as String,
        etiqueta: json['etiqueta'] as String,
        creadoEn: json['creado_en'] as int,
        intentos: json['intentos'] as int? ?? 0,
        error: json['error'] as String?,
        codigoError: json['codigo_error'] as String?,
        fallido: json['fallido'] as bool? ?? false,
        usuarioId: json['usuario_id'] as String?,
        viajeId: json['viaje_id'] as String,
        parte: json['parte'] as String,
        motivo: json['motivo'] as String,
        testigoId: json['testigo_id'] as String,
        foto: FotoLocal.fromJson(json['foto'] as Map<String, dynamic>),
        fechaMomento: json['fecha_momento'] as String,
        lat: (json['lat'] as num).toDouble(),
        lng: (json['lng'] as num).toDouble(),
        precisionM: (json['precision_m'] as num?)?.toDouble(),
      );

  final String viajeId;

  /// La parte que **se negó**: la otra es quien deja la constancia.
  final String parte;
  final String motivo;

  /// Una tercera persona, ni el inspector ni el conductor del viaje.
  final String testigoId;

  /// La foto de la constancia, con su sello. Se sube antes de registrar; una sola sirve a todos los carros.
  final FotoLocal foto;
  final String fechaMomento;
  final double lat;
  final double lng;
  final double? precisionM;

  @override
  String get tipo => 'no_firma_ninera';

  NoFirmaNineraJob conFoto(FotoLocal nueva) => _copiar(foto: nueva);

  @override
  NoFirmaNineraJob conIntento({required int intentos, required String? error, String? codigo, required bool fallido}) =>
      _copiar(intentos: intentos, error: error, codigoError: codigo, fallido: fallido);

  @override
  NoFirmaNineraJob conUsuario(String usuario) => _copiar(usuarioId: usuario);

  NoFirmaNineraJob _copiar({
    int? intentos,
    String? error,
    String? codigoError,
    bool? fallido,
    String? usuarioId,
    FotoLocal? foto,
  }) =>
      NoFirmaNineraJob(
        id: id,
        etiqueta: etiqueta,
        creadoEn: creadoEn,
        intentos: intentos ?? this.intentos,
        error: error,
        codigoError: codigoError ?? this.codigoError,
        fallido: fallido ?? this.fallido,
        usuarioId: usuarioId ?? this.usuarioId,
        viajeId: viajeId,
        parte: parte,
        motivo: motivo,
        testigoId: testigoId,
        foto: foto ?? this.foto,
        fechaMomento: fechaMomento,
        lat: lat,
        lng: lng,
        precisionM: precisionM,
      );

  @override
  Map<String, dynamic> toJson() => {
        'kind': 'no_firma_ninera',
        'id': id,
        'etiqueta': etiqueta,
        'creado_en': creadoEn,
        'intentos': intentos,
        'error': error,
        'codigo_error': codigoError,
        'fallido': fallido,
        'usuario_id': usuarioId,
        'viaje_id': viajeId,
        'parte': parte,
        'motivo': motivo,
        'testigo_id': testigoId,
        'foto': foto.toJson(),
        'fecha_momento': fechaMomento,
        'lat': lat,
        'lng': lng,
        'precision_m': precisionM,
      };
}
