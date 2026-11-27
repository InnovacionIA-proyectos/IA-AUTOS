import '../../inspecciones/domain/inspeccion_resumen.dart';
import '../../sync/domain/job.dart';
import '../../vehiculos/domain/inventario.dart';
import '../../viajes/domain/viaje.dart';

/// Qué parte firma la niñera quien mira (S8-HU-01.3 CA-5): entrega el inspector del viaje y recibe su conductor.
/// Null para cualquier otro: nadie firma por otro, y el servidor responde 403.
String? parteEnLaNinera(Viaje viaje, String? usuarioId) {
  if (usuarioId == null) return null;
  if (viaje.inspectorId == usuarioId) return ParteTransferencia.entrega;
  if (viaje.conductorId == usuarioId) return ParteTransferencia.recibe;
  return null;
}

/// Los carros que entran en la firma: los activos, por su puesto en la niñera (los que no tienen, al final). Un
/// carro que no sale o se bajó no se firma ni se le pide nada (ESC-04).
List<ViajeVehiculo> carrosDeLaFirma(Viaje viaje) {
  final activos = [...viaje.vehiculos.where((v) => v.sale)];
  activos.sort((a, b) => (a.posicion ?? 1 << 20).compareTo(b.posicion ?? 1 << 20));
  return activos;
}

/// Lo primero que le falta al cargue para poder firmar la niñera, con las palabras del servidor (CA-6), o null si
/// cada carro activo tiene la llegada y la salida del cargue cerradas.
///
/// Vale la **última versión** de cada informe, como en el servidor: una llegada cerrada con su corrección todavía
/// en borrador no está cerrada.
String? faltanteDelCargue(Viaje viaje, List<InspeccionResumen> informesDelCargue) {
  for (final carro in carrosDeLaFirma(viaje)) {
    bool cerrada(String momento) {
      InspeccionResumen? ultima;
      for (final i in informesDelCargue) {
        if (i.vin != carro.vin || i.momento != momento) continue;
        if (ultima == null || i.version > ultima.version) ultima = i;
      }
      return ultima?.estado == 'cerrada';
    }

    if (!cerrada('llegada')) return 'Falta la llegada del cargue del VIN ${carro.vin}';
    if (!cerrada('salida')) return 'Falta la salida del cargue del VIN ${carro.vin}';
  }
  return null;
}

/// La transferencia del cargue de un carro en [viajeId], la que lleva la firma de la niñera. Null si todavía no hay
/// ninguna (nadie ha firmado).
TransferenciaVista? transferenciaDelCargue(Inventario inventario, String viajeId) {
  final etapa = inventario.etapas.where((e) => e.proceso == 'cargue' && e.viajeId == viajeId).firstOrNull;
  if (etapa == null) return null;
  return switch (accionDeEtapa(etapa, inventario)) {
    FirmarTransferencia(:final transferencia) => transferencia,
    SinInforme() => null,
  };
}

/// Si [parte] ya firmó esta transferencia, o ya está cerrada de cualquier forma.
bool yaFirmo(TransferenciaVista? transferencia, String parte) {
  if (transferencia == null) return false;
  if (transferencia.cerrada) return true;
  return parte == ParteTransferencia.entrega ? transferencia.firmoEntrega : transferencia.firmoRecibe;
}

/// Lo que el servidor admite en las observaciones de una reserva.
const reservaMax = 1000;
