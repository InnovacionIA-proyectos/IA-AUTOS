import '../../sync/domain/job.dart';
import '../../viajes/domain/viaje.dart';
import 'acomodacion.dart';
import 'checklist_vistas.dart';
import 'extra_local.dart';
import 'foto_local.dart';
import 'hallazgo_local.dart';

/// Por encima de esta precisión del GPS (en metros) se avisa, sin bloquear.
///
/// Una foto sellada con 200 metros de margen no prueba dónde se tomó. No impide cerrar porque en un patio
/// techado el GPS siempre es malo y bloquear dejaría a la gente sin poder trabajar; se avisa para que, si se
/// puede, salgan a cielo abierto.
///
/// Es el valor inicial, el mismo que el backend trae en el parámetro `gps_precision_minima_m`. La app usa el que
/// lee de `GET /api/parametros/app` (o su copia local); este solo manda si nunca pudo leer ninguno (S8-HU-09.2 CA-1).
const precisionBajaMetrosPorDefecto = 30.0;

/// Por qué todavía no se puede cerrar un informe.
///
/// Son las mismas reglas que impone el servidor al cerrar. Se comprueban aquí para que la persona lo sepa en el
/// patio, con el vehículo delante, y no cuando el envío falle horas después.
sealed class BloqueoCierre {
  const BloqueoCierre();

  String get mensaje;
}

/// Sin catálogos no se pueden registrar novedades, y un informe cerrado sin ellas diría que el vehículo venía
/// sano. Por eso bloquea el cierre en vez de dejar pasar un informe vacío.
class SinCatalogos extends BloqueoCierre {
  const SinCatalogos();

  @override
  String get mensaje => 'No se han descargado los catálogos de daño. Conéctate para descargarlos.';
}

class SinUbicacion extends BloqueoCierre {
  const SinUbicacion();

  @override
  String get mensaje => 'Esperando ubicación…';
}

/// Faltan fotos de vistas obligatorias. Se nombran: «falta una foto» no dice cuál ni cuántas.
class FaltanVistas extends BloqueoCierre {
  const FaltanVistas(this.vistas);

  final List<VistaChecklist> vistas;

  @override
  String get mensaje => vistas.length == 1
      ? 'Falta la foto de la vista «${vistas.single.nombre}».'
      : 'Faltan ${vistas.length} fotos obligatorias: ${vistas.map((v) => v.nombre).join(', ')}.';
}

/// Hay fotos sin sello de ubicación. No se cierra con ellas: el sello es lo que da valor a la evidencia, y
/// ponerle una ubicación anterior sería inventarla.
class FotosSinUbicacion extends BloqueoCierre {
  const FotosSinUbicacion(this.cuantas);

  final int cuantas;

  @override
  String get mensaje => cuantas == 1
      ? 'Una foto quedó sin ubicación: repítela antes de cerrar.'
      : '$cuantas fotos quedaron sin ubicación: repítelas antes de cerrar.';
}

/// Una novedad sin su foto es la palabra de alguien contra la del siguiente, y el servidor la rechaza.
class HallazgoSinFoto extends BloqueoCierre {
  const HallazgoSinFoto(this.zona);

  final String zona;

  @override
  String get mensaje => 'La novedad de «$zona» necesita su foto.';
}

/// Un extra sin foto no prueba nada: es la misma regla que impone el servidor al cerrar.
class ExtraSinFoto extends BloqueoCierre {
  const ExtraSinFoto(this.nombre);

  final String nombre;

  @override
  String get mensaje => "El extra '$nombre' no tiene foto";
}

/// No se sabe qué unidades tiene el viaje, y sin ellas no se puede comprobar que cada una tenga su lugar.
class SinUnidadesDelViaje extends BloqueoCierre {
  const SinUnidadesDelViaje();

  @override
  String get mensaje => 'No se pudieron cargar las unidades del viaje. Conéctate e inténtalo de nuevo.';
}

/// Una unidad que sigue en el viaje y no tiene lugar en la niñera. Es la regla del servidor al cerrar la segunda
/// inspección del cargue, con sus mismas palabras.
///
/// [desdeLaNinera]: es otro carro, que en la salida de este no se acomoda; se dice dónde se le pone el puesto.
class FaltaPosicion extends BloqueoCierre {
  const FaltaPosicion(this.vin, {this.desdeLaNinera = false});

