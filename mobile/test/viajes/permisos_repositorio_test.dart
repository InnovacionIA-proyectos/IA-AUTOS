import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/core/errores/error_api.dart';
import 'package:traza_autos/core/http/cliente_api.dart';
import 'package:traza_autos/features/viajes/data/permisos_repositorio.dart';
import 'package:traza_autos/features/viajes/domain/permiso_sin_inspector.dart';

import '../support/dio_falso.dart';

/// S7-HU-06.2 (#352): el permiso de la excepción «No hay inspector» donde el punto de cargue sí tiene inspectores.

(PermisosRepositorio, AdaptadorFalso) repositorio(ResponseBody Function(RequestOptions) responder) {
  final adaptador = AdaptadorFalso(responder);
  final api = ClienteApi(baseUrl: 'https://api.test', token: () async => 'jwt', dio: Dio()..httpClientAdapter = adaptador);
  return (PermisosRepositorio(api), adaptador);
}

Map<String, Object?> permisoJson({String estado = 'pendiente', bool porTiempo = false}) => {
      'id': 'p1',
      'viaje_id': 'v1',
      'estado': estado,
      'solicitado_at': '2026-11-13T10:00:00Z',
      'vence_at': '2026-11-13T10:10:00Z',
      'conductor_id': 'c1',
      'conductor_nombre': 'Luis Gómez',
      'lugar_id': 'l1',
      'lugar': 'CEDI Envigado',
      'motivo_codigo': 'inspector_no_llego',
      'motivo': 'El inspector no llegó',
      'motivo_texto': null,
      'fecha': '2026-11-13T09:58:00Z',
      'lat': 6.2442,
      'lng': -75.5812,
      'concedido_at': estado == 'concedido' ? '2026-11-13T10:03:00Z' : null,
      'concedido_por': null,
      'concedido_por_nombre': estado == 'concedido' && !porTiempo ? 'Ana Restrepo' : null,
      'autorizado_por_tiempo': porTiempo,
    };

void main() {
  group('consultar el permiso', () {
    test('lee el estado, el plazo y con qué motivo se pidió', () async {
      final (repo, _) = repositorio((_) => json(200, permisoJson()));

      final permiso = (await repo.delViaje('v1'))!;

      expect(permiso.estado, EstadoPermiso.pendiente);
      expect(permiso.concedido, isFalse);
      expect(permiso.lugar, 'CEDI Envigado');
      expect(permiso.motivoCodigo, 'inspector_no_llego');
      expect(permiso.venceAt, DateTime.utc(2026, 11, 13, 10, 10));
    });

    // «Este viaje no tiene solicitud» es una respuesta, no una falla: la pantalla ofrece pedirlo.
    test('el 404 no es un error: es que todavía no se ha pedido ninguno', () async {
      final (repo, _) = repositorio((_) => json(404, const {'error': 'NOT_FOUND', 'code': 'NOT_FOUND', 'message': 'El viaje no tiene una solicitud de permiso', 'timestamp': 'x'}));

      expect(await repo.delViaje('v1'), isNull);
    });

    test('los demás errores sí se propagan', () async {
      final (repo, _) = repositorio((_) => json(403, const {'error': 'FORBIDDEN', 'code': 'FORBIDDEN', 'message': 'El viaje no está asignado a su usuario', 'timestamp': 'x'}));

      await expectLater(repo.delViaje('v1'), throwsA(isA<ErrorApi>().having((e) => e.status, 'status', 403)));
    });

    test('concedido por tiempo se distingue de concedido por alguien', () async {
      final (repo, _) = repositorio((_) => json(200, permisoJson(estado: 'concedido', porTiempo: true)));

      final permiso = (await repo.delViaje('v1'))!;

      expect(permiso.concedido, isTrue);
      expect(permiso.porTiempo, isTrue);
      expect(permiso.concedidoPorNombre, isNull);
    });
  });

  group('pedir el permiso', () {
    test('manda el motivo, el momento y el lugar con los nombres del contrato', () async {
      RequestOptions? enviado;
      final (repo, _) = repositorio((o) {
        enviado = o;
        return json(201, permisoJson());
      });

      await repo.solicitar(
        'v1',
        motivo: 'inspector_no_llego',
        fecha: DateTime.utc(2026, 11, 13, 9, 58),
        lat: 6.2442,
        lng: -75.5812,
      );

      expect(enviado!.path, '/api/viajes/v1/permiso-sin-inspector');
      expect(enviado!.data, {
        'motivo_sin_inspector': 'inspector_no_llego',
        'motivo_sin_inspector_texto': null,
        'fecha': '2026-11-13T09:58:00.000Z',
        'lat': 6.2442,
        'lng': -75.5812,
      });
    });
  });

  group('la cuenta atrás', () {
    test('cuenta lo que falta para que lo conceda el reloj', () {
      final permiso = PermisoSinInspector.fromJson(permisoJson());

      expect(permiso.faltaPara(DateTime.utc(2026, 11, 13, 10, 2, 30)), const Duration(minutes: 7, seconds: 30));
    });

    // Pasado el plazo no se enseña un número negativo: el servidor lo concede en la siguiente consulta.
    test('cumplido el plazo, no falta nada', () {
      final permiso = PermisoSinInspector.fromJson(permisoJson());

      expect(permiso.faltaPara(DateTime.utc(2026, 11, 13, 10, 42)), Duration.zero);
    });
  });

  group('lo que dice el botón de la salida del cargue', () {
    test('sin inspectores en el punto, se activa en el momento', () {
      expect(etiquetaExcepcion(cargueConInspector: false), 'No hay inspector');
      expect(etiquetaExcepcion(cargueConInspector: false, permiso: PermisoSinInspector.fromJson(permisoJson())), 'No hay inspector');
    });

    test('con inspectores, se pide, se mira y se sigue', () {
      expect(etiquetaExcepcion(cargueConInspector: true), 'Pedir permiso');
      expect(
        etiquetaExcepcion(cargueConInspector: true, permiso: PermisoSinInspector.fromJson(permisoJson())),
        'Ver el permiso',
      );
      expect(
        etiquetaExcepcion(
          cargueConInspector: true,
          permiso: PermisoSinInspector.fromJson(permisoJson(estado: 'concedido')),
        ),
        'Continuar el cargue',
      );
    });
  });
}
