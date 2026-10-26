import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/core/tema/tema.dart';
import 'package:traza_autos/features/auth/domain/sesion.dart';
import 'package:traza_autos/features/custodia/data/transferencias_repositorio.dart';
import 'package:traza_autos/features/custodia/data/usuarios_repositorio.dart';
import 'package:traza_autos/features/custodia/presentation/firma_page.dart';
import 'package:traza_autos/features/custodia/providers.dart';
import 'package:traza_autos/features/vehiculos/domain/inventario.dart';

import '../support/arnes.dart';

/// Quién puede firmar una transferencia lo decide el viaje: su conductor y su inspector. Y una vez que alguien
/// firmó, las partes quedan fijas: el servidor responde 409 «los firmantes no se pueden cambiar», así que quien
/// firma de segundo hereda, no elige.

const ana = Usuario(id: 'u1', nombre: 'Ana Pérez', email: '', rol: 'inspector');
const luis = Usuario(id: 'u2', nombre: 'Luis Gómez', email: '', rol: 'conductor');
const marta = Usuario(id: 'u3', nombre: 'Marta Ríos', email: '', rol: 'coordinador_calidad');

class UsuariosRepositorioFalso implements UsuariosRepositorio {
  final List<String> viajesConsultados = [];
  bool listaCompletaPedida = false;

  @override
  Future<List<Usuario>> listar() async {
    listaCompletaPedida = true;
    return const [ana, luis, marta];
  }

  @override
  Future<List<Usuario>> firmantesDeViaje(String viajeId) async {
    viajesConsultados.add(viajeId);
    return const [ana, luis];
  }
}

class TransferenciasRepositorioFalso implements TransferenciasRepositorio {
  TransferenciaVista? transferencia;

  @override
  Future<TransferenciaVista?> deInforme(String inspeccionId) async => transferencia;
}

/// Lo que devuelve la API cuando ya firmó quien entrega y falta quien recibe.
TransferenciaVista mediaFirmada() => TransferenciaVista(
      entregaId: luis.id,
      recibeId: ana.id,
      entregaNombre: luis.nombre,
      recibeNombre: ana.nombre,
      entregaFirmoEn: DateTime(2026, 10, 26, 9),
    );

Future<(UsuariosRepositorioFalso, TransferenciasRepositorioFalso)> abrir(
  WidgetTester tester, {
  String? viajeId = 'viaje-1',
  TransferenciaVista? transferencia,
}) async {
  final usuarios = UsuariosRepositorioFalso();
  final transferencias = TransferenciasRepositorioFalso()..transferencia = transferencia;
  await tester.pumpWidget(
    ProviderScope(
      retry: (_, _) => null,
      overrides: [
        ...Arnes().overrides,
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
    testWidgets('solo se dibuja el lienzo de quien tiene la sesión', (tester) async {
      await abrir(tester, transferencia: mediaFirmada());

      expect(find.text('Tu firma'), findsOneWidget);
      expect(find.text('Su firma se registra desde su propia sesión.'), findsOneWidget);
    });
  });
}
