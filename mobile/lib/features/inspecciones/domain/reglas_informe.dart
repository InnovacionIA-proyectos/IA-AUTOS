import '../../sync/domain/job.dart';
import 'checklist_vistas.dart';
import 'extra_local.dart';
import 'foto_local.dart';
import 'hallazgo_local.dart';

/// Por encima de esta precisión del GPS (en metros) se avisa, sin bloquear.
///
/// Una foto sellada con 200 metros de margen no prueba dónde se tomó. No impide cerrar porque en un patio
/// techado el GPS siempre es malo y bloquear dejaría a la gente sin poder trabajar; se avisa para que, si se
/// puede, salgan a cielo abierto. **Valor inicial, pendiente de confirmar con Calidad.**
const precisionBajaMetros = 30.0;

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
}) {
  if (!catalogosListos) return const SinCatalogos();
  if (!hayUbicacion) return const SinUbicacion();

  final faltan = vistasObligatoriasSinFoto(checklist, fotos);
  if (faltan.isNotEmpty) return FaltanVistas(faltan);

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

/// Fotos cuyo sello tiene poca precisión. No bloquean: se avisa.
List<FotoLocal> fotosConPrecisionBaja(List<FotoLocal> fotos) =>
    [...fotos.where((f) => (f.precision ?? 0) > precisionBajaMetros)];

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
}) {
  String? opcional(String valor) => valor.trim().isEmpty ? null : valor.trim();
  return InformeJob(
    id: id,
    etiqueta: 'Informe $vin · $proceso',
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
