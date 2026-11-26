import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:traza_autos/core/config/parametros_app.dart';
import 'package:traza_autos/core/errores/error_api.dart';
import 'package:traza_autos/core/tema/tema.dart';
import 'package:traza_autos/core/ui/widgets.dart';
import 'package:traza_autos/features/auth/domain/sesion.dart';
import 'package:traza_autos/features/catalogos/providers.dart';
import 'package:traza_autos/features/inspecciones/presentation/captura_page.dart';
import 'package:traza_autos/features/inspecciones/presentation/filas_captura.dart';
import 'package:traza_autos/features/inspecciones/presentation/seccion_acomodacion.dart';
import 'package:traza_autos/features/vehiculos/data/vehiculos_repositorio.dart';

import 'package:traza_autos/features/vehiculos/domain/inventario.dart';
import 'package:traza_autos/features/vehiculos/providers.dart';
import 'package:traza_autos/features/viajes/providers.dart';

import '../support/arnes.dart';
import '../support/dobles_catalogos.dart';
import '../support/dobles_viajes.dart';

/// El criterio de la S5-HU-22.9: «la captura toma placa, tráiler, unidad y conductor del viaje; se retiran los
/// campos de texto libre». Es lo único que prueba que la decisión llegue a la pantalla y no solo al repositorio.

/// Varios fotogramas en vez de pumpAndSettle: la página busca el GPS y muestra indicadores que no paran solos.
///
/// La superficie es alta a propósito: el `ListView` de la captura es perezoso y en una pantalla de teléfono no
/// construye el campo de notas ni el botón de cierre, que son justo lo que hay que mirar.
Future<Arnes> abrir(
  WidgetTester tester,
  ViajesRepositorioFalso viajes, {
  String? viajeId = 'v1',
  Arnes? arnes,
  List<Map<String, dynamic>> vistas = const [],
  String proceso = 'Cargue',
  String procesoCodigo = 'cargue',
  String momento = Momento.llegada,
  List<Map<String, dynamic>> muelles = const [],
  // S8-HU-11.2 CA-2: el borrador del servidor que se continúa, y el inventario de donde se lee lo que ya tiene.
  String? borradorId,
  Map<String, dynamic>? inventario,
}) async {
  await tester.binding.setSurfaceSize(const Size(600, 2400));
  addTearDown(() => tester.binding.setSurfaceSize(null));
  final usado = arnes ?? Arnes();
  await tester.pumpWidget(
    ProviderScope(
      retry: (_, _) => null,
      overrides: [
        ...usado.overrides,
        viajesRepositorioProvider.overrideWithValue(viajes),
        if (inventario != null) vehiculosRepositorioProvider.overrideWithValue(_InventarioFijo(inventario)),
        // Con copia guardada además de la remota: así los catálogos están listos también sin señal, como queda
        // el celular tras el primer ingreso. Sin ellos el cierre se bloquea y no se llega al desenlace.
        catalogosRepositorioProvider.overrideWithValue(
          CatalogosRepositorioFalso()
            ..guardado = catalogosCon('Golpe', vistas: vistas, muelles: muelles)
            ..remoto = catalogosCon('Golpe', vistas: vistas, muelles: muelles),
        ),
      ],
      child: MaterialApp(
        theme: crearTema(),
        home: CapturaPage(
          vin: 'VIN00000000000001',
          etapaId: 'e1',
          proceso: procesoCodigo,
          momento: momento,
          viajeId: viajeId,
          borradorId: borradorId,
        ),
      ),
    ),
  );
  for (var i = 0; i < 5; i++) {
    await tester.pump(const Duration(milliseconds: 50));
  }
  return usado;
}

/// El inventario del carro, siempre el mismo: de él lee la captura lo que ya tiene el borrador que continúa.
class _InventarioFijo implements VehiculosRepositorio {
  _InventarioFijo(this.json);

  final Map<String, dynamic> json;

  @override
  Future<List<VehiculoBusqueda>> buscar(String termino) async => const [];

  @override
  Future<Inventario> inventario(String vin) async => Inventario.fromJson(json);
}

/// Cierra el informe y espera a que salga el diálogo del desenlace.
Future<void> cerrarInforme(WidgetTester tester) async {
  await tester.tap(find.widgetWithText(FilledButton, 'Cerrar informe'));
  for (var i = 0; i < 8; i++) {
    await tester.pump(const Duration(milliseconds: 50));
  }
}

/// El valor que acompaña a una etiqueta de la tarjeta del viaje («Placa» → «ABC123»).
Finder dato(String etiqueta, String valor) => find.ancestor(
      of: find.text(etiqueta),
      matching: find.widgetWithText(Row, valor),
    );

