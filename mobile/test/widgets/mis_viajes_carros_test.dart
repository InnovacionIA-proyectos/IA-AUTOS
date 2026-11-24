import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:traza_autos/core/tema/tema.dart';
import 'package:traza_autos/features/auth/domain/sesion.dart';
import 'package:traza_autos/features/auth/presentation/auth_controller.dart';
import 'package:traza_autos/features/viajes/domain/viaje.dart';
import 'package:traza_autos/features/viajes/presentation/destinos_vistos.dart';
import 'package:traza_autos/features/viajes/presentation/mis_viajes_page.dart';
import 'package:traza_autos/features/viajes/providers.dart';

import '../support/arnes.dart';
import '../support/dobles_viajes.dart';

/// S8-HU-03.2 CA-4 y CA-5: el muelle en Mis viajes, y el destino de cada carro resaltado para el conductor cuando
/// cambió desde la última vez que abrió ese carro.

const _luis = Sesion(token: 'jwt', usuario: Usuario(id: 'luis', nombre: 'Luis Gómez', email: '', rol: 'conductor'));
const _vin = 'VINAAAAAAAAAAAAA1';
final _cambio = DateTime.utc(2026, 11, 24, 15);

Future<void> avanzar(WidgetTester tester) async {
  for (var i = 0; i < 12; i++) {
    await tester.pump(const Duration(milliseconds: 50));
  }
}

Future<Arnes> abrir(WidgetTester tester, ViajesRepositorioFalso viajes, {Arnes? arnes}) async {
  await tester.binding.setSurfaceSize(const Size(600, 1600));
  addTearDown(() => tester.binding.setSurfaceSize(null));
  final a = arnes ?? Arnes(sesion: _luis);
  await tester.pumpWidget(
    ProviderScope(
      retry: (_, _) => null,
      overrides: [...a.overrides, viajesRepositorioProvider.overrideWithValue(viajes)],
      child: MaterialApp.router(
        theme: crearTema(),
        routerConfig: GoRouter(
          routes: [
            GoRoute(
              path: '/',
              builder: (_, _) => Consumer(
                builder: (_, ref, _) => ref.watch(authControllerProvider.select((e) => e.fase)) == FaseAuth.autenticado
                    ? const MisViajesPage()
                    : const SizedBox(),
              ),
            ),
            GoRoute(
              path: '/inspecciones/vin/:vin',
              builder: (_, estado) => Scaffold(appBar: AppBar(), body: Text('Ficha ${estado.pathParameters['vin']}')),
            ),
          ],
        ),
      ),
    ),
  );
  await avanzar(tester);
  return a;
}

ViajesRepositorioFalso _conDestino({DateTime? cambiado}) => ViajesRepositorioFalso()
  ..remotos = [
    viaje(
      'v1',
      vines: [_vin],
      muelle: 'Muelle 3',
      trailer: 'TRX-202',
      destinos: {_vin: 'Patio Guayabal'},
      destinosCambiados: {_vin: ?cambiado},
    ),
  ];

