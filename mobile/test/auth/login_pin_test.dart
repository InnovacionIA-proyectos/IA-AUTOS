import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/core/errores/error_api.dart';
import 'package:traza_autos/features/auth/presentation/auth_controller.dart';

import '../support/arnes.dart';

ErrorApi bloqueada(DateTime hasta) => ErrorApi(
      message: 'Cuenta bloqueada',
      code: 'ACCOUNT_LOCKED',
      status: 423,
      details: {'bloqueado_hasta': hasta.toUtc().toIso8601String()},
    );

Future<ProviderContainer> iniciar(Arnes arnes) async {
  final contenedor = arnes.contenedor();
  addTearDown(contenedor.dispose);
  contenedor.read(authControllerProvider);
  await pumpEventQueue();
  return contenedor;
}

void main() {
  group('ingreso con cédula y PIN', () {
    test('guarda la sesión del personal de campo, que puede no tener correo', () async {
      final arnes = Arnes(sesion: null)..auth.sesionConPin = sesionConPinTemporal;
      final contenedor = await iniciar(arnes);

      final error = await contenedor.read(authControllerProvider.notifier).iniciarSesionConPin(' 1.234.567 ', '482915');

      expect(error, isNull);
      final estado = contenedor.read(authControllerProvider);
      expect(estado.fase, FaseAuth.autenticado);
      expect(estado.sesion!.usuario.cedula, '1234567');
      expect(arnes.almacen.sesion, isNotNull, reason: 'la sesión se guarda en el almacén seguro');
    });

    test('una cédula o un PIN errados dan un mensaje que no distingue cuál falló', () async {
      final arnes = Arnes(sesion: null)..auth.error = errorApi(401);
      final contenedor = await iniciar(arnes);

      final error = await contenedor.read(authControllerProvider.notifier).iniciarSesionConPin('1234567', '0000');

      expect(error, 'Cédula o PIN incorrectos.');
      expect(contenedor.read(authControllerProvider).fase, FaseAuth.noAutenticado);
    });

    test('una cuenta bloqueada dice hasta qué hora', () async {
      final hasta = DateTime(2026, 3, 4, 14, 35);
      final arnes = Arnes(sesion: null)..auth.error = bloqueada(hasta);
      final contenedor = await iniciar(arnes);

      final error = await contenedor.read(authControllerProvider.notifier).iniciarSesionConPin('1234567', '0000');

      expect(error, contains('14:35'));
      expect(error, contains('bloqueada'));
    });

    test('sin la hora del bloqueo igual se explica qué pasó', () async {
      final arnes = Arnes(sesion: null)
        ..auth.error = const ErrorApi(message: 'x', code: 'ACCOUNT_LOCKED', status: 423);
      final contenedor = await iniciar(arnes);

      final error = await contenedor.read(authControllerProvider.notifier).iniciarSesionConPin('1234567', '0000');

      expect(error, contains('bloqueada'));
    });
  });

  group('cambio de PIN forzado', () {
    test('tras cambiarlo deja de exigirse y el token sigue siendo el mismo', () async {
      final arnes = Arnes(sesion: sesionConPinTemporal);
      final contenedor = await iniciar(arnes);
      expect(contenedor.read(authControllerProvider).sesion!.usuario.debeCambiarPin, isTrue);

      final error = await contenedor.read(authControllerProvider.notifier).cambiarPin('482915', '730194');

      expect(error, isNull);
      final sesion = contenedor.read(authControllerProvider).sesion!;
      expect(sesion.usuario.debeCambiarPin, isFalse);
      expect(sesion.token, 'jwt-pin', reason: 'el contrato dice que el mismo token sirve tras el cambio');
      expect(sesion.usuario.cedula, '1234567');
      expect(arnes.auth.cambiosDePin.single, ('482915', '730194'));
      expect(arnes.almacen.sesion!.usuario.debeCambiarPin, isFalse, reason: 'también se guarda');
    });

    test('un PIN actual errado muestra el error del campo que manda el servidor', () async {
      final arnes = Arnes(sesion: sesionConPinTemporal)
        ..auth.errorCambioPin = const ErrorApi(
          message: 'Datos inválidos',
          code: 'VALIDATION_ERROR',
          status: 400,
          details: {
            'fieldErrors': {
              'pin_actual': ['El PIN actual no es correcto'],
            },
          },
        );
      final contenedor = await iniciar(arnes);

      final error = await contenedor.read(authControllerProvider.notifier).cambiarPin('000000', '730194');

      expect(error, 'El PIN actual no es correcto');
      expect(contenedor.read(authControllerProvider).sesion!.usuario.debeCambiarPin, isTrue);
    });

    test('un PIN actual errado puede bloquear la cuenta', () async {
      final arnes = Arnes(sesion: sesionConPinTemporal)..auth.errorCambioPin = bloqueada(DateTime(2026, 3, 4, 9, 5));
      final contenedor = await iniciar(arnes);

      expect(await contenedor.read(authControllerProvider.notifier).cambiarPin('000000', '730194'), contains('09:05'));
    });

    test('sin sesión no intenta nada', () async {
      final arnes = Arnes(sesion: null);
      final contenedor = await iniciar(arnes);

      expect(await contenedor.read(authControllerProvider.notifier).cambiarPin('1', '2'), isNotNull);
      expect(arnes.auth.cambiosDePin, isEmpty);
    });

    // El PIN ya cambió en el servidor: si además se dejara escapar el fallo del almacén, la pantalla se quedaría
    // con el botón deshabilitado y sin forma de salir salvo matando la app.
    test('si el almacén seguro falla igual se sigue adelante, no se queda encerrada', () async {
      final arnes = Arnes(sesion: sesionConPinTemporal)..almacen.fallaAlGuardar = true;
      final contenedor = await iniciar(arnes);

      final error = await contenedor.read(authControllerProvider.notifier).cambiarPin('482915', '730194');

      expect(error, isNull);
      expect(contenedor.read(authControllerProvider).sesion!.usuario.debeCambiarPin, isFalse);
      expect(contenedor.read(authControllerProvider).puedeOperar, isTrue);
      expect(arnes.almacen.sesion, isNull, reason: 'mejor entrar de nuevo que restaurar la marca vieja');
    });
  });

  group('PIN reiniciado con la sesión abierta', () {
    test('el 403 del servidor marca la sesión y manda a cambiar el PIN', () async {
      final arnes = Arnes();
      final contenedor = await iniciar(arnes);
      expect(contenedor.read(authControllerProvider).puedeOperar, isTrue);

      contenedor.read(authControllerProvider.notifier).requiereCambioDePin();
      await pumpEventQueue();

      final estado = contenedor.read(authControllerProvider);
      expect(estado.fase, FaseAuth.autenticado, reason: 'el token sigue valiendo para cambiar el PIN');
      expect(estado.sesion!.usuario.debeCambiarPin, isTrue);
      expect(estado.puedeOperar, isFalse);
      expect(arnes.almacen.sesion!.usuario.debeCambiarPin, isTrue, reason: 'también al reabrir la app');
    });

    test('no hace nada sin sesión ni cuando ya estaba marcada', () async {
      final arnes = Arnes(sesion: null);
      final contenedor = await iniciar(arnes);

      contenedor.read(authControllerProvider.notifier).requiereCambioDePin();

      expect(contenedor.read(authControllerProvider).fase, FaseAuth.noAutenticado);
    });
  });
}
