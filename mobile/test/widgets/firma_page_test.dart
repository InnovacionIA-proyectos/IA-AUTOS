import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/core/tema/tema.dart';
import 'package:traza_autos/features/custodia/presentation/firma_page.dart';
import 'package:traza_autos/features/custodia/presentation/lienzo_firma.dart';
import 'package:traza_autos/features/custodia/providers.dart';
import 'package:traza_autos/features/vehiculos/domain/inventario.dart';

import '../support/arnes.dart';
import '../support/dobles_custodia.dart';

/// Quién puede firmar una transferencia lo decide el viaje: su conductor y su inspector. Y una vez que alguien
/// firmó, las partes quedan fijas: el servidor responde 409 «los firmantes no se pueden cambiar», así que quien
/// firma de segundo hereda, no elige.

Future<(UsuariosRepositorioFalso, TransferenciasRepositorioFalso)> abrir(
  WidgetTester tester, {
  String? viajeId = 'viaje-1',
  TransferenciaVista? transferencia,
  Arnes? arnes,
}) async {
  // Alta a propósito: la pantalla es un ListView y en una de teléfono no construye el botón de abajo.
  await tester.binding.setSurfaceSize(const Size(600, 2000));
  addTearDown(() => tester.binding.setSurfaceSize(null));
  final usuarios = UsuariosRepositorioFalso();
  final transferencias = TransferenciasRepositorioFalso()..transferencia = transferencia;
  await tester.pumpWidget(
    ProviderScope(
      retry: (_, _) => null,
      overrides: [
        ...(arnes ?? Arnes()).overrides,
        usuariosRepositorioProvider.overrideWithValue(usuarios),
        transferenciasRepositorioProvider.overrideWithValue(transferencias),
      ],
      child: MaterialApp(
        theme: crearTema(),
        home: FirmaPage(
          vin: 'VIN00000000000001',
          inspeccionId: 'i1',
          proceso: 'Cargue',
          soloFirma: false,
          viajeId: viajeId,
        ),
      ),
    ),
  );
  for (var i = 0; i < 6; i++) {
    await tester.pump(const Duration(milliseconds: 50));
  }
  return (usuarios, transferencias);
}

