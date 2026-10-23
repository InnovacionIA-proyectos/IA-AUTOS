import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/features/auth/presentation/auth_controller.dart';
import 'package:traza_autos/features/sync/presentation/sync_controller.dart';

import '../support/arnes.dart';
import '../support/dobles_sync.dart';

Future<ProviderContainer> iniciar(Arnes arnes) async {
  final contenedor = arnes.contenedor();
  addTearDown(contenedor.dispose);
  contenedor.listen(syncControllerProvider, (_, _) {}); // la mantiene viva, como hace la app
  await pumpEventQueue();
  return contenedor;
}

void main() {
  test('con sesión iniciada y señal estable envía lo que haya en la cola al arrancar', () async {
    final arnes = Arnes();
    await arnes.cola.agregar(informe(id: 'a'));
    await arnes.cola.agregar(informe(id: 'b'));

    final contenedor = await iniciar(arnes);

    expect(arnes.cola.jobs, isEmpty);
    expect(contenedor.read(syncControllerProvider).pendientes, 0);
  });

  test('sin sesión NO procesa la cola (no gasta intentos con 401)', () async {
    final arnes = Arnes(sesion: null);
    await arnes.cola.agregar(informe());

    await iniciar(arnes);

    expect(arnes.gateway.llamadas, isEmpty);
    expect(arnes.cola.jobs.single.intentos, 0);
  });

  // Con un PIN temporal el servidor responde 403 a todo salvo cambiarlo, y un 403 no se arregla reintentando:
  // si la cola saliera igual, el conductor encontraría sus informes marcados como fallidos sin haber hecho nada.
  test('con un PIN temporal NO procesa la cola (el 403 la marcaría fallida)', () async {
    final arnes = Arnes(sesion: sesionConPinTemporal);
    await arnes.cola.agregar(informe());

    await iniciar(arnes);

    expect(arnes.gateway.llamadas, isEmpty);
    expect(arnes.cola.jobs.single.intentos, 0);
    expect(arnes.cola.jobs.single.fallido, isFalse);
  });

  // El arranque no es el único disparador: con el PIN temporal puesto, ni la vuelta de la señal ni el reintento
  // manual deben sacar la cola.
  test('con un PIN temporal tampoco sale al volver la señal ni al reintentar a mano', () async {
    final arnes = Arnes(sesion: sesionConPinTemporal)..red.estable = false;
    await arnes.cola.agregar(informe(id: 'a'));
    final contenedor = await iniciar(arnes);

    arnes.red.cambiarA(true);
    await pumpEventQueue();
    await contenedor.read(syncControllerProvider.notifier).reintentar('a');
    await pumpEventQueue();

    expect(arnes.gateway.llamadas, isEmpty);
    expect(arnes.cola.jobs.single.intentos, 0);
  });

  test('al cambiar el PIN arranca la cola sola, sin reiniciar la app', () async {
    final arnes = Arnes(sesion: sesionConPinTemporal);
    await arnes.cola.agregar(informe(usuarioId: sesionConPinTemporal.usuario.id));
    final contenedor = await iniciar(arnes);
    expect(arnes.cola.jobs, hasLength(1));

    await contenedor.read(authControllerProvider.notifier).cambiarPin('482915', '730194');
    await pumpEventQueue();

    expect(arnes.cola.jobs, isEmpty);
    expect(contenedor.read(syncControllerProvider).pendientes, 0);
  });

  test('sin señal cuenta lo pendiente y se marca como sin conexión', () async {
    final arnes = Arnes()..red.estable = false;
    await arnes.cola.agregar(informe());

    final contenedor = await iniciar(arnes);

    final estado = contenedor.read(syncControllerProvider);
    expect(estado.online, isFalse);
    expect(estado.pendientes, 1);
    expect(arnes.gateway.llamadas, isEmpty);
  });

  test('al recuperar una señal estable sincroniza solo', () async {
    final arnes = Arnes()..red.estable = false;
    await arnes.cola.agregar(informe());
    final contenedor = await iniciar(arnes);
    expect(contenedor.read(syncControllerProvider).pendientes, 1);

    arnes.red.cambiarA(true);
    await pumpEventQueue();

    final estado = contenedor.read(syncControllerProvider);
    expect(estado.online, isTrue);
    expect(estado.pendientes, 0);
    expect(arnes.cola.jobs, isEmpty);
  });

  test('al perder la señal solo actualiza el estado, no intenta enviar', () async {
    final arnes = Arnes();
    final contenedor = await iniciar(arnes);

    arnes.red.cambiarA(false);
    await pumpEventQueue();

    expect(contenedor.read(syncControllerProvider).online, isFalse);
    expect(arnes.gateway.llamadas, isEmpty);
  });

  test('un fallo no recuperable queda contado como "fallido", no como pendiente', () async {
    final arnes = Arnes();
    arnes.gateway.errores['crearInspeccion'] = [errorApi(422, 'Datos inválidos')];
    await arnes.cola.agregar(informe());

    final contenedor = await iniciar(arnes);

    final estado = contenedor.read(syncControllerProvider);
    expect((estado.pendientes, estado.fallidos), (0, 1));
    expect(arnes.cola.jobs.single.error, 'Datos inválidos');
  });

  test('reintentar un fallido lo reenvía y limpia el contador', () async {
    final arnes = Arnes();
    arnes.gateway.errores['crearInspeccion'] = [errorApi(422)];
    await arnes.cola.agregar(informe());
    final contenedor = await iniciar(arnes);
    expect(contenedor.read(syncControllerProvider).fallidos, 1);

    await contenedor.read(syncControllerProvider.notifier).reintentar('j1');
    await pumpEventQueue();

    expect(contenedor.read(syncControllerProvider).fallidos, 0);
    expect(arnes.cola.jobs, isEmpty);
  });

  test('descartar un fallido lo quita sin enviarlo', () async {
    final arnes = Arnes();
    arnes.gateway.errores['crearInspeccion'] = [errorApi(422)];
    await arnes.cola.agregar(informe());
    final contenedor = await iniciar(arnes);

    await contenedor.read(syncControllerProvider.notifier).descartar('j1');

    expect(contenedor.read(syncControllerProvider).fallidos, 0);
    expect(arnes.cola.jobs, isEmpty);
    expect(arnes.gateway.llamadas.where((l) => l == 'cerrarInspeccion'), isEmpty);
  });

  test('sincronizar a demanda no se reentra mientras ya hay una pasada en curso', () async {
    final arnes = Arnes();
    await arnes.cola.agregar(informe());
    final contenedor = await iniciar(arnes);
    await arnes.cola.agregar(informe(id: 'otro'));
    final sync = contenedor.read(syncControllerProvider.notifier);

    await Future.wait([sync.sincronizar(), sync.sincronizar()]);

    expect(arnes.gateway.llamadas.where((l) => l == 'crearInspeccion'), hasLength(2)); // uno por job, sin duplicar
  });
}
