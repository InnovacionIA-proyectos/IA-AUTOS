import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/features/viajes/domain/entrega_punto.dart';

/// La entrega por parada reconoce los rechazos del servidor por su **motivo** (`details.motivo`), no por el texto
/// (S8-HU-04.2 CA-3). Esta prueba busca esos motivos, y los códigos del cierre con una sola firma, en el código del
/// backend: si allá cambian, se ve aquí en rojo en vez de que la app deje de reconocerlos sin que nadie se entere.
///
/// Igual que la prueba del contrato, necesita la raíz del monorepo montada: sin ella se salta.
File? _localizar(String ruta) {
  for (final candidato in ['../$ruta', ruta]) {
    final archivo = File(candidato);
    if (archivo.existsSync()) return archivo;
  }
  return null;
}

/// Los motivos llegan con el PR #456 (S8-HU-04.1); hasta que entre a QA, el backend montado es el anterior.

void main() {
  final motivos = _localizar('backend/src/IA-AUTOS.Dominio/Comun/clsMotivosRechazo.cs');
  final custodia = _localizar('backend/src/IA-AUTOS.Dominio/Custodia/clsTransferenciaCustodia.cs');
  if (motivos == null || custodia == null) {
    test('rechazos de la entrega por parada', () {}, skip: 'No se encontró el backend (monta la raíz del monorepo).');
    return;
  }

  final deMotivos = motivos.readAsStringSync();
  final deCustodia = custodia.readAsStringSync();

  group('los motivos de la parada que la pantalla explica', () {
    for (final motivo in MotivoParada.todos) {
      test(motivo, () => expect(deMotivos, contains('"$motivo"')));
    }
  });

  group('los motivos del cierre con una sola firma', () {
    for (final motivo in [MotivoUnaFirma.seNegoAFirmar, MotivoUnaFirma.noHabiaNadie]) {
      test(motivo, () => expect(deCustodia, contains('"$motivo"')));
    }
  });

  // Ya está en QA: el retiro de la firma carro por carro del cargue (S8-HU-01.3) sigue reconociéndose por su motivo.
  test('firma_por_ninera', () => expect(deMotivos, contains('"firma_por_ninera"')));
}
