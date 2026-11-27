import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:traza_autos/core/tema/tema.dart';
import 'package:traza_autos/features/auth/domain/sesion.dart';
import 'package:traza_autos/features/auth/presentation/auth_controller.dart';
import 'package:traza_autos/features/sync/domain/job.dart';
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

  // S8-HU-04.2: la entrega por parada desde el viaje.
  group('las paradas del viaje', () {
    const vinSur = 'VINCCCCCCCCCCCCC3';
    ViajesRepositorioFalso dosParadas() => ViajesRepositorioFalso()
      ..remotos = [
        viaje(
          'v1',
          vines: [_vin, 'VINBBBBBBBBBBBBB2', vinSur],
          destinos: {_vin: 'Patio Guayabal', 'VINBBBBBBBBBBBBB2': 'Patio Guayabal', vinSur: 'Concesionario Sur'},
        ),
      ];

    // CA-1: el botón de antes («Entrega por punto») ya no sale; el conductor entra por las paradas.
    testWidgets('el conductor entra a las paradas, y ya no a «Entrega por punto»', (tester) async {
      await abrir(tester, dosParadas());

      expect(find.widgetWithText(OutlinedButton, 'Paradas y entregas'), findsOneWidget);
      expect(find.text('Entrega por punto'), findsNothing);
    });

    // CA-9: «Llegué a esta parada» sale de la copia del viaje, una por parada, y sin señal queda en la cola con
    // la hora y el GPS de ese momento, una llegada por carro.
    testWidgets('«Llegué a esta parada» deja en la cola la llegada de cada carro de esa parada', (tester) async {
      final arnes = Arnes(sesion: _luis)..red.estable = false;
      await abrir(tester, dosParadas(), arnes: arnes);

      expect(find.text('Parada: Patio Guayabal'), findsOneWidget);
      expect(find.text('Parada: Concesionario Sur'), findsOneWidget);
      await tester.tap(find.widgetWithText(FilledButton, 'Llegué a esta parada').first);
      await avanzar(tester);
      await tester.tap(find.text('Confirmar'));
      await avanzar(tester);

      final llegadas = arnes.cola.jobs.whereType<LlegadaJob>().toList();
      expect(llegadas.map((l) => l.vin), [_vin, 'VINBBBBBBBBBBBBB2'], reason: 'solo los de esa parada');
      expect(llegadas.map((l) => (l.fecha, l.lat, l.lng)).toSet(), hasLength(1), reason: 'la misma hora y el mismo lugar');
      expect(find.textContaining('2 carros quedan en cola'), findsOneWidget);
      await tester.tap(find.text('OK'));
      await avanzar(tester);

      expect(find.text('La llegada a esta parada está guardada: se enviará cuando haya señal.'), findsOneWidget);
      expect(find.widgetWithText(FilledButton, 'Llegué a esta parada'), findsOneWidget, reason: 'queda la otra parada');
    });

    testWidgets('la parada donde ya llegaron todos no pide «Llegué» otra vez', (tester) async {
      final viajes = ViajesRepositorioFalso()
        ..remotos = [
          Viaje(
            id: 'v1',
            codigoMtrack: 'MTK-1',
            placa: 'ABC123',
            estado: 'en_curso',
            vehiculos: const [
              ViajeVehiculo(vin: _vin, lugarDestino: 'Patio Guayabal', estadoTransporte: EstadoTransporte.sinNovedad),
            ],
          ),
        ];
      await abrir(tester, viajes);

      expect(find.widgetWithText(FilledButton, 'Llegué a esta parada'), findsNothing);
    });

    // S8-HU-07.3 CA-5: un carro cuya ruta el servidor todavía no deja registrar no va en «Llegué a esta parada».
    Viaje conUnoSinRuta({bool todos = false}) => Viaje(
          id: 'v1',
          codigoMtrack: 'MTK-1',
          placa: 'ABC123',
          estado: 'en_curso',
          vehiculos: [
            const ViajeVehiculo(
              vin: _vin,
              lugarDestino: 'Patio Guayabal',
              puedeRegistrarRuta: false,
              motivoSinRuta: 'entrega_del_cargue_sin_cerrar',
            ),
            ViajeVehiculo(
              vin: 'VINBBBBBBBBBBBBB2',
              lugarDestino: 'Patio Guayabal',
              puedeRegistrarRuta: !todos,
              motivoSinRuta: todos ? 'entrega_del_cargue_sin_cerrar' : null,
            ),
          ],
        );

    testWidgets('el carro con la entrega del cargue sin cerrar no entra en la llegada de la parada y se dice por qué', (tester) async {
      final arnes = Arnes(sesion: _luis)..red.estable = false;
      await abrir(tester, ViajesRepositorioFalso()..remotos = [conUnoSinRuta()], arnes: arnes);

      expect(find.text('Falta cerrar la entrega del cargue: $_vin'), findsOneWidget);
      await tester.tap(find.widgetWithText(FilledButton, 'Llegué a esta parada'));
      await avanzar(tester);
      await tester.tap(find.text('Confirmar'));
      await avanzar(tester);

      expect(arnes.cola.jobs.whereType<LlegadaJob>().map((l) => l.vin), ['VINBBBBBBBBBBBBB2']);
    });

    testWidgets('si ninguno se puede registrar todavía, el botón de la parada se ve gris', (tester) async {
      await abrir(tester, ViajesRepositorioFalso()..remotos = [conUnoSinRuta(todos: true)]);

      final boton = tester.widget<FilledButton>(find.widgetWithText(FilledButton, 'Llegué a esta parada'));
      expect(boton.onPressed, isNull);
      expect(find.text('Falta cerrar la entrega del cargue: $_vin, VINBBBBBBBBBBBBB2'), findsOneWidget);
    });

    // CA-1: el coordinador, el admin y el super admin no ven la entrega de ningún viaje.
    testWidgets('el coordinador no ve ni las paradas ni «Llegué a esta parada»', (tester) async {
      await abrir(
        tester,
        dosParadas(),
        arnes: Arnes(
          sesion: const Sesion(
            token: 'jwt',
            usuario: Usuario(id: 'coord-1', nombre: 'Sara Calderón', email: '', rol: 'coordinador_calidad'),
          ),
        ),
      );

      expect(find.widgetWithText(OutlinedButton, 'Paradas y entregas'), findsNothing);
      expect(find.widgetWithText(FilledButton, 'Llegué a esta parada'), findsNothing);
      expect(find.textContaining('Entregar en esta parada'), findsNothing);
    });
  });
  // S8-HU-10.2 CA-3 y CA-9: la tarjeta dice el papel que manda el servidor, y sin él no dice nada.
  group('el papel del inspector en la tarjeta', () {
    testWidgets('«Tú recibes» para quien solo tomó la entrega de una parada', (tester) async {
      await abrir(
        tester,
        ViajesRepositorioFalso()..remotos = [viaje('v1', codigo: 'MTK-0417', vines: [_vin], papel: 'recibe')],
        arnes: Arnes(sesion: sesionPrueba),
      );

      expect(find.text('Tú recibes'), findsOneWidget);
    });

    testWidgets('«Tú cargas» para quien carga', (tester) async {
      await abrir(
        tester,
        ViajesRepositorioFalso()..remotos = [viaje('v1', vines: [_vin], papel: 'carga')],
        arnes: Arnes(sesion: sesionPrueba),
      );

      expect(find.text('Tú cargas'), findsOneWidget);
    });

    testWidgets('el conductor no ve ninguna etiqueta, y un valor desconocido tampoco se muestra', (tester) async {
      await abrir(tester, ViajesRepositorioFalso()..remotos = [viaje('v1', vines: [_vin]), viaje('v2', vines: ['VIN2'], papel: 'supervisa')]);

      expect(find.textContaining('Tú '), findsNothing);
      expect(find.text('supervisa'), findsNothing);
    });

    // CA-6: sin señal la tarjeta sigue diciendo el papel, desde la copia guardada.
    testWidgets('sin señal se ve el papel de la copia guardada', (tester) async {
      final viajes = ViajesRepositorioFalso()
        ..guardados = [Viaje.fromJson(viaje('v1', vines: [_vin], papel: 'recibe').toJson())]
        ..error = errorApi(0, 'Sin conexión');
      await abrir(tester, viajes, arnes: Arnes(sesion: sesionPrueba));

      expect(find.text('Tú recibes'), findsOneWidget);
    });
  });

  // S8-HU-01.3 CA-10: el informe de la niñera también lo tiene quien ve el viaje y no abre la pantalla de la niñera.
  group('el informe de la niñera en la tarjeta del viaje', () {
    const otroInspector =
        Sesion(token: 'jwt', usuario: Usuario(id: 'pedro', nombre: 'Pedro Salazar', email: '', rol: 'inspector'));

    ViajesRepositorioFalso delViaje() => ViajesRepositorioFalso()
      ..remotos = [viaje('v1', vines: [_vin], inspectorId: 'ana', conductorId: 'luis')];

    testWidgets('el conductor del viaje lo tiene dentro de la niñera, no repetido en la tarjeta', (tester) async {
      await abrir(tester, delViaje());

      expect(find.text('Niñera y firma del cargue'), findsOneWidget);
      expect(find.text('Informe de la niñera'), findsNothing);
    });

    testWidgets('quien no firma la niñera lo descarga desde la tarjeta', (tester) async {
      final viajes = delViaje();
      final arnes = await abrir(tester, viajes, arnes: Arnes(sesion: otroInspector));

      expect(find.text('Niñera y firma del cargue'), findsNothing);
      await tester.tap(find.text('Informe de la niñera'));
      await avanzar(tester);

      expect(viajes.pdfsPedidos, 1);
      expect(arnes.pdfs.abiertos, ['ninera-MTK-1.pdf']);
    });
  });
}
