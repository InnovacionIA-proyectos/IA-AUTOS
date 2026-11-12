import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/core/tema/tema.dart';
import 'package:traza_autos/features/catalogos/domain/catalogos.dart';
import 'package:traza_autos/features/inspecciones/presentation/selector_dano.dart';

final clases = [ValorCatalogo({'id': 'c1', 'codigo': 'golpe', 'nombre': 'Golpe', 'orden': 1, 'activo': true})];
final tamanos = [ValorCatalogo({'id': 't1', 'codigo': '1_5cm', 'nombre': 'De 1 a 5 cm', 'orden': 1, 'activo': true})];

/// Abre la hoja con un botón y devuelve lo que la hoja entregue al cerrarse.
Future<DatosDano? Function()> abrir(WidgetTester tester, {DatosDano? inicial}) async {
  DatosDano? resultado;
  await tester.pumpWidget(MaterialApp(
    theme: crearTema(),
    home: Scaffold(
      body: Builder(
        builder: (context) => TextButton(
          onPressed: () async =>
              resultado = await pedirDano(context, titulo: 'Capot', clases: clases, tamanos: tamanos, inicial: inicial),
          child: const Text('abrir'),
        ),
      ),
    ),
  ));
  await tester.tap(find.text('abrir'));
  await tester.pumpAndSettle();
  return () => resultado;
}

void main() {
  testWidgets('la observación vuelve sin espacios sobrantes', (tester) async {
    final resultado = await abrir(tester);

    await tester.tap(find.text('Golpe'));
    await tester.tap(find.text('De 1 a 5 cm'));
    await tester.enterText(find.byType(TextField), '  rayón en la compuerta  ');
    await tester.tap(find.text('Agregar'));
    await tester.pumpAndSettle();

    expect(resultado()?.nota, 'rayón en la compuerta');
  });

  // Es opcional: dejarla vacía no puede mandar una nota en blanco.
  testWidgets('sin observación la nota vuelve nula', (tester) async {
    final resultado = await abrir(tester);

    await tester.tap(find.text('Golpe'));
    await tester.tap(find.text('De 1 a 5 cm'));
    await tester.enterText(find.byType(TextField), '   ');
    await tester.tap(find.text('Agregar'));
    await tester.pumpAndSettle();

    expect(resultado(), isNotNull);
    expect(resultado()?.nota, isNull);
  });

  testWidgets('al corregir, la hoja abre con la observación que ya tenía', (tester) async {
    await abrir(tester, inicial: (clase: 'golpe', tamano: '1_5cm', nivel: null, nota: 'golpe en la bisagra'));

    expect(find.text('golpe en la bisagra'), findsOneWidget);
  });
}
