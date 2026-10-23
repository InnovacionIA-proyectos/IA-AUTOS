import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/features/auth/domain/sesion.dart';
import 'package:traza_autos/features/auth/presentation/auth_controller.dart';

import '../support/arnes.dart';

/// El menú sale de lo que el servidor dice que la persona puede hacer, no de una tabla de roles repetida en la app.
/// Se piden al entrar y se guardan con la sesión para que el menú exista también sin señal.

Future<ProviderContainer> iniciar(Arnes arnes) async {
  final contenedor = arnes.contenedor();
  addTearDown(contenedor.dispose);
  contenedor.read(authControllerProvider);
  await pumpEventQueue();
  return contenedor;
}

Usuario usuarioDe(ProviderContainer c) => c.read(authControllerProvider).sesion!.usuario;

void main() {
  group('al iniciar sesión', () {
    test('se traen del perfil y se guardan junto a la sesión', () async {
      final arnes = Arnes(sesion: null)
        ..auth.capacidadesDelPerfil = [Capacidad.verViajes, Capacidad.registrarInspecciones];
      final contenedor = await iniciar(arnes);

      await contenedor.read(authControllerProvider.notifier).iniciarSesion('ana@boterosoto.com.co', 'x');

      expect(usuarioDe(contenedor).capacidades, [Capacidad.verViajes, Capacidad.registrarInspecciones]);
      expect(arnes.almacen.sesion!.usuario.capacidades, contains(Capacidad.registrarInspecciones),
          reason: 'al reabrir la app el menú tiene que existir sin pedir nada');
    });

    test('también con cédula y PIN: /me está exento del 403 por PIN temporal', () async {
      final arnes = Arnes(sesion: null)
        ..auth.sesionConPin = sesionConPinTemporal
        ..auth.capacidadesDelPerfil = [Capacidad.verViajes];
      final contenedor = await iniciar(arnes);

      await contenedor.read(authControllerProvider.notifier).iniciarSesionConPin('1234567', '482915');

      expect(usuarioDe(contenedor).capacidades, [Capacidad.verViajes]);
    });

    // Entrar no puede depender de una segunda petición: si /me no responde, se entra igual.
    test('si el perfil no responde se entra igual, con el respaldo por rol', () async {
      final arnes = Arnes(sesion: null)..auth.errorCapacidades = errorApi(500);
      final contenedor = await iniciar(arnes);

      final error = await contenedor.read(authControllerProvider.notifier).iniciarSesion('ana@boterosoto.com.co', 'x');

      expect(error, isNull);
      expect(contenedor.read(authControllerProvider).fase, FaseAuth.autenticado);
      final usuario = usuarioDe(contenedor);
      expect(usuario.capacidades, isEmpty, reason: 'no se inventan: se marca que no se supieron');
      expect(usuario.puede(Capacidad.verViajes), isTrue, reason: 'pero el respaldo por rol deja trabajar');
    });
  });

  group('sesión restaurada de una versión anterior', () {
    test('sin capacidades guardadas no deja a nadie sin menú', () async {
      const vieja = Sesion(
        token: 'jwt',
        usuario: Usuario(id: 'u1', nombre: 'Ana', email: 'a@b.co', rol: 'inspector'),
      );
      final contenedor = await iniciar(Arnes(sesion: vieja));

      final usuario = usuarioDe(contenedor);
      expect(usuario.capacidades, isEmpty);
      expect(usuario.puede(Capacidad.registrarInspecciones), isTrue);
      expect(usuario.puede(Capacidad.gestionarViajes), isFalse);
    });

    test('las guardadas se conservan y no se vuelven a pedir', () async {
      final sesion = Sesion(
        token: 'jwt',
        usuario: const Usuario(id: 'u1', nombre: 'Ana', email: 'a@b.co', rol: 'inspector')
            .copiar(capacidades: [Capacidad.verViajes]),
      );
      final arnes = Arnes(sesion: sesion);
      final contenedor = await iniciar(arnes);

      expect(usuarioDe(contenedor).capacidades, [Capacidad.verViajes]);
      expect(arnes.auth.consultasDePerfil, 0, reason: 'restaurar la sesión funciona sin red');
    });
  });

  group('el respaldo por rol reproduce lo que autoriza el backend', () {
    test('el conductor registra informes pero no gestiona nada', () {
      final c = capacidadesDeRol('conductor');
      expect(c, contains(Capacidad.registrarInspecciones));
      expect(c, isNot(contains(Capacidad.gestionarViajes)));
      expect(c, isNot(contains(Capacidad.cargarVehiculos)));
    });

    test('el coordinador de calidad gestiona viajes pero no registra informes', () {
      final c = capacidadesDeRol('coordinador_calidad');
      expect(c, contains(Capacidad.gestionarViajes));
      expect(c, isNot(contains(Capacidad.registrarInspecciones)));
    });

    test('el comercial solo mira', () {
      expect(
        capacidadesDeRol('comercial'),
        [Capacidad.verInspecciones, Capacidad.verViajes, Capacidad.verReportes],
      );
    });

    test('un rol desconocido no deja a nadie encerrado, pero tampoco le abre nada', () {
      final c = capacidadesDeRol('rol_que_no_existe_todavia');
      expect(c, contains(Capacidad.verViajes));
      expect(c, isNot(contains(Capacidad.registrarInspecciones)));
    });
  });

  test('lo que manda el servidor pesa más que el respaldo', () {
    // Si allá le quitan una capacidad a un rol, la app tiene que obedecer sin esperar una versión nueva.
    final usuario = const Usuario(id: 'u1', nombre: 'Ana', email: 'a@b.co', rol: 'inspector')
        .copiar(capacidades: [Capacidad.verViajes]);

    expect(usuario.puede(Capacidad.registrarInspecciones), isFalse);
    expect(capacidadesDeRol('inspector'), contains(Capacidad.registrarInspecciones));
  });
}
