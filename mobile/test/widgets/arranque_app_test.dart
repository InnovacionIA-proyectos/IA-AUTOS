import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/app/app.dart';
import 'package:traza_autos/features/catalogos/providers.dart';

import '../support/arnes.dart';
import '../support/dobles_catalogos.dart';

/// Varios fotogramas en vez de pumpAndSettle: la app muestra indicadores de progreso que no paran solos.
Future<void> abrir(WidgetTester tester, Arnes arnes, CatalogosRepositorioFalso catalogos) async {
  await tester.pumpWidget(
    ProviderScope(
      retry: (_, _) => null,
      overrides: [...arnes.overrides, catalogosRepositorioProvider.overrideWithValue(catalogos)],
      child: const TrazaApp(),
    ),
  );
  for (var i = 0; i < 5; i++) {
    await tester.pump(const Duration(milliseconds: 50));
  }
}

void main() {
  testWidgets('al abrir la app se descargan los catálogos, sin esperar a que alguien abra un VIN', (tester) async {
    final catalogos = CatalogosRepositorioFalso()..remoto = catalogosCon('Golpe');

    await abrir(tester, Arnes(), catalogos);

    // Si nadie observa el controlador, los catálogos solo se bajarían al construir vin_page o captura_page, que
    // puede ser ya en el patio y sin señal: justo el escenario que la HU debe evitar.
    expect(catalogos.sincronizaciones, 1);
  });

  testWidgets('sin sesión no se descargan (la API respondería 401)', (tester) async {
    final catalogos = CatalogosRepositorioFalso()..remoto = catalogosCon('Golpe');

    await abrir(tester, Arnes(sesion: null), catalogos);

    expect(catalogos.sincronizaciones, 0);
  });
}
