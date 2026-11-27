import '../../../core/util/formato.dart';
import 'aviso.dart';

/// A dónde lleva un aviso al tocarlo (S8-HU-09.2 CA-6). Lo decide el `tipo` con los ids del aviso y de sus `datos`,
/// nunca el texto del mensaje: el texto lo puede reescribir el servidor sin avisar.
///
/// Es la misma decisión para la bandeja y para el push (CA-5): el toque de una notificación trae el `tipo` y los
/// mismos ids, y con [destinoDeAviso] abre lo mismo que abriría desde la bandeja.
sealed class DestinoAviso {
  const DestinoAviso();

  /// La ruta de la app, o null si no hay una pantalla a la cual ir: entonces el aviso se lee en la bandeja.
  String? get ruta;

  /// Lo que dice el botón que lleva ahí.
  String get etiqueta;
}

/// El viaje (`viaje_id`): «Fuera del rango» del reclamo, «Viaje sin movimiento»...
class DestinoViaje extends DestinoAviso {
  const DestinoViaje(this.viajeId);

  final String viajeId;

  @override
  String get ruta => '/viajes/viaje/${Uri.encodeComponent(viajeId)}';

  @override
  String get etiqueta => 'Ver el viaje';
}

/// La unidad (`vin`), dentro de su viaje si el aviso lo dice.
class DestinoUnidad extends DestinoAviso {
  const DestinoUnidad(this.vin, {this.viajeId});

  final String vin;
  final String? viajeId;

  @override
  String get ruta => [
        '/inspecciones/vin/${Uri.encodeComponent(vin)}',
        if (viajeId != null) 'viajeId=${Uri.encodeQueryComponent(viajeId!)}',
      ].join('?');

  @override
  String get etiqueta => 'Ver la unidad';
}

/// El hallazgo (`hallazgo_id`): se abre en la ficha de su unidad, que es donde se ve con su foto y su zona.
class DestinoHallazgo extends DestinoAviso {
  const DestinoHallazgo(this.hallazgoId, {required this.vin, this.viajeId});

  final String hallazgoId;
  final String vin;
  final String? viajeId;

  @override
  String get ruta => '/inspecciones/vin/${Uri.encodeComponent(vin)}?${[
        if (viajeId != null) 'viajeId=${Uri.encodeQueryComponent(viajeId!)}',
        'hallazgo=${Uri.encodeQueryComponent(hallazgoId)}',
      ].join('&')}';

  @override
  String get etiqueta => 'Ver el hallazgo';
}

/// El lugar (`lugar_id`): «Ubicación fijada» y la propuesta de ubicación. La app no tiene pantalla de lugares —la
/// ubicación se confirma en el catálogo del portal—, así que el aviso se queda en la bandeja con sus datos.
class DestinoLugar extends DestinoAviso {
  const DestinoLugar(this.lugarId, {this.lugar});

  final String lugarId;
  final String? lugar;

  @override
  String? get ruta => null;

  @override
  String get etiqueta => lugar ?? 'El lugar';
}

/// A dónde lleva un aviso de [tipo] con estos ids. Los ids van en el aviso (`viaje_id`, `vin`, `hallazgo_id`) o en
/// sus [datos] (`viaje_id`, `vin`, `hallazgo_id`, `lugar_id`); los del aviso mandan.
///
/// Null cuando no hay a dónde ir: un tipo que la app no conoce (se abre en la bandeja sin romper) o uno sin ids.
DestinoAviso? destinoDeAviso({
  required String tipo,
  String? viajeId,
  String? vin,
  String? hallazgoId,
  Map<String, dynamic>? datos,
}) {
  if (!tiposDeAviso.contains(tipo)) return null;
  String? deDatos(String clave) => switch (datos?[clave]) { final String s when s.isNotEmpty => s, _ => null };
  final viaje = viajeId ?? deDatos('viaje_id');
  final unidad = vin ?? deDatos('vin');
  final hallazgo = hallazgoId ?? deDatos('hallazgo_id');
  final lugar = deDatos('lugar_id');
  final aLugar = lugar == null ? null : DestinoLugar(lugar, lugar: deDatos('lugar'));
  final aViaje = viaje == null ? null : DestinoViaje(viaje);
  return switch (tipo) {
    // Avisos de un lugar: no hay viaje ni unidad de por medio.
    tipoUbicacionFijada || tipoPropuestaUbicacion => aLugar,
    // El del reclamo de la entrega es del viaje entero (quién la tomó y desde dónde); el de las fotos, de la unidad.
    tipoFueraDeRango when deDatos('hecho') == 'reclamo_entrega' || unidad == null => aViaje ?? aLugar,
    _ when hallazgo != null && unidad != null => DestinoHallazgo(hallazgo, vin: unidad, viajeId: viaje),
    _ when unidad != null => DestinoUnidad(unidad, viajeId: viaje),
    _ => aViaje ?? aLugar,
  };
}

extension DestinoDelAviso on Aviso {
  DestinoAviso? get destino =>
      destinoDeAviso(tipo: tipo, viajeId: viajeId, vin: vin, hallazgoId: hallazgoId, datos: datos);
}

/// Los datos propios de «Ubicación fijada» y de «Fuera del rango», una línea por dato, para leerlos sin depender de
/// cómo redactó el mensaje el servidor (S8-HU-09.2 CA-6, ESC-07 y ESC-09). Vacío en los demás avisos.
List<String> lineasDeDatos(Aviso aviso) {
  final datos = aviso.datos;
  if (datos == null) return const [];
  String? texto(String clave) => switch (datos[clave]) { final String s when s.isNotEmpty => s, _ => null };
  num? numero(String clave) => switch (datos[clave]) { final num n => n, _ => null };
  final lugar = texto('lugar');
  final quien = texto('usuario_nombre');
  final precision = numero('precision_m');
  return switch (aviso.tipo) {
    tipoUbicacionFijada => [
        if (lugar != null) 'Lugar: $lugar',
        if (quien != null) 'La fijó: $quien',
        if (texto('fijada_at') case final cuando?) 'Cuándo: ${formatearFechaIso(cuando)}',
        if (precision != null) 'GPS con precisión ${metros(precision)}',
      ],
    tipoFueraDeRango => [
        if (lugar != null) 'Lugar: $lugar',
        if (quien != null) 'Tomó la entrega: $quien',
        if ((numero('distancia_m'), numero('radio_m')) case (final distancia?, final radio?))
          'A ${metros(distancia)} del lugar · rango ${metros(radio)}'
        else if (numero('distancia_m') case final distancia?)
          'A ${metros(distancia)} del lugar',
        if (precision != null) 'GPS con precisión ${metros(precision)}',
      ],
    _ => const [],
  };
}

/// «2.300 m»: metros enteros con el punto de miles, como se escriben en Colombia.
String metros(num valor) {
  final entero = valor.round().abs().toString();
  final grupos = <String>[];
  for (var fin = entero.length; fin > 0; fin -= 3) {
    grupos.insert(0, entero.substring(fin - 3 < 0 ? 0 : fin - 3, fin));
  }
  return '${valor.round() < 0 ? '-' : ''}${grupos.join('.')} m';
}
