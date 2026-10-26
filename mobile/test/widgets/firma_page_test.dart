import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/core/tema/tema.dart';
import 'package:traza_autos/features/auth/domain/sesion.dart';
import 'package:traza_autos/features/custodia/data/usuarios_repositorio.dart';
import 'package:traza_autos/features/custodia/presentation/firma_page.dart';
import 'package:traza_autos/features/custodia/providers.dart';

import '../support/arnes.dart';

/// Quién puede firmar una transferencia lo decide el viaje: su conductor y su inspector. Ofrecer la lista
/// completa de usuarios lleva a elegir a quien el servidor va a rechazar, y de paso enseña nombres de gente
/// ajena al viaje a cualquiera que abra la pantalla.

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

Future<UsuariosRepositorioFalso> abrir(WidgetTester tester, {String? viajeId = 'viaje-1'}) async {
  final usuarios = UsuariosRepositorioFalso();
  await tester.pumpWidget(
    ProviderScope(
      retry: (_, _) => null,
      overrides: [
        ...Arnes().overrides,
        usuariosRepositorioProvider.overrideWithValue(usuarios),
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
  return usuarios;
}

void main() {
  testWidgets('ofrece solo a los firmantes del viaje', (tester) async {
    final usuarios = await abrir(tester);

    expect(usuarios.viajesConsultados, ['viaje-1']);
    expect(usuarios.listaCompletaPedida, isFalse, reason: 'la lista de usuarios no decide quién firma');
    expect(find.widgetWithText(ChoiceChip, 'Ana Pérez'), findsWidgets);
    expect(find.widgetWithText(ChoiceChip, 'Luis Gómez'), findsWidgets);
    expect(find.widgetWithText(ChoiceChip, 'Marta Ríos'), findsNothing, reason: 'no participa en el viaje');
  });

  // Sin viaje no hay de dónde sacar los firmantes. Antes que caer en la lista completa —que es el defecto—,
  // se dice qué pasa.
  testWidgets('una etapa sin viaje lo dice y no pregunta por nadie', (tester) async {
    final usuarios = await abrir(tester, viajeId: null);

    expect(find.text('Esta etapa no tiene viaje asociado'), findsOneWidget);
    expect(usuarios.viajesConsultados, isEmpty);
    expect(usuarios.listaCompletaPedida, isFalse);
  });
}
