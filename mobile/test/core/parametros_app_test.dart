import 'package:dio/dio.dart';
import 'package:drift/native.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/core/config/parametros_app.dart';
import 'package:traza_autos/core/db/app_database.dart';
import 'package:traza_autos/core/db/cache_lecturas.dart';
import 'package:traza_autos/core/http/cliente_api.dart';
import 'package:traza_autos/features/auth/domain/sesion.dart';
import 'package:traza_autos/features/auth/presentation/auth_controller.dart';
import 'package:traza_autos/features/inspecciones/domain/acomodacion.dart';
import 'package:traza_autos/features/inspecciones/domain/reglas_informe.dart';

import '../support/arnes.dart';
import '../support/dio_falso.dart';

/// S8-HU-01.3 CA-3 y S8-HU-09.1 CA-10: la app lee del servidor el mínimo de fotos de la acomodación y la precisión
/// del GPS; sin señal usa la última respuesta guardada, y sin nada, el valor de hoy de la regla.
void main() {
  late AppDatabase db;
  late CacheLecturasRepo cache;

  setUp(() {
    db = AppDatabase(NativeDatabase.memory());
    cache = CacheLecturasRepo(db);
  });

  tearDown(() => db.close());

  ParametrosAppDelServidor fuente(AdaptadorFalso adaptador) => ParametrosAppDelServidor(
        ClienteApi(baseUrl: 'https://api.test', token: () async => 'jwt', dio: Dio()..httpClientAdapter = adaptador),
        cache,
      );

  test('pide la ruta de la app y lee los dos valores', () async {
    final adaptador = AdaptadorFalso((_) => json(200, {'acomodacion_fotos_minimas': 2, 'gps_precision_minima_m': 25}));

    final p = await fuente(adaptador).leer();

    expect(adaptador.peticiones.single.path, endsWith('/api/parametros/app'));
    expect((p?.acomodacionFotosMinimas, p?.gpsPrecisionMinimaM), (2, 25.0));
  });

  test('sin señal usa la última respuesta guardada', () async {
    await fuente(AdaptadorFalso((_) => json(200, {'acomodacion_fotos_minimas': 3, 'gps_precision_minima_m': 40}))).leer();

    final p = await fuente(AdaptadorFalso((o) => throw DioException.connectionError(requestOptions: o, reason: 'sin red'))).leer();

    expect((p?.acomodacionFotosMinimas, p?.gpsPrecisionMinimaM), (3, 40.0));
  });

  test('sin respuesta ni copia no inventa nada', () async {
    final p = await fuente(AdaptadorFalso((_) => json(403, {'code': 'FORBIDDEN'}))).leer();

    expect(p, isNull);
  });

  // S8-HU-09.2 CA-1: ya no hay valor compilado; sin respuesta ni copia manda el valor de hoy de la regla.
  test('lo del servidor (o su copia) manda; sin ninguno, el valor de hoy', () {
    const delServidor = ParametrosApp(acomodacionFotosMinimas: 2, gpsPrecisionMinimaM: 25);

    expect(fotosAcomodacionMinimasDe(delServidor, acomodacionFotosMinimasPorDefecto), 2);
    expect(fotosAcomodacionMinimasDe(null, acomodacionFotosMinimasPorDefecto), 1);
    expect(precisionBajaDe(delServidor), 25);
    expect(precisionBajaDe(null), isNull, reason: 'null es «el valor inicial de la regla», los 30 m de hoy');
    expect(precisionBajaMetrosPorDefecto, 30);
  });

  test('un valor fuera de rango no se usa', () {
    final p = ParametrosApp.fromJson({'acomodacion_fotos_minimas': -1, 'gps_precision_minima_m': null});
    expect((p.acomodacionFotosMinimas, p.gpsPrecisionMinimaM), (null, null));
  });

  // El servidor admite 0 fotos mínimas: entonces el cierre no las exige, y la captura tampoco.
  test('cero fotos mínimas es un valor válido', () {
    expect(ParametrosApp.fromJson({'acomodacion_fotos_minimas': 0}).acomodacionFotosMinimas, 0);
    expect(fotosAcomodacionMinimasDe(const ParametrosApp(acomodacionFotosMinimas: 0), acomodacionFotosMinimasPorDefecto), 0);
  });

  test('un campo que llega vacío no borra el bueno de la copia local', () async {
    await fuente(AdaptadorFalso((_) => json(200, {'acomodacion_fotos_minimas': 2, 'gps_precision_minima_m': 25}))).leer();

    final p = await fuente(AdaptadorFalso((_) => json(200, {'acomodacion_fotos_minimas': 3, 'gps_precision_minima_m': null})))
        .leer();

    expect((p?.acomodacionFotosMinimas, p?.gpsPrecisionMinimaM), (3, 25.0));
    final copia = await fuente(AdaptadorFalso((_) => json(500, {}))).guardados();
    expect((copia?.acomodacionFotosMinimas, copia?.gpsPrecisionMinimaM), (3, 25.0));
  });

  // El refresco al volver la señal y la pantalla que los mira hacen una sola petición, no dos.
  test('lo que se acaba de traer no se vuelve a pedir enseguida', () async {
    final adaptador = AdaptadorFalso((_) => json(200, {'acomodacion_fotos_minimas': 2, 'gps_precision_minima_m': 25}));
    final f = fuente(adaptador);

    await f.leer();
    final segunda = await f.leer();

    expect(adaptador.peticiones, hasLength(1));
    expect(segunda?.acomodacionFotosMinimas, 2);
  });

  test('un fallo no se recuerda: la siguiente vez se vuelve a pedir', () async {
    var falla = true;
    final adaptador = AdaptadorFalso(
      (o) => falla ? throw DioException.connectionError(requestOptions: o, reason: 'sin red') : json(200, {'acomodacion_fotos_minimas': 2}),
    );
    final f = fuente(adaptador);

    await f.leer();
    falla = false;
    final p = await f.leer();

    expect(adaptador.peticiones, hasLength(2));
    expect(p?.acomodacionFotosMinimas, 2);
  });

  test('la copia local se lee sin ir a la red', () async {
    await fuente(AdaptadorFalso((_) => json(200, {'acomodacion_fotos_minimas': 2, 'gps_precision_minima_m': 25}))).leer();
    final sinRed = AdaptadorFalso((o) => throw DioException.connectionError(requestOptions: o, reason: 'sin red'));

    final p = await fuente(sinRed).guardados();

    expect((p?.acomodacionFotosMinimas, p?.gpsPrecisionMinimaM), (2, 25.0));
    expect(sinRed.peticiones, isEmpty);
  });

  // S8-HU-09.2 CA-1: se piden al iniciar sesión y al recuperar señal, sin esperar a que alguien abra una captura.
  group('cuándo se refrescan', () {
    Future<(ProviderContainer, Arnes)> iniciar({Sesion? sesion = sesionPrueba}) async {
      final arnes = Arnes(sesion: sesion);
      final contenedor = arnes.contenedor();
      addTearDown(contenedor.dispose);
      contenedor.listen(refrescoParametrosAppProvider, (_, _) {}); // lo mantiene vivo, como hace la app
      await pumpEventQueue();
      return (contenedor, arnes);
    }

    test('al iniciar sesión', () async {
      final (contenedor, arnes) = await iniciar(sesion: null);
      expect(arnes.parametros.lecturas, 0, reason: 'sin sesión la ruta respondería 401');

      await contenedor.read(authControllerProvider.notifier).iniciarSesion('ana@x.co', 'clave');
      await pumpEventQueue();

      expect(arnes.parametros.lecturas, 1);
    });

    test('al volver una señal estable, y no al perderla', () async {
      final (_, arnes) = await iniciar();
      final antes = arnes.parametros.lecturas;

      arnes.red.cambiarA(false);
      await pumpEventQueue();
      expect(arnes.parametros.lecturas, antes);

      arnes.red.cambiarA(true);
      await pumpEventQueue();
      expect(arnes.parametros.lecturas, antes + 1);
    });

    test('con un PIN temporal no se piden (la ruta respondería 403)', () async {
      final (_, arnes) = await iniciar(sesion: sesionConPinTemporal);

      arnes.red.cambiarA(true);
      await pumpEventQueue();

      expect(arnes.parametros.lecturas, 0);
    });

    test('quien está mirando los parámetros recibe los nuevos sin volver a abrir', () async {
      final (contenedor, arnes) = await iniciar();
      arnes.parametros.valor = const ParametrosApp(acomodacionFotosMinimas: 1, gpsPrecisionMinimaM: 30);
      contenedor.listen(parametrosAppProvider, (_, _) {});
      await pumpEventQueue();
      expect(contenedor.read(parametrosAppProvider).value?.acomodacionFotosMinimas, 1);

      // ESC-06: Jorge sube las fotos mínimas de 1 a 2 en el portal y vuelve la señal.
      arnes.parametros.valor = const ParametrosApp(acomodacionFotosMinimas: 2, gpsPrecisionMinimaM: 30);
      arnes.red.cambiarA(true);
      await pumpEventQueue();

      expect(contenedor.read(parametrosAppProvider).value?.acomodacionFotosMinimas, 2);
    });
  });
}
