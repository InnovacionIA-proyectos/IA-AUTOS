import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/core/tema/tema.dart';
import 'package:traza_autos/features/custodia/presentation/lienzo_firma.dart';

Widget envolver(Widget hijo) => MaterialApp(theme: crearTema(), home: Scaffold(body: SingleChildScrollView(child: hijo)));

void main() {
  group('LienzoFirma', () {
    testWidgets('al dibujar reporta un path SVG que empieza con M y al limpiar reporta null', (tester) async {
      final cambios = <String?>[];
      await tester.pumpWidget(envolver(LienzoFirma(etiqueta: 'Firma de quien entrega', onCambio: cambios.add)));
      expect(find.text('Firme aquí'), findsOneWidget);

      final centro = tester.getCenter(find.text('Firme aquí'));
      final gesto = await tester.startGesture(centro);
      await gesto.moveBy(const Offset(40, 10));
      await gesto.moveBy(const Offset(20, 30));
      await gesto.up();
      await tester.pump();

      expect(cambios.last, isNotNull);
      expect(cambios.last, startsWith('M '));
      expect(RegExp('L ').allMatches(cambios.last!).length, 2);
      expect(find.text('Firme aquí'), findsNothing);

      await tester.tap(find.text('Limpiar'));
      await tester.pump();

      expect(cambios.last, isNull);
      expect(find.text('Firme aquí'), findsOneWidget);
    });

    testWidgets('dos trazos se acumulan en el mismo path', (tester) async {
      final cambios = <String?>[];
      await tester.pumpWidget(envolver(LienzoFirma(etiqueta: 'Firma', onCambio: cambios.add)));
      final centro = tester.getCenter(find.text('Firme aquí'));

      for (final desplazamiento in [const Offset(30, 0), const Offset(0, 30)]) {
        final gesto = await tester.startGesture(centro);
        await gesto.moveBy(desplazamiento);
        await gesto.up();
      }
      await tester.pump();

      expect(RegExp('M ').allMatches(cambios.last!).length, 2);
    });
  });
}
