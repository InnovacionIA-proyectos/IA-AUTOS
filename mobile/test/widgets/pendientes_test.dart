import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/core/dispositivo/servicios.dart';
import 'package:traza_autos/core/errores/error_api.dart';
import 'package:traza_autos/core/tema/tema.dart';
import 'package:traza_autos/core/ui/widgets.dart';
import 'package:traza_autos/features/auth/domain/sesion.dart';
import 'package:traza_autos/features/auth/presentation/auth_controller.dart';
import 'package:traza_autos/features/viajes/domain/pendiente.dart';
import 'package:traza_autos/features/viajes/domain/ubicacion_lugar.dart';
import 'package:traza_autos/features/viajes/presentation/detalle_pendiente_page.dart';
import 'package:traza_autos/features/viajes/presentation/entrega_punto_page.dart';
import 'package:traza_autos/features/viajes/presentation/mis_viajes_page.dart';
import 'package:traza_autos/features/viajes/presentation/seccion_pendientes.dart';
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
  Arnes? arnes,
  LugaresRepositorioFalso? lugares,
}) async {
  await tester.binding.setSurfaceSize(const Size(600, 2400));
  addTearDown(() => tester.binding.setSurfaceSize(null));
  final repositorio =
      viajes ?? (ViajesRepositorioFalso()..remotos = [if (vinesDeMisViajes.isNotEmpty) viaje('mio', vines: vinesDeMisViajes)]);
  await tester.pumpWidget(
    ProviderScope(
      retry: (_, _) => null,
      overrides: [
        ...(arnes ?? Arnes(sesion: sesion)).overrides,
        viajesRepositorioProvider.overrideWithValue(repositorio),
        reclamosRepositorioProvider.overrideWithValue(reclamos),
        // Los lugares que el inspector ve con «Fijar ubicación aquí»: por defecto, ninguno sin ubicación.
        lugaresRepositorioProvider.overrideWithValue(lugares ?? LugaresRepositorioFalso()),
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

  // Orden visual: lo que nadie ha reclamado (franja verde), lo que ya tomó otro inspector (aparte, en gris) y lo que
  // tomó quien mira (en «Mis viajes», franja azul) no se confunden.
  testWidgets('lo libre, lo de otro inspector y lo propio se distinguen por grupo y por franja', (tester) async {
    await abrir(
      tester,
      ReclamosRepositorioFalso()
        ..pendientesActuales = [
          pendienteDe('v1', placa: 'AAA111'),
          pendienteDe('v2', placa: 'BBB222', estado: EstadoPendiente.enRevision, por: 'Andrés'),
          pendienteDe('v3', placa: 'CCC333', estado: EstadoPendiente.mio),
        ],
    );

    Color? franjaDe(String placa) =>
        tester.widget<Tarjeta>(find.ancestor(of: find.text(placa), matching: find.byType(Tarjeta)).first).franja;

    expect(find.text(leyendaPorTomar), findsOneWidget);
    expect(franjaDe('AAA111'), Colores.exito, reason: 'libre para tomar');
    expect(franjaDe('BBB222'), Colores.textoSecundario, reason: 'la tiene otro inspector');
    expect(franjaDe('CCC333'), Colores.primario, reason: 'la tomó quien mira');
    // Lo de otro inspector va aparte, con su subtítulo, debajo de lo libre.
    expect(find.text(tituloDeLoAjeno(1)), findsOneWidget);
    expect(_y(tester, find.text('AAA111')), lessThan(_y(tester, find.text(tituloDeLoAjeno(1)))));
    expect(_y(tester, find.text(tituloDeLoAjeno(1))), lessThan(_y(tester, find.text('BBB222'))));
  });

  testWidgets('sin nada de otro inspector no sale ese subtítulo', (tester) async {
    await abrir(tester, ReclamosRepositorioFalso()..pendientesActuales = [pendienteDe('v1', placa: 'AAA111')]);

    expect(find.textContaining('Las tiene otro inspector'), findsNothing);
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

  // S8-HU-05.2 CA-7 (ESC-08): la parada ya se cerró sin inspector del punto; el cierre es definitivo y se dice a qué
  // hora, en la de Colombia.
  testWidgets('la parada cerrada sin inspector del punto dice a qué hora y que el cierre es definitivo', (tester) async {
    final reclamos = ReclamosRepositorioFalso()
      ..pendientesActuales = [pendienteDe('v9', placa: 'ENT009', proceso: 'entrega', lugar: 'Puerto de Cartagena')]
      ..errorReclamar = const ErrorApi(
        message: 'texto del servidor',
        code: 'CONFLICT',
        status: 409,
        details: {'motivo': 'entrega_cerrada_sin_inspector', 'cerrada_at': '2026-11-26T14:20:00Z'},
      );
    await abrir(tester, reclamos);

    await tester.tap(find.widgetWithText(FilledButton, 'Tomar'));
    await avanzar(tester);

    expect(find.text('Esta entrega se cerró sin inspector del punto a las 09:20: el cierre es definitivo'), findsOneWidget);
    expect(find.text('Tú recibes'), findsNothing);
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

  // Revisión (CA-6 de S8-HU-10.2): sin señal, lo tomado sigue en «Mis viajes» con lo último guardado.
  testWidgets('lo tomado se guarda en el teléfono y sin señal sigue en «Mis viajes»', (tester) async {
    final conSenal = Arnes();
    await abrir(
      tester,
      ReclamosRepositorioFalso()..pendientesActuales = [pendienteDe('v9', placa: 'ENT009', proceso: 'entrega', estado: EstadoPendiente.mio)],
      arnes: conSenal,
    );
    expect(conSenal.tomadosGuardados.guardados['tomados.u1'], contains('v9'));

    final sinSenal = Arnes()..tomadosGuardados.guardados.addAll(conSenal.tomadosGuardados.guardados);
    await abrir(tester, ReclamosRepositorioFalso()..errorPendientes = Exception('sin red'), arnes: sinSenal);

    expect(find.text('Tú recibes'), findsOneWidget);
    expect(find.text('MTK-v9'), findsOneWidget);
  });

  // S8-HU-04.2 CA-10: la entrega se toma y se libera por parada; el cargue no lleva parada.
  testWidgets('tomar la entrega manda su parada, y tomar el cargue no manda ninguna', (tester) async {
    final reclamos = await abrir(
      tester,
      ReclamosRepositorioFalso()
        ..pendientesActuales = [
          pendienteDe('v9', placa: 'ENT009', proceso: 'entrega', lugar: 'Puerto de Cartagena'),
          pendienteDe('v8', placa: 'CAR008'),
        ],
    );

    await tester.tap(find.widgetWithText(FilledButton, 'Tomar').first);
    await avanzar(tester);
    await tester.tap(find.widgetWithText(FilledButton, 'Tomar'));
    await avanzar(tester);

    expect(reclamos.reclamados, [('v9', 'entrega'), ('v8', 'cargue')]);
    expect(reclamos.paradasReclamadas, ['l-Puerto de Cartagena', null]);
  });

  testWidgets('si otro inspector ya tomó la parada, se dice con el mensaje del servidor', (tester) async {
    await abrir(
      tester,
      ReclamosRepositorioFalso()
        ..pendientesActuales = [pendienteDe('v9', proceso: 'entrega', lugar: 'Puerto de Cartagena')]
        ..errorReclamar = errorApi(409, 'Ya fue tomado por otro inspector'),
    );

    await tester.tap(find.widgetWithText(FilledButton, 'Tomar'));
    await avanzar(tester);

    expect(find.text('Ya fue tomado por otro inspector'), findsOneWidget);
  });

  testWidgets('liberar la entrega nombra su parada', (tester) async {
    final reclamos = await abrir(
      tester,
      ReclamosRepositorioFalso()
        ..pendientesActuales = [pendienteDe('v1', proceso: 'entrega', estado: EstadoPendiente.mio, lugar: 'Puerto de Cartagena')],
    );

    await liberarDesdeLaTarjeta(tester, 'Liberar');

    expect(reclamos.paradasLiberadas, ['l-Puerto de Cartagena']);
  });

  // ESC-11: dos paradas del mismo viaje son dos entregas, cada una con su «Liberar» que dice cuál es.
  testWidgets('dos paradas tomadas del mismo viaje: cada «Liberar» nombra la suya', (tester) async {
    final reclamos = await abrir(
      tester,
      ReclamosRepositorioFalso()
        ..pendientesActuales = [
          pendienteDe('v1', proceso: 'entrega', estado: EstadoPendiente.mio, lugar: 'Puerto de Cartagena'),
          pendienteDe('v1', proceso: 'entrega', estado: EstadoPendiente.mio, lugar: 'Concesionario Sur'),
        ],
      viajes: ViajesRepositorioFalso()..remotos = [viaje('v1', codigo: 'MTK-v1', vines: ['VINAAAAAAAAAAAAA1'])],
    );

    await liberarDesdeLaTarjeta(tester, 'Liberar la entrega · Concesionario Sur');

    expect(reclamos.paradasLiberadas, ['l-Concesionario Sur']);
  });

  // CA-1: el inspector entrega en la parada que tomó, desde el viaje y desde lo que tomó en «Pendientes».
  testWidgets('desde el viaje, el inspector entrega solo en la parada que tomó', (tester) async {
    await abrir(
      tester,
      ReclamosRepositorioFalso()
        ..pendientesActuales = [
          pendienteDe('v1', estado: EstadoPendiente.mio),
          pendienteDe('v1', proceso: 'entrega', estado: EstadoPendiente.mio, lugar: 'Puerto de Cartagena'),
        ],
      viajes: ViajesRepositorioFalso()..remotos = [viaje('v1', codigo: 'MTK-v1', vines: ['VINAAAAAAAAAAAAA1'])],
    );

    expect(find.byType(BotonEntregarEnParada), findsOneWidget, reason: 'el cargue no se entrega por parada');
    expect(find.widgetWithText(FilledButton, 'Entregar en esta parada · Puerto de Cartagena'), findsOneWidget);
    expect(find.widgetWithText(OutlinedButton, 'Paradas y entregas'), findsNothing, reason: 'la lista es del conductor');
  });

  testWidgets('la entrega tomada que no llega en «Mis viajes» también se entrega, desde su tarjeta y su detalle',
      (tester) async {
    await abrir(
      tester,
      ReclamosRepositorioFalso()
        ..pendientesActuales = [pendienteDe('v9', proceso: 'entrega', estado: EstadoPendiente.mio, lugar: 'Puerto de Cartagena')],
    );

    expect(find.widgetWithText(FilledButton, 'Entregar en esta parada · Puerto de Cartagena'), findsOneWidget);
    await tester.tap(find.widgetWithText(FilledButton, 'Ver unidades'));
    await avanzar(tester);
    expect(find.widgetWithText(FilledButton, 'Entregar en esta parada · Puerto de Cartagena'), findsOneWidget);
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

  // S8-HU-09.2 CA-4 y ESC-09: la entrega se toma con el GPS del momento; el servidor decide si quedó lejos y avisa.
  group('la ubicación al tomar una entrega', () {
    ReclamosRepositorioFalso conEntrega() => ReclamosRepositorioFalso()
      ..pendientesActuales = [pendienteDe('v9', placa: 'ENT009', proceso: 'entrega', lugar: 'Puerto de Cartagena')];

    testWidgets('manda la posición y la precisión del GPS de ese momento', (tester) async {
      final arnes = Arnes()..ubicacion.posicion = Posicion(10.39, -75.51, precision: 10, tomadaEn: DateTime.now());
      final reclamos = await abrir(tester, conEntrega(), arnes: arnes);

      await tester.tap(find.widgetWithText(FilledButton, 'Tomar'));
      await avanzar(tester);

      final enviada = reclamos.posicionesReclamadas.single;
      expect((enviada?.lat, enviada?.lng, enviada?.precision), (10.39, -75.51, 10));
      expect(find.text('ENT009 quedó en Mis viajes'), findsOneWidget);
      expect(find.textContaining('Se tomó sin ubicación'), findsNothing);
    });

    testWidgets('sin GPS en ese instante la toma igual, sin ubicación, y lo dice', (tester) async {
      final arnes = Arnes()..ubicacion.posicion = null;
      final reclamos = await abrir(tester, conEntrega(), arnes: arnes);

      await tester.tap(find.widgetWithText(FilledButton, 'Tomar'));
      await avanzar(tester);

      expect(reclamos.reclamados, [('v9', 'entrega')], reason: 'el reclamo nunca se bloquea por el GPS');
      expect(reclamos.posicionesReclamadas, [null]);
      expect(
        find.textContaining('Se tomó sin ubicación: el coordinador no sabrá si estabas en el lugar'),
        findsOneWidget,
      );
      expect(find.text('Tú recibes'), findsOneWidget);
    });

    testWidgets('una lectura vieja del GPS no se manda como si fuera de ahora', (tester) async {
      final arnes = Arnes()
        ..ubicacion.posicion = Posicion(6.2, -75.5, precision: 5, tomadaEn: DateTime.now().subtract(const Duration(hours: 3)));
      final reclamos = await abrir(tester, conEntrega(), arnes: arnes);

      await tester.tap(find.widgetWithText(FilledButton, 'Tomar'));
      await avanzar(tester);

      expect(reclamos.posicionesReclamadas, [null]);
      expect(find.textContaining('Se tomó sin ubicación'), findsOneWidget);
    });

    testWidgets('el cargue no lleva ubicación ni dice nada de ella', (tester) async {
      final arnes = Arnes()..ubicacion.posicion = null;
      final reclamos = await abrir(tester, ReclamosRepositorioFalso()..pendientesActuales = [pendienteDe('v1')], arnes: arnes);

      await tester.tap(find.widgetWithText(FilledButton, 'Tomar'));
      await avanzar(tester);

      expect(reclamos.posicionesReclamadas, [null]);
      expect(find.textContaining('Se tomó sin ubicación'), findsNothing);
    });

    test('sin GPS devuelve null; con una lectura de ahora, esa', () async {
      expect(await posicionParaReclamar(UbicacionFalsa()..posicion = null), isNull);
      final ahora = Posicion(1, 2, precision: 3, tomadaEn: DateTime.now());
      expect(await posicionParaReclamar(UbicacionFalsa()..posicion = ahora), same(ahora));
    });
  });

  // S8-HU-09.2 CA-3 y ESC-08: la inspectora fija la ubicación del punto en el cargue que reclamó.
  group('«Fijar ubicación aquí» del inspector', () {
    testWidgets('en el cargue reclamado, con el lugar del pendiente y la precisión del GPS', (tester) async {
      final lugares = LugaresRepositorioFalso()..estadosPorLugar = {'l-Patio de Prueba': EstadoUbicacionLugar.sinUbicacion};
      final arnes = Arnes()..ubicacion.posicion = Posicion(6.17, -75.58, precision: 12, tomadaEn: DateTime.now());
      await abrir(
        tester,
        ReclamosRepositorioFalso()
          ..pendientesActuales = [pendienteDe('v1', estado: EstadoPendiente.mio, lugar: 'Patio de Prueba')],
        viajes: ViajesRepositorioFalso()..remotos = [viaje('v1', codigo: 'MT-2026-0421', vines: ['VINAAAAAAAAAAAAA1'])],
        arnes: arnes,
        lugares: lugares,
      );

      expect(find.text('Tú cargas'), findsOneWidget);
      await tester.tap(find.widgetWithText(OutlinedButton, 'Fijar ubicación aquí'));
      await avanzar(tester);

      expect(lugares.fijados.single.$1, 'l-Patio de Prueba');
      expect(lugares.fijados.single.$2.precision, 12);
      expect(find.text('Ubicación pendiente de confirmar'), findsOneWidget);
    });

    testWidgets('en el detalle de lo que tomó también, y no en lo que no es suyo', (tester) async {
      final lugares = LugaresRepositorioFalso()
        ..estadosPorLugar = {
          'l-Puerto de Cartagena': EstadoUbicacionLugar.sinUbicacion,
          'l-CEDI Envigado': EstadoUbicacionLugar.sinUbicacion,
        };
      await abrir(
        tester,
        ReclamosRepositorioFalso()
          ..pendientesActuales = [
            pendienteDe('v9', proceso: 'entrega', estado: EstadoPendiente.mio, lugar: 'Puerto de Cartagena'),
            pendienteDe('v2', placa: 'BBB222', estado: EstadoPendiente.enRevision, por: 'Andrés'),
          ],
        lugares: lugares,
      );

      expect(find.widgetWithText(OutlinedButton, 'Fijar ubicación aquí'), findsNothing, reason: 'en la lista no');
      await tester.tap(find.widgetWithText(FilledButton, 'Ver unidades'));
      await avanzar(tester);

      expect(find.widgetWithText(OutlinedButton, 'Fijar ubicación aquí'), findsOneWidget);
    });

    testWidgets('con el lugar ya con ubicación no se ofrece', (tester) async {
      await abrir(
        tester,
        ReclamosRepositorioFalso()
          ..pendientesActuales = [pendienteDe('v1', estado: EstadoPendiente.mio, lugar: 'Patio de Prueba')],
        viajes: ViajesRepositorioFalso()..remotos = [viaje('v1', vines: ['VINAAAAAAAAAAAAA1'])],
        lugares: LugaresRepositorioFalso()..estadosPorLugar = {'l-Patio de Prueba': EstadoUbicacionLugar.confirmada},
      );

      expect(find.widgetWithText(OutlinedButton, 'Fijar ubicación aquí'), findsNothing);
    });
  });

  // S8-HU-09.2 CA-4: mientras se toma (esperando el GPS o la red) el botón dice «Tomando…» y no se vuelve a tocar.
  testWidgets('mientras se toma, el botón no manda un segundo reclamo', (tester) async {
    final espera = Completer<void>();
    final reclamos = await abrir(
      tester,
      ReclamosRepositorioFalso()
        ..pendientesActuales = [pendienteDe('v9', placa: 'ENT009', proceso: 'entrega', lugar: 'Puerto de Cartagena')]
        ..esperaAlReclamar = espera,
    );

    await tester.tap(find.widgetWithText(FilledButton, 'Tomar'));
    await tester.pump();

    final boton = find.widgetWithText(FilledButton, 'Tomando…');
    expect(boton, findsOneWidget);
    expect(tester.widget<FilledButton>(boton).onPressed, isNull);
    await tester.tap(boton, warnIfMissed: false);
    await tester.pump();

    espera.complete();
    await avanzar(tester);
    expect(reclamos.reclamados, [('v9', 'entrega')], reason: 'un solo reclamo');
  });
}
