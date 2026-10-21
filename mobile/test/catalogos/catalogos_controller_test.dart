import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/features/catalogos/data/catalogos_repositorio.dart';
import 'package:traza_autos/features/catalogos/domain/catalogos.dart';
import 'package:traza_autos/features/catalogos/presentation/catalogos_controller.dart';
import 'package:traza_autos/features/catalogos/providers.dart';

import '../support/arnes.dart';

Catalogos catalogosCon(String nombre, {String version = 'v1', String usuarioId = 'u1'}) => Catalogos.desdeRespuesta(
      {
        'version': version,
        'catalogos': {
          Catalogo.clasesDano: [
            {'id': 'i1', 'codigo': 'golpe', 'nombre': nombre, 'orden': 1, 'activo': true},
          ],
        },
      },
      usuarioId: usuarioId,
    );

class CatalogosRepositorioFalso implements CatalogosRepositorio {
  Catalogos? guardado;
  Catalogos? remoto;
  Object? error;
  int sincronizaciones = 0;

  @override
  Future<Catalogos?> leerCache(String usuarioId) async => guardado;

  @override
  Future<Catalogos> sincronizar(String usuarioId) async {
    sincronizaciones++;
    if (error != null) throw error!;
    guardado = remoto;
    return remoto!;
  }
}

Future<ProviderContainer> iniciar(Arnes arnes, CatalogosRepositorioFalso repositorio) async {
  final contenedor = ProviderContainer(
    overrides: [...arnes.overrides, catalogosRepositorioProvider.overrideWithValue(repositorio)],
    retry: (_, _) => null,
  );
  addTearDown(contenedor.dispose);
  contenedor.listen(catalogosControllerProvider, (_, _) {}); // lo mantiene vivo, como hace la app
  await pumpEventQueue();
  return contenedor;
}

void main() {
  test('sin sesión no consulta los catálogos (la API respondería 401)', () async {
    final repositorio = CatalogosRepositorioFalso()..remoto = catalogosCon('Golpe');

    await iniciar(Arnes(sesion: null), repositorio);

    expect(repositorio.sincronizaciones, 0);
  });

  test('con señal descarga y queda listo', () async {
    final repositorio = CatalogosRepositorioFalso()..remoto = catalogosCon('Golpe');

    final contenedor = await iniciar(Arnes(), repositorio);

    final estado = contenedor.read(catalogosControllerProvider);
    expect(estado.fase, FaseCatalogos.listo);
    expect(estado.catalogos!.etiqueta(Catalogo.clasesDano, 'golpe'), 'Golpe');
  });

  // El escenario del milestone: "Dado un inspector con la app sin señal, cuando abre una inspección, entonces usa
  // la última versión de los catálogos guardada en el celular, no valores fijos en el código".
  test('sin señal usa la última copia guardada en el dispositivo', () async {
    final repositorio = CatalogosRepositorioFalso()..guardado = catalogosCon('Golpe fuerte');
    final arnes = Arnes()..red.estable = false;

    final contenedor = await iniciar(arnes, repositorio);

    final estado = contenedor.read(catalogosControllerProvider);
    expect(estado.fase, FaseCatalogos.listo);
    expect(estado.catalogos!.etiqueta(Catalogo.clasesDano, 'golpe'), 'Golpe fuerte');
    expect(repositorio.sincronizaciones, 0, reason: 'sin señal no se intenta la descarga');
  });

  test('sin copia y sin señal avisa en vez de mostrar selectores vacíos', () async {
    final arnes = Arnes()..red.estable = false;

    final contenedor = await iniciar(arnes, CatalogosRepositorioFalso());

    expect(contenedor.read(catalogosControllerProvider).fase, FaseCatalogos.sinCatalogos);
  });

  test('un fallo al revalidar no borra lo que ya se está mostrando', () async {
    final repositorio = CatalogosRepositorioFalso()
      ..guardado = catalogosCon('Golpe')
      ..error = errorApi(500);

    final contenedor = await iniciar(Arnes(), repositorio);

    final estado = contenedor.read(catalogosControllerProvider);
    expect(estado.fase, FaseCatalogos.listo);
    expect(estado.catalogos!.etiqueta(Catalogo.clasesDano, 'golpe'), 'Golpe');
  });

  test('al volver una señal estable se revalida', () async {
    final repositorio = CatalogosRepositorioFalso()..remoto = catalogosCon('Golpe');
    final arnes = Arnes()..red.estable = false;
    await iniciar(arnes, repositorio);

    arnes.red.cambiarA(true);
    await pumpEventQueue();

    expect(repositorio.sincronizaciones, 1);
  });
}
