import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intl/date_symbol_data_local.dart';
import 'package:traza_autos/core/errores/error_api.dart';
import 'package:traza_autos/core/tema/tema.dart';
import 'package:traza_autos/features/sync/domain/job.dart';
import 'package:traza_autos/features/sync/presentation/cola_page.dart';

import '../support/arnes.dart';
import '../support/dobles_sync.dart';

/// Lo que se puede tocar en la cola, que es donde se borran fotos. Un envío ajeno o uno que el servidor rechazó
/// por permisos no son de quien está mirando: ofrecerle «Descartar» sería dejarle borrar evidencia de otra
/// persona y entorpecerle el trabajo, sin que ella se entere.

Future<void> abrir(WidgetTester tester, Arnes arnes) async {
  await tester.pumpWidget(
    ProviderScope(
      retry: (_, _) => null,
      overrides: arnes.overrides,
      child: MaterialApp(theme: crearTema(), home: const ColaPage()),
    ),
  );
  for (var i = 0; i < 8; i++) {
    await tester.pump(const Duration(milliseconds: 50));
  }
}

Finder get descartar => find.widgetWithText(OutlinedButton, 'Descartar');
Finder get reintentar => find.widgetWithText(FilledButton, 'Reintentar');

void main() {
  // La fila muestra la fecha con `DateFormat('es_CO')`, que `main.dart` prepara al arrancar la app.
  setUpAll(() => initializeDateFormatting('es_CO'));

  testWidgets('un envío propio con error se puede reintentar y descartar', (tester) async {
    final arnes = Arnes();
    await arnes.cola.agregar(informe(id: 'j', fallido: true));

    await abrir(tester, arnes);

    expect(find.text('Con error'), findsOneWidget);
    expect(reintentar, findsOneWidget);
    expect(descartar, findsOneWidget);
  });

  // S8-HU-02.2 CA-4 y CA-6: retirado no es «con error»; reintentar daría lo mismo, así que solo se quita.
  testWidgets('uno retirado dice por qué y solo se puede quitar', (tester) async {
    final arnes = Arnes();
    await arnes.cola.agregar(
      informe(id: 'j').conIntento(intentos: 1, error: 'El carro ya no está en el viaje', codigo: codigoRetirado, fallido: true),
    );

    await abrir(tester, arnes);

    expect(find.text('Retirado'), findsOneWidget);
    expect(find.text('Con error'), findsNothing);
    expect(find.text('El carro ya no está en el viaje'), findsOneWidget);
    expect(reintentar, findsNothing);
    expect(find.widgetWithText(OutlinedButton, 'Quitar'), findsOneWidget);
  });

  testWidgets('uno de otra persona se ve, pero no se puede tocar', (tester) async {
    final arnes = Arnes();
    await arnes.cola.agregar(informe(id: 'j', usuarioId: 'otra-persona', fallido: true));

    await abrir(tester, arnes);

    expect(find.text('De otro usuario'), findsOneWidget);
    expect(find.textContaining('cuando entre con su usuario'), findsOneWidget);
    expect(descartar, findsNothing);
    expect(reintentar, findsNothing);
  });

  // El caso del job adoptado: es mío en el dispositivo, pero la inspección la creó otra persona y el servidor no
  // me deja continuarla. Borrarlo sería tirar sus fotos.
  testWidgets('uno que el servidor rechazó por permisos tampoco', (tester) async {
    final arnes = Arnes();
    await arnes.cola.agregar(
      informe(id: 'j', fallido: true).conIntento(
        intentos: 1,
        error: 'No tiene permisos sobre esta inspección',
        codigo: codigoProhibido,
        fallido: true,
      ),
    );

    await abrir(tester, arnes);

    expect(find.textContaining('solo ella puede terminarlo'), findsOneWidget);
    expect(descartar, findsNothing);
    expect(reintentar, findsNothing);
  });

  testWidgets('un rechazo que sí es suyo conserva la salida de la cola', (tester) async {
    final arnes = Arnes();
    await arnes.cola.agregar(
      informe(id: 'j', fallido: true).conIntento(
        intentos: 1,
        error: 'Momento inválido: use salida o llegada',
        codigo: 'VALIDATION_ERROR',
        fallido: true,
      ),
    );

    await abrir(tester, arnes);

    expect(descartar, findsOneWidget, reason: 'si no, queda atrapado con un envío que nunca va a salir');
    expect(find.textContaining('Momento inválido'), findsOneWidget);
  });

  // S8-HU-07.3: «Captura incompleta» es de un informe. A la novedad del viaje rechazada con 400 no le falta ninguna
  // captura: se ve «Con error» con el mensaje del servidor y se puede reintentar o descartar.
  testWidgets('un 400 de la novedad del viaje no se ve como «Captura incompleta»', (tester) async {
    final arnes = Arnes();
    await arnes.cola.agregar(
      NovedadViajeJob(
        id: 'nv',
        etiqueta: 'Novedad del viaje: Otro',
        creadoEn: 1,
        usuarioId: usuarioPrueba,
        viajeId: 'v1',
        tipoNovedad: 'otro',
        texto: 'Paro en el peaje',
        fecha: '2026-10-21T21:20:00.000Z',
      ).conIntento(intentos: 1, error: 'Datos inválidos\n· Ese tipo de novedad no es para tu rol', codigo: 'VALIDATION_ERROR', fallido: true),
    );
    await arnes.cola.agregar(informe(id: 'j').conIntento(intentos: 1, error: 'Falta la foto', codigo: 'VALIDATION_ERROR', fallido: true));

    await abrir(tester, arnes);

    expect(find.text('Captura incompleta'), findsOneWidget, reason: 'solo la del informe');
    expect(find.text('Con error'), findsOneWidget, reason: 'la de la novedad del viaje');
    expect(find.textContaining('Ese tipo de novedad no es para tu rol'), findsOneWidget);
  });

  testWidgets('sin nada en la cola lo dice', (tester) async {
    await abrir(tester, Arnes());

    expect(find.text('Todo enviado'), findsOneWidget);
  });
}
