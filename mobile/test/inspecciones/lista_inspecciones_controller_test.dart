import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/features/auth/presentation/auth_controller.dart';
import 'package:traza_autos/features/inspecciones/domain/inspeccion_resumen.dart';
import 'package:traza_autos/features/sync/presentation/sync_controller.dart';
import 'package:traza_autos/features/inspecciones/presentation/lista_inspecciones_controller.dart';

import '../support/arnes.dart';

const _a = InspeccionResumen(id: '1', vin: 'VIN1', momento: 'salida', momentoNombre: 'Inspeccion de salida', fecha: '2026-01-01T10:00:00Z', estado: 'cerrada');
const _b = InspeccionResumen(id: '2', vin: 'VIN2', momento: 'salida', momentoNombre: 'Inspeccion de salida', fecha: '2026-01-02T10:00:00Z', estado: 'borrador');

Future<ProviderContainer> preparar(Arnes arnes) async {
  final contenedor = arnes.contenedor();
  addTearDown(contenedor.dispose);
  // Se inician la sesión y la sincronización (como hace la app) y se espera a que restauren su estado.
  contenedor.listen(authControllerProvider, (_, _) {});
  contenedor.listen(syncControllerProvider, (_, _) {});
  contenedor.listen(listaInspeccionesControllerProvider, (_, _) {});
  await pumpEventQueue();
  return contenedor;
}

void main() {
  test('con señal descarga el listado y lo guarda en caché', () async {
    final arnes = Arnes();
    arnes.inspecciones.remoto = [_a, _b];
    final contenedor = await preparar(arnes);

    await contenedor.read(listaInspeccionesControllerProvider.notifier).cargar();

    final estado = contenedor.read(listaInspeccionesControllerProvider);
    expect(estado.fase, FaseLista.listo);
    expect(estado.items.map((i) => i.id), ['1', '2']);
    expect(arnes.inspecciones.cache, hasLength(2));
  });

  test('con caché y sin señal muestra lo cacheado y no llama a la API', () async {
    final arnes = Arnes()..red.estable = false;
    arnes.inspecciones.cache = [_a];
    final contenedor = await preparar(arnes);

    await contenedor.read(listaInspeccionesControllerProvider.notifier).cargar();

    final estado = contenedor.read(listaInspeccionesControllerProvider);
    expect(estado.fase, FaseLista.listo);
    expect(estado.items.single.id, '1');
    expect(arnes.inspecciones.llamadasRemotas, 0);
  });

  test('sin caché y sin señal explica que hay que conectarse una vez', () async {
    final arnes = Arnes()..red.estable = false;
    final contenedor = await preparar(arnes);

    await contenedor.read(listaInspeccionesControllerProvider.notifier).cargar();

    expect(contenedor.read(listaInspeccionesControllerProvider).fase, FaseLista.sinConexion);
  });

  test('si la API falla y no hay caché muestra el error', () async {
    final arnes = Arnes();
    arnes.inspecciones.errorRemoto = errorApi(503);
    final contenedor = await preparar(arnes);

    await contenedor.read(listaInspeccionesControllerProvider.notifier).cargar();

    expect(contenedor.read(listaInspeccionesControllerProvider).fase, FaseLista.error);
  });

  test('si la API falla pero hay caché, la conserva en pantalla', () async {
    final arnes = Arnes();
    arnes.inspecciones.cache = [_a];
    arnes.inspecciones.errorRemoto = errorApi(0);
    final contenedor = await preparar(arnes);

    await contenedor.read(listaInspeccionesControllerProvider.notifier).cargar();

    final estado = contenedor.read(listaInspeccionesControllerProvider);
    expect(estado.fase, FaseLista.listo);
    expect(estado.items.single.id, '1');
  });

  test('sin sesión no hace nada', () async {
    final arnes = Arnes(sesion: null);
    final contenedor = await preparar(arnes);

    await contenedor.read(listaInspeccionesControllerProvider.notifier).cargar();

    expect(arnes.inspecciones.llamadasRemotas, 0);
  });
}
