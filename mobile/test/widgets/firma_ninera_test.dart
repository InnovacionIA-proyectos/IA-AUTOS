import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/core/dispositivo/servicios.dart';
import 'package:traza_autos/core/tema/tema.dart';
import 'package:traza_autos/features/auth/domain/sesion.dart';
import 'package:traza_autos/features/auth/presentation/auth_controller.dart';
import 'package:traza_autos/features/custodia/presentation/firma_ninera_page.dart';
import 'package:traza_autos/features/custodia/presentation/lienzo_firma.dart';
import 'package:traza_autos/features/custodia/providers.dart';
import 'package:traza_autos/features/inspecciones/domain/inspeccion_resumen.dart';
import 'package:traza_autos/features/viajes/domain/viaje.dart';
import 'package:traza_autos/features/viajes/providers.dart';

import '../support/arnes.dart';
import '../support/dobles_custodia.dart';
import '../support/dobles_viajes.dart';

/// S8-HU-01.3: la pantalla de la firma de toda la niñera, una vez por parte, con reservas y constancia.

const _ana = Sesion(token: 'jwt', usuario: ana);
const _luis = Sesion(token: 'jwt', usuario: luis);

/// Ana (u1) inspecciona y Luis (u2) conduce. VINX no sale y no se firma.
Viaje _viaje({bool cerrado = false}) => Viaje(
      id: 'v1',
      codigoMtrack: 'MTK-1',
      placa: 'ABC123',
      estado: 'en_curso',
      inspectorId: ana.id,
      inspectorNombre: ana.nombre,
      conductorId: luis.id,
      conductorNombre: luis.nombre,
      cargueCerrado: cerrado,
      vehiculos: const [
        ViajeVehiculo(vin: 'VINA0000000000001', posicion: 1),
        ViajeVehiculo(vin: 'VINB0000000000002', posicion: 2),
        ViajeVehiculo(vin: 'VINX0000000000009', estadoUnidad: 'no_sale'),
      ],
    );

List<InspeccionResumen> _cargueCompleto({bool sinSalidaDeB = false}) => [
      for (final vin in ['VINA0000000000001', 'VINB0000000000002'])
        for (final momento in ['llegada', 'salida'])
          if (!(sinSalidaDeB && vin == 'VINB0000000000002' && momento == 'salida'))
            InspeccionResumen(id: '$vin-$momento', vin: vin, momento: momento, momentoNombre: momento, fecha: '', estado: 'cerrada'),
    ];

Future<void> avanzar(WidgetTester tester) async {
  for (var i = 0; i < 12; i++) {
    await tester.pump(const Duration(milliseconds: 50));
  }
}

Future<Arnes> abrir(WidgetTester tester, {Sesion sesion = _ana, Viaje? viaje, List<InspeccionResumen>? informes}) async {
  await tester.binding.setSurfaceSize(const Size(600, 2200));
  addTearDown(() => tester.binding.setSurfaceSize(null));
  final arnes = Arnes(sesion: sesion);
  arnes.inspecciones.delViajeResultado = informes ?? _cargueCompleto();
  await tester.pumpWidget(
    ProviderScope(
      retry: (_, _) => null,
      overrides: [
        ...arnes.overrides,
        viajesRepositorioProvider.overrideWithValue(ViajesRepositorioFalso()..viajeDeEtapa = viaje ?? _viaje()),
        usuariosRepositorioProvider.overrideWithValue(UsuariosRepositorioFalso()),
      ],
      child: MaterialApp(
        theme: crearTema(),
        home: Consumer(
          builder: (_, ref, _) => ref.watch(authControllerProvider.select((e) => e.fase)) == FaseAuth.autenticado
              ? const FirmaNineraPage(viajeId: 'v1')
              : const SizedBox(),
        ),
      ),
    ),
  );
  await avanzar(tester);
  return arnes;
}

Future<void> trazar(WidgetTester tester) async {
  final lienzo = tester.getBottomLeft(find.byType(LienzoFirma)) + const Offset(60, -80);
  await tester.dragFrom(lienzo, const Offset(40, 30));
  await tester.pump();
}

FilledButton boton(WidgetTester tester, String texto) => tester.widget<FilledButton>(find.widgetWithText(FilledButton, texto));

