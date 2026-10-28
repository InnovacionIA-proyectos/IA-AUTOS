import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/core/tema/tema.dart';
import 'package:traza_autos/features/inspecciones/domain/checklist_vistas.dart';
import 'package:traza_autos/features/inspecciones/domain/foto_local.dart';
import 'package:traza_autos/features/inspecciones/domain/hallazgo_local.dart';
import 'package:traza_autos/features/inspecciones/presentation/filas_captura.dart';

/// Lo que se captura tiene que verse. Si no, quien está en el patio no puede comprobar que quedó bien y se
/// entera cuando ya no tiene el vehículo delante.
///
/// Las fotos se muestran con `Image.file` sobre rutas que no existen en la prueba: Flutter lo resuelve como un
/// error de carga sin romper el árbol, así que lo que se comprueba es que el hueco de la imagen esté.

const vistaObligatoria = VistaChecklist(id: 'v-frontal', codigo: 'frontal', nombre: 'Frontal', obligatoria: true);
const vistaAdicional = VistaChecklist(id: 'v-baul', codigo: 'baul', nombre: 'Piezas en el baúl', obligatoria: false);

Future<void> montar(WidgetTester tester, Widget fila) => tester.pumpWidget(
      MaterialApp(
        theme: crearTema(),
        home: Scaffold(body: ListView(children: [fila])),
      ),
    );

