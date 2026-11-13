import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/features/auth/presentation/auth_controller.dart';
import 'package:traza_autos/features/sync/domain/desenlace_envio.dart';
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

  // Hasta ahora la cola solo despertaba al cambiar la señal, al entrar o a mano: con la red conectada y el
  // servidor caído no se movía nunca, y la pantalla no podía prometer cuándo lo volvería a intentar.
  group('el reintento por tiempo', () {
    test('con la red conectada y el servidor sin responder, anuncia cuándo lo volverá a intentar', () async {
      final arnes = Arnes();
      arnes.gateway.errores['crearInspeccion'] = [errorHttp(503, 'El servidor no responde')];
      await arnes.cola.agregar(informe());

      final contenedor = await iniciar(arnes);

      expect(arnes.cola.jobs.single.fallido, isFalse, reason: 'un 503 se reintenta solo');
      expect(contenedor.read(syncControllerProvider).proximoIntento, isNotNull);
    });

    test('sin nada pendiente no hay nada que anunciar ni que despertar', () async {
      final contenedor = await iniciar(Arnes());

      expect(contenedor.read(syncControllerProvider).pendientes, 0);
      expect(contenedor.read(syncControllerProvider).proximoIntento, isNull);
    });

    // Sin señal el reloj sobra: de eso ya se encarga la escucha de la red, y despertar el teléfono para no poder
    // enviar nada es justo lo que agota la batería en una jornada de patio.
    test('sin señal no se programa nada', () async {
      final arnes = Arnes()..red.estable = false;
      await arnes.cola.agregar(informe());

      final contenedor = await iniciar(arnes);

      expect(arnes.cola.jobs, hasLength(1));
      expect(contenedor.read(syncControllerProvider).proximoIntento, isNull);
    });

    test('lo que el servidor rechazó de verdad espera a una persona, no al reloj', () async {
      final arnes = Arnes();
      arnes.gateway.errores['crearInspeccion'] = [errorHttp(409, 'El viaje está finalizado')];
      await arnes.cola.agregar(informe());

      final contenedor = await iniciar(arnes);

      expect(arnes.cola.jobs.single.fallido, isTrue);
      expect(contenedor.read(syncControllerProvider).proximoIntento, isNull);
    });
  });

  // La cola entregaba en silencio: el informe recién enviado no aparecía en la lista y el historial de daños
  // seguía mostrando la versión anterior hasta que alguien bajaba a refrescar a mano.
  group('avisar de que lo enviado ya está en el servidor', () {
    test('al aceptar el servidor un envío, el contador sube', () async {
      final arnes = Arnes();
      await arnes.cola.agregar(informe(id: 'a'));

      final contenedor = await iniciar(arnes);

      expect(contenedor.read(syncControllerProvider).entregados, 1);
    });

    test('cada envío aceptado suma: dos pasadas dejan el contador en dos', () async {
      final arnes = Arnes();
      await arnes.cola.agregar(informe(id: 'a'));
      final contenedor = await iniciar(arnes);
      expect(contenedor.read(syncControllerProvider).entregados, 1);

      await arnes.cola.agregar(informe(id: 'b'));
      await contenedor.read(syncControllerProvider.notifier).sincronizar();

      expect(contenedor.read(syncControllerProvider).entregados, 2);
    });

    // Si subiera sin que nada saliera, cada pasada en vacío recargaría todas las pantallas sin motivo.
    test('una pasada que no entrega nada no mueve el contador', () async {
      final arnes = Arnes();
      arnes.gateway.errores['crearInspeccion'] = [errorHttp(503, 'El servidor no responde')];
      await arnes.cola.agregar(informe());

      final contenedor = await iniciar(arnes);

      expect(contenedor.read(syncControllerProvider).entregados, 0);
    });
  });

  group('por qué no se tocó la cola', () {
    test('con la API inalcanzable se dice eso, para no culpar a la señal', () async {
      final arnes = Arnes()..red.alcanzable = false;
      await arnes.cola.agregar(informe());

      final contenedor = await iniciar(arnes);

      expect(arnes.cola.jobs, hasLength(1), reason: 'no se intentó: el job no gasta un intento');
      expect(contenedor.read(syncControllerProvider).pausa, MotivoPausa.apiCaida);
    });

    test('sin señal el motivo es la señal', () async {
      final arnes = Arnes()..red.estable = false;
      await arnes.cola.agregar(informe());

      final contenedor = await iniciar(arnes);

      expect(contenedor.read(syncControllerProvider).pausa, MotivoPausa.sinSenal);
    });

    test('una pasada que sí recorrió la cola no deja motivo de pausa', () async {
      final arnes = Arnes();
      await arnes.cola.agregar(informe());

      final contenedor = await iniciar(arnes);

      expect(contenedor.read(syncControllerProvider).pausa, isNull);
    });
  });

  // Con el servidor caído la pasada se detiene antes de tocar ningún job, así que `intentos` no sube. Sin contar
  // las pausas, la espera se quedaba en quince segundos para siempre: una jornada de patio serían miles de
  // despertares, justo lo que la escala existe para evitar.
  group('la espera crece aunque el servidor no deje ni intentarlo', () {
    Future<Duration?> faltaTras(ProviderContainer contenedor, int pasadas) async {
      for (var i = 0; i < pasadas; i++) {
        await contenedor.read(syncControllerProvider.notifier).sincronizar();
      }
      final cuando = contenedor.read(syncControllerProvider).proximoIntento;
      return cuando?.difference(DateTime.now());
    }

    test('el primer reintento con el servidor caído llega pronto, no a los diez minutos', () async {
      final arnes = Arnes()..red.alcanzable = false;
      await arnes.cola.agregar(informe());
      final contenedor = await iniciar(arnes);

      final falta = contenedor.read(syncControllerProvider).proximoIntento!.difference(DateTime.now());

      expect(falta.inSeconds, lessThanOrEqualTo(30), reason: 'un servidor que parpadea no debe costar una jornada');
      expect(falta.inSeconds, greaterThan(5));
    });

    test('cada pasada en vano espacia más la siguiente', () async {
      final arnes = Arnes()..red.alcanzable = false;
      await arnes.cola.agregar(informe());
      final contenedor = await iniciar(arnes);
      final inicial = contenedor.read(syncControllerProvider).proximoIntento!.difference(DateTime.now());

      final despues = await faltaTras(contenedor, 1);
      final masTarde = await faltaTras(contenedor, 1);

      expect(despues!.inSeconds, greaterThan(inicial.inSeconds), reason: 'sin esto se queda en 15 s para siempre');
      expect(masTarde!.inSeconds, greaterThan(despues.inSeconds), reason: 'y sigue creciendo');
    });

    test('la espera no crece sin tope: la jornada no se pasa despertando el teléfono', () async {
      final arnes = Arnes()..red.alcanzable = false;
      await arnes.cola.agregar(informe());
      final contenedor = await iniciar(arnes);

      final falta = await faltaTras(contenedor, 20);

      expect(falta!.inMinutes, lessThanOrEqualTo(10));
    });

    test('ninguna de esas pausas le gasta intentos al envío: el servidor no lo rechazó', () async {
      final arnes = Arnes()..red.alcanzable = false;
      await arnes.cola.agregar(informe());
      final contenedor = await iniciar(arnes);

      await faltaTras(contenedor, 5);

      expect(arnes.cola.jobs.single.intentos, 0);
      expect(arnes.cola.jobs.single.fallido, isFalse, reason: 'estar caído no puede dar por perdido un envío');
    });

    test('cuando el servidor vuelve, la espera se reinicia', () async {
      final arnes = Arnes()..red.alcanzable = false;
      await arnes.cola.agregar(informe(id: 'a'));
      final contenedor = await iniciar(arnes);
      await faltaTras(contenedor, 3);

      arnes.red.alcanzable = true;
      await contenedor.read(syncControllerProvider.notifier).sincronizar();
      await arnes.cola.agregar(informe(id: 'b'));
      arnes.red.alcanzable = false;
      final falta = await faltaTras(contenedor, 1);

      expect(falta!.inSeconds, lessThanOrEqualTo(15), reason: 'la cuenta de pausas volvió a cero al entregar');
    });
  });
}
