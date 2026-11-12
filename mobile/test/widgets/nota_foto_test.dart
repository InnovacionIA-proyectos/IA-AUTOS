import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/core/tema/tema.dart';
import 'package:traza_autos/features/inspecciones/presentation/nota_foto.dart';

/// S7-HU-02.2 CA-4: la nota de una vista adicional se pide **antes** de tomar la foto.
///
/// Lo que esta prueba puede ver es el diálogo: que no deja seguir sin nota y que lo que ofrece es tomar la foto,
/// no guardarla. Que la cámara se abra después lo decide `_tomarFoto` en la pantalla de captura, que no se puede
/// ejercitar aquí porque abre la cámara del teléfono; eso se comprueba en el equipo.
void main() {
  /// Abre el diálogo y devuelve la caja donde caerá lo que conteste: vacía mientras siga abierto.
  Future<List<String?>> abrir(WidgetTester tester) async {
    final respuesta = <String?>[];
    await tester.pumpWidget(
      MaterialApp(
        theme: crearTema(),
        home: Builder(
          builder: (context) => TextButton(
            onPressed: () async => respuesta.add(await pedirNotaDeFoto(context, vista: 'Baúl')),
            child: const Text('abrir'),
          ),
        ),
      ),
    );
    await tester.tap(find.text('abrir'));
    await tester.pumpAndSettle();
    expect(find.byType(AlertDialog), findsOneWidget);
    return respuesta;
  }

  testWidgets('dice qué se va a fotografiar y no deja seguir sin nota', (tester) async {
    await abrir(tester);

    expect(find.text('Baúl'), findsOneWidget);
    expect(find.textContaining('qué daño vas a fotografiar'), findsOneWidget);
    expect(
      tester.widget<FilledButton>(find.widgetWithText(FilledButton, 'Tomar la foto')).onPressed,
      isNull,
      reason: 'sin nota el servidor rechazaría la evidencia: no se llega a la cámara',
    );
  });

  testWidgets('con la nota escrita se pasa a la cámara, y lo que viaja es la nota sin espacios', (tester) async {
    final respuesta = await abrir(tester);

    await tester.enterText(find.byType(TextField), '  Falta el gato  ');
    await tester.pump();
    await tester.tap(find.widgetWithText(FilledButton, 'Tomar la foto'));
    await tester.pumpAndSettle();

    expect(find.byType(AlertDialog), findsNothing);
    expect(respuesta.single, 'Falta el gato');
  });

  // Cancelar aquí es no tomar la foto. Antes se cancelaba con la foto ya tomada y se perdía sin decir nada.
  testWidgets('cancelar no devuelve nada: entonces la cámara ni se abre', (tester) async {
    final respuesta = await abrir(tester);

    await tester.tap(find.widgetWithText(TextButton, 'Cancelar'));
    await tester.pumpAndSettle();

    expect(respuesta.single, isNull, reason: 'quien la pidió se devuelve sin tomar nada');
  });
}
