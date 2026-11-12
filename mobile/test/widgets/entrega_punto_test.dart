import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/core/tema/tema.dart';
import 'package:traza_autos/features/auth/presentation/auth_controller.dart';
import 'package:traza_autos/features/custodia/presentation/lienzo_firma.dart';
import 'package:traza_autos/features/sync/domain/job.dart';
import 'package:traza_autos/features/viajes/domain/entrega_punto.dart';
import 'package:traza_autos/features/viajes/presentation/entrega_punto_page.dart';
import 'package:traza_autos/features/viajes/providers.dart';

import '../support/arnes.dart';
import '../support/dobles_viajes.dart';

/// S6-HU-11.3: la entrega agrupada por punto, con una firma por punto y la opción de cerrar con una sola.

Future<void> avanzar(WidgetTester tester) async {
  for (var i = 0; i < 12; i++) {
    await tester.pump(const Duration(milliseconds: 50));
  }
}

Future<Arnes> abrir(
  WidgetTester tester,
  EntregasRepositorioFalso entregas, {
  Arnes? arnes,
  bool sinInspector = false,
  Size tamano = const Size(600, 2400),
}) async {
  await tester.binding.setSurfaceSize(tamano);
  addTearDown(() => tester.binding.setSurfaceSize(null));
  final usado = arnes ?? Arnes();
  await tester.pumpWidget(
    ProviderScope(
      retry: (_, _) => null,
      overrides: [...usado.overrides, entregasRepositorioProvider.overrideWithValue(entregas)],
      child: MaterialApp(theme: crearTema(), home: _Arranque(sinInspector: sinInspector)),
    ),
  );
  await avanzar(tester);
  return usado;
}

class _Arranque extends ConsumerWidget {
  const _Arranque({required this.sinInspector});

  final bool sinInspector;

  @override
  Widget build(BuildContext context, WidgetRef ref) =>
      ref.watch(authControllerProvider.select((e) => e.fase)) == FaseAuth.autenticado
          ? EntregasViajePage(viajeId: 'v1', viajeSinInspector: sinInspector)
          : const SizedBox();
}

Future<void> firmar(WidgetTester tester) => firmarEn(tester, 0);

/// Dibuja en el lienzo n: el 0 es el de quien entrega y el 1, cuando lo hay, el de quien recibe en el punto.
Future<void> firmarEn(WidgetTester tester, int indice) async {
  final lienzo = tester.getBottomLeft(find.byType(LienzoFirma).at(indice)) + const Offset(60, -80);
  await tester.dragFrom(lienzo, const Offset(40, 30));
  await tester.pump();
}

