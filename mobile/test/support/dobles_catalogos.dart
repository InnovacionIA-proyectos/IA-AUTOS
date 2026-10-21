import 'package:traza_autos/features/catalogos/data/catalogos_repositorio.dart';
import 'package:traza_autos/features/catalogos/domain/catalogos.dart';

Catalogos catalogosCon(String nombre, {String version = 'v1', String usuarioId = 'u1'}) => Catalogos.desdeRespuesta(
      {
        'version': version,
        'catalogos': {
          Catalogo.clasesDano: [
            {'id': 'i1', 'codigo': 'golpe', 'nombre': nombre, 'orden': 1, 'activo': true},
          ],
          Catalogo.tamanosDano: [
            {'id': 'i2', 'codigo': 'mas_20cm', 'nombre': 'Más de 20 cm', 'orden': 1, 'activo': true},
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
