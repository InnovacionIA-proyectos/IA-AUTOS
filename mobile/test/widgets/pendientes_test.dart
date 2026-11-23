import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/core/tema/tema.dart';
import 'package:traza_autos/features/auth/domain/sesion.dart';
import 'package:traza_autos/features/auth/presentation/auth_controller.dart';
import 'package:traza_autos/features/viajes/domain/pendiente.dart';
import 'package:traza_autos/features/viajes/presentation/detalle_pendiente_page.dart';
import 'package:traza_autos/features/viajes/presentation/mis_viajes_page.dart';
import 'package:traza_autos/features/viajes/providers.dart';

import '../support/arnes.dart';
import '../support/dobles_viajes.dart';

/// S6-HU-12.4 y S8-HU-10.2: «Por tomar» con lo que el inspector puede tomar en su punto, y lo que tomó en «Mis
/// viajes», desde donde se libera.

Future<void> avanzar(WidgetTester tester) async {
  for (var i = 0; i < 12; i++) {
    await tester.pump(const Duration(milliseconds: 50));
  }
}

Future<ReclamosRepositorioFalso> abrir(
  WidgetTester tester,
  ReclamosRepositorioFalso reclamos, {
  Sesion? sesion = sesionPrueba,
  List<String> vinesDeMisViajes = const [],
  ViajesRepositorioFalso? viajes,
}) async {
  await tester.binding.setSurfaceSize(const Size(600, 2400));
  addTearDown(() => tester.binding.setSurfaceSize(null));
  final repositorio =
      viajes ?? (ViajesRepositorioFalso()..remotos = [if (vinesDeMisViajes.isNotEmpty) viaje('mio', vines: vinesDeMisViajes)]);
  await tester.pumpWidget(
    ProviderScope(
      retry: (_, _) => null,
      overrides: [
        ...Arnes(sesion: sesion).overrides,
        viajesRepositorioProvider.overrideWithValue(repositorio),
        reclamosRepositorioProvider.overrideWithValue(reclamos),
      ],
      child: MaterialApp(theme: crearTema(), home: const _Arranque()),
    ),
  );
  await avanzar(tester);
  return reclamos;
}

/// Carga la sesión como lo hace la app antes de mostrar Mis viajes.
class _Arranque extends ConsumerWidget {
  const _Arranque();

  @override
  Widget build(BuildContext context, WidgetRef ref) =>
      ref.watch(authControllerProvider.select((e) => e.fase)) == FaseAuth.autenticado ? const MisViajesPage() : const SizedBox();
}

double _y(WidgetTester tester, Finder f) => tester.getTopLeft(f).dy;

Future<void> liberarDesdeLaTarjeta(WidgetTester tester, String boton) async {
  await tester.tap(find.widgetWithText(OutlinedButton, boton));
  await avanzar(tester);
  await tester.tap(find.widgetWithText(TextButton, 'Liberar'));
  await avanzar(tester);
}

