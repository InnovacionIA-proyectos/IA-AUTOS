import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/core/dispositivo/servicios.dart';
import 'package:traza_autos/core/tema/tema.dart';
import 'package:traza_autos/features/auth/domain/sesion.dart';
import 'package:traza_autos/features/auth/presentation/auth_controller.dart';
import 'package:traza_autos/features/catalogos/domain/catalogos.dart';
import 'package:traza_autos/core/errores/error_api.dart';
import 'package:traza_autos/features/vehiculos/data/vehiculos_repositorio.dart';
import 'package:traza_autos/features/vehiculos/domain/inventario.dart';
import 'package:traza_autos/features/vehiculos/providers.dart';
import 'package:traza_autos/features/viajes/domain/viaje.dart';
import 'package:traza_autos/features/viajes/presentation/acciones_conductor.dart';
import 'package:traza_autos/features/viajes/presentation/mis_viajes_page.dart';
import 'package:traza_autos/features/viajes/providers.dart';

import '../support/arnes.dart';
import '../support/dobles_viajes.dart';

/// S6-HU-02.4: lo que el conductor hace con cada unidad de su viaje en ruta.

const _conductor = Sesion(
  token: 'jwt',
  usuario: Usuario(id: 'conductor-1', nombre: 'Luis Gómez', email: '', rol: 'conductor'),
);

Viaje _viaje({String estado = 'en_curso', List<ViajeVehiculo>? unidades}) => Viaje(
      id: 'v1',
      codigoMtrack: 'MTK-1',
      placa: 'ABC123',
      estado: estado,
      vehiculos: unidades ??
          const [
            ViajeVehiculo(vin: 'VINSINREGISTRO0001'),
            ViajeVehiculo(vin: 'VINCONNOVEDAD0002', estadoTransporte: EstadoTransporte.conNovedad, novedades: 2),
          ],
    );

Future<Arnes> abrir(WidgetTester tester, Viaje viaje, {Sesion sesion = _conductor, Arnes? arnes}) async {
  await tester.binding.setSurfaceSize(const Size(600, 3000));
  addTearDown(() => tester.binding.setSurfaceSize(null));
  final usado = arnes ?? Arnes(sesion: sesion);
  final viajes = ViajesRepositorioFalso()..remotos = [viaje];
  await tester.pumpWidget(
    ProviderScope(
      retry: (_, _) => null,
      overrides: [
        ...usado.overrides,
        viajesRepositorioProvider.overrideWithValue(viajes),
        // El conductor baja el inventario de cada unidad al abrir; aquí no hay servidor.
        vehiculosRepositorioProvider.overrideWithValue(_SinInventario()),
        reclamosRepositorioProvider.overrideWithValue(ReclamosRepositorioFalso()),
      ],
      child: MaterialApp(theme: crearTema(), home: const _Arranque()),
    ),
  );
  await avanzar(tester);
  return usado;
}

class _SinInventario implements VehiculosRepositorio {
  @override
  Future<Inventario> inventario(String vin) async => throw const ErrorApi.red();
}

class _Arranque extends ConsumerWidget {
  const _Arranque();

  @override
  Widget build(BuildContext context, WidgetRef ref) =>
      ref.watch(authControllerProvider.select((e) => e.fase)) == FaseAuth.autenticado ? const MisViajesPage() : const SizedBox();
}

Future<void> avanzar(WidgetTester tester) async {
  for (var i = 0; i < 12; i++) {
    await tester.pump(const Duration(milliseconds: 50));
  }
}

