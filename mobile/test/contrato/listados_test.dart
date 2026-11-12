import 'package:dio/dio.dart';
import 'package:drift/native.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/core/db/app_database.dart';
import 'package:traza_autos/core/db/cache_lecturas.dart';
import 'package:traza_autos/core/errores/error_api.dart';
import 'package:traza_autos/core/http/cliente_api.dart';
import 'package:traza_autos/core/http/listados.dart';
import 'package:traza_autos/features/avisos/data/avisos_repositorio.dart';
import 'package:traza_autos/features/custodia/data/usuarios_repositorio.dart';
import 'package:traza_autos/features/inspecciones/data/inspecciones_repositorio.dart';
import 'package:traza_autos/features/inspecciones/data/novedades_repositorio.dart';
import 'package:traza_autos/features/sofasa/data/sofasa_repositorio.dart';
import 'package:traza_autos/features/viajes/data/entregas_repositorio.dart';
import 'package:traza_autos/features/viajes/data/lugares_repositorio.dart';
import 'package:traza_autos/features/viajes/data/reclamos_repositorio.dart';
import 'package:traza_autos/features/viajes/data/viajes_repositorio.dart';

import '../support/dio_falso.dart';

/// Una prueba por cada listado que la app consume, con la forma que el contrato le da a esa ruta.
///
/// Existe porque la prueba de contrato no alcanza a verlo: comprueba que cada esquema traiga los campos que la app
/// lee, y lo que falla aqui no es el esquema del elemento sino **lo que lo envuelve**. `GET /api/inspecciones` paso
/// a responder `{items, total, pagina, tamano}` sin que `InspeccionListadoDto` cambiara, y la pestana se quedo en
/// «revisa tu conexion» con la API respondiendo 200.
void main() {
  late AppDatabase db;
  late CacheLecturasRepo cache;

  setUp(() {
    db = AppDatabase(NativeDatabase.memory());
    cache = CacheLecturasRepo(db);
  });

  tearDown(() => db.close());

  ClienteApi apiCon(AdaptadorFalso adaptador) =>
      ClienteApi(baseUrl: 'https://api.test', token: () async => 'jwt', dio: Dio()..httpClientAdapter = adaptador);

  /// La envoltura con que el contrato pagina: `{items, total, pagina, tamano}`.
  Map<String, dynamic> paginaDe(List<Map<String, dynamic>> items) =>
      {'items': items, 'total': items.length, 'pagina': 1, 'tamano': 50};

  const inspeccion = {
    'id': 'ins-1',
    'vin': 'VIN00000000000001',
    'momento': 'salida',
    'fecha': '2026-11-09T12:00:00.000Z',
    'estado': 'cerrada',
  };
  const orden = {'id': 'ord-1', 'vin': 'VIN00000000000001', 'status_code': 'LIB'};
  const usuario = {'id': 'u1', 'nombre': 'Ana Perez', 'rol': 'inspector'};
  const viaje = {'id': 'via-1', 'codigo_mtrack': 'MTK-1', 'placa': 'ABC123', 'estado': 'en_curso', 'vehiculos': <dynamic>[]};
  const pendiente = {'viaje_id': 'via-1', 'proceso': 'cargue', 'lugar': 'CEDI', 'unidades': 3};
  const aviso = {'id': 'av-1', 'tipo': 'novedad_conductor', 'urgencia': 'alta', 'titulo': 'Novedad'};
  const novedad = {'id': 'h1', 'vin': 'VIN00000000000001', 'proceso': 'transporte', 'momento': 'llegada', 'tamano_dano': '1_5cm'};

  group('las rutas que el contrato pagina se leen por su envoltura', () {
    test('GET /api/inspecciones', () async {
      final adaptador = AdaptadorFalso((_) => json(200, paginaDe(const [inspeccion])));

      final pagina = await InspeccionesRepositorio(apiCon(adaptador), cache).listarRemoto('u1');

      expect((pagina.items.single.id, pagina.total), ('ins-1', 1));
    });

    test('GET /api/sofasa/ordenes', () async {
      final adaptador = AdaptadorFalso((_) => json(200, paginaDe(const [orden])));

      final lista = await SofasaRepositorio(apiCon(adaptador)).ordenes();

      expect(lista.single.id, 'ord-1');
    });

    test('GET /api/usuarios, con la sesion de quien administra usuarios', () async {
      // La forma depende de quien pregunta: a quien administra usuarios la API le responde una pagina.
      final adaptador = AdaptadorFalso((_) => json(200, paginaDe(const [usuario])));

      final lista = await UsuariosRepositorio(apiCon(adaptador)).listar();

      expect(lista.single.id, 'u1');
    });

    test('GET /api/notificaciones', () async {
      final adaptador = AdaptadorFalso((_) => json(200, {...paginaDe(const [aviso]), 'no_leidas': 1}));

      final pagina = await AvisosRepositorio(apiCon(adaptador)).pagina();

      expect((pagina.avisos.single.id, pagina.noLeidos), ('av-1', 1));
    });

    test('GET /api/hallazgos', () async {
      final adaptador = AdaptadorFalso((_) => json(200, paginaDe(const [novedad])));

      final lista = await NovedadesRepositorio(apiCon(adaptador)).porRevisar(vin: 'VIN00000000000001');

      expect(lista.single.id, 'h1');
    });
  });

  group('las rutas que el contrato responde como arreglo siguen leyendose', () {
    test('GET /api/viajes/mios', () async {
      final adaptador = AdaptadorFalso((_) => json(200, const [viaje]));

      final lista = await ViajesRepositorio(apiCon(adaptador), cache).listarRemoto('u1');

      expect(lista.single.id, 'via-1');
    });

    test('GET /api/viajes/mios?estado=finalizado', () async {
      final adaptador = AdaptadorFalso((_) => json(200, const [viaje]));

      final lista = await ViajesRepositorio(apiCon(adaptador), cache).terminados();

      expect(lista.single.id, 'via-1');
    });

    test('GET /api/viajes/pendientes', () async {
      final adaptador = AdaptadorFalso((_) => json(200, const [pendiente]));

      final lista = await ReclamosRepositorio(apiCon(adaptador)).pendientes();

      expect(lista.single.viajeId, 'via-1');
    });

    test('GET /api/viajes/{id}/firmantes', () async {
      final adaptador = AdaptadorFalso((_) => json(200, const [usuario]));

      final lista = await UsuariosRepositorio(apiCon(adaptador)).firmantesDeViaje('via-1');

      expect(lista.single.id, 'u1');
    });

    test('GET /api/usuarios, con la sesion de quien no administra usuarios', () async {
      // Al inspector que elige testigo en «No firma» la misma ruta le responde la lista reducida.
      final adaptador = AdaptadorFalso((_) => json(200, const [usuario]));

      final lista = await UsuariosRepositorio(apiCon(adaptador)).listar();

      expect(lista.single.id, 'u1');
    });
  });

  group('las rutas con envoltura propia no se tocan', () {
    test('GET /api/catalogos/lugares trae sus elementos en items', () async {
      final adaptador = AdaptadorFalso((_) => json(200, const {
            'version': 'v1',
            'items': [
              {'id': 'lug-1', 'codigo': 'cedi', 'nombre': 'CEDI', 'estado_ubicacion': 'confirmada'},
            ],
          }));

      final estados = await LugaresRepositorio(apiCon(adaptador)).estados();

      expect(estados.keys.single, 'lug-1');
    });

    test('GET /api/viajes/{id}/entregas trae sus puntos en puntos', () async {
      final adaptador = AdaptadorFalso((_) => json(200, const {
            'viaje_id': 'via-1',
            'puntos': [
              {'viaje_id': 'via-1', 'lugar_id': 'lug-1', 'lugar': 'Concesionario', 'unidades': <dynamic>[]},
            ],
          }));

      final puntos = await EntregasRepositorio(apiCon(adaptador)).delViaje('via-1');

      expect(puntos.single.lugarId, 'lug-1');
    });
  });

  group('una respuesta que no es ninguna de las dos formas', () {
    test('no se convierte en lista vacia: levanta un ErrorApi con la ruta', () {
      expect(
        () => elementosDe(const {'datos': <dynamic>[]}, ruta: '/api/inspecciones'),
        throwsA(isA<ErrorApi>()
            .having((e) => e.code, 'code', codigoFormaInesperada)
            // El servidor respondio, y bien: no es un error de red ni algo que se arregle reintentando solo.
            .having((e) => e.status, 'status', 200)
            .having((e) => e.esTransitorio, 'esTransitorio', isFalse)
            .having((e) => e.detalles, 'detalles', contains('Ruta: /api/inspecciones'))),
      );
    });

    test('el repositorio la propaga como ErrorApi, no como una falla de conversion', () async {
      final adaptador = AdaptadorFalso((_) => json(200, const {'datos': <dynamic>[]}));

      await expectLater(
        SofasaRepositorio(apiCon(adaptador)).ordenes(),
        throwsA(isA<ErrorApi>().having((e) => e.code, 'code', codigoFormaInesperada)),
      );
    });
  });
}
