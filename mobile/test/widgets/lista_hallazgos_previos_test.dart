import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intl/date_symbol_data_local.dart';
import 'package:traza_autos/core/tema/tema.dart';
import 'package:traza_autos/features/catalogos/providers.dart';
import 'package:traza_autos/features/inspecciones/domain/hallazgo_previo.dart';
import 'package:traza_autos/features/inspecciones/presentation/lista_hallazgos_previos.dart';
import 'package:traza_autos/features/vehiculos/domain/inventario.dart';

import '../support/arnes.dart';
import '../support/dobles_catalogos.dart';

/// Con qué venía el vehículo, en texto. El dibujo dice **dónde**; esta lista dice **qué** era y **de dónde viene**,
/// que es lo que permite decidir si el daño que se tiene delante es el mismo o uno nuevo.

final previo = HallazgoPrevio(
  id: 'h-previo',
  zona: 'Izquierdo 7 · Espejo retrovisor',
  proceso: 'cargue',
  momento: Momento.salida,
  cuadranteNumero: 7,
  lado: 'izquierdo',
  clase: 'golpe',
  tamano: 'mas_20cm',
  nivel: 'v1',
  codigoMtrack: 'MTK-0417',
  cerradaEn: DateTime.utc(2026, 10, 1),
);

Future<Arnes> montar(
  WidgetTester tester, {
  List<HallazgoPrevio> previos = const [],
  bool cargando = false,
  bool conSenal = true,
}) async {
  final arnes = Arnes();
  arnes.red.estable = conSenal;
  await tester.pumpWidget(
    ProviderScope(
      retry: (_, _) => null,
      overrides: [
        ...arnes.overrides,
        catalogosRepositorioProvider.overrideWithValue(
          CatalogosRepositorioFalso()
            ..guardado = catalogosCon('Golpe', etapas: etapasDePrueba)
            ..remoto = catalogosCon('Golpe', etapas: etapasDePrueba),
        ),
      ],
      child: MaterialApp(
        theme: crearTema(),
        home: Scaffold(
          body: SingleChildScrollView(
            child: ListaHallazgosPrevios(previos: previos, cargando: cargando),
          ),
        ),
      ),
    ),
  );
  for (var i = 0; i < 5; i++) {
    await tester.pump(const Duration(milliseconds: 50));
  }
  return arnes;
}

void main() {
  // La fecha del origen se formatea en es_CO: sin esto el formateador lanza al construir la fila.
  setUpAll(() => initializeDateFormatting('es_CO'));

  testWidgets('mientras se buscan no se afirma que no hay', (tester) async {
    await montar(tester, cargando: true);

    expect(find.text('Buscando con qué venía el vehículo…'), findsOneWidget);
    expect(find.text('Sin daños anteriores registrados.'), findsNothing);
  });

  testWidgets('un vehículo sin historial lo dice, en vez de dejar un hueco', (tester) async {
    await montar(tester);

    expect(find.text('Sin daños anteriores registrados.'), findsOneWidget);
  });

  // Sin señal y sin copia, la lista vacía no significa que el vehículo venga sano. Decir que viene sano sería
  // lo peor que puede decir esta pantalla.
  testWidgets('sin señal no se afirma que viene sano', (tester) async {
    await montar(tester, conSenal: false);

    expect(find.text('Sin señal: no se pudo comprobar si trae daños anteriores.'), findsOneWidget);
    expect(find.text('Sin daños anteriores registrados.'), findsNothing);
  });

  testWidgets('cada daño va con su zona, su clasificación y de qué viaje y etapa viene', (tester) async {
    await montar(tester, previos: [previo]);

    expect(find.text('Izquierdo 7 · Espejo retrovisor'), findsOneWidget);
    expect(find.text('Golpe · Más de 20 cm · V1'), findsOneWidget);
    expect(find.textContaining('MTK-0417 · Cargue · Inspección de salida'), findsOneWidget);
  });

  testWidgets('una novedad que nadie clasificó se dice «Sin calificar», no se deja a medias', (tester) async {
    await montar(tester, previos: [
      HallazgoPrevio(
        id: 'h2',
        zona: 'Derecho 3',
        proceso: 'transporte',
        momento: Momento.llegada,
        clase: 'golpe',
        tamano: 'mas_20cm',
        cerradaEn: DateTime.utc(2026, 10, 2),
      ),
    ]);

    expect(find.text('Golpe · Más de 20 cm · Sin calificar'), findsOneWidget);
  });

  // Son de informes ya cerrados, que son inmutables: ofrecer un botón sería prometer algo que el servidor
  // rechazaría.
  testWidgets('no se pueden editar ni quitar: la lista no ofrece ninguna acción', (tester) async {
    await montar(tester, previos: [previo]);

    expect(find.byType(ElevatedButton), findsNothing);
    expect(find.byType(TextButton), findsNothing);
    expect(find.byType(IconButton), findsNothing);
    expect(find.textContaining('se muestran para comparar, no se pueden cambiar'), findsOneWidget);
  });

  testWidgets('dice cuántos son, para no tener que contarlos', (tester) async {
    await montar(tester, previos: [previo]);

    expect(find.text('1 daño'), findsOneWidget);
  });
}
