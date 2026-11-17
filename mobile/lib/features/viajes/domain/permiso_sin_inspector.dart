/// El permiso para hacer el cargue sin inspector donde el punto **sí** tiene inspectores asignados.
///
/// Donde no los hay la excepción se activa en el momento, como siempre (S7-HU-06.2 CA-2). Donde los hay, el
/// conductor la pide con su motivo y espera: la concede un coordinador —y queda habilitado de inmediato— o, si
/// nadie responde en diez minutos, la concede el reloj y queda marcada como autorizada por tiempo.
///
/// Quien concede por tiempo es el servidor, con un proceso periódico suyo: la espera no depende de que esta
/// pantalla siga abierta ni de que la app siga viva.
class PermisoSinInspector {
  const PermisoSinInspector({
    required this.id,
    required this.viajeId,
    required this.estado,
    required this.solicitadoAt,
    required this.venceAt,
    required this.motivoCodigo,
    this.lugar,
    this.motivo,
    this.motivoTexto,
    this.fecha,
    this.lat,
    this.lng,
    this.concedidoAt,
    this.concedidoPorNombre,
    this.porTiempo = false,
  });

  factory PermisoSinInspector.fromJson(Map<String, dynamic> json) => PermisoSinInspector(
        id: json['id'] as String,
        viajeId: json['viaje_id'] as String,
        estado: EstadoPermiso.desde(json['estado'] as String?),
        solicitadoAt: _fecha(json['solicitado_at']) ?? DateTime.now().toUtc(),
        venceAt: _fecha(json['vence_at']) ?? DateTime.now().toUtc(),
        motivoCodigo: json['motivo_codigo'] as String? ?? '',
        lugar: json['lugar'] as String?,
        motivo: json['motivo'] as String?,
        motivoTexto: json['motivo_texto'] as String?,
        fecha: _fecha(json['fecha']),
        lat: (json['lat'] as num?)?.toDouble(),
        lng: (json['lng'] as num?)?.toDouble(),
        concedidoAt: _fecha(json['concedido_at']),
        concedidoPorNombre: json['concedido_por_nombre'] as String?,
        porTiempo: json['autorizado_por_tiempo'] as bool? ?? false,
      );

  static DateTime? _fecha(Object? valor) => valor is String ? DateTime.tryParse(valor)?.toUtc() : null;

  final String id;
  final String viajeId;
  final EstadoPermiso estado;

  /// Cuándo se pidió, y desde cuándo el reloj puede concederlo (diez minutos después).
  final DateTime solicitadoAt;
  final DateTime venceAt;

  /// El punto de cargue, para nombrarlo en la espera.
  final String? lugar;

  /// El motivo con el que se pidió: el código es lo que se vuelve a mandar al capturar, para no volver a
  /// preguntarlo, y el nombre es lo que se le enseña a la persona.
  final String motivoCodigo;
  final String? motivo;
  final String? motivoTexto;

  /// El momento y el lugar en que se pidió. Van con la inspección, no los de cuando por fin se captura.
  final DateTime? fecha;
  final double? lat;
  final double? lng;

  final DateTime? concedidoAt;
  final String? concedidoPorNombre;

  /// Nadie respondió y lo concedió el reloj.
  final bool porTiempo;

  bool get concedido => estado == EstadoPermiso.concedido;

  /// Cuánto falta para que el reloj lo conceda. Cero cuando el plazo ya pasó: el proceso del servidor lo
  /// concede enseguida, y faltar «menos de nada» no es algo que haya que enseñar.
  Duration faltaPara(DateTime ahora) {
    final falta = venceAt.difference(ahora.toUtc());
    return falta.isNegative ? Duration.zero : falta;
  }
}

enum EstadoPermiso {
  pendiente,
  concedido;

  static EstadoPermiso desde(String? valor) => valor == 'concedido' ? concedido : pendiente;
}

/// Qué dice el botón de la salida del cargue cuando al conductor le toca la excepción.
///
/// Son cuatro frases porque son cuatro situaciones distintas, y el conductor tiene que poder leer en el botón
/// qué va a pasar si lo toca: activar, pedir, esperar o seguir.
String etiquetaExcepcion({required bool cargueConInspector, PermisoSinInspector? permiso}) {
  if (!cargueConInspector) return 'No hay inspector';
  return switch (permiso) {
    null => 'Pedir permiso',
    PermisoSinInspector(concedido: true) => 'Continuar el cargue',
    _ => 'Ver el permiso',
  };
}
