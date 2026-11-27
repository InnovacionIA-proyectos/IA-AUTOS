import '../../sync/domain/job.dart';
import 'entrega_punto.dart';

/// La espera del inspector del punto (S8-HU-05): en una parada con inspectores donde nadie tomó la entrega, el
/// conductor toca «No hay inspector» y desde ese momento corren dos plazos que fija el servidor. A los X minutos se
/// avisa al coordinador; a los X más Y se habilita el plan B: cerrar sin inspector del punto.
///
/// «Llegué» no crea ninguna espera: sin activarla no pasa nada por mucho tiempo que pase (ESC-01).
class EsperaDelPunto {
  const EsperaDelPunto({
    required this.estado,
    required this.activadaAt,
    required this.avisoDesde,
    required this.planBDesde,
    this.id,
    this.motivo,
    this.motivoTexto,
    this.avisoAt,
    this.planBHabilitado = false,
    this.inspectorNombre,
    this.inspectorLlegoAt,
    this.cerradaAt,
    this.deEsteTelefono = false,
  });

  /// La espera que este teléfono anota en la copia sin señal de la parada al activar «No hay inspector», antes de
  /// que el servidor la confirme: lleva la hora de activación y los plazos con los X e Y de la parada (CA-3). La
  /// marca [claveDeEsteTelefono] no es del contrato: la pone solo la app, y la copia del servidor la reemplaza.
  factory EsperaDelPunto.anotada(SinInspectorParadaJob activacion) {
    final activada = DateTime.parse(activacion.fechaMomento).toUtc();
    return EsperaDelPunto(
      estado: EstadoEspera.esperando,
      activadaAt: activada,
      avisoDesde: activada.add(Duration(minutes: activacion.avisoMinutos)),
      planBDesde: activada.add(Duration(minutes: activacion.avisoMinutos + activacion.planBMinutos)),
      motivo: activacion.motivo,
      motivoTexto: activacion.motivoTexto,
      deEsteTelefono: true,
    );
  }

  /// La marca de la espera anotada en este teléfono dentro de la copia guardada.
  static const claveDeEsteTelefono = 'anotada_en_este_telefono';

  /// Como la guarda la copia sin señal de la parada.
  Map<String, dynamic> toJson() => {
        'id': id,
        'estado': switch (estado) {
          EstadoEspera.esperando => 'esperando',
          EstadoEspera.inspectorLlego => 'inspector_llego',
          EstadoEspera.cerradaSinInspectorDelPunto => 'cerrada_sin_inspector_del_punto',
          EstadoEspera.anulada => 'anulada',
        },
        'motivo_sin_inspector': motivo,
        'motivo_sin_inspector_texto': motivoTexto,
        'activada_at': activadaAt.toIso8601String(),
        'aviso_desde': avisoDesde.toIso8601String(),
        'aviso_at': avisoAt?.toIso8601String(),
        'plan_b_desde': planBDesde.toIso8601String(),
        'plan_b_habilitado': planBHabilitado,
        'inspector_nombre': inspectorNombre,
        'inspector_llego_at': inspectorLlegoAt?.toIso8601String(),
        'cerrada_at': cerradaAt?.toIso8601String(),
        if (deEsteTelefono) claveDeEsteTelefono: true,
      };

  /// Lee `EsperaDelPuntoDto`. Un servidor que no mande los plazos ya calculados los deja en la hora de activación:
  /// la app nunca adelanta el plan B por su cuenta más allá de lo que dice el servidor.
  factory EsperaDelPunto.fromJson(Map<String, dynamic> json) {
    final activada = _fecha(json['activada_at']) ?? DateTime.now().toUtc();
    return EsperaDelPunto(
      id: json['id'] as String?,
      estado: EstadoEspera.desde(json['estado'] as String?),
      motivo: json['motivo_sin_inspector'] as String?,
      motivoTexto: json['motivo_sin_inspector_texto'] as String?,
      activadaAt: activada,
      avisoDesde: _fecha(json['aviso_desde']) ?? activada,
      avisoAt: _fecha(json['aviso_at']),
      planBDesde: _fecha(json['plan_b_desde']) ?? activada,
      planBHabilitado: json['plan_b_habilitado'] as bool? ?? false,
      inspectorNombre: json['inspector_nombre'] as String?,
      inspectorLlegoAt: _fecha(json['inspector_llego_at']),
      cerradaAt: _fecha(json['cerrada_at']),
      deEsteTelefono: json[claveDeEsteTelefono] == true,
    );
  }

