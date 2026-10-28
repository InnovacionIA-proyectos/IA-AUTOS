import 'package:drift/native.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/core/db/app_database.dart';
import 'package:traza_autos/core/db/cache_lecturas.dart';
import 'package:traza_autos/core/errores/error_api.dart';
import 'package:traza_autos/features/inspecciones/data/hallazgos_previos_repositorio.dart';
import 'package:traza_autos/features/vehiculos/data/vehiculos_repositorio.dart';
import 'package:traza_autos/features/vehiculos/domain/inventario.dart';

/// Inventario con un solo hallazgo cerrado, que es lo único que mira el repositorio.
Inventario inventarioCon(String hallazgoId) => Inventario.fromJson({
      'id': 'v1',
      'vin': 'VIN00000000000001',
      'marca': 'Renault',
      'modelo': 'Logan',
      'operacion': 'exportacion',
      'etapas': [
        {
          'id': 'e1',
          'orden': 1,
          'proceso': 'cargue',
          'requiere_inspeccion': true,
          'inspecciones': [
            {
              'id': 'i1',
              'momento': 'salida',
              'fecha': '2026-10-01T08:00:00Z',
              'estado': 'cerrada',
              'cerrada_at': '2026-10-01T09:00:00Z',
              'hallazgos': [
                {'id': hallazgoId, 'zona': 'Izquierdo 7 · Espejo retrovisor', 'cuadrante_numero': 7, 'lado': 'izquierdo', 'clase_dano': 'golpe', 'tamano_dano': '1_5cm'},
              ],
            },
          ],
        },
      ],
    });

class VehiculosFalso implements VehiculosRepositorio {
  VehiculosFalso(this.respuesta);

  /// Lo que devuelve `inventario`, o el error que lanza.
  Object respuesta;
  var llamadas = 0;

  @override
  Future<Inventario> inventario(String vin) async {
    llamadas++;
    if (respuesta is Inventario) return respuesta as Inventario;
    throw respuesta;
  }
}

void main() {
  late AppDatabase db;
  late CacheLecturasRepo cache;

  setUp(() {
    db = AppDatabase(NativeDatabase.memory());
    cache = CacheLecturasRepo(db);
  });
  tearDown(() => db.close());

  test('con señal los baja y los guarda', () async {
    final vehiculos = VehiculosFalso(inventarioCon('h1'));
    final repo = HallazgosPreviosRepositorio(vehiculos, cache);

    expect((await repo.de('VIN00000000000001')).single.id, 'h1');
    expect(await cache.leer('hallazgos-previos:VIN00000000000001'), isNotNull);
  });

  test('sin señal devuelve la última copia guardada', () async {
    final vehiculos = VehiculosFalso(inventarioCon('h1'));
    final repo = HallazgosPreviosRepositorio(vehiculos, cache);
    await repo.de('VIN00000000000001');

    vehiculos.respuesta = const ErrorApi.red();
    final previos = await repo.de('VIN00000000000001');

    expect(previos.single.id, 'h1', reason: 'la propuesta de preexistente tiene que funcionar en el patio');
    expect(previos.single.zona, 'Izquierdo 7 · Espejo retrovisor');
    expect(previos.single.clave, '7I');
  });

  test('sin señal y sin copia no propone nada, pero tampoco rompe la captura', () async {
    final repo = HallazgosPreviosRepositorio(VehiculosFalso(const ErrorApi.red()), cache);

    expect(await repo.de('VIN00000000000001'), isEmpty);
  });
}
