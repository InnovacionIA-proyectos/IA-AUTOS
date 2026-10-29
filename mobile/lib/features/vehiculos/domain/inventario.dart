import '../../inspecciones/domain/nota_aclaratoria.dart';

/// Un hallazgo tal como lo devuelve la API (`HallazgoDto`).
///
/// Con el modelo de zonas, `parte_vehiculo` y `coords_diagrama` llegan nulos en todo hallazgo nuevo y la clase llega
/// nula en una novedad del conductor sin revisar: aquí nada de eso se lee como obligatorio, o un solo hallazgo así
/// tumbaría la página entera del VIN.
class HallazgoVista {
  const HallazgoVista({
    required this.id,
    required this.zona,
    this.cuadranteNumero,
    this.lado,
    this.clase,
    required this.tamano,
    this.nivel,
    this.esPreexistente = false,
    this.origen,
    this.revisadoEn,
  });

  factory HallazgoVista.fromJson(Map<String, dynamic> json) => HallazgoVista(
        id: json['id'] as String,
        // `zona` es el texto que arma el servidor («Izquierdo 7 · Puerta delantera»); en un hallazgo histórico sin
        // ubicar es la parte que se escribió entonces.
        zona: json['zona'] as String? ?? json['parte_vehiculo'] as String? ?? 'Sin zona',
        cuadranteNumero: (json['cuadrante_numero'] as num?)?.toInt(),
        lado: json['lado'] as String?,
        clase: json['clase_dano'] as String?,
        tamano: json['tamano_dano'] as String? ?? '',
        nivel: json['nivel_severidad'] as String?,
        esPreexistente: json['es_preexistente'] as bool? ?? false,
        origen: json['origen'] as String?,
        revisadoEn: switch (json['revisado_at']) { final String f => DateTime.tryParse(f), _ => null },
      );

  final String id;
  final String zona;

  /// Número y lado de la zona; nulos en una zona provisional, en un hallazgo histórico sin ubicar y en la novedad
  /// del conductor sin revisar. Con ellos se marca la zona en el dibujo.
  final int? cuadranteNumero;
  final String? lado;

  final String? clase;
  final String tamano;
  final String? nivel;

  /// El daño ya venía de antes y así lo confirmó quien inspeccionó: no ocurrió en esta etapa.
  final bool esPreexistente;

  /// Quién lo registró: `inspector` o `conductor`. Una novedad del conductor ya revisada se ve como cualquier
  /// otro hallazgo, pero sigue diciendo de dónde salió.
  final String? origen;

  bool get loRegistroElConductor => origen == 'conductor';

  /// Cuándo lo revisó un inspector. Null si nadie lo ha hecho **o** si a quien pregunta no se le muestra: al
  /// conductor el servidor le oculta la revisión entera.
  final DateTime? revisadoEn;

  /// Qué decir del nivel cuando no hay nivel que decir. Revisado sin nivel a la vista es el caso del conductor:
  /// alguien ya lo clasificó, pero a él no se le muestra con qué.
  bool get yaLoRevisaron => revisadoEn != null;
}

class EvidenciaVista {
  const EvidenciaVista({required this.id, this.url, this.hallazgoId, this.venceEn});

  factory EvidenciaVista.fromJson(Map<String, dynamic> json) =>
      EvidenciaVista(
        id: json['id'] as String,
        url: json['url'] as String?,
        hallazgoId: json['hallazgo_id'] as String?,
        venceEn: switch (json['url_expira_at']) { final String f => DateTime.tryParse(f), _ => null },
      );

  final String id;

  /// Ruta relativa o absoluta de la imagen; null si no es servible.
  final String? url;

  /// El daño que retrata. Null en las fotos del checklist de vistas, que no son de ningún daño.
  final String? hallazgoId;

  /// Hasta cuándo vale el enlace firmado. Pasada esa hora el servidor responde 403 y hay que pedir uno nuevo.
  final DateTime? venceEn;
}

/// Los dos momentos de una etapa. Son valores fijos del contrato: la API solo acepta estos dos.
abstract final class Momento {
  static const salida = 'salida';
  static const llegada = 'llegada';

