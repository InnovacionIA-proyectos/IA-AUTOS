import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/core/tema/tema.dart';
import 'package:traza_autos/core/errores/error_api.dart';
import 'package:traza_autos/features/auth/domain/sesion.dart';
import 'package:traza_autos/features/auth/presentation/auth_controller.dart';
import 'package:traza_autos/features/vehiculos/data/vehiculos_repositorio.dart';
import 'package:traza_autos/features/vehiculos/domain/inventario.dart';
import 'package:traza_autos/features/vehiculos/providers.dart';
import 'package:traza_autos/features/viajes/domain/viaje.dart';
import 'package:traza_autos/features/viajes/presentation/buscar_vin_page.dart';
import 'package:traza_autos/features/viajes/presentation/mis_viajes_controller.dart';
import 'package:traza_autos/features/viajes/presentation/mis_viajes_page.dart';
import 'package:traza_autos/features/viajes/providers.dart';

import '../support/arnes.dart';
import '../support/dobles_viajes.dart';

/// S7-HU-09: el cable entre el rol y la búsqueda.
///
/// El predicado y el repositorio ya tienen sus pruebas; lo que no tenía ninguna es lo que los une: que al
/// coordinador de calidad le salga la lupa aunque no tenga viajes, y que su búsqueda vaya al servidor en vez de
/// a la copia local de viajes, que para él está vacía. Invertir cualquiera de las dos condiciones lo deja sin
/// ninguna entrada a la ficha sin que falle una sola prueba.
class VehiculosFalso implements VehiculosRepositorio {
  List<VehiculoBusqueda> resultado = const [];
  final terminos = <String>[];

  @override
  Future<List<VehiculoBusqueda>> buscar(String termino) async {
    terminos.add(termino);
    return resultado;
  }

  @override
  Future<Inventario> inventario(String vin) async => throw const ErrorApi.red();
}

Viaje _viaje() => const Viaje(
      id: 'v1',
      codigoMtrack: 'MTK-1',
      placa: 'ABC123',
      estado: 'en_curso',
      vehiculos: [ViajeVehiculo(vin: 'VIN00000000000001', marca: 'Renault', modelo: 'Duster')],
    );

Future<void> avanzar(WidgetTester tester) async {
  for (var i = 0; i < 12; i++) {
    await tester.pump(const Duration(milliseconds: 50));
  }
}

Future<Arnes> abrir(
  WidgetTester tester,
  Widget pantalla, {
  required Sesion sesion,
  List<Viaje> viajes = const [],
  VehiculosFalso? vehiculos,
}) async {
  await tester.binding.setSurfaceSize(const Size(600, 2400));
  addTearDown(() => tester.binding.setSurfaceSize(null));
  final arnes = Arnes(sesion: sesion);
  await tester.pumpWidget(
    ProviderScope(
      retry: (_, _) => null,
      overrides: [
        ...arnes.overrides,
        viajesRepositorioProvider.overrideWithValue(ViajesRepositorioFalso()..remotos = viajes),
        vehiculosRepositorioProvider.overrideWithValue(vehiculos ?? VehiculosFalso()),
        reclamosRepositorioProvider.overrideWithValue(ReclamosRepositorioFalso()),
      ],
      child: MaterialApp(
        theme: crearTema(),
        home: Consumer(
          builder: (_, ref, _) =>
              ref.watch(authControllerProvider.select((e) => e.fase)) == FaseAuth.autenticado ? pantalla : const SizedBox(),
        ),
      ),
    ),
  );
  // A la búsqueda se llega desde Mis viajes, que ya cargó los viajes; la pantalla suelta no los carga sola.
  await ProviderScope.containerOf(tester.element(find.byType(MaterialApp)))
      .read(misViajesControllerProvider.notifier)
      .cargar();
  await avanzar(tester);
  return arnes;
}

void main() {
  group('la lupa en Mis viajes', () {
    testWidgets('al coordinador de calidad se le ofrece aunque no tenga ni un viaje', (tester) async {
      await abrir(tester, const MisViajesPage(), sesion: sesionCoordinador);

      expect(find.byType(FloatingActionButton), findsOneWidget, reason: 'es su única entrada a la ficha');
    });

    testWidgets('al inspector sin viajes no: no hay nada suyo donde buscar', (tester) async {
      await abrir(tester, const MisViajesPage(), sesion: sesionPrueba);

      expect(find.byType(FloatingActionButton), findsNothing);
    });

    testWidgets('al inspector con viajes sí', (tester) async {
      await abrir(tester, const MisViajesPage(), sesion: sesionPrueba, viajes: [_viaje()]);

      expect(find.byType(FloatingActionButton), findsOneWidget);
    });
  });

  group('la búsqueda', () {
    testWidgets('la del coordinador la contesta el servidor, no la copia de viajes', (tester) async {
      final vehiculos = VehiculosFalso()
        ..resultado = const [VehiculoBusqueda(vin: 'VIN00000000000009', marca: 'Renault', modelo: 'Logan')];
      await abrir(tester, const BuscarVinPage(), sesion: sesionCoordinador, vehiculos: vehiculos);

      await tester.enterText(find.byType(TextField), 'VIN0000');
      await tester.testTextInput.receiveAction(TextInputAction.search);
      await avanzar(tester);

      expect(vehiculos.terminos, ['VIN0000']);
      expect(find.text('VIN00000000000009'), findsOneWidget);
      expect(find.text('Renault Logan'), findsOneWidget);
    });

    testWidgets('con menos de tres caracteres no se le pregunta nada al servidor', (tester) async {
      final vehiculos = VehiculosFalso();
      await abrir(tester, const BuscarVinPage(), sesion: sesionCoordinador, vehiculos: vehiculos);

      await tester.enterText(find.byType(TextField), 'VI');
      await tester.testTextInput.receiveAction(TextInputAction.search);
      await avanzar(tester);

      expect(vehiculos.terminos, isEmpty);
      expect(find.text('Escribe al menos 3 caracteres'), findsOneWidget);
    });

    // El inspector busca en lo suyo, que funciona sin señal. Preguntarle al servidor por él sería pedirle
    // carros que luego no puede abrir.
    testWidgets('la del inspector sale de sus viajes y no toca el servidor', (tester) async {
      final vehiculos = VehiculosFalso();
      await abrir(tester, const BuscarVinPage(), sesion: sesionPrueba, viajes: [_viaje()], vehiculos: vehiculos);

      await tester.enterText(find.byType(TextField), 'VIN00000000000001');
      await avanzar(tester);

      expect(vehiculos.terminos, isEmpty, reason: 'lo suyo sale de la copia local y funciona sin señal');
      // La marca y el modelo solo salen en la tarjeta del resultado: el VIN también está en el campo de texto.
      expect(find.text('Renault Duster'), findsOneWidget);
    });
  });
}
