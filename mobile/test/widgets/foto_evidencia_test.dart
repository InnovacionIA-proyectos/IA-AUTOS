import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/core/tema/tema.dart';
import 'package:traza_autos/core/ui/foto_evidencia.dart';

/// Las fotos de evidencia se abren con un enlace firmado que vence. Lo que importa aquí es que el enlace vaya
/// solo, que un enlace vencido no se reintente y que sin señal se diga, en vez de dejar un hueco gris.
Future<int> montar(
  WidgetTester tester, {
  String? url = 'https://api.test/api/uploads/x.jpg?exp=1&firma=abc',
  DateTime? venceEn,
  bool sinSenal = false,
}) async {
  var refrescos = 0;
  await tester.pumpWidget(
    MaterialApp(
      theme: crearTema(),
      home: Scaffold(
        body: FotoEvidencia(
          url: url,
          alto: 100,
          venceEn: venceEn,
          sinSenal: sinSenal,
          alVencer: () => refrescos++,
        ),
      ),
    ),
  );
  await tester.pump();
  return refrescos;
}

void main() {
  testWidgets('el enlace firmado va solo, sin Authorization', (tester) async {
    await montar(tester, venceEn: DateTime.now().add(const Duration(minutes: 10)));

    final imagen = tester.widget<Image>(find.byType(Image));
    final red = imagen.image as NetworkImage;
    expect(red.headers, anyOf(isNull, isEmpty), reason: 'la firma es lo que autoriza, no la sesión');
  });

  testWidgets('un enlace vencido no se pide: se refresca la fuente', (tester) async {
    await montar(tester, venceEn: DateTime.now().subtract(const Duration(minutes: 1)));
    await tester.pump();

    expect(find.byType(Image), findsNothing, reason: 'pedirlo daría 403');
    expect(find.text('Actualizando la foto…'), findsOneWidget);
  });

  testWidgets('sin señal se dice, en vez de dejar un hueco', (tester) async {
    await montar(tester, sinSenal: true, venceEn: DateTime.now().add(const Duration(minutes: 10)));

    expect(find.text('Sin conexión'), findsOneWidget);
    expect(find.byType(Image), findsNothing);
  });

  testWidgets('sin enlace tampoco se queda en blanco', (tester) async {
    await montar(tester, url: null);

    expect(find.text('Sin foto'), findsOneWidget);
  });

  // Refrescar la fuente vuelve a construir este widget; sin el control, cada reconstrucción pediría otra vez.
  testWidgets('la fuente se refresca una sola vez por enlace', (tester) async {
    var refrescos = 0;
    final vencido = DateTime.now().subtract(const Duration(minutes: 1));
    Widget arbol() => MaterialApp(
          theme: crearTema(),
          home: Scaffold(
            body: FotoEvidencia(url: 'https://api.test/x.jpg', alto: 100, venceEn: vencido, alVencer: () => refrescos++),
          ),
        );

    await tester.pumpWidget(arbol());
    await tester.pump();
    await tester.pumpWidget(arbol());
    await tester.pump();

    expect(refrescos, 1);
  });
}
