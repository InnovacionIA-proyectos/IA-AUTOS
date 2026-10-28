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

  /// Lo que el dibujo estándar no trae. **El procesador todavía no los envía**: el servidor no tiene dónde
  /// recibirlos. Quedan guardados con el informe mientras está en la cola.
  final List<ExtraLocal> extras;

  // Progreso reanudable.
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
        extras: extras,
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