  static const todos = [salida, llegada];
}

/// Cómo se cerró una transferencia. La diferencia importa: «firmada» no es el único cierre válido, y
/// cualquiera de los tres la vuelve inmutable para el servidor.
abstract final class TipoCierre {
  static const firmada = 'firmada';
  static const conReservas = 'con_reservas';
  static const noFirma = 'no_firma';

  /// El conductor hizo la salida del cargue sin inspector y nadie de la empresa entregó: firma solo quien recibe.
  static const reciboSinEntrega = 'recibo_sin_entrega';
}

/// La transferencia de un informe tal como la ve la pantalla. Lo que importa aquí es **quién falta por
/// firmar**: cada parte firma desde su propia sesión, así que entre una firma y la otra pueden pasar horas y
/// quien mira tiene que saber en cuál de los dos estados está.
class TransferenciaVista {
  const TransferenciaVista({
    required this.entregaId,
    required this.recibeId,
    this.entregaNombre,
    this.recibeNombre,
    this.entregaFirmoEn,
    this.recibeFirmoEn,
    this.tipoCierre,
  });

  factory TransferenciaVista.fromJson(Map<String, dynamic> json) => TransferenciaVista(
        entregaId: json['entrega_usuario_id'] as String? ?? '',
        recibeId: json['recibe_usuario_id'] as String? ?? '',
        // El inventario no los manda; el detalle del informe sí.
        entregaNombre: json['entrega_nombre'] as String?,
        recibeNombre: json['recibe_nombre'] as String?,
        entregaFirmoEn: switch (json['entrega_firmada_at']) { final String f => DateTime.tryParse(f), _ => null },
        recibeFirmoEn: switch (json['recibe_firmada_at']) { final String f => DateTime.tryParse(f), _ => null },
        tipoCierre: json['tipo_cierre'] as String?,
      );

  final String entregaId;
  final String recibeId;
  final String? entregaNombre;
  final String? recibeNombre;
  final DateTime? entregaFirmoEn;
  final DateTime? recibeFirmoEn;

  /// Null = todavía abierta. Es lo que bloquea la salida de la etapa siguiente: «firmada» no alcanza, porque
  /// una transferencia puede cerrarse de tres formas distintas.
  final String? tipoCierre;

  bool get cerrada => tipoCierre != null;
  bool get firmoEntrega => entregaFirmoEn != null;
  bool get firmoRecibe => recibeFirmoEn != null;
  bool get completa => firmoEntrega && firmoRecibe;

  /// Si ya firmó uno solo, falta el otro. Null cuando no ha firmado nadie o cuando ya firmaron los dos.
  bool? get faltaRecibe => firmoEntrega == firmoRecibe ? null : !firmoRecibe;

  /// ¿Ya firmó esta persona? Null si no es ninguna de las dos partes.
  bool? firmo(String? usuarioId) => switch (usuarioId) {
        final id? when id == entregaId => firmoEntrega,
        final id? when id == recibeId => firmoRecibe,
        _ => null,
      };
}

/// El motivo por el que el conductor hizo la salida del cargue sin inspector.
class MotivoSinInspector {
  const MotivoSinInspector({required this.codigo, required this.nombre, this.texto});

  factory MotivoSinInspector.fromJson(Map<String, dynamic> json) => MotivoSinInspector(
        codigo: json['codigo'] as String? ?? '',
        nombre: json['nombre'] as String? ?? '',
        texto: json['texto'] as String?,
      );

  final String codigo;
  final String nombre;

  /// Lo que escribió quien lo registró, cuando el motivo lo pide («Otro»).
  final String? texto;

  /// «Otro: El inspector se enfermó», o solo el nombre.
  String get resumen => texto == null || texto!.isEmpty ? nombre : '$nombre: $texto';
}

/// Algo del proceso que no es un daño: no tiene zona, clase ni nivel. Por ahora, el cargue sin inspector y el recibo
/// sin entrega.
class NovedadProceso {
  const NovedadProceso({required this.tipo, required this.motivo, this.texto, this.usuarioNombre, this.fecha});

