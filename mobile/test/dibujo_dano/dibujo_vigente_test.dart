import 'dart:convert';

import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/core/http/cliente_api.dart';
import 'package:traza_autos/features/catalogos/domain/catalogos.dart';
import 'package:traza_autos/features/dibujo_dano/data/dibujo_dano_repositorio.dart';
import 'package:traza_autos/features/dibujo_dano/data/dibujo_desde_catalogos.dart';
import 'package:traza_autos/features/dibujo_dano/domain/dibujo_dano.dart';
import 'package:traza_autos/features/dibujo_dano/domain/toque.dart';
import 'package:traza_autos/features/dibujo_dano/presentation/dibujo_dano_controller.dart';

import '../support/dobles_dibujo.dart';

/// Regla única: el dibujo del servidor si está completo, y si no, el de los assets. Nunca una mezcla.

Catalogos catalogos(Map<String, List<Map<String, dynamic>>> porNombre, {String version = 'v1'}) =>
    Catalogos.desdeRespuesta({'version': version, 'catalogos': porNombre}, usuarioId: 'u1');

/// Lo que publica hoy la API de QA: 5 paneles sin tamaño de referencia ni zonas con lado, y regiones de la ficha
/// anterior.
final catalogoDeHoy = catalogos({
  Catalogo.panelesDano: [
    {'id': 'p1', 'codigo': 'superior', 'nombre': 'Superior', 'orden': 1, 'activo': true, 'imagen_url': null},
  ],
  Catalogo.cuadranteRegiones: [
    {
      'id': 'r1', 'panel': 'superior', 'cuadrante_id': 'c1', 'numero': 30, 'nombre': 'Techo', 'lado': null,
      'geometria': {'puntos': [[0.1, 0.1], [0.5, 0.1], [0.5, 0.5]]},
    },
  ],
});

/// `ClienteApi` que no habla con nadie: devuelve unos bytes fijos o falla, y cuenta las descargas.
class ApiDeImagenes extends ClienteApi {
  ApiDeImagenes({this.falla = false}) : super(baseUrl: 'https://api.test', token: () async => null);

  final bool falla;
  final pedidas = <String>[];

  @override
  Future<Uint8List> descargarBytes(String ruta) async {
    pedidas.add(ruta);
    if (falla) throw Exception('sin red');
    return Uint8List.fromList([1, 2, 3]);
  }
}

/// Sirve el JSON real de los assets sin depender del empaquetado de la prueba.
class AssetsDePrueba extends CachingAssetBundle {
  @override
  Future<ByteData> load(String key) async {
    if (key != DibujoDanoRepositorio.rutaJson) throw StateError('Asset inesperado: $key');
    return ByteData.sublistView(utf8.encode(jsonEncode(jsonDibujoAssets())));
  }
}

