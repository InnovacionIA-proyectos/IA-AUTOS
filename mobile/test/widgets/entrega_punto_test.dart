import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/core/dispositivo/servicios.dart';
import 'package:traza_autos/core/errores/error_api.dart';
import 'package:traza_autos/core/tema/tema.dart';
import 'package:traza_autos/features/auth/domain/sesion.dart';
import 'package:traza_autos/features/auth/presentation/auth_controller.dart';
import 'package:traza_autos/features/catalogos/providers.dart';
import 'package:traza_autos/features/custodia/presentation/lienzo_firma.dart';
import 'package:traza_autos/features/inspecciones/domain/foto_local.dart';
import 'package:traza_autos/features/sync/domain/job.dart';
import 'package:traza_autos/features/sync/presentation/sync_controller.dart';
import 'package:traza_autos/features/viajes/domain/entrega_punto.dart';
import 'package:traza_autos/features/viajes/presentation/entrega_punto_page.dart';
import 'package:traza_autos/features/viajes/providers.dart';

import '../support/arnes.dart';
import '../support/dobles_catalogos.dart';
import '../support/dobles_viajes.dart';

/// S8-HU-04.2: «Entregar en esta parada» por rol, el rechazo por novedad, las reservas, el cierre con una sola firma
/// con su evidencia, la hora y el GPS del momento y «Llegué a esta parada». Sin interruptor: quién recibe lo dice el
/// servidor.

const _vin1 = 'VINAAAAAAAAAAAAA1';
const _vin2 = 'VINBBBBBBBBBBBBB2';

const _coordinador = Sesion(
  token: 'jwt',
  usuario: Usuario(id: 'coord-1', nombre: 'Sara Calderón', email: '', rol: 'coordinador_calidad'),
);

/// Un golpe con foto en la revisión de llegada del carro: lo que justifica rechazarlo.
const _golpe = DanoDeLlegada(hallazgoId: 'h-golpe', zona: 'Izquierdo 7 · Puerta delantera', clase: 'golpe');

Future<void> avanzar(WidgetTester tester) async {
  for (var i = 0; i < 12; i++) {
    await tester.pump(const Duration(milliseconds: 50));
  }
}

/// Abre [pantalla] con la sesión de [arnes]: la lista de paradas o una parada sola.
Future<Arnes> abrir(
  WidgetTester tester,
  EntregasRepositorioFalso entregas, {
  Arnes? arnes,
  Widget pantalla = const EntregasViajePage(viajeId: 'v1'),
  Size tamano = const Size(600, 4000),
}) async {
  await tester.binding.setSurfaceSize(tamano);
  addTearDown(() => tester.binding.setSurfaceSize(null));
  final usado = arnes ?? Arnes(sesion: sesionConductor);
  await tester.pumpWidget(
    ProviderScope(
      retry: (_, _) => null,
      overrides: [
        ...usado.overrides,
        entregasRepositorioProvider.overrideWithValue(entregas),
        // «Llegué» y la entrega recargan «Mis viajes» al terminar: sin esto saldrían a la red.
        viajesRepositorioProvider.overrideWithValue(ViajesRepositorioFalso()),
        lugaresRepositorioProvider.overrideWithValue(LugaresRepositorioFalso()),
        tomarFotoDeCierreProvider.overrideWithValue((_, _) async => _fotoDelCierre),
        // S8-HU-05.2: donde se ofrece «No hay inspector» la pantalla tiene listos los motivos del catálogo.
        catalogosRepositorioProvider.overrideWithValue(
          CatalogosRepositorioFalso()..guardado = catalogosCon('Golpe', motivos: motivosDePrueba),
        ),
      ],
      child: MaterialApp(theme: crearTema(), home: _Arranque(pantalla)),
    ),
  );
  await avanzar(tester);
  return usado;
}

const _fotoDelCierre = FotoLocal(ruta: '/tmp/cierre.jpg', selloFecha: '2026-11-18T20:30:00.000Z', selloLat: 10.39, selloLng: -75.51);

/// La parada sola, como la abre «Entregar en esta parada».
Widget parada(String lugar) => EntregaParadaPage(viajeId: 'v1', lugarId: 'l-$lugar');

class _Arranque extends ConsumerWidget {
  const _Arranque(this.pantalla);

  final Widget pantalla;

  @override
  Widget build(BuildContext context, WidgetRef ref) =>
      ref.watch(authControllerProvider.select((e) => e.fase)) == FaseAuth.autenticado ? pantalla : const SizedBox();
}

/// Dibuja en el lienzo n: el 0 es el de quien firma y el 1, cuando lo hay, el de la persona del lugar.
Future<void> firmarEn(WidgetTester tester, int indice) async {
  final lienzo = tester.getBottomLeft(find.byType(LienzoFirma).at(indice)) + const Offset(60, -80);
  await tester.dragFrom(lienzo, const Offset(40, 30));
  await tester.pump();
}

Future<void> firmar(WidgetTester tester) => firmarEn(tester, 0);

FilledButton boton(WidgetTester tester, String texto) => tester.widget<FilledButton>(find.widgetWithText(FilledButton, texto));

/// El motivo que va pegado debajo de un botón gris (centrado), y no el mismo texto en otra parte de la pantalla.
Finder motivoDelBoton(String texto) =>
    find.byWidgetPredicate((w) => w is Text && w.data == texto && w.textAlign == TextAlign.center);

Future<void> tocar(WidgetTester tester, Finder cual) async {
  await tester.ensureVisible(cual);
  await tester.tap(cual);
  await avanzar(tester);
}

ErrorApi rechazoDeParada(int status, String motivo, String mensaje) =>
    ErrorApi(message: mensaje, code: status == 403 ? 'FORBIDDEN' : 'CONFLICT', status: status, details: {'motivo': motivo});