  factory NovedadProceso.fromJson(Map<String, dynamic> json) => NovedadProceso(
        tipo: json['tipo'] as String? ?? '',
        motivo: json['motivo'] as String? ?? '',
        texto: json['texto'] as String?,
        usuarioNombre: json['usuario_nombre'] as String?,
        fecha: switch (json['fecha']) { final String f => DateTime.tryParse(f), _ => null },
      );

  final String tipo;
  final String motivo;
  final String? texto;
  final String? usuarioNombre;
  final DateTime? fecha;

  String get resumen => texto == null || texto!.isEmpty ? motivo : '$motivo: $texto';
}

class InspeccionVista {
  const InspeccionVista({
    required this.id,
    required this.momento,
    required this.momentoNombre,
    required this.fecha,
    required this.estado,
    this.usuarioId,
    this.notas,
    this.cerradaEn,
    this.hallazgos = const [],
    this.evidencias = const [],
    this.transferencia,
    this.notasAclaratorias = const [],
    this.motivoSinInspector,
    this.novedadesProceso = const [],
    this.version = 1,
    this.corrigeInspeccionId,
  });

  factory InspeccionVista.fromJson(Map<String, dynamic> json) {
    final transferencia = json['transferencia'] as Map<String, dynamic>?;
    return InspeccionVista(
      id: json['id'] as String,
      momento: json['momento'] as String,
      momentoNombre: json['momento_nombre'] as String? ?? json['momento'] as String,
      usuarioId: json['usuario_id'] as String?,
      fecha: DateTime.parse(json['fecha'] as String),
      estado: json['estado'] as String,
      notas: json['notas'] as String?,
      cerradaEn: switch (json['cerrada_at']) { final String f => DateTime.tryParse(f), _ => null },
      hallazgos: [for (final h in (json['hallazgos'] as List<dynamic>? ?? [])) HallazgoVista.fromJson(h as Map<String, dynamic>)],
      evidencias: [for (final e in (json['evidencias'] as List<dynamic>? ?? [])) EvidenciaVista.fromJson(e as Map<String, dynamic>)],
      transferencia: transferencia == null ? null : TransferenciaVista.fromJson(transferencia),
      notasAclaratorias: [
        for (final n in (json['notas_aclaratorias'] as List<dynamic>? ?? []))
          NotaAclaratoria.fromJson(n as Map<String, dynamic>),
      ],
      motivoSinInspector: switch (json['motivo_sin_inspector']) {
        final Map<String, dynamic> m => MotivoSinInspector.fromJson(m),
        _ => null,
      },
      novedadesProceso: [
        for (final n in (json['novedades_proceso'] as List<dynamic>? ?? []))
          NovedadProceso.fromJson(n as Map<String, dynamic>),
      ],
      version: json['version'] as int? ?? 1,
      corrigeInspeccionId: json['corrige_inspeccion_id'] as String?,
    );
  }

  final String id;

  /// Codigo del momento (`salida` o `llegada`).
  final String momento;

  /// Texto listo para mostrar que manda la API («Inspección de salida»). No es un catalogo administrable.
  final String momentoNombre;

  final DateTime fecha;
  final String estado;

  /// Quién lo registró. Sirve para distinguir el informe propio del ajeno.
  final String? usuarioId;
  final String? notas;
  /// Cuándo se cerró; null si sigue en borrador.
  final DateTime? cerradaEn;

  final List<HallazgoVista> hallazgos;
  final List<EvidenciaVista> evidencias;

  /// Null mientras nadie haya empezado la transferencia de este informe.
  final TransferenciaVista? transferencia;

  /// Lo que se aclaró después de cerrarlo. El informe no cambia; las notas se suman al lado. Distinto de
  /// [notas], que son las que se escribieron al capturar.
  final List<NotaAclaratoria> notasAclaratorias;

  /// Por qué la hizo el conductor sin inspector; null en las demás inspecciones.
  final MotivoSinInspector? motivoSinInspector;

