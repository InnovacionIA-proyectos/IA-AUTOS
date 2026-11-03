/// El dibujo de daños: los paneles (las vistas del vehículo donde se toca), las zonas (cuadrante + lado) y las
/// regiones (el polígono de una zona en un panel).
///
/// Dart puro: se arma igual desde la copia que viene en los assets que desde el catálogo del servidor, y así se
/// prueba sin Flutter.
///
/// **El lado sale de los datos, nunca del número.** Cada zona trae su `lado` (o lo trae vacío si es central); aquí
/// no hay ningún rango escrito, porque la numeración la decide Calidad y ya cambió una vez.
library;

import 'dart:typed_data';

import 'geometria.dart';

export 'geometria.dart' show Punto;

/// Valores del contrato para el lado de una zona (los mismos que acepta `POST /api/inspecciones/{id}/hallazgos`).
abstract final class LadoZona {
  static const izquierdo = 'izquierdo';
  static const derecho = 'derecho';
}

/// Clave única de una zona: número más `I` o `D`, o el número solo si es central («26D», «26I», «54»).
///
/// Es la que se usa en toda la app para comparar: la selección, «un daño por cuadrante», las regiones de cada panel
/// y los hallazgos ya registrados. Se arma siempre aquí para que no haya dos formas de escribirla.
String claveDeZona(int numero, String? lado) => switch (lado) {
      LadoZona.izquierdo => '${numero}I',
      LadoZona.derecho => '${numero}D',
      _ => '$numero',
    };

/// Una zona del vehículo: lo que se guarda en el hallazgo (`cuadrante_numero` + `lado`).
class ZonaDano {
  const ZonaDano({
    required this.numero,
    required this.lado,
    required this.nombre,
    required this.nombreCompleto,
    this.aiagArea,
  });

  final int numero;

  /// `izquierdo`, `derecho` o null en las centrales. Viaja tal cual en `lado`.
  final String? lado;

  /// Nombre de la pieza sin el lado («Llanta trasera»).
  final String nombre;

  /// Nombre para mostrar, con el lado («Llanta trasera derecha»). Es el título de la ficha y del hallazgo.
  final String nombreCompleto;

  /// Código de área AIAG-ECG equivalente; informativo.
  final String? aiagArea;

  String get clave => claveDeZona(numero, lado);

  /// «Cuadrante 26, lado derecho · AIAG 74-75 / 76-77» o «Cuadrante 54». El AIAG solo si la zona lo trae: el
  /// catálogo del servidor todavía puede no publicarlo.
  String get descripcion {
    final ubicacion = switch (lado) {
      null => 'Cuadrante $numero',
      final l => 'Cuadrante $numero, lado $l',
    };
    return switch (aiagArea) {
      final area? when area.isNotEmpty => '$ubicacion · AIAG $area',
      _ => ubicacion,
    };
  }
}

/// De dónde sale la imagen de fondo de un panel: un asset de la app o los bytes descargados del servidor.
class ImagenPanel {
  const ImagenPanel.asset(String this.rutaAsset) : bytes = null;
  const ImagenPanel.bytes(Uint8List this.bytes) : rutaAsset = null;

  final String? rutaAsset;
  final Uint8List? bytes;
}

/// Una vista del vehículo donde se toca la zona del daño. No es una vista de foto: las 7 guías de cámara son otra
/// cosa y no se tocan.
class PanelDano {
  const PanelDano({
    required this.codigo,
    required this.nombre,
    required this.anchoRef,
    required this.altoRef,
    required this.imagen,
  });

  final String codigo;

  /// Lo que dice la pestaña.
  final String nombre;

  /// Tamaño de la imagen sobre la que se calcularon los polígonos. El widget dibuja con esta proporción y
  /// `BoxFit.fill`: si la imagen se recorta o se deforma, los polígonos quedan corridos.
  final double anchoRef;
  final double altoRef;

  final ImagenPanel imagen;

  double get proporcion => anchoRef / altoRef;
}

