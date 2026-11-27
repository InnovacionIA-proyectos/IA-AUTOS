import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:traza_autos/core/dispositivo/abridor_pdf.dart';
import 'package:traza_autos/core/errores/error_api.dart';
import 'package:traza_autos/core/tema/tema.dart';
import 'package:traza_autos/features/auth/domain/sesion.dart';
import 'package:traza_autos/features/auth/presentation/auth_controller.dart';
import 'package:traza_autos/features/inspecciones/domain/puestos_guardados.dart';
import 'package:traza_autos/features/inspecciones/presentation/ninera_page.dart';
import 'package:traza_autos/features/vehiculos/data/vehiculos_repositorio.dart';
import 'package:traza_autos/features/vehiculos/domain/inventario.dart';
import 'package:traza_autos/features/vehiculos/providers.dart';
import 'package:traza_autos/features/viajes/domain/viaje.dart';
import 'package:traza_autos/features/viajes/providers.dart';

import '../support/arnes.dart';
import '../support/dobles_custodia.dart';
import '../support/dobles_viajes.dart';

/// S8-HU-01.3 CA-1, CA-2 y CA-4: la pantalla de la niñera, con sus puestos, los carros y el carro de cada puesto.

const _vinA = 'VINA0000000000001';
const _vinB = 'VINB0000000000002';
const _vinC = 'VINC0000000000003';
final _clave = clavePuestos(usuarioId: ana.id, viajeId: 'v1');

/// La salida del cargue de cada carro, abierta salvo la de los VIN en [cerradas].
class _Inventarios implements VehiculosRepositorio {
  _Inventarios({this.cerradas = const {}});

  final Set<String> cerradas;

  @override
  Future<List<VehiculoBusqueda>> buscar(String termino) async => const [];

  @override
  Future<Inventario> inventario(String vin) async => Inventario.fromJson({
        'id': vin,
        'vin': vin,
        'marca': 'Renault',
        'operacion': 'nacional',
        'etapas': [
          {
            'id': 'e-$vin',
            'orden': 1,
            'proceso': 'cargue',
            'requiere_inspeccion': true,
            'viaje_id': 'v1',
            'inspecciones': [
              if (cerradas.contains(vin))
                {
                  'id': '$vin-salida',
                  'momento': 'salida',
                  'momento_nombre': 'salida',
                  'fecha': '2026-11-23T11:00:00Z',
                  'cerrada_at': '2026-11-23T11:30:00Z',
                  'estado': 'cerrada',
                  'hallazgos': [],
                  'evidencias': [],
                },
            ],
          },
        ],
      });
}

Viaje _viaje({Map<String, int> posiciones = const {_vinB: 2}}) => viaje(
      'v1',
      vines: [_vinA, _vinB, _vinC],
      posiciones: posiciones,
      inspectorId: ana.id,
      conductorId: luis.id,
    );

Future<void> avanzar(WidgetTester tester) async {
  for (var i = 0; i < 12; i++) {
    await tester.pump(const Duration(milliseconds: 50));
  }
}

Future<Arnes> abrir(
  WidgetTester tester, {
  Sesion sesion = const Sesion(token: 'jwt', usuario: ana),
  Arnes? arnes,
  Viaje? delViaje,
  Set<String> cerradas = const {},
  ViajesRepositorioFalso? viajes,
}) async {
  await tester.binding.setSurfaceSize(const Size(600, 2200));
  addTearDown(() => tester.binding.setSurfaceSize(null));
  final usado = arnes ?? Arnes(sesion: sesion);
  await tester.pumpWidget(
    ProviderScope(
      retry: (_, _) => null,
      overrides: [
        ...usado.overrides,
        viajesRepositorioProvider.overrideWithValue((viajes ?? ViajesRepositorioFalso())..viajeDeEtapa = delViaje ?? _viaje()),
        vehiculosRepositorioProvider.overrideWithValue(_Inventarios(cerradas: cerradas)),
      ],
      child: MaterialApp.router(
        theme: crearTema(),
        routerConfig: GoRouter(
          routes: [
            GoRoute(
              path: '/',
              builder: (_, _) => Consumer(
                builder: (_, ref, _) => ref.watch(authControllerProvider.select((e) => e.fase)) == FaseAuth.autenticado
                    ? const NineraPage(viajeId: 'v1')
                    : const SizedBox(),
              ),
            ),
            GoRoute(
              path: '/inspecciones/vin/:vin',
              builder: (_, estado) => Scaffold(body: Text('Ficha ${estado.pathParameters['vin']}')),
              routes: [
                GoRoute(
                  path: 'captura/:etapaId',
                  builder: (_, estado) => Scaffold(
                    body: Text('Captura ${estado.pathParameters['etapaId']} ${estado.uri.queryParameters['momento']}'),
                  ),
                ),
              ],
            ),
          ],
        ),
      ),
    ),
  );
  await avanzar(tester);
  return usado;
}

/// El puesto del esquema, por su número.
Finder puesto(String numero) => find.descendant(of: find.byType(InkWell), matching: find.text(numero)).first;

