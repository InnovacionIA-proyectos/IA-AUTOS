import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/features/viajes/domain/viaje.dart';

Viaje con(String estado) => Viaje(id: 'v1', codigoMtrack: 'MTK-1', placa: 'ABC123', estado: estado);

/// Un viaje terminado conserva sus etapas y sus informes, pero ya no admite inspecciones nuevas: el servidor
/// responde 409. La app tiene que saberlo para no ofrecer capturar.
void main() {
  test('se trabaja mientras está programado o en curso', () {
    expect(con('programado').admiteTrabajo, isTrue);
    expect(con('en_curso').admiteTrabajo, isTrue);
  });

  test('terminado o cancelado, ya no', () {
    expect(con('finalizado').admiteTrabajo, isFalse);
    expect(con('cancelado').admiteTrabajo, isFalse);
  });

  test('un estado que la app no conozca no habilita a trabajar', () {
    expect(con('').admiteTrabajo, isFalse);
    expect(con('otro_estado_nuevo').admiteTrabajo, isFalse);
  });
}
