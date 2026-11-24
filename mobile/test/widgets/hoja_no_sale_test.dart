import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/core/tema/tema.dart';
import 'package:traza_autos/features/catalogos/domain/catalogos.dart';
import 'package:traza_autos/features/vehiculos/domain/inventario.dart';
import 'package:traza_autos/features/vehiculos/presentation/hoja_no_sale.dart';

/// S8-HU-02.2 CA-1: la hoja de «no sale» con los motivos del catálogo del servidor.

const hallazgoV1Plus = HallazgoVista(
  id: 'h1',
  zona: 'Izquierdo 7 · Espejo retrovisor',
  cuadranteNumero: 7,
  lado: 'izquierdo',
  clase: 'golpe',
  tamano: 'mas_20cm',
  nivel: 'v1_plus',
);

const otroV1Plus = HallazgoVista(id: 'h2', zona: 'Frente 1 · Capó', clase: 'golpe', tamano: 'mas_20cm', nivel: 'v1_plus');

const danoDelConductor = HallazgoVista(id: 'c1', zona: 'Puerta', tamano: 'mas_20cm', origen: 'conductor');

/// Los motivos como los sirve la API, con sus tres marcas.
const motivosNoSale = [
  {'id': 'n1', 'codigo': 'dano_v1_plus', 'nombre': 'Daño V1+', 'orden': 1, 'activo': true, 'exige_hallazgo': true, 'exige_texto': false, 'solo_sin_inspector': false},
  {'id': 'n2', 'codigo': 'dano_reportado_conductor', 'nombre': 'Daño reportado por el conductor', 'orden': 2, 'activo': true, 'exige_hallazgo': true, 'exige_texto': false, 'solo_sin_inspector': true},
  {'id': 'n3', 'codigo': 'no_llego_al_patio', 'nombre': 'Carro no llegó al patio', 'orden': 3, 'activo': true, 'exige_hallazgo': false, 'exige_texto': false, 'solo_sin_inspector': false},
  {'id': 'n4', 'codigo': 'faltan_documentos', 'nombre': 'Faltan documentos', 'orden': 4, 'activo': true, 'exige_hallazgo': false, 'exige_texto': false, 'solo_sin_inspector': false},
  {'id': 'n6', 'codigo': 'otro', 'nombre': 'Otro', 'orden': 6, 'activo': true, 'exige_hallazgo': false, 'exige_texto': true, 'solo_sin_inspector': false},
];

Catalogos _catalogos({List<Map<String, dynamic>> motivos = motivosNoSale}) => Catalogos.desdeRespuesta(
      {
        'version': 'v1',
        'catalogos': {
          Catalogo.clasesDano: [
            {'id': 'i1', 'codigo': 'golpe', 'nombre': 'Golpe', 'orden': 1, 'activo': true},
          ],
          Catalogo.motivosNoSale: motivos,
        },
      },
      usuarioId: 'u1',
    );

