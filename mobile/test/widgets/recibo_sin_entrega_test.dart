import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/core/tema/tema.dart';
import 'package:traza_autos/features/auth/domain/sesion.dart';
import 'package:traza_autos/features/auth/presentation/auth_controller.dart';
import 'package:traza_autos/features/custodia/presentation/lienzo_firma.dart';
import 'package:traza_autos/features/custodia/presentation/recibo_sin_entrega_page.dart';
import 'package:traza_autos/features/sync/domain/job.dart';

import '../support/arnes.dart';

/// S8-HU-11.2 CA-7: el recibo sin entrega dice lo que pasó con SU envío, no con la cola entera.

const _luis = Sesion(token: 'jwt', usuario: Usuario(id: 'luis', nombre: 'Luis Gómez', email: '', rol: 'conductor'));

Future<void> avanzar(WidgetTester tester) async {
  for (var i = 0; i < 12; i++) {
    await tester.pump(const Duration(milliseconds: 50));
  }
}

Future<Arnes> abrir(WidgetTester tester, {Arnes? arnes}) async {
  await tester.binding.setSurfaceSize(const Size(600, 1600));
  addTearDown(() => tester.binding.setSurfaceSize(null));
  final a = arnes ?? Arnes(sesion: _luis);
  await tester.pumpWidget(
    ProviderScope(
      retry: (_, _) => null,
      overrides: a.overrides,
      child: MaterialApp(
        theme: crearTema(),
        home: Consumer(
          builder: (_, ref, _) => ref.watch(authControllerProvider.select((e) => e.fase)) == FaseAuth.autenticado
              ? const ReciboSinEntregaPage(vin: '9BWZZZ377VT004251', inspeccionId: 'salida-1')
              : const SizedBox(),
        ),
      ),
    ),
  );
  await avanzar(tester);
  return a;
}

Future<void> firmarYRegistrar(WidgetTester tester) async {
  final lienzo = tester.getBottomLeft(find.byType(LienzoFirma)) + const Offset(60, -80);
  await tester.dragFrom(lienzo, const Offset(40, 30));
  await tester.pump();
  await tester.tap(find.widgetWithText(FilledButton, 'Firmar recibo sin entrega'));
  await avanzar(tester);
}

/// Un envío de otro carro que el servidor ya rechazó y espera a una persona.
Job _fotoRechazadaDeOtroCarro() => const ReciboSinEntregaJob(
      id: 'otro',
      etiqueta: 'Foto 9BWZZZ377VT004250',
      creadoEn: 0,
      usuarioId: 'luis',
      inspeccionId: 'otra-salida',
      firma: 'x',
      error: 'La foto no es válida',
      fallido: true,
    );

void main() {
  // ESC-08.
  testWidgets('aceptado, dice «registrado» aunque otro envío de la cola esté rechazado', (tester) async {
    final arnes = Arnes(sesion: _luis);
    await arnes.cola.agregar(_fotoRechazadaDeOtroCarro());
    await abrir(tester, arnes: arnes);

    await firmarYRegistrar(tester);

    expect(arnes.gateway.recibosSinEntrega.single.$1, 'salida-1');
    expect(find.text('Recibo sin entrega registrado'), findsOneWidget);
    expect(find.text('No se pudo registrar el recibo'), findsNothing);
    expect(find.textContaining('rechazó'), findsNothing);
  });

  testWidgets('sin señal dice que quedó en cola y no que se registró', (tester) async {
    final arnes = Arnes(sesion: _luis)..red.estable = false;
    await abrir(tester, arnes: arnes);

    await firmarYRegistrar(tester);

    expect(arnes.gateway.recibosSinEntrega, isEmpty);
    expect(find.text('Quedó en cola: se enviará cuando haya señal'), findsOneWidget);
    expect(find.text('Recibo sin entrega registrado'), findsNothing);
  });

  testWidgets('con el servidor fallando por algo pasajero, dice que no salió y cuándo se reintenta', (tester) async {
    final arnes = Arnes(sesion: _luis);
    arnes.gateway.errores['firmarReciboSinEntrega'] = [errorApi(503, 'Servicio no disponible')];
    await abrir(tester, arnes: arnes);

    await firmarYRegistrar(tester);

    expect(find.text('Quedó en cola: todavía no se envió'), findsOneWidget);
    expect(find.textContaining('Se reintenta solo en'), findsOneWidget);
    expect(find.text('Recibo sin entrega registrado'), findsNothing);
    expect(find.widgetWithText(TextButton, 'Reintentar'), findsNothing, reason: 'se reintenta solo');
  });

  testWidgets('rechazado, dice el motivo del servidor y deja reintentar desde ahí', (tester) async {
    final arnes = Arnes(sesion: _luis);
    arnes.gateway.errores['firmarReciboSinEntrega'] = [errorApi(409, 'La salida no se hizo sin inspector')];
    await abrir(tester, arnes: arnes);

    await firmarYRegistrar(tester);

    expect(find.text('No se pudo registrar el recibo'), findsOneWidget);
    expect(find.textContaining('La salida no se hizo sin inspector'), findsOneWidget);
    expect(find.text('Recibo sin entrega registrado'), findsNothing);

    // El servidor ya no lo rechaza: «Reintentar» lo manda otra vez y el diálogo dice cómo quedó ahora.
    await tester.tap(find.widgetWithText(TextButton, 'Reintentar'));
    await avanzar(tester);

    expect(arnes.gateway.recibosSinEntrega, hasLength(2));
    expect(find.text('Recibo sin entrega registrado'), findsOneWidget);
    expect(find.text('No se pudo registrar el recibo'), findsNothing);
    expect(arnes.cola.jobs, isEmpty, reason: 'salió: ya no queda en la cola');
  });
}
