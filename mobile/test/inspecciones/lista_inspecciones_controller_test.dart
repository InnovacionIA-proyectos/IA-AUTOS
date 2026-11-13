import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/features/auth/presentation/auth_controller.dart';
import 'package:traza_autos/features/inspecciones/domain/inspeccion_resumen.dart';
import 'package:traza_autos/features/sync/presentation/sync_controller.dart';
import 'package:traza_autos/features/inspecciones/presentation/lista_inspecciones_controller.dart';

import '../support/arnes.dart';
import '../support/dobles_sync.dart';

/// Un contador de envíos que la prueba mueve a mano, para poder bajarlo y subirlo.
class _Envios extends Notifier<int> {
  @override
  int build() => 3;

  void poner(int valor) => state = valor;
}

final _enviosProvider = NotifierProvider<_Envios, int>(_Envios.new);

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

  // Lo que se veía en el patio: se cerraba un informe, la cola lo entregaba, y la lista seguía mostrando lo de
  // antes hasta que alguien bajaba a refrescar. Con una corrección era peor: quedaba viéndose la versión vieja.
  test('cuando la cola entrega algo, la lista se recarga sola', () async {
    final arnes = Arnes();
    arnes.inspecciones.remoto = [_a];
    final contenedor = await preparar(arnes);
    await contenedor.read(listaInspeccionesControllerProvider.notifier).cargar();
    expect(contenedor.read(listaInspeccionesControllerProvider).items.map((i) => i.id), ['1']);

    // El servidor ya tiene el informe nuevo y la cola lo acaba de entregar.
    arnes.inspecciones.remoto = [_a, _b];
    await arnes.cola.agregar(informe());
    await contenedor.read(syncControllerProvider.notifier).sincronizar();
    await pumpEventQueue();

    expect(
      contenedor.read(listaInspeccionesControllerProvider).items.map((i) => i.id),
      ['1', '2'],
      reason: 'sin esto habría que bajar a refrescar a mano',
    );
  });

  test('una pasada que no entrega nada no la hace recargar', () async {
    final arnes = Arnes();
    arnes.inspecciones.remoto = [_a];
    final contenedor = await preparar(arnes);
    await contenedor.read(listaInspeccionesControllerProvider.notifier).cargar();
    final antes = arnes.inspecciones.llamadasRemotas;

    await contenedor.read(syncControllerProvider.notifier).sincronizar();
    await pumpEventQueue();

    expect(arnes.inspecciones.llamadasRemotas, antes, reason: 'nada cambió en el servidor: nada que recargar');
  });

  // El contador vuelve a cero cuando la cola se rearma. Reaccionar a esa bajada dejaba la ficha del VIN
  // parpadeando entre «buscando daños» y el resultado, porque cada reinicio la mandaba a buscar de nuevo.
  test('que el contador se reinicie no la hace recargar: solo cuenta que suba', () async {
    final arnes = Arnes();
    arnes.inspecciones.remoto = [_a];
    final contenedor = ProviderContainer(
      overrides: [...arnes.overrides, entregadosProvider.overrideWith((ref) => ref.watch(_enviosProvider))],
      retry: (_, _) => null,
    );
    addTearDown(contenedor.dispose);
    contenedor.listen(authControllerProvider, (_, _) {});
    contenedor.listen(listaInspeccionesControllerProvider, (_, _) {});
    await pumpEventQueue();
    await contenedor.read(listaInspeccionesControllerProvider.notifier).cargar();
    final antes = arnes.inspecciones.llamadasRemotas;

    contenedor.read(_enviosProvider.notifier).poner(0);
    await pumpEventQueue();

    expect(arnes.inspecciones.llamadasRemotas, antes, reason: 'una bajada no es trabajo nuevo en el servidor');

    contenedor.read(_enviosProvider.notifier).poner(1);
    await pumpEventQueue();

    expect(arnes.inspecciones.llamadasRemotas, greaterThan(antes), reason: 'una subida sí');
  });

  /// S7-HU-21: la pestaña deja de pedir «solo las mías» a quien no captura ninguna.
  group('qué lista pide cada quien', () {
    test('el coordinador de calidad no manda el filtro de autor', () async {
      final arnes = Arnes(sesion: sesionCoordinador);
      arnes.inspecciones.remoto = [_a, _b];
      final contenedor = await preparar(arnes);

      await contenedor.read(listaInspeccionesControllerProvider.notifier).cargar();

      expect(
        arnes.inspecciones.peticiones.single.usuarioId,
        isNull,
        reason: 'con el filtro puesto el servidor le responde sus propias inspecciones, que son ninguna',
      );
      expect(contenedor.read(listaInspeccionesControllerProvider).items, hasLength(2));
    });

    test('el inspector lo sigue mandando: ve solo las suyas, igual que antes', () async {
      final arnes = Arnes();
      arnes.inspecciones.remoto = [_a];
      final contenedor = await preparar(arnes);

      await contenedor.read(listaInspeccionesControllerProvider.notifier).cargar();

      expect(arnes.inspecciones.peticiones.single.usuarioId, 'u1');
    });

    // El conductor no ve esta pestaña, pero el filtro sale del rol y no de la pantalla: si alguna vez la abre
    // —o si otra pantalla usa el mismo listado— tiene que seguir viendo solo lo suyo.
    test('el conductor también: lo suyo es lo de sus viajes', () async {
      final arnes = Arnes(sesion: sesionConductor);
      arnes.inspecciones.remoto = [_a];
      final contenedor = await preparar(arnes);

      await contenedor.read(listaInspeccionesControllerProvider.notifier).cargar();

      expect(arnes.inspecciones.peticiones.single.usuarioId, 'u9');
    });
  });

  /// La lista de un cliente no cabe en una pantalla ni en una petición: llega por páginas y se pide más.
  group('una lista larga', () {
    Arnes conCientoVeinte() => Arnes(sesion: sesionCoordinador)
      ..inspecciones.paginas = {
        1: [_a],
        2: [_b],
      }
      ..inspecciones.totalRemoto = 120;

    test('empieza por la primera página y dice que falta más', () async {
      final arnes = conCientoVeinte();
      final contenedor = await preparar(arnes);

      await contenedor.read(listaInspeccionesControllerProvider.notifier).cargar();

      final estado = contenedor.read(listaInspeccionesControllerProvider);
      expect((estado.items.length, estado.total, estado.hayMas), (1, 120, true));
      expect(arnes.inspecciones.peticiones.single.pagina, 1);
    });

    test('cargar más trae la siguiente y la agrega al final', () async {
      final arnes = conCientoVeinte();
      final contenedor = await preparar(arnes);
      await contenedor.read(listaInspeccionesControllerProvider.notifier).cargar();

      await contenedor.read(listaInspeccionesControllerProvider.notifier).cargarMas();

      final estado = contenedor.read(listaInspeccionesControllerProvider);
      expect(estado.items.map((i) => i.id), ['1', '2'], reason: 'se agrega, no se reemplaza');
      expect(arnes.inspecciones.peticiones.last.pagina, 2);
    });

    test('con todas en pantalla no queda nada que cargar', () async {
      final arnes = Arnes(sesion: sesionCoordinador)..inspecciones.remoto = [_a, _b];
      final contenedor = await preparar(arnes);
      await contenedor.read(listaInspeccionesControllerProvider.notifier).cargar();
      final antes = arnes.inspecciones.llamadasRemotas;

      expect(contenedor.read(listaInspeccionesControllerProvider).hayMas, isFalse);
      await contenedor.read(listaInspeccionesControllerProvider.notifier).cargarMas();

      expect(arnes.inspecciones.llamadasRemotas, antes, reason: 'no hay página siguiente que pedir');
    });

    // Lo que ya se leyó no se pierde porque la página siguiente no haya llegado.
    test('si falla la página siguiente, la lista se queda y se dice por qué', () async {
      final arnes = conCientoVeinte();
      final contenedor = await preparar(arnes);
      await contenedor.read(listaInspeccionesControllerProvider.notifier).cargar();
      arnes.inspecciones.errorRemoto = errorApi(0);

      await contenedor.read(listaInspeccionesControllerProvider.notifier).cargarMas();

      final estado = contenedor.read(listaInspeccionesControllerProvider);
      expect((estado.fase, estado.items.length), (FaseLista.listo, 1));
      expect(estado.mensaje, isNotNull);
    });
  });

  group('buscar por VIN', () {
    test('se lo pregunta al servidor', () async {
      final arnes = Arnes(sesion: sesionCoordinador)..inspecciones.remoto = [_a];
      final contenedor = await preparar(arnes);

      await contenedor.read(listaInspeccionesControllerProvider.notifier).buscar('VIN1');

      expect(arnes.inspecciones.peticiones.last.vin, 'VIN1');
      expect(contenedor.read(listaInspeccionesControllerProvider).busqueda, 'VIN1');
    });

    // La copia local guarda la primera página de la lista sin filtro: enseñarla como resultado diría que esos
    // informes son del VIN que se buscó.
    test('no responde con lo que hay guardado', () async {
      final arnes = Arnes(sesion: sesionCoordinador)
        ..inspecciones.cache = [_a]
        ..inspecciones.remoto = [_b];
      final contenedor = await preparar(arnes);

      await contenedor.read(listaInspeccionesControllerProvider.notifier).buscar('VIN2');

      expect(contenedor.read(listaInspeccionesControllerProvider).items.single.id, '2');
    });

    test('sin señal dice que buscar necesita conexión, en vez de responder con la copia vieja', () async {
      final arnes = Arnes(sesion: sesionCoordinador)..red.estable = false;
      arnes.inspecciones.cache = [_a];
      final contenedor = await preparar(arnes);

      await contenedor.read(listaInspeccionesControllerProvider.notifier).buscar('VIN2');

      final estado = contenedor.read(listaInspeccionesControllerProvider);
      expect(estado.fase, FaseLista.sinConexion);
      expect(estado.mensaje, contains('Conéctate'));
    });

    test('vacío vuelve a la lista entera', () async {
      final arnes = Arnes(sesion: sesionCoordinador)..inspecciones.remoto = [_a, _b];
      final contenedor = await preparar(arnes);
      await contenedor.read(listaInspeccionesControllerProvider.notifier).buscar('VIN1');

      await contenedor.read(listaInspeccionesControllerProvider.notifier).buscar('');

      expect(arnes.inspecciones.peticiones.last.vin, isNull);
      expect(contenedor.read(listaInspeccionesControllerProvider).items, hasLength(2));
    });
  });
}
