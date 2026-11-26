import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/features/viajes/presentation/entrega_punto_page.dart';

/// La entrega por punto reconoce tres rechazos del servidor por su **texto**: el 400 no trae un código propio.
/// Esta prueba busca esos textos en el código del backend, para que un cambio de redacción allá se vea aquí en
/// rojo en vez de que la app deje de reconocerlos sin que nadie se entere —y el envío se quede en la cola con
/// un mensaje que el conductor no sabe interpretar—.
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
  final entregas = _localizar('backend/src/IA-AUTOS.Aplicacion/Viajes/clsEntregasService.cs');
  final custodia = _localizar('backend/src/IA-AUTOS.Dominio/Custodia/clsTransferenciaCustodia.cs');
  if (entregas == null || custodia == null) {
    test('rechazos de la entrega por punto', () {}, skip: 'No se encontró el backend (monta la raíz del monorepo).');
    return;
  }

  // En minúsculas, como los compara la app.
  final deEntregas = entregas.readAsStringSync().toLowerCase();
  final deCustodia = custodia.readAsStringSync().toLowerCase();

  test('«tiene inspector asignado»: mandar los datos de un externo donde el punto tiene inspector', () {
    expect(deEntregas, contains(rechazosDelPunto.conInspector));
  });

  test('«indique quién recibe»: el punto tiene inspector, pero no hay quien reciba', () {
    expect(deEntregas, contains(rechazosDelPunto.sinQuienReciba));
  });

  // Salen de la validación del externo, que vive en la transferencia: el nombre y la cédula, cada uno con su
  // género. El trozo que busca la app tiene que cubrir los dos.
  test('«de quien recibe es obligatorio/a»: el punto no tiene inspector y faltan los datos del externo', () {
    expect(deCustodia, contains('el nombre ${rechazosDelPunto.faltanDatos}'));
    expect(deCustodia, contains('la cédula ${rechazosDelPunto.faltanDatos}'));
  });
}
