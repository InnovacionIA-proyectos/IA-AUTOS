import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/core/tema/tema.dart';
import 'package:traza_autos/features/vehiculos/domain/inventario.dart';
import 'package:traza_autos/features/vehiculos/presentation/hoja_no_sale.dart';

import '../support/dobles_catalogos.dart';

const hallazgoV1Plus = HallazgoVista(
  id: 'h1',
  zona: 'Izquierdo 7 · Espejo retrovisor',
  cuadranteNumero: 7,
  lado: 'izquierdo',
  clase: 'golpe',
  tamano: 'mas_20cm',
  nivel: 'v1_plus',
);

/// Abre la hoja y recoge lo que devuelva.
Future<List<DatosNoSale?>> abrirHoja(WidgetTester tester, {List<HallazgoVista> hallazgos = const [hallazgoV1Plus]}) async {
  final respuestas = <DatosNoSale?>[];
  await tester.pumpWidget(
    MaterialApp(
      theme: crearTema(),
      home: Builder(
        builder: (context) => Scaffold(
          body: Center(
            child: ElevatedButton(
              onPressed: () async => respuestas.add(
                await pedirNoSale(context, hallazgos: hallazgos, catalogos: catalogosCon('Golpe')),
              ),
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
  testWidgets('con un solo V1+ dice cuál es, sin hacer elegir', (tester) async {
    await abrirHoja(tester);

    expect(find.text('Por: Izquierdo 7 · Espejo retrovisor: Golpe'), findsOneWidget);
    expect(find.byType(RadioListTile<String>), findsNothing);
  });

  testWidgets('sin motivo no deja marcar', (tester) async {
    await abrirHoja(tester);

    final boton = tester.widget<FilledButton>(find.widgetWithText(FilledButton, 'Marcar que no sale'));
    expect(boton.onPressed, isNull);
  });

  // El VIN se escribe a mano, 17 caracteres, en un teclado de celular que sugiere y mete espacios. Un espacio de
  // más y el servidor responde «No existe en el inventario», que parece un error del dato y es del tecleo.
  testWidgets('el VIN de reemplazo viaja en mayúsculas y sin espacios', (tester) async {
    final respuestas = await abrirHoja(tester);

    await tester.enterText(find.widgetWithText(TextField, 'Motivo'), 'Rechazado');
    await tester.enterText(find.widgetWithText(TextField, 'VIN de reemplazo (opcional)'), ' 93yrbc00xr 7654321 ');
    await tester.pumpAndSettle();
    await tester.tap(find.widgetWithText(FilledButton, 'Marcar que no sale'));
    await tester.pumpAndSettle();

    expect(respuestas.single?.vinReemplazo, '93YRBC00XR7654321');
  });

  testWidgets('sin VIN de reemplazo no se manda ninguno', (tester) async {
    final respuestas = await abrirHoja(tester);

    await tester.enterText(find.widgetWithText(TextField, 'Motivo'), 'Rechazado');
    await tester.pumpAndSettle();
    await tester.tap(find.widgetWithText(FilledButton, 'Marcar que no sale'));
    await tester.pumpAndSettle();

    expect(respuestas.single?.vinReemplazo, isNull);
    expect(respuestas.single?.motivo, 'Rechazado');
  });
}
