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

/// S6-HU-12.4: la lista de pendientes del punto del inspector, con sus tres estados, «revisar este» y liberar.

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
}) async {
  await tester.binding.setSurfaceSize(const Size(600, 2400));
  addTearDown(() => tester.binding.setSurfaceSize(null));
  final viajes = ViajesRepositorioFalso()..remotos = [if (vinesDeMisViajes.isNotEmpty) viaje('mio', vines: vinesDeMisViajes)];
  await tester.pumpWidget(
    ProviderScope(
      retry: (_, _) => null,
      overrides: [
        ...Arnes(sesion: sesion).overrides,
        viajesRepositorioProvider.overrideWithValue(viajes),
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

void main() {
  testWidgets('el inspector ve cada pendiente con su estado y quién lo tiene', (tester) async {
    await abrir(
      tester,
      ReclamosRepositorioFalso()
        ..pendientesActuales = [
          pendienteDe('v1', placa: 'AAA111'),
          pendienteDe('v2', placa: 'BBB222', estado: EstadoPendiente.enRevision, por: 'Andrés'),
          pendienteDe('v3', placa: 'CCC333', estado: EstadoPendiente.mio),
        ],
    );

    expect(find.text('Pendientes de mi punto'), findsOneWidget);
    expect(find.text('Disponible'), findsOneWidget);
    expect(find.text('En revisión por Andrés'), findsOneWidget);
    expect(find.text('Reclamado por ti'), findsOneWidget);
    // Solo lo disponible se puede reclamar; lo que tiene otro no ofrece ningún botón.
    expect(find.widgetWithText(FilledButton, 'Revisar este'), findsOneWidget);
    expect(find.widgetWithText(OutlinedButton, 'Abrir'), findsOneWidget);
  });

  testWidgets('sin pendientes las dos listas siguen ahí y lo dicen, y los viajes propios siguen debajo', (tester) async {
    await abrir(tester, ReclamosRepositorioFalso(), vinesDeMisViajes: ['VINAAAAAAAAAAAAA1']);

    expect(find.text('Por recibir (0)'), findsOneWidget);
    expect(find.text('Nada por recibir'), findsOneWidget);
    expect(find.text('Por cargar (0)'), findsOneWidget);
    expect(find.text('Nada por cargar'), findsOneWidget);
    expect(find.text('VINAAAAAAAAAAAAA1'), findsOneWidget);
  });

  // S8-HU-10.2 ESC-01: entregas en «Por recibir», cargues en «Por cargar», y ninguno en las dos.
  testWidgets('los pendientes se parten en «Por recibir» y «Por cargar», cada uno en una sola', (tester) async {
    await abrir(
      tester,
      ReclamosRepositorioFalso()
        ..pendientesActuales = [
          pendienteDe('v1', placa: 'ENT001', proceso: 'entrega'),
          pendienteDe('v2', placa: 'CAR001'),
          pendienteDe('v3', placa: 'ENT002', proceso: 'entrega', estado: EstadoPendiente.mio),
          // Un proceso que la app no conoce no se pierde: cae con lo que se carga, como lo lee `Pendiente.fromJson`.
          pendienteDe('v4', placa: 'RAR001', proceso: 'otro'),
        ],
    );

    expect(find.text('Por recibir (2)'), findsOneWidget);
    expect(find.text('Por cargar (2)'), findsOneWidget);
    expect(find.text('RAR001'), findsOneWidget);
    expect(find.text('Nada por recibir'), findsNothing);
    expect(find.text('Nada por cargar'), findsNothing);
    for (final placa in ['ENT001', 'CAR001', 'ENT002']) {
      expect(find.text(placa), findsOneWidget, reason: '$placa sale en una sola lista');
    }

    // El orden en pantalla: primero lo que llega, después lo que sale.
    double y(String texto) => tester.getTopLeft(find.text(texto)).dy;
    expect(y('Por recibir (2)'), lessThan(y('ENT001')));
    expect(y('ENT002'), lessThan(y('Por cargar (2)')));
    expect(y('Por cargar (2)'), lessThan(y('CAR001')));
    // Las acciones no cambian (CA-7): lo disponible se reclama y lo propio se abre.
    expect(find.widgetWithText(FilledButton, 'Revisar este'), findsNWidgets(3));
    expect(find.widgetWithText(OutlinedButton, 'Abrir'), findsOneWidget);
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

    expect(find.textContaining('No se pudieron consultar los pendientes'), findsOneWidget);
    reclamos.errorPendientes = null;
    reclamos.pendientesActuales = [pendienteDe('v1')];
    await tester.tap(find.widgetWithText(TextButton, 'Reintentar'));
    await avanzar(tester);

    expect(find.text('Disponible'), findsOneWidget);
  });

  testWidgets('«Revisar este» reclama el camión y abre su detalle con el punto', (tester) async {
    final reclamos = await abrir(tester, ReclamosRepositorioFalso()..pendientesActuales = [pendienteDe('v1', placa: 'AAA111')]);

    await tester.tap(find.widgetWithText(FilledButton, 'Revisar este'));
    await avanzar(tester);

    expect(reclamos.reclamados, [('v1', 'cargue')]);
    expect(find.byType(DetallePendientePage), findsOneWidget);
    expect(find.text('Punto de cargue'), findsOneWidget);
    expect(find.text('CEDI Envigado'), findsWidgets);
    expect(find.text('VINAAAAAAAAAAAAA1'), findsOneWidget);
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

    await tester.tap(find.widgetWithText(FilledButton, 'Revisar este').first);
    await avanzar(tester);

    expect(find.text('Ya fue tomado por otro inspector'), findsOneWidget);
    expect(find.byType(DetallePendientePage), findsNothing);
    expect(find.text('En revisión por Andrés'), findsOneWidget);
    expect(find.text('BBB222'), findsOneWidget, reason: 'el otro camión sigue en la lista');
    expect(find.widgetWithText(FilledButton, 'Revisar este'), findsOneWidget);
  });

  testWidgets('desde el detalle se libera el reclamo propio y el camión vuelve a estar disponible', (tester) async {
    final reclamos = await abrir(tester, ReclamosRepositorioFalso()..pendientesActuales = [pendienteDe('v1', estado: EstadoPendiente.mio)]);

    await tester.tap(find.widgetWithText(OutlinedButton, 'Abrir'));
    await avanzar(tester);
    await tester.tap(find.widgetWithText(OutlinedButton, 'Liberar mi reclamo'));
    await avanzar(tester);
    await tester.tap(find.widgetWithText(TextButton, 'Liberar'));
    await avanzar(tester);

    expect(reclamos.liberados, [('v1', 'cargue')]);
    expect(find.byType(DetallePendientePage), findsNothing);
    expect(find.text('Disponible'), findsOneWidget);
    expect(find.text('Reclamado por ti'), findsNothing);
  });

  testWidgets('si no se pudo liberar, se queda en el detalle con el motivo', (tester) async {
    final reclamos = ReclamosRepositorioFalso()
      ..pendientesActuales = [pendienteDe('v1', estado: EstadoPendiente.mio)]
      ..errorLiberar = errorApi(404, 'No hay un reclamo activo');
    await abrir(tester, reclamos);

    await tester.tap(find.widgetWithText(OutlinedButton, 'Abrir'));
    await avanzar(tester);
    await tester.tap(find.widgetWithText(OutlinedButton, 'Liberar mi reclamo'));
    await avanzar(tester);
    await tester.tap(find.widgetWithText(TextButton, 'Liberar'));
    await avanzar(tester);

    expect(find.byType(DetallePendientePage), findsOneWidget);
    expect(find.text('No hay un reclamo activo'), findsOneWidget);
  });

  testWidgets('la entrega se reclama con la misma pantalla y muestra el punto de entrega', (tester) async {
    final reclamos = await abrir(
      tester,
      ReclamosRepositorioFalso()
        ..pendientesActuales = [pendienteDe('v9', proceso: 'entrega', lugar: 'Concesionario Cali Sur')],
    );

    expect(find.textContaining('Entrega · Concesionario Cali Sur'), findsOneWidget);
    await tester.tap(find.widgetWithText(FilledButton, 'Revisar este'));
    await avanzar(tester);

    expect(reclamos.reclamados, [('v9', 'entrega')]);
    expect(find.text('Entrega por revisar'), findsOneWidget);
    expect(find.text('Punto de entrega'), findsOneWidget);
    expect(find.text('Concesionario Cali Sur'), findsOneWidget);
  });

  testWidgets('quien no es inspector no ve pendientes ni los consulta', (tester) async {
    final reclamos = await abrir(
      tester,
      ReclamosRepositorioFalso()..pendientesActuales = [pendienteDe('v1')],
      sesion: const Sesion(token: 'jwt', usuario: Usuario(id: 'u2', nombre: 'Luis Gómez', email: '', rol: 'conductor')),
      vinesDeMisViajes: ['VINAAAAAAAAAAAAA1'],
    );

    expect(find.text('Pendientes de mi punto'), findsNothing);
    expect(reclamos.consultas, 0, reason: 'a un conductor la API le responde 403');
  });
}
