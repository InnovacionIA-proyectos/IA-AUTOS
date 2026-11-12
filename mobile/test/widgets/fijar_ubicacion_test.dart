import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/core/dispositivo/servicios.dart';
import 'package:traza_autos/core/errores/error_api.dart';
import 'package:traza_autos/core/tema/tema.dart';
import 'package:traza_autos/features/auth/domain/sesion.dart';
import 'package:traza_autos/features/auth/presentation/auth_controller.dart';
import 'package:traza_autos/features/viajes/domain/ubicacion_lugar.dart';
import 'package:traza_autos/features/viajes/presentation/entrega_punto_page.dart';
import 'package:traza_autos/features/viajes/providers.dart';

import '../support/arnes.dart';
import '../support/dobles_viajes.dart';

/// S6-HU-17.4: «fijar ubicación aquí» en el punto de entrega del conductor.

const _conductor = Sesion(
  token: 'jwt-conductor',
  usuario: Usuario(id: 'u2', nombre: 'Luis Gómez', email: '', rol: 'conductor'),
);

Future<void> avanzar(WidgetTester tester) async {
  for (var i = 0; i < 12; i++) {
    await tester.pump(const Duration(milliseconds: 50));
  }
}

class _Arranque extends ConsumerWidget {
  const _Arranque();

  @override
  Widget build(BuildContext context, WidgetRef ref) =>
      ref.watch(authControllerProvider.select((e) => e.fase)) == FaseAuth.autenticado ? const EntregasViajePage(viajeId: 'v1') : const SizedBox();
}

Future<Arnes> abrir(WidgetTester tester, LugaresRepositorioFalso lugares, {Sesion sesion = _conductor}) async {
  await tester.binding.setSurfaceSize(const Size(600, 2400));
  addTearDown(() => tester.binding.setSurfaceSize(null));
  final arnes = Arnes(sesion: sesion);
  await tester.pumpWidget(
    ProviderScope(
      retry: (_, _) => null,
      overrides: [
        ...arnes.overrides,
        entregasRepositorioProvider.overrideWithValue(EntregasRepositorioFalso()..puntos = [puntoDe('Autonorte')]),
        lugaresRepositorioProvider.overrideWithValue(lugares),
      ],
      child: MaterialApp(theme: crearTema(), home: const _Arranque()),
    ),
  );
  await avanzar(tester);
  return arnes;
}

void main() {
  final boton = find.widgetWithText(OutlinedButton, 'Fijar ubicación aquí');

  testWidgets('un lugar sin ubicación ofrece fijarla y envía la posición y la precisión del teléfono', (tester) async {
    final lugares = LugaresRepositorioFalso()..estadosPorLugar = {'l-Autonorte': EstadoUbicacionLugar.sinUbicacion};
    final arnes = await abrir(tester, lugares);
    arnes.ubicacion.posicion = const Posicion(6.1706, -75.5874, precision: 18.5);

    await tester.tap(boton);
    await avanzar(tester);

    expect(lugares.fijados, hasLength(1));
    expect(lugares.fijados.single.$1, 'l-Autonorte');
    expect(lugares.fijados.single.$2.lat, 6.1706);
    expect(lugares.fijados.single.$2.precision, 18.5);
    expect(boton, findsNothing);
    expect(find.text('Ubicación pendiente de confirmar'), findsOneWidget);
  });

  testWidgets('un lugar con ubicación confirmada no muestra el botón ni ningún aviso', (tester) async {
    await abrir(tester, LugaresRepositorioFalso()..estadosPorLugar = {'l-Autonorte': EstadoUbicacionLugar.confirmada});

    expect(boton, findsNothing);
    expect(find.text('Ubicación pendiente de confirmar'), findsNothing);
    expect(find.textContaining('rango'), findsNothing);
  });

  testWidgets('sin señal lo dice y deja reintentar sin perder la entrega', (tester) async {
    final lugares = LugaresRepositorioFalso()
      ..estadosPorLugar = {'l-Autonorte': EstadoUbicacionLugar.sinUbicacion}
      ..errorAlFijar = const ErrorApi.red();
    await abrir(tester, lugares);

    await tester.tap(boton);
    await avanzar(tester);

    expect(find.textContaining('No se pudo enviar la ubicación'), findsOneWidget);
    expect(boton, findsOneWidget);
    expect(find.widgetWithText(FilledButton, 'Firmar la entrega'), findsOneWidget);

    lugares.errorAlFijar = null;
    await tester.tap(boton);
    await avanzar(tester);

    expect(lugares.fijados, hasLength(1));
    expect(find.text('Ubicación pendiente de confirmar'), findsOneWidget);
  });

  testWidgets('sin GPS pide activarlo y no envía nada', (tester) async {
    final lugares = LugaresRepositorioFalso()..estadosPorLugar = {'l-Autonorte': EstadoUbicacionLugar.sinUbicacion};
    final arnes = await abrir(tester, lugares);
    arnes.ubicacion.error = const UbicacionNoDisponible('GPS apagado');

    await tester.tap(boton);
    await avanzar(tester);

    expect(find.textContaining('No hay señal de GPS'), findsOneWidget);
    expect(lugares.fijados, isEmpty);
  });

  testWidgets('si otro ya fijó el lugar (409) el botón desaparece sin mostrar un error', (tester) async {
    final lugares = LugaresRepositorioFalso()
      ..estadosPorLugar = {'l-Autonorte': EstadoUbicacionLugar.sinUbicacion}
      ..errorAlFijar = const ErrorApi(message: 'El lugar ya tiene ubicación', code: 'CONFLICT', status: 409);
    await abrir(tester, lugares);
    lugares.estadosPorLugar = {'l-Autonorte': EstadoUbicacionLugar.pendienteConfirmar};

    await tester.tap(boton);
    await avanzar(tester);

    expect(boton, findsNothing);
    expect(find.textContaining('ya tiene ubicación'), findsNothing);
    expect(find.text('Ubicación pendiente de confirmar'), findsOneWidget);
  });

  testWidgets('si el catálogo no se puede consultar no se ofrece fijar y la entrega sigue', (tester) async {
    await abrir(tester, LugaresRepositorioFalso()..errorAlConsultar = const ErrorApi.red());

    expect(boton, findsNothing);
    expect(find.widgetWithText(FilledButton, 'Firmar la entrega'), findsOneWidget);
  });

  testWidgets('el inspector no ve el botón aunque el lugar no tenga ubicación', (tester) async {
    await abrir(
      tester,
      LugaresRepositorioFalso()..estadosPorLugar = {'l-Autonorte': EstadoUbicacionLugar.sinUbicacion},
      sesion: sesionPrueba,
    );

    expect(boton, findsNothing);
  });
}
