import '../../../core/errores/error_api.dart';
import 'espera_del_punto.dart';

/// La entrega de una parada: las unidades del viaje que se entregan en un mismo lugar de destino.
///
/// Es lo que responde `GET /api/viajes/{id}/entregas` (una por parada) y `GET /api/viajes/{id}/entregas/{lugar}`.
/// En cada parada se firma una vez para todas.
class EntregaPunto {
  const EntregaPunto({
    required this.viajeId,
    required this.lugarId,
    required this.lugar,
    required this.unidades,
    required this.pendientes,
    required this.entregada,
    this.ciudad,
    this.tieneInspector = false,
    this.estadoDelServidor,
    this.receptorDelServidor,
    this.receptorNombre,
    this.conductorLlego = true,
    this.espera,
    this.esperaAvisoMinutos = esperaAvisoMinutosPorDefecto,
    this.esperaPlanBMinutos = esperaPlanBMinutosPorDefecto,
    this.danosPorCalificar = const [],
    this.copiaSinSenal = false,
  });

  /// [copiaSinSenal]: se leyó de la copia guardada en el teléfono porque no hubo señal para pedirla.
  factory EntregaPunto.fromJson(Map<String, dynamic> json, {bool copiaSinSenal = false}) {
    final unidades = [
      for (final u in json['unidades'] as List<dynamic>? ?? const []) UnidadEntrega.fromJson(u as Map<String, dynamic>),
    ];
    return EntregaPunto(
      viajeId: json['viaje_id'] as String,
      lugarId: json['lugar_id'] as String,
      lugar: json['lugar'] as String? ?? '',
      ciudad: json['ciudad'] as String?,
      unidades: unidades,
      pendientes: json['unidades_pendientes'] as int? ?? unidades.where((u) => u.porEntregar).length,
      entregada: json['entregada'] as bool? ?? false,
      tieneInspector: json['lugar_tiene_inspector'] as bool? ?? false,
      estadoDelServidor: EstadoParada.desde(json['estado_parada'] as String?),
      receptorDelServidor: ReceptorParada.desde(json['receptor'] as String?),
      receptorNombre: json['receptor_nombre'] as String?,
      // Un servidor de antes de la entrega por parada no lo manda. Sin el dato no se bloquea la firma: el servidor
      // la rechaza con `conductor_sin_llegada` si todavía no toca, y la pantalla lo explica (S8-HU-04.2 CA-2).
      conductorLlego: json['conductor_llego'] as bool? ?? true,
      // S8-HU-05.2 CA-3: la última espera de la parada y los X e Y vigentes, con los que el reloj cuenta sin señal.
      espera: switch (json['espera']) {
        final Map<String, dynamic> e => EsperaDelPunto.fromJson(e),
        _ => null,
      },
      esperaAvisoMinutos: (json['espera_aviso_minutos'] as num?)?.toInt() ?? esperaAvisoMinutosPorDefecto,
      esperaPlanBMinutos: (json['espera_plan_b_minutos'] as num?)?.toInt() ?? esperaPlanBMinutosPorDefecto,
      danosPorCalificar: [
        for (final d in json['danos_por_calificar'] as List<dynamic>? ?? const [])
          DanoPorCalificar.fromJson(d as Map<String, dynamic>),
      ],
      copiaSinSenal: copiaSinSenal,
    );
  }

  final String viajeId;
  final String lugarId;
  final String lugar;
  final String? ciudad;
  final List<UnidadEntrega> unidades;

  /// «Aquí entregas estas N unidades».
  final int pendientes;
  final bool entregada;

  /// Este lugar tiene inspectores asignados, y por eso la entrega la recibe uno de ellos y no quien esté allí.
  ///
  /// Lo decide el servidor **por punto de entrega** —no por viaje— y es el mismo dato con el que rechaza la firma
  /// (`lugar_tiene_inspector`, S7-HU-22.1). Antes la app lo adivinaba mirando si el viaje tenía inspector, que es
  /// otra cosa: un viaje sin inspector puede entregar en un punto que sí los tiene.
  final bool tieneInspector;

  /// `estado_parada` y `receptor` tal como llegan; null si el servidor no los manda (uno de antes de la entrega por
  /// parada). Se leen por [estado] y [receptor].
  final EstadoParada? estadoDelServidor;
  final ReceptorParada? receptorDelServidor;

