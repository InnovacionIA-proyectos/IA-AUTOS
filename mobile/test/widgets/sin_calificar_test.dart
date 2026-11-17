import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/core/tema/tema.dart';
import 'package:traza_autos/features/auth/domain/sesion.dart';
import 'package:traza_autos/features/auth/presentation/auth_controller.dart';
import 'package:traza_autos/features/catalogos/providers.dart';
import 'package:traza_autos/features/inspecciones/domain/novedad_pendiente.dart';
import 'package:traza_autos/features/inspecciones/providers.dart';
import 'package:traza_autos/features/inspecciones/presentation/sin_calificar_page.dart';
import 'package:traza_autos/features/viajes/domain/viaje.dart';
import 'package:traza_autos/features/viajes/presentation/mis_viajes_page.dart';
import 'package:traza_autos/features/viajes/providers.dart';

import '../support/arnes.dart';
import '../support/dobles_catalogos.dart';
import '../support/dobles_viajes.dart';

/// S7-HU-03.2: un daño que el conductor registró y nadie calificó sigue a la vista, y el inspector del viaje
/// los tiene todos juntos en una lista, lo más viejo primero.

const _conductor = Sesion(
  token: 'jwt',
  usuario: Usuario(id: 'conductor-1', nombre: 'Luis Gómez', email: '', rol: 'conductor'),
);

NovedadPendiente novedad(String id, {String vin = 'VIN00000000000001'}) => NovedadPendiente(
      id: id,
      vin: vin,
      proceso: 'transporte',
      momento: 'llegada',
      clase: 'golpe',
      tamano: 'mas_20cm',
      foto: const FotoNovedad(tomadaEn: null, sinUbicacion: true),
    );

/// El viaje del que cuelga todo. `inspectorId` decide quién ve la lista.
Viaje viaje({String? inspectorId = 'u1'}) => Viaje(
      id: 'v1',
      codigoMtrack: 'MTK-1',
      placa: 'ABC123',
      estado: 'en_curso',
      inspectorId: inspectorId,
      vehiculos: const [ViajeVehiculo(vin: 'VIN00000000000001')],
    );

Future<void> avanzar(WidgetTester tester) async {
  for (var i = 0; i < 12; i++) {
    await tester.pump(const Duration(milliseconds: 50));
  }
}

Future<Arnes> abrir(
  WidgetTester tester,
  Widget pantalla, {
  Sesion? sesion,
  List<NovedadPendiente> pendientes = const [],
  Viaje? conViaje,
}) async {
  await tester.binding.setSurfaceSize(const Size(600, 2400));
  addTearDown(() => tester.binding.setSurfaceSize(null));
  final arnes = Arnes(sesion: sesion ?? sesionPrueba)..novedades.delViaje = pendientes;
  await tester.pumpWidget(
    ProviderScope(
      retry: (_, _) => null,
      overrides: [
        ...arnes.overrides,
        catalogosRepositorioProvider.overrideWithValue(CatalogosRepositorioFalso()..guardado = catalogosCon('Golpe')),
        viajesRepositorioProvider.overrideWithValue(ViajesRepositorioFalso()..remotos = [conViaje ?? viaje()]),
        reclamosRepositorioProvider.overrideWithValue(ReclamosRepositorioFalso()),
      ],
      child: MaterialApp(
        theme: crearTema(),
        home: Consumer(
          builder: (_, ref, _) =>
              ref.watch(authControllerProvider.select((e) => e.fase)) == FaseAuth.autenticado ? pantalla : const SizedBox(),
        ),
      ),
    ),
  );
  await avanzar(tester);
  return arnes;
}

