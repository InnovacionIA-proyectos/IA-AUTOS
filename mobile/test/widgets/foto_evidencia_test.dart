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
  // La memoria de las fotos que no estaban vive fuera del widget, para que sobreviva al redibujo. Entre pruebas
  // hay que limpiarla, o una prueba heredaría lo que falló en la anterior.
  setUp(olvidarFotosQueFaltan);

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

  // Lo que se vio en el patio: una foto que ya no está en el almacén dejaba la pantalla parpadeando entre
  // «buscando» y el resultado, y el teléfono hizo casi seiscientas peticiones en unos minutos. La guarda que
  // había era por enlace, y cada refresco firma una URL distinta: se reiniciaba sola en cada vuelta.
  group('una foto que ya no está no puede entrar en bucle', () {
    /// Reconstruye como lo hace la app al refrescar: enlace nuevo cada vez, y la imagen siempre falla.
    Future<int> vueltas(WidgetTester tester, int cuantas) async {
      var refrescos = 0;
      for (var i = 0; i < cuantas; i++) {
        await tester.pumpWidget(
          MaterialApp(
            theme: crearTema(),
            home: Scaffold(
              body: FotoEvidencia(
                // Cada refresco trae otra firma, igual que el servidor.
                url: 'https://api.test/api/uploads/x.jpg?exp=$i&firma=f$i',
                alto: 100,
                venceEn: DateTime.now().add(const Duration(minutes: 10)),
                alVencer: () => refrescos++,
              ),
            ),
          ),
        );
        await tester.pump();
        await tester.pump();
      }
      return refrescos;
    }

    testWidgets('se pide un enlace nuevo una sola vez, por más vueltas que dé', (tester) async {
      expect(await vueltas(tester, 6), 1, reason: 'si el enlace nuevo también falla, el problema no es el enlace');
    });

    testWidgets('y se dice que ya no está, en vez de quedarse intentando', (tester) async {
      await vueltas(tester, 3);

      expect(find.text('La foto ya no está disponible'), findsOneWidget);
    });

    /// Lo que hacía la pantalla de verdad, y que las vueltas de arriba no reproducen: mientras vuelve a buscar,
    /// la lista se cambia por un renglón de «buscando…». La foto sale del árbol y su estado se va con ella, así
    /// que a la vuelta nace una nueva, sin memoria de que el archivo ya había fallado.
    Future<int> vueltasVaciando(WidgetTester tester, int cuantas) async {
      var refrescos = 0;
      for (var i = 0; i < cuantas; i++) {
        await tester.pumpWidget(
          MaterialApp(theme: crearTema(), home: const Scaffold(body: Text('Buscando…'))),
        );
        await tester.pump();
        await tester.pumpWidget(
          MaterialApp(
            theme: crearTema(),
            home: Scaffold(
              body: FotoEvidencia(
                url: 'https://api.test/api/uploads/x.jpg?exp=$i&firma=f$i',
                alto: 100,
                venceEn: DateTime.now().add(const Duration(minutes: 10)),
                alVencer: () => refrescos++,
              ),
            ),
          ),
        );
        await tester.pump();
        await tester.pump();
      }
      return refrescos;
    }

    testWidgets('tampoco si la pantalla se vacía mientras busca y la foto se vuelve a montar', (tester) async {
      expect(
        await vueltasVaciando(tester, 6),
        1,
        reason: 'el archivo es el mismo aunque cambien la firma y el widget: pedir otro enlace no lo trae',
      );
    });

    testWidgets('y al volver ya se sabe que no está, sin pedir nada', (tester) async {
      await vueltasVaciando(tester, 2);

      expect(find.text('La foto ya no está disponible'), findsOneWidget);
    });
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