  /// Lo que pasó en el proceso alrededor de esta inspección (cargue sin inspector, recibo sin entrega).
  final List<NovedadProceso> novedadesProceso;

  /// 1 en la original; 2, 3… en las correcciones. Corregir una inspección cerrada no la cambia: crea la versión
  /// siguiente, que la referencia en [corrigeInspeccionId].
  final int version;
  final String? corrigeInspeccionId;

  /// La hizo el conductor porque no había inspector.
  bool get fueSinInspector => motivoSinInspector != null;

  bool get transferenciaFirmada => transferencia?.completa ?? false;
  String? get transferenciaTipoCierre => transferencia?.tipoCierre;

  bool get cerrada => estado == 'cerrada';
}

class EtapaVista {
  const EtapaVista({
    required this.id,
    required this.orden,
    required this.proceso,
    required this.requiereInspeccion,
    this.lugar,
    this.viajeId,
    this.conPersonalApoyo = false,
    this.traeMarcaApoyo = true,
    this.inspecciones = const [],
  });

  factory EtapaVista.fromJson(Map<String, dynamic> json) => EtapaVista(
        id: json['id'] as String,
        orden: json['orden'] as int,
        proceso: json['proceso'] as String,
        requiereInspeccion: json['requiere_inspeccion'] as bool,
        lugar: json['lugar'] as String?,
        viajeId: json['viaje_id'] as String?,
        conPersonalApoyo: json['con_personal_apoyo'] as bool? ?? false,
        traeMarcaApoyo: json.containsKey('con_personal_apoyo'),
        inspecciones: [for (final i in (json['inspecciones'] as List<dynamic>? ?? [])) InspeccionVista.fromJson(i as Map<String, dynamic>)],
      );

  final String id;
  final int orden;

  /// Código de la etapa del catálogo `etapas` (cargue, transporte, descargue).
  final String proceso;
  final bool requiereInspeccion;

  /// Nombre del lugar de la etapa («CEDI 1»), ya resuelto por la API. No es un código.
  final String? lugar;

  /// Viaje del que cuelga la etapa: de él salen la placa, el tráiler, la unidad y el conductor.
  final String? viajeId;

  /// En el cargue hay inspector o embarcador. Entonces el conductor no captura: solo firma el recibo.
  final bool conPersonalApoyo;

  /// La API manda la marca `con_personal_apoyo`. El contrato 2.10 la retiró: la salida del cargue la hace siempre un
  /// inspector, y el conductor solo la hace con la excepción «No hay inspector».
  final bool traeMarcaApoyo;

  final List<InspeccionVista> inspecciones;

  /// ¿Esta versión ya tiene una corrección encima? Entonces no es la que vale.
  bool fueCorregida(InspeccionVista inspeccion) => inspecciones.any((o) => o.corrigeInspeccionId == inspeccion.id);
}

/// Cómo se dice, en una línea, dónde está una inspección en su cadena de versiones. Null en la versión 1 que nadie
/// corrigió: no hay nada que decir.
String? textoDeVersion(InspeccionVista inspeccion, {bool corregida = false}) {
  if (inspeccion.version > 1) return 'Versión ${inspeccion.version} · corrige la versión ${inspeccion.version - 1}';
  if (corregida) return 'Versión ${inspeccion.version} · ya fue corregida';
  return null;
}

class Inventario {
  const Inventario({required this.id, required this.vin, required this.marca, this.modelo, required this.operacion, this.etapas = const []});

  factory Inventario.fromJson(Map<String, dynamic> json) => Inventario(
        id: json['id'] as String,
        vin: json['vin'] as String,
        marca: json['marca'] as String,
        modelo: json['modelo'] as String?,
        operacion: json['operacion'] as String,
        etapas: [for (final e in (json['etapas'] as List<dynamic>? ?? [])) EtapaVista.fromJson(e as Map<String, dynamic>)],
      );

  final String id;
  final String vin;
  final String marca;
  final String? modelo;
  final String operacion;
  final List<EtapaVista> etapas;

