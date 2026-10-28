import 'dart:convert';
import 'dart:io';
import 'dart:typed_data';

import 'package:traza_autos/features/dibujo_dano/data/dibujo_dano_repositorio.dart';
import 'package:traza_autos/features/dibujo_dano/domain/dibujo_dano.dart';

/// El JSON del dibujo tal como va en los assets. `flutter test` corre desde la raíz del paquete.
Map<String, dynamic> jsonDibujoAssets() =>
    jsonDecode(File('assets/dibujo_dano/regiones-zonas-dano.json').readAsStringSync()) as Map<String, dynamic>;

/// El dibujo de los assets, armado sin `AssetBundle`.
DibujoDano dibujoDeAssets() => DibujoDano.desdeJson(
      jsonDibujoAssets(),
      imagen: (_, archivo) => ImagenPanel.asset('${DibujoDanoRepositorio.carpetaImagenes}$archivo'),
    );

class AlmacenImagenesEnMemoria implements AlmacenImagenesPanel {
  final guardadas = <String, Uint8List>{};

  @override
  Future<Uint8List?> leer(String clave) async => guardadas[clave];

  @override
  Future<void> guardar(String clave, Uint8List bytes) async => guardadas[clave] = bytes;
}

/// Los catálogos `paneles-dano`, `zonas-dano` y `cuadrante-regiones` con la forma que se le propone al backend,
/// armados desde el JSON de los assets. Es lo que el servidor publicaría al cargar esa misma versión.
Map<String, List<Map<String, dynamic>>> catalogosDibujoDelServidor(Map<String, dynamic> json) => {
      'paneles-dano': [
        for (final (i, p) in (json['paneles'] as List).indexed)
          {
            'id': 'p$i',
            'codigo': p['codigo'],
            'nombre': nombreLegible(p['codigo'] as String),
            'orden': i + 1,
            'activo': true,
            'imagen_url': '/api/catalogos/paneles-dano/${p['codigo']}/imagen?v=1',
            'ancho_ref': p['ancho_ref'],
            'alto_ref': p['alto_ref'],
          },
      ],
      'zonas-dano': [
        for (final (i, c) in (json['cuadrantes'] as List).indexed)
          {
            'id': 'z$i',
            'numero': c['numero'],
            'lado': c['lado'],
            'nombre': c['nombre'],
            'nombre_completo': c['nombre_completo'],
            'aiag_area': c['aiag_area'],
            'orden': i,
            'activo': true,
          },
      ],
      'cuadrante-regiones': [
        for (final (i, r) in (json['regiones'] as List).indexed)
          {
            'id': 'r$i',
            'panel': r['panel'],
            'numero': r['cuadrante_numero'],
            'lado': r['lado'],
            'prioridad': r['prioridad'],
            'geometria': r['geometria'],
          },
      ],
    };
