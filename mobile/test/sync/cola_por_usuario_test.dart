import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/core/errores/error_api.dart';
import 'package:traza_autos/features/sync/domain/job.dart';
import 'package:traza_autos/features/sync/domain/procesador_cola.dart';

import '../support/arnes.dart';
import '../support/dobles_sync.dart';

/// El escenario de la HU: «Dado un celular donde dos usuarios dejaron envíos pendientes, cuando el segundo tiene
/// la sesión abierta, entonces los envíos del primero no se mandan con el token del segundo».
///
/// Importa porque el servidor atribuye el informe al dueño del token y la cola sobrevive al cierre de sesión: sin
/// esto, el trabajo de una persona quedaba firmado por la siguiente, que es justo lo que la identidad individual
/// viene a evitar.

const ana = 'u-ana';
const luis = 'u-luis';

void main() {
  late ColaEnMemoria cola;
  late GatewayFalso gateway;
  late RedFalsa red;
  late ProcesadorCola procesador;

  setUp(() {
    cola = ColaEnMemoria();
    gateway = GatewayFalso();
    red = RedFalsa();
    procesador = ProcesadorCola(cola: cola, gateway: gateway, red: red, archivos: ArchivosFalsos(const []));
  });

  test('no se envía lo de otra persona', () async {
    await cola.agregar(informe(id: 'de-ana', usuarioId: ana));
    await cola.agregar(informe(id: 'de-luis', usuarioId: luis));

    await procesador.procesar(luis);

    expect(cola.jobs.map((j) => j.id), ['de-ana'], reason: 'el de Ana sigue esperando a Ana');
    expect(cola.jobs.single.intentos, 0, reason: 'ni siquiera gasta un intento');
  });

  test('lo de otra persona tampoco se cuenta ni se muestra', () async {
    await cola.agregar(informe(id: 'de-ana', usuarioId: ana));
    await cola.agregar(informe(id: 'falla-de-ana', usuarioId: ana, fallido: true));

    expect(await procesador.contarPendientes(luis), 0);
    expect(await procesador.contarFallidos(luis), 0);
    expect(await procesador.visibles(luis), isEmpty);
    expect((await procesador.visibles(ana)).map((j) => j.id), ['de-ana', 'falla-de-ana']);
  });

  test('cada quien envía lo suyo cuando le toca', () async {
    await cola.agregar(informe(id: 'de-ana', usuarioId: ana));
    await cola.agregar(informe(id: 'de-luis', usuarioId: luis));

    await procesador.procesar(luis);
    await procesador.procesar(ana);

    expect(cola.jobs, isEmpty);
  });

  group('jobs sin dueño, encolados por una versión anterior', () {
    test('se ven, pero no se envían solos', () async {
      await cola.agregar(informe(id: 'viejo', usuarioId: null));

      await procesador.procesar(luis);

      expect(cola.jobs.single.id, 'viejo');
      expect(gateway.llamadas, isEmpty);
      expect((await procesador.visibles(luis)).single.esHuerfano, isTrue,
          reason: 'visible para que alguien lo resuelva, no perdido en silencio');
    });

    test('la sesión guardada los adopta al arrancar', () async {
      await cola.agregar(informe(id: 'viejo', usuarioId: null));

      expect(await procesador.adoptarHuerfanos(ana), 1);

      expect(cola.jobs.single.usuarioId, ana);
      await procesador.procesar(ana);
      expect(cola.jobs, isEmpty);
    });

    test('adoptar no toca lo que ya tiene dueño', () async {
      await cola.agregar(informe(id: 'de-ana', usuarioId: ana));
      await cola.agregar(informe(id: 'viejo', usuarioId: null));

      expect(await procesador.adoptarHuerfanos(luis), 1);

      expect(cola.jobs.firstWhere((j) => j.id == 'de-ana').usuarioId, ana);
      expect(cola.jobs.firstWhere((j) => j.id == 'viejo').usuarioId, luis);
    });

    // Un informe solo cambia de autor si alguien lo decide: reintentar a mano es ese acto explícito.
    test('reintentar a mano lo hace propio de quien reintenta', () async {
      await cola.agregar(informe(id: 'viejo', usuarioId: null, fallido: true, intentos: 8));

      await procesador.reintentar('viejo', luis);

      final job = cola.jobs.single;
      expect(job.usuarioId, luis);
      expect(job.fallido, isFalse);
      expect(job.intentos, 0);
    });
  });

  test('el dueño sobrevive a los reintentos y al progreso', () async {
    const job = InformeJob(
      id: 'j',
      etiqueta: 'Informe',
      creadoEn: 1,
      usuarioId: ana,
      etapaId: 'e',
      momento: 'informe',
      fecha: '2026-01-02T10:00:00.000Z',
      lat: 1,
      lng: 2,
    );

    expect(job.conIntento(intentos: 3, error: 'x', fallido: false).usuarioId, ana);
    expect(job.conProgreso(inspeccionId: 'i-1').usuarioId, ana);
    expect(InformeJob.fromJson(job.toJson()).usuarioId, ana, reason: 'viaja en el payload, sin migrar la tabla');
  });

  test('lo de otra persona se ve en el dispositivo aunque no se envie ni se cuente', () async {
    await cola.agregar(informe(id: 'de-ana', usuarioId: ana));
    await cola.agregar(informe(id: 'de-luis', usuarioId: luis));

    // Si lo ajeno no apareciera, quien usa el equipo no tendria como saber por que hay trabajo que nunca sale.
    expect((await procesador.enElDispositivo()).map((j) => j.id), ['de-ana', 'de-luis']);
    expect((await procesador.visibles(luis)).map((j) => j.id), ['de-luis']);
    expect(await procesador.contarPendientes(luis), 1);
  });

  group('la pausa por PIN temporal', () {
    setUp(() {
      gateway.errores['crearInspeccion'] = [
        const ErrorApi(message: 'Debes cambiar tu PIN antes de continuar', code: codigoCambioPinRequerido, status: 403),
      ];
    });

    // La cola queda en pausa hasta que la persona cambie el PIN: no es un fallo del envio, asi que no puede
    // gastar uno de los ocho intentos. Con ocho pausas el informe habria muerto sin que nada estuviera mal.
    test('no gasta un intento ni marca el job como fallido', () async {
      await cola.agregar(informe(id: 'j', usuarioId: luis));

      await procesador.procesar(luis);

      final job = cola.jobs.single;
      expect(job.intentos, 0);
      expect(job.fallido, isFalse);
    });

    test('un 403 que no sea el del PIN si gasta intento y marca fallido', () async {
      gateway.errores['crearInspeccion'] = [errorApi(403, 'No tiene permisos sobre esta inspeccion')];
      await cola.agregar(informe(id: 'j', usuarioId: luis));

      await procesador.procesar(luis);

      final job = cola.jobs.single;
      expect(job.intentos, 1);
      expect(job.fallido, isTrue);
    });
  });

  group('un informe que el servidor rechaza por permisos', () {
    // Solo puede pasar con un job adoptado: la inspeccion la creo otra persona y el servidor no deja
    // continuarla. Nadie mas que su dueno la termina, asi que quien esta mirando no deberia poder borrarla.
    test('guarda el codigo del error, no solo el mensaje', () async {
      gateway.errores['crearInspeccion'] = [
        const ErrorApi(message: 'No tiene permisos sobre esta inspeccion', code: codigoProhibido, status: 403),
      ];
      await cola.agregar(informe(id: 'j', usuarioId: luis));

      await procesador.procesar(luis);

      final job = cola.jobs.single;
      expect(job.codigoError, codigoProhibido);
      expect(job.fallido, isTrue);
      expect(job.esProhibido, isTrue, reason: 'la pantalla decide por el codigo, no comparando textos');
    });

    test('otro fallo no se marca como prohibido', () async {
      gateway.errores['crearInspeccion'] = [
        const ErrorApi(message: 'Momento invalido', code: 'VALIDATION_ERROR', status: 400),
      ];
      await cola.agregar(informe(id: 'j', usuarioId: luis));

      await procesador.procesar(luis);

      final job = cola.jobs.single;
      expect(job.codigoError, 'VALIDATION_ERROR');
      expect(job.esProhibido, isFalse, reason: 'ese si se puede descartar: es la unica salida de la cola');
    });

    test('el codigo viaja en el payload y sobrevive a releer la cola', () async {
      const job = InformeJob(
        id: 'j',
        etiqueta: 'Informe',
        creadoEn: 1,
        usuarioId: luis,
        error: 'No tiene permisos',
        codigoError: codigoProhibido,
        fallido: true,
        etapaId: 'e',
        momento: 'informe',
        fecha: '2026-01-02T10:00:00.000Z',
        lat: 1,
        lng: 2,
      );

      expect(InformeJob.fromJson(job.toJson()).esProhibido, isTrue);
    });

    test('reintentar a mano limpia el codigo', () async {
      await cola.agregar(informe(id: 'j', usuarioId: null, fallido: true));
      gateway.errores['crearInspeccion'] = [
        const ErrorApi(message: 'x', code: codigoProhibido, status: 403),
      ];

      await procesador.reintentar('j', luis);

      expect(cola.jobs.single.codigoError, isNull);
      expect(cola.jobs.single.esProhibido, isFalse);
    });
  });
}