void main() {
  group('dibujoDesdeCatalogos', () {
    test('con lo que publica hoy la API no hay dibujo del servidor', () {
      expect(dibujoDesdeCatalogos(catalogoDeHoy, const {}), isNull);
    });

    test('con el catálogo completo y todas las imágenes, arma el mismo dibujo que los assets', () {
      final delServidor = catalogos(catalogosDibujoDelServidor(jsonDibujoAssets()));
      final imagenes = {
        for (final p in delServidor.opciones(Catalogo.panelesDano)) p.codigo!: ImagenPanel.bytes(Uint8List(1)),
      };

      final dibujo = dibujoDesdeCatalogos(delServidor, imagenes)!;

      expect(dibujo.origen, OrigenDibujo.servidor);
      expect(dibujo.paneles, hasLength(7));
      expect(dibujo.zonas, hasLength(80));
      expect([for (final p in dibujo.paneles) ...dibujo.regionesDe(p.codigo)], hasLength(251));
      expect(dibujo.zonaDe(26, LadoZona.derecho)!.nombreCompleto, 'Llanta trasera derecha');
    });

    test('si falta la imagen de un panel no se usa nada del servidor', () {
      final delServidor = catalogos(catalogosDibujoDelServidor(jsonDibujoAssets()));
      final imagenes = {
        for (final p in delServidor.opciones(Catalogo.panelesDano).skip(1)) p.codigo!: ImagenPanel.bytes(Uint8List(1)),
      };
      expect(dibujoDesdeCatalogos(delServidor, imagenes), isNull);
    });
  });

  group('DibujoDanoRepositorio.vigente', () {
    DibujoDanoRepositorio repositorio({required ApiDeImagenes api, AlmacenImagenesEnMemoria? imagenes}) =>
        DibujoDanoRepositorio(assets: AssetsDePrueba(), api: api, imagenes: imagenes ?? AlmacenImagenesEnMemoria());

    test('sin dibujo en el servidor usa los assets y no baja nada', () async {
      final api = ApiDeImagenes();
      final dibujo = await repositorio(api: api).vigente(catalogoDeHoy);

      expect(dibujo.origen, OrigenDibujo.assets);
      expect(dibujo.paneles.first.imagen.rutaAsset, startsWith(DibujoDanoRepositorio.carpetaImagenes));
      expect(api.pedidas, isEmpty);
    });

    test('con dibujo completo baja las 7 imágenes una sola vez y las guarda', () async {
      final api = ApiDeImagenes();
      final imagenes = AlmacenImagenesEnMemoria();
      final delServidor = catalogos(catalogosDibujoDelServidor(jsonDibujoAssets()));

      final primero = await repositorio(api: api, imagenes: imagenes).vigente(delServidor);
      // Otro arranque de la app: las imágenes ya están guardadas.
      final segundo = await repositorio(api: api, imagenes: imagenes).vigente(delServidor);

      expect((primero.origen, segundo.origen), (OrigenDibujo.servidor, OrigenDibujo.servidor));
      expect(api.pedidas, hasLength(7));
      expect(imagenes.guardadas, hasLength(7));
    });

    test('sin señal para bajar las imágenes, usa los assets completos', () async {
      final delServidor = catalogos(catalogosDibujoDelServidor(jsonDibujoAssets()));
      final dibujo = await repositorio(api: ApiDeImagenes(falla: true)).vigente(delServidor);
      expect(dibujo.origen, OrigenDibujo.assets);
    });
  });

  group('SeleccionZonaController', () {
    late ProviderContainer contenedor;
    late SeleccionZonaController seleccion;

    EstadoSeleccionZona estado() => contenedor.read(seleccionZonaProvider('prueba'));

    setUp(() {
      contenedor = ProviderContainer(retry: (_, _) => null);
      // autoDispose: se escucha para que no se libere entre lecturas.
      contenedor.listen(seleccionZonaProvider('prueba'), (_, _) {});
      seleccion = contenedor.read(seleccionZonaProvider('prueba').notifier);
    });
    tearDown(() => contenedor.dispose());

    test('tocar elige y tocar de nuevo suelta', () {
      seleccion.aplicar(const ToqueElegir('26D'));
      expect(estado().seleccionada, '26D');
      seleccion.aplicar(const ToqueElegir('26D'));
      expect(estado().seleccionada, isNull);
    });

    test('un toque ambiguo suelta la selección y deja los candidatos; elegir uno los quita', () {
      seleccion.aplicar(const ToqueElegir('1I'));
      seleccion.aplicar(const ToqueAmbiguo(['1I', '2I']));
      expect(estado().seleccionada, isNull);
      expect(estado().candidatos, ['1I', '2I']);

      seleccion.elegir('2I');
      expect(estado().seleccionada, '2I');
      expect(estado().candidatos, isEmpty);
    });

    test('cambiar de pestaña conserva la selección, para ver el punto amarillo', () {
      seleccion.aplicar(const ToqueElegir('54'));
      seleccion.verPanel(3);
      expect((estado().panel, estado().seleccionada), (3, '54'));
    });

    test('tocar fuera suelta', () {
      seleccion.aplicar(const ToqueElegir('54'));
      seleccion.aplicar(const ToqueFuera());
      expect(estado().seleccionada, isNull);
    });

    test('ir a una zona desde la lista cambia a una pestaña donde se ve', () {
      final dibujo = dibujoDeAssets();
      seleccion.verPanel(dibujo.paneles.indexWhere((p) => p.codigo == 'costado_izquierdo'));
      seleccion.enfocar(dibujo, '26D'); // llanta trasera derecha: no se ve en el costado izquierdo
      final panel = dibujo.paneles[estado().panel];
      expect(dibujo.regionesDe(panel.codigo).any((r) => r.clave == '26D'), isTrue);
      expect(estado().seleccionada, '26D');
    });
  });
}
