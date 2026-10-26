import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intl/date_symbol_data_local.dart';
import 'package:traza_autos/core/errores/error_api.dart';
import 'package:traza_autos/core/tema/tema.dart';
import 'package:traza_autos/features/sync/presentation/cola_page.dart';

import '../support/arnes.dart';
import '../support/dobles_sync.dart';

/// Lo que se puede tocar en la cola, que es donde se borran fotos. Un envío ajeno o uno que el servidor rechazó
/// por permisos no son de quien está mirando: ofrecerle «Descartar» sería dejarle borrar evidencia de otra
/// persona y entorpecerle el trabajo, sin que ella se entere.

Future<void> abrir(WidgetTester tester, Arnes arnes) async {
  await tester.pumpWidget(
    ProviderScope(
      retry: (_, _) => null,
      overrides: arnes.overrides,
      child: MaterialApp(theme: crearTema(), home: const ColaPage()),
    ),
  );
  for (var i = 0; i < 8; i++) {
    await tester.pump(const Duration(milliseconds: 50));
  }
}

Finder get descartar => find.widgetWithText(OutlinedButton, 'Descartar');
Finder get reintentar => find.widgetWithText(FilledButton, 'Reintentar');

void main() {
  // La fila muestra la fecha con `DateFormat('es_CO')`, que `main.dart` prepara al arrancar la app.
  setUpAll(() => initializeDateFormatting('es_CO'));

  testWidgets('un envío propio con error se puede reintentar y descartar', (tester) async {
    final arnes = Arnes();
    await arnes.cola.agregar(informe(id: 'j', fallido: true));

    await abrir(tester, arnes);

    expect(find.text('Con error'), findsOneWidget);
    expect(reintentar, findsOneWidget);
    expect(descartar, findsOneWidget);
  });

  testWidgets('uno de otra persona se ve, pero no se puede tocar', (tester) async {
    final arnes = Arnes();
    await arnes.cola.agregar(informe(id: 'j', usuarioId: 'otra-persona', fallido: true));

    await abrir(tester, arnes);

    expect(find.text('De otro usuario'), findsOneWidget);
    expect(find.textContaining('cuando entre con su usuario'), findsOneWidget);
    expect(descartar, findsNothing);
    expect(reintentar, findsNothing);
  });

  // El caso del job adoptado: es mío en el dispositivo, pero la inspección la creó otra persona y el servidor no
  // me deja continuarla. Borrarlo sería tirar sus fotos.
  testWidgets('uno que el servidor rechazó por permisos tampoco', (tester) async {
    final arnes = Arnes();
    await arnes.cola.agregar(
      informe(id: 'j', fallido: true).conIntento(
        intentos: 1,
        error: 'No tiene permisos sobre esta inspección',
        codigo: codigoProhibido,
        fallido: true,
      ),
    );

    await abrir(tester, arnes);

    expect(find.textContaining('solo ella puede terminarlo'), findsOneWidget);
    expect(descartar, findsNothing);
    expect(reintentar, findsNothing);
  });

  testWidgets('un rechazo que sí es suyo conserva la salida de la cola', (tester) async {
    final arnes = Arnes();
    await arnes.cola.agregar(
      informe(id: 'j', fallido: true).conIntento(
        intentos: 1,
        error: 'Momento inválido: use salida o llegada',
        codigo: 'VALIDATION_ERROR',
        fallido: true,
      ),
    );

    await abrir(tester, arnes);

    expect(descartar, findsOneWidget, reason: 'si no, queda atrapado con un envío que nunca va a salir');
    expect(find.textContaining('Momento inválido'), findsOneWidget);
  });

  testWidgets('sin nada en la cola lo dice', (tester) async {
    await abrir(tester, Arnes());

    expect(find.text('Todo enviado'), findsOneWidget);
  });
}