void main() {
  group('la lista del viaje', () {
    testWidgets('trae los daños sin calificar con su VIN y la cuenta de lo que falta', (tester) async {
      await abrir(
        tester,
        const SinCalificarPage(viajeId: 'v1', codigoMtrack: 'MTK-1'),
        pendientes: [novedad('h1'), novedad('h2', vin: 'VIN00000000000002')],
      );

      expect(find.text('Sin calificar · MTK-1'), findsOneWidget);
      expect(find.textContaining('Faltan calificar 2 daños'), findsOneWidget);
      expect(find.text('VIN00000000000001'), findsOneWidget);
      expect(find.text('VIN00000000000002'), findsOneWidget);
      expect(find.widgetWithText(FilledButton, 'Calificar'), findsNWidgets(2));
    });

    testWidgets('respeta el orden en que llegan: lo más viejo primero', (tester) async {
      await abrir(
        tester,
        const SinCalificarPage(viajeId: 'v1', codigoMtrack: 'MTK-1'),
        pendientes: [novedad('h-viejo', vin: 'VIN00000000000001'), novedad('h-nuevo', vin: 'VIN00000000000002')],
      );

      final primero = tester.getTopLeft(find.text('VIN00000000000001')).dy;
      final segundo = tester.getTopLeft(find.text('VIN00000000000002')).dy;
      expect(primero, lessThan(segundo));
    });

    testWidgets('sin nada pendiente lo dice, en vez de una lista vacía', (tester) async {
      await abrir(tester, const SinCalificarPage(viajeId: 'v1', codigoMtrack: 'MTK-1'));

      expect(find.text('Nada sin calificar'), findsOneWidget);
      expect(find.widgetWithText(FilledButton, 'Calificar'), findsNothing);
    });

    // CA-3: al calificar uno, desaparece de la lista sin rearmar la pantalla. Lo que se comprueba es la recarga:
    // la segunda consulta trae uno menos y la fila se va.
    testWidgets('calificado uno, la lista se vuelve a pedir y ya no lo trae', (tester) async {
      final arnes = await abrir(
        tester,
        const SinCalificarPage(viajeId: 'v1', codigoMtrack: 'MTK-1'),
        pendientes: [novedad('h1'), novedad('h2', vin: 'VIN00000000000002')],
      );
      expect(arnes.novedades.viajesConsultados, hasLength(1));

      // La revisión la hace otra pantalla; aquí se simula su resultado: el servidor ya tiene uno menos.
      arnes.novedades.delViaje = [novedad('h2', vin: 'VIN00000000000002')];
      final contenedor = ProviderScope.containerOf(tester.element(find.byType(SinCalificarPage)));
      contenedor.invalidate(sinCalificarDelViajeProvider('v1'));
      await avanzar(tester);

      expect(arnes.novedades.viajesConsultados, hasLength(2), reason: 'se volvió a preguntar, no se adivinó');
      expect(find.text('VIN00000000000001'), findsNothing);
      expect(find.text('VIN00000000000002'), findsOneWidget);
      expect(find.textContaining('Falta calificar 1 daño'), findsOneWidget);
    });

    testWidgets('se consulta por el viaje, no por una unidad', (tester) async {
      final arnes = await abrir(
        tester,
        const SinCalificarPage(viajeId: 'v1', codigoMtrack: 'MTK-1'),
        pendientes: [novedad('h1')],
      );

      expect(arnes.novedades.viajesConsultados, ['v1']);
    });
  });

  /// CA-4: la lista es del inspector del viaje. A los demás el servidor les responde 403, así que ni se ofrece.
  group('el botón en Mis viajes', () {
    testWidgets('se le ofrece al inspector del viaje, con cuántos faltan', (tester) async {
      await abrir(tester, const MisViajesPage(), pendientes: [novedad('h1'), novedad('h2')]);

      expect(find.widgetWithText(OutlinedButton, 'Sin calificar (2)'), findsOneWidget);
    });

    testWidgets('con uno solo lo dice en singular', (tester) async {
      await abrir(tester, const MisViajesPage(), pendientes: [novedad('h1')]);

      expect(find.widgetWithText(OutlinedButton, 'Sin calificar (1)'), findsOneWidget);
    });

    testWidgets('al conductor del viaje no: él los registra, no los califica', (tester) async {
      await abrir(tester, const MisViajesPage(), sesion: _conductor, pendientes: [novedad('h1')]);

      expect(find.textContaining('Sin calificar'), findsNothing);
    });

    testWidgets('a un inspector que no es el del viaje, tampoco', (tester) async {
      await abrir(
        tester,
        const MisViajesPage(),
        pendientes: [novedad('h1')],
        conViaje: viaje(inspectorId: 'otro-inspector'),
      );

      expect(find.textContaining('Sin calificar'), findsNothing);
    });

    testWidgets('sin nada pendiente no se ofrece una pantalla vacía', (tester) async {
      await abrir(tester, const MisViajesPage());

      expect(find.textContaining('Sin calificar'), findsNothing);
    });
  });
}
