import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/core/tema/tema.dart';
import 'package:traza_autos/features/custodia/presentation/lienzo_firma.dart';
import 'package:traza_autos/features/vehiculos/domain/coords_diagrama.dart';
import 'package:traza_autos/features/vehiculos/presentation/diagrama_vehiculo.dart';

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

  group('DiagramaVehiculo', () {
    testWidgets('en lectura no ofrece botones por parte y marca los puntos', (tester) async {
      await tester.pumpWidget(envolver(const DiagramaVehiculo(marcas: [CoordsDiagrama(0.5, 0.66), CoordsDiagrama(0.2, 0.3)])));

      expect(find.byType(ActionChip), findsNothing);
      expect(find.bySemanticsLabel('Diagrama del vehículo con 2 novedades marcadas'), findsOneWidget);
    });

    testWidgets('sin marcas lo dice en su descripción accesible', (tester) async {
      await tester.pumpWidget(envolver(const DiagramaVehiculo(marcas: [])));
      expect(find.bySemanticsLabel('Diagrama del vehículo sin novedades'), findsOneWidget);
    });

    testWidgets('una parte única reporta de inmediato su etiqueta y coordenadas', (tester) async {
      final elegidos = <(String, CoordsDiagrama)>[];
      await tester.pumpWidget(envolver(DiagramaVehiculo(marcas: const [], onElegir: (e, c) => elegidos.add((e, c)))));

      await tester.tap(find.widgetWithText(ActionChip, 'Capot'));
      await tester.pump();

      expect(elegidos.single, ('Capot', const CoordsDiagrama(0.5, 0.66)));
    });

    testWidgets('una parte con posiciones pregunta cuál y reporta la elegida', (tester) async {
      final elegidos = <(String, CoordsDiagrama)>[];
      await tester.pumpWidget(envolver(DiagramaVehiculo(marcas: const [], onElegir: (e, c) => elegidos.add((e, c)))));

      await tester.tap(find.widgetWithText(ActionChip, 'Guardafango ▾'));
      await tester.pumpAndSettle();
      expect(find.text('¿En qué posición?'), findsOneWidget);

      await tester.tap(find.widgetWithText(ActionChip, 'Delantero derecho').last);
      await tester.pumpAndSettle();

      expect(elegidos.single, ('Guardafango delantero derecho', const CoordsDiagrama(0.84, 0.66)));
    });

    testWidgets('cerrar el selector de posición sin elegir no reporta nada', (tester) async {
      final elegidos = <String>[];
      await tester.pumpWidget(envolver(DiagramaVehiculo(marcas: const [], onElegir: (e, _) => elegidos.add(e))));

      await tester.tap(find.widgetWithText(ActionChip, 'Puertas ▾'));
      await tester.pumpAndSettle();
      await tester.tapAt(const Offset(10, 10)); // toca fuera de la hoja
      await tester.pumpAndSettle();

      expect(elegidos, isEmpty);
    });
  });
}
