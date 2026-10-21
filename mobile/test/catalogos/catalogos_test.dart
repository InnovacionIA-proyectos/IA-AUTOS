import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/features/catalogos/domain/catalogos.dart';

Map<String, dynamic> valor(
  String codigo,
  String nombre, {
  int orden = 0,
  bool activo = true,
  Map<String, dynamic> extra = const {},
}) =>
    {'id': 'id-$codigo', 'codigo': codigo, 'nombre': nombre, 'orden': orden, 'activo': activo, ...extra};

Map<String, dynamic> respuesta({String version = 'v1', Map<String, dynamic>? catalogos}) => {
      'version': version,
      'catalogos': catalogos ??
          {
            Catalogo.clasesDano: [
              valor('golpe', 'Golpe', orden: 1),
              valor('mancha', 'Mancha', orden: 2),
              valor('raya_pintar', 'Raya para pintar', orden: 3, activo: false),
            ],
            Catalogo.clientes: [valor('sofasa', 'Sofasa')],
          },
    };

void main() {
  group('lecturas', () {
    test('opciones trae solo los activos, en su orden; todos incluye los inactivos', () {
      final catalogos = Catalogos.desdeRespuesta(respuesta(), usuarioId: 'u1');

      expect([for (final v in catalogos.opciones(Catalogo.clasesDano)) v.codigo], ['golpe', 'mancha']);
      expect([for (final v in catalogos.todos(Catalogo.clasesDano)) v.codigo], ['golpe', 'mancha', 'raya_pintar']);
    });

    test('opciones ordena por orden, sin confiar en el que mande el servidor', () {
      final catalogos = Catalogos.desdeRespuesta(
        respuesta(catalogos: {
          Catalogo.clasesDano: [
            valor('tercero', 'Tercero', orden: 3),
            valor('primero', 'Primero', orden: 1),
            valor('segundo', 'Segundo', orden: 2),
          ],
        }),
        usuarioId: 'u1',
      );

      expect([for (final v in catalogos.opciones(Catalogo.clasesDano)) v.codigo], ['primero', 'segundo', 'tercero']);
    });

    test('etiqueta resuelve el nombre, también el de un valor inactivo (informes viejos)', () {
      final catalogos = Catalogos.desdeRespuesta(respuesta(), usuarioId: 'u1');

      expect(catalogos.etiqueta(Catalogo.clasesDano, 'golpe'), 'Golpe');
      expect(catalogos.etiqueta(Catalogo.clasesDano, 'raya_pintar'), 'Raya para pintar');
    });

    test('un código desconocido se devuelve tal cual, como hacía etiquetar()', () {
      final catalogos = Catalogos.desdeRespuesta(respuesta(), usuarioId: 'u1');

      expect(catalogos.etiqueta(Catalogo.clasesDano, 'clase_nueva'), 'clase_nueva');
      expect(catalogos.etiqueta('catalogo-que-no-existe', 'x'), 'x');
    });

    test('se conservan los campos propios de cada catálogo, no solo código y nombre', () {
      final catalogos = Catalogos.desdeRespuesta(
        respuesta(catalogos: {
          Catalogo.vistasFoto: [
            valor('piezas_baul', 'Piezas en el baúl', extra: {'obligatoria': false, 'tipo_vehiculo': null}),
          ],
          Catalogo.nivelesSeveridad: [valor('v1', 'V1', extra: {'accion': 'reparar'})],
        }),
        usuarioId: 'u1',
      );

      final vista = catalogos.valor(Catalogo.vistasFoto, 'piezas_baul')!;
      expect(vista.campo<bool>('obligatoria'), isFalse);
      expect(catalogos.valor(Catalogo.nivelesSeveridad, 'v1')!.campo<String>('accion'), 'reparar');
    });

    test('un valor sin código (cuadrantes) aparece en la lista aunque no se pueda buscar por código', () {
      final catalogos = Catalogos.desdeRespuesta(
        respuesta(catalogos: {
          Catalogo.cuadrantes: [
            {'id': 'c1', 'codigo': null, 'nombre': 'Puerta delantera', 'numero': 1, 'orden': 1, 'activo': true},
          ],
        }),
        usuarioId: 'u1',
      );

      expect(catalogos.opciones(Catalogo.cuadrantes).single.campo<int>('numero'), 1);
      expect(catalogos.valor(Catalogo.cuadrantes, ''), isNull);
    });
  });

  group('copia guardada y aislamiento por usuario', () {
    test('ida y vuelta conserva versión, dueño y contenido', () {
      final original = Catalogos.desdeRespuesta(respuesta(version: 'abc'), usuarioId: 'u1');

      final recuperada = Catalogos.desdeAlmacen(original.toJson(), usuarioId: 'u1');

      expect(recuperada.version, 'abc');
      expect(recuperada.usuarioId, 'u1');
      expect(recuperada.etiqueta(Catalogo.clasesDano, 'golpe'), 'Golpe');
      expect(recuperada.opciones(Catalogo.clientes).single.codigo, 'sofasa');
    });

    test('una copia descargada por otro usuario NO entrega clientes, pero sí el resto', () {
      final deOtro = Catalogos.desdeRespuesta(respuesta(), usuarioId: 'u1').toJson();

      final leida = Catalogos.desdeAlmacen(deOtro, usuarioId: 'u2');

      expect(leida.opciones(Catalogo.clientes), isEmpty, reason: 'clientes es el catálogo que la API podría filtrar');
      expect(leida.opciones(Catalogo.clasesDano), isNotEmpty, reason: 'los maestros son iguales para todos');
    });
  });
}