void main() {
  testWidgets('muestra placa, tráiler, unidad y conductor tomados del viaje', (tester) async {
    final viajes = ViajesRepositorioFalso()
      ..viajeDeEtapa = viaje('v1', placa: 'XKL482', trailer: 'T-9', unidad: 'Niñera', conductor: 'Luis Gómez');

    await abrir(tester, viajes);

    expect(viajes.etapasConsultadas, ['v1']);
    expect(dato('Placa', 'XKL482'), findsOneWidget);
    expect(dato('Tráiler', 'T-9'), findsOneWidget);
    expect(dato('Unidad', 'Niñera'), findsOneWidget);
    expect(dato('Conductor', 'Luis Gómez'), findsOneWidget);
  });

  // La otra mitad del criterio, y la que se rompe sin ruido: alguien vuelve a poner un campo escribible y el
  // informe vuelve a llevar datos de transporte tecleados, que el servidor ya no acepta.
  testWidgets('no quedan campos de texto libre del transporte: solo se escriben las notas', (tester) async {
    await abrir(tester, ViajesRepositorioFalso()..viajeDeEtapa = viaje('v1'));

    final campos = tester.widgetList<TextField>(find.byType(TextField));
    expect(campos, hasLength(1));
    expect((campos.single.decoration!.labelText)!, contains('Notas'));
  });

  testWidgets('los cuatro datos no son editables', (tester) async {
    await abrir(tester, ViajesRepositorioFalso()..viajeDeEtapa = viaje('v1', conductor: 'Luis Gómez'));

    for (final valor in ['ABC123', 'Niñera', 'Luis Gómez']) {
      expect(find.widgetWithText(TextField, valor), findsNothing, reason: '$valor se muestra, no se escribe');
    }
  });

  testWidgets('un viaje sin tráiler ni conductor los marca en vez de dejarlos en blanco', (tester) async {
    await abrir(tester, ViajesRepositorioFalso()..viajeDeEtapa = viaje('v1', trailer: null, conductor: null));

    expect(dato('Tráiler', '—'), findsOneWidget);
    expect(dato('Conductor', '—'), findsOneWidget);
  });

  testWidgets('una etapa sin viaje lo dice y no pregunta al servidor', (tester) async {
    final viajes = ViajesRepositorioFalso();

    await abrir(tester, viajes, viajeId: null);

    expect(find.text('Esta etapa no tiene viaje asociado.'), findsOneWidget);
    expect(viajes.etapasConsultadas, isEmpty);
  });

  // Que no se puedan traer los datos no debe dejar a nadie sin poder registrar lo que está viendo en el patio: el
  // servidor toma la placa y el conductor del viaje al recibir el informe, no de aquí.
  testWidgets('si el viaje no se puede traer avisa, pero deja cerrar el informe igual', (tester) async {
    await abrir(tester, ViajesRepositorioFalso()..errorDeLaEtapa = Exception('sin red'));

    expect(find.text('No se pudieron cargar los datos del viaje.'), findsOneWidget);
    final boton = tester.widget<FilledButton>(find.widgetWithText(FilledButton, 'Cerrar informe'));
    expect(boton.onPressed, isNotNull);
  });

  // Lo que el informe le dice a quien está en el patio tiene que ser verdad: es su única señal de si el trabajo
  // quedó hecho. El mensaje se decidía con el contador de pendientes de TODA la cola, que excluye los fallidos,
  // así que un rechazo del servidor se anunciaba como «enviado».
  group('el desenlace que se anuncia al cerrar', () {
    testWidgets('enviado: el job salió de la cola', (tester) async {
      final arnes = await abrir(tester, ViajesRepositorioFalso()..viajeDeEtapa = viaje('v1'));

      await cerrarInforme(tester);

      expect(find.text('Informe enviado'), findsOneWidget);
      expect(arnes.cola.jobs, isEmpty);
    });

    testWidgets('en cola: sin señal se queda pendiente y se enviará solo', (tester) async {
      final arnes = Arnes()..red.estable = false;
      await abrir(tester, ViajesRepositorioFalso()..viajeDeEtapa = viaje('v1'), arnes: arnes);

      await cerrarInforme(tester);

      expect(find.text('Informe guardado'), findsOneWidget);
      expect(find.textContaining('se enviará automáticamente'), findsOneWidget);
      expect(arnes.cola.jobs.single.fallido, isFalse);
    });

    testWidgets('rechazado: NO dice enviado, dice qué pasó y dónde quedó', (tester) async {
      final arnes = Arnes()
        ..gateway.errores['crearInspeccion'] = [errorApi(400, 'Momento inválido: use salida o llegada')];
      await abrir(tester, ViajesRepositorioFalso()..viajeDeEtapa = viaje('v1'), arnes: arnes);

      await cerrarInforme(tester);

      expect(arnes.cola.jobs.single.fallido, isTrue, reason: 'un 400 no se arregla reintentando');
      expect(find.text('Informe enviado'), findsNothing);
      expect(find.text('No se pudo enviar'), findsOneWidget);
      expect(find.textContaining('Momento inválido'), findsOneWidget, reason: 'el motivo del servidor, tal cual');
      expect(find.textContaining('Cola'), findsOneWidget, reason: 'dónde recuperarlo');
    });

    // S8-HU-09.2 CA-2: la captura y la ficha que la abrió, con el servidor respondiendo una vez el 403 del permiso.
    Future<(Arnes, PermisosRepositorioFalso)> abrirConEspera(WidgetTester tester, {required bool bajoPermiso}) async {
      await tester.binding.setSurfaceSize(const Size(600, 2400));
      addTearDown(() => tester.binding.setSurfaceSize(null));
      final arnes = Arnes();
      arnes.gateway.errores['crearInspeccion'] = [
        const ErrorApi(
          message: 'El punto de cargue tiene inspectores: pida el permiso de la excepción «No hay inspector» y espere',
          code: 'FORBIDDEN',
          status: 403,
          details: {'motivo': 'conductor_sin_permiso_de_excepcion'},
        ),
      ];
      final permisos = PermisosRepositorioFalso()..permiso = permisoDe(viajeId: 'v1', falta: Duration.zero);
      final router = GoRouter(
        initialLocation: '/',
        routes: [
          GoRoute(
            path: '/',
            builder: (_, _) => const Scaffold(body: Text('La ficha del carro')),
            routes: [
              GoRoute(
                path: 'captura',
                builder: (_, _) => CapturaPage(
                  vin: 'VIN00000000000001',
                  etapaId: 'e1',
                  proceso: 'cargue',
                  momento: Momento.llegada,
                  viajeId: 'v1',
                  bajoPermiso: bajoPermiso,
                ),
              ),
            ],
          ),
        ],
      );
      await tester.pumpWidget(
        ProviderScope(
          retry: (_, _) => null,
          overrides: [
            ...arnes.overrides,
            viajesRepositorioProvider.overrideWithValue(ViajesRepositorioFalso()..viajeDeEtapa = viaje('v1')),
            catalogosRepositorioProvider.overrideWithValue(
              CatalogosRepositorioFalso()
                ..guardado = catalogosCon('Golpe')
                ..remoto = catalogosCon('Golpe'),
            ),
            permisosRepositorioProvider.overrideWithValue(permisos),
          ],
          child: MaterialApp.router(theme: crearTema(), routerConfig: router),
        ),
      );
      router.go('/captura');
      for (var i = 0; i < 5; i++) {
        await tester.pump(const Duration(milliseconds: 50));
      }
      return (arnes, permisos);
    }

    Future<void> fotogramas(WidgetTester tester) async {
      for (var i = 0; i < 8; i++) {
        await tester.pump(const Duration(milliseconds: 50));
      }
    }

    // Se siguió con la cuenta atrás en cero y el servidor todavía responde que espere. La pantalla vuelve a consultar
    // el permiso y lo muestra; lo capturado sigue en la cola y sale cuando el servidor lo concede.
    testWidgets('bajo un permiso pedido, el servidor aún pide esperar: lo vuelve a consultar sin perder lo capturado',
        (tester) async {
      final (arnes, permisos) = await abrirConEspera(tester, bajoPermiso: true);

      await cerrarInforme(tester);

      expect(find.text('El permiso aún no se ve concedido'), findsOneWidget);
      expect(find.textContaining('Nada se perdió'), findsOneWidget);
      expect(arnes.cola.jobs.single.fallido, isFalse, reason: 'esperar no es un rechazo: sigue en la cola');
      final consultasAntes = permisos.consultas;
      await tester.tap(find.widgetWithText(TextButton, 'OK'));
      await fotogramas(tester);

      expect(find.text('Permiso para el cargue'), findsOneWidget);
      expect(permisos.consultas, greaterThan(consultasAntes), reason: 'lo vuelve a consultar');
      // El reloj del teléfono dice cero, pero el servidor acaba de decir que no: ofrecer seguir repetiría el rechazo.
      expect(find.textContaining('El servidor aún no lo concede; se reintenta solo'), findsOneWidget);
      expect(find.widgetWithText(FilledButton, 'Continuar el cargue'), findsNothing);

      // El servidor ya lo ve concedido: ahora sí se sigue, y lo capturado sale.
      permisos.permiso = permisoDe(viajeId: 'v1', concedido: true, porTiempo: true);
      await tester.tap(find.widgetWithText(OutlinedButton, 'Actualizar'));
      await fotogramas(tester);
      await tester.tap(find.widgetWithText(FilledButton, 'Continuar el cargue'));
      await fotogramas(tester);

      expect(arnes.cola.jobs, isEmpty, reason: 'al seguir, lo capturado salió con el permiso ya concedido');
      expect(find.text('La ficha del carro'), findsOneWidget);
    });

    // Sin permiso pedido el servidor usa el mismo motivo, pero el tiempo no lo arregla: es un rechazo, con su mensaje.
    testWidgets('sin un permiso pedido, ese mismo 403 se dice como rechazo y no abre ningún permiso', (tester) async {
      final (arnes, _) = await abrirConEspera(tester, bajoPermiso: false);

      await cerrarInforme(tester);

      expect(find.text('No se pudo enviar'), findsOneWidget);
      expect(find.textContaining('pida el permiso de la excepción'), findsOneWidget, reason: 'el mensaje del servidor');
      expect(find.text('El permiso aún no se ve concedido'), findsNothing);
      expect(arnes.cola.jobs.single.fallido, isTrue);
      await tester.tap(find.widgetWithText(TextButton, 'OK'));
      await fotogramas(tester);

      expect(find.text('Permiso para el cargue'), findsNothing);
      expect(find.text('La ficha del carro'), findsOneWidget);
    });

  });

  group('el checklist de vistas', () {
    testWidgets('lista las vistas del catálogo, con su condición', (tester) async {
      await abrir(tester, ViajesRepositorioFalso()..viajeDeEtapa = viaje('v1'), vistas: vistasDePrueba);

      expect(find.text('Fotos de la inspección'), findsOneWidget);
      // «Frontal» también es una pestaña del dibujo de daños: se busca solo dentro del checklist.
      expect(find.descendant(of: find.byType(FilaVista), matching: find.text('Frontal')), findsOneWidget);
      expect(find.text('Piezas en el baúl'), findsOneWidget);
      expect(find.text('Solo si hay faltante o daño'), findsOneWidget,
          reason: 'la adicional dice cuándo se toma; la obligatoria no lo necesita');
      expect(find.widgetWithText(TextButton, 'Tomar'), findsNWidgets(2));
    });

    // Sin catálogo no se inventa un checklist: pedir vistas que nadie configuró sería peor que no pedir ninguna.
    testWidgets('sin vistas descargadas no se inventan', (tester) async {
      await abrir(tester, ViajesRepositorioFalso()..viajeDeEtapa = viaje('v1'));

      expect(find.text('Fotos de la inspección'), findsNothing);
      expect(find.widgetWithText(TextButton, 'Tomar'), findsNothing);
    });

    // En Transporte la salida y la llegada son el registro del conductor: lo que se exige es el GPS, no 7 fotos.
    testWidgets('en Transporte no hay checklist y se dice por qué', (tester) async {
      await abrir(
        tester,
        ViajesRepositorioFalso()..viajeDeEtapa = viaje('v1'),
        vistas: vistasDePrueba,
        procesoCodigo: 'transporte',
      );

      expect(find.text('Fotos de la inspección'), findsNothing);
      expect(find.textContaining('no pide fotos por vista'), findsOneWidget);
    });

    testWidgets('el título dice qué momento se está capturando', (tester) async {
      await abrir(tester, ViajesRepositorioFalso()..viajeDeEtapa = viaje('v1'), vistas: vistasDePrueba, momento: Momento.salida);

      expect(find.text('Inspección de salida · cargue'), findsOneWidget);
    });
  });

  // La segunda inspección del cargue (la salida) no pide las vistas de estado: documenta dónde quedó cada unidad en
  // la niñera. Es la misma regla con la que el servidor la deja cerrar.
  group('la acomodación de la segunda inspección del cargue', () {
    // A es el carro de la captura: el único que se acomoda en ella.
    const vinA = 'VIN00000000000001';
    const vinB = 'VINB0000000000002';

    // El VIN de la captura también va en su encabezado: se busca dentro de la acomodación.
    Finder enLaNinera(String vin) => find.descendant(of: find.byType(SeccionAcomodacion), matching: find.text(vin));

    Future<void> abrirAcomodacion(WidgetTester tester, {Map<String, int> posiciones = const {}}) => abrir(
          tester,
          ViajesRepositorioFalso()..viajeDeEtapa = viaje('v1', vines: [vinA, vinB], posiciones: posiciones),
          vistas: vistasDePrueba,
          momento: Momento.salida,
        );

    testWidgets('en vez de las vistas pide el lugar de cada unidad del viaje', (tester) async {
      await abrirAcomodacion(tester);

      expect(find.text('Acomodación en la niñera'), findsOneWidget);
      expect(find.text('Fotos de la inspección'), findsNothing);
      expect(enLaNinera(vinA), findsOneWidget);
      expect(enLaNinera(vinB), findsOneWidget);
    });

    testWidgets('en la llegada del cargue sigue pidiendo las vistas', (tester) async {
      await abrir(tester, ViajesRepositorioFalso()..viajeDeEtapa = viaje('v1', vines: [vinA]), vistas: vistasDePrueba);

      expect(find.text('Acomodación en la niñera'), findsNothing);
      expect(find.text('Fotos de la inspección'), findsOneWidget);
    });

    testWidgets('no deja cerrar mientras falte el lugar de una unidad, y dice cuál', (tester) async {
      await abrirAcomodacion(tester);

      final boton = tester.widget<FilledButton>(find.widgetWithText(FilledButton, 'Cerrar informe'));
      expect(boton.onPressed, isNull);
      expect(find.text('Falta la posición de la unidad $vinA'), findsOneWidget);
    });

    testWidgets('con este carro en su lugar, lo que falta de otro se pone desde la niñera', (tester) async {
      await abrirAcomodacion(tester);

      await tester.tap(find.descendant(of: find.widgetWithText(Tarjeta, vinA), matching: find.widgetWithText(ChoiceChip, '4')));
      await tester.pump();

      expect(find.text('Falta la posición de la unidad $vinB: ponla desde la pantalla de la niñera'), findsOneWidget);
    });

    // Dentro de la salida de un carro solo se acomoda ese carro: los demás se ven, pero se acomodan desde la niñera.
    testWidgets('los otros carros se ven con su puesto, pero no se acomodan aquí', (tester) async {
      await abrirAcomodacion(tester);

      expect(find.descendant(of: find.widgetWithText(Tarjeta, vinB), matching: find.byType(ChoiceChip)), findsNothing);
      expect(
        find.descendant(of: find.widgetWithText(Tarjeta, vinB), matching: find.text('Sin puesto: se pone desde la pantalla de la niñera')),
        findsOneWidget,
      );
    });

    testWidgets('un lugar que ya tiene otra unidad no se puede elegir', (tester) async {
      await abrirAcomodacion(tester, posiciones: {vinB: 4});

      final chip = tester.widget<ChoiceChip>(
        find.descendant(of: find.widgetWithText(Tarjeta, vinA), matching: find.widgetWithText(ChoiceChip, '4')),
      );
      expect(chip.onSelected, isNull);
    });

    testWidgets('las posiciones que el viaje ya tenía llegan marcadas', (tester) async {
      await abrirAcomodacion(tester, posiciones: {vinA: 4, vinB: 3});

      final elegidas = tester
          .widgetList<ChoiceChip>(find.descendant(of: find.widgetWithText(Tarjeta, vinA), matching: find.byType(ChoiceChip)))
          .where((c) => c.selected);
      expect(elegidas.map((c) => ((c.label as SizedBox).child as Text).data), ['4']);
      expect(find.descendant(of: find.widgetWithText(Tarjeta, vinB), matching: find.text('Puesto 3')), findsOneWidget);
      expect(find.text('Sin posición todavía'), findsNothing);
    });

    // S8-HU-01.3 CA-3 y ESC-01: una foto de la niñera tomada en la acomodación de otro carro sirve para este.
    testWidgets('las fotos de la acomodación se cuentan por viaje', (tester) async {
      await abrir(
        tester,
        ViajesRepositorioFalso()..viajeDeEtapa = viaje('v1', vines: [vinA], posiciones: {vinA: 1}, fotosAcomodacion: 1),
        vistas: vistasDePrueba,
        momento: Momento.salida,
      );

      expect(find.textContaining('Faltan fotos de la acomodación'), findsNothing);
    });

    // S8-HU-01.3 CA-3 y S8-HU-09 ESC-06: el mínimo es el que manda el servidor, no el de hoy.
    testWidgets('el mínimo de fotos de la niñera es el que manda el servidor', (tester) async {
      final arnes = Arnes()..parametros.valor = const ParametrosApp(acomodacionFotosMinimas: 2);
      await abrir(
        tester,
        ViajesRepositorioFalso()..viajeDeEtapa = viaje('v1', vines: [vinA], posiciones: {vinA: 1}, fotosAcomodacion: 1),
        arnes: arnes,
        vistas: vistasDePrueba,
        momento: Momento.salida,
      );

      expect(find.text('Faltan fotos de la acomodación: se exige al menos 2 y hay 1'), findsOneWidget);
      expect(find.text('Se piden al menos 2 fotos de la niñera. Llevan fecha y ubicación como las demás.'), findsOneWidget);
    });

    // S8-HU-09.2 CA-1: sin respuesta del servidor ni copia guardada, la regla de hoy (1 foto) y no otra.
    testWidgets('sin parámetros del servidor ni copia pide la foto de hoy', (tester) async {
      await abrir(
        tester,
        ViajesRepositorioFalso()..viajeDeEtapa = viaje('v1', vines: [vinA], posiciones: {vinA: 1}, fotosAcomodacion: 1),
        vistas: vistasDePrueba,
        momento: Momento.salida,
      );

      expect(find.textContaining('Faltan fotos de la acomodación'), findsNothing);
      expect(find.text('Se piden al menos 1 foto de la niñera. Llevan fecha y ubicación como las demás.'), findsOneWidget);
    });

    // S8-HU-02.2 CA-6: un carro bajado ya no va en la niñera ni pide puesto.
    testWidgets('un carro bajado no aparece en la niñera ni pide puesto', (tester) async {
      await abrir(
        tester,
        ViajesRepositorioFalso()
          ..viajeDeEtapa = viaje('v1', vines: [vinA, vinB], posiciones: {vinA: 1}, estados: {vinB: 'bajado'}),
        vistas: vistasDePrueba,
        momento: Momento.salida,
      );

      expect(enLaNinera(vinA), findsOneWidget);
      expect(enLaNinera(vinB), findsNothing);
      expect(find.textContaining('Falta la posición'), findsNothing);
    });

    testWidgets('sin poder traer el viaje lo dice y no deja cerrar', (tester) async {
      await abrir(
        tester,
        ViajesRepositorioFalso()..errorDeLaEtapa = Exception('sin red'),
        vistas: vistasDePrueba,
        momento: Momento.salida,
      );

      expect(find.text('No se pudieron cargar las unidades del viaje.'), findsOneWidget);
      final boton = tester.widget<FilledButton>(find.widgetWithText(FilledButton, 'Cerrar informe'));
      expect(boton.onPressed, isNull);
    });
  });

  // Pautas del Entendimiento (S5-HU-24.12): la placa es un código que se lee carácter a carácter contra la
  // unidad que se tiene delante, igual que el VIN.
  testWidgets('la placa va en monoespaciada, como el VIN', (tester) async {
    await abrir(tester, ViajesRepositorioFalso()..viajeDeEtapa = viaje('v1', placa: 'XKL482'));

    final placa = tester.widget<Text>(find.text('XKL482'));
    expect(placa.style?.fontFamily, 'monospace');
  });

  // S8-HU-03.2: el muelle y el tráiler en el cargue, y el cierre que exige el muelle donde hay muelles.
  group('el muelle y el tráiler del cargue', () {
    const vinA = 'VINA0000000000001';
    const muelles = [
      {'id': 'm1', 'codigo': 'm1', 'nombre': 'Muelle 1', 'orden': 1, 'activo': true, 'lugar_id': 'cedi'},
      {'id': 'm3', 'codigo': 'm3', 'nombre': 'Muelle 3', 'orden': 3, 'activo': true, 'lugar_id': 'cedi'},
      {'id': 'mx', 'codigo': 'm1', 'nombre': 'Muelle de otro lugar', 'orden': 1, 'activo': true, 'lugar_id': 'otro'},
    ];
    const luis = Sesion(token: 'jwt', usuario: Usuario(id: 'luis', nombre: 'Luis Gómez', email: '', rol: 'conductor'));

    Future<void> avanzar(WidgetTester tester) async {
      for (var i = 0; i < 8; i++) {
        await tester.pump(const Duration(milliseconds: 50));
      }
    }

    testWidgets('el inspector del viaje ve el muelle y elige uno de los muelles de su lugar', (tester) async {
      final viajes = ViajesRepositorioFalso()
        ..viajeDeEtapa = viaje('v1', vines: [vinA], inspectorId: 'u1', lugarCargueId: 'cedi')
        ..viajeTrasCambiarCargue =
            viaje('v1', vines: [vinA], inspectorId: 'u1', lugarCargueId: 'cedi', muelleId: 'm3', muelle: 'Muelle 3');
      await abrir(tester, viajes, muelles: muelles);

      expect(find.text('Sin asignar'), findsOneWidget);
      await tester.tap(find.widgetWithText(FilledButton, 'Elegir muelle'));
      await avanzar(tester);
      expect(find.text('Muelle 1'), findsOneWidget);
      expect(find.text('Muelle de otro lugar'), findsNothing, reason: 'solo los del lugar de cargue');

      await tester.tap(find.text('Muelle 3'));
      await avanzar(tester);

      expect(viajes.carguesCambiados, [
        {'viaje_id': 'v1', 'muelle_id': 'm3'},
      ]);
      expect(find.text('Muelle 3 asignado al viaje'), findsOneWidget);
      expect(find.text('Muelle 3'), findsOneWidget, reason: 'la pantalla muestra el viaje ya cambiado');
      expect(find.widgetWithText(FilledButton, 'Cambiar muelle'), findsOneWidget);
    });

    testWidgets('en un lugar sin muelles no hay selector, pero el tráiler sí se cambia', (tester) async {
      final viajes = ViajesRepositorioFalso()
        ..viajeDeEtapa = viaje('v1', vines: [vinA], trailer: 'TRX-101', inspectorId: 'u1', lugarCargueId: 'sin-muelles');
      await abrir(tester, viajes, muelles: muelles);

      expect(find.widgetWithText(FilledButton, 'Elegir muelle'), findsNothing);
      await tester.tap(find.widgetWithText(OutlinedButton, 'Cambiar tráiler'));
      await avanzar(tester);
      await tester.enterText(find.byType(TextField).last, 'TRX-202');
      await tester.tap(find.widgetWithText(TextButton, 'Guardar'));
      await avanzar(tester);

      expect(viajes.carguesCambiados, [
        {'viaje_id': 'v1', 'trailer': 'TRX-202'},
      ]);
      expect(find.text('Tráiler cambiado a TRX-202'), findsOneWidget);
    });

    testWidgets('con el cargue ya cerrado el servidor lo rechaza y se muestra su mensaje', (tester) async {
      final viajes = ViajesRepositorioFalso()
        ..viajeDeEtapa = viaje('v1', vines: [vinA], inspectorId: 'u1', lugarCargueId: 'cedi')
        ..errorCambiarCargue = errorApi(409, 'El tráiler y el muelle solo los cambia el inspector durante el cargue');
      await abrir(tester, viajes, muelles: muelles);

      await tester.tap(find.widgetWithText(FilledButton, 'Elegir muelle'));
      await avanzar(tester);
      await tester.tap(find.text('Muelle 1'));
      await avanzar(tester);

      expect(find.text('El tráiler y el muelle solo los cambia el inspector durante el cargue'), findsOneWidget);
    });

    testWidgets('el conductor, otro inspector y el inspector con el cargue cerrado no ven los controles', (tester) async {
      for (final (arnes, delViaje) in [
        (Arnes(sesion: luis), viaje('v1', vines: [vinA], inspectorId: 'u1', lugarCargueId: 'cedi')),
        (Arnes(), viaje('v1', vines: [vinA], inspectorId: 'otro', lugarCargueId: 'cedi')),
        (Arnes(), viaje('v1', vines: [vinA], inspectorId: 'u1', lugarCargueId: 'cedi', cargueCerrado: true)),
      ]) {
        await abrir(tester, ViajesRepositorioFalso()..viajeDeEtapa = delViaje, arnes: arnes, muelles: muelles);

        expect(find.text('Muelle'), findsOneWidget, reason: 'el muelle se ve igual');
        expect(find.widgetWithText(OutlinedButton, 'Cambiar tráiler'), findsNothing);
        expect(find.widgetWithText(FilledButton, 'Elegir muelle'), findsNothing);
      }
    });

    // ESC-03: donde el lugar no tiene muelles, el muelle no se muestra como faltante.
    testWidgets('en un cargue donde el lugar no tiene muelles no se dice «Sin asignar»', (tester) async {
      await abrir(
        tester,
        ViajesRepositorioFalso()..viajeDeEtapa = viaje('v1', vines: [vinA], inspectorId: 'u1', lugarCargueId: 'sin-muelles'),
        muelles: muelles,
      );

      expect(find.text('Muelle'), findsNothing);
      expect(find.text('Sin asignar'), findsNothing);
    });

    testWidgets('fuera del cargue no se habla de muelle si el viaje no tiene', (tester) async {
      await abrir(tester, ViajesRepositorioFalso()..viajeDeEtapa = viaje('v1', inspectorId: 'u1'), procesoCodigo: 'descargue');

      expect(find.text('Muelle'), findsNothing);
      expect(find.widgetWithText(OutlinedButton, 'Cambiar tráiler'), findsNothing);
    });

    testWidgets('donde hay muelles, la acomodación no cierra sin el muelle del viaje', (tester) async {
      await abrir(
        tester,
        ViajesRepositorioFalso()
          ..viajeDeEtapa = viaje('v1', vines: [vinA], posiciones: {vinA: 1}, inspectorId: 'u1', lugarCargueId: 'cedi'),
        momento: Momento.salida,
        muelles: muelles,
      );

      expect(find.text('Falta el muelle del viaje'), findsOneWidget);
      final boton = tester.widget<FilledButton>(find.widgetWithText(FilledButton, 'Cerrar informe'));
      expect(boton.onPressed, isNull);
    });

    testWidgets('con el muelle asignado, o en un lugar sin muelles, el muelle no bloquea', (tester) async {
      for (final delViaje in [
        viaje('v1', vines: [vinA], posiciones: {vinA: 1}, lugarCargueId: 'cedi', muelleId: 'm1', muelle: 'Muelle 1'),
        viaje('v1', vines: [vinA], posiciones: {vinA: 1}, lugarCargueId: 'sin-muelles'),
      ]) {
        await abrir(tester, ViajesRepositorioFalso()..viajeDeEtapa = delViaje, momento: Momento.salida, muelles: muelles);

        expect(find.text('Falta el muelle del viaje'), findsNothing);
        expect(find.textContaining('Faltan fotos de la acomodación'), findsOneWidget, reason: 'sigue con lo siguiente');
      }
    });
  });

  // S8-HU-11.2 CA-2: «Continuar» sobre un borrador del servidor muestra lo que ya tiene, no vuelve a pedir la vista
  // que ya está allá, y el envío arranca con su id: no crea otro informe ni repite fotos.
  group('continuar un borrador del servidor', () {
    Map<String, dynamic> inventarioConBorrador() => {
          'id': 'v1',
          'vin': 'VIN00000000000001',
          'marca': 'Renault',
          'operacion': 'nacional',
          'etapas': [
            {
              'id': 'e1',
              'orden': 1,
              'proceso': 'cargue',
              'requiere_inspeccion': true,
              'viaje_id': 'v1',
              'inspecciones': [
                {
                  'id': 'b1',
                  'momento': 'llegada',
                  'momento_nombre': 'Inspección de llegada',
                  'fecha': '2026-01-02T09:00:00Z',
                  'estado': 'borrador',
                  'usuario_id': 'u1',
                  'hallazgos': [
                    {'id': 'h1', 'zona': 'Izquierdo 13 · Puerta delantera', 'clase_dano': 'golpe', 'tamano_dano': 'menos_5cm'},
                  ],
                  'evidencias': [
                    {'id': 'ev1', 'url': null, 'hallazgo_id': null, 'vista_id': 'v-frontal'},
                    {'id': 'ev2', 'url': null, 'hallazgo_id': 'h1'},
                  ],
                },
              ],
            },
          ],
        };

    testWidgets('muestra lo que ya tiene y la vista subida no se vuelve a pedir', (tester) async {
      await abrir(
        tester,
        ViajesRepositorioFalso()..viajeDeEtapa = viaje('v1'),
        vistas: vistasDePrueba,
        borradorId: 'b1',
        inventario: inventarioConBorrador(),
      );

      expect(find.textContaining('Tiene 1 foto y 1 daño'), findsOneWidget);
      expect(find.textContaining('Izquierdo 13 · Puerta delantera'), findsOneWidget);
      expect(
        find.descendant(of: find.byType(FilaVista), matching: find.text('Ya está en el servidor')),
        findsOneWidget,
      );
      expect(find.textContaining('Falta la foto de la vista'), findsNothing);
      final cerrar = tester.widget<FilledButton>(find.widgetWithText(FilledButton, 'Cerrar informe'));
      expect(cerrar.onPressed, isNotNull, reason: 'la obligatoria ya está en el servidor');
    });

    testWidgets('al cerrar no crea otro informe: cierra ese mismo', (tester) async {
      final arnes = await abrir(
        tester,
        ViajesRepositorioFalso()..viajeDeEtapa = viaje('v1'),
        vistas: vistasDePrueba,
        borradorId: 'b1',
        inventario: inventarioConBorrador(),
      );

      await cerrarInforme(tester);

      expect(arnes.gateway.llamadas, isNot(contains('crearInspeccion')));
      expect(arnes.gateway.llamadas, contains('cerrarInspeccion'));
      expect(arnes.gateway.evidencias, isEmpty, reason: 'lo que ya estaba no se vuelve a subir');
    });

    testWidgets('sin el borrador a la vista, la vista se pide como siempre', (tester) async {
      await abrir(
        tester,
        ViajesRepositorioFalso()..viajeDeEtapa = viaje('v1'),
        vistas: vistasDePrueba,
        borradorId: 'otro',
        inventario: inventarioConBorrador(),
      );

      expect(find.textContaining('No se pudo leer lo que tiene'), findsOneWidget);
      expect(find.textContaining('Falta la foto de la vista'), findsOneWidget);
    });
  });
}