void main() {
  testWidgets('dibuja los puestos con el carro de cada uno y dice cuáles faltan', (tester) async {
    await abrir(tester);

    expect(find.text('Puesto 2'), findsOneWidget, reason: 'B ya tiene el 2 en el servidor');
    expect(find.text('Faltan 2 carros sin puesto'), findsOneWidget);
    expect(find.text('Firma de la niñera'), findsOneWidget);
  });

  // CA-1 y CA-4.
  testWidgets('el inspector pone un carro en un puesto libre y queda guardado en el teléfono', (tester) async {
    final arnes = await abrir(tester);

    await tester.tap(puesto('1'));
    await avanzar(tester);
    expect(find.text(_vinB), findsOneWidget, reason: 'en la lista de carros, no en la de libres');
    await tester.tap(find.descendant(of: find.byType(ListTile), matching: find.text(_vinA)));
    await avanzar(tester);

    expect(PuestosGuardados.fromJson(arnes.puestos.guardados[_clave]!).cambios, {_vinA: 1});
    expect(find.text('Puesto 1'), findsOneWidget);
    expect(find.text('Falta el puesto de $_vinC'), findsOneWidget);
  });

  testWidgets('desde un puesto ocupado se quita el carro', (tester) async {
    final arnes = await abrir(tester);

    await tester.tap(puesto('2'));
    await avanzar(tester);
    await tester.tap(find.text('Quitar de este puesto'));
    await avanzar(tester);

    expect(PuestosGuardados.fromJson(arnes.puestos.guardados[_clave]!).cambios, {_vinB: null});
    expect(find.text('Faltan 3 carros sin puesto'), findsOneWidget);
  });

  // CA-2: tocar un puesto ocupado abre la salida del cargue de ESE carro.
  testWidgets('abrir el carro de un puesto lleva a la salida del cargue de ese VIN', (tester) async {
    await abrir(tester);

    await tester.tap(puesto('2'));
    await avanzar(tester);
    await tester.tap(find.text('Abrir $_vinB'));
    await avanzar(tester);

    expect(find.text('Captura e-$_vinB salida'), findsOneWidget);
  });

  testWidgets('si su salida ya se cerró, abre la ficha del carro', (tester) async {
    await abrir(tester, cerradas: {_vinB});

    await tester.tap(puesto('2'));
    await avanzar(tester);
    await tester.tap(find.text('Abrir $_vinB'));
    await avanzar(tester);

    expect(find.text('Ficha $_vinB'), findsOneWidget);
  });

  testWidgets('lo guardado en el teléfono se ve al volver', (tester) async {
    final arnes = Arnes(sesion: const Sesion(token: 'jwt', usuario: ana));
    arnes.puestos.guardados[_clave] = const PuestosGuardados(cambios: {_vinA: 1, _vinC: 3}, base: {_vinA: null, _vinC: null}).toJson();
    await abrir(tester, arnes: arnes);

    expect(find.text('Puesto 1'), findsOneWidget);
    expect(find.text('Puesto 3'), findsOneWidget);
    expect(find.textContaining('sin puesto'), findsNothing);
  });

  testWidgets('el conductor mira y abre, pero no acomoda', (tester) async {
    final arnes = await abrir(tester, sesion: const Sesion(token: 'jwt', usuario: luis));

    await tester.tap(puesto('1'));
    await avanzar(tester);
    expect(find.byType(ListTile), findsNothing, reason: 'un puesto libre no ofrece nada');

    await tester.tap(puesto('2'));
    await avanzar(tester);
    expect(find.text('Captura e-$_vinB salida'), findsOneWidget, reason: 'el ocupado abre el carro de una vez');
    expect(arnes.puestos.guardados, isEmpty);
  });

  // CA-10: el informe de la niñera se descarga y se abre; si falla, dice por qué y deja reintentar.
  group('el informe de la niñera', () {
    testWidgets('se descarga y se abre con su nombre', (tester) async {
      final viajes = ViajesRepositorioFalso();
      final arnes = await abrir(tester, viajes: viajes);

      await tester.tap(find.text('Informe de la niñera'));
      await avanzar(tester);

      expect(viajes.pdfsPedidos, 1);
      expect(arnes.pdfs.abiertos, ['ninera-MTK-1.pdf']);
    });

    testWidgets('sin señal lo dice y reintentar lo abre', (tester) async {
      final viajes = ViajesRepositorioFalso()..errorPdf = const ErrorApi.red();
      final arnes = await abrir(tester, viajes: viajes);

      await tester.tap(find.text('Informe de la niñera'));
      await avanzar(tester);
      expect(find.text('No se pudo abrir el informe de la niñera'), findsOneWidget);
      expect(find.text('No hay señal o el servidor no responde.'), findsOneWidget);

      viajes.errorPdf = null;
      await tester.tap(find.widgetWithText(TextButton, 'Reintentar'));
      await avanzar(tester);

      expect(arnes.pdfs.abiertos, ['ninera-MTK-1.pdf']);
      expect(find.text('No se pudo abrir el informe de la niñera'), findsNothing);
    });

    testWidgets('si el teléfono no tiene con qué abrirlo, lo dice', (tester) async {
      final arnes = Arnes(sesion: const Sesion(token: 'jwt', usuario: ana));
      arnes.pdfs.error = const PdfNoAbierto('Este teléfono no tiene una app para abrir PDF.');
      await abrir(tester, arnes: arnes);

      await tester.tap(find.text('Informe de la niñera'));
      await avanzar(tester);

      expect(find.text('Este teléfono no tiene una app para abrir PDF.'), findsOneWidget);
    });

    testWidgets('un rechazo del servidor se muestra con su mensaje', (tester) async {
      final viajes = ViajesRepositorioFalso()..errorPdf = errorApi(403, 'El viaje no es tuyo');
      await abrir(tester, viajes: viajes);

      await tester.tap(find.text('Informe de la niñera'));
      await avanzar(tester);

      expect(find.text('El viaje no es tuyo'), findsOneWidget);
    });
  });
}