void main() {
  // CA-5 y ESC-02.
  testWidgets('el inspector firma una vez la entrega de toda la niñera, con su GPS', (tester) async {
    final arnes = await abrir(tester);

    expect(find.textContaining('para los 2 carros'), findsOneWidget, reason: 'el que no sale no cuenta');
    expect(find.text('VINX0000000000009'), findsNothing);
    expect(boton(tester, 'Firmar la entrega de la niñera').onPressed, isNull, reason: 'sin trazo no hay firma');

    await trazar(tester);
    await tester.tap(find.widgetWithText(FilledButton, 'Firmar la entrega de la niñera'));
    await avanzar(tester);

    final firma = arnes.gateway.firmasNinera.single;
    expect((firma.viajeId, firma.parte, firma.lat, firma.lng), ('v1', 'entrega', 6.2, -75.5));
    expect(firma.reservas, isEmpty);
    expect(find.text('Firma de la niñera registrada'), findsOneWidget);
    expect(find.text('Quedó para los 2 carros de la niñera.'), findsOneWidget);
  });

  // CA-6 y ESC-03.
  testWidgets('mientras falte la salida de un carro, firmar sale gris con lo que falta', (tester) async {
    await abrir(tester, informes: _cargueCompleto(sinSalidaDeB: true));

    await trazar(tester);

    expect(find.text('Falta la salida del cargue del VIN VINB0000000000002'), findsOneWidget);
    expect(boton(tester, 'Firmar la entrega de la niñera').onPressed, isNull);
    final constancia = tester.widget<OutlinedButton>(find.widgetWithText(OutlinedButton, 'El conductor se negó a firmar'));
    expect(constancia.onPressed, isNull, reason: 'la constancia tiene el mismo bloqueo');
  });

  // CA-13.
  testWidgets('el conductor recibe con reservas en un carro y viajan con su firma', (tester) async {
    final arnes = await abrir(tester, sesion: _luis);

    await tester.tap(find.text('Recibir con reservas').first);
    await avanzar(tester);
    final guardar = find.widgetWithText(TextButton, 'Guardar');
    expect(tester.widget<TextButton>(guardar).onPressed, isNull, reason: 'sin texto no deja seguir');
    await tester.enterText(find.byType(TextField).last, 'Rayón en la puerta izquierda');
    await tester.pump();
    await tester.tap(guardar);
    await avanzar(tester);

    expect(find.text('Con reservas: Rayón en la puerta izquierda'), findsOneWidget);

    await trazar(tester);
    await tester.tap(find.widgetWithText(FilledButton, 'Firmar el recibo de la niñera'));
    await avanzar(tester);

    final firma = arnes.gateway.firmasNinera.single;
    expect(firma.parte, 'recibe');
    expect(firma.reservas.single.vin, 'VINA0000000000001');
    expect(firma.reservas.single.observaciones, 'Rayón en la puerta izquierda');
  });

  testWidgets('el inspector no deja reservas', (tester) async {
    await abrir(tester);

    expect(find.text('Recibir con reservas'), findsNothing);
  });

  testWidgets('sin GPS no firma y lo dice', (tester) async {
    final arnes = await abrir(tester);
    arnes.ubicacion.error = const UbicacionNoDisponible('GPS apagado');

    await trazar(tester);
    await tester.tap(find.widgetWithText(FilledButton, 'Firmar la entrega de la niñera'));
    await avanzar(tester);

    expect(find.text('Sin ubicación no se puede firmar: activa el GPS e inténtalo de nuevo.'), findsOneWidget);
    expect(arnes.cola.jobs, isEmpty);
  });

  testWidgets('quien no es del viaje no firma', (tester) async {
    await abrir(tester, sesion: const Sesion(token: 'jwt', usuario: marta));

    expect(find.textContaining('La niñera la firman el inspector y el conductor del viaje'), findsOneWidget);
    expect(find.byType(LienzoFirma), findsNothing);
  });

  testWidgets('con la entrega del cargue cerrada ya no se firma', (tester) async {
    await abrir(tester, viaje: _viaje(cerrado: true));

    expect(find.text('La entrega del cargue de esta niñera ya está cerrada.'), findsOneWidget);
    expect(find.byType(LienzoFirma), findsNothing);
  });

  // CA-14.
  testWidgets('la constancia es de toda la niñera y pide motivo, testigo y foto', (tester) async {
    await abrir(tester);

    await tester.tap(find.widgetWithText(OutlinedButton, 'El conductor se negó a firmar'));
    await avanzar(tester);

    expect(find.textContaining('Luis Gómez se negó a firmar. La constancia es para toda la niñera'), findsOneWidget);
    // El testigo es una tercera persona: ni el inspector ni el conductor del viaje.
    expect(find.widgetWithText(ChoiceChip, marta.nombre), findsOneWidget);
    expect(find.widgetWithText(ChoiceChip, ana.nombre), findsNothing);
    expect(find.widgetWithText(ChoiceChip, luis.nombre), findsNothing);
    expect(find.text('Falta el motivo, el testigo, la foto.'), findsOneWidget);

    await tester.enterText(find.byType(TextField), 'Se niega a firmar el recibo');
    await tester.tap(find.widgetWithText(ChoiceChip, marta.nombre));
    await tester.pump();

    expect(find.text('Falta la foto.'), findsOneWidget);
    expect(boton(tester, 'Registrar constancia de la niñera').onPressed, isNull);
  });
}
