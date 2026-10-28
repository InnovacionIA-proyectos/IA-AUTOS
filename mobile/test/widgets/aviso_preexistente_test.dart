import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/core/tema/tema.dart';
import 'package:traza_autos/features/inspecciones/domain/hallazgo_previo.dart';
import 'package:traza_autos/features/inspecciones/presentation/aviso_preexistente.dart';
import 'package:traza_autos/features/vehiculos/domain/inventario.dart';

import '../support/dobles_catalogos.dart';

final previo = HallazgoPrevio(
  id: 'h-previo',
  zona: 'Izquierdo 7 · Espejo retrovisor',
  proceso: 'cargue',
  momento: Momento.salida,
  cuadranteNumero: 7,
  lado: 'izquierdo',
  clase: 'golpe',
  tamano: 'mas_20cm',
  cerradaEn: DateTime.utc(2026, 10, 1),
);

/// Abre la hoja. El holder recoge la respuesta cuando se responde.
Future<List<bool?>> abrirHoja(WidgetTester tester, {String? urlFoto}) async {
  final respuestas = <bool?>[];
  await tester.pumpWidget(
    MaterialApp(
      theme: crearTema(),
      home: Builder(
        builder: (context) => Scaffold(
          body: Center(
            child: ElevatedButton(
              onPressed: () async {
                respuestas.add(await preguntarPreexistente(
                  context,
                  previo: previo,
                  catalogos: catalogosCon('Golpe', etapas: etapasDePrueba),
                  urlFoto: urlFoto,
                ));
              },
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
  testWidgets('dice dónde estaba registrado, con la zona y la clase', (tester) async {
    await abrirHoja(tester);

    expect(find.text('Ya estaba registrado en Cargue (Izquierdo 7 · Espejo retrovisor, Golpe)'), findsOneWidget);
  });

  testWidgets('«Es el mismo daño» confirma que es preexistente', (tester) async {
    final respuestas = await abrirHoja(tester);
    await tester.tap(find.widgetWithText(FilledButton, 'Es el mismo daño'));
    await tester.pumpAndSettle();

    expect(respuestas, [true]);
  });

  testWidgets('«Es nuevo» responde que no lo es', (tester) async {
    final respuestas = await abrirHoja(tester);
    await tester.tap(find.widgetWithText(OutlinedButton, 'Es nuevo'));
    await tester.pumpAndSettle();

    expect(respuestas, [false]);
  });

  testWidgets('sin foto previa no se deja un hueco en la hoja', (tester) async {
    await abrirHoja(tester);

    expect(find.byType(Image), findsNothing);
  });

  // En un teléfono real la hoja traía título de dos líneas, el texto, la foto y los dos botones, y se pasaba del
  // alto que `showModalBottomSheet` le da por defecto: los botones quedaban cortados y no se podían tocar.
  testWidgets('en una pantalla de teléfono los dos botones se pueden tocar', (tester) async {
    await tester.binding.setSurfaceSize(const Size(360, 640));
    addTearDown(() => tester.binding.setSurfaceSize(null));

    final respuestas = await abrirHoja(tester, urlFoto: 'https://api.test/api/uploads/previo.jpg');
    await tester.tap(find.widgetWithText(FilledButton, 'Es el mismo daño'));
    await tester.pumpAndSettle();

    expect(respuestas, [true]);
  });

  testWidgets('en una pantalla de teléfono «Es nuevo» también alcanza', (tester) async {
    await tester.binding.setSurfaceSize(const Size(360, 640));
    addTearDown(() => tester.binding.setSurfaceSize(null));

    final respuestas = await abrirHoja(tester, urlFoto: 'https://api.test/api/uploads/previo.jpg');
    await tester.tap(find.widgetWithText(OutlinedButton, 'Es nuevo'));
    await tester.pumpAndSettle();

    expect(respuestas, [false]);
  });
}
