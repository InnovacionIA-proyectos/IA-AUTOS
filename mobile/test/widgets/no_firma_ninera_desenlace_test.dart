import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/core/tema/tema.dart';
import 'package:traza_autos/features/auth/domain/sesion.dart';
import 'package:traza_autos/features/auth/presentation/auth_controller.dart';
import 'package:traza_autos/features/custodia/presentation/no_firma_ninera_page.dart';
import 'package:traza_autos/features/custodia/providers.dart';
import 'package:traza_autos/features/inspecciones/domain/foto_local.dart';
import 'package:traza_autos/features/inspecciones/domain/inspeccion_resumen.dart';
import 'package:traza_autos/features/viajes/domain/viaje.dart';

import '../support/arnes.dart';
import '../support/dobles_custodia.dart';

/// S8-HU-11.2 CA-8: la constancia de no firma de la niñera dice lo que pasó con SU envío: registrada, en cola o
/// rechazada con el motivo del servidor y «Reintentar».

const _ana = Sesion(token: 'jwt', usuario: ana);

const _viaje = Viaje(
  id: 'v1',
  codigoMtrack: 'MTK-1',
  placa: 'ABC123',
  estado: 'en_curso',
  inspectorId: 'u1',
  inspectorNombre: 'Ana Pérez',
  conductorId: 'u2',
  conductorNombre: 'Luis Gómez',
  vehiculos: [
    ViajeVehiculo(vin: 'VINA0000000000001', posicion: 1),
    ViajeVehiculo(vin: 'VINB0000000000002', posicion: 2),
  ],
);

const _foto = FotoLocal(ruta: '/tmp/constancia.jpg', selloFecha: '2026-11-23T12:00:00Z');

Future<void> avanzar(WidgetTester tester) async {
  for (var i = 0; i < 12; i++) {
    await tester.pump(const Duration(milliseconds: 50));
  }
}

/// Abre la constancia desde otra pantalla, como en la app: al terminar, la constancia se cierra sola.
Future<Arnes> abrir(WidgetTester tester, {Arnes? arnes}) async {
  await tester.binding.setSurfaceSize(const Size(600, 2000));
  addTearDown(() => tester.binding.setSurfaceSize(null));
  final a = arnes ?? Arnes(sesion: _ana);
  a.archivos.existentes.add(_foto.ruta);
  // El cargue completo de los dos carros: sin él, la constancia tiene el mismo bloqueo que la firma (CA-6).
  a.inspecciones.delViajeResultado = [
    for (final vin in ['VINA0000000000001', 'VINB0000000000002'])
      for (final momento in ['llegada', 'salida'])
        InspeccionResumen(id: '$vin-$momento', vin: vin, momento: momento, momentoNombre: momento, fecha: '', estado: 'cerrada'),
  ];
  await tester.pumpWidget(
    ProviderScope(
      retry: (_, _) => null,
      overrides: [...a.overrides, usuariosRepositorioProvider.overrideWithValue(UsuariosRepositorioFalso())],
      child: MaterialApp(
        theme: crearTema(),
        home: Consumer(
          builder: (_, ref, _) => ref.watch(authControllerProvider.select((e) => e.fase)) == FaseAuth.autenticado
              ? Builder(
                  builder: (contexto) => Scaffold(
                    body: TextButton(
                      onPressed: () => Navigator.of(contexto).push<void>(
                        MaterialPageRoute(
                          builder: (_) => NoFirmaNineraPage(viaje: _viaje, parteQueSeNego: 'recibe', fotoDePrueba: _foto),
                        ),
                      ),
                      child: const Text('Abrir constancia'),
                    ),
                  ),
                )
              : const SizedBox(),
        ),
      ),
    ),
  );
  await avanzar(tester);
  await tester.tap(find.text('Abrir constancia'));
  await avanzar(tester);
  return a;
}

Future<void> registrar(WidgetTester tester) async {
  await tester.enterText(find.byType(TextField), 'Se niega a firmar el recibo');
  await tester.tap(find.widgetWithText(ChoiceChip, marta.nombre));
  await tester.pump();
  await tester.tap(find.widgetWithText(FilledButton, 'Registrar constancia de la niñera'));
  await avanzar(tester);
}

void main() {
  testWidgets('aceptada, dice que se registró y que la entrega del cargue quedó cerrada', (tester) async {
    final arnes = await abrir(tester);

    await registrar(tester);

    expect(arnes.gateway.constanciasNinera.single.viajeId, 'v1');
    expect(find.text('Constancia de la niñera registrada'), findsOneWidget);
    expect(find.textContaining('los 2 carros quedó cerrada con la constancia'), findsOneWidget);
  });

  testWidgets('sin señal dice que quedó en cola y no que se registró', (tester) async {
    final arnes = Arnes(sesion: _ana)..red.estable = false;
    await abrir(tester, arnes: arnes);

    await registrar(tester);

    expect(arnes.gateway.constanciasNinera, isEmpty);
    expect(find.text('Quedó en cola: se enviará cuando haya señal'), findsOneWidget);
    expect(find.text('Constancia de la niñera registrada'), findsNothing);
  });

  testWidgets('rechazada, dice el motivo del servidor y deja reintentar desde ahí', (tester) async {
    final arnes = Arnes(sesion: _ana);
    arnes.gateway.errores['registrarNoFirmaNinera'] = [errorApi(409, 'Falta la salida del cargue del VIN VINB0000000000002')];
    await abrir(tester, arnes: arnes);

    await registrar(tester);

    expect(find.text('No se pudo registrar la constancia'), findsOneWidget);
    expect(find.textContaining('Falta la salida del cargue del VIN VINB0000000000002'), findsOneWidget);

    await tester.tap(find.widgetWithText(TextButton, 'Reintentar'));
    await avanzar(tester);

    expect(find.text('Constancia de la niñera registrada'), findsOneWidget);
    expect(arnes.cola.jobs, isEmpty, reason: 'salió: ya no queda en la cola');
  });
}