void main() {
  group('la lista de paradas', () {
    // CA-8: cada parada dice cómo va, también la que se firmó como constancia del rechazo de todos sus carros.
    testWidgets('cada parada dice su estado', (tester) async {
      await abrir(
        tester,
        EntregasRepositorioFalso()
          ..puntos = [
            puntoDe('Pendiente Norte'),
            puntoDe('Entregada Sur', estado: EstadoEntrega.cerrada),
            puntoDe(
              'Con rechazo',
              estadoParada: EstadoParada.entregadaConRechazos,
              estados: {_vin1: EstadoEntrega.cerrada, _vin2: EstadoEntrega.rechazado},
            ),
            puntoDe(
              'Todos rechazados',
              vines: const ['VIN000000000000A1', 'VIN000000000000A2', 'VIN000000000000A3'],
              estado: EstadoEntrega.rechazado,
              estadoParada: EstadoParada.firmadaTodosRechazados,
            ),
            puntoDe('Sin nadie', receptor: ReceptorParada.sinReceptor),
          ],
      );

      expect(find.text('Pendiente'), findsOneWidget);
      expect(find.text('Entregada'), findsOneWidget);
      expect(find.text('Entregada · 1 carro rechazado'), findsOneWidget);
      expect(find.text('Firmada · 3 carros rechazados'), findsOneWidget);
      expect(find.text('Sin receptor'), findsOneWidget);
      expect(find.text('Rechazado por novedad: espera el nuevo destino del coordinador'), findsNWidgets(4));
    });

    testWidgets('los carros sin destino salen aparte, con a quién pedírselo', (tester) async {
      await abrir(
        tester,
        EntregasRepositorioFalso()
          ..puntos = [puntoDe('Autonorte')]
          ..unidadesSinDestino = 2,
      );

      expect(find.textContaining('2 carros no tienen destino'), findsOneWidget);
      expect(find.textContaining('Pídele al coordinador que le asigne un destino'), findsOneWidget);
    });

    // CA-1: el conductor entrega en la parada donde ya marcó «Llegué»; antes, lo que tiene es «Llegué a esta parada».
    testWidgets('el conductor entrega donde ya llegó, y donde no, marca «Llegué a esta parada»', (tester) async {
      await abrir(
        tester,
        EntregasRepositorioFalso()
          ..puntos = [
            puntoDe('Puerto de Cartagena'),
            puntoDe('Concesionario Norte', vines: const ['VINCCCCCCCCCCCCC3'], conductorLlego: false),
          ],
      );

      expect(find.widgetWithText(FilledButton, 'Entregar en esta parada'), findsOneWidget);
      expect(find.widgetWithText(FilledButton, 'Llegué a esta parada'), findsOneWidget);
    });

    testWidgets('sin señal no se pueden cargar las paradas y se puede reintentar', (tester) async {
      final entregas = EntregasRepositorioFalso()..error = Exception('sin red');
      await abrir(tester, entregas);

      expect(find.text('No se pudieron cargar las paradas'), findsOneWidget);
      entregas.error = null;
      entregas.puntos = [puntoDe('Autonorte')];
      await tester.tap(find.widgetWithText(FilledButton, 'Reintentar'));
      await avanzar(tester);

      expect(find.text('Aquí entregas estos 2 carros'), findsOneWidget);
    });

    // CA-9: un toque manda la llegada de cada carro por la cola, con la misma hora y el mismo GPS; el carro que el
    // servidor no deja marcar sale aparte con su motivo y no frena a los demás.
    testWidgets('«Llegué a esta parada» manda la llegada de cada carro y deja aparte el que no se pudo', (tester) async {
      final arnes = Arnes(sesion: sesionConductor);
      arnes.ubicacion.posicion = Posicion(10.39, -75.51, tomadaEn: DateTime.now());
      arnes.gateway.errores['confirmarLlegada'] = [errorApi(409, 'El cargue del VIN $_vin1 no está cerrado')];
      await abrir(tester, EntregasRepositorioFalso()..puntos = [puntoDe('Concesionario Norte', conductorLlego: false)], arnes: arnes);

      await tocar(tester, find.widgetWithText(FilledButton, 'Llegué a esta parada'));
      expect(find.textContaining('$_vin1\n$_vin2'), findsOneWidget, reason: 'la confirmación dice qué carros');
      await tocar(tester, find.text('Confirmar'));

      final llegadas = arnes.gateway.llegadasEnviadas;
      expect(llegadas.map((l) => l.$2), [_vin1, _vin2]);
      expect(llegadas.map((l) => (l.$3, l.$4, l.$5)).toSet(), hasLength(1), reason: 'la misma hora y el mismo lugar');
      expect((llegadas.first.$4, llegadas.first.$5), (10.39, -75.51));
      expect(find.textContaining('Quedó la llegada de 1 carro.'), findsOneWidget);
      expect(find.textContaining('· $_vin1: El cargue del VIN $_vin1 no está cerrado'), findsOneWidget);
      final fallido = arnes.cola.jobs.single as LlegadaJob;
      expect((fallido.vin, fallido.fallido), (_vin1, true));
    });

    // 1: con la llegada ya en el servidor, la parada ofrece entregar sin tener que actualizar a mano.
    testWidgets('después de «Llegué a esta parada» la parada ofrece entregar', (tester) async {
      final entregas = EntregasRepositorioFalso()..puntos = [puntoDe('Concesionario Norte', conductorLlego: false)];
      await abrir(tester, entregas);

      await tocar(tester, find.widgetWithText(FilledButton, 'Llegué a esta parada'));
      // Lo que dirá el servidor cuando le llegue la llegada.
      entregas.puntos = [puntoDe('Concesionario Norte')];
      await tocar(tester, find.text('Confirmar'));
      await tocar(tester, find.text('OK'));

      expect(find.widgetWithText(FilledButton, 'Entregar en esta parada'), findsOneWidget);
      expect(find.widgetWithText(FilledButton, 'Llegué a esta parada'), findsNothing);
    });

    // 1: la llegada que esperaba señal sale después, sola; la parada se actualiza cuando la cola la entrega.
    testWidgets('cuando la cola entrega la llegada que esperaba señal, la parada se actualiza', (tester) async {
      final arnes = Arnes(sesion: sesionConductor)..red.estable = false;
      final entregas = EntregasRepositorioFalso()..puntos = [puntoDe('Concesionario Norte', conductorLlego: false)];
      await abrir(tester, entregas, arnes: arnes);

      await tocar(tester, find.widgetWithText(FilledButton, 'Llegué a esta parada'));
      await tocar(tester, find.text('Confirmar'));
      await tocar(tester, find.text('OK'));
      expect(find.text('La llegada a esta parada está guardada: se enviará cuando haya señal.'), findsOneWidget);

      entregas.puntos = [puntoDe('Concesionario Norte')];
      arnes.red.estable = true;
      final contenedor = ProviderScope.containerOf(tester.element(find.byType(EntregasViajePage)));
      await contenedor.read(syncControllerProvider.notifier).sincronizar();
      await avanzar(tester);

      expect(arnes.cola.jobs, isEmpty);
      expect(find.widgetWithText(FilledButton, 'Entregar en esta parada'), findsOneWidget);
    });

    // 10: lo que detiene la llegada no siempre es la señal: se dice lo que dijo el servidor.
    testWidgets('si la llegada espera por el cargue sin cerrar, lo dice en vez de culpar a la señal', (tester) async {
      final arnes = Arnes(sesion: sesionConductor);
      arnes.gateway.errores['confirmarLlegada'] = [
        for (var i = 0; i < 2; i++)
          const ErrorApi(
            message: 'Falta cerrar el Cargue',
            code: 'ETAPA_PREVIA_SIN_CIERRE',
            status: 409,
            details: {'etapa_previa': 'Cargue'},
          ),
      ];
      await abrir(tester, EntregasRepositorioFalso()..puntos = [puntoDe('Concesionario Norte', conductorLlego: false)], arnes: arnes);

      await tocar(tester, find.widgetWithText(FilledButton, 'Llegué a esta parada'));
      await tocar(tester, find.text('Confirmar'));
      await tocar(tester, find.text('OK'));

      expect(find.textContaining('espera: Falta cerrar Cargue'), findsOneWidget);
      expect(find.textContaining('cuando haya señal'), findsNothing);
    });

    testWidgets('sin GPS «Llegué a esta parada» espera la señal', (tester) async {
      final arnes = Arnes(sesion: sesionConductor);
      arnes.ubicacion.error = const UbicacionNoDisponible('sin señal');
      await abrir(tester, EntregasRepositorioFalso()..puntos = [puntoDe('Concesionario Norte', conductorLlego: false)], arnes: arnes);

      expect(boton(tester, 'Llegué a esta parada').onPressed, isNull);
      expect(find.text('Sin ubicación: espera la señal del GPS'), findsOneWidget);
    });
  });

  group('el conductor en una parada con inspector', () {
    // CA-3: sin interruptor y sin pedir datos de nadie: firma él, desde su app, y el inspector desde la suya.
    testWidgets('dice que firma el inspector del punto y no pide datos de nadie', (tester) async {
      await abrir(tester, EntregasRepositorioFalso()..puntos = [puntoDe('Puerto', receptorNombre: 'Pedro')], pantalla: parada('Puerto'));

      expect(find.text('Aquí firma el inspector del punto'), findsOneWidget);
      // S8-HU-05.2 CA-5: el nombre de quien la tomó y que se firma con él.
      expect(find.text('Pedro tomó la entrega: firma con él'), findsOneWidget);
      expect(find.byType(SwitchListTile), findsNothing);
      expect(find.widgetWithText(TextField, 'Nombre de quien recibe'), findsNothing);
      expect(find.byType(LienzoFirma), findsOneWidget);
      expect(find.widgetWithText(TextButton, 'Rechazar por novedad'), findsNothing, reason: 'rechaza quien recibe');
    });

    // CA-7: la hora y el GPS del momento van con la firma.
    testWidgets('la firma sale con la hora y el GPS del momento', (tester) async {
      final arnes = Arnes(sesion: sesionConductor);
      arnes.ubicacion.posicion = Posicion(10.39, -75.51, precision: 6, tomadaEn: DateTime.now());
      await abrir(tester, EntregasRepositorioFalso()..puntos = [puntoDe('Puerto')], arnes: arnes, pantalla: parada('Puerto'));

      expect(boton(tester, 'Entregar los 2 carros').onPressed, isNull, reason: 'sin trazo no hay nada que registrar');
      await firmar(tester);
      await tocar(tester, find.widgetWithText(FilledButton, 'Entregar los 2 carros'));

      final enviada = arnes.gateway.entregasEnviadas.single;
      expect((enviada.viajeId, enviada.lugarId, enviada.motivoCodigo), ('v1', 'l-Puerto', null));
      expect(enviada.firma, isNotEmpty);
      expect((enviada.momento.lat, enviada.momento.lng, enviada.momento.precision), (10.39, -75.51, 6.0));
      expect(enviada.momento.fecha, matches(RegExp(r'^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d[+-]\d\d:\d\d$')));
      expect(arnes.gateway.externosEnviados.single, (null, null, null));
      expect(find.text('Entrega registrada'), findsOneWidget);
      expect(find.textContaining('sin ubicación'), findsNothing);
    });

    testWidgets('sin GPS se firma igual y dice que se registró sin ubicación', (tester) async {
      final arnes = Arnes(sesion: sesionConductor);
      arnes.ubicacion.error = const UbicacionNoDisponible('sin señal');
      await abrir(tester, EntregasRepositorioFalso()..puntos = [puntoDe('Puerto')], arnes: arnes, pantalla: parada('Puerto'));

      await firmar(tester);
      await tocar(tester, find.widgetWithText(FilledButton, 'Entregar los 2 carros'));

      final enviada = arnes.gateway.entregasEnviadas.single;
      expect((enviada.momento.lat, enviada.momento.lng), (null, null));
      expect(enviada.momento.fecha, isNotNull, reason: 'la hora sí va');
      expect(find.textContaining('Se registró sin ubicación'), findsOneWidget);
    });

    testWidgets('sin señal queda en la cola con la hora del momento, a nombre de quien firmó', (tester) async {
      final arnes = Arnes(sesion: sesionConductor)..red.estable = false;
      await abrir(tester, EntregasRepositorioFalso()..puntos = [puntoDe('Puerto')], arnes: arnes, pantalla: parada('Puerto'));

      await firmar(tester);
      await tocar(tester, find.widgetWithText(FilledButton, 'Entregar los 2 carros'));

      expect(find.text('Entrega guardada'), findsOneWidget);
      final job = arnes.cola.jobs.single as EntregaPuntoJob;
      expect((job.viajeId, job.lugarId, job.usuarioId, job.fallido, job.sinUbicacion), ('v1', 'l-Puerto', 'u9', false, false));
      expect(job.fechaMomento, isNotNull);
      expect(arnes.gateway.entregasEnviadas, isEmpty);
    });

    // ESC-02: nadie tomó la entrega en una parada con inspectores. No se firma, y se dice qué hacer.
    testWidgets('si nadie tomó la entrega, no deja firmar y dice qué hacer', (tester) async {
      final arnes = Arnes(sesion: sesionConductor);
      await abrir(
        tester,
        EntregasRepositorioFalso()..puntos = [puntoDe('Puerto', receptor: ReceptorParada.sinReceptor)],
        arnes: arnes,
        pantalla: parada('Puerto'),
      );
      await firmar(tester);

      expect(boton(tester, 'Entregar los 2 carros').onPressed, isNull);
      expect(
        find.text('Este punto tiene inspector: pídele a un inspector del punto que tome la entrega en su app'),
        findsWidgets,
      );
      expect(arnes.cola.jobs, isEmpty);
    });

    // CA-3: el rechazo de la parada se explica por su motivo, no por el texto, y lo escrito se queda.
    testWidgets('si el servidor dice sin_receptor, lo explica, descarta el envío y no se pierde lo escrito', (tester) async {
      final arnes = Arnes(sesion: sesionConductor);
      arnes.gateway.errores['firmarEntregaPunto'] = [rechazoDeParada(409, MotivoParada.sinReceptor, 'cualquier texto')];
      await abrir(tester, EntregasRepositorioFalso()..puntos = [puntoDe('Puerto')], arnes: arnes, pantalla: parada('Puerto'));

      await firmar(tester);
      await tocar(tester, find.widgetWithText(FilledButton, 'Entregar los 2 carros'));

      expect(find.text('Nadie ha tomado la entrega de esta parada'), findsOneWidget);
      expect(find.textContaining('pídele a un inspector del punto que tome la entrega'), findsWidgets);
      expect(arnes.cola.jobs, isEmpty, reason: 'reintentarlo mandaría exactamente lo mismo');
      await tocar(tester, find.widgetWithText(TextButton, 'Entendido'));
      expect(boton(tester, 'Entregar los 2 carros').onPressed, isNotNull, reason: 'la firma sigue dibujada');
    });

    testWidgets('un carro sin la revisión de llegada cerrada deja la firma gris y dice cuál', (tester) async {
      final entregas = EntregasRepositorioFalso()
        ..puntos = [puntoDe('Puerto', estados: {_vin2: EstadoEntrega.sinInspeccion})];
      await abrir(tester, entregas, pantalla: parada('Puerto'));
      await firmar(tester);

      expect(find.text('Falta la revisión de llegada'), findsOneWidget);
      expect(find.textContaining('Falta la revisión de llegada del VIN $_vin2'), findsOneWidget);
      expect(boton(tester, 'Entregar los 2 carros').onPressed, isNull);
    });

    // La revisión existe pero sigue abierta: el servidor respondería `llegada_abierta`.
    testWidgets('una revisión de llegada abierta también deja la firma gris', (tester) async {
      final entregas = EntregasRepositorioFalso()
        ..puntos = [puntoDe('Puerto')]
        ..revisiones = {'ins-$_vin1': const RevisionDeLlegada(cerrada: false)};
      await abrir(tester, entregas, pantalla: parada('Puerto'));
      await firmar(tester);

      expect(find.textContaining('Falta la revisión de llegada del VIN $_vin1'), findsOneWidget);
      expect(boton(tester, 'Entregar los 2 carros').onPressed, isNull);
    });

    // CA-5: quien entrega lee las reservas de quien recibe antes de firmar.
    testWidgets('muestra las reservas que dejó quien recibe, carro por carro', (tester) async {
      await abrir(
        tester,
        EntregasRepositorioFalso()..puntos = [puntoDe('Puerto', reservas: {_vin2: 'Llegó sucio y sin manual'})],
        pantalla: parada('Puerto'),
      );

      expect(find.text('Con reservas: Llegó sucio y sin manual'), findsOneWidget);
    });

    // CA-5: las reservas que llegaron después de su firma le retiran la firma en esos carros. Lo que manda el servidor
    // entonces: el carro sin reservas ya cerró con las dos firmas y el de las reservas tiene la de quien recibe y ya no
    // la del conductor.
    testWidgets('si las reservas llegaron después de su firma, le pide firmar otra vez esos carros', (tester) async {
      await abrir(
        tester,
        EntregasRepositorioFalso()
          ..puntos = [
            puntoDe(
              'Puerto',
              estados: {_vin1: EstadoEntrega.cerrada},
              reservas: {_vin2: 'Llegó sucio y sin manual'},
              entregaFirmada: {_vin1},
              recibeFirmada: {_vin1, _vin2},
            ),
          ],
        pantalla: parada('Puerto'),
      );

      expect(find.textContaining('dejó reservas después de tu firma: firma otra vez este carro: $_vin2'), findsOneWidget);
      expect(find.byType(LienzoFirma), findsOneWidget);
    });

    // Si el conductor todavía no había firmado, las reservas son las de siempre: no hay nada que firmar «otra vez».
    testWidgets('si firma por primera vez después de las reservas, no le dice que firme otra vez', (tester) async {
      await abrir(
        tester,
        EntregasRepositorioFalso()
          ..puntos = [
            puntoDe('Puerto', reservas: {_vin2: 'Llegó sucio y sin manual'}, recibeFirmada: {_vin1, _vin2}),
          ],
        pantalla: parada('Puerto'),
      );

      expect(find.text('Con reservas: Llegó sucio y sin manual'), findsOneWidget);
      expect(find.textContaining('firma otra vez'), findsNothing);
    });

    // 4: con la firma de quien recibe ya puesta, el servidor no deja cerrar con una sola firma.
    testWidgets('si quien recibe ya firmó, no ofrece cerrar con una sola firma y dice por qué', (tester) async {
      await abrir(
        tester,
        EntregasRepositorioFalso()..puntos = [puntoDe('Puerto', receptorNombre: 'Pedro', recibeFirmada: {_vin1, _vin2})],
        pantalla: parada('Puerto'),
      );

      final cerrar = find.widgetWithText(TextButton, 'La otra parte no está: cerrar con una sola firma');
      expect(tester.widget<TextButton>(cerrar).onPressed, isNull);
      expect(motivoDelBoton('Pedro ya firmó: la entrega no puede cerrarse con una sola firma. Firma la parada.'), findsOneWidget);
    });

    testWidgets('ya firmada por él, dice que falta quien recibe', (tester) async {
      await abrir(
        tester,
        EntregasRepositorioFalso()..puntos = [puntoDe('Puerto', receptorNombre: 'Pedro', entregaFirmada: {_vin1, _vin2})],
        pantalla: parada('Puerto'),
      );

      expect(find.text('Ya firmaste la entrega de esta parada. Falta la firma de Pedro.'), findsOneWidget);
      expect(find.byType(LienzoFirma), findsNothing);
    });
  });

  /// CA-3: en una parada sin inspectores recibe la persona del lugar: firma ahí mismo, con su nombre y su cédula.
  group('el conductor en una parada sin inspectores', () {
    Future<Arnes> abrirFirma(WidgetTester tester, {Arnes? arnes, EntregasRepositorioFalso? entregas}) => abrir(
          tester,
          entregas ?? (EntregasRepositorioFalso()..puntos = [puntoDe('Norte', tieneInspector: false)]),
          arnes: arnes,
          pantalla: parada('Norte'),
        );

    Future<void> escribirDatos(WidgetTester tester) async {
      await tester.enterText(find.widgetWithText(TextField, 'Nombre de quien recibe'), 'Rosa Ríos');
      await tester.pump();
      await tester.enterText(find.widgetWithText(TextField, 'Cédula de quien recibe'), '1 020 304 050');
      await tester.pump();
    }

    testWidgets('pide el nombre, la cédula y la firma de quien recibe, sin interruptor', (tester) async {
      await abrirFirma(tester);

      expect(find.text('Aquí recibe la persona del lugar'), findsOneWidget);
      expect(find.byType(SwitchListTile), findsNothing);
      expect(find.byType(LienzoFirma), findsNWidgets(2), reason: 'la suya y la de quien recibe');
      await firmarEn(tester, 0);
      expect(find.text('Escribe el nombre de quien recibe.'), findsOneWidget);
      expect(boton(tester, 'Entregar los 2 carros').onPressed, isNull);
    });

    testWidgets('un nombre de dos letras o una cédula sin números no pasan de aquí', (tester) async {
      final arnes = Arnes(sesion: sesionConductor);
      await abrirFirma(tester, arnes: arnes);
      await firmarEn(tester, 0);
      await firmarEn(tester, 1);

      await tester.enterText(find.widgetWithText(TextField, 'Nombre de quien recibe'), 'Ro');
      await tester.pump();
      expect(find.textContaining('muy corto'), findsOneWidget);

      await tester.enterText(find.widgetWithText(TextField, 'Nombre de quien recibe'), 'Rosa Ríos');
      await tester.enterText(find.widgetWithText(TextField, 'Cédula de quien recibe'), 'sin numeros');
      await tester.pump();
      expect(find.textContaining('tiene que llevar números'), findsOneWidget);
      expect(arnes.cola.jobs, isEmpty);
    });

    testWidgets('con los tres la entrega sale cerrada y dice quién recibió', (tester) async {
      final arnes = Arnes(sesion: sesionConductor);
      await abrirFirma(tester, arnes: arnes);

      await firmarEn(tester, 0);
      await escribirDatos(tester);
      await firmarEn(tester, 1);
      await tocar(tester, find.widgetWithText(FilledButton, 'Entregar los 2 carros'));

      expect(arnes.gateway.externosEnviados.single.$1, 'Rosa Ríos');
      expect(arnes.gateway.externosEnviados.single.$2, '1 020 304 050');
      expect(arnes.gateway.externosEnviados.single.$3, isNotEmpty);
      expect(find.text('Entrega cerrada'), findsOneWidget);
      expect(find.textContaining('Recibió Rosa Ríos'), findsOneWidget);
    });

    // ESC-14: donde no hay inspectores, el conductor marca el rechazo a pedido de la persona del lugar.
    testWidgets('el conductor rechaza por novedad a pedido de quien recibe', (tester) async {
      final arnes = Arnes(sesion: sesionConductor);
      await abrirFirma(
        tester,
        arnes: arnes,
        entregas: EntregasRepositorioFalso()
          ..puntos = [puntoDe('Norte', tieneInspector: false)]
          ..revisiones = {'ins-$_vin2': const RevisionDeLlegada(cerrada: true, danosConFoto: [_golpe])},
      );

      await tocar(tester, find.widgetWithText(TextButton, 'Rechazar por novedad').last);
      await tocar(tester, find.text('Izquierdo 7 · Puerta delantera'));
      await firmarEn(tester, 0);
      await escribirDatos(tester);
      await firmarEn(tester, 1);
      await tocar(tester, find.widgetWithText(FilledButton, 'Entregar 1 carro · 1 rechazado'));

      expect(arnes.gateway.entregasEnviadas.single.rechazos, const [RechazoCarro(vin: _vin2, hallazgoId: 'h-golpe')]);
    });
  });

  group('el inspector que tomó la parada', () {
    // CA-2: «Firmar» gris hasta que el conductor marque «Llegué»; al actualizar después, se activa.
    testWidgets('firma solo después del «Llegué» del conductor, y se activa al actualizar', (tester) async {
      // Sin el «Llegué» el servidor no deja crear la revisión de llegada: los carros llegan sin ella.
      final entregas = EntregasRepositorioFalso()
        ..puntos = [puntoDe('Puerto', conductorLlego: false, estado: EstadoEntrega.sinInspeccion)];
      await abrir(tester, entregas, arnes: Arnes(), pantalla: parada('Puerto'));
      await firmar(tester);

      expect(find.text('Recibes tú'), findsOneWidget);
      expect(boton(tester, 'Entregar los 2 carros').onPressed, isNull);
      expect(motivoDelBoton('El conductor aún no marca «Llegué» en esta parada'), findsOneWidget, reason: 'lo primero que falta');

      // El conductor llegó, pero las revisiones todavía no están: ahora eso es lo que falta.
      entregas.puntos = [puntoDe('Puerto', estado: EstadoEntrega.sinInspeccion)];
      await tocar(tester, find.byTooltip('Actualizar la parada'));
      expect(motivoDelBoton('Falta la revisión de llegada de 2 carros: termínalas antes de firmar.'), findsOneWidget);

      entregas.puntos = [puntoDe('Puerto')];
      await tocar(tester, find.byTooltip('Actualizar la parada'));

      expect(boton(tester, 'Entregar los 2 carros').onPressed, isNotNull, reason: 'la firma dibujada se conserva');
    });

    // 7: «Actualizar» también vuelve a pedir las revisiones de llegada: una que se cerró deja de bloquear.
    testWidgets('al actualizar se vuelve a mirar la revisión de llegada', (tester) async {
      final entregas = EntregasRepositorioFalso()
        ..puntos = [puntoDe('Puerto')]
        ..revisiones = {'ins-$_vin1': const RevisionDeLlegada(cerrada: false)};
      await abrir(tester, entregas, arnes: Arnes(), pantalla: parada('Puerto'));
      await firmar(tester);
      expect(boton(tester, 'Entregar los 2 carros').onPressed, isNull);

      entregas.revisiones = {};
      await tocar(tester, find.byTooltip('Actualizar la parada'));

      expect(boton(tester, 'Entregar los 2 carros').onPressed, isNotNull);
    });

    // 8: el rol no basta: si el servidor dice que la parada la tomó otro inspector, no firma.
    testWidgets('si la parada la tomó otro inspector, no ofrece firmar y dice quién la tiene', (tester) async {
      await abrir(
        tester,
        EntregasRepositorioFalso()..puntos = [puntoDe('Puerto', receptorNombre: 'Pedro Ruiz')],
        arnes: Arnes(),
        pantalla: parada('Puerto'),
      );

      expect(find.text('Recibes tú'), findsNothing);
      expect(find.byType(LienzoFirma), findsNothing);
      expect(find.text('La entrega de esta parada la tomó Pedro Ruiz: firma él.'), findsOneWidget);
    });

    testWidgets('el inspector que la tomó la firma aunque el servidor diga su nombre', (tester) async {
      await abrir(
        tester,
        EntregasRepositorioFalso()..puntos = [puntoDe('Puerto', receptorNombre: sesionPrueba.usuario.nombre)],
        arnes: Arnes(),
        pantalla: parada('Puerto'),
      );

      expect(find.text('Recibes tú'), findsOneWidget);
      expect(find.byType(LienzoFirma), findsOneWidget);
    });

    testWidgets('si nadie ha tomado la parada, le dice que la tome antes de firmar', (tester) async {
      await abrir(
        tester,
        EntregasRepositorioFalso()..puntos = [puntoDe('Puerto', receptor: ReceptorParada.sinReceptor)],
        arnes: Arnes(),
        pantalla: parada('Puerto'),
      );

      expect(find.byType(LienzoFirma), findsNothing);
      expect(find.text('Toma la entrega de esta parada en «Pendientes» antes de firmar.'), findsOneWidget);
    });

    testWidgets('si el servidor dice conductor_sin_llegada, lo explica por el motivo', (tester) async {
      final arnes = Arnes();
      arnes.gateway.errores['firmarEntregaPunto'] = [rechazoDeParada(409, MotivoParada.conductorSinLlegada, 'x')];
      await abrir(tester, EntregasRepositorioFalso()..puntos = [puntoDe('Puerto')], arnes: arnes, pantalla: parada('Puerto'));

      await firmar(tester);
      await tocar(tester, find.widgetWithText(FilledButton, 'Entregar los 2 carros'));

      expect(find.text('El conductor aún no marca «Llegué»'), findsOneWidget);
      expect(arnes.cola.jobs, isEmpty);
    });

    // CA-4, ESC-06: rechazar exige elegir un daño con foto de su revisión de llegada; el botón cuenta.
    testWidgets('rechaza un carro por un daño con foto y firma los demás', (tester) async {
      final arnes = Arnes();
      final entregas = EntregasRepositorioFalso()
        ..puntos = [puntoDe('Puerto')]
        ..revisiones = {'ins-$_vin2': const RevisionDeLlegada(cerrada: true, danosConFoto: [_golpe])};
      await abrir(tester, entregas, arnes: arnes, pantalla: parada('Puerto'));

      await tocar(tester, find.widgetWithText(TextButton, 'Rechazar por novedad').last);
      expect(find.text('Rechazar por novedad'), findsWidgets);
      await tocar(tester, find.text('Izquierdo 7 · Puerta delantera'));

      expect(find.text('Rechazado por novedad: Izquierdo 7 · Puerta delantera'), findsOneWidget);
      await firmar(tester);
      await tocar(tester, find.widgetWithText(FilledButton, 'Entregar 1 carro · 1 rechazado'));

      final enviada = arnes.gateway.entregasEnviadas.single;
      expect(enviada.rechazos, const [RechazoCarro(vin: _vin2, hallazgoId: 'h-golpe')]);
      expect(find.textContaining('1 carro entregado y 1 carro rechazado'), findsOneWidget);
    });

    // ESC-15, C24: si se rechazan todos, la parada igual se firma, como constancia.
    testWidgets('con todos rechazados la parada igual se firma', (tester) async {
      final arnes = Arnes();
      await abrir(
        tester,
        EntregasRepositorioFalso()
          ..puntos = [puntoDe('Puerto')]
          ..revisiones = {
            'ins-$_vin1': const RevisionDeLlegada(cerrada: true, danosConFoto: [_golpe]),
            'ins-$_vin2': const RevisionDeLlegada(cerrada: true, danosConFoto: [DanoDeLlegada(hallazgoId: 'h-2', zona: 'Techo')]),
          },
        arnes: arnes,
        pantalla: parada('Puerto'),
      );

      await tocar(tester, find.widgetWithText(TextButton, 'Rechazar por novedad').first);
      await tocar(tester, find.text('Izquierdo 7 · Puerta delantera'));
      await tocar(tester, find.widgetWithText(TextButton, 'Rechazar por novedad'));
      await tocar(tester, find.text('Techo'));
      await firmar(tester);
      await tocar(tester, find.widgetWithText(FilledButton, 'Firmar: 2 carros rechazados, ninguno entregado'));

      expect(arnes.gateway.entregasEnviadas.single.rechazos.map((r) => r.vin), [_vin1, _vin2]);
    });

    testWidgets('un rechazo se puede quitar', (tester) async {
      await abrir(
        tester,
        EntregasRepositorioFalso()
          ..puntos = [puntoDe('Puerto')]
          ..revisiones = {'ins-$_vin1': const RevisionDeLlegada(cerrada: true, danosConFoto: [_golpe])},
        arnes: Arnes(),
        pantalla: parada('Puerto'),
      );

      await tocar(tester, find.widgetWithText(TextButton, 'Rechazar por novedad').first);
      await tocar(tester, find.text('Izquierdo 7 · Puerta delantera'));
      await tocar(tester, find.widgetWithText(TextButton, 'Quitar el rechazo'));

      expect(find.widgetWithText(FilledButton, 'Entregar los 2 carros'), findsOneWidget);
    });

    testWidgets('sin un daño con foto, «Rechazar por novedad» sale gris y dice qué hace falta', (tester) async {
      await abrir(tester, EntregasRepositorioFalso()..puntos = [puntoDe('Puerto', vines: const [_vin1])], arnes: Arnes(), pantalla: parada('Puerto'));

      expect(tester.widget<TextButton>(find.widgetWithText(TextButton, 'Rechazar por novedad')).onPressed, isNull);
      expect(find.text('Para rechazarlo, registra el daño con su foto en la revisión de llegada'), findsOneWidget);
    });

    // CA-4: el 409 `sin_novedad_para_rechazar` se muestra con el mensaje del servidor.
    testWidgets('si el servidor no acepta el rechazo, muestra su mensaje', (tester) async {
      final arnes = Arnes();
      const mensaje = 'El VIN $_vin2 no tiene un daño que justifique el rechazo: entrégalo en esta parada';
      arnes.gateway.errores['firmarEntregaPunto'] = [rechazoDeParada(409, MotivoParada.sinNovedadParaRechazar, mensaje)];
      await abrir(
        tester,
        EntregasRepositorioFalso()
          ..puntos = [puntoDe('Puerto')]
          ..revisiones = {'ins-$_vin2': const RevisionDeLlegada(cerrada: true, danosConFoto: [_golpe])},
        arnes: arnes,
        pantalla: parada('Puerto'),
      );

      await tocar(tester, find.widgetWithText(TextButton, 'Rechazar por novedad').last);
      await tocar(tester, find.text('Izquierdo 7 · Puerta delantera'));
      await firmar(tester);
      await tocar(tester, find.widgetWithText(FilledButton, 'Entregar 1 carro · 1 rechazado'));

      expect(find.text('Este carro no se puede rechazar'), findsOneWidget);
      expect(find.textContaining(mensaje), findsOneWidget);
      expect(arnes.cola.jobs, isEmpty);
    });

    testWidgets('si el servidor no acepta el daño elegido (400), lo explica y no se pierde lo escrito', (tester) async {
      final arnes = Arnes();
      arnes.gateway.errores['firmarEntregaPunto'] = [
        const ErrorApi(
          message: 'Datos inválidos',
          code: 'VALIDATION_ERROR',
          status: 400,
          details: {
            'fieldErrors': {
              'rechazos': ['Para no entregar un carro elige el daño de su revisión de llegada que lo motiva (VIN $_vin2)'],
            },
          },
        ),
      ];
      await abrir(
        tester,
        EntregasRepositorioFalso()
          ..puntos = [puntoDe('Puerto')]
          ..revisiones = {'ins-$_vin2': const RevisionDeLlegada(cerrada: true, danosConFoto: [_golpe])},
        arnes: arnes,
        pantalla: parada('Puerto'),
      );

      await tocar(tester, find.widgetWithText(TextButton, 'Rechazar por novedad').last);
      await tocar(tester, find.text('Izquierdo 7 · Puerta delantera'));
      await firmar(tester);
      await tocar(tester, find.widgetWithText(FilledButton, 'Entregar 1 carro · 1 rechazado'));

      expect(find.text('El servidor no aceptó la entrega'), findsOneWidget);
      expect(find.textContaining('elige el daño de su revisión de llegada'), findsOneWidget);
      expect(arnes.cola.jobs, isEmpty, reason: 'reintentarlo mandaría lo mismo');
      await tocar(tester, find.widgetWithText(TextButton, 'Entendido'));
      expect(find.text('Rechazado por novedad: Izquierdo 7 · Puerta delantera'), findsOneWidget, reason: 'sigue lo escrito');
      expect(boton(tester, 'Entregar 1 carro · 1 rechazado').onPressed, isNotNull);
    });

    // CA-5, ESC-08: quien recibe deja reservas en un carro y viajan con su firma.
    testWidgets('recibe un carro con reservas y viajan con su firma', (tester) async {
      final arnes = Arnes();
      await abrir(tester, EntregasRepositorioFalso()..puntos = [puntoDe('Puerto')], arnes: arnes, pantalla: parada('Puerto'));

      await tocar(tester, find.widgetWithText(TextButton, 'Recibir con reservas').last);
      await tester.enterText(find.widgetWithText(TextField, 'Observación'), 'Llegó sucio y sin manual');
      await tester.pump();
      await tocar(tester, find.widgetWithText(TextButton, 'Guardar'));
      expect(find.text('Con reservas: Llegó sucio y sin manual'), findsOneWidget);

      await firmar(tester);
      await tocar(tester, find.widgetWithText(FilledButton, 'Entregar los 2 carros'));

      final reservas = arnes.gateway.entregasEnviadas.single.reservas;
      expect(reservas, const [ReservaCarro(vin: _vin2, observaciones: 'Llegó sucio y sin manual')]);
    });

    testWidgets('si la entrega la tomó otro inspector, lo explica', (tester) async {
      final arnes = Arnes();
      arnes.gateway.errores['firmarEntregaPunto'] = [rechazoDeParada(403, MotivoParada.entregaTomada, 'x')];
      await abrir(tester, EntregasRepositorioFalso()..puntos = [puntoDe('Puerto')], arnes: arnes, pantalla: parada('Puerto'));

      await firmar(tester);
      await tocar(tester, find.widgetWithText(FilledButton, 'Entregar los 2 carros'));

      expect(find.text('Esta entrega la tomó otro inspector'), findsOneWidget);
      expect(arnes.cola.jobs, isEmpty);
    });
  });

  group('cerrar con una sola firma', () {
    Future<Arnes> abrirCierre(WidgetTester tester, {bool tieneInspector = false, Arnes? arnes}) async {
      final usado = await abrir(
        tester,
        EntregasRepositorioFalso()..puntos = [puntoDe('Norte', tieneInspector: tieneInspector)],
        arnes: arnes,
        pantalla: parada('Norte'),
      );
      await tocar(tester, find.text('La otra parte no está: cerrar con una sola firma'));
      return usado;
    }

    // C28: con una sola firma no hay rechazos, y lo dice.
    testWidgets('dice que no rechaza carros y no ofrece «Rechazar por novedad»', (tester) async {
      await abrirCierre(tester);

      expect(
        find.text('Con una sola firma se cierran todos los carros de la parada: para rechazar uno, firma la parada con quien recibe'),
        findsOneWidget,
      );
      expect(find.widgetWithText(TextButton, 'Rechazar por novedad'), findsNothing);
      expect(boton(tester, 'Cerrar con una sola firma').onPressed, isNull);
      expect(find.text('Elige el motivo.'), findsOneWidget);
    });

    // ESC-09: «No había nadie» no pide fotos aparte: valen las de la revisión de llegada de cada carro.
    testWidgets('«No había nadie para recibir» cierra sin fotos aparte, con testigo', (tester) async {
      final arnes = Arnes(sesion: sesionConductor);
      await abrirCierre(tester, arnes: arnes);

      await tocar(tester, find.text('No había nadie para recibir'));
      await tester.enterText(find.widgetWithText(TextField, 'Nombre del testigo'), 'Andrés Pardo');
      await tester.pump();
      await firmar(tester);
      expect(find.text('Escribe la cédula del testigo, o borra su nombre.'), findsOneWidget, reason: 'van juntos');
      expect(boton(tester, 'Cerrar con una sola firma').onPressed, isNull);

      await tester.enterText(find.widgetWithText(TextField, 'Cédula del testigo'), '79 123 456');
      await tester.pump();
      await tocar(tester, find.widgetWithText(FilledButton, 'Cerrar con una sola firma'));

      final enviada = arnes.gateway.entregasEnviadas.single;
      expect((enviada.motivoCodigo, enviada.testigo), (MotivoUnaFirma.noHabiaNadie, ('Andrés Pardo', '79 123 456')));
      expect(enviada.fotos, isEmpty);
      expect(enviada.rechazos, isEmpty);
      expect(enviada.momento.fecha, isNotNull);
      expect(find.text('Parada cerrada con una sola firma'), findsOneWidget);
    });

    // CA-6: «se negó a firmar» exige al menos una foto, que va a cada carro de la parada.
    testWidgets('«Quien recibe se negó a firmar» pide la foto, y la foto va a cada carro', (tester) async {
      final arnes = Arnes(sesion: sesionConductor);
      arnes.archivos.existentes.add(_fotoDelCierre.ruta);
      await abrirCierre(tester, arnes: arnes);

      await tocar(tester, find.text('Quien recibe se negó a firmar'));
      await firmar(tester);
      expect(boton(tester, 'Cerrar con una sola firma').onPressed, isNull);
      expect(find.text('Con este motivo la foto es obligatoria'), findsOneWidget);

      await tocar(tester, find.text('Tomar foto'));
      await tester.enterText(find.widgetWithText(TextField, 'Algo más (opcional)'), 'Se fue sin firmar');
      await tester.pump();
      await tocar(tester, find.widgetWithText(FilledButton, 'Cerrar con una sola firma'));

      final enviada = arnes.gateway.entregasEnviadas.single;
      expect((enviada.motivoCodigo, enviada.motivoTexto), (MotivoUnaFirma.seNegoAFirmar, 'Se fue sin firmar'));
      expect(enviada.fotos.map((f) => f.vin), [_vin1, _vin2]);
    });

    testWidgets('si el servidor no deja cerrarla con una sola firma, lo explica y conserva la foto', (tester) async {
      final arnes = Arnes(sesion: sesionConductor);
      arnes.archivos.existentes.add(_fotoDelCierre.ruta);
      arnes.gateway.errores['cerrarEntregaConUnaFirma'] = [
        errorApi(409, 'Quien recibe ya firmó: la entrega no puede cerrarse con una sola firma'),
      ];
      await abrirCierre(tester, arnes: arnes);

      await tocar(tester, find.text('Quien recibe se negó a firmar'));
      await firmar(tester);
      await tocar(tester, find.text('Tomar foto'));
      await tocar(tester, find.widgetWithText(FilledButton, 'Cerrar con una sola firma'));

      expect(find.textContaining('Quien recibe ya firmó'), findsOneWidget);
      expect(arnes.cola.jobs, isEmpty, reason: 'ni la firma ni la foto se quedan atrapadas en la cola');
      await tocar(tester, find.widgetWithText(TextButton, 'Entendido'));
      expect(find.text('Otra foto'), findsOneWidget, reason: 'la foto sigue aquí');
      expect(boton(tester, 'Cerrar con una sola firma').onPressed, isNotNull);
    });

    // Donde la parada tiene inspectores, «No había nadie» no aplica: el servidor responde `sin_receptor`.
    testWidgets('en una parada con inspector no ofrece «No había nadie para recibir»', (tester) async {
      await abrirCierre(tester, tieneInspector: true);

      expect(find.text('Quien recibe se negó a firmar'), findsOneWidget);
      expect(find.text('No había nadie para recibir'), findsNothing);
    });

    testWidgets('se puede volver a la firma de las dos partes', (tester) async {
      await abrirCierre(tester);

      await tocar(tester, find.text('Volver: firman las dos partes'));

      expect(find.widgetWithText(FilledButton, 'Entregar los 2 carros'), findsOneWidget);
      expect(find.text('Elige el motivo.'), findsNothing);
    });
  });

  // CA-1: el coordinador, el admin y el super admin no firman ninguna parada.
  testWidgets('quien no es conductor ni inspector no tiene con qué firmar', (tester) async {
    await abrir(tester, EntregasRepositorioFalso()..puntos = [puntoDe('Puerto')], arnes: Arnes(sesion: _coordinador), pantalla: parada('Puerto'));

    expect(find.byType(LienzoFirma), findsNothing);
    expect(find.widgetWithText(FilledButton, 'Entregar los 2 carros'), findsNothing);
    expect(find.text('La parada la firman el conductor del viaje y quien recibe en ella.'), findsOneWidget);
  });

  // Lo que una prueba puede ver es el ancho, que es donde se corta: a lo alto la pantalla se desplaza.
  testWidgets('a lo ancho de un teléfono pequeño no se corta nada', (tester) async {
    await abrir(
      tester,
      EntregasRepositorioFalso()
        ..puntos = [
          puntoDe(
            'Concesionario Autonorte de la Avenida',
            tieneInspector: false,
            estados: {_vin1: EstadoEntrega.sinInspeccion},
            reservas: {_vin2: 'Llegó sucio y sin manual'},
          ),
        ],
      pantalla: parada('Concesionario Autonorte de la Avenida'),
      tamano: const Size(320, 6000),
    );

    expect(tester.takeException(), isNull);
    expect(find.widgetWithText(TextField, 'Nombre de quien recibe'), findsOneWidget);
    await tocar(tester, find.text('La otra parte no está: cerrar con una sola firma'));
    expect(tester.takeException(), isNull);
  });
}