void main() {
  testWidgets('el viaje muestra su muelle junto al tráiler, y cada carro a dónde va', (tester) async {
    await abrir(tester, _conDestino());

    expect(find.textContaining('TRX-202 · Muelle 3'), findsOneWidget);
    expect(find.text('Va a Patio Guayabal'), findsOneWidget);
    expect(find.text('Destino nuevo'), findsNothing, reason: 'nunca cambió');
  });

  testWidgets('al conductor se le resalta el destino que cambió hasta que abre ese carro', (tester) async {
    final arnes = await abrir(tester, _conDestino(cambiado: _cambio));

    expect(find.text('Destino nuevo'), findsOneWidget);

    await tester.tap(find.text(_vin));
    await avanzar(tester);
    expect(find.text('Ficha $_vin'), findsOneWidget);
    await tester.pageBack();
    await avanzar(tester);

    expect(find.text('Destino nuevo'), findsNothing);
    expect(find.text('Va a Patio Guayabal'), findsOneWidget);
    expect(arnes.destinosVistos.guardados.values.single, contains('v1|$_vin'), reason: 'queda guardado en el teléfono');
  });

  testWidgets('si el destino vuelve a cambiar después de abrirlo, se resalta otra vez', (tester) async {
    final arnes = Arnes(sesion: _luis);
    arnes.destinosVistos.guardados[DestinosVistos.clave('luis')] =
        '{"v1|$_vin":"${_cambio.subtract(const Duration(hours: 2)).toIso8601String()}"}';
    await abrir(tester, _conDestino(cambiado: _cambio), arnes: arnes);

    expect(find.text('Destino nuevo'), findsOneWidget);
  });

  testWidgets('lo que ya vio no se resalta al volver a entrar', (tester) async {
    final arnes = Arnes(sesion: _luis);
    arnes.destinosVistos.guardados[DestinosVistos.clave('luis')] = '{"v1|$_vin":"${_cambio.toIso8601String()}"}';
    await abrir(tester, _conDestino(cambiado: _cambio), arnes: arnes);

    expect(find.text('Va a Patio Guayabal'), findsOneWidget);
    expect(find.text('Destino nuevo'), findsNothing);
  });

  testWidgets('el inspector ve el destino, pero el resaltado es del conductor', (tester) async {
    await abrir(tester, _conDestino(cambiado: _cambio), arnes: Arnes());

    expect(find.text('Va a Patio Guayabal'), findsOneWidget);
    expect(find.text('Destino nuevo'), findsNothing);
  });

  // S8-HU-02.2 CA-3 y CA-6: el conductor ve por qué no sale cada carro, y un carro bajado ya no está en el viaje.
  group('el carro que no sale o se bajó', () {
    ViajesRepositorioFalso conCarros() => ViajesRepositorioFalso()
      ..remotos = [
        viaje(
          'v1',
          vines: [_vin, 'VINBBBBBBBBBBBBB2', 'VINCCCCCCCCCCCCC3'],
          estados: {'VINBBBBBBBBBBBBB2': 'no_sale', 'VINCCCCCCCCCCCCC3': 'bajado'},
          noSales: {
            'VINBBBBBBBBBBBBB2': const NoSale(
              motivo: 'Faltan documentos',
              motivoCodigo: 'faltan_documentos',
              vinReemplazo: 'VINDDDDDDDDDDDDD4',
            ),
          },
        ),
      ];

    testWidgets('el conductor ve el motivo con su etiqueta y quién lo reemplaza', (tester) async {
      await abrir(tester, conCarros());

      expect(find.text('Este carro no sale: Faltan documentos · la reemplaza VINDDDDDDDDDDDDD4'), findsOneWidget);
    });

    testWidgets('un carro bajado no aparece en el viaje', (tester) async {
      await abrir(tester, conCarros());

      expect(find.text(_vin), findsOneWidget);
      expect(find.text('VINBBBBBBBBBBBBB2'), findsOneWidget, reason: 'el que no sale sigue en la lista');
      expect(find.text('VINCCCCCCCCCCCCC3'), findsNothing);
    });

    testWidgets('tampoco desde la copia guardada sin señal', (tester) async {
      final viajes = conCarros();
      viajes.guardados = viajes.remotos;
      viajes.error = Exception('sin red');
      await abrir(tester, viajes);

      expect(find.text(_vin), findsOneWidget);
      expect(find.text('VINCCCCCCCCCCCCC3'), findsNothing);
    });
  });

  // S8-HU-01.3: la niñera (puestos y firma) se abre desde el viaje, para el inspector y el conductor del viaje.
  group('la niñera del viaje', () {
    ViajesRepositorioFalso conFirma({bool cerrado = false}) => ViajesRepositorioFalso()
      ..remotos = [viaje('v1', vines: [_vin], inspectorId: 'u1', conductorId: 'luis', cargueCerrado: cerrado)];

    testWidgets('el conductor y el inspector del viaje la tienen en la tarjeta', (tester) async {
      await abrir(tester, conFirma());
      expect(find.widgetWithText(OutlinedButton, 'Niñera y firma del cargue'), findsOneWidget, reason: 'conductor');
    });

    testWidgets('el inspector del viaje también', (tester) async {
      await abrir(tester, conFirma(), arnes: Arnes());
      expect(find.widgetWithText(OutlinedButton, 'Niñera y firma del cargue'), findsOneWidget);
    });

    testWidgets('con el cargue cerrado se sigue viendo la niñera, sin la firma', (tester) async {
      await abrir(tester, conFirma(cerrado: true));
      expect(find.widgetWithText(OutlinedButton, 'Niñera y firma del cargue'), findsNothing);
      expect(find.widgetWithText(OutlinedButton, 'Niñera'), findsOneWidget);
    });

    testWidgets('a quien no es del viaje no se le ofrece', (tester) async {
      await abrir(tester, ViajesRepositorioFalso()..remotos = [viaje('v1', vines: [_vin], inspectorId: 'otro', conductorId: 'otro2')]);
      expect(find.widgetWithText(OutlinedButton, 'Niñera y firma del cargue'), findsNothing);
      expect(find.widgetWithText(OutlinedButton, 'Niñera'), findsNothing);
    });
  });
}