  /// Cómo va la parada (S8-HU-04.2 CA-8). Si el servidor no lo manda, se deduce de `entregada`.
  EstadoParada get estado => estadoDelServidor ?? (entregada ? EstadoParada.entregada : EstadoParada.pendiente);

  /// Quién recibe en la parada (CA-3). Si el servidor no lo manda, se deduce de si el punto tiene inspector.
  ReceptorParada get receptor =>
      receptorDelServidor ?? (tieneInspector ? ReceptorParada.inspectorDelPunto : ReceptorParada.personaDelLugar);

  /// El nombre del inspector que tomó la entrega o de la persona que ya recibió. Null si nadie.
  final String? receptorNombre;

  /// El conductor ya marcó «Llegué» en esta parada: la firma del inspector solo se activa entonces (CA-2).
  final bool conductorLlego;

  /// La última espera del inspector del punto de esta parada; null si el conductor nunca activó «No hay inspector»
  /// en ella (S8-HU-05.2).
  final EsperaDelPunto? espera;

  /// X e Y vigentes: los minutos hasta el aviso al coordinador y, después de él, hasta el plan B. Se guardan con la
  /// activación para que el reloj cuente sin señal (CA-3).
  final int esperaAvisoMinutos;
  final int esperaPlanBMinutos;

  /// Los daños sin nivel que el conductor marcó en sus revisiones de llegada del plan B (CA-10). Al conductor el
  /// servidor no le enseña el nivel, y la pantalla tampoco le enseña esta lista.
  final List<DanoPorCalificar> danosPorCalificar;

  /// Es la copia guardada en el teléfono: no hubo señal para pedirla. El reloj de la espera igual cuenta con ella.
  final bool copiaSinSenal;

  /// Las que se entregan en esta parada y todavía no están ni entregadas ni rechazadas: sobre ellas va la firma.
  List<UnidadEntrega> get porEntregar => [...unidades.where((u) => u.porEntregar)];

  /// Los carros que no se entregaron por una novedad de su revisión de llegada (C20).
  List<UnidadEntrega> get rechazadas => [...unidades.where((u) => u.estado == EstadoEntrega.rechazado)];

  /// Alguna todavía no tiene su revisión de llegada del Descargue, que es donde se firma: el servidor rechaza la
  /// firma de la parada hasta que todas la tengan cerrada.
  List<UnidadEntrega> get sinInspeccion => [...porEntregar.where((u) => u.estado == EstadoEntrega.sinInspeccion)];
}

/// Cómo va una parada (`estado_parada`).
enum EstadoParada {
  pendiente,
  entregada,

  /// Se firmó: algunos carros se entregaron y otros se rechazaron.
  entregadaConRechazos,

  /// Se firmó como constancia del rechazo de cada carro: ninguno se entregó (C24).
  firmadaTodosRechazados;

  /// Null si el servidor no lo manda o manda uno que la app no conoce: entonces se deduce de `entregada`.
  static EstadoParada? desde(String? valor) => switch (valor) {
        'pendiente' => pendiente,
        'entregada' => entregada,
        'entregada_con_rechazos' => entregadaConRechazos,
        'firmada_todos_rechazados' => firmadaTodosRechazados,
        _ => null,
      };
}

/// Quién recibe en una parada (`receptor`): lo dice el servidor, y la pantalla no lo cambia (C9 y C15).
enum ReceptorParada {
  /// El inspector que tomó la entrega de esta parada. Firma desde su app.
  inspectorDelPunto,

  /// El punto no tiene inspectores: recibe quien esté allí, con su nombre, su cédula y su firma.
  personaDelLugar,

  /// El punto tiene inspectores y ninguno tomó la entrega: no se puede firmar.
  sinReceptor;

  static ReceptorParada? desde(String? valor) => switch (valor) {
        'inspector_del_punto' => inspectorDelPunto,
        'persona_del_lugar' => personaDelLugar,
        'sin_receptor' => sinReceptor,
        _ => null,
      };
}

enum EstadoEntrega {
  /// Aún no tiene la revisión de llegada del Descargue.
  sinInspeccion,
  pendiente,
  cerrada,