  static DateTime? _fecha(Object? valor) => valor is String ? DateTime.tryParse(valor)?.toUtc() : null;

  final String? id;
  final EstadoEspera estado;

  /// El código de `motivos-sin-inspector` con que se activó, y lo que se escribió si el motivo lo pide.
  final String? motivo;
  final String? motivoTexto;

  /// La hora del celular al tocar «No hay inspector»: desde ella cuentan los dos plazos, aunque llegara después.
  final DateTime activadaAt;

  /// Desde cuándo se avisa al coordinador (`activada_at` + X) y cuándo se avisó, si ya.
  final DateTime avisoDesde;
  final DateTime? avisoAt;

  /// Desde cuándo se puede cerrar sin inspector del punto (`activada_at` + X + Y) y si el servidor ya lo habilitó.
  final DateTime planBDesde;
  final bool planBHabilitado;

  /// El inspector que llegó durante la espera.
  final String? inspectorNombre;
  final DateTime? inspectorLlegoAt;

  /// Cuándo se cerró la parada sin inspector del punto.
  final DateTime? cerradaAt;

  /// La anotó este teléfono al activar, y el servidor todavía no la ha confirmado: su reloj es el del teléfono.
  final bool deEsteTelefono;

  /// Sigue corriendo: nadie tomó la entrega y la parada no se ha cerrado.
  bool get abierta => estado == EstadoEspera.esperando;
}

/// Cómo terminó (o no) la espera (`estado`).
enum EstadoEspera {
  esperando,

  /// Un inspector tomó la entrega durante la espera: el reloj nunca se la quita (ESC-07).
  inspectorLlego,

  /// El conductor cerró la parada con el plan B.
  cerradaSinInspectorDelPunto,

  /// El viaje se canceló o se finalizó a mano con la espera abierta.
  anulada;

  static EstadoEspera desde(String? valor) => switch (valor) {
        'esperando' => esperando,
        'inspector_llego' => inspectorLlego,
        'cerrada_sin_inspector_del_punto' => cerradaSinInspectorDelPunto,
        _ => anulada,
      };
}

/// Un daño sin nivel que el conductor marcó en su revisión de llegada con el plan B (`danos_por_calificar`). Lo
/// califica el inspector que tomó la parada antes de firmar (S8-HU-05.2 CA-10, C19).
class DanoPorCalificar {
  const DanoPorCalificar({required this.hallazgoId, required this.vin, this.parte, this.clase, this.evidenciaId});

  factory DanoPorCalificar.fromJson(Map<String, dynamic> json) => DanoPorCalificar(
        hallazgoId: json['hallazgo_id'] as String,
        vin: json['vin'] as String? ?? '',
        parte: json['parte'] as String?,
        clase: json['clase_dano'] as String?,
        evidenciaId: json['evidencia_id'] as String?,
      );

  final String hallazgoId;
  final String vin;

  /// Dónde está el daño, como lo escribe el servidor.
  final String? parte;

  /// Código de `clases-dano`; null si el conductor no lo eligió.
  final String? clase;
  final String? evidenciaId;
}

/// Los valores por defecto de X e Y si el servidor no los manda: los mismos del contrato.
const esperaAvisoMinutosPorDefecto = 15;
const esperaPlanBMinutosPorDefecto = 30;