  final String vin;
  final bool desdeLaNinera;

  @override
  String get mensaje =>
      desdeLaNinera ? 'Falta la posición de la unidad $vin: ponla desde la pantalla de la niñera' : 'Falta la posición de la unidad $vin';
}

/// El lugar de cargue tiene muelles y el viaje todavía no tiene el suyo. Mismas palabras que el servidor al cerrar.
class FaltaMuelle extends BloqueoCierre {
  const FaltaMuelle();

  @override
  String get mensaje => 'Falta el muelle del viaje';
}

class FaltanFotosDeAcomodacion extends BloqueoCierre {
  const FaltanFotosDeAcomodacion({required this.minimas, required this.hay});

  final int minimas;
  final int hay;

  @override
  String get mensaje => 'Faltan fotos de la acomodación: se exige al menos $minimas y hay $hay';
}

/// Reglas de cierre, en el mismo orden en que conviene resolverlas. Devuelve null si se puede cerrar.
///
/// [checklist] vacío significa que la etapa no pide vistas (Transporte): ahí lo único que se exige es la
/// ubicación del registro.
BloqueoCierre? bloqueoDeCierre({
  required bool hayUbicacion,
  required List<FotoLocal> fotos,
  required List<HallazgoLocal> hallazgos,
  List<ExtraLocal> extras = const [],
  List<VistaChecklist> checklist = const [],
  bool catalogosListos = true,
  Acomodacion? acomodacion,
  Set<String> vistasEnElServidor = const {},
}) {
  if (!catalogosListos) return const SinCatalogos();
  if (!hayUbicacion) return const SinUbicacion();

  // Al continuar un borrador (S8-HU-11.2 CA-2), la vista que ya tiene su foto en el servidor no se vuelve a pedir.
  final faltan = vistasObligatoriasSinFoto(checklist, fotos, enElServidor: vistasEnElServidor);
  if (faltan.isNotEmpty) return FaltanVistas(faltan);

  if (acomodacion != null) {
    if (acomodacion.unidades == null) return const SinUnidadesDelViaje();
    // El servidor exige el puesto de todos los carros del viaje para cerrar la salida de cualquiera. Primero el de
    // este carro, que se pone aquí; los demás se ponen desde la niñera.
    final sin = vinesSinPosicion(acomodacion.unidades!, acomodacion.posiciones);
    final propio = acomodacion.vinPropio;
    if (propio != null && sin.contains(propio)) return FaltaPosicion(propio);
    if (sin.isNotEmpty) return FaltaPosicion(sin.first, desdeLaNinera: propio != null);
    if (acomodacion.exigeMuelle && acomodacion.muelleId == null) return const FaltaMuelle();
    // Por viaje (S8-HU-01.3 CA-3): el servidor cuenta las fotos de todas las salidas del cargue del viaje, así
    // que una foto de la niñera sirve para todos los carros. Las que el viaje ya tiene más las de esta pantalla.
    final hay = fotosDeAcomodacion(fotos).length + acomodacion.fotosDelViaje;
    if (hay < acomodacion.fotosMinimas) return FaltanFotosDeAcomodacion(minimas: acomodacion.fotosMinimas, hay: hay);
  }

  for (final hallazgo in hallazgos) {
    if (hallazgo.foto == null) return HallazgoSinFoto(hallazgo.zona);
  }
  for (final extra in extras) {
    if (extra.fotos.isEmpty) return ExtraSinFoto(extra.nombre);
  }

  // Se cuentan todas, incluidas las de los hallazgos: cualquier evidencia sin sello es evidencia floja.
  final sinSello = [...fotos, ...hallazgos.map((h) => h.foto), for (final e in extras) ...e.fotos]
      .whereType<FotoLocal>()
      .where((f) => f.sinUbicacion);
  if (sinSello.isNotEmpty) return FotosSinUbicacion(sinSello.length);

  return null;
}