  /// No se entregó: se rechazó por una novedad de su revisión de llegada y sigue en el camión.
  rechazado;

  static EstadoEntrega desde(String? valor) => switch (valor) {
        'cerrada' => cerrada,
        'pendiente' => pendiente,
        'rechazado' => rechazado,
        _ => sinInspeccion,
      };
}

class UnidadEntrega {
  const UnidadEntrega({
    required this.vin,
    required this.estado,
    this.inspeccionId,
    this.tipoCierre,
    this.entregaFirmada = false,
    this.recibeFirmada = false,
    this.recibioExterno,
    this.reservas,
    this.motivoCodigo,
    this.rechazo,
    this.sinInspectorDelPunto = false,
  });

  factory UnidadEntrega.fromJson(Map<String, dynamic> json) => UnidadEntrega(
        vin: json['vin'] as String,
        estado: EstadoEntrega.desde(json['estado'] as String?),
        inspeccionId: json['inspeccion_id'] as String?,
        tipoCierre: json['tipo_cierre'] as String?,
        entregaFirmada: json['entrega_firmada_at'] != null,
        recibeFirmada: json['recibe_firmada_at'] != null,
        recibioExterno: json['recibe_externo_nombre'] as String?,
        reservas: switch (json['reservas']) { final String r when r.trim().isNotEmpty => r, _ => null },
        motivoCodigo: json['motivo_codigo'] as String?,
        rechazo: switch (json['rechazo']) {
          final Map<String, dynamic> r => RechazoEntrega.fromJson(r),
          _ => null,
        },
        sinInspectorDelPunto: json['entregada_sin_inspector_del_punto'] as bool? ?? false,
      );

  final String vin;
  final EstadoEntrega estado;

  /// La revisión de llegada del Descargue de este carro, donde se firma. Null mientras no exista.
  final String? inspeccionId;
  final String? tipoCierre;
  final bool entregaFirmada;
  final bool recibeFirmada;

  /// Quién recibió cuando no fue un usuario del sistema, sino una persona del punto de entrega.
  ///
  /// Se muestra el nombre y no la cédula: la cédula es un dato personal y para saber quién recibió basta el
  /// nombre. Queda guardada en la entrega y en la auditoría, que es donde hace falta.
  final String? recibioExterno;

  /// Las observaciones de quien recibe sobre este carro (S8-HU-04.2 CA-5). Quien entrega las lee antes de firmar.
  final String? reservas;

  /// El motivo codificado del cierre con una sola firma, cuando se cerró así.
  final String? motivoCodigo;

  /// El daño por el que se rechazó, cuando se rechazó.
  final RechazoEntrega? rechazo;

  /// Se entregó con el plan B: «Entregada sin inspector del punto» (S8-HU-05). Un carro rechazado nunca la lleva.
  final bool sinInspectorDelPunto;

  bool get cerrada => estado == EstadoEntrega.cerrada;

  /// Ni entregada ni rechazada: falta firmarla en la parada.
  bool get porEntregar => estado == EstadoEntrega.pendiente || estado == EstadoEntrega.sinInspeccion;
}

/// El rechazo de un carro en su parada: el daño de su revisión de llegada que lo motivó y quién lo marcó.
class RechazoEntrega {
  const RechazoEntrega({required this.hallazgoId, this.marcadoPorNombre});

  factory RechazoEntrega.fromJson(Map<String, dynamic> json) => RechazoEntrega(
        hallazgoId: json['hallazgo_id'] as String? ?? '',
        marcadoPorNombre: json['marcado_por_nombre'] as String?,
      );

  final String hallazgoId;
  final String? marcadoPorNombre;
}

/// Las paradas de un viaje y cuántos carros no tienen destino (viajes de antes del destino por unidad).
class EntregasDelViaje {
  const EntregasDelViaje({required this.puntos, this.unidadesSinDestino = 0});

  factory EntregasDelViaje.fromJson(Map<String, dynamic> json) => EntregasDelViaje(
        puntos: [
          for (final p in json['puntos'] as List<dynamic>? ?? const []) EntregaPunto.fromJson(p as Map<String, dynamic>),
        ],
        unidadesSinDestino: json['unidades_sin_destino'] as int? ?? 0,
      );

