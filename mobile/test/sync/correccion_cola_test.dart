import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/core/errores/error_api.dart';
import 'package:traza_autos/features/sync/domain/job.dart';
import 'package:traza_autos/features/sync/domain/procesador_cola.dart';

import '../support/dobles_sync.dart';

/// Corregir una inspección cerrada desde la app. La corrección se arma en el teléfono y solo al enviarla se le
/// pide al servidor la versión siguiente: el servidor no deja descartar una versión vacía, así que pedirla al
/// pulsar «Corregir» dejaría un borrador que impide volver a corregir en cuanto alguien se arrepiente.
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

  test('un informe normal sigue creando una inspección de la etapa', () async {
    await cola.agregar(informe());

    await procesador.procesar(usuarioPrueba);

    expect(gateway.llamadas, contains('crearInspeccion'));
    expect(gateway.corregidas, isEmpty);
  });

  test('una corrección pide la versión siguiente de la que corrige, no una inspección nueva', () async {
    await cola.agregar(informe(corrigeInspeccionId: 'insp-vieja'));

    await procesador.procesar(usuarioPrueba);

    expect(gateway.corregidas, ['insp-vieja']);
    expect(gateway.llamadas, isNot(contains('crearInspeccion')));
    expect(cola.jobs, isEmpty, reason: 'salió completa, como cualquier informe');
  });

  // Lo que hace que abandonarla no cueste nada: hasta aquí la corrección solo existió en el teléfono.
  test('mientras no se envía, el servidor no sabe nada de ella', () async {
    await cola.agregar(informe(corrigeInspeccionId: 'insp-vieja'));

    expect(gateway.llamadas, isEmpty);
    expect(gateway.corregidas, isEmpty);
  });

  test('si el servidor la rechaza, queda con su motivo y no se crea nada', () async {
    gateway.errores['corregirInspeccion'] = [
      ErrorApi(message: 'El viaje de esta inspección está finalizado', code: 'CONFLICTO', status: 409),
    ];
    await cola.agregar(informe(corrigeInspeccionId: 'insp-vieja'));

    await procesador.procesar(usuarioPrueba);

    final job = cola.jobs.single as InformeJob;
    expect(job.fallido, isTrue, reason: 'un 409 no se arregla reintentando');
    expect(job.error, 'El viaje de esta inspección está finalizado');
    expect(job.inspeccionId, isNull, reason: 'no quedó ninguna versión a medias');
  });

  test('la corrección sobrevive a que se cierre la app: se guarda con el informe', () {
    final guardado = Job.fromJson(informe(corrigeInspeccionId: 'insp-vieja').toJson()) as InformeJob;

    expect(guardado.corrigeInspeccionId, 'insp-vieja');
    expect(guardado.esCorreccion, isTrue);
  });

  test('un informe de antes de que esto existiera se lee igual y no es una corrección', () {
    final sinClave = {...informe().toJson()}..remove('corrige_inspeccion_id');

    expect((Job.fromJson(sinClave) as InformeJob).esCorreccion, isFalse);
  });
}
