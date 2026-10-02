import '../../sync/domain/job.dart';

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

class InspeccionVista {
  const InspeccionVista({
    required this.id,
    required this.momento,
    required this.fecha,
    required this.estado,
    this.notas,
    this.hallazgos = const [],
    this.evidencias = const [],
    this.transferenciaFirmada = false,
  });

  factory InspeccionVista.fromJson(Map<String, dynamic> json) => InspeccionVista(
        id: json['id'] as String,
        momento: json['momento'] as String,
        fecha: DateTime.parse(json['fecha'] as String),
        estado: json['estado'] as String,
        notas: json['notas'] as String?,
        hallazgos: [for (final h in (json['hallazgos'] as List<dynamic>? ?? [])) HallazgoVista.fromJson(h as Map<String, dynamic>)],
        evidencias: [for (final e in (json['evidencias'] as List<dynamic>? ?? [])) EvidenciaVista.fromJson(e as Map<String, dynamic>)],
        transferenciaFirmada: ((json['transferencia'] as Map<String, dynamic>?)?['firmada'] as bool?) ?? false,
      );

  final String id;
  final String momento;
  final DateTime fecha;
  final String estado;
  final String? notas;
  final List<HallazgoVista> hallazgos;
  final List<EvidenciaVista> evidencias;
  final bool transferenciaFirmada;

  bool get cerrada => estado == 'cerrada';
}

class EtapaVista {
  const EtapaVista({required this.id, required this.orden, required this.proceso, required this.requiereInspeccion, this.inspecciones = const []});

  factory EtapaVista.fromJson(Map<String, dynamic> json) => EtapaVista(
        id: json['id'] as String,
        orden: json['orden'] as int,
        proceso: json['proceso'] as String,
        requiereInspeccion: json['requiere_inspeccion'] as bool,
        inspecciones: [for (final i in (json['inspecciones'] as List<dynamic>? ?? [])) InspeccionVista.fromJson(i as Map<String, dynamic>)],
      );

  final String id;
  final int orden;
  final String proceso;
  final bool requiereInspeccion;
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

/// Qué se puede hacer hoy en una etapa de la ruta de custodia.
sealed class AccionEtapa {
  const AccionEtapa();
}

/// La etapa requiere informe y aún no hay: hacer uno nuevo.
class CapturarInforme extends AccionEtapa {
  const CapturarInforme();
}

/// Hay un informe sobre el que firmar la transferencia. [soloFirma] = la etapa no exige informe propio y se firma
/// sobre el último informe del VIN.
class FirmarTransferencia extends AccionEtapa {
  const FirmarTransferencia({required this.inspeccionId, required this.soloFirma, required this.firmada});

  final String inspeccionId;
  final bool soloFirma;
  final bool firmada;
}

/// No hay nada sobre lo que firmar todavía.
class SinInforme extends AccionEtapa {
  const SinInforme();
}

AccionEtapa accionDeEtapa(EtapaVista etapa, Inventario inventario) {
  final inspecciones = etapa.inspecciones;
  final referencia = inspecciones.where((i) => i.cerrada).firstOrNull ?? inspecciones.firstOrNull;
  if (etapa.requiereInspeccion) {
    if (referencia == null) return const CapturarInforme();
    return FirmarTransferencia(inspeccionId: referencia.id, soloFirma: false, firmada: referencia.transferenciaFirmada);
  }
  final base = referencia ?? inventario.ultimaInspeccion?.inspeccion;
  if (base == null) return const SinInforme();
  return FirmarTransferencia(inspeccionId: base.id, soloFirma: true, firmada: base.transferenciaFirmada);
}