/// El reloj de la espera, calculado con la hora de activación guardada y no con la pantalla (S8-HU-05.2 CA-3): sigue
/// contando con la pantalla cerrada y tras reiniciar la app, porque lo único que necesita es esa hora y los dos
/// plazos, que viajan con la copia de la parada o con la activación que espera señal.
///
/// Con señal manda lo que dice el servidor: sus plazos ya calculados, si ya avisó y si ya habilitó el plan B.
class RelojEspera {
  const RelojEspera({
    required this.activadaAt,
    required this.avisoDesde,
    required this.planBDesde,
    this.avisoHecho = false,
    this.planBDelServidor = false,
    this.motivo,
    this.motivoTexto,
    this.local = false,
    this.sinEnviar = false,
  });

  /// La espera que ya tiene el servidor, o la que anotó este teléfono en la copia de la parada.
  factory RelojEspera.delServidor(EsperaDelPunto espera) => RelojEspera(
        activadaAt: espera.activadaAt,
        avisoDesde: espera.avisoDesde,
        planBDesde: espera.planBDesde,
        avisoHecho: espera.avisoAt != null,
        planBDelServidor: espera.planBHabilitado,
        motivo: espera.motivo,
        motivoTexto: espera.motivoTexto,
        local: espera.deEsteTelefono,
      );

  /// La activación que todavía no llegó al servidor: cuenta desde la hora del celular con los X e Y que trajo la
  /// parada, que es exactamente lo que hará el servidor al recibirla (ESC-04).
  factory RelojEspera.local({
    required DateTime activadaAt,
    required int avisoMinutos,
    required int planBMinutos,
    String? motivo,
    String? motivoTexto,
    bool sinEnviar = true,
  }) {
    final desde = activadaAt.toUtc();
    return RelojEspera(
      activadaAt: desde,
      avisoDesde: desde.add(Duration(minutes: avisoMinutos)),
      planBDesde: desde.add(Duration(minutes: avisoMinutos + planBMinutos)),
      motivo: motivo,
      motivoTexto: motivoTexto,
      local: true,
      sinEnviar: sinEnviar,
    );
  }

  final DateTime activadaAt;
  final DateTime avisoDesde;
  final DateTime planBDesde;

  /// El servidor ya avisó al coordinador.
  final bool avisoHecho;

  /// El servidor ya dijo que el plan B está habilitado.
  final bool planBDelServidor;
  final String? motivo;
  final String? motivoTexto;

  /// El reloj es el del celular: la activación todavía no la confirmó el servidor.
  final bool local;

  /// La activación sigue en la cola de este teléfono, esperando señal.
  final bool sinEnviar;

  /// Con la espera del servidor manda el servidor: el coordinador fue avisado cuando dice `aviso_at`, aunque el
  /// teléfono ya haya pasado la hora (S8-HU-05.2 CA-3). Solo el reloj del teléfono lo da por hecho con la hora.
  bool avisado(DateTime ahora) => avisoHecho || (local && !ahora.toUtc().isBefore(avisoDesde));

  /// La espera del servidor ya pasó la hora del aviso y el servidor aún no dice que avisó: hay que preguntarle.
  bool avisoPorConfirmar(DateTime ahora) => !local && !avisoHecho && !ahora.toUtc().isBefore(avisoDesde);

  bool planBHabilitado(DateTime ahora) => planBDelServidor || !ahora.toUtc().isBefore(planBDesde);

  /// Los minutos que faltan, redondeados hacia arriba: «en 12 min» mientras queden 11 y algo. Nunca cero mientras
  /// falte algo, que se leería como «ya».
  static int minutosHasta(DateTime ahora, DateTime hasta) {
    final segundos = hasta.difference(ahora.toUtc()).inSeconds;
    if (segundos <= 0) return 0;
    return (segundos + 59) ~/ 60;
  }

  int minutosParaAviso(DateTime ahora) => avisado(ahora) ? 0 : minutosHasta(ahora, avisoDesde);

