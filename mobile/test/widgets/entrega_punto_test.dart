import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/core/tema/tema.dart';
import 'package:traza_autos/features/auth/presentation/auth_controller.dart';
import 'package:traza_autos/features/custodia/presentation/lienzo_firma.dart';
import 'package:traza_autos/features/sync/domain/job.dart';
import 'package:traza_autos/features/viajes/domain/entrega_punto.dart';
import 'package:traza_autos/features/viajes/presentation/entrega_punto_page.dart';
import 'package:traza_autos/features/viajes/providers.dart';

import '../support/arnes.dart';
import '../support/dobles_viajes.dart';

/// S6-HU-11.3: la entrega agrupada por punto, con una firma por punto y la opción de cerrar con una sola.

Future<void> avanzar(WidgetTester tester) async {
  for (var i = 0; i < 12; i++) {
    await tester.pump(const Duration(milliseconds: 50));
  }
}

Future<Arnes> abrir(WidgetTester tester, EntregasRepositorioFalso entregas, {Arnes? arnes}) async {
  await tester.binding.setSurfaceSize(const Size(600, 2400));
  addTearDown(() => tester.binding.setSurfaceSize(null));
  final usado = arnes ?? Arnes();
  await tester.pumpWidget(
    ProviderScope(
      retry: (_, _) => null,
      overrides: [...usado.overrides, entregasRepositorioProvider.overrideWithValue(entregas)],
      child: MaterialApp(theme: crearTema(), home: const _Arranque()),
    ),
  );
  await avanzar(tester);
  return usado;
}

class _Arranque extends ConsumerWidget {
  const _Arranque();

  @override
  Widget build(BuildContext context, WidgetRef ref) =>
      ref.watch(authControllerProvider.select((e) => e.fase)) == FaseAuth.autenticado ? const EntregasViajePage(viajeId: 'v1') : const SizedBox();
}

Future<void> firmar(WidgetTester tester) async {
  final lienzo = tester.getBottomLeft(find.byType(LienzoFirma)) + const Offset(60, -80);
  await tester.dragFrom(lienzo, const Offset(40, 30));
  await tester.pump();
}