void main() {
  testWidgets('cada punto dice cuántas unidades se entregan allí, con sus VIN', (tester) async {
    await abrir(
      tester,
      EntregasRepositorioFalso()
        ..puntos = [
          puntoDe('Concesionario Autonorte', vines: ['VIN1AAAAAAAAAAAA1', 'VIN2AAAAAAAAAAAA2', 'VIN3AAAAAAAAAAAA3', 'VIN4AAAAAAAAAAAA4']),
          puntoDe('Concesionario Cali Sur', vines: ['VIN5AAAAAAAAAAAA5']),
        ],
    );

    expect(find.text('Aquí entregas estas 4 unidades'), findsOneWidget);
    expect(find.text('Aquí entregas esta unidad'), findsOneWidget);
    expect(find.text('VIN1AAAAAAAAAAAA1'), findsOneWidget);
    expect(find.text('VIN5AAAAAAAAAAAA5'), findsOneWidget);
    expect(find.widgetWithText(FilledButton, 'Firmar la entrega'), findsNWidgets(2));
  });

  testWidgets('un punto ya entregado lo dice y no ofrece firmar', (tester) async {
    await abrir(tester, EntregasRepositorioFalso()..puntos = [puntoDe('Autonorte', estado: EstadoEntrega.cerrada)]);

    expect(find.text('Entregada'), findsOneWidget);
    expect(find.widgetWithText(FilledButton, 'Firmar la entrega'), findsNothing);
  });

  testWidgets('sin señal no se pueden cargar las entregas y se puede reintentar', (tester) async {
    final entregas = EntregasRepositorioFalso()..error = Exception('sin red');
    await abrir(tester, entregas);

    expect(find.text('No se pudieron cargar las entregas'), findsOneWidget);
    entregas.error = null;
    entregas.puntos = [puntoDe('Autonorte')];
    await tester.tap(find.widgetWithText(FilledButton, 'Reintentar'));
    await avanzar(tester);

    expect(find.text('Aquí entregas estas 2 unidades'), findsOneWidget);
  });

  group('la firma del punto', () {
    testWidgets('una firma cierra la entrega de todas las unidades del punto', (tester) async {
      final arnes = Arnes();
      await abrir(tester, EntregasRepositorioFalso()..puntos = [puntoDe('Autonorte')], arnes: arnes);

      await tester.tap(find.widgetWithText(FilledButton, 'Firmar la entrega'));
      await avanzar(tester);
      final boton = tester.widget<FilledButton>(find.widgetWithText(FilledButton, 'Registrar mi firma'));
      expect(boton.onPressed, isNull, reason: 'sin trazo no hay nada que registrar');

      await firmar(tester);
      await tester.tap(find.widgetWithText(FilledButton, 'Registrar mi firma'));
      await avanzar(tester);

      expect(arnes.gateway.entregasEnviadas, hasLength(1));
      expect(arnes.gateway.entregasEnviadas.single.$1, 'v1');
      expect(arnes.gateway.entregasEnviadas.single.$2, 'l-Autonorte');
      expect(arnes.gateway.entregasEnviadas.single.$3, isNull);
      expect(arnes.gateway.entregasEnviadas.single.$4, isNotEmpty);
      expect(find.text('Entrega registrada'), findsOneWidget);
    });

    testWidgets('sin señal queda en la cola, a nombre de quien firmó', (tester) async {
      final arnes = Arnes()..red.estable = false;
      await abrir(tester, EntregasRepositorioFalso()..puntos = [puntoDe('Autonorte')], arnes: arnes);

      await tester.tap(find.widgetWithText(FilledButton, 'Firmar la entrega'));
      await avanzar(tester);
      await firmar(tester);
      await tester.tap(find.widgetWithText(FilledButton, 'Registrar mi firma'));
      await avanzar(tester);

      expect(find.text('Entrega guardada'), findsOneWidget);
      final job = arnes.cola.jobs.single as EntregaPuntoJob;
      expect((job.viajeId, job.lugarId, job.usuarioId, job.fallido), ('v1', 'l-Autonorte', 'u1', false));
      expect(arnes.gateway.entregasEnviadas, isEmpty);
    });

    testWidgets('si falta la inspección de llegada de una unidad dice cuál y no deja firmar', (tester) async {
      await abrir(
        tester,
        EntregasRepositorioFalso()
          ..puntos = [
            EntregaPunto(
              viajeId: 'v1',
              lugarId: 'l1',
              lugar: 'Autonorte',
              pendientes: 2,
              entregada: false,
              unidades: const [
                UnidadEntrega(vin: 'VINAAAAAAAAAAAAA1', estado: EstadoEntrega.pendiente),
                UnidadEntrega(vin: 'VINBBBBBBBBBBBBB2', estado: EstadoEntrega.sinInspeccion),
              ],
            ),
          ],
      );

      await tester.tap(find.widgetWithText(FilledButton, 'Firmar la entrega'));
      await avanzar(tester);
      await firmar(tester);

      expect(find.textContaining('Aún no tienen la inspección de llegada: VINBBBBBBBBBBBBB2'), findsOneWidget);
      final boton = tester.widget<FilledButton>(find.widgetWithText(FilledButton, 'Registrar mi firma'));
      expect(boton.onPressed, isNull);
    });

    // Un botón gris sin motivo deja adivinando a quien está en el punto, y el aviso del principio de la
    // pantalla ya no se ve cuando se ha bajado hasta la firma.
    testWidgets('el botón gris dice cuántas faltan, ahí mismo', (tester) async {
      await abrir(
        tester,
        EntregasRepositorioFalso()
          ..puntos = [
            EntregaPunto(
              viajeId: 'v1',
              lugarId: 'l1',
              lugar: 'Autonorte',
              pendientes: 3,
              entregada: false,
              unidades: const [
                UnidadEntrega(vin: 'VINAAAAAAAAAAAAA1', estado: EstadoEntrega.pendiente),
                UnidadEntrega(vin: 'VINBBBBBBBBBBBBB2', estado: EstadoEntrega.sinInspeccion),
                UnidadEntrega(vin: 'VINCCCCCCCCCCCCC3', estado: EstadoEntrega.sinInspeccion),
              ],
            ),
          ],
      );

      await tester.tap(find.widgetWithText(FilledButton, 'Firmar la entrega'));
      await avanzar(tester);
      await firmar(tester);

      expect(find.textContaining('Faltan 2 unidades por descargar'), findsWidgets);
    });

    // Con todas descargadas, lo que falta es la firma: el motivo tiene que decir eso y no quedarse en el
    // anterior, que ya no aplica.
    testWidgets('sin unidades pendientes el motivo pasa a ser la firma que falta', (tester) async {
      await abrir(tester, EntregasRepositorioFalso()..puntos = [puntoDe('Autonorte')]);

      await tester.tap(find.widgetWithText(FilledButton, 'Firmar la entrega'));
      await avanzar(tester);

      expect(find.text('Dibuja tu firma arriba.'), findsOneWidget);
      expect(find.textContaining('por descargar'), findsNothing);
    });
  });

  group('cerrar con una sola firma', () {
    Future<Arnes> abrirCierre(WidgetTester tester) async {
      final arnes = Arnes();
      await abrir(tester, EntregasRepositorioFalso()..puntos = [puntoDe('Autonorte')], arnes: arnes);
      await tester.tap(find.widgetWithText(FilledButton, 'Firmar la entrega'));
      await avanzar(tester);
      await firmar(tester);
      await tester.tap(find.textContaining('cerrar con una sola firma'));
      await avanzar(tester);
      return arnes;
    }

    testWidgets('pide el motivo y avisa que quedará como novedad para el coordinador antes de confirmar', (tester) async {
      await abrirCierre(tester);

      expect(find.text('Quedará registrado como novedad para el coordinador.'), findsOneWidget);
      final boton = tester.widget<FilledButton>(find.widgetWithText(FilledButton, 'Cerrar con una sola firma'));
      expect(boton.onPressed, isNull, reason: 'sin motivo no se cierra');
    });

    testWidgets('con motivo se encola el cierre con una sola firma', (tester) async {
      final arnes = await abrirCierre(tester);

      await tester.enterText(find.byType(TextField), 'no hay inspector en el punto');
      await tester.pump();
      await tester.tap(find.widgetWithText(FilledButton, 'Cerrar con una sola firma'));
      await avanzar(tester);

      expect(arnes.gateway.entregasEnviadas.single.$3, 'no hay inspector en el punto');
      expect(find.text('Entrega registrada'), findsOneWidget);
    });

    testWidgets('se puede volver a la firma de las dos partes', (tester) async {
      await abrirCierre(tester);

      await tester.tap(find.text('Volver: firman las dos partes'));
      await avanzar(tester);

      expect(find.widgetWithText(FilledButton, 'Registrar mi firma'), findsOneWidget);
      expect(find.text('Quedará registrado como novedad para el coordinador.'), findsNothing);
    });
  });

  /// S7-HU-02.2: en un punto sin inspector recibe alguien que no es usuario de la app. Firma ahí mismo, con su
  /// nombre y su cédula, y con eso la entrega queda cerrada: no falta nadie por firmar después.
  group('recibe una persona del punto', () {
    Future<Arnes> abrirFirma(WidgetTester tester, {required bool sinInspector, Arnes? arnes}) async {
      final usado = await abrir(
        tester,
        EntregasRepositorioFalso()..puntos = [puntoDe('Autonorte')],
        arnes: arnes,
        sinInspector: sinInspector,
      );
      await tester.tap(find.widgetWithText(FilledButton, 'Firmar la entrega'));
      await avanzar(tester);
      return usado;
    }

    Future<void> escribirDatos(WidgetTester tester) async {
      await tester.enterText(find.widgetWithText(TextField, 'Nombre de quien recibe'), 'Ana Restrepo');
      await tester.pump();
      await tester.enterText(find.widgetWithText(TextField, 'Cédula de quien recibe'), '1 020 304 050');
      await tester.pump();
    }

    testWidgets('sin inspector en el viaje pide los tres datos y no deja registrar sin ellos', (tester) async {
      await abrirFirma(tester, sinInspector: true);

      expect(find.widgetWithText(TextField, 'Nombre de quien recibe'), findsOneWidget);
      expect(find.widgetWithText(TextField, 'Cédula de quien recibe'), findsOneWidget);
      expect(find.byType(LienzoFirma), findsNWidgets(2), reason: 'la suya y la de quien recibe');
      // Encendido de entrada, pero se puede apagar: el punto puede tener inspectores de planta aunque el viaje
      // no tenga inspector, y entonces el servidor rechaza los datos del externo.
      expect(tester.widget<SwitchListTile>(find.byType(SwitchListTile)).value, isTrue);

      await firmarEn(tester, 0);

      expect(find.text('Escribe el nombre de quien recibe.'), findsOneWidget);
      expect(tester.widget<FilledButton>(find.widgetWithText(FilledButton, 'Registrar la entrega')).onPressed, isNull);
    });

    // Un viaje sin inspector que entrega en un punto que sí tiene inspectores de planta: ahí el servidor
    // rechaza los datos del externo, y sin poder apagarlo el conductor se quedaba sin forma de cerrar.
    testWidgets('aunque el viaje no tenga inspector, se puede apagar', (tester) async {
      await abrirFirma(tester, sinInspector: true);

      await tester.tap(find.byType(SwitchListTile));
      await tester.pump();

      expect(find.widgetWithText(TextField, 'Nombre de quien recibe'), findsNothing);
      expect(find.byType(LienzoFirma), findsOneWidget);
    });

    // ESC-03: escribir solo el nombre no es firmar. Se reclama lo que falta, de uno en uno.
    testWidgets('con solo el nombre sigue faltando la cédula, y luego el trazo', (tester) async {
      await abrirFirma(tester, sinInspector: true);
      await firmarEn(tester, 0);

      await tester.enterText(find.widgetWithText(TextField, 'Nombre de quien recibe'), 'Ana Restrepo');
      await tester.pump();
      expect(find.text('Escribe la cédula de quien recibe.'), findsOneWidget);

      await tester.enterText(find.widgetWithText(TextField, 'Cédula de quien recibe'), '1 020 304 050');
      await tester.pump();
      expect(find.text('Falta la firma de quien recibe.'), findsOneWidget);
      expect(tester.widget<FilledButton>(find.widgetWithText(FilledButton, 'Registrar la entrega')).onPressed, isNull);
    });

    // Lo mismo que exige el servidor, comprobado antes de firmar: si no, se encola y vuelve un 400 con el carro
    // ya entregado.
    testWidgets('un nombre de dos letras o una cédula sin números no pasan de aquí', (tester) async {
      final arnes = Arnes();
      await abrirFirma(tester, sinInspector: true, arnes: arnes);
      await firmarEn(tester, 0);
      await firmarEn(tester, 1);

      await tester.enterText(find.widgetWithText(TextField, 'Nombre de quien recibe'), 'An');
      await tester.pump();
      expect(find.textContaining('muy corto'), findsOneWidget);

      await tester.enterText(find.widgetWithText(TextField, 'Nombre de quien recibe'), 'Ana Restrepo');
      await tester.enterText(find.widgetWithText(TextField, 'Cédula de quien recibe'), 'sin numeros');
      await tester.pump();
      expect(find.textContaining('tiene que llevar números'), findsOneWidget);
      expect(arnes.cola.jobs, isEmpty, reason: 'nada se encoló: la entrega sigue sin cerrarse');
    });

    testWidgets('con los tres la entrega sale cerrada y dice quién recibió', (tester) async {
      final arnes = Arnes();
      await abrirFirma(tester, sinInspector: true, arnes: arnes);

      await firmarEn(tester, 0);
      await escribirDatos(tester);
      await firmarEn(tester, 1);
      await tester.tap(find.widgetWithText(FilledButton, 'Registrar la entrega'));
      await avanzar(tester);

      expect(arnes.gateway.externosEnviados.single.$1, 'Ana Restrepo');
      expect(arnes.gateway.externosEnviados.single.$2, '1 020 304 050');
      expect(arnes.gateway.externosEnviados.single.$3, isNotEmpty, reason: 'su trazo viaja con la entrega');
      expect(find.text('Entrega cerrada'), findsOneWidget);
      expect(find.textContaining('Recibió Ana Restrepo'), findsOneWidget);
    });

    testWidgets('sin señal los tres datos quedan en la cola con la entrega', (tester) async {
      final arnes = Arnes()..red.estable = false;
      await abrirFirma(tester, sinInspector: true, arnes: arnes);

      await firmarEn(tester, 0);
      await escribirDatos(tester);
      await firmarEn(tester, 1);
      await tester.tap(find.widgetWithText(FilledButton, 'Registrar la entrega'));
      await avanzar(tester);

      final job = arnes.cola.jobs.single as EntregaPuntoJob;
      expect((job.recibeNombre, job.recibeCedula, job.recibeExterno), ('Ana Restrepo', '1 020 304 050', true));
      expect(job.firmaRecibe, isNotEmpty);
    });

    // CA-2: el punto que sí tiene inspector firma como siempre. Que el servidor rechace los datos del externo
    // en ese caso es justo la razón de no mandarlos por si acaso.
    testWidgets('con inspector en el viaje no se pide nada de nadie más', (tester) async {
      final arnes = Arnes();
      await abrirFirma(tester, sinInspector: false, arnes: arnes);

      expect(find.widgetWithText(TextField, 'Nombre de quien recibe'), findsNothing);
      expect(find.byType(LienzoFirma), findsOneWidget);

      await firmarEn(tester, 0);
      await tester.tap(find.widgetWithText(FilledButton, 'Registrar mi firma'));
      await avanzar(tester);

      expect(arnes.gateway.externosEnviados.single, (null, null, null));
      expect(find.text('Entrega registrada'), findsOneWidget);
    });

    // El contrato no dice si el punto tiene inspector: lo sabe quien está allí.
    testWidgets('aun con inspector en el viaje se puede decir que recibe alguien del punto', (tester) async {
      await abrirFirma(tester, sinInspector: false);

      expect(tester.widget<SwitchListTile>(find.byType(SwitchListTile)).value, isFalse);
      await tester.tap(find.byType(SwitchListTile));
      await tester.pump();

      expect(find.widgetWithText(TextField, 'Nombre de quien recibe'), findsOneWidget);
      expect(find.byType(LienzoFirma), findsNWidgets(2));
    });

    // CA-6: el servidor decide por punto, y ese dato pudo cambiar desde que se abrió la pantalla. El rechazo no
    // es transitorio, así que no sirve dejarlo en la cola: se descarta, se corrige el camino y se firma aquí.
    group('cuando el punto resulta no ser el que se creía', () {
      const conInspector = 'El punto de entrega tiene inspector asignado: la entrega la firma él, no una persona externa';
      const sinQuienReciba = 'Indique quién recibe: el viaje no tiene inspector';

      testWidgets('rechazado por tener inspector, se apaga el interruptor y no se pierde lo escrito', (tester) async {
        final arnes = Arnes();
        arnes.gateway.errores['firmarEntregaPunto'] = [errorApi(400, conInspector)];
        await abrirFirma(tester, sinInspector: true, arnes: arnes);

        await firmarEn(tester, 0);
        await escribirDatos(tester);
        await firmarEn(tester, 1);
        await tester.tap(find.widgetWithText(FilledButton, 'Registrar la entrega'));
        await avanzar(tester);

        expect(find.text('Cambió quién recibe aquí'), findsOneWidget);
        expect(find.textContaining('sí tiene inspector'), findsOneWidget);
        expect(tester.widget<SwitchListTile>(find.byType(SwitchListTile)).value, isFalse, reason: 'corregido');
        expect(arnes.cola.jobs, isEmpty, reason: 'reintentarlo mandaría exactamente lo mismo');
      });

      testWidgets('rechazado por no haber quien reciba, se encienden los datos del externo', (tester) async {
        final arnes = Arnes();
        arnes.gateway.errores['firmarEntregaPunto'] = [errorApi(400, sinQuienReciba)];
        await abrirFirma(tester, sinInspector: false, arnes: arnes);

        await firmarEn(tester, 0);
        await tester.tap(find.widgetWithText(FilledButton, 'Registrar mi firma'));
        await avanzar(tester);

        expect(find.text('Cambió quién recibe aquí'), findsOneWidget);
        await tester.tap(find.widgetWithText(TextButton, 'Entendido'));
        await avanzar(tester);

        expect(tester.widget<SwitchListTile>(find.byType(SwitchListTile)).value, isTrue);
        expect(find.widgetWithText(TextField, 'Nombre de quien recibe'), findsOneWidget);
      });

      // El callejón sin salida: viaje sin inspector entregando donde sí lo hay. Las dos correcciones se pisan,
      // así que girar otra vez sería dejar al conductor dando vueltas.
      testWidgets('corregido dos veces al revés, se dice que esta entrega no se puede cerrar', (tester) async {
        final arnes = Arnes();
        arnes.gateway.errores['firmarEntregaPunto'] = [errorApi(400, sinQuienReciba), errorApi(400, conInspector)];
        await abrirFirma(tester, sinInspector: false, arnes: arnes);

        await firmarEn(tester, 0);
        await tester.tap(find.widgetWithText(FilledButton, 'Registrar mi firma'));
        await avanzar(tester);
        await tester.tap(find.widgetWithText(TextButton, 'Entendido'));
        await avanzar(tester);

        await escribirDatos(tester);
        await firmarEn(tester, 1);
        await tester.tap(find.widgetWithText(FilledButton, 'Registrar la entrega'));
        await avanzar(tester);

        expect(find.text('Esta entrega no la puedes cerrar'), findsOneWidget);
        expect(find.textContaining('reclamar el inspector del punto'), findsOneWidget);
        expect(tester.widget<SwitchListTile>(find.byType(SwitchListTile)).value, isTrue, reason: 'ya no gira más');
      });
    });

    testWidgets('una entrega ya cerrada por alguien del punto dice quién recibió', (tester) async {
      await abrir(
        tester,
        EntregasRepositorioFalso()
          ..puntos = [
            const EntregaPunto(
              viajeId: 'v1',
              lugarId: 'l1',
              lugar: 'Autonorte',
              pendientes: 0,
              entregada: true,
              unidades: [
                UnidadEntrega(vin: 'VINAAAAAAAAAAAAA1', estado: EstadoEntrega.cerrada, recibioExterno: 'Ana Restrepo'),
              ],
            ),
          ],
      );

      expect(find.text('Recibió: Ana Restrepo'), findsOneWidget);
    });

    // CA-5. Lo que una prueba puede ver es el ancho, que es donde se corta: a lo alto la pantalla se desplaza y
    // nunca recorta. `setSurfaceSize` va en píxeles lógicos, así que 320 son los 320 de un teléfono pequeño; el
    // alto es generoso para que se construya el formulario entero. Así se destapó que «Firma de quien recibe» y
    // «Limpiar» no cabían en la misma fila. Que el área de firma se use cómoda solo lo dice el celular.
    testWidgets('a lo ancho de un teléfono pequeño no se corta nada', (tester) async {
      await abrir(
        tester,
        EntregasRepositorioFalso()
          ..puntos = [
            // El caso más apretado de la fila: la insignia más larga de la app junto a un VIN de diecisiete, y
            // otra unidad con el nombre de quien recibió debajo.
            const EntregaPunto(
              viajeId: 'v1',
              lugarId: 'l1',
              lugar: 'Concesionario Autonorte de la Avenida',
              pendientes: 2,
              entregada: false,
              unidades: [
                UnidadEntrega(vin: 'VINAAAAAAAAAAAAA1', estado: EstadoEntrega.sinInspeccion),
                UnidadEntrega(vin: 'VINBBBBBBBBBBBBB2', estado: EstadoEntrega.cerrada, recibioExterno: 'Ana Restrepo'),
              ],
            ),
          ],
        sinInspector: true,
        tamano: const Size(320, 6000),
      );
      await tester.tap(find.widgetWithText(FilledButton, 'Firmar la entrega'));
      await avanzar(tester);

      expect(tester.takeException(), isNull);
      expect(find.widgetWithText(TextField, 'Nombre de quien recibe'), findsOneWidget);
      expect(find.byType(LienzoFirma), findsNWidgets(2));
    });
  });
}
