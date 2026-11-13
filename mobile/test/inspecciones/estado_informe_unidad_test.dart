import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/features/inspecciones/domain/estado_informe_unidad.dart';
import 'package:traza_autos/features/inspecciones/domain/inspeccion_resumen.dart';

/// Cómo va cada unidad de un cargue. Quien reclamó el camión tiene nueve carros delante y necesita saber, sin
/// entrar uno por uno, cuál cerró, cuál dejó a medias y cuál no ha tocado.
InspeccionResumen informe(String vin, {required String estado, String momento = 'salida'}) => InspeccionResumen(
      id: 'ins-$vin-$momento',
      vin: vin,
      momento: momento,
      momentoNombre: momento,
      fecha: '2026-11-09T12:00:00.000Z',
      estado: estado,
    );

void main() {
  const vin = 'VIN00000000000001';

  test('sin ningún informe de esa unidad, no hay informe', () {
    expect(estadoDeUnidad(vin, const []), EstadoInformeUnidad.sinInforme);
  });

  test('con un borrador, en borrador', () {
    expect(estadoDeUnidad(vin, [informe(vin, estado: 'borrador')]), EstadoInformeUnidad.borrador);
  });

  test('con uno cerrado, cerrada', () {
    expect(estadoDeUnidad(vin, [informe(vin, estado: 'cerrada')]), EstadoInformeUnidad.cerrada);
  });

  // Una etapa tiene salida y llegada: decir «en borrador» porque la segunda está empezada escondería que la
  // primera ya quedó cerrada, que es trabajo hecho.
  test('con la salida cerrada y la llegada en borrador, gana lo más avanzado', () {
    final informes = [
      informe(vin, estado: 'cerrada'),
      informe(vin, estado: 'borrador', momento: 'llegada'),
    ];

    expect(estadoDeUnidad(vin, informes), EstadoInformeUnidad.cerrada);
  });

  test('los informes de otra unidad no cuentan', () {
    final informes = [informe('VIN00000000000002', estado: 'cerrada')];

    expect(estadoDeUnidad(vin, informes), EstadoInformeUnidad.sinInforme);
  });

  test('cada estado se dice con palabras, no con un código', () {
    expect(EstadoInformeUnidad.sinInforme.etiqueta, 'Sin informe');
    expect(EstadoInformeUnidad.borrador.etiqueta, 'En borrador');
    expect(EstadoInformeUnidad.cerrada.etiqueta, 'Cerrada');
  });
}