  final List<EntregaPunto> puntos;
  final int unidadesSinDestino;
}

/// Un daño de la revisión de llegada que tiene foto: el único que justifica rechazar el carro en la parada (C20).
class DanoDeLlegada {
  const DanoDeLlegada({required this.hallazgoId, required this.zona, this.clase});

  final String hallazgoId;

  /// «Izquierdo 7 · Puerta delantera», tal como lo arma el servidor.
  final String zona;

  /// Código de `clases-dano`; null en la novedad del conductor sin revisar.
  final String? clase;
}

/// Lo que de la revisión de llegada de un carro le importa a la parada: si está cerrada y qué daños con foto tiene.
class RevisionDeLlegada {
  const RevisionDeLlegada({required this.cerrada, this.danosConFoto = const []});

  /// Lee el informe (`GET /api/inspecciones/{id}`) con la misma regla del servidor (`clsFotosDeHallazgos`): un daño
  /// tiene foto si alguna foto del informe es suya o, en el daño de un extra, si el extra tiene foto. No cuentan el
  /// sello de quien valida ni la constancia: no son la foto del daño, y el servidor rechazaría el rechazo con 400.
  factory RevisionDeLlegada.fromJson(Map<String, dynamic> json) {
    final evidencias = [
      for (final e in json['evidencias'] as List<dynamic>? ?? const [])
        if (e case final Map<String, dynamic> foto when foto['es_sello'] != true && foto['es_constancia'] != true) foto,
    ];
    final hallazgos = [for (final h in json['hallazgos'] as List<dynamic>? ?? const []) h as Map<String, dynamic>];
    final conFoto = {
      for (final e in evidencias)
        if (e['hallazgo_id'] case final String id) id,
    };
    final extrasConFoto = {
      for (final e in evidencias)
        if (e['extra_id'] case final String id) id,
    };
    return RevisionDeLlegada(
      cerrada: json['estado'] == 'cerrada',
      danosConFoto: [
        for (final h in hallazgos)
          if (conFoto.contains(h['id']) || extrasConFoto.contains(h['extra_id']))
            DanoDeLlegada(
              hallazgoId: h['id'] as String,
              zona: h['zona'] as String? ?? h['parte_vehiculo'] as String? ?? 'Sin zona',
              clase: h['clase_dano'] as String?,
            ),
      ],
    );
  }

  final bool cerrada;
  final List<DanoDeLlegada> danosConFoto;
}

/// Los motivos codificados del cierre con una sola firma (S8-HU-04.2 CA-6).
abstract final class MotivoUnaFirma {
  /// Quien recibe (o quien entrega) se negó a firmar: exige al menos una foto.
  static const seNegoAFirmar = 'se_nego_a_firmar';

  /// No había nadie para recibir: solo en una parada sin inspectores, y su evidencia son las fotos de la revisión
  /// de llegada de cada carro (C23).
  static const noHabiaNadie = 'no_habia_nadie';

  /// El cierre del plan B con la sola firma del conductor (S8-HU-05.2 CA-4).
  static const sinInspectorDelPunto = 'sin_inspector_del_punto';
}

/// Los motivos (`details.motivo`) con los que el servidor rechaza la firma de una parada por algo de la parada misma
/// y no del envío. Reintentar manda lo mismo y recibe lo mismo: la pantalla lo explica leyendo el motivo, no el
/// texto (S8-HU-04.2 CA-3), y conserva lo escrito.
abstract final class MotivoParada {
  /// El punto tiene inspectores y ninguno tomó la entrega.
  static const sinReceptor = 'sin_receptor';

  /// El inspector firma antes de que el conductor marque «Llegué» en la parada.
  static const conductorSinLlegada = 'conductor_sin_llegada';

  /// La revisión de llegada de algún carro de la parada sigue abierta o no existe.
  static const llegadaAbierta = 'llegada_abierta';

  /// El carro que se rechaza no tiene ningún daño con foto en su revisión de llegada.
  static const sinNovedadParaRechazar = 'sin_novedad_para_rechazar';

  /// La entrega de esta parada la tomó otro inspector.
  static const entregaTomada = 'entrega_tomada';