/// Abre la hoja y recoge lo que devuelva.
Future<List<DatosNoSale?>> abrirHoja(
  WidgetTester tester, {
  List<HallazgoVista> v1Plus = const [hallazgoV1Plus],
  List<HallazgoVista> delConductor = const [],
  bool admiteSinInspector = false,
  List<Map<String, dynamic>> motivos = motivosNoSale,
}) async {
  await tester.binding.setSurfaceSize(const Size(600, 1600));
  addTearDown(() => tester.binding.setSurfaceSize(null));
  final respuestas = <DatosNoSale?>[];
  await tester.pumpWidget(
    MaterialApp(
      theme: crearTema(),
      home: Builder(
        builder: (context) => Scaffold(
          body: Center(
            child: ElevatedButton(
              onPressed: () async => respuestas.add(
                await pedirNoSale(
                  context,
                  catalogos: _catalogos(motivos: motivos),
                  hallazgosV1Plus: v1Plus,
                  danosDelConductor: delConductor,
                  admiteSinInspector: admiteSinInspector,
                ),
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

Future<void> elegir(WidgetTester tester, String motivo) async {
  await tester.tap(find.text(motivo));
  await tester.pumpAndSettle();
}

Future<void> marcar(WidgetTester tester) async {
  await tester.tap(find.widgetWithText(FilledButton, 'Marcar que no sale'));
  await tester.pumpAndSettle();
}

bool habilitado(WidgetTester tester, String motivo) =>
    tester.widget<RadioListTile<String>>(find.widgetWithText(RadioListTile<String>, motivo)).enabled ?? true;

FilledButton botonMarcar(WidgetTester tester) => tester.widget<FilledButton>(find.widgetWithText(FilledButton, 'Marcar que no sale'));

void main() {
  testWidgets('ofrece los motivos del catálogo con su nombre, y sin elegir no deja marcar', (tester) async {
    await abrirHoja(tester);

    for (final nombre in ['Daño V1+', 'Daño reportado por el conductor', 'Carro no llegó al patio', 'Faltan documentos', 'Otro']) {
      expect(find.text(nombre), findsOneWidget, reason: nombre);
    }
    expect(botonMarcar(tester).onPressed, isNull);
  });

  testWidgets('un motivo que no pide nada se manda solo', (tester) async {
    final respuestas = await abrirHoja(tester);

    await elegir(tester, 'Faltan documentos');
    expect(find.widgetWithText(TextField, 'Motivo'), findsNothing, reason: 'no pide texto');
    await marcar(tester);

    expect(respuestas.single, (motivoCodigo: 'faltan_documentos', motivoTexto: null, hallazgoId: null, vinReemplazo: null));
  });

  testWidgets('«Daño V1+» adjunta el único V1+ sin hacer elegir', (tester) async {
    final respuestas = await abrirHoja(tester);

    await elegir(tester, 'Daño V1+');
    expect(find.text('Por: Izquierdo 7 · Espejo retrovisor: Golpe'), findsOneWidget);
    await marcar(tester);

    expect(respuestas.single?.motivoCodigo, 'dano_v1_plus');
    expect(respuestas.single?.hallazgoId, 'h1');
  });

  testWidgets('con dos V1+ hay que elegir cuál lo causa', (tester) async {
    final respuestas = await abrirHoja(tester, v1Plus: [hallazgoV1Plus, otroV1Plus]);

    await elegir(tester, 'Daño V1+');
    expect(botonMarcar(tester).onPressed, isNull);
    await elegir(tester, 'Frente 1 · Capó: Golpe');
    await marcar(tester);

    expect(respuestas.single?.hallazgoId, 'h2');
  });

  testWidgets('sin un V1+ en un informe cerrado, «Daño V1+» se ve pero no se elige', (tester) async {
    await abrirHoja(tester, v1Plus: const []);

    expect(habilitado(tester, 'Daño V1+'), isFalse);
    expect(find.text('No hay un daño V1+ en un informe cerrado de este carro'), findsOneWidget);
    expect(habilitado(tester, 'Faltan documentos'), isTrue);
  });

  testWidgets('«Daño reportado por el conductor» no se elige donde el lugar no admite cargue sin inspector', (tester) async {
    await abrirHoja(tester, delConductor: const [danoDelConductor]);

    expect(habilitado(tester, 'Daño reportado por el conductor'), isFalse);
    expect(find.text('Este lugar de cargue no lo admite'), findsOneWidget);
  });

  testWidgets('donde sí lo admite y hay un daño del conductor sin calificar, lo adjunta', (tester) async {
    final respuestas = await abrirHoja(tester, delConductor: const [danoDelConductor], admiteSinInspector: true);

    await elegir(tester, 'Daño reportado por el conductor');
    await marcar(tester);

    expect(respuestas.single?.motivoCodigo, 'dano_reportado_conductor');
    expect(respuestas.single?.hallazgoId, 'c1');
  });

  testWidgets('«Otro» exige el texto antes de enviar', (tester) async {
    final respuestas = await abrirHoja(tester);

    await elegir(tester, 'Otro');
    expect(botonMarcar(tester).onPressed, isNull);
    await tester.enterText(find.widgetWithText(TextField, 'Motivo'), '  Lo pidió el cliente  ');
    await tester.pumpAndSettle();
    await marcar(tester);

    expect(respuestas.single, (motivoCodigo: 'otro', motivoTexto: 'Lo pidió el cliente', hallazgoId: null, vinReemplazo: null));
  });

  // El VIN se escribe a mano, 17 caracteres, en un teclado de celular que sugiere y mete espacios. Un espacio de
  // más y el servidor responde «No existe en el inventario», que parece un error del dato y es del tecleo.
  testWidgets('el VIN de reemplazo sigue opcional y viaja en mayúsculas y sin espacios', (tester) async {
    final respuestas = await abrirHoja(tester);

    await elegir(tester, 'Carro no llegó al patio');
    await tester.enterText(find.widgetWithText(TextField, 'VIN de reemplazo (opcional)'), ' 93yrbc00xr 7654321 ');
    await tester.pumpAndSettle();
    await marcar(tester);

    expect(respuestas.single?.vinReemplazo, '93YRBC00XR7654321');
  });

  testWidgets('sin los motivos descargados lo dice y no deja marcar', (tester) async {
    await abrirHoja(tester, motivos: const []);

    expect(find.text('No se han descargado los motivos. Conéctate para descargarlos.'), findsOneWidget);
    expect(botonMarcar(tester).onPressed, isNull);
  });
}
