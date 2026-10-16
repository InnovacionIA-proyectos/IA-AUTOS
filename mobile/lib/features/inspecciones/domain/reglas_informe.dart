import '../../sync/domain/job.dart';
import 'foto_local.dart';
import 'hallazgo_local.dart';

/// Motivos por los que todavía no se puede cerrar un informe.
enum BloqueoCierre {
  sinUbicacion('Esperando ubicación…'),
  faltaTransporte('Ingresa el conductor y la placa.'),
  faltaFotoNovedad('Registraste una novedad: agrega al menos una foto.');

  const BloqueoCierre(this.mensaje);

  final String mensaje;
}

/// Reglas de cierre (iguales a las de la app original):
///  - ubicación disponible,
///  - conductor y placa (datos del transporte),
///  - fotos obligatorias SOLO si hay novedad (al menos una).
/// Devuelve null si se puede cerrar.
BloqueoCierre? bloqueoDeCierre({
  required bool hayUbicacion,
  required String conductor,
  required String placa,
  required int cantidadHallazgos,
  required int cantidadFotos,
}) {
  if (!hayUbicacion) return BloqueoCierre.sinUbicacion;
  if (conductor.trim().isEmpty || placa.trim().isEmpty) return BloqueoCierre.faltaTransporte;
  if (cantidadHallazgos > 0 && cantidadFotos == 0) return BloqueoCierre.faltaFotoNovedad;
  return null;
}

/// Arma el job de un informe cerrado a partir de lo capturado. El sello de cada foto ya viene en [fotos].
InformeJob construirInformeJob({
  required String id,
  required int creadoEn,
  required String vin,
  required String proceso,
  required String etapaId,
  required DateTime fecha,
  required double lat,
  required double lng,
  required String conductor,
  required String placa,
  String trailer = '',
  String notas = '',
  required List<FotoLocal> fotos,
  required List<HallazgoLocal> hallazgos,
}) {
  String? opcional(String valor) => valor.trim().isEmpty ? null : valor.trim();
  return InformeJob(
    id: id,
    etiqueta: 'Informe $vin · $proceso',
    creadoEn: creadoEn,
    etapaId: etapaId,
    momento: 'informe',
    fecha: fecha.toUtc().toIso8601String(),
    lat: lat,
    lng: lng,
    conductorNombre: conductor.trim(),
    placa: placa.trim(),
    trailer: opcional(trailer),
    notas: opcional(notas),
    fotos: fotos,
    hallazgos: hallazgos,
  );
}

/// Una firma válida exige que quien entrega y quien recibe sean personas distintas y que haya al menos un trazo.
/// (El servidor valida además que quien firma sea el usuario autenticado.)
bool firmaValida({required String? entregaId, required String? recibeId, required bool hayFirma}) =>
    entregaId != null && recibeId != null && entregaId != recibeId && hayFirma;