/// El polígono de una zona en un panel.
class RegionDano {
  RegionDano({
    required this.panel,
    required this.zona,
    required this.prioridad,
    required this.puntos,
    required double anchoRef,
    required double altoRef,
  }) : puntosRef = [for (final p in puntos) (x: p.x * anchoRef, y: p.y * altoRef)];

  /// Código del panel.
  final String panel;

  final ZonaDano zona;

  /// Viene en el JSON (hoy todas en 1). Se conserva pero no se usa para ordenar: ver la guía de integración.
  final int prioridad;

  /// Puntos normalizados (0..1) sobre la imagen del panel, como vienen en los datos. Son los que se pintan.
  final List<Punto> puntos;

  /// Los mismos puntos en píxeles de la imagen de referencia. Son los que se usan para resolver el toque.
  final List<Punto> puntosRef;

  String get clave => zona.clave;

  late final ({Punto punto, double radio}) _interior = puntoInterior(puntosRef);

  /// Dónde va el número, en píxeles de referencia.
  Punto get puntoEtiqueta => _interior.punto;

  /// Distancia del punto de la etiqueta al borde, en píxeles de referencia: el «tamaño» de la pieza.
  double get radioInterior => _interior.radio;
}

/// De dónde salió el dibujo vigente. Se muestra en pruebas y sirve para diagnosticar en campo.
enum OrigenDibujo { assets, servidor }

class DibujoDano {
  DibujoDano._({
    required this.version,
    required this.origen,
    required this.paneles,
    required this._zonas,
    required this._regiones,
  });

  /// Arma el dibujo y comprueba que sea coherente: toda región apunta a una zona que existe y a un panel que existe,
  /// una zona no se repite y una zona no se dibuja dos veces en el mismo panel. Un dibujo incoherente lanza
  /// [FormatException]: es preferible caer a la copia de los assets que dibujar a medias.
  factory DibujoDano.armar({
    required String version,
    required OrigenDibujo origen,
    required List<PanelDano> paneles,
    required List<ZonaDano> zonas,
    required List<({String panel, int numero, String? lado, int prioridad, List<Punto> puntos})> regiones,
  }) {
    if (paneles.isEmpty) throw const FormatException('El dibujo no trae paneles');
    final porClave = <String, ZonaDano>{};
    for (final z in zonas) {
      if (porClave.containsKey(z.clave)) throw FormatException('La zona ${z.clave} está repetida');
      porClave[z.clave] = z;
    }
    final porCodigo = {for (final p in paneles) p.codigo: p};
    final porPanel = {for (final p in paneles) p.codigo: <RegionDano>[]};
    for (final r in regiones) {
      final panel = porCodigo[r.panel] ?? (throw FormatException('La región ${r.numero} apunta al panel ${r.panel}, que no existe'));
      final clave = claveDeZona(r.numero, r.lado);
      final zona = porClave[clave] ?? (throw FormatException('La región del panel ${r.panel} apunta a la zona $clave, que no existe'));
      if (r.puntos.length < 3) throw FormatException('La región $clave del panel ${r.panel} tiene menos de 3 puntos');
      final lista = porPanel[r.panel]!;
      if (lista.any((x) => x.clave == clave)) throw FormatException('La zona $clave está dos veces en el panel ${r.panel}');
      lista.add(RegionDano(
        panel: r.panel,
        zona: zona,
        prioridad: r.prioridad,
        puntos: r.puntos,
        anchoRef: panel.anchoRef,
        altoRef: panel.altoRef,
      ));
    }
    return DibujoDano._(version: version, origen: origen, paneles: List.unmodifiable(paneles), zonas: porClave, regiones: porPanel);
  }

