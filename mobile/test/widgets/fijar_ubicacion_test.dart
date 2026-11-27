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
import 'package:traza_autos/features/viajes/presentation/fijar_ubicacion.dart';
import 'package:traza_autos/features/viajes/providers.dart';

import '../support/arnes.dart';
import '../support/dobles_viajes.dart';

/// S6-HU-17.4: «fijar ubicación aquí» en el punto de entrega del conductor; S8-HU-09.2 CA-3: también la inspectora
/// en la parada que tomó (el cargue reclamado se prueba en pendientes_test.dart), y nunca el coordinador ni el
/// administrador.

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
    expect(find.widgetWithText(FilledButton, 'Entregar en esta parada'), findsOneWidget);

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
    expect(find.widgetWithText(FilledButton, 'Entregar en esta parada'), findsOneWidget);
  });

  // La lista de paradas es la del conductor: el inspector fija desde la parada que tomó (S8-HU-09.2 CA-3).
  testWidgets('en la lista de paradas del conductor, el inspector no ve el botón', (tester) async {
    await abrir(
      tester,
      LugaresRepositorioFalso()..estadosPorLugar = {'l-Autonorte': EstadoUbicacionLugar.sinUbicacion},
      sesion: sesionPrueba,
    );

    expect(boton, findsNothing);
  });

  // S8-HU-09.2 CA-3 y ESC-07: la inspectora que tomó la parada está en el lugar y deja su ubicación con la precisión.
  testWidgets('la inspectora que tomó la parada fija la ubicación con la precisión del GPS', (tester) async {
    final lugares = LugaresRepositorioFalso()..estadosPorLugar = {'l-Patio de Prueba': EstadoUbicacionLugar.sinUbicacion};
    final arnes = await abrirParada(tester, lugares, sesion: sesionPrueba);
    arnes.ubicacion.posicion = const Posicion(6.1706, -75.5874, precision: 12);

    await tester.tap(boton);
    await avanzar(tester);

    expect(lugares.fijados.single.$1, 'l-Patio de Prueba');
    expect(lugares.fijados.single.$2.precision, 12);
    expect(find.text('Ubicación pendiente de confirmar'), findsOneWidget);
  });

  testWidgets('en la parada, si otro ya la fijó (409), el botón desaparece sin error', (tester) async {
    final lugares = LugaresRepositorioFalso()
      ..estadosPorLugar = {'l-Patio de Prueba': EstadoUbicacionLugar.sinUbicacion}
      ..errorAlFijar = const ErrorApi(message: 'El lugar ya tiene ubicación', code: 'CONFLICT', status: 409);
    await abrirParada(tester, lugares, sesion: sesionPrueba);
    lugares.estadosPorLugar = {'l-Patio de Prueba': EstadoUbicacionLugar.pendienteConfirmar};

    await tester.tap(boton);
    await avanzar(tester);

    expect(boton, findsNothing);
    expect(find.textContaining('ya tiene ubicación'), findsNothing);
  });

  // ESC-08: Ana y Luis lo ven; Carlos (coordinador) y Jorge (administrador) no, aunque el lugar no tenga ubicación.
  group('quién ve «Fijar ubicación aquí»', () {
    Future<LugaresRepositorioFalso> abrirSolo(WidgetTester tester, String rol) async {
      final lugares = LugaresRepositorioFalso()..estadosPorLugar = {'l-1': EstadoUbicacionLugar.sinUbicacion};
      final sesion = Sesion(token: 'jwt', usuario: Usuario(id: 'u-$rol', nombre: rol, email: '', rol: rol));
      await tester.pumpWidget(
        ProviderScope(
          retry: (_, _) => null,
          overrides: [...Arnes(sesion: sesion).overrides, lugaresRepositorioProvider.overrideWithValue(lugares)],
          child: MaterialApp(
            theme: crearTema(),
            home: const Scaffold(body: _ConSesion(child: FijarUbicacionLugar(lugarId: 'l-1'))),
          ),
        ),
      );
      await avanzar(tester);
      return lugares;
    }

    for (final rol in ['inspector', 'conductor']) {
      testWidgets('$rol: sí', (tester) async {
        await abrirSolo(tester, rol);
        expect(boton, findsOneWidget);
      });
    }

    for (final rol in ['coordinador_calidad', 'admin', 'super_admin', 'comercial']) {
      testWidgets('$rol: no', (tester) async {
        final lugares = await abrirSolo(tester, rol);
        expect(find.byType(FijarUbicacionLugar), findsOneWidget, reason: 'la pantalla sí lo pone');
        expect(boton, findsNothing);
        expect(lugares.consultas, 0, reason: 'ni siquiera pregunta por el estado de los lugares');
      });
    }

    test('la regla', () {
      expect(['inspector', 'conductor'].every(puedeFijarUbicacion), isTrue);
      expect(['coordinador_calidad', 'admin', 'super_admin', 'comercial', null].any(puedeFijarUbicacion), isFalse);
    });
  });
}

/// Espera a que la sesión esté cargada antes de pintar [child], como hace la app.
class _ConSesion extends ConsumerWidget {
  const _ConSesion({required this.child});

  final Widget child;

  @override
  Widget build(BuildContext context, WidgetRef ref) =>
      ref.watch(authControllerProvider.select((e) => e.fase)) == FaseAuth.autenticado ? child : const SizedBox();
}

/// La parada que tomó la inspectora: «Entregar en esta parada» abre esta pantalla.
Future<Arnes> abrirParada(WidgetTester tester, LugaresRepositorioFalso lugares, {required Sesion sesion}) async {
  await tester.binding.setSurfaceSize(const Size(600, 2400));
  addTearDown(() => tester.binding.setSurfaceSize(null));
  final arnes = Arnes(sesion: sesion);
  await tester.pumpWidget(
    ProviderScope(
      retry: (_, _) => null,
      overrides: [
        ...arnes.overrides,
        entregasRepositorioProvider.overrideWithValue(
          EntregasRepositorioFalso()..puntos = [puntoDe('Patio de Prueba', receptorNombre: sesion.usuario.nombre)],
        ),
        lugaresRepositorioProvider.overrideWithValue(lugares),
      ],
      child: MaterialApp(
        theme: crearTema(),
        home: const _ConSesion(child: EntregaParadaPage(viajeId: 'v1', lugarId: 'l-Patio de Prueba')),
      ),
    ),
  );
  await avanzar(tester);
  return arnes;
}
