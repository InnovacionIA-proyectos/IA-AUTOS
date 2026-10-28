import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/core/tema/tema.dart';
import 'package:traza_autos/features/vehiculos/domain/inventario.dart';
import 'package:traza_autos/features/vehiculos/presentation/detalle_hallazgo.dart';

import '../support/dobles_catalogos.dart';

/// Lo que ve quien abre un daño desde el dibujo, en la pantalla del VIN. Es solo lectura: un informe cerrado no
/// se corrige desde aquí.
Future<void> abrir(WidgetTester tester, HallazgoVista hallazgo, {String? urlFoto}) async {
  await tester.pumpWidget(
    MaterialApp(
      theme: crearTema(),
      home: Builder(
        builder: (context) => Scaffold(
          body: Center(
            child: ElevatedButton(
              onPressed: () => mostrarDetalleHallazgo(
                context,
                hallazgo: hallazgo,
                catalogos: catalogosCon('Golpe'),
                urlFoto: urlFoto,
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
}

void main() {
  testWidgets('dice dónde, qué, de qué tamaño y con qué nivel', (tester) async {
    await abrir(
      tester,
      const HallazgoVista(
        id: 'h1',
        zona: 'Llanta trasera derecha',
        cuadranteNumero: 26,
        clase: 'golpe',
        tamano: 'mas_20cm',
        nivel: 'v1',
      ),
    );

    expect(find.text('Llanta trasera derecha'), findsOneWidget);
    expect(find.text('Golpe · Más de 20 cm · V1'), findsOneWidget);
  });

  testWidgets('un daño preexistente lo dice: no ocurrió en esta etapa', (tester) async {
    await abrir(
      tester,
      const HallazgoVista(
        id: 'h1', zona: 'Llanta trasera derecha', clase: 'golpe', tamano: 'mas_20cm', nivel: 'v1',
        esPreexistente: true,
      ),
    );

    expect(find.text('Ya venía de antes'), findsOneWidget);
  });

  // La novedad del conductor llega sin nivel: no se puede mostrar en blanco, porque parecería un daño leve.
  testWidgets('sin nivel dice «Por revisar»', (tester) async {
    await abrir(
      tester,
      const HallazgoVista(id: 'h1', zona: 'Capot', clase: 'golpe', tamano: 'mas_20cm'),
    );

    expect(find.text('Golpe · Más de 20 cm · Por revisar'), findsOneWidget);
  });

  testWidgets('sin foto no se deja un hueco', (tester) async {
    await abrir(tester, const HallazgoVista(id: 'h1', zona: 'Capot', clase: 'golpe', tamano: 'mas_20cm'));

    expect(find.byType(Image), findsNothing);
  });

  // En el celular la ficha no tenía salida: se abría, se leía y había que adivinar que se cierra arrastrando.
  testWidgets('se cierra desde la ficha, sin tener que arrastrarla', (tester) async {
    await abrir(tester, const HallazgoVista(id: 'h1', zona: 'Capot', clase: 'golpe', tamano: 'mas_20cm'));
    expect(find.text('Capot'), findsOneWidget);

    await tester.tap(find.widgetWithText(TextButton, 'Cerrar'));
    await tester.pumpAndSettle();

    expect(find.text('Capot'), findsNothing);
  });
}