  /// Lee el JSON del dibujo (el formato del archivo `regiones-zonas-dano.json`: `version`, `paneles`, `cuadrantes`
  /// y `regiones`). [imagen] dice dónde está la imagen de cada panel a partir del nombre de archivo que trae el JSON.
  ///
  /// El JSON todavía no trae nombre de panel: si no viene `nombre`, la pestaña muestra el código legible
  /// («costado_izquierdo» → «Costado izquierdo»).
  factory DibujoDano.desdeJson(
    Map<String, dynamic> json, {
    required ImagenPanel Function(String codigo, String archivo) imagen,
    OrigenDibujo origen = OrigenDibujo.assets,
  }) {
    final paneles = [
      for (final p in _lista(json, 'paneles'))
        PanelDano(
          codigo: p['codigo'] as String,
          nombre: p['nombre'] as String? ?? nombreLegible(p['codigo'] as String),
          anchoRef: (p['ancho_ref'] as num).toDouble(),
          altoRef: (p['alto_ref'] as num).toDouble(),
          imagen: imagen(p['codigo'] as String, p['imagen'] as String),
        ),
    ];
    final zonas = [
      for (final c in _lista(json, 'cuadrantes'))
        ZonaDano(
          numero: (c['numero'] as num).toInt(),
          lado: c['lado'] as String?,
          nombre: c['nombre'] as String,
          nombreCompleto: c['nombre_completo'] as String? ?? c['nombre'] as String,
          aiagArea: c['aiag_area'] as String?,
        ),
    ];
    final regiones = [
      for (final r in _lista(json, 'regiones'))
        (
          panel: r['panel'] as String,
          numero: (r['cuadrante_numero'] as num).toInt(),
          lado: r['lado'] as String?,
          prioridad: (r['prioridad'] as num?)?.toInt() ?? 1,
          puntos: leerPuntos(r['geometria']),
        ),
    ];
    return DibujoDano.armar(
      version: '${json['version'] ?? ''}',
      origen: origen,
      paneles: paneles,
      zonas: zonas,
      regiones: regiones,
    );
  }

  /// Versión de los datos: la del JSON en los assets o la del catálogo del servidor.
  final String version;
  final OrigenDibujo origen;
  final List<PanelDano> paneles;
  final Map<String, ZonaDano> _zonas;
  final Map<String, List<RegionDano>> _regiones;

  Iterable<ZonaDano> get zonas => _zonas.values;

  ZonaDano? zona(String clave) => _zonas[clave];

  ZonaDano? zonaDe(int numero, String? lado) => _zonas[claveDeZona(numero, lado)];

  List<RegionDano> regionesDe(String panel) => _regiones[panel] ?? const [];

  /// Paneles donde se ve una zona: los que llevan el punto amarillo en la pestaña cuando está elegida.
  List<PanelDano> panelesDondeSeVe(String clave) =>
      [for (final p in paneles) if (regionesDe(p.codigo).any((r) => r.clave == clave)) p];

  /// Cuántas de las zonas de [danos] se ven en el panel: el número de la pestaña.
  int conDanoEn(String panel, Map<String, String?> danos) =>
      regionesDe(panel).where((r) => danos.containsKey(r.clave)).length;

  static List<Map<String, dynamic>> _lista(Map<String, dynamic> json, String clave) {
    final valor = json[clave];
    if (valor is! List) throw FormatException('El dibujo no trae «$clave»');
    return [for (final v in valor) v as Map<String, dynamic>];
  }
}

/// Lee `{"puntos": [[x, y], ...]}` y comprueba que cada coordenada esté entre 0 y 1.
List<Punto> leerPuntos(Object? geometria) {
  final puntos = geometria is Map ? geometria['puntos'] : null;
  if (puntos is! List) throw const FormatException('La geometría no trae «puntos»');
  return [
    for (final p in puntos)
      if (p is List && p.length >= 2 && p[0] is num && p[1] is num)
        (x: _normalizada((p[0] as num).toDouble()), y: _normalizada((p[1] as num).toDouble()))
      else
        throw const FormatException('Un punto de la geometría no es [x, y]'),
  ];
}

double _normalizada(double v) =>
    v >= 0 && v <= 1 ? v : throw FormatException('Coordenada fuera de 0..1: $v');

/// «costado_izquierdo» → «Costado izquierdo». Solo para cuando los datos no traen el nombre del panel.
String nombreLegible(String codigo) {
  final texto = codigo.replaceAll('_', ' ');
  return texto.isEmpty ? texto : texto[0].toUpperCase() + texto.substring(1);
}
