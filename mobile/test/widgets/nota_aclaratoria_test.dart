import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/core/tema/tema.dart';
import 'package:traza_autos/features/inspecciones/domain/nota_aclaratoria.dart';
import 'package:traza_autos/features/inspecciones/presentation/nota_aclaratoria_hoja.dart';

/// Una nota en blanco no aclara nada, y el servidor la rechaza: el botón no se habilita sin texto.
Future<List<String?>> abrirHoja(WidgetTester tester) async {
  final respuestas = <String?>[];
  await tester.pumpWidget(
    MaterialApp(
      theme: crearTema(),
      home: Builder(
        builder: (context) => Scaffold(
          body: Center(
            child: ElevatedButton(
              onPressed: () async =>
                  respuestas.add(await pedirNotaAclaratoria(context, sobre: 'el informe de cargue')),
              child: const Text('abrir'),
            ),
          ),
        ),
      ),
    ),
  );
  await tester.tap(find.text('abrir'));
  await tester.pumpAndSettle();
  return respuestas;
}

void main() {
  testWidgets('sin texto no deja agregarla', (tester) async {
    await abrirHoja(tester);

    final boton = tester.widget<FilledButton>(find.widgetWithText(FilledButton, 'Agregar la nota'));
    expect(boton.onPressed, isNull);
  });

  testWidgets('con texto devuelve lo escrito, sin espacios de sobra', (tester) async {
    final respuestas = await abrirHoja(tester);

    await tester.enterText(find.byType(TextField), '  La placa estaba mal escrita  ');
    await tester.pumpAndSettle();
    await tester.tap(find.widgetWithText(FilledButton, 'Agregar la nota'));
    await tester.pumpAndSettle();

    expect(respuestas, ['La placa estaba mal escrita']);
  });

  testWidgets('dice sobre qué queda y que no se podrá borrar', (tester) async {
    await abrirHoja(tester);

    expect(find.textContaining('Sobre el informe de cargue'), findsOneWidget);
    expect(find.textContaining('nadie puede borrarla'), findsOneWidget);
  });

  testWidgets('la nota ya guardada se lee con quién y cuándo', (tester) async {
    await tester.pumpWidget(
      MaterialApp(
        theme: crearTema(),
        home: const Scaffold(
          body: FilaNotaAclaratoria(
            nota: NotaAclaratoria(id: 'n1', texto: 'La placa estaba mal escrita', autorNombre: 'Ana Pérez'),
            cuando: '3 ene 2026, 8:00 a. m.',
          ),
        ),
      ),
    );

    expect(find.text('La placa estaba mal escrita'), findsOneWidget);
    expect(find.text('Ana Pérez · 3 ene 2026, 8:00 a. m.'), findsOneWidget);
  });
}
