import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/features/auth/presentation/auth_controller.dart';
import 'package:traza_autos/features/sync/presentation/sync_controller.dart';
import 'package:traza_autos/features/vehiculos/domain/etiquetas.dart';
import 'package:traza_autos/features/viajes/presentation/mis_viajes_controller.dart';
import 'package:traza_autos/features/viajes/providers.dart';

import '../support/arnes.dart';
import '../support/dobles_viajes.dart';

Future<ProviderContainer> iniciar(Arnes arnes, ViajesRepositorioFalso repositorio) async {
  final contenedor = ProviderContainer(
    overrides: [...arnes.overrides, viajesRepositorioProvider.overrideWithValue(repositorio)],
    retry: (_, _) => null,
  );
  addTearDown(contenedor.dispose);
  // Se inician la sesión y la sincronización (como hace la app) y se espera a que restauren su estado: sin esto
  // `cargar()` saldría sin usuario, porque AuthController restaura por microtask.
  contenedor.listen(authControllerProvider, (_, _) {});
  contenedor.listen(syncControllerProvider, (_, _) {});
  contenedor.listen(misViajesControllerProvider, (_, _) {});
  await pumpEventQueue();
  await contenedor.read(misViajesControllerProvider.notifier).cargar();
  return contenedor;
}

void main() {
  group('carga', () {
    test('con señal descarga mis viajes', () async {
      final repositorio = ViajesRepositorioFalso()..remotos = [viaje('v1', vines: ['VIN1'])];

      final contenedor = await iniciar(Arnes(), repositorio);

      final estado = contenedor.read(misViajesControllerProvider);
      expect(estado.fase, FaseViajes.listo);
      expect(estado.viajes.single.vehiculos.single.vin, 'VIN1');
    });

    test('sin señal usa la última copia guardada', () async {
      final repositorio = ViajesRepositorioFalso()..guardados = [viaje('v1', vines: ['VIN1'])];

      final contenedor = await iniciar(Arnes()..red.estable = false, repositorio);

      expect(contenedor.read(misViajesControllerProvider).fase, FaseViajes.listo);
      expect(repositorio.llamadasRemotas, 0);
    });

    test('sin copia y sin señal lo dice, en vez de parecer que no hay viajes', () async {
      final contenedor = await iniciar(Arnes()..red.estable = false, ViajesRepositorioFalso());

      expect(contenedor.read(misViajesControllerProvider).fase, FaseViajes.sinConexion);
    });

    test('un fallo al refrescar no borra lo que ya se muestra', () async {
      final repositorio = ViajesRepositorioFalso()
        ..guardados = [viaje('v1', vines: ['VIN1'])]
        ..error = errorApi(500);

      final contenedor = await iniciar(Arnes(), repositorio);

      expect(contenedor.read(misViajesControllerProvider).fase, FaseViajes.listo);
    });

    test('un fallo sin copia guardada sí es error', () async {
      final repositorio = ViajesRepositorioFalso()..error = errorApi(500);

      final contenedor = await iniciar(Arnes(), repositorio);

      expect(contenedor.read(misViajesControllerProvider).fase, FaseViajes.error);
    });

    test('sin sesión no consulta nada', () async {
      final repositorio = ViajesRepositorioFalso()..remotos = [viaje('v1')];

      await iniciar(Arnes(sesion: null), repositorio);

      expect(repositorio.llamadasRemotas, 0);
    });
  });

  group('buscar dentro de mis viajes', () {
    final estado = EstadoViajes(FaseViajes.listo, [
      viaje('v1', codigo: 'MTK-100', vines: ['9BWZZZ377VT004251']),
      viaje('v2', codigo: 'MTK-200', vines: ['FIC05UCZ260100343', 'FIC05UCZ260100999']),
    ]);

    test('sin texto devuelve todas las unidades de mis viajes', () {
      expect(estado.buscar('').length, 3);
      expect(estado.buscar('   ').length, 3);
    });

    test('encuentra por fragmento del VIN, sin importar mayúsculas', () {
      expect([for (final (_, v) in estado.buscar('100343')) v.vin], ['FIC05UCZ260100343']);
      expect([for (final (_, v) in estado.buscar('fic05')) v.vin].length, 2);
    });

    test('encuentra por marca, modelo y por el código del viaje', () {
      expect(estado.buscar('kwid').length, 3);
      expect([for (final (viaje, _) in estado.buscar('MTK-100')) viaje.id], ['v1']);
    });

    test('un VIN que no es de mis viajes no aparece: la API lo rechazaría con 403', () {
      expect(estado.buscar('VINQUENOEXISTE00'), isEmpty);
    });
  });

  group('la insignia del estado del viaje', () {
    // El contrato admite cuatro estados. Con un ternario, «finalizado» y «cancelado» se mostraban como
    // «Programado»: no es que faltara traducción, es que decía algo falso. Hoy no se nota porque `mios` solo
    // trae los activos, pero la S6-HU-09.5 pide consultar los terminados.
    test('los cuatro estados del contrato tienen su texto', () {
      expect(etiquetar(etiquetaEstadoViaje, 'programado'), 'Programado');
      expect(etiquetar(etiquetaEstadoViaje, 'en_curso'), 'En curso');
      expect(etiquetar(etiquetaEstadoViaje, 'finalizado'), 'Finalizado');
      expect(etiquetar(etiquetaEstadoViaje, 'cancelado'), 'Cancelado');
    });

    test('un estado que no conocemos se muestra tal cual, no como otro', () {
      expect(etiquetar(etiquetaEstadoViaje, 'en_revision'), 'en_revision');
    });
  });
}