  /// La inspección más reciente de cualquier etapa (la que se revisa antes del cambio de responsabilidad).
  ({EtapaVista etapa, InspeccionVista inspeccion})? get ultimaInspeccion {
    ({EtapaVista etapa, InspeccionVista inspeccion})? mejor;
    for (final etapa in etapas) {
      for (final inspeccion in etapa.inspecciones) {
        if (mejor == null || inspeccion.fecha.isAfter(mejor.inspeccion.fecha)) mejor = (etapa: etapa, inspeccion: inspeccion);
      }
    }
    return mejor;
  }

  /// El mismo inventario con solo las etapas de [viajeId]; tal cual si no se pide ninguno.
  ///
  /// Un VIN puede haber andado en varios viajes y su inventario los trae todos. Quien abre la unidad lo hace
  /// desde un viaje, y mezclar las etapas de otro haría ver como pendiente lo que ya se cerró en su momento.
  Inventario delViaje(String? viajeId) => viajeId == null
      ? this
      : Inventario(
          id: id,
          vin: vin,
          marca: marca,
          modelo: modelo,
          operacion: operacion,
          etapas: [for (final e in etapas) if (e.viajeId == viajeId) e],
        );
}

/// Por qué todavía no se puede crear la salida de una etapa.
///
/// Se calcula con lo que ya está en el inventario, para deshabilitar el botón **antes** de llamar. Los códigos
/// son los mismos que manda el servidor en el `details` de su 409 `ETAPA_PREVIA_SIN_CIERRE`, así que el mensaje
/// que ve la persona es el mismo lo haya decidido la app o el servidor.
class BloqueoMomento {
  const BloqueoMomento({required this.motivo, required this.procesoPrevio});

  static const sinLlegada = 'etapa_previa_sin_cierre';
  static const transferenciaAbierta = 'transferencia_sin_cierre';

  final String motivo;

  /// Código de la etapa anterior, para que la pantalla lo traduzca con el catálogo.
  final String procesoPrevio;
}

/// Uno de los dos momentos de una etapa, tal como lo ve la pantalla.
class MomentoEtapa {
  const MomentoEtapa({required this.momento, this.inspeccion, this.bloqueo});

  final String momento;

  /// La inspección de ese momento, si ya existe. Si hay varias versiones, la última.
  final InspeccionVista? inspeccion;

  /// Null si se puede empezar.
  final BloqueoMomento? bloqueo;

  bool get cerrada => inspeccion?.cerrada ?? false;
  bool get enBorrador => inspeccion != null && !inspeccion!.cerrada;
  bool get sePuedeCapturar => inspeccion == null && bloqueo == null;
}

/// La salida del cargue no la hace el conductor: la hace un inspector, y si no hay ninguno el conductor activa en el
/// momento la excepción «No hay inspector» y da el motivo. Vale con la marca de personal de apoyo (contrato anterior)
/// y sin ella (contrato 2.10 en adelante, donde la regla es para todos los cargues).
///
/// La llegada y las demás etapas no cambian: ahí el conductor captura como siempre.
bool conductorUsaExcepcion(EtapaVista etapa, {required String rol, required String momento}) =>
    rol == 'conductor' &&
    etapa.proceso == 'cargue' &&
    momento == Momento.salida &&
    (etapa.conPersonalApoyo || !etapa.traeMarcaApoyo);

/// Lo que se dice en una etapa de un viaje que ya cerró. Se puede mirar lo que quedó, no agregar nada.
const mensajeViajeTerminado = 'Este viaje ya terminó: solo se puede consultar.';

/// Los dos momentos de una etapa que exige inspección, en orden. Una etapa que no la exige no tiene ninguno:
/// ahí solo se firma.
List<MomentoEtapa> momentosDeEtapa(EtapaVista etapa, Inventario inventario) {
  if (!etapa.requiereInspeccion) return const [];
  final bloqueoSalida = _bloqueoDeLaPrevia(etapa, inventario);
  return [
    for (final momento in Momento.todos)
      MomentoEtapa(
        momento: momento,
        // La versión que vale es la más alta: las anteriores quedan encadenadas debajo, sin cambios.
        inspeccion: etapa.inspecciones.where((i) => i.momento == momento).fold<InspeccionVista?>(
          null,
          (mejor, i) => mejor == null || i.version >= mejor.version ? i : mejor,
        ),
        // Solo la salida espera a la etapa anterior. La llegada no espera a nada: ni la app ni la API exigen
        // que la salida de esta misma etapa esté capturada antes (comprobado contra la API el 26-oct-2026).
        bloqueo: momento == Momento.salida ? bloqueoSalida : null,
      ),
  ];
}