  int minutosParaPlanB(DateTime ahora) => planBHabilitado(ahora) ? 0 : minutosHasta(ahora, planBDesde);

  /// CA-3: lo que dice el reloj del aviso al coordinador.
  String textoAviso(DateTime ahora) => switch (minutosParaAviso(ahora)) {
        _ when avisado(ahora) => 'El coordinador ya fue avisado',
        0 => 'Avisaremos al coordinador en un momento',
        final minutos => 'Avisaremos al coordinador en $minutos min',
      };

  /// CA-3: lo que dice el reloj del plan B.
  String textoPlanB(DateTime ahora) => planBHabilitado(ahora)
      ? 'Ya puedes cerrar sin inspector del punto'
      : 'Podrás cerrar sin inspector del punto en ${minutosParaPlanB(ahora)} min';
}

/// El reloj que vale ahora en la parada, o null si no hay ninguna espera corriendo.
///
/// Si un inspector tomó la entrega, la espera desaparece aunque ya haya pasado el plazo (CA-5, ESC-07). Primero
/// cuenta la espera abierta del servidor; si no la hay, la activación que este teléfono tiene en la cola esperando
/// señal ([activacion]), que el servidor contará desde la misma hora. [activacion] también puede ser la que acaba
/// de salir de la cola mientras la parada se vuelve a pedir ([sinEnviar] en falso): sigue contando con ella hasta
/// que la parada traiga la espera abierta.
RelojEspera? relojDeLaParada(EntregaPunto punto, {SinInspectorParadaJob? activacion, bool sinEnviar = true}) {
  if (punto.receptor == ReceptorParada.inspectorDelPunto) return null;
  if (punto.entregada || punto.porEntregar.isEmpty) return null;
  if (punto.espera case final espera? when espera.abierta) return RelojEspera.delServidor(espera);
  if (activacion != null && !activacion.fallido) {
    final activada = DateTime.tryParse(activacion.fechaMomento);
    if (activada == null) return null;
    return RelojEspera.local(
      activadaAt: activada,
      avisoMinutos: activacion.avisoMinutos,
      planBMinutos: activacion.planBMinutos,
      motivo: activacion.motivo,
      motivoTexto: activacion.motivoTexto,
      sinEnviar: sinEnviar,
    );
  }
  return null;
}

/// S8-HU-05.2 CA-1: «No hay inspector» sale solo al conductor del viaje, en una parada con inspectores, después de
/// su «Llegué» en ella y mientras nadie haya tomado la entrega y no haya ya una espera corriendo.
bool ofreceNoHayInspector(EntregaPunto punto, {required String? rol, RelojEspera? reloj}) =>
    rol == 'conductor' &&
    punto.tieneInspector &&
    punto.conductorLlego &&
    punto.receptor == ReceptorParada.sinReceptor &&
    !punto.entregada &&
    punto.porEntregar.isNotEmpty &&
    reloj == null;

/// Lo que dice la ficha del carro en vez de «Capturar».
const mensajeLlegadaDelInspector = 'La revisión de llegada la hace el inspector del punto';

/// S8-HU-05.2 CA-9 (C10): por qué el conductor no captura la revisión de llegada del descargue de un carro, o null si
/// sí puede. En una parada con inspectores la hace el inspector del punto hasta que el plan B se habilite; en una
/// sin inspectores, o sin saber cómo está la parada (sin señal y sin copia), no cambia nada: decide el servidor.
String? bloqueoDeLaLlegadaDelConductor(EntregaPunto? punto, {SinInspectorParadaJob? activacion, required DateTime ahora}) {
  if (punto == null || !punto.tieneInspector) return null;
  final reloj = relojDeLaParada(punto, activacion: activacion);
  if (reloj == null) return mensajeLlegadaDelInspector;
  if (reloj.planBHabilitado(ahora)) return null;
  return '$mensajeLlegadaDelInspector: podrás hacerla sin él en ${reloj.minutosParaPlanB(ahora)} min';
}