void main() {
  testWidgets('ofrece solo a los firmantes del viaje', (tester) async {
    final (usuarios, _) = await abrir(tester);

    expect(usuarios.viajesConsultados, ['viaje-1']);
    expect(usuarios.listaCompletaPedida, isFalse, reason: 'la lista de usuarios no decide quién firma');
    expect(find.widgetWithText(ChoiceChip, 'Ana Pérez'), findsWidgets);
    expect(find.widgetWithText(ChoiceChip, 'Luis Gómez'), findsWidgets);
    expect(find.widgetWithText(ChoiceChip, 'Marta Ríos'), findsNothing, reason: 'no participa en el viaje');
  });

  // Sin viaje no hay de dónde sacar los firmantes. Antes que caer en la lista completa —que es el defecto—,
  // se dice qué pasa.
  testWidgets('una etapa sin viaje lo dice y no pregunta por nadie', (tester) async {
    final (usuarios, _) = await abrir(tester, viajeId: null);

    expect(find.text('Esta etapa no tiene viaje asociado'), findsOneWidget);
    expect(usuarios.viajesConsultados, isEmpty);
    expect(usuarios.listaCompletaPedida, isFalse);
  });

  // El defecto de fondo: quien firmaba de segundo recibía una propuesta hecha desde su propia sesión (yo
  // entrego, el otro recibe), que con las partes al revés es exactamente lo que el servidor rechaza con 409.
  group('cuando la transferencia ya existe', () {
    testWidgets('las partes se heredan y ya no se eligen', (tester) async {
      await abrir(tester, transferencia: mediaFirmada());

      expect(find.byType(ChoiceChip), findsNothing, reason: 'cambiarlas da 409');
      expect(find.text('Luis Gómez'), findsOneWidget);
      expect(find.text('Ana Pérez'), findsOneWidget);
      expect(find.textContaining('ya no se cambian'), findsOneWidget);
    });

    testWidgets('se ve quién firmó y quién falta', (tester) async {
      await abrir(tester, transferencia: mediaFirmada());

      expect(find.text('Ya firmó'), findsOneWidget);
      expect(find.text('Falta su firma'), findsOneWidget);
    });

    // La sesión de la prueba es Ana, que aquí es quien recibe: su lienzo sale, el de Luis no.
    // Repetir la propia firma lo permite el servidor y sirve para corregir un trazo malo, pero el botón no
    // puede decir «Registrar mi firma» a quien ya firmó: se lee como que la anterior no quedó.
    testWidgets('a quien ya firmó el botón le dice que va a volver a firmar', (tester) async {
      // Ana firmó como quien recibe; la sesión de la prueba es Ana.
      await abrir(
        tester,
        transferencia: TransferenciaVista(
          entregaId: luis.id,
          recibeId: ana.id,
          entregaNombre: luis.nombre,
          recibeNombre: ana.nombre,
          recibeFirmoEn: DateTime(2026, 10, 26, 9),
        ),
      );

      expect(find.text('Volver a firmar'), findsOneWidget);
      expect(find.text('Registrar mi firma'), findsNothing);
      expect(find.textContaining('ya quedó registrada'), findsOneWidget);
    });

    testWidgets('solo se dibuja el lienzo de quien tiene la sesión', (tester) async {
      await abrir(tester, transferencia: mediaFirmada());

      expect(find.text('Tu firma'), findsOneWidget);
      expect(find.text('Su firma se registra desde su propia sesión.'), findsOneWidget);
    });
  });

  // #117: quien recibe puede firmar dejando por escrito con qué no está conforme. La sesión de la prueba es
  // Ana, que en `mediaFirmada()` es quien recibe.
  group('recibo con reservas', () {
    /// Un trazo en el lienzo: sin firma no hay nada que registrar, con reservas o sin ellas. El lienzo son los
    /// 160 px de abajo del widget; el resto es su título y el botón de limpiar.
    Future<void> firmar(WidgetTester tester) async {
      final lienzo = tester.getBottomLeft(find.byType(LienzoFirma)) + const Offset(60, -80);
      await tester.dragFrom(lienzo, const Offset(40, 30));
      await tester.pump();
    }

    testWidgets('solo lo ofrece a quien recibe', (tester) async {
      await abrir(tester, transferencia: mediaFirmada());
      expect(find.text('Recibo con reservas'), findsOneWidget);

      // La misma transferencia al revés: ahora Ana entrega, y entregar no admite reservas.
      await abrir(
        tester,
        transferencia: TransferenciaVista(entregaId: ana.id, recibeId: luis.id, entregaNombre: ana.nombre, recibeNombre: luis.nombre),
      );
      expect(find.text('Recibo con reservas'), findsNothing);
    });

    testWidgets('sin observaciones no se puede registrar, y se dice por qué', (tester) async {
      await abrir(tester, transferencia: mediaFirmada());
      await firmar(tester);
      expect(tester.widget<FilledButton>(find.byType(FilledButton)).onPressed, isNotNull, reason: 'la firma sola sí vale');

      await tester.tap(find.byType(Switch));
      await tester.pump();

      expect(tester.widget<FilledButton>(find.byType(FilledButton)).onPressed, isNull);
      expect(find.textContaining('sin ellas no es un recibo con reservas'), findsOneWidget);
    });

    testWidgets('lo escrito llega a la API por el endpoint de reservas', (tester) async {
      final arnes = Arnes();
      await abrir(tester, transferencia: mediaFirmada(), arnes: arnes);
      await firmar(tester);
      await tester.tap(find.byType(Switch));
      await tester.pump();
      await tester.enterText(find.byType(TextField), 'Llega con un golpe en el capot');
      await tester.pump();

      await tester.tap(find.byType(FilledButton));
      for (var i = 0; i < 8; i++) {
        await tester.pump(const Duration(milliseconds: 50));
      }

      // La cola lo envía de inmediato porque hay señal, así que lo que queda es lo que recibió la API.
      expect(arnes.gateway.llamadas, ['recibirConReservas']);
      expect(arnes.gateway.reservasEnviadas, ['Llega con un golpe en el capot']);
      expect(arnes.gateway.firmasEnviadas.single, isNotNull, reason: 'las reservas no reemplazan la firma');
    });
  });
}
