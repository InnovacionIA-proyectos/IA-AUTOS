import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/features/sync/domain/procesador_cola.dart';
import 'package:traza_autos/features/vehiculos/domain/inventario.dart';

/// S8-HU-06.2 CA-6: el descargue tiene un solo momento, la llegada. La app lo decide sola (no hay campo en el
/// inventario que lo diga) y reconoce por su código el 409 de una salida del descargue que quedó en la cola. Esta
/// prueba busca ese código y su motivo en el backend: si allá cambian, se ve aquí en rojo y no en el teléfono.
///
/// Igual que la prueba del contrato, necesita la raíz del monorepo montada: sin ella se salta.
File? _localizar(String ruta) {
  for (final candidato in ['../$ruta', ruta]) {
    final archivo = File(candidato);
    if (archivo.existsSync()) return archivo;
  }
  return null;
}

void main() {
  test('el descargue solo tiene la llegada; el cargue y el transporte, los dos momentos', () {
    expect(momentosDelProceso('descargue'), [Momento.llegada]);
    expect(momentosDelProceso('cargue'), Momento.todos);
    expect(momentosDelProceso('transporte'), Momento.todos);
  });

  final codigos = _localizar('backend/src/IA-AUTOS.Dominio/Comun/clsCodigosError.cs');
  final motivos = _localizar('backend/src/IA-AUTOS.Dominio/Comun/clsMotivosRechazo.cs');
  if (codigos == null || motivos == null) {
    test('el 409 de la salida del descargue', () {}, skip: 'No se encontró el backend (monta la raíz del monorepo).');
    return;
  }

  test('el código del 409 existe en el backend', () {
    expect(codigos.readAsStringSync(), contains('"$codigoMomentoNoAdmitido"'));
  });

  test('su motivo existe en el backend', () {
    expect(motivos.readAsStringSync(), contains('"$motivoSalidaDescargue"'));
  });
}