void main() {
  group('lo que el conductor ve de cada unidad', () {
    testWidgets('el estado de transporte, cuántas novedades y las dos acciones', (tester) async {
      await abrir(tester, _viaje());

      expect(find.text('Sin registro'), findsOneWidget);
      expect(find.text('Con novedad'), findsOneWidget);
      expect(find.text('2 novedades'), findsOneWidget);
      expect(find.widgetWithText(OutlinedButton, 'Reportar novedad'), findsNWidgets(2));
      expect(find.widgetWithText(FilledButton, 'Llegué'), findsNWidgets(2));
    });

    testWidgets('con la llegada confirmada ya no hay nada que hacer: dice cómo quedó', (tester) async {
      await abrir(
        tester,
        _viaje(unidades: const [
          ViajeVehiculo(vin: 'VINSINNOVEDAD00001', estadoTransporte: EstadoTransporte.sinNovedad),
          ViajeVehiculo(vin: 'VINLLEGADACON00002', estadoTransporte: EstadoTransporte.llegadaConNovedad, novedades: 1),
        ]),
      );

      expect(find.text('Sin novedad'), findsOneWidget);
      expect(find.text('Llegada con novedad'), findsOneWidget);
      expect(find.widgetWithText(FilledButton, 'Llegué'), findsNothing);
      expect(find.widgetWithText(OutlinedButton, 'Reportar novedad'), findsNothing);
    });

    testWidgets('un viaje programado también: la primera novedad es la que lo pone en curso', (tester) async {
      await abrir(tester, _viaje(estado: 'programado'));

      expect(find.widgetWithText(OutlinedButton, 'Reportar novedad'), findsNWidgets(2));
    });

    testWidgets('un viaje terminado no ofrece nada', (tester) async {
      await abrir(tester, _viaje(estado: 'finalizado'));

      expect(find.widgetWithText(FilledButton, 'Llegué'), findsNothing);
    });

    testWidgets('una unidad que no sale no ofrece nada', (tester) async {
      await abrir(tester, _viaje(unidades: const [ViajeVehiculo(vin: 'VINNOSALE00000001', estadoUnidad: 'no_sale')]));

      expect(find.widgetWithText(FilledButton, 'Llegué'), findsNothing);
    });

    testWidgets('el inspector no ve estas acciones: son del conductor', (tester) async {
      await abrir(tester, _viaje(), sesion: sesionPrueba);

      expect(find.widgetWithText(FilledButton, 'Llegué'), findsNothing);
      expect(find.widgetWithText(OutlinedButton, 'Reportar novedad'), findsNothing);
    });
  });

  group('«Llegué»', () {
    testWidgets('sin GPS no se habilita y dice que espere la señal', (tester) async {
      final arnes = Arnes(sesion: _conductor)..ubicacion.error = const UbicacionNoDisponible('GPS apagado');
      await abrir(tester, _viaje(), arnes: arnes);

      expect(find.text('Sin ubicación: espera la señal del GPS'), findsNWidgets(2));
      final boton = tester.widget<FilledButton>(find.widgetWithText(FilledButton, 'Llegué').first);
      expect(boton.onPressed, isNull);
    });

    testWidgets('una unidad sin novedades confirma «Sin novedad» y sale con el lugar de ese momento', (tester) async {
      final arnes = Arnes(sesion: _conductor)..ubicacion.posicion = const Posicion(4.9, -74.4);
      await abrir(tester, _viaje(), arnes: arnes);

      await tester.tap(find.widgetWithText(FilledButton, 'Llegué').first);
      await avanzar(tester);
      expect(find.text('Se confirmará «Sin novedad».'), findsOneWidget);
      await tester.tap(find.widgetWithText(TextButton, 'Confirmar'));
      await avanzar(tester);

      final llegada = arnes.gateway.llegadasEnviadas.single;
      expect((llegada.$1, llegada.$2, llegada.$4, llegada.$5), ('v1', 'VINSINREGISTRO0001', 4.9, -74.4));
      expect(DateTime.tryParse(llegada.$3), isNotNull);
    });

    testWidgets('una unidad con novedades confirma «Llegada con novedad»', (tester) async {
      final arnes = Arnes(sesion: _conductor);
      await abrir(tester, _viaje(), arnes: arnes);

      await tester.tap(find.widgetWithText(FilledButton, 'Llegué').last);
      await avanzar(tester);

      expect(find.textContaining('Se confirmará «Llegada con novedad»'), findsOneWidget);
      expect(find.textContaining('2 novedades'), findsWidgets);
    });

    testWidgets('si se cancela no sale nada', (tester) async {
      final arnes = Arnes(sesion: _conductor);
      await abrir(tester, _viaje(), arnes: arnes);

      await tester.tap(find.widgetWithText(FilledButton, 'Llegué').first);
      await avanzar(tester);
      await tester.tap(find.widgetWithText(TextButton, 'Cancelar'));
      await avanzar(tester);

      expect(arnes.gateway.llegadasEnviadas, isEmpty);
      expect(arnes.cola.jobs, isEmpty);
    });

    testWidgets('sin señal queda en la cola, a nombre del conductor, con el lugar del momento', (tester) async {
      final arnes = Arnes(sesion: _conductor)
        ..red.estable = false
        ..ubicacion.posicion = const Posicion(4.9, -74.4);
      await abrir(tester, _viaje(), arnes: arnes);

      await tester.tap(find.widgetWithText(FilledButton, 'Llegué').first);
      await avanzar(tester);
      await tester.tap(find.widgetWithText(TextButton, 'Confirmar'));
      await avanzar(tester);

      final job = arnes.cola.jobs.single;
      expect((job.usuarioId, job.fallido), ('conductor-1', false));
      expect(arnes.gateway.llegadasEnviadas, isEmpty);
    });
  });

  group('la hoja de la novedad', () {
    ({String tipo, String tamano})? resultado;

    Future<void> abrirHoja(WidgetTester tester) async {
      resultado = null;
      await tester.pumpWidget(
        MaterialApp(
          theme: crearTema(),
          home: Builder(
            builder: (contexto) => Scaffold(
              body: FilledButton(
                onPressed: () async {
                  resultado = await pedirDatosNovedad(
                    contexto,
                    tamanos: const [
                      ValorCatalogo({'id': 't1', 'codigo': '1_5cm', 'nombre': '1 a 5 cm', 'orden': 1, 'activo': true}),
                      ValorCatalogo({'id': 't2', 'codigo': 'mas_20cm', 'nombre': 'Más de 20 cm', 'orden': 2, 'activo': true}),
                    ],
                  );
                },
                child: const Text('abrir'),
              ),
            ),
          ),
        ),
      );
      await tester.tap(find.text('abrir'));
      await avanzar(tester);
    }

    testWidgets('el tipo y el tamaño se eligen de listas y sin los dos no se guarda', (tester) async {
      await abrirHoja(tester);

      expect(find.byType(TextField), findsNothing, reason: 'no hay texto libre');
      for (final tipo in ['Golpe', 'Piedra', 'Granizo', 'Incidente vial']) {
        expect(find.text(tipo), findsOneWidget);
      }
      expect(find.text('1 a 5 cm'), findsOneWidget);
      expect(tester.widget<FilledButton>(find.widgetWithText(FilledButton, 'Guardar novedad')).onPressed, isNull);

      await tester.tap(find.text('Granizo'));
      await tester.pump();
      expect(tester.widget<FilledButton>(find.widgetWithText(FilledButton, 'Guardar novedad')).onPressed, isNull);

      await tester.tap(find.text('Más de 20 cm'));
      await tester.pump();
      expect(tester.widget<FilledButton>(find.widgetWithText(FilledButton, 'Guardar novedad')).onPressed, isNotNull);
    });

    testWidgets('devuelve lo elegido', (tester) async {
      await abrirHoja(tester);

      await tester.tap(find.text('Piedra'));
      await tester.tap(find.text('1 a 5 cm'));
      await tester.pump();
      await tester.tap(find.widgetWithText(FilledButton, 'Guardar novedad'));
      await avanzar(tester);

      expect((resultado?.tipo, resultado?.tamano), ('piedra', '1_5cm'));
    });
  });
}
