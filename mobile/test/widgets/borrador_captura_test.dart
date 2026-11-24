import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:traza_autos/core/tema/tema.dart';
import 'package:traza_autos/core/ui/widgets.dart';
import 'package:traza_autos/features/auth/presentation/auth_controller.dart';
import 'package:traza_autos/features/catalogos/providers.dart';
import 'package:traza_autos/features/inspecciones/domain/borrador_captura.dart';
import 'package:traza_autos/features/inspecciones/domain/extra_local.dart';
import 'package:traza_autos/features/inspecciones/domain/foto_local.dart';
import 'package:traza_autos/features/inspecciones/domain/hallazgo_local.dart';
import 'package:traza_autos/features/inspecciones/presentation/captura_page.dart';
import 'package:traza_autos/features/sync/domain/job.dart';
import 'package:traza_autos/features/vehiculos/domain/inventario.dart';
import 'package:traza_autos/features/viajes/domain/viaje.dart';
import 'package:traza_autos/features/viajes/providers.dart';

import '../support/arnes.dart';
import '../support/dobles_catalogos.dart';
import '../support/dobles_viajes.dart';

/// S8-HU-11.2 CA-4, CA-5 y CA-9: lo capturado se guarda en el teléfono mientras se hace; salir no pierde nada,
/// volver ofrece continuar o empezar de nuevo, y cerrar o empezar de nuevo no deja fotos sin dueño.

const _vin = 'VIN00000000000001';
final _clave = claveBorrador(usuarioId: 'u1', vin: _vin, etapaId: 'e1', momento: Momento.llegada);

FotoLocal foto(String ruta) => FotoLocal(ruta: ruta, selloFecha: '2026-11-23T14:00:00Z', selloLat: 6.2, selloLng: -75.5);

/// ESC-09: Ana dejó la captura con 4 fotos y 1 daño. `viejo.jpg` es la foto de un daño que quitó antes de salir.
BorradorCaptura guardadoDeAna() => BorradorCaptura(
      fotos: [foto('f1.jpg'), foto('f2.jpg'), foto('f3.jpg'), foto('f4.jpg')],
      hallazgos: [
        HallazgoLocal(
          cuadranteNumero: 13,
          lado: 'izquierdo',
          zona: 'Puerta delantera izquierda',
          clase: 'golpe',
          tamano: 'mas_20cm',
          nivel: 'v1',
          foto: foto('h1.jpg'),
        ),
      ],
      notas: 'Golpe visto al bajar de la niñera',
      archivos: const {'f1.jpg', 'f2.jpg', 'f3.jpg', 'f4.jpg', 'h1.jpg', 'viejo.jpg'},
    );

Future<void> avanzar(WidgetTester tester) async {
  for (var i = 0; i < 8; i++) {
    await tester.pump(const Duration(milliseconds: 50));
  }
}

/// La captura se abre desde otra pantalla, como en la app: así se puede salir de ella y volver.
Future<Arnes> montar(WidgetTester tester, {Arnes? arnes, String momento = Momento.llegada, Viaje? delViaje}) async {
  await tester.binding.setSurfaceSize(const Size(600, 2400));
  addTearDown(() => tester.binding.setSurfaceSize(null));
  final usado = arnes ?? Arnes();
  await tester.pumpWidget(
    ProviderScope(
      retry: (_, _) => null,
      overrides: [
        ...usado.overrides,
        viajesRepositorioProvider.overrideWithValue(ViajesRepositorioFalso()..viajeDeEtapa = delViaje ?? viaje('v1')),
        catalogosRepositorioProvider.overrideWithValue(
          CatalogosRepositorioFalso()
            ..guardado = catalogosCon('Golpe')
            ..remoto = catalogosCon('Golpe'),
        ),
      ],
      // Con GoRouter, como en la app: la captura sale con `context.pop()` al cerrar el informe.
      child: MaterialApp.router(
        theme: crearTema(),
        routerConfig: GoRouter(
          routes: [
            GoRoute(
              path: '/',
              builder: (context, _) => Consumer(
                builder: (context, ref, _) =>
                    ref.watch(authControllerProvider.select((e) => e.fase)) != FaseAuth.autenticado
                        ? const SizedBox()
                        : Scaffold(
                            body: TextButton(
                              onPressed: () => context.push('/captura'),
                              child: const Text('abrir captura'),
                            ),
                          ),
              ),
            ),
            GoRoute(
              path: '/captura',
              builder: (_, _) => CapturaPage(
                vin: _vin,
                etapaId: 'e1',
                proceso: 'cargue',
                momento: momento,
                viajeId: 'v1',
              ),
            ),
          ],
        ),
      ),
    ),
  );
  await avanzar(tester);
  return usado;
}

