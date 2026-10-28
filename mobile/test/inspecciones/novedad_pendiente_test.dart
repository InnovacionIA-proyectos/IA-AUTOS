import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/features/inspecciones/domain/novedad_pendiente.dart';

Map<String, dynamic> novedadJson({Map<String, dynamic>? evidencia}) => {
      'id': 'h1',
      'vin': 'VIN00000000000001',
      'viaje_id': 'via-1',
      'proceso': 'transporte',
      'momento': 'llegada',
      'tipo_novedad': 'golpe_en_ruta',
      'clase_dano': null,
      'tamano_dano': '1_5cm',
      'nivel_severidad': null,
      'origen': 'conductor',
      'nota': 'Se golpeó al pasar un hueco',
      'evidencia': evidencia,
    };

void main() {
  test('lee la novedad como la lista la API', () {
    final n = NovedadPendiente.fromJson(novedadJson(evidencia: {
      'url': '/api/uploads/dano.jpg',
      'sello_fecha': '2026-10-28T15:30:00Z',
      'sello_lat': 6.18,
      'sello_lng': -75.59,
      'sin_ubicacion': false,
    }));

    expect((n.id, n.vin, n.proceso, n.tamano), ('h1', 'VIN00000000000001', 'transporte', '1_5cm'));
    expect(n.tipoNovedad, 'golpe_en_ruta');
    expect(n.clase, isNull, reason: 'el conductor puede no haberla elegido; la pone quien revisa');
    expect(n.foto!.url, '/api/uploads/dano.jpg');
    expect(n.foto!.tomadaEn, DateTime.utc(2026, 10, 28, 15, 30));
    expect(n.foto!.tieneUbicacion, isTrue);
  });

  test('una foto sin GPS dice la hora pero no el lugar', () {
    final n = NovedadPendiente.fromJson(novedadJson(evidencia: {
      'url': '/api/uploads/dano.jpg',
      'sello_fecha': '2026-10-28T15:30:00Z',
      'sello_lat': null,
      'sello_lng': null,
      'sin_ubicacion': true,
    }));

    expect(n.foto!.tieneUbicacion, isFalse);
    expect(n.foto!.tomadaEn, isNotNull);
  });

  test('una novedad sin foto no rompe la lista', () {
    expect(NovedadPendiente.fromJson(novedadJson()).foto, isNull);
  });
}