void main() {
  testWidgets('cada punto dice cuántas unidades se entregan allí, con sus VIN', (tester) async {
    await abrir(
      tester,
      EntregasRepositorioFalso()
        ..puntos = [
          puntoDe('Concesionario Autonorte', vines: ['VIN1AAAAAAAAAAAA1', 'VIN2AAAAAAAAAAAA2', 'VIN3AAAAAAAAAAAA3', 'VIN4AAAAAAAAAAAA4']),
          puntoDe('Concesionario Cali Sur', vines: ['VIN5AAAAAAAAAAAA5']),
        ],
    );

    expect(find.text('Aquí entregas estas 4 unidades'), findsOneWidget);
    expect(find.text('Aquí entregas esta unidad'), findsOneWidget);
    expect(find.text('VIN1AAAAAAAAAAAA1'), findsOneWidget);
    expect(find.text('VIN5AAAAAAAAAAAA5'), findsOneWidget);
    expect(find.widgetWithText(FilledButton, 'Firmar la entrega'), findsNWidgets(2));
  });

  testWidgets('un punto ya entregado lo dice y no ofrece firmar', (tester) async {
    await abrir(tester, EntregasRepositorioFalso()..puntos = [puntoDe('Autonorte', estado: EstadoEntrega.cerrada)]);

    expect(find.text('Entregada'), findsOneWidget);
    expect(find.widgetWithText(FilledButton, 'Firmar la entrega'), findsNothing);
  });

  testWidgets('sin señal no se pueden cargar las entregas y se puede reintentar', (tester) async {
    final entregas = EntregasRepositorioFalso()..error = Exception('sin red');
    await abrir(tester, entregas);

    expect(find.text('No se pudieron cargar las entregas'), findsOneWidget);
    entregas.error = null;
    entregas.puntos = [puntoDe('Autonorte')];
    await tester.tap(find.widgetWithText(FilledButton, 'Reintentar'));
    await avanzar(tester);

    expect(find.text('Aquí entregas estas 2 unidades'), findsOneWidget);
  });

  group('la firma del punto', () {
    testWidgets('una firma cierra la entrega de todas las unidades del punto', (tester) async {
      final arnes = Arnes();
      await abrir(tester, EntregasRepositorioFalso()..puntos = [puntoDe('Autonorte')], arnes: arnes);

      await tester.tap(find.widgetWithText(FilledButton, 'Firmar la entrega'));
      await avanzar(tester);
      final boton = tester.widget<FilledButton>(find.widgetWithText(FilledButton, 'Registrar mi firma'));
      expect(boton.onPressed, isNull, reason: 'sin trazo no hay nada que registrar');

      await firmar(tester);
      await tester.tap(find.widgetWithText(FilledButton, 'Registrar mi firma'));
      await avanzar(tester);

      expect(arnes.gateway.entregasEnviadas, hasLength(1));
      expect(arnes.gateway.entregasEnviadas.single.$1, 'v1');
      expect(arnes.gateway.entregasEnviadas.single.$2, 'l-Autonorte');
      expect(arnes.gateway.entregasEnviadas.single.$3, isNull);
      expect(arnes.gateway.entregasEnviadas.single.$4, isNotEmpty);
      expect(find.text('Entrega registrada'), findsOneWidget);
    });

    testWidgets('sin señal queda en la cola, a nombre de quien firmó', (tester) async {
      final arnes = Arnes()..red.estable = false;
      await abrir(tester, EntregasRepositorioFalso()..puntos = [puntoDe('Autonorte')], arnes: arnes);

      await tester.tap(find.widgetWithText(FilledButton, 'Firmar la entrega'));
      await avanzar(tester);
      await firmar(tester);
      await tester.tap(find.widgetWithText(FilledButton, 'Registrar mi firma'));
      await avanzar(tester);

      expect(find.text('Entrega guardada'), findsOneWidget);
      final job = arnes.cola.jobs.single as EntregaPuntoJob;
      expect((job.viajeId, job.lugarId, job.usuarioId, job.fallido), ('v1', 'l-Autonorte', 'u1', false));
      expect(arnes.gateway.entregasEnviadas, isEmpty);
    });

    testWidgets('si falta la inspección de llegada de una unidad dice cuál y no deja firmar', (tester) async {
      await abrir(
        tester,
        EntregasRepositorioFalso()
          ..puntos = [
            EntregaPunto(
              viajeId: 'v1',
              lugarId: 'l1',
              lugar: 'Autonorte',
              pendientes: 2,
              entregada: false,
              unidades: const [
                UnidadEntrega(vin: 'VINAAAAAAAAAAAAA1', estado: EstadoEntrega.pendiente),
                UnidadEntrega(vin: 'VINBBBBBBBBBBBBB2', estado: EstadoEntrega.sinInspeccion),
              ],
            ),
          ],
      );

      await tester.tap(find.widgetWithText(FilledButton, 'Firmar la entrega'));
      await avanzar(tester);
      await firmar(tester);

      expect(find.textContaining('Aún no tienen la inspección de llegada del descargue: VINBBBBBBBBBBBBB2'), findsOneWidget);
      final boton = tester.widget<FilledButton>(find.widgetWithText(FilledButton, 'Registrar mi firma'));
      expect(boton.onPressed, isNull);
    });
  });

  group('cerrar con una sola firma', () {
    Future<Arnes> abrirCierre(WidgetTester tester) async {
      final arnes = Arnes();
      await abrir(tester, EntregasRepositorioFalso()..puntos = [puntoDe('Autonorte')], arnes: arnes);
      await tester.tap(find.widgetWithText(FilledButton, 'Firmar la entrega'));
      await avanzar(tester);
      await firmar(tester);
      await tester.tap(find.textContaining('cerrar con una sola firma'));
      await avanzar(tester);
      return arnes;
    }

    testWidgets('pide el motivo y avisa que quedará como novedad para el coordinador antes de confirmar', (tester) async {
      await abrirCierre(tester);

      expect(find.text('Quedará registrado como novedad para el coordinador.'), findsOneWidget);
      final boton = tester.widget<FilledButton>(find.widgetWithText(FilledButton, 'Cerrar con una sola firma'));
      expect(boton.onPressed, isNull, reason: 'sin motivo no se cierra');
    });

    testWidgets('con motivo se encola el cierre con una sola firma', (tester) async {
      final arnes = await abrirCierre(tester);

      await tester.enterText(find.byType(TextField), 'no hay inspector en el punto');
      await tester.pump();
      await tester.tap(find.widgetWithText(FilledButton, 'Cerrar con una sola firma'));
      await avanzar(tester);

      expect(arnes.gateway.entregasEnviadas.single.$3, 'no hay inspector en el punto');
      expect(find.text('Entrega registrada'), findsOneWidget);
    });

    testWidgets('se puede volver a la firma de las dos partes', (tester) async {
      await abrirCierre(tester);

      await tester.tap(find.text('Volver: firman las dos partes'));
      await avanzar(tester);

      expect(find.widgetWithText(FilledButton, 'Registrar mi firma'), findsOneWidget);
      expect(find.text('Quedará registrado como novedad para el coordinador.'), findsNothing);
    });
  });
}
