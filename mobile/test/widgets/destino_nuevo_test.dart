import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:traza_autos/core/tema/tema.dart';
import 'package:traza_autos/features/auth/domain/sesion.dart';
import 'package:traza_autos/features/auth/presentation/auth_controller.dart';
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
}
