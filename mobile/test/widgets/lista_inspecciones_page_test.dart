import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intl/date_symbol_data_local.dart';
import 'package:traza_autos/core/tema/tema.dart';
import 'package:traza_autos/features/inspecciones/presentation/lista_inspecciones_page.dart';
import 'package:traza_autos/features/sync/presentation/sync_controller.dart';

import '../support/arnes.dart';

/// La pestaña de Inspecciones vive dentro del shell: una vez construida no se vuelve a construir al volver a
/// ella, así que lo que muestra se queda como estaba cuando se abrió por primera vez.
Future<Arnes> abrir(WidgetTester tester) async {
  final arnes = Arnes();
  await tester.pumpWidget(
    ProviderScope(
      retry: (_, _) => null,
      overrides: arnes.overrides,
      child: MaterialApp(theme: crearTema(), home: const ListaInspeccionesPage()),
    ),
  );
  for (var i = 0; i < 6; i++) {
    await tester.pump(const Duration(milliseconds: 50));
  }
  return arnes;
}

void main() {
  setUpAll(() => initializeDateFormatting('es_CO'));

  // El defecto visto en el celular: se cierra un informe, se entra a Inspecciones y sigue apareciendo como
  // estaba antes; había que arrastrar la pantalla hacia abajo para verlo cerrado.
  testWidgets('al terminar de trabajar la cola, la lista se vuelve a cargar sola', (tester) async {
    final arnes = await abrir(tester);
    final antes = arnes.inspecciones.llamadasRemotas;
    expect(antes, greaterThan(0), reason: 'la primera carga es la de entrada a la pestaña');

    final contenedor = ProviderScope.containerOf(tester.element(find.byType(ListaInspeccionesPage)));
    await contenedor.read(syncControllerProvider.notifier).sincronizar();
    for (var i = 0; i < 6; i++) {
      await tester.pump(const Duration(milliseconds: 50));
    }

    expect(arnes.inspecciones.llamadasRemotas, greaterThan(antes));
  });
}