void main() {
  group('la fila de una vista', () {
    testWidgets('sin foto dice qué falta y ofrece tomarla', (tester) async {
      await montar(tester, const FilaVista(vista: vistaObligatoria, foto: null, alTomar: null));

      expect(find.text('Frontal'), findsOneWidget);
      expect(find.text('Tomar'), findsOneWidget);
    });

    testWidgets('la adicional dice cuándo se toma; la obligatoria no lo necesita', (tester) async {
      await montar(tester, const FilaVista(vista: vistaAdicional, foto: null, alTomar: null));
      expect(find.text('Solo si hay faltante o daño'), findsOneWidget);

      await montar(tester, const FilaVista(vista: vistaObligatoria, foto: null, alTomar: null));
      expect(find.text('Solo si hay faltante o daño'), findsNothing);
    });

    // El defecto que apareció en el celular: bajo techo la precisión siempre es mala, así que el aviso salía
    // siempre y tapaba la nota, que era lo único que la persona había escrito.
    testWidgets('la nota se ve aunque el GPS sea impreciso', (tester) async {
      await montar(
        tester,
        const FilaVista(
          vista: vistaAdicional,
          foto: FotoLocal(
            ruta: 'baul.jpg',
            selloFecha: '2026-01-02T10:00:00.000Z',
            selloLat: 6.2,
            selloLng: -75.5,
            vistaId: 'v-baul',
            nota: 'Falta el gato',
            precision: 120,
          ),
          alTomar: null,
        ),
      );

      expect(find.text('Falta el gato'), findsOneWidget);
      expect(find.textContaining('poco precisa'), findsOneWidget, reason: 'las dos cosas, no una u otra');
    });

    testWidgets('una foto sin ubicación pide repetirla', (tester) async {
      await montar(
        tester,
        const FilaVista(
          vista: vistaObligatoria,
          foto: FotoLocal(ruta: 'frontal.jpg', selloFecha: '2026-01-02T10:00:00.000Z', vistaId: 'v-frontal'),
          alTomar: null,
        ),
      );

      expect(find.textContaining('Sin ubicación'), findsOneWidget);
      expect(find.text('Repetir'), findsOneWidget);
    });

    testWidgets('con buena precisión no se avisa de nada', (tester) async {
      await montar(
        tester,
        const FilaVista(
          vista: vistaObligatoria,
          foto: FotoLocal(
            ruta: 'frontal.jpg',
            selloFecha: '2026-01-02T10:00:00.000Z',
            selloLat: 6.2,
            selloLng: -75.5,
            vistaId: 'v-frontal',
            precision: 8,
          ),
          alTomar: null,
        ),
      );

      expect(find.textContaining('poco precisa'), findsNothing);
      expect(find.textContaining('Sin ubicación'), findsNothing);
    });
  });

  group('la fila de una novedad', () {
    // El otro defecto del celular: el daño se veía descrito pero su foto no aparecía por ningún lado.
    testWidgets('muestra su foto', (tester) async {
      await montar(
        tester,
        FilaHallazgo(
          hallazgo: const HallazgoLocal(
            cuadranteNumero: 52,
            lado: null,
            zona: 'Capot',
            clase: 'golpe',
            tamano: '1_5cm',
            foto: FotoLocal(ruta: 'dano.jpg', selloFecha: '2026-01-02T10:00:00.000Z'),
          ),
          clase: 'Golpe',
          tamano: 'De 1 a 5 cm',
          alQuitar: () {},
        ),
      );

      expect(find.text('Capot: Golpe (De 1 a 5 cm)'), findsOneWidget);
      expect(find.bySemanticsLabel('Foto de la novedad en Capot'), findsOneWidget);
    });

    testWidgets('sin foto no se inventa un hueco', (tester) async {
      await montar(
        tester,
        FilaHallazgo(
          hallazgo: const HallazgoLocal(cuadranteNumero: 52, lado: null, zona: 'Capot', clase: 'golpe', tamano: '1_5cm'),
          clase: 'Golpe',
          tamano: 'De 1 a 5 cm',
          alQuitar: () {},
        ),
      );

      expect(find.byType(Image), findsNothing);
    });

    testWidgets('la observación se ve debajo de la zona', (tester) async {
      await montar(
        tester,
        FilaHallazgo(
          hallazgo: const HallazgoLocal(
            cuadranteNumero: 52,
            lado: null,
            zona: 'Capot',
            clase: 'golpe',
            tamano: '1_5cm',
            nota: 'golpe cerca de la bisagra',
          ),
          clase: 'Golpe',
          tamano: 'De 1 a 5 cm',
          alQuitar: () {},
        ),
      );

      expect(find.text('golpe cerca de la bisagra'), findsOneWidget);
    });
  });

  // A 56 px no se distingue si una foto salió movida u oscura, y eso se descubre cuando ya no se tiene el
  // vehículo delante. Tocarla la abre en grande.
  group('abrir la foto', () {
    testWidgets('tocar la miniatura de una vista abre el visor', (tester) async {
      await montar(
        tester,
        const FilaVista(
          vista: vistaObligatoria,
          foto: FotoLocal(ruta: 'frontal.jpg', selloFecha: '2026-01-02T10:00:00.000Z', selloLat: 6.2, selloLng: -75.5),
          alTomar: null,
        ),
      );

      await tester.tap(find.bySemanticsLabel('Foto de Frontal'));
      await tester.pumpAndSettle();

      expect(find.byType(InteractiveViewer), findsOneWidget);
      expect(find.text('Frontal'), findsWidgets);
    });

    testWidgets('tocar la foto de una novedad también', (tester) async {
      await montar(
        tester,
        FilaHallazgo(
          hallazgo: const HallazgoLocal(
            cuadranteNumero: 52,
            lado: null,
            zona: 'Capot',
            clase: 'golpe',
            tamano: '1_5cm',
            foto: FotoLocal(ruta: 'dano.jpg', selloFecha: '2026-01-02T10:00:00.000Z'),
          ),
          clase: 'Golpe',
          tamano: 'De 1 a 5 cm',
          alQuitar: () {},
        ),
      );

      await tester.tap(find.bySemanticsLabel('Foto de la novedad en Capot'));
      await tester.pumpAndSettle();

      expect(find.byType(InteractiveViewer), findsOneWidget);
      expect(find.text('Novedad en Capot'), findsOneWidget);
    });
  });

  // Lo que se vio en el celular con varias novedades: las fotos quedaban una encima de otra, casi tocandose, y
  // la lista se leia como un solo bloque.
  testWidgets('varias novedades no quedan pegadas una a otra', (tester) async {
    FilaHallazgo fila(String zona) => FilaHallazgo(
          hallazgo: HallazgoLocal(
            cuadranteNumero: 52,
            lado: null,
            zona: zona,
            clase: 'golpe',
            tamano: '1_5cm',
            foto: const FotoLocal(ruta: 'dano.jpg', selloFecha: '2026-01-02T10:00:00.000Z'),
          ),
          clase: 'Golpe',
          tamano: 'De 1 a 5 cm',
          alQuitar: () {},
        );

    await tester.pumpWidget(
      MaterialApp(
        theme: crearTema(),
        home: Scaffold(body: ListView(children: [fila('Capot'), fila('Parabrisas')])),
      ),
    );

    // El hueco entre las dos fotos es lo que se ve pegado en la pantalla.
    final arriba = tester.getRect(find.byType(Image).first);
    final abajo = tester.getRect(find.byType(Image).last);
    expect(abajo.top - arriba.bottom, greaterThanOrEqualTo(Espacio.sm), reason: 'las fotos no pueden tocarse');
  });
}
