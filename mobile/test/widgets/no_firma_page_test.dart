import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/core/tema/tema.dart';
import 'package:traza_autos/features/custodia/presentation/no_firma_page.dart';
import 'package:traza_autos/features/custodia/providers.dart';

import '../support/arnes.dart';
import '../support/dobles_custodia.dart';

/// Una constancia de no firma vale por lo que aporta: el motivo, un testigo que no sea ninguna de las dos
/// partes, y una foto. Sin las tres cosas no es una constancia, y registrarla a medias sería peor que no
/// tenerla, porque deja cerrada la transferencia con un papel vacío.

Future<UsuariosRepositorioFalso> abrir(WidgetTester tester) async {
  await tester.binding.setSurfaceSize(const Size(600, 2000));
  addTearDown(() => tester.binding.setSurfaceSize(null));
  final usuarios = UsuariosRepositorioFalso();
  await tester.pumpWidget(
    ProviderScope(
      retry: (_, _) => null,
      overrides: [
        ...Arnes().overrides,
        usuariosRepositorioProvider.overrideWithValue(usuarios),
      ],
      child: MaterialApp(
        theme: crearTema(),
        home: NoFirmaPage(
          vin: 'VIN00000000000001',
          inspeccionId: 'i1',
          proceso: 'Cargue',
          entregaId: luis.id,
          recibeId: ana.id,
          nombreQueNoFirma: luis.nombre,
        ),
      ),
    ),
  );
  for (var i = 0; i < 6; i++) {
    await tester.pump(const Duration(milliseconds: 50));
  }
  return usuarios;
}

void main() {
  testWidgets('dice de quién es la constancia y qué queda registrado', (tester) async {
    await abrir(tester);

    expect(find.textContaining('Luis Gómez no firmó'), findsOneWidget);
  });

  // El servidor lo exige y tiene razón: una de las partes no atestigua sobre sí misma.
  testWidgets('el testigo es una tercera persona: las dos partes no se ofrecen', (tester) async {
    await abrir(tester);

    expect(find.widgetWithText(ChoiceChip, 'Marta Ríos'), findsOneWidget);
    expect(find.widgetWithText(ChoiceChip, 'Luis Gómez'), findsNothing);
    expect(find.widgetWithText(ChoiceChip, 'Ana Pérez'), findsNothing);
  });

  testWidgets('no se registra mientras falte algo, y se dice qué falta', (tester) async {
    await abrir(tester);
    expect(tester.widget<FilledButton>(find.byType(FilledButton)).onPressed, isNull);
    expect(find.textContaining('sin los tres no es una constancia'), findsOneWidget);

    // Con motivo y testigo sigue faltando la foto, que es lo que la sostiene.
    await tester.enterText(find.byType(TextField), 'Dijo que no firmaba sin su supervisor');
    await tester.pump();
    await tester.tap(find.widgetWithText(ChoiceChip, 'Marta Ríos'));
    await tester.pump();

    expect(tester.widget<FilledButton>(find.byType(FilledButton)).onPressed, isNull);
  });
}