/// La etapa anterior del mismo viaje debe tener cerrada su llegada y ninguna transferencia suya abierta.
///
/// Las dos condiciones son **las mismas que comprueba el servidor**, y eso importa más que su redacción: la
/// app solo deshabilita el botón antes de llamar, así que una regla propia más estricta bloquearía trabajo que
/// la API aceptaría. En particular, la transferencia se mira en **toda la etapa**, no en el informe de llegada:
/// cuelga del primer informe que se cierre, que normalmente es el de salida.
BloqueoMomento? _bloqueoDeLaPrevia(EtapaVista etapa, Inventario inventario) {
  EtapaVista? previa;
  for (final otra in inventario.etapas) {
    if (otra.viajeId != etapa.viajeId || otra.orden >= etapa.orden) continue;
    if (previa == null || otra.orden > previa.orden) previa = otra;
  }
  if (previa == null) return null; // la primera etapa no tiene anterior

  if (!previa.inspecciones.any((i) => i.momento == Momento.llegada && i.cerrada)) {
    return BloqueoMomento(motivo: BloqueoMomento.sinLlegada, procesoPrevio: previa.proceso);
  }
  if (previa.inspecciones.any((i) => i.transferencia?.cerrada == false)) {
    return BloqueoMomento(motivo: BloqueoMomento.transferenciaAbierta, procesoPrevio: previa.proceso);
  }
  return null;
}

/// El texto del bloqueo, con el nombre de la etapa ya traducido. Sirve para el que calcula la app y para el que
/// trae el 409 del servidor.
String mensajeBloqueo(String motivo, String etapaPrevia) => motivo == BloqueoMomento.transferenciaAbierta
    ? 'Falta cerrar la transferencia de $etapaPrevia'
    : 'Falta cerrar la llegada de $etapaPrevia';

/// Qué se puede hacer hoy en una etapa de la ruta de custodia.
sealed class AccionEtapa {
  const AccionEtapa();
}

/// Hay un informe sobre el que firmar la transferencia. [soloFirma] = la etapa no exige informe propio y se firma
/// sobre el último informe del VIN.
class FirmarTransferencia extends AccionEtapa {
  const FirmarTransferencia({required this.inspeccionId, required this.soloFirma, required this.transferencia});

  final String inspeccionId;
  final bool soloFirma;

  /// Null mientras no haya firmado nadie.
  final TransferenciaVista? transferencia;

  /// Cerrada de cualquiera de las tres formas: ya no admite firmas y el servidor rechazaría otra.
  bool get cerrada => transferencia?.cerrada ?? false;
}

/// No hay nada sobre lo que firmar todavía.
class SinInforme extends AccionEtapa {
  const SinInforme();
}

/// Sobre qué se firma la transferencia de una etapa. Capturar ya no sale de aquí: eso lo deciden los momentos,
/// que son dos por etapa.
AccionEtapa accionDeEtapa(EtapaVista etapa, Inventario inventario) {
  final inspecciones = etapa.inspecciones;
  final referencia = inspecciones.where((i) => i.cerrada).firstOrNull ?? inspecciones.firstOrNull;
  if (etapa.requiereInspeccion) {
    if (referencia == null) return const SinInforme();
    return FirmarTransferencia(inspeccionId: referencia.id, soloFirma: false, transferencia: referencia.transferencia);
  }
  final base = referencia ?? inventario.ultimaInspeccion?.inspeccion;
  if (base == null) return const SinInforme();
  return FirmarTransferencia(inspeccionId: base.id, soloFirma: true, transferencia: base.transferencia);
}