/// Lo que la segunda inspección del cargue exige además de la ubicación: cada unidad con su lugar y las fotos de la
/// acomodación. [unidades] es null mientras no se conozca el viaje.
class Acomodacion {
  const Acomodacion({
    required this.unidades,
    required this.posiciones,
    required this.fotosMinimas,
    this.fotosDelViaje = 0,
    this.exigeMuelle = false,
    this.muelleId,
    this.vinPropio,
  });

  /// El carro de la inspección: el único que se acomoda en su salida. Null, cualquiera (sin pantalla de por medio).
  final String? vinPropio;

  final List<ViajeVehiculo>? unidades;
  final Map<String, int> posiciones;
  final int fotosMinimas;

  /// Las fotos de la acomodación que el viaje ya tiene en el servidor, tomadas en la de cualquier carro.
  final int fotosDelViaje;

  /// El lugar de cargue tiene al menos un muelle activo: entonces el viaje necesita el suyo para cerrar.
  final bool exigeMuelle;

  /// El muelle que tiene el viaje ahora.
  final String? muelleId;
}

/// Fotos cuyo sello tiene poca precisión. No bloquean: se avisa.
List<FotoLocal> fotosConPrecisionBaja(List<FotoLocal> fotos, {double? umbral}) =>
    [...fotos.where((f) => (f.precision ?? 0) > (umbral ?? precisionBajaMetrosPorDefecto))];

/// Arma el job de un informe cerrado a partir de lo capturado. El sello de cada foto ya viene en [fotos].
InformeJob construirInformeJob({
  required String id,
  required int creadoEn,
  required String usuarioId,
  required String momento,
  required String vin,
  required String proceso,
  required String etapaId,
  required DateTime fecha,
  required double lat,
  required double lng,
  String notas = '',
  required List<FotoLocal> fotos,
  required List<HallazgoLocal> hallazgos,
  List<ExtraLocal> extras = const [],
  String? viajeId,
  Map<String, int> posiciones = const {},
  Map<String, int> posicionesPrevias = const {},
  String? motivoSinInspector,
  String? motivoSinInspectorTexto,
  bool bajoPermiso = false,
  String? corrigeInspeccionId,
  String? inspeccionId,
}) {
  String? opcional(String valor) => valor.trim().isEmpty ? null : valor.trim();
  return InformeJob(
    id: id,
    // La etiqueta es lo que se lee en la cola: una corrección y el informe que corrige se distinguen ahí.
    etiqueta: corrigeInspeccionId == null ? 'Informe $vin · $proceso' : 'Corrección $vin · $proceso',
    creadoEn: creadoEn,
    usuarioId: usuarioId,
    etapaId: etapaId,
    momento: momento,
    fecha: fecha.toUtc().toIso8601String(),
    lat: lat,
    lng: lng,
    notas: opcional(notas),
    fotos: fotos,
    hallazgos: hallazgos,
    extras: extras,
    viajeId: viajeId,
    posiciones: posiciones,
    posicionesPrevias: posicionesPrevias,
    motivoSinInspector: motivoSinInspector,
    motivoSinInspectorTexto: motivoSinInspectorTexto,
    bajoPermiso: bajoPermiso,
    corrigeInspeccionId: corrigeInspeccionId,
    // S8-HU-11.2 CA-2: continuar un borrador del servidor arranca el envío con su id. La cola no crea otro informe
    // y solo sube lo que se agregó aquí: lo que ya estaba allá no viaja en el job.
    inspeccionId: inspeccionId,
  );
}

/// El trazo que se manda al firmar: el de **quien tiene la sesión**.
///
/// El servidor atribuye la firma al usuario del token (`transferencia.Firmar(usuarioAutenticado, firma)`), así
/// que solo viaja una y tiene que ser la suya. Quien firma debe ser una de las dos partes, y entrega y recibe
/// tienen que ser personas distintas.
///
/// Devuelve null cuando todavía no se puede firmar.
String? firmaDeLaSesion({
  required String? yoId,
  required String? entregaId,
  required String? recibeId,
  String? firmaEntrega,
  String? firmaRecibe,
}) {
  if (yoId == null || entregaId == null || recibeId == null || entregaId == recibeId) return null;
  final mia = switch (yoId) {
    _ when yoId == entregaId => firmaEntrega,
    _ when yoId == recibeId => firmaRecibe,
    _ => null,
  };
  return mia == null || mia.isEmpty ? null : mia;
}
