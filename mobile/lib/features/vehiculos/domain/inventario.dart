import 'coords_diagrama.dart';

class HallazgoVista {
  const HallazgoVista({required this.id, required this.parte, required this.clase, required this.tamano, this.coords});

  factory HallazgoVista.fromJson(Map<String, dynamic> json) => HallazgoVista(
        id: json['id'] as String,
        parte: json['parte_vehiculo'] as String,
        clase: json['clase_dano'] as String,
        tamano: json['tamano_dano'] as String,
        coords: json['coords_diagrama'] == null ? null : CoordsDiagrama.fromJson(json['coords_diagrama'] as Map<String, dynamic>),
      );

  final String id;
  final String parte;
  final String clase;
  final String tamano;
  final CoordsDiagrama? coords;
}

class EvidenciaVista {
  const EvidenciaVista({required this.id, this.url});

  factory EvidenciaVista.fromJson(Map<String, dynamic> json) =>
      EvidenciaVista(id: json['id'] as String, url: json['url'] as String?);

  final String id;

  /// Ruta relativa o absoluta de la imagen; null si no es servible.
  final String? url;
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

  final List<InspeccionVista> inspecciones;
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

/// Quién captura esta etapa: el conductor solo cuando no hay quien lo haga por él.
///
/// La API se lo permitiría igual; es la app la que no se lo ofrece, porque el cargue con personal de apoyo lo
/// inspecciona ese personal y el conductor solo firma lo que recibe.
bool puedeCapturar(EtapaVista etapa, {required String rol}) =>
    !(rol == 'conductor' && etapa.proceso == 'cargue' && etapa.conPersonalApoyo);

/// Lo que se le dice al conductor cuando no le toca a él.
const mensajeCargueConApoyo = 'Este cargue tiene personal de apoyo: solo firmas el recibo.';

/// Los dos momentos de una etapa que exige inspección, en orden. Una etapa que no la exige no tiene ninguno:
/// ahí solo se firma.
List<MomentoEtapa> momentosDeEtapa(EtapaVista etapa, Inventario inventario) {
  if (!etapa.requiereInspeccion) return const [];
  final bloqueoSalida = _bloqueoDeLaPrevia(etapa, inventario);
  return [
    for (final momento in Momento.todos)
      MomentoEtapa(
        momento: momento,
        inspeccion: etapa.inspecciones.where((i) => i.momento == momento).lastOrNull,
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