  /// La parada no es de ninguno de los puntos del inspector.
  static const noEsSuPunto = 'no_es_su_punto';

  /// El motivo del cierre con una sola firma todavía no se puede usar.
  static const motivoNoAplica = 'motivo_no_aplica';

  /// S8-HU-05: el plan B todavía no se habilita (`details.faltan_minutos`).
  static const planBNoHabilitado = 'plan_b_no_habilitado';

  /// S8-HU-05: el conductor ya cerró la parada sin inspector del punto; el reclamo del inspector llega tarde.
  static const entregaCerradaSinInspector = 'entrega_cerrada_sin_inspector';

  /// S8-HU-05: el inspector que tomó la parada no ha calificado los daños que marcó el conductor (`details.faltan`).
  static const danosSinCalificar = 'danos_sin_calificar';

  /// S8-HU-05: la activación de «No hay inspector» en una parada que no tiene inspectores.
  static const paradaSinInspectores = 'parada_sin_inspectores';

  /// S8-HU-05: la activación de «No hay inspector» en una parada ya entregada.
  static const entregaCerrada = 'entrega_cerrada';

  /// S8-HU-05: la activación de «No hay inspector» antes de marcar «Llegué» en la parada.
  static const sinLlegueEnParada = 'sin_llegue_en_parada';

  static const todos = {
    sinReceptor,
    conductorSinLlegada,
    llegadaAbierta,
    sinNovedadParaRechazar,
    entregaTomada,
    noEsSuPunto,
    motivoNoAplica,
    planBNoHabilitado,
    entregaCerradaSinInspector,
    danosSinCalificar,
    paradaSinInspectores,
    entregaCerrada,
    sinLlegueEnParada,
  };
}

/// S8-HU-05.2 CA-6: lo que se dice de una entrega de la parada que el servidor rechazó después, desde la cola, leído
/// por su motivo y nunca por el texto del servidor. Un motivo que la app no conoce no se inventa: se dice que el
/// servidor la rechazó y se ofrece abrir la parada para ver cómo quedó.
String explicacionDelRechazoTardio(String? motivo) => switch (motivo) {
      MotivoParada.entregaTomada =>
        'Mientras se enviaba, un inspector del punto tomó la entrega: firma con él. Tu firma se conserva',
      MotivoParada.planBNoHabilitado => 'Aún no se podía cerrar sin inspector del punto: espera y vuelve a intentarlo',
      MotivoParada.sinReceptor => 'Aquí firma el inspector del punto: pídele que tome la entrega en su app',
      MotivoParada.entregaCerradaSinInspector => 'Esta entrega ya se cerró sin inspector del punto',
      MotivoParada.conductorSinLlegada => 'El conductor aún no marca «Llegué» en esta parada',
      MotivoParada.llegadaAbierta => 'Falta cerrar la revisión de llegada de algún carro de la parada',
      MotivoParada.sinNovedadParaRechazar => 'Un carro rechazado no tiene un daño con foto que lo justifique',
      MotivoParada.danosSinCalificar => 'Antes de firmar, califica los daños que marcó el conductor en esta parada',
      codigoSinRespuesta => mensajeSinRespuesta,
      _ => 'El servidor rechazó la entrega',
    };

/// Lo que se dice de un envío de la parada que agotó sus intentos sin respuesta: no lo rechazó nadie.
const mensajeSinRespuesta = 'No hubo respuesta del servidor tras varios intentos: vuelve a intentarlo';

/// Lo mismo para «No hay inspector» que el servidor rechazó desde la cola.
String explicacionDeLaActivacionRechazada(String? motivo) => switch (motivo) {
      MotivoParada.entregaTomada => 'Mientras se enviaba, un inspector del punto tomó la entrega: firma con él',
      MotivoParada.entregaCerrada => 'Esta parada ya se entregó: no hay a quién esperar',
      MotivoParada.paradaSinInspectores => 'Esta parada no tiene inspectores: aquí recibe la persona del lugar',
      MotivoParada.sinLlegueEnParada => 'Marca «Llegué a esta parada» antes de activar «No hay inspector»',
      codigoSinRespuesta => mensajeSinRespuesta,
      _ => 'El servidor rechazó «No hay inspector»',
    };
