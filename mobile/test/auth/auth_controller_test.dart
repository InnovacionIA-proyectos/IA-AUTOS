import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/features/auth/presentation/auth_controller.dart';

import '../support/arnes.dart';
import '../support/dobles_sync.dart';

Future<ProviderContainer> iniciar(Arnes arnes) async {
  final contenedor = arnes.contenedor();
  addTearDown(contenedor.dispose);
  contenedor.read(authControllerProvider);
  await pumpEventQueue();
  return contenedor;
}

void main() {
  group('restauración al arrancar', () {
    test('arranca "cargando" y luego restaura la sesión guardada', () async {
      final arnes = Arnes();
      final contenedor = arnes.contenedor();
      addTearDown(contenedor.dispose);

      expect(contenedor.read(authControllerProvider).fase, FaseAuth.cargando);
      await pumpEventQueue();

      final estado = contenedor.read(authControllerProvider);
      expect(estado.fase, FaseAuth.autenticado);
      expect(estado.sesion!.usuario.nombre, 'Ana Pérez');
    });

    test('sin sesión guardada queda no autenticado', () async {
      final contenedor = await iniciar(Arnes(sesion: null));
      expect(contenedor.read(authControllerProvider).fase, FaseAuth.noAutenticado);
    });
  });

  group('iniciarSesion', () {
    test('con credenciales válidas guarda la sesión de forma segura y autentica', () async {
      final arnes = Arnes(sesion: null);
      final contenedor = await iniciar(arnes);

      final error = await contenedor.read(authControllerProvider.notifier).iniciarSesion('  ana@boterosoto.com.co ', 'Clave-1');

      expect(error, isNull);
      expect(arnes.auth.intentos.single, ('ana@boterosoto.com.co', 'Clave-1')); // el correo se recorta
      expect(arnes.almacen.sesion?.token, 'jwt-prueba');
      expect(contenedor.read(authControllerProvider).fase, FaseAuth.autenticado);
    });

    for (final (status, texto) in [
      (401, 'Correo o contraseña incorrectos.'),
      (429, 'Demasiados intentos'),
      (0, 'No se pudo conectar'),
    ]) {
      test('error $status: devuelve un mensaje claro y no autentica ni guarda nada', () async {
        final arnes = Arnes(sesion: null)..auth.error = errorApi(status);
        final contenedor = await iniciar(arnes);

        final error = await contenedor.read(authControllerProvider.notifier).iniciarSesion('a@b.co', 'x');

        expect(error, contains(texto));
        expect(arnes.almacen.sesion, isNull);
        expect(contenedor.read(authControllerProvider).fase, FaseAuth.noAutenticado);
      });
    }

    test('otro error de la API muestra su mensaje', () async {
      final arnes = Arnes(sesion: null)..auth.error = errorApi(503, 'Servicio en mantenimiento');
      final contenedor = await iniciar(arnes);

      expect(await contenedor.read(authControllerProvider.notifier).iniciarSesion('a@b.co', 'x'), 'Servicio en mantenimiento');
    });

    test('un fallo inesperado no revela detalles internos', () async {
      final arnes = Arnes(sesion: null)..auth.error = StateError('detalle interno');
      final contenedor = await iniciar(arnes);

      final error = await contenedor.read(authControllerProvider.notifier).iniciarSesion('a@b.co', 'x');

      expect(error, isNot(contains('detalle interno')));
    });
  });

  group('cierre de sesión', () {
    test('cerrarSesion borra el almacén seguro', () async {
      final arnes = Arnes();
      final contenedor = await iniciar(arnes);

      await contenedor.read(authControllerProvider.notifier).cerrarSesion();

      expect(arnes.almacen.sesion, isNull);
      expect(contenedor.read(authControllerProvider).fase, FaseAuth.noAutenticado);
    });

    test('sesionExpirada cierra la sesión con un aviso y conserva la cola', () async {
      final arnes = Arnes();
      await arnes.cola.agregar(informe());
      final contenedor = await iniciar(arnes);

      contenedor.read(authControllerProvider.notifier).sesionExpirada();

      final estado = contenedor.read(authControllerProvider);
      expect(estado.fase, FaseAuth.noAutenticado);
      expect(estado.aviso, contains('expiró'));
      expect(arnes.almacen.sesion, isNull);
      expect(arnes.cola.jobs, hasLength(1));
    });

    test('sesionExpirada sin sesión no hace nada (evita avisos duplicados)', () async {
      final contenedor = await iniciar(Arnes(sesion: null));

      contenedor.read(authControllerProvider.notifier).sesionExpirada();

      expect(contenedor.read(authControllerProvider).aviso, isNull);
    });
  });
}