void main() {
  testWidgets('«Por tomar» ofrece lo disponible, muestra en gris lo de otro y no repite lo propio', (tester) async {
    await abrir(
      tester,
      ReclamosRepositorioFalso()
        ..pendientesActuales = [
          pendienteDe('v1', placa: 'AAA111'),
          pendienteDe('v2', placa: 'BBB222', estado: EstadoPendiente.enRevision, por: 'Andrés'),
          pendienteDe('v3', placa: 'CCC333', estado: EstadoPendiente.mio),
        ],
    );

    expect(find.text('Por tomar'), findsOneWidget);
    expect(find.text('Disponible'), findsOneWidget);
    expect(find.text('Lo tiene Andrés'), findsOneWidget);
    expect(find.text('Reclamado por ti'), findsNothing);
    // Solo lo disponible se toma; lo que tiene otro no ofrece ningún botón.
    expect(find.widgetWithText(FilledButton, 'Tomar'), findsOneWidget);
    expect(find.text('Por cargar (2)'), findsOneWidget, reason: 'lo propio ya no está por tomar');
    // Lo propio está en «Mis viajes», una sola vez, con quién hace qué y cómo soltarlo.
    expect(find.text('CCC333'), findsOneWidget);
    expect(_y(tester, find.text('Mis viajes').last), lessThan(_y(tester, find.text('CCC333'))));
    expect(find.text('Tú cargas'), findsOneWidget);
    expect(find.widgetWithText(OutlinedButton, 'Liberar'), findsOneWidget);
  });

  testWidgets('lo disponible va antes que lo que tiene otro inspector', (tester) async {
    await abrir(
      tester,
      ReclamosRepositorioFalso()
        ..pendientesActuales = [
          pendienteDe('v2', placa: 'BBB222', estado: EstadoPendiente.enRevision, por: 'Andrés'),
          pendienteDe('v1', placa: 'AAA111'),
        ],
    );

    expect(_y(tester, find.text('AAA111')), lessThan(_y(tester, find.text('BBB222'))));
  });

  testWidgets('sin pendientes las dos listas siguen ahí y lo dicen, y los viajes propios siguen debajo', (tester) async {
    await abrir(tester, ReclamosRepositorioFalso(), vinesDeMisViajes: ['VINAAAAAAAAAAAAA1']);

    expect(find.text('Por recibir (0)'), findsOneWidget);
    expect(find.text('Nada por recibir'), findsOneWidget);
    expect(find.text('Por cargar (0)'), findsOneWidget);
    expect(find.text('Nada por cargar'), findsOneWidget);
    expect(find.text('VINAAAAAAAAAAAAA1'), findsOneWidget);
  });

  testWidgets('sin nada tomado ni asignado, «Mis viajes» dice cómo llegan los viajes', (tester) async {
    await abrir(tester, ReclamosRepositorioFalso()..pendientesActuales = [pendienteDe('v1')]);

    expect(find.textContaining('Cuando tomes un camión de «Por tomar»'), findsOneWidget);
  });

  // S8-HU-10.2 ESC-01: entregas en «Por recibir», cargues en «Por cargar», y ninguno en las dos.
  testWidgets('los pendientes se parten en «Por recibir» y «Por cargar», cada uno en una sola', (tester) async {
    await abrir(
      tester,
      ReclamosRepositorioFalso()
        ..pendientesActuales = [
          pendienteDe('v1', placa: 'ENT001', proceso: 'entrega'),
          pendienteDe('v2', placa: 'CAR001'),
          pendienteDe('v3', placa: 'ENT002', proceso: 'entrega'),
          // Un proceso que la app no conoce no se pierde: cae con lo que se carga, como lo lee `Pendiente.fromJson`.
          pendienteDe('v4', placa: 'RAR001', proceso: 'otro'),
        ],
    );

    expect(find.text('Por recibir (2)'), findsOneWidget);
    expect(find.text('Por cargar (2)'), findsOneWidget);
    expect(find.text('Nada por recibir'), findsNothing);
    expect(find.text('Nada por cargar'), findsNothing);
    for (final placa in ['ENT001', 'CAR001', 'ENT002', 'RAR001']) {
      expect(find.text(placa), findsOneWidget, reason: '$placa sale en una sola lista');
    }

    // El orden en pantalla: primero lo que llega, después lo que sale.
    expect(_y(tester, find.text('Por recibir (2)')), lessThan(_y(tester, find.text('ENT001'))));
    expect(_y(tester, find.text('ENT002')), lessThan(_y(tester, find.text('Por cargar (2)'))));
    expect(_y(tester, find.text('Por cargar (2)')), lessThan(_y(tester, find.text('CAR001'))));
    expect(find.widgetWithText(FilledButton, 'Tomar'), findsNWidgets(4));
  });

  // S8-HU-10.2 ESC-02: una lista vacía no desaparece.
  testWidgets('con solo un cargue, «Por recibir» sigue visible y dice que no hay nada', (tester) async {
    await abrir(tester, ReclamosRepositorioFalso()..pendientesActuales = [pendienteDe('v1', placa: 'CAR001')]);

    expect(find.text('Por cargar (1)'), findsOneWidget);
    expect(find.text('CAR001'), findsOneWidget);
    expect(find.text('Por recibir (0)'), findsOneWidget);
    expect(find.text('Nada por recibir'), findsOneWidget);
  });

  testWidgets('sin señal avisa que se consulta con conexión y deja reintentar', (tester) async {
    final reclamos = ReclamosRepositorioFalso()..errorPendientes = Exception('sin red');
    await abrir(tester, reclamos);

    expect(find.textContaining('No se pudo consultar lo que hay por tomar'), findsOneWidget);
    reclamos.errorPendientes = null;
    reclamos.pendientesActuales = [pendienteDe('v1')];
    await tester.tap(find.widgetWithText(TextButton, 'Reintentar'));
    await avanzar(tester);

    expect(find.text('Disponible'), findsOneWidget);
  });

  testWidgets('«Tomar» un cargue lo saca de «Por tomar» y lo deja en «Mis viajes» con sus unidades', (tester) async {
    final viajes = ViajesRepositorioFalso();
    final reclamos = ReclamosRepositorioFalso()
      ..pendientesActuales = [pendienteDe('v1', placa: 'AAA111')]
      // Como el servidor: tomar el cargue hace al inspector inspector del viaje, y el viaje llega con los suyos.
      ..alReclamar = (id, _) => viajes.remotos = [viaje(id, codigo: 'MTK-v1', placa: 'AAA111', vines: ['VINAAAAAAAAAAAAA1'])];
    await abrir(tester, reclamos, viajes: viajes);

    await tester.tap(find.widgetWithText(FilledButton, 'Tomar'));
    await avanzar(tester);

    expect(reclamos.reclamados, [('v1', 'cargue')]);
    expect(find.text('AAA111 quedó en Mis viajes'), findsOneWidget);
    expect(find.byType(DetallePendientePage), findsNothing, reason: 'se queda en la lista: el viaje ya está debajo');
    expect(find.text('Por cargar (0)'), findsOneWidget);
    expect(find.widgetWithText(FilledButton, 'Tomar'), findsNothing);
    expect(find.text('MTK-v1'), findsOneWidget);
    expect(find.text('VINAAAAAAAAAAAAA1'), findsOneWidget);
    expect(find.text('Tú cargas'), findsOneWidget);
    expect(find.widgetWithText(OutlinedButton, 'Liberar'), findsOneWidget);
  });

  testWidgets('si otro inspector se adelantó lo dice y la lista se actualiza sin perder nada', (tester) async {
    final reclamos = ReclamosRepositorioFalso()
      ..pendientesActuales = [pendienteDe('v1', placa: 'AAA111'), pendienteDe('v2', placa: 'BBB222')]
      ..errorReclamar = errorApi(409, 'Ya fue tomado por otro inspector')
      ..despuesDeConsultar = [
        pendienteDe('v1', placa: 'AAA111', estado: EstadoPendiente.enRevision, por: 'Andrés'),
        pendienteDe('v2', placa: 'BBB222'),
      ];
    await abrir(tester, reclamos);

    await tester.tap(find.widgetWithText(FilledButton, 'Tomar').first);
    await avanzar(tester);

    expect(find.text('Ya fue tomado por otro inspector'), findsOneWidget);
    expect(find.text('Lo tiene Andrés'), findsOneWidget);
    expect(find.text('BBB222'), findsOneWidget, reason: 'el otro camión sigue en la lista');
    expect(find.widgetWithText(FilledButton, 'Tomar'), findsOneWidget);
    expect(find.text('Tú cargas'), findsNothing);
  });

  testWidgets('la entrega se toma igual y, mientras el servidor no la mande en «Mis viajes», se ve con lo de la lista',
      (tester) async {
    final reclamos = await abrir(
      tester,
      ReclamosRepositorioFalso()
        ..pendientesActuales = [pendienteDe('v9', placa: 'ENT009', proceso: 'entrega', lugar: 'Concesionario Cali Sur')],
    );

    expect(find.textContaining('Entrega · Concesionario Cali Sur'), findsOneWidget);
    await tester.tap(find.widgetWithText(FilledButton, 'Tomar'));
    await avanzar(tester);

    expect(reclamos.reclamados, [('v9', 'entrega')]);
    expect(find.text('Por recibir (0)'), findsOneWidget);
    expect(find.text('Tú recibes'), findsOneWidget);
    expect(find.text('MTK-v9'), findsOneWidget);
    expect(find.textContaining('Entrega · Concesionario Cali Sur'), findsOneWidget);

    // Sus unidades se ven en el detalle, con el punto de entrega.
    await tester.tap(find.widgetWithText(FilledButton, 'Ver unidades'));
    await avanzar(tester);
    expect(find.text('Entrega por revisar'), findsOneWidget);
    expect(find.text('Punto de entrega'), findsOneWidget);
    expect(find.text('Concesionario Cali Sur'), findsOneWidget);
    expect(find.text('VINAAAAAAAAAAAAA1'), findsOneWidget);
  });

  testWidgets('desde «Mis viajes» se libera lo tomado y vuelve a «Por tomar»', (tester) async {
    final reclamos = await abrir(
      tester,
      ReclamosRepositorioFalso()..pendientesActuales = [pendienteDe('v1', placa: 'ENT001', proceso: 'entrega', estado: EstadoPendiente.mio)],
    );
    expect(find.text('Por recibir (0)'), findsOneWidget);

    await liberarDesdeLaTarjeta(tester, 'Liberar');

    expect(reclamos.liberados, [('v1', 'entrega')]);
    expect(find.text('ENT001 volvió a «Por tomar»'), findsOneWidget);
    expect(find.text('Por recibir (1)'), findsOneWidget);
    expect(find.text('Disponible'), findsOneWidget);
    expect(find.text('Tú recibes'), findsNothing);
  });

  testWidgets('cancelar al liberar no suelta nada', (tester) async {
    final reclamos = await abrir(
      tester,
      ReclamosRepositorioFalso()..pendientesActuales = [pendienteDe('v1', proceso: 'entrega', estado: EstadoPendiente.mio)],
    );

    await tester.tap(find.widgetWithText(OutlinedButton, 'Liberar'));
    await avanzar(tester);
    await tester.tap(find.widgetWithText(TextButton, 'Cancelar'));
    await avanzar(tester);

    expect(reclamos.liberados, isEmpty);
    expect(find.text('Tú recibes'), findsOneWidget);
  });

  testWidgets('si no se pudo liberar desde «Mis viajes», lo dice y sigue tomado', (tester) async {
    final reclamos = ReclamosRepositorioFalso()
      ..pendientesActuales = [pendienteDe('v1', proceso: 'entrega', estado: EstadoPendiente.mio)]
      ..errorLiberar = errorApi(404, 'No hay un reclamo activo');
    await abrir(tester, reclamos);

    await liberarDesdeLaTarjeta(tester, 'Liberar');

    expect(find.text('No hay un reclamo activo'), findsOneWidget);
    expect(find.text('Tú recibes'), findsOneWidget);
  });

  testWidgets('quien carga y recibe el mismo viaje lo ve una vez, con los dos papeles y un «Liberar» para cada uno',
      (tester) async {
    final reclamos = await abrir(
      tester,
      ReclamosRepositorioFalso()
        ..pendientesActuales = [
          pendienteDe('v1', estado: EstadoPendiente.mio),
          pendienteDe('v1', proceso: 'entrega', estado: EstadoPendiente.mio),
        ],
      viajes: ViajesRepositorioFalso()..remotos = [viaje('v1', codigo: 'MTK-v1', vines: ['VINAAAAAAAAAAAAA1'])],
    );

    expect(find.text('MTK-v1'), findsOneWidget);
    expect(find.text('Tú cargas y recibes'), findsOneWidget);

    await liberarDesdeLaTarjeta(tester, 'Liberar la entrega');

    expect(reclamos.liberados, [('v1', 'entrega')]);
    expect(find.text('Tú cargas'), findsOneWidget);
    expect(find.widgetWithText(OutlinedButton, 'Liberar'), findsOneWidget);
  });

  testWidgets('desde el detalle también se libera y el camión vuelve a «Por tomar»', (tester) async {
    final reclamos = await abrir(
      tester,
      ReclamosRepositorioFalso()..pendientesActuales = [pendienteDe('v1', proceso: 'entrega', estado: EstadoPendiente.mio)],
    );

    await tester.tap(find.widgetWithText(FilledButton, 'Ver unidades'));
    await avanzar(tester);
    await tester.tap(find.widgetWithText(OutlinedButton, 'Liberar mi reclamo'));
    await avanzar(tester);
    await tester.tap(find.widgetWithText(TextButton, 'Liberar'));
    await avanzar(tester);

    expect(reclamos.liberados, [('v1', 'entrega')]);
    expect(find.byType(DetallePendientePage), findsNothing);
    expect(find.text('Disponible'), findsOneWidget);
    expect(find.text('Tú recibes'), findsNothing);
  });

  testWidgets('si no se pudo liberar desde el detalle, se queda ahí con el motivo', (tester) async {
    final reclamos = ReclamosRepositorioFalso()
      ..pendientesActuales = [pendienteDe('v1', proceso: 'entrega', estado: EstadoPendiente.mio)]
      ..errorLiberar = errorApi(404, 'No hay un reclamo activo');
    await abrir(tester, reclamos);

    await tester.tap(find.widgetWithText(FilledButton, 'Ver unidades'));
    await avanzar(tester);
    await tester.tap(find.widgetWithText(OutlinedButton, 'Liberar mi reclamo'));
    await avanzar(tester);
    await tester.tap(find.widgetWithText(TextButton, 'Liberar'));
    await avanzar(tester);

    expect(find.byType(DetallePendientePage), findsOneWidget);
    expect(find.text('No hay un reclamo activo'), findsOneWidget);
  });

  testWidgets('quien no es inspector no ve «Por tomar» ni lo consulta', (tester) async {
    final reclamos = await abrir(
      tester,
      ReclamosRepositorioFalso()..pendientesActuales = [pendienteDe('v1')],
      sesion: const Sesion(token: 'jwt', usuario: Usuario(id: 'u2', nombre: 'Luis Gómez', email: '', rol: 'conductor')),
      vinesDeMisViajes: ['VINAAAAAAAAAAAAA1'],
    );

    expect(find.text('Por tomar'), findsNothing);
    expect(reclamos.consultas, 0, reason: 'a un conductor la API le responde 403');
  });
}
