import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/core/tema/tema.dart';
import 'package:traza_autos/core/ui/visor_imagen.dart';

/// Abrir la foto es lo que permite comprobar en el patio que quedó bien, con el vehículo todavía delante. Lo que
/// se prueba aquí es que abra, que deje acercar y, sobre todo, que al cerrar la pantalla de atrás siga igual.

/// Una imagen que no toca disco ni red: lo que importa es el comportamiento del visor, no qué foto muestra.
final imagenDePrueba = MemoryImage(Uint8List.fromList(<int>[
  137, 80, 78, 71, 13, 10, 26, 10, // firma PNG; basta para construir el proveedor
]));

Future<void> montar(WidgetTester tester) => tester.pumpWidget(
      MaterialApp(
        theme: crearTema(),
        home: Scaffold(
          body: Builder(
            builder: (context) => Column(
              children: [
                const Text('pantalla de atrás'),
                TextButton(
                  onPressed: () => mostrarVisorImagen(context, imagen: imagenDePrueba, titulo: 'Lateral derecha'),
                  child: const Text('abrir'),
                ),
              ],
            ),
          ),
        ),
      ),
    );

Future<void> abrir(WidgetTester tester) async {
  await montar(tester);
  await tester.tap(find.text('abrir'));
  await tester.pumpAndSettle();
}

void main() {
  testWidgets('muestra la foto con su nombre y se puede acercar', (tester) async {
    await abrir(tester);

    expect(find.text('Lateral derecha'), findsOneWidget);
    expect(find.byType(InteractiveViewer), findsOneWidget);
    expect(tester.widget<InteractiveViewer>(find.byType(InteractiveViewer)).maxScale, greaterThan(1));
  });

  testWidgets('el botón de cerrar devuelve la pantalla de atrás tal como estaba', (tester) async {
    await abrir(tester);
    expect(find.byType(InteractiveViewer), findsOneWidget);

    await tester.tap(find.byTooltip('Cerrar'));
    await tester.pumpAndSettle();

    expect(find.byType(InteractiveViewer), findsNothing);
    expect(find.text('pantalla de atrás'), findsOneWidget);
    expect(find.text('abrir'), findsOneWidget, reason: 'se puede volver a abrir');
  });

  // Es lo primero que la gente intenta para cerrar algo a pantalla completa. El pellizco sigue acercando,
  // porque es otro gesto.
  testWidgets('tocar la imagen también cierra', (tester) async {
    await abrir(tester);

    await tester.tapAt(const Offset(20, 400));
    await tester.pumpAndSettle();

    expect(find.byType(InteractiveViewer), findsNothing);
    expect(find.text('pantalla de atrás'), findsOneWidget);
  });

  testWidgets('el botón atrás del sistema cierra solo el visor', (tester) async {
    await abrir(tester);

    await tester.binding.handlePopRoute();
    await tester.pumpAndSettle();

    expect(find.byType(InteractiveViewer), findsNothing);
    expect(find.text('pantalla de atrás'), findsOneWidget);
  });
}
