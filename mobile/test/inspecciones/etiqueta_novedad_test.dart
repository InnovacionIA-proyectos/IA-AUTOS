import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/features/inspecciones/domain/novedad_pendiente.dart';
import 'package:traza_autos/features/inspecciones/presentation/revision_novedad_page.dart';

import '../support/dobles_catalogos.dart';

/// Cómo se lee una novedad del conductor en la lista. En el celular salía solo el tamaño: sin la clase, había que
/// abrirla para saber de qué daño hablaba.
NovedadPendiente novedad({String? clase, String? tipoNovedad}) => NovedadPendiente(
      id: 'n1',
      vin: 'VIN00000000000001',
      proceso: 'transporte',
      momento: 'llegada',
      clase: clase,
      tamano: 'mas_20cm',
      tipoNovedad: tipoNovedad,
    );

void main() {
  final catalogos = catalogosCon('Golpe');

  test('dice la clase que eligió el conductor y el tamaño', () {
    expect(etiquetaNovedad(novedad(clase: 'golpe'), catalogos), 'Golpe · Más de 20 cm');
  });

  // La clase dice qué quedó en la lata; el tipo de novedad, qué pasó en la vía. No son lo mismo y caben los dos.
  test('con tipo de novedad, lo agrega al final', () {
    expect(etiquetaNovedad(novedad(clase: 'golpe', tipoNovedad: 'piedra'), catalogos), 'Golpe · Más de 20 cm · Piedra');
  });

  test('sin clase lo dice: es lo que quien revisa viene a poner', () {
    expect(etiquetaNovedad(novedad(), catalogos), 'Sin clase · Más de 20 cm');
  });

  test('el detalle lleva hora, lugar y quién la registró', () {
    const conFoto = NovedadPendiente(
      id: 'n1',
      vin: 'VIN00000000000001',
      proceso: 'transporte',
      momento: 'llegada',
      tamano: 'mas_20cm',
      foto: FotoNovedad(lat: 6.18206, lng: -75.59129),
    );

    expect(detalleNovedad(conFoto, conductor: 'Luis Gómez'), contains('6.18206, -75.59129'));
    expect(detalleNovedad(conFoto, conductor: 'Luis Gómez'), endsWith('Luis Gómez'));
  });

  test('sin conductor conocido no deja un separador colgando', () {
    expect(detalleNovedad(novedad()), 'Sin hora · Sin ubicación');
  });
}