Future<void> abrirCaptura(WidgetTester tester) async {
  await tester.tap(find.text('abrir captura'));
  await avanzar(tester);
}

Arnes conGuardado(BorradorCaptura borrador) {
  final arnes = Arnes();
  arnes.borradores.guardados[_clave] = borrador.toJson();
  return arnes;
}

Finder get _cerrarInforme => find.widgetWithText(FilledButton, 'Cerrar informe');

void main() {
  // S8-HU-01.3 CA-4 y ESC-10: los puestos de la acomodación se guardan en el teléfono al elegirlos.
  group('los puestos de la acomodación', () {
    const vinA = 'VINA0000000000001';
    const vinB = 'VINB0000000000002';
    final claveSalida = claveBorrador(usuarioId: 'u1', vin: _vin, etapaId: 'e1', momento: Momento.salida);

    testWidgets('elegir un puesto lo guarda en el teléfono, sin repetir lo que ya tiene el servidor', (tester) async {
      final arnes = await montar(
        tester,
        momento: Momento.salida,
        delViaje: viaje('v1', vines: [vinA, vinB], posiciones: {vinB: 3}),
      );
      await abrirCaptura(tester);

      await tester.tap(find.descendant(of: find.widgetWithText(Tarjeta, vinA), matching: find.widgetWithText(ChoiceChip, '4')));
      await avanzar(tester);

      expect(BorradorCaptura.fromJson(arnes.borradores.guardados[claveSalida]!).posiciones, {vinA: 4});
    });

    testWidgets('al volver se ofrecen y continuar los pone en su puesto', (tester) async {
      final arnes = Arnes();
      arnes.borradores.guardados[claveSalida] = const BorradorCaptura(posiciones: {vinA: 4}).toJson();
      await montar(tester, arnes: arnes, momento: Momento.salida, delViaje: viaje('v1', vines: [vinA, vinB]));
      await abrirCaptura(tester);

      await tester.tap(find.text('Continuar lo guardado (0 fotos, 0 daños, 1 puesto)'));
      await avanzar(tester);

      final elegidas = tester
          .widgetList<ChoiceChip>(find.descendant(of: find.widgetWithText(Tarjeta, vinA), matching: find.byType(ChoiceChip)))
          .where((c) => c.selected);
      expect(elegidas.map((c) => ((c.label as SizedBox).child as Text).data), ['4']);
      expect(find.text('Falta la posición de la unidad $vinB'), findsOneWidget);
    });
  });

  group('volver a una captura guardada', () {
    testWidgets('ofrece continuar con lo guardado o empezar de nuevo, y no deja capturar antes', (tester) async {
      await montar(tester, arnes: conGuardado(guardadoDeAna()));
      await abrirCaptura(tester);

      expect(find.widgetWithText(FilledButton, 'Continuar lo guardado (4 fotos, 1 daño)'), findsOneWidget);
      expect(find.widgetWithText(OutlinedButton, 'Empezar de nuevo'), findsOneWidget);
      expect(_cerrarInforme, findsNothing, reason: 'capturar antes de decidir pisaría lo guardado');
    });

    // ESC-09.
    testWidgets('«Continuar» devuelve las fotos, el daño y las notas, y el cierre los lleva', (tester) async {
      final arnes = conGuardado(guardadoDeAna())..red.estable = false;
      await montar(tester, arnes: arnes);
      await abrirCaptura(tester);

      await tester.tap(find.widgetWithText(FilledButton, 'Continuar lo guardado (4 fotos, 1 daño)'));
      await avanzar(tester);

      expect(find.textContaining('Puerta delantera izquierda'), findsWidgets);
      expect(find.text('Golpe visto al bajar de la niñera'), findsOneWidget);

      await tester.tap(_cerrarInforme);
      await avanzar(tester);

      final job = arnes.cola.jobs.single as InformeJob;
      expect([for (final f in job.fotos) f.ruta], ['f1.jpg', 'f2.jpg', 'f3.jpg', 'f4.jpg']);
      expect(job.hallazgos.single.foto?.ruta, 'h1.jpg');
      expect(job.notas, 'Golpe visto al bajar de la niñera');
      // CA-5: cerrado, el borrador se va; la foto que ya no era de nadie se borra; las del job las borra la cola
      // cuando el servidor las recibe.
      expect(arnes.borradores.guardados, isEmpty);
      expect(arnes.archivos.borrados, ['viejo.jpg']);
    });

    // ESC-10.
    testWidgets('«Empezar de nuevo», confirmado, borra lo guardado y todas sus fotos', (tester) async {
      final arnes = conGuardado(guardadoDeAna());
      await montar(tester, arnes: arnes);
      await abrirCaptura(tester);

      await tester.tap(find.widgetWithText(OutlinedButton, 'Empezar de nuevo'));
      await avanzar(tester);
      expect(find.textContaining('4 fotos y 1 daño'), findsOneWidget, reason: 'nombra lo que se pierde');
      await tester.tap(find.widgetWithText(TextButton, 'Borrar y empezar'));
      await avanzar(tester);

      expect(arnes.borradores.guardados, isEmpty);
      expect(arnes.archivos.borrados.toSet(), {'f1.jpg', 'f2.jpg', 'f3.jpg', 'f4.jpg', 'h1.jpg', 'viejo.jpg'});
      expect(_cerrarInforme, findsOneWidget);
      expect(find.textContaining('Puerta delantera izquierda'), findsNothing, reason: 'el momento quedó vacío');
    });

    testWidgets('si no confirma, no se borra nada y la oferta sigue', (tester) async {
      final arnes = conGuardado(guardadoDeAna());
      await montar(tester, arnes: arnes);
      await abrirCaptura(tester);

      await tester.tap(find.widgetWithText(OutlinedButton, 'Empezar de nuevo'));
      await avanzar(tester);
      await tester.tap(find.widgetWithText(TextButton, 'Cancelar'));
      await avanzar(tester);

      expect(arnes.borradores.guardados, contains(_clave));
      expect(arnes.archivos.borrados, isEmpty);
      expect(find.widgetWithText(FilledButton, 'Continuar lo guardado (4 fotos, 1 daño)'), findsOneWidget);
    });

    testWidgets('lo guardado por otra persona en este teléfono no se le ofrece', (tester) async {
      final arnes = Arnes();
      arnes.borradores.guardados[claveBorrador(usuarioId: 'diana', vin: _vin, etapaId: 'e1', momento: Momento.llegada)] =
          guardadoDeAna().toJson();
      await montar(tester, arnes: arnes);
      await abrirCaptura(tester);

      expect(find.textContaining('Continuar lo guardado'), findsNothing);
      expect(_cerrarInforme, findsOneWidget);
    });

    testWidgets('un guardado sin nada que continuar solo borra sus fotos, sin preguntar', (tester) async {
      final arnes = conGuardado(const BorradorCaptura(archivos: {'viejo.jpg'}));
      await montar(tester, arnes: arnes);
      await abrirCaptura(tester);

      expect(find.textContaining('Continuar lo guardado'), findsNothing);
      expect(arnes.archivos.borrados, ['viejo.jpg']);
      expect(arnes.borradores.guardados, isEmpty);
    });
  });

  group('mientras se captura', () {
    testWidgets('lo que se escribe se guarda, y al salir lo dice y al volver lo ofrece', (tester) async {
      final arnes = await montar(tester);
      await abrirCaptura(tester);

      await tester.enterText(find.byType(TextField), 'Rayón en el bómper');
      await tester.pump(const Duration(milliseconds: 500));
      expect(arnes.borradores.guardados[_clave]?['notas'], 'Rayón en el bómper');

      await tester.pageBack();
      await avanzar(tester);
      expect(find.text('Queda guardado en este teléfono'), findsOneWidget);

      await abrirCaptura(tester);
      expect(find.widgetWithText(FilledButton, 'Continuar lo guardado (0 fotos, 0 daños)'), findsOneWidget);
    });

    testWidgets('salir sin haber capturado nada no avisa ni guarda', (tester) async {
      final arnes = await montar(tester);
      await abrirCaptura(tester);

      await tester.pageBack();
      await avanzar(tester);

      expect(find.text('Queda guardado en este teléfono'), findsNothing);
      expect(arnes.borradores.guardados, isEmpty);
    });

    testWidgets('quitar un daño se guarda enseguida, y su foto queda anotada para borrarla', (tester) async {
      final arnes = conGuardado(guardadoDeAna());
      await montar(tester, arnes: arnes);
      await abrirCaptura(tester);
      await tester.tap(find.widgetWithText(FilledButton, 'Continuar lo guardado (4 fotos, 1 daño)'));
      await avanzar(tester);

      await tester.ensureVisible(find.text('Quitar'));
      await tester.tap(find.text('Quitar'));
      await avanzar(tester);

      final guardado = BorradorCaptura.fromJson(arnes.borradores.guardados[_clave]!);
      expect(guardado.hallazgos, isEmpty);
      expect(guardado.archivos, contains('h1.jpg'));
    });

    testWidgets('cerrado el informe, salir no dice que quedó guardado', (tester) async {
      final arnes = await montar(tester);
      await abrirCaptura(tester);
      await tester.enterText(find.byType(TextField), 'Sin novedades');
      await tester.pump(const Duration(milliseconds: 500));
      expect(arnes.borradores.guardados, contains(_clave));

      await tester.tap(_cerrarInforme);
      await avanzar(tester);
      await tester.tap(find.widgetWithText(TextButton, 'OK'));
      await avanzar(tester);

      // La transición de salida dura más que un `avanzar`.
      await avanzar(tester);
      expect(find.byType(CapturaPage), findsNothing, reason: 'el cierre saca la pantalla');
      expect(find.text('Queda guardado en este teléfono'), findsNothing);
      expect(arnes.borradores.guardados, isEmpty);
    });

    testWidgets('lo escrito justo antes de salir también queda guardado', (tester) async {
      final arnes = await montar(tester);
      await abrirCaptura(tester);

      // Se sale en el mismo instante: la pantalla se va antes de que venza la espera del guardado de las notas.
      await tester.enterText(find.byType(TextField), 'Llanta baja');
      await tester.pageBack();
      await avanzar(tester);

      expect(arnes.borradores.guardados[_clave]?['notas'], 'Llanta baja');
      expect(find.text('Queda guardado en este teléfono'), findsOneWidget);
    });

    testWidgets('si la pantalla se destruye antes de la espera de las notas, igual se guardan', (tester) async {
      final arnes = await montar(tester);
      await abrirCaptura(tester);

      await tester.enterText(find.byType(TextField), 'Espejo rajado');
      // Desmontar todo de una vez: `dispose` corre ya, con el guardado de las notas todavía en espera.
      await tester.pumpWidget(const SizedBox());
      await tester.pump(const Duration(milliseconds: 500));

      expect(arnes.borradores.guardados[_clave]?['notas'], 'Espejo rajado');
    });

    // Encolar y soltar el borrador van juntos: si encolar falla, nada se suelta ni se borra.
    testWidgets('si no se pudo encolar, lo guardado y sus fotos siguen, y se puede volver a cerrar', (tester) async {
      final arnes = conGuardado(guardadoDeAna())..red.estable = false;
      arnes.cierre.error = Exception('disco lleno');
      await montar(tester, arnes: arnes);
      await abrirCaptura(tester);
      await tester.tap(find.widgetWithText(FilledButton, 'Continuar lo guardado (4 fotos, 1 daño)'));
      await avanzar(tester);

      await tester.tap(_cerrarInforme);
      await avanzar(tester);

      expect(find.text('No se pudo guardar'), findsOneWidget);
      expect(arnes.cola.jobs, isEmpty);
      expect(arnes.borradores.guardados, contains(_clave));
      expect(arnes.archivos.borrados, isEmpty, reason: 'ni siquiera la foto sin dueño: el borrador sigue listándola');

      await tester.tap(find.widgetWithText(TextButton, 'OK'));
      await avanzar(tester);
      arnes.cierre.error = null;
      await tester.tap(_cerrarInforme);
      await avanzar(tester);

      expect(arnes.cola.jobs, hasLength(1));
      expect(arnes.borradores.guardados, isEmpty);
      expect(arnes.archivos.borrados, ['viejo.jpg']);
    });
  });

  test('una corrección guarda aparte de la primera captura del mismo momento', () {
    expect(
      claveBorrador(usuarioId: 'u1', vin: _vin, etapaId: 'e1', momento: Momento.llegada, corrigeInspeccionId: 'i9'),
      isNot(_clave),
    );
  });

  test('las fotos de los extras cuentan como fotos al ofrecer continuar', () {
    final borrador = BorradorCaptura(
      fotos: [foto('f1.jpg')],
      extras: [ExtraLocal(tipo: 'gato', nombre: 'Gato', fotos: [foto('x1.jpg'), foto('x2.jpg')])],
    );
    expect(etiquetaContinuar(borrador), 'Continuar lo guardado (3 fotos, 0 daños)');
  });

  // CA-9: nada que la cola no guarde ya.
  test('el guardado lleva solo fotos, daños, extras, notas, rutas de archivo y puestos', () {
    // Los puestos son VIN y número de puesto (S8-HU-01.3 CA-4): ningún dato personal.
    expect(guardadoDeAna().toJson().keys.toSet(), {'fotos', 'hallazgos', 'extras', 'notas', 'archivos', 'posiciones'});
  });

  test('un guardado con solo puestos no está vacío y los cuenta al ofrecer continuar', () {
    const conPuestos = BorradorCaptura(posiciones: {'VINA': 4, 'VINB': 2});
    final leido = BorradorCaptura.fromJson(conPuestos.toJson());

    expect(leido.posiciones, {'VINA': 4, 'VINB': 2});
    expect(leido.vacio, isFalse);
    expect(etiquetaContinuar(leido), 'Continuar lo guardado (0 fotos, 0 daños, 2 puestos)');
    expect(etiquetaContinuar(const BorradorCaptura()), 'Continuar lo guardado (0 fotos, 0 daños)');
  });
}
