import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/core/http/cliente_api.dart';
import 'package:traza_autos/features/sync/data/gateway_informes_api.dart';
import 'package:traza_autos/features/sync/domain/procesador_cola.dart';

import '../support/arnes.dart';
import '../support/dio_falso.dart';
import '../support/dobles_sync.dart';

void main() {
  late ColaEnMemoria cola;
  late GatewayFalso gateway;
  late ProcesadorCola procesador;

  setUp(() {
    cola = ColaEnMemoria();
    gateway = GatewayFalso();
    procesador = ProcesadorCola(cola: cola, gateway: gateway, red: RedFalsa(), archivos: ArchivosFalsos(['a.jpg']));
  });

  group('las posiciones de la niñera en la cola', () {
    test('van después de crear la inspección y antes de cerrarla', () async {
      await cola.agregar(informe(viajeId: 'v1', posiciones: {'VINA': 4, 'VINB': 3}));

      await procesador.procesar(usuarioPrueba);

      expect(gateway.llamadas, [
        'crearInspeccion',
        'registrarPosicion',
        'registrarPosicion',
        'registrarPosicion',
        'registrarPosicion',
        'cerrarInspeccion',
      ]);
      expect(cola.jobs, isEmpty);
    });

    test('libera primero las que cambian y después las asigna, para que un intercambio no choque', () async {
      await cola.agregar(
        informe(viajeId: 'v1', posiciones: {'VINA': 3, 'VINB': 4}, posicionesPrevias: {'VINA': 4, 'VINB': 3}),
      );

      await procesador.procesar(usuarioPrueba);

      expect(gateway.posicionesEnviadas, [
        ('v1', 'VINA', null),
        ('v1', 'VINB', null),
        ('v1', 'VINA', 3),
        ('v1', 'VINB', 4),
      ]);
    });

    test('no vuelve a mandar lo que el viaje ya traía', () async {
      await cola.agregar(
        informe(viajeId: 'v1', posiciones: {'VINA': 4, 'VINB': 3}, posicionesPrevias: {'VINA': 4}),
      );

      await procesador.procesar(usuarioPrueba);

      expect(gateway.posicionesEnviadas, [('v1', 'VINB', null), ('v1', 'VINB', 3)]);
    });

    test('un informe sin posiciones no toca esa ruta', () async {
      await cola.agregar(informe());

      await procesador.procesar(usuarioPrueba);

      expect(gateway.llamadas, ['crearInspeccion', 'cerrarInspeccion']);
    });

    test('al reanudar no repite las posiciones que ya se enviaron', () async {
      await cola.agregar(
        informe(inspeccionId: 'insp-previa', viajeId: 'v1', posiciones: {'VINA': 4}, posicionesEnviadas: true),
      );

      await procesador.procesar(usuarioPrueba);

      expect(gateway.posicionesEnviadas, isEmpty);
      expect(gateway.llamadas, ['cerrarInspeccion']);
    });

    test('si el servidor rechaza una posición, el informe queda fallido con su mensaje y conserva lo capturado', () async {
      gateway.errores['registrarPosicion'] = [
        errorApi(409, 'La posición 4 ya está ocupada por otra unidad de este viaje'),
      ];
      await cola.agregar(informe(viajeId: 'v1', posiciones: {'VINA': 4}));

      final r = await procesador.procesar(usuarioPrueba);

      expect((r.enviados, r.fallidos), (0, 1));
      final job = cola.jobs.single;
      expect(job.fallido, isTrue);
      expect(job.error, contains('La posición 4 ya está ocupada por otra unidad de este viaje'));
      expect(gateway.llamadas, isNot(contains('cerrarInspeccion')));
    });
  });

  group('la posición hacia la API', () {
    test('es un PUT a la unidad del viaje con la clave presente, también al liberar', () async {
      final adaptador = AdaptadorFalso((_) => json(200, {'id': 'v1'}));
      final api = GatewayInformesApi(ClienteApi(baseUrl: 'https://api.test', token: () async => 'jwt', dio: Dio()..httpClientAdapter = adaptador));

      await api.registrarPosicion('v 1', '9FB45RFL8NM201001', 4);
      await api.registrarPosicion('v 1', '9FB45RFL8NM201001', null);

      final primera = adaptador.peticiones.first;
      expect((primera.method, primera.uri.path), ('PUT', '/api/viajes/v%201/vehiculos/9FB45RFL8NM201001/posicion'));
      expect(primera.data, {'posicion': 4});
      expect(adaptador.peticiones.last.data, {'posicion': null});
    });
  });
}
