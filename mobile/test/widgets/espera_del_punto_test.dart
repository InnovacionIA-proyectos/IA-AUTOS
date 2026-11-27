import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intl/date_symbol_data_local.dart';
import 'package:traza_autos/core/dispositivo/servicios.dart';
import 'package:traza_autos/core/errores/error_api.dart';
import 'package:traza_autos/core/tema/tema.dart';
import 'package:traza_autos/features/auth/domain/sesion.dart';
import 'package:traza_autos/features/auth/presentation/auth_controller.dart';
import 'package:traza_autos/features/catalogos/providers.dart';
import 'package:traza_autos/features/custodia/presentation/lienzo_firma.dart';
import 'package:traza_autos/features/inspecciones/domain/foto_local.dart';
import 'package:traza_autos/features/inspecciones/domain/novedad_pendiente.dart';
import 'package:traza_autos/features/sync/domain/job.dart';
import 'package:traza_autos/features/sync/presentation/cola_page.dart';
import 'package:traza_autos/features/sync/presentation/sync_controller.dart';
import 'package:traza_autos/features/viajes/domain/entrega_punto.dart';
import 'package:traza_autos/features/viajes/domain/espera_del_punto.dart';
import 'package:traza_autos/features/viajes/presentation/entrega_punto_page.dart';
import 'package:traza_autos/features/viajes/providers.dart';

import '../support/arnes.dart';
import '../support/dobles_catalogos.dart';
import '../support/dobles_viajes.dart';

/// S8-HU-05.2: «No hay inspector» en la parada, el reloj que sigue sin la pantalla, el plan B y el rechazo tardío
/// explicado. La hora del teléfono la fija cada prueba.

const _vin1 = 'VINAAAAAAAAAAAAA1';
const _vin2 = 'VINBBBBBBBBBBBBB2';

/// Las 08:00 de Colombia: la hora en que el conductor tocó «No hay inspector».
final _ocho = DateTime.utc(2026, 11, 26, 13);

DateTime _a(int minutos) => _ocho.add(Duration(minutes: minutos));

/// La hora del teléfono en cada prueba.
var _ahora = _ocho;

const _admin = Sesion(token: 'jwt', usuario: Usuario(id: 'adm', nombre: 'Marta Ruiz', email: '', rol: 'admin'));
const _superAdmin = Sesion(token: 'jwt', usuario: Usuario(id: 'sa', nombre: 'Root', email: '', rol: 'super_admin'));

/// El inspector del punto que tomó la entrega: es el `sesionPrueba` del arnés.
const _pedro = 'Ana Pérez';

/// La espera que tiene el servidor, activada a las 08:00 con X = 15 e Y = 30.
EsperaDelPunto _espera({EstadoEspera estado = EstadoEspera.esperando, bool planB = false, DateTime? avisoAt}) => EsperaDelPunto(
      estado: estado,
      activadaAt: _ocho,
      avisoDesde: _a(15),
      avisoAt: avisoAt,
      planBDesde: _a(45),
      planBHabilitado: planB,
    );

/// «Puerto de Cartagena»: tiene inspectores y nadie ha tomado la entrega.
EntregaPunto _sinNadie({
  EsperaDelPunto? espera,
  bool conductorLlego = true,
  Map<String, EstadoEntrega> estados = const {},
}) =>
    puntoDe('Puerto', receptor: ReceptorParada.sinReceptor, espera: espera, conductorLlego: conductorLlego, estados: estados);

Future<void> _avanzar(WidgetTester tester) async {
  for (var i = 0; i < 12; i++) {
    await tester.pump(const Duration(milliseconds: 50));
  }
}

Future<void> _tocar(WidgetTester tester, Finder cual) async {
  await tester.ensureVisible(cual);
  await tester.tap(cual);
  await _avanzar(tester);
}

Future<void> _firmarEn(WidgetTester tester, int indice) async {
  final lienzo = find.byType(LienzoFirma).at(indice);
  await tester.ensureVisible(lienzo);
  await tester.dragFrom(tester.getBottomLeft(lienzo) + const Offset(60, -80), const Offset(40, 30));
  await tester.pump();
}

FilledButton _boton(WidgetTester tester, String texto) => tester.widget<FilledButton>(find.widgetWithText(FilledButton, texto));

Finder get _noHayInspector => find.widgetWithText(OutlinedButton, 'No hay inspector');

ErrorApi _rechazo(int status, String motivo, String mensaje) =>
    ErrorApi(message: mensaje, code: status == 403 ? 'FORBIDDEN' : 'CONFLICT', status: status, details: {'motivo': motivo});

class _Arranque extends ConsumerWidget {
  const _Arranque(this.pantalla);

  final Widget pantalla;

  @override
  Widget build(BuildContext context, WidgetRef ref) =>
      ref.watch(authControllerProvider.select((e) => e.fase)) == FaseAuth.autenticado ? pantalla : const SizedBox();
}

/// Abre la parada «Puerto» con la sesión de [arnes] (el conductor, si no se dice otra cosa).
Future<Arnes> _abrir(
  WidgetTester tester,
  EntregasRepositorioFalso entregas, {
  Arnes? arnes,
  Widget pantalla = const EntregaParadaPage(viajeId: 'v1', lugarId: 'l-Puerto'),
}) async {
  await tester.binding.setSurfaceSize(const Size(600, 5000));
  addTearDown(() => tester.binding.setSurfaceSize(null));
  final usado = arnes ?? Arnes(sesion: sesionConductor);
  await tester.pumpWidget(
    ProviderScope(
      retry: (_, _) => null,
      overrides: [
        ...usado.overrides,
        entregasRepositorioProvider.overrideWithValue(entregas),
        viajesRepositorioProvider.overrideWithValue(ViajesRepositorioFalso()),
        lugaresRepositorioProvider.overrideWithValue(LugaresRepositorioFalso()),
        catalogosRepositorioProvider.overrideWithValue(
          CatalogosRepositorioFalso()..guardado = catalogosCon('Golpe', motivos: motivosDePrueba),
        ),
        horaDelTelefonoProvider.overrideWithValue(() => _ahora),
      ],
      child: MaterialApp(theme: crearTema(), home: _Arranque(pantalla)),
    ),
  );
  await _avanzar(tester);
  return usado;
}

/// La activación que quedó en la cola de este teléfono, sin señal, a la hora [momento].
SinInspectorParadaJob _activacionGuardada(DateTime momento) => SinInspectorParadaJob(
      id: 'a1',
      etiqueta: 'No hay inspector · Puerto',
      creadoEn: 1,
      usuarioId: sesionConductor.usuario.id,
      viajeId: 'v1',
      lugarId: 'l-Puerto',
      motivo: 'inspector_no_llego',
      fechaMomento: isoConZona(momento),
      avisoMinutos: 15,
      planBMinutos: 30,
    );

void main() {
  setUpAll(() => initializeDateFormatting('es_CO'));
  setUp(() => _ahora = _ocho);

  // CA-1 y ESC-02: a quién y dónde se ofrece «No hay inspector».
  group('«No hay inspector», por rol y por parada', () {
    testWidgets('el conductor lo tiene en la parada con inspectores donde nadie tomó la entrega', (tester) async {
      await _abrir(tester, EntregasRepositorioFalso()..puntos = [_sinNadie()]);

      expect(_noHayInspector, findsOneWidget);
      expect(find.text('Aquí firma el inspector del punto'), findsOneWidget);
      expect(find.text('La otra parte no está: cerrar con una sola firma'), findsNothing, reason: 'no hay cierre sin inspector');
    });

    testWidgets('no en una parada sin inspectores: ahí está el formulario de quien recibe', (tester) async {
      await _abrir(
        tester,
        EntregasRepositorioFalso()..puntos = [puntoDe('Puerto', tieneInspector: false, receptor: ReceptorParada.personaDelLugar)],
      );

      expect(_noHayInspector, findsNothing);
      expect(find.widgetWithText(TextField, 'Nombre de quien recibe'), findsOneWidget);
    });

    testWidgets('no con la entrega tomada: dice quién la tomó y que se firma con él', (tester) async {
      await _abrir(
        tester,
        EntregasRepositorioFalso()..puntos = [puntoDe('Puerto', receptor: ReceptorParada.inspectorDelPunto, receptorNombre: 'Pedro')],
      );

      expect(_noHayInspector, findsNothing);
      expect(find.text('Pedro tomó la entrega: firma con él'), findsOneWidget);
    });

    testWidgets('no antes de su «Llegué» en esa parada', (tester) async {
      await _abrir(tester, EntregasRepositorioFalso()..puntos = [_sinNadie(conductorLlego: false)]);

      expect(_noHayInspector, findsNothing);
    });

    testWidgets('nunca a inspector, coordinador, admin ni super admin', (tester) async {
      for (final sesion in [sesionPrueba, sesionCoordinador, _admin, _superAdmin]) {
        await _abrir(tester, EntregasRepositorioFalso()..puntos = [_sinNadie(espera: _espera())], arnes: Arnes(sesion: sesion));

        expect(_noHayInspector, findsNothing, reason: sesion.usuario.rol);
        expect(find.text('Esperando al inspector del punto'), findsNothing, reason: 'sin acción sobre la espera');
      }
    });
  });

  // CA-2 y CA-3: la activación va por la cola con la hora y el GPS del celular; el reloj ya cuenta sin señal.
  group('la activación y el reloj', () {
    testWidgets('sin señal se guarda con el motivo, la hora y el GPS, y el reloj cuenta sin la pantalla', (tester) async {
      final arnes = Arnes(sesion: sesionConductor)..red.estable = false;
      arnes.ubicacion.posicion = Posicion(10.39, -75.51, precision: 8, tomadaEn: DateTime.now());
      await _abrir(tester, EntregasRepositorioFalso()..puntos = [_sinNadie()], arnes: arnes);

      await _tocar(tester, _noHayInspector);
      expect(find.text('Si nadie toma la entrega, avisaremos al coordinador en 15 min.'), findsOneWidget);
      expect(_boton(tester, 'Empezar la espera').onPressed, isNull, reason: 'sin motivo no se sigue');
      await _tocar(tester, find.text('Otro'));
      await tester.enterText(find.byType(TextField).last, 'El inspector se enfermó');
      await tester.pump();
      await _tocar(tester, find.widgetWithText(FilledButton, 'Empezar la espera'));

      final job = arnes.cola.jobs.single as SinInspectorParadaJob;
      expect((job.viajeId, job.lugarId, job.motivo, job.motivoTexto), ('v1', 'l-Puerto', 'otro', 'El inspector se enfermó'));
      expect((job.lat, job.lng, job.precisionM, job.avisoMinutos, job.planBMinutos), (10.39, -75.51, 8.0, 15, 30));
      expect(DateTime.parse(job.fechaMomento).toUtc(), _ocho);
      expect(arnes.gateway.activacionesEnviadas, isEmpty);
      expect(find.text('Avisaremos al coordinador en 15 min'), findsOneWidget);
      expect(find.text('Podrás cerrar sin inspector del punto en 45 min'), findsWidgets);
      expect(_noHayInspector, findsNothing, reason: 'una sola espera a la vez');

      // El tiempo pasa con la pantalla quieta: el reloj se repinta con la hora guardada.
      _ahora = _a(27);
      await tester.pump(const Duration(seconds: 16));
      expect(find.text('El coordinador ya fue avisado'), findsOneWidget);
      expect(find.text('Podrás cerrar sin inspector del punto en 18 min'), findsWidgets);

      // Vuelve la señal: sale sola, con la hora del momento de tocarla.
      arnes.red.estable = true;
      await arnes.procesador.procesar(sesionConductor.usuario.id);
      expect(arnes.gateway.activacionesEnviadas.single.momento.fecha, job.fechaMomento);
    });

    testWidgets('con señal sale de una vez y la parada se actualiza', (tester) async {
      final entregas = EntregasRepositorioFalso()..puntos = [_sinNadie()];
      final arnes = await _abrir(tester, entregas);
      final consultas = entregas.consultas;

      await _tocar(tester, _noHayInspector);
      await _tocar(tester, find.text('El inspector no llegó'));
      await _tocar(tester, find.widgetWithText(FilledButton, 'Empezar la espera'));

      expect(arnes.gateway.activacionesEnviadas.single.motivo, 'inspector_no_llego');
      expect(arnes.cola.jobs, isEmpty);
      expect(entregas.consultas, greaterThan(consultas));
    });

    testWidgets('si el servidor la rechaza por la parada, lo explica y no queda en la cola', (tester) async {
      final arnes = Arnes(sesion: sesionConductor);
      arnes.gateway.errores['activarSinInspector'] = [_rechazo(409, 'parada_sin_inspectores', 'texto crudo')];
      await _abrir(tester, EntregasRepositorioFalso()..puntos = [_sinNadie()], arnes: arnes);

      await _tocar(tester, _noHayInspector);
      await _tocar(tester, find.text('El inspector no llegó'));
      await _tocar(tester, find.widgetWithText(FilledButton, 'Empezar la espera'));

      expect(find.text('Esta parada no tiene inspectores: aquí recibe la persona del lugar'), findsOneWidget);
      expect(find.text('texto crudo'), findsNothing);
      expect(arnes.cola.jobs, isEmpty);
    });

    // CA-3: tras reiniciar la app sin señal, la activación sigue en la cola y el reloj cuenta con su hora.
    testWidgets('tras reiniciar la app el reloj sigue con la hora de activación guardada', (tester) async {
      final arnes = Arnes(sesion: sesionConductor)..red.estable = false;
      await arnes.cola.agregar(_activacionGuardada(_ocho));
      _ahora = _a(50);

      await _abrir(tester, EntregasRepositorioFalso()..puntos = [_sinNadie()], arnes: arnes);

      expect(find.text('Esperando al inspector del punto'), findsOneWidget);
      expect(find.text('El coordinador ya fue avisado'), findsOneWidget);
      expect(find.text('Ya puedes cerrar sin inspector del punto'), findsOneWidget);
      expect(find.widgetWithText(FilledButton, 'Recibe una persona del lugar'), findsOneWidget);
    });

    testWidgets('con señal manda lo que dice el servidor', (tester) async {
      _ahora = _a(10);
      await _abrir(
        tester,
        EntregasRepositorioFalso()..puntos = [_sinNadie(espera: _espera(planB: true, avisoAt: _a(15)))],
      );

      expect(find.text('El coordinador ya fue avisado'), findsOneWidget);
      expect(find.text('Ya puedes cerrar sin inspector del punto'), findsOneWidget);
    });

    testWidgets('sin señal se ve la copia de la parada, y lo dice', (tester) async {
      _ahora = _a(20);
      final copia = _sinNadie(espera: _espera());
      await _abrir(
        tester,
        EntregasRepositorioFalso()
          ..puntos = [
            EntregaPunto(
              viajeId: copia.viajeId,
              lugarId: copia.lugarId,
              lugar: copia.lugar,
              unidades: copia.unidades,
              pendientes: copia.pendientes,
              entregada: false,
              tieneInspector: true,
              receptorDelServidor: ReceptorParada.sinReceptor,
              espera: copia.espera,
              copiaSinSenal: true,
            ),
          ],
      );

      expect(find.text('Sin señal: ves la última copia de la parada. La espera sigue contando.'), findsOneWidget);
      expect(find.text('Podrás cerrar sin inspector del punto en 25 min'), findsWidgets);
    });
  });

  // CA-4: el plan B antes y después del plazo.
  group('el plan B', () {
    testWidgets('antes del plazo no hay ningún cierre sin inspector: dice cuánto falta', (tester) async {
      _ahora = _a(40);
      await _abrir(tester, EntregasRepositorioFalso()..puntos = [_sinNadie(espera: _espera())]);
      await _firmarEn(tester, 0);

      expect(find.widgetWithText(FilledButton, 'Recibe una persona del lugar'), findsNothing);
      expect(find.widgetWithText(OutlinedButton, 'Cerrar con mi firma'), findsNothing);
      expect(find.text('La otra parte no está: cerrar con una sola firma'), findsNothing);
      expect(_boton(tester, 'Entregar los 2 carros').onPressed, isNull);
      expect(find.text('Podrás cerrar sin inspector del punto en 5 min'), findsWidgets);
    });

    testWidgets('con el plazo cumplido recibe la persona del lugar, con su nombre, su cédula y su firma', (tester) async {
      _ahora = _a(50);
      final arnes = Arnes(sesion: sesionConductor);
      arnes.ubicacion.posicion = Posicion(10.39, -75.51, precision: 6, tomadaEn: DateTime.now());
      await _abrir(tester, EntregasRepositorioFalso()..puntos = [_sinNadie(espera: _espera())], arnes: arnes);

      await _tocar(tester, find.widgetWithText(FilledButton, 'Recibe una persona del lugar'));
      await tester.enterText(find.widgetWithText(TextField, 'Nombre de quien recibe'), 'Andrés Pardo');
      await tester.enterText(find.widgetWithText(TextField, 'Cédula de quien recibe'), '79.123.456');
      await tester.pump();
      await _firmarEn(tester, 0);
      await _firmarEn(tester, 1);
      await _tocar(tester, find.widgetWithText(FilledButton, 'Entregar los 2 carros'));

      final enviada = arnes.gateway.entregasEnviadas.single;
      expect((enviada.motivoCodigo, enviada.momento.lat, enviada.momento.lng), (null, 10.39, -75.51));
      expect(enviada.momento.fecha, isNotNull);
      final (nombre, cedula, firma) = arnes.gateway.externosEnviados.single;
      expect((nombre, cedula, firma?.isNotEmpty), ('Andrés Pardo', '79.123.456', true));
      expect(find.text('Entrega cerrada sin inspector del punto'), findsOneWidget);
    });

    testWidgets('con un carro sin su revisión de llegada cerrada no deja cerrar', (tester) async {
      _ahora = _a(50);
      await _abrir(
        tester,
        EntregasRepositorioFalso()..puntos = [_sinNadie(espera: _espera(), estados: {_vin1: EstadoEntrega.sinInspeccion})],
      );

      await _tocar(tester, find.widgetWithText(FilledButton, 'Recibe una persona del lugar'));

      expect(find.text('Falta la revisión de llegada del VIN $_vin1: termínala antes de firmar.'), findsOneWidget);
    });

    testWidgets('«Cerrar con mi firma» lleva el motivo «Sin inspector del punto»', (tester) async {
      _ahora = _a(50);
      final arnes = await _abrir(tester, EntregasRepositorioFalso()..puntos = [_sinNadie(espera: _espera())]);

      await _tocar(tester, find.widgetWithText(OutlinedButton, 'Cerrar con mi firma'));
      expect(find.text('Sin inspector del punto'), findsOneWidget);
      await _firmarEn(tester, 0);
      await _tocar(tester, find.widgetWithText(FilledButton, 'Cerrar sin inspector del punto'));

      final enviada = arnes.gateway.entregasEnviadas.single;
      expect((enviada.motivoCodigo, enviada.firma?.isNotEmpty), (MotivoUnaFirma.sinInspectorDelPunto, true));
      expect(enviada.rechazos, isEmpty);
      expect(find.text('Entrega cerrada sin inspector del punto'), findsOneWidget);
    });

    testWidgets('si el servidor responde plan_b_no_habilitado, dice los minutos que faltan', (tester) async {
      _ahora = _a(50);
      final arnes = Arnes(sesion: sesionConductor);
      arnes.gateway.errores['cerrarEntregaConUnaFirma'] = [
        _rechazo(409, 'plan_b_no_habilitado', 'Todavía no puedes cerrar sin inspector del punto: faltan 5 minutos'),
      ];
      await _abrir(tester, EntregasRepositorioFalso()..puntos = [_sinNadie(espera: _espera())], arnes: arnes);

      await _tocar(tester, find.widgetWithText(OutlinedButton, 'Cerrar con mi firma'));
      await _firmarEn(tester, 0);
      await _tocar(tester, find.widgetWithText(FilledButton, 'Cerrar sin inspector del punto'));

      expect(find.text('Aún no se puede cerrar sin inspector del punto'), findsOneWidget);
      expect(find.textContaining('faltan 5 minutos'), findsOneWidget);
      expect(arnes.cola.jobs, isEmpty);
    });
  });

  // CA-5 y ESC-07 / ESC-09: el inspector que llega, y el que suelta la entrega.
  group('el inspector que llega', () {
    testWidgets('al refrescar, la espera desaparece y se firma con él', (tester) async {
      _ahora = _a(70);
      final entregas = EntregasRepositorioFalso()..puntos = [_sinNadie(espera: _espera())];
      await _abrir(tester, entregas);
      expect(find.text('Esperando al inspector del punto'), findsOneWidget);

      entregas.puntos = [
        puntoDe('Puerto', receptor: ReceptorParada.inspectorDelPunto, receptorNombre: 'Pedro', espera: _espera(estado: EstadoEspera.inspectorLlego)),
      ];
      await _tocar(tester, find.byTooltip('Actualizar la parada'));

      expect(find.text('Esperando al inspector del punto'), findsNothing);
      expect(find.text('Pedro tomó la entrega: firma con él'), findsOneWidget);
      expect(find.widgetWithText(FilledButton, 'Recibe una persona del lugar'), findsNothing);
      expect(_noHayInspector, findsNothing);
    });

    testWidgets('si la soltó, vuelve «No hay inspector» y la nueva activación cuenta desde cero', (tester) async {
      _ahora = _a(55);
      final arnes = Arnes(sesion: sesionConductor)..red.estable = false;
      await _abrir(
        tester,
        EntregasRepositorioFalso()..puntos = [_sinNadie(espera: _espera(estado: EstadoEspera.inspectorLlego))],
        arnes: arnes,
      );

      expect(find.text('Esperando al inspector del punto'), findsNothing);
      await _tocar(tester, _noHayInspector);
      await _tocar(tester, find.text('El inspector no llegó'));
      await _tocar(tester, find.widgetWithText(FilledButton, 'Empezar la espera'));

      expect(DateTime.parse((arnes.cola.jobs.single as SinInspectorParadaJob).fechaMomento).toUtc(), _a(55));
      expect(find.text('Avisaremos al coordinador en 15 min'), findsOneWidget);
      expect(find.text('Podrás cerrar sin inspector del punto en 45 min'), findsWidgets);
    });
  });

  // CA-6 y ESC-10: lo que el servidor rechazó después se explica, en la parada y en «Cola».
  group('el rechazo tardío', () {
    EntregaPuntoJob rechazada(String motivo) => EntregaPuntoJob(
          id: 'e1',
          etiqueta: 'Entrega Puerto',
          creadoEn: 1,
          usuarioId: sesionConductor.usuario.id,
          viajeId: 'v1',
          lugarId: 'l-Puerto',
          firma: 'M0 0L10 10',
          fechaMomento: '2026-11-26T09:20:00-05:00',
          recibeNombre: 'Andrés Pardo',
          recibeCedula: '79123456',
          firmaRecibe: 'M1 1L9 9',
        ).conIntento(intentos: 1, error: 'texto crudo del servidor', codigo: motivo, fallido: true);

    testWidgets('en la parada: un inspector la tomó, y la firma guardada sale para firmar con él', (tester) async {
      final arnes = Arnes(sesion: sesionConductor);
      await arnes.cola.agregar(rechazada(MotivoParada.entregaTomada));
      await _abrir(
        tester,
        EntregasRepositorioFalso()..puntos = [puntoDe('Puerto', receptor: ReceptorParada.inspectorDelPunto, receptorNombre: 'Pedro')],
        arnes: arnes,
      );

      expect(
        find.text('Mientras se enviaba, un inspector del punto tomó la entrega: firma con él. Tu firma se conserva'),
        findsOneWidget,
      );
      expect(find.text('texto crudo del servidor'), findsNothing);
      await _tocar(tester, find.widgetWithText(FilledButton, 'Firmar con él con mi firma'));

      final enviada = arnes.gateway.entregasEnviadas.single;
      expect((enviada.firma, enviada.motivoCodigo, enviada.momento.fecha), ('M0 0L10 10', null, '2026-11-26T09:20:00-05:00'));
      expect(arnes.gateway.externosEnviados.single, (null, null, null));
      expect(arnes.cola.jobs, isEmpty);
    });

    testWidgets('en «Cola», cada motivo con su explicación y nunca el texto crudo', (tester) async {
      for (final (motivo, texto) in [
        (MotivoParada.entregaTomada, 'Mientras se enviaba, un inspector del punto tomó la entrega: firma con él. Tu firma se conserva'),
        (MotivoParada.planBNoHabilitado, 'Aún no se podía cerrar sin inspector del punto: espera y vuelve a intentarlo'),
        (MotivoParada.sinReceptor, 'Aquí firma el inspector del punto: pídele que tome la entrega en su app'),
        (MotivoParada.entregaCerradaSinInspector, 'Esta entrega ya se cerró sin inspector del punto'),
        ('motivo_que_no_conozco', 'El servidor rechazó la entrega'),
      ]) {
        final arnes = Arnes(sesion: sesionConductor);
        await arnes.cola.agregar(rechazada(motivo));
        await _abrir(tester, EntregasRepositorioFalso()..puntos = [_sinNadie()], arnes: arnes, pantalla: const ColaPage());

        expect(find.text(texto), findsOneWidget, reason: motivo);
        expect(find.text('texto crudo del servidor'), findsNothing, reason: motivo);
        expect(find.widgetWithText(TextButton, 'Abrir la parada'), findsOneWidget, reason: motivo);
      }
    });

    testWidgets('desde «Cola» se abre la parada', (tester) async {
      final arnes = Arnes(sesion: sesionConductor);
      await arnes.cola.agregar(rechazada('motivo_que_no_conozco'));
      await _abrir(tester, EntregasRepositorioFalso()..puntos = [_sinNadie()], arnes: arnes, pantalla: const ColaPage());

      await _tocar(tester, find.widgetWithText(TextButton, 'Abrir la parada'));

      expect(find.text('Entrega · Puerto'), findsOneWidget);
      expect(find.text('El servidor rechazó la entrega'), findsOneWidget);
    });
  });

  // CA-10 (C19): el inspector que tomó la parada califica los daños que marcó el conductor con el plan B.
  group('los daños que marcó el conductor', () {
    const dano = DanoPorCalificar(hallazgoId: 'h1', vin: _vin1, parte: 'Puerta delantera', clase: 'golpe', evidenciaId: 'ev1');

    EntregaPunto tomadaConDanos({Map<String, EstadoEntrega> estados = const {}, List<DanoPorCalificar> danos = const [dano]}) =>
        puntoDe('Puerto', receptor: ReceptorParada.inspectorDelPunto, receptorNombre: _pedro, danosPorCalificar: danos, estados: estados);

    testWidgets('el inspector los ve por calificar y «Firmar» espera, con cuántos faltan', (tester) async {
      await _abrir(tester, EntregasRepositorioFalso()..puntos = [tomadaConDanos()], arnes: Arnes());
      await _firmarEn(tester, 0);

      expect(find.text('Por calificar en esta parada'), findsOneWidget);
      expect(find.text('Puerta delantera · Golpe'), findsOneWidget);
      expect(find.widgetWithText(FilledButton, 'Calificar'), findsOneWidget);
      expect(_boton(tester, 'Entregar los 2 carros').onPressed, isNull);
      expect(find.text('Califica los daños que marcó el conductor: falta 1'), findsOneWidget);
    });

    testWidgets('«Calificar» abre la calificación con la zona y la clase que ya trae', (tester) async {
      final arnes = Arnes()
        ..novedades.delViaje = const [
          NovedadPendiente(
            id: 'h1',
            vin: _vin1,
            proceso: 'descargue',
            momento: 'llegada',
            tamano: 'mas_20cm',
            clase: 'golpe',
            zona: 'Izquierdo 7 · Puerta delantera',
            cuadranteNumero: 7,
            lado: 'izquierdo',
          ),
        ];
      await _abrir(tester, EntregasRepositorioFalso()..puntos = [tomadaConDanos()], arnes: arnes);

      await _tocar(tester, find.widgetWithText(FilledButton, 'Calificar'));

      expect(find.text('Revisar la novedad'), findsOneWidget);
      expect(find.text('Zona: Izquierdo 7 · Puerta delantera'), findsOneWidget);
      expect(find.widgetWithText(FilledButton, 'Calificar en esta zona'), findsOneWidget);
    });

    testWidgets('los carros revisados salen como revisados y los que faltan le ofrecen «Capturar»', (tester) async {
      await _abrir(
        tester,
        EntregasRepositorioFalso()..puntos = [tomadaConDanos(estados: {_vin2: EstadoEntrega.sinInspeccion}, danos: const [])],
        arnes: Arnes(),
      );

      expect(find.text('Revisado'), findsOneWidget);
      expect(find.widgetWithText(OutlinedButton, 'Capturar'), findsOneWidget);
    });

    testWidgets('el conductor no ve la lista', (tester) async {
      await _abrir(tester, EntregasRepositorioFalso()..puntos = [tomadaConDanos()]);

      expect(find.text('Por calificar en esta parada'), findsNothing);
      expect(find.widgetWithText(FilledButton, 'Calificar'), findsNothing);
      expect(find.widgetWithText(OutlinedButton, 'Capturar'), findsNothing);
    });

    testWidgets('el 409 danos_sin_calificar se dice con el mensaje del servidor', (tester) async {
      final arnes = Arnes();
      arnes.gateway.errores['firmarEntregaPunto'] = [
        _rechazo(409, 'danos_sin_calificar', 'Antes de firmar, califica los daños que marcó el conductor en esta parada: falta 1'),
      ];
      await _abrir(tester, EntregasRepositorioFalso()..puntos = [tomadaConDanos(danos: const [])], arnes: arnes);

      await _firmarEn(tester, 0);
      await _tocar(tester, find.widgetWithText(FilledButton, 'Entregar los 2 carros'));

      expect(find.text('Faltan daños por calificar'), findsOneWidget);
      expect(find.textContaining('Antes de firmar, califica los daños que marcó el conductor en esta parada: falta 1'), findsOneWidget);
    });
  });

  // CA-11 (C25 y C28): con el plan B y la persona del lugar, el conductor rechaza un carro a pedido de ella.
  group('rechazar un carro con el plan B', () {
    const golpe = DanoDeLlegada(hallazgoId: 'h-golpe', zona: 'Izquierdo 7 · Puerta delantera', clase: 'golpe');

    EntregasRepositorioFalso conGolpeEnElSegundo() => EntregasRepositorioFalso()
      ..puntos = [_sinNadie(espera: _espera())]
      ..revisiones = {'ins-$_vin2': const RevisionDeLlegada(cerrada: true, danosConFoto: [golpe])};

    testWidgets('antes del plan B no se ofrece', (tester) async {
      _ahora = _a(40);
      await _abrir(tester, conGolpeEnElSegundo());

      expect(find.widgetWithText(TextButton, 'Rechazar por novedad'), findsNothing);
    });

    testWidgets('con la persona del lugar se rechaza por un daño con foto, y el botón dice cuántos', (tester) async {
      _ahora = _a(50);
      final arnes = await _abrir(tester, conGolpeEnElSegundo());

      await _tocar(tester, find.widgetWithText(FilledButton, 'Recibe una persona del lugar'));
      expect(find.widgetWithText(TextButton, 'Rechazar por novedad'), findsNWidgets(2));
      final sinDano = tester.widget<TextButton>(find.widgetWithText(TextButton, 'Rechazar por novedad').first);
      expect(sinDano.onPressed, isNull, reason: 'el primero no tiene un daño con foto');
      expect(find.text('Para rechazarlo, registra el daño con su foto en la revisión de llegada'), findsOneWidget);

      await _tocar(tester, find.widgetWithText(TextButton, 'Rechazar por novedad').last);
      await _tocar(tester, find.text('Izquierdo 7 · Puerta delantera'));
      await tester.enterText(find.widgetWithText(TextField, 'Nombre de quien recibe'), 'Andrés Pardo');
      await tester.enterText(find.widgetWithText(TextField, 'Cédula de quien recibe'), '79123456');
      await tester.pump();
      await _firmarEn(tester, 0);
      await _firmarEn(tester, 1);
      await _tocar(tester, find.widgetWithText(FilledButton, 'Entregar 1 carro · 1 rechazado'));

      expect(arnes.gateway.entregasEnviadas.single.rechazos, [const RechazoCarro(vin: _vin2, hallazgoId: 'h-golpe')]);
    });

    testWidgets('con su sola firma no se rechaza ninguno, y lo dice', (tester) async {
      _ahora = _a(50);
      await _abrir(tester, conGolpeEnElSegundo());

      await _tocar(tester, find.widgetWithText(OutlinedButton, 'Cerrar con mi firma'));

      expect(find.widgetWithText(TextButton, 'Rechazar por novedad'), findsNothing);
      expect(
        find.text('Con tu sola firma se entregan todos los carros: para rechazar uno, cierra con la persona del lugar'),
        findsOneWidget,
      );
    });

    // Si un inspector toma la parada mientras tanto, los rechazos marcados con la persona del lugar no viajan: en una
    // parada con inspector los decide él, y el servidor respondería 403.
    testWidgets('si un inspector toma la parada, los rechazos del plan B se olvidan y no se envían', (tester) async {
      _ahora = _a(50);
      final entregas = conGolpeEnElSegundo();
      final arnes = await _abrir(tester, entregas);
      await _tocar(tester, find.widgetWithText(FilledButton, 'Recibe una persona del lugar'));
      await _tocar(tester, find.widgetWithText(TextButton, 'Rechazar por novedad').last);
      await _tocar(tester, find.text('Izquierdo 7 · Puerta delantera'));
      expect(find.text('Rechazado por novedad: Izquierdo 7 · Puerta delantera'), findsOneWidget);

      entregas.puntos = [puntoDe('Puerto', receptor: ReceptorParada.inspectorDelPunto, receptorNombre: 'Pedro')];
      await _tocar(tester, find.byTooltip('Actualizar la parada'));
      expect(find.text('Rechazado por novedad: Izquierdo 7 · Puerta delantera'), findsNothing);
      await _firmarEn(tester, 0);
      await _tocar(tester, find.widgetWithText(FilledButton, 'Entregar los 2 carros'));

      expect(arnes.gateway.entregasEnviadas.single.rechazos, isEmpty);
    });

    testWidgets('al volver a elegir cómo cerrar, lo decidido con la persona del lugar se olvida', (tester) async {
      _ahora = _a(50);
      final arnes = await _abrir(tester, conGolpeEnElSegundo());
      await _tocar(tester, find.widgetWithText(FilledButton, 'Recibe una persona del lugar'));
      await _tocar(tester, find.widgetWithText(TextButton, 'Rechazar por novedad').last);
      await _tocar(tester, find.text('Izquierdo 7 · Puerta delantera'));

      await _tocar(tester, find.widgetWithText(TextButton, 'Volver: elegir cómo cerrar'));
      await _tocar(tester, find.widgetWithText(OutlinedButton, 'Cerrar con mi firma'));
      expect(find.text('Rechazado por novedad: Izquierdo 7 · Puerta delantera'), findsNothing);
      await _firmarEn(tester, 0);
      await _tocar(tester, find.widgetWithText(FilledButton, 'Cerrar sin inspector del punto'));

      expect(arnes.gateway.entregasEnviadas.single.rechazos, isEmpty);
    });
  });

  // Lo que encontró la revisión de #421.
  group('la revisión', () {
    // CA-3: la activación sale sola de la cola, en segundo plano. La parada se vuelve a pedir y el reloj no se pierde.
    testWidgets('cuando la activación sale sola de la cola, la parada se vuelve a pedir y el reloj sigue', (tester) async {
      _ahora = _a(10);
      final arnes = Arnes(sesion: sesionConductor)..red.estable = false;
      final entregas = EntregasRepositorioFalso()..puntos = [_sinNadie()];
      await _abrir(tester, entregas, arnes: arnes);
      await _tocar(tester, _noHayInspector);
      await _tocar(tester, find.text('El inspector no llegó'));
      await _tocar(tester, find.widgetWithText(FilledButton, 'Empezar la espera'));
      expect(entregas.anotadas.single.fechaMomento, (arnes.cola.jobs.single as SinInspectorParadaJob).fechaMomento,
          reason: 'la hora y los plazos quedan en la copia sin señal de la parada');
      final consultas = entregas.consultas;

      // Vuelve la señal, la cola la envía sola, pero la parada no se puede pedir: el reloj sigue con la activación.
      arnes.red.estable = true;
      entregas.error = const ErrorApi.red();
      final contenedor = ProviderScope.containerOf(tester.element(find.byType(FirmaEntregaPuntoPage)));
      await contenedor.read(syncControllerProvider.notifier).sincronizar();
      await _avanzar(tester);

      expect(arnes.cola.jobs, isEmpty);
      expect(entregas.consultas, greaterThan(consultas), reason: 'la parada se volvió a pedir');
      expect(find.text('Esperando al inspector del punto'), findsOneWidget);
      expect(find.text('Avisaremos al coordinador en 15 min'), findsOneWidget, reason: 'cuenta desde que la activó');
      expect(_noHayInspector, findsNothing);

      // Con señal llega la espera del servidor, y es ella la que cuenta.
      entregas
        ..error = null
        ..puntos = [_sinNadie(espera: _espera(avisoAt: _a(15)))];
      _ahora = _a(16);
      await _tocar(tester, find.byTooltip('Actualizar la parada'));
      expect(find.text('El coordinador ya fue avisado'), findsOneWidget);
      expect(_noHayInspector, findsNothing);
    });

    // CA-6: la activación rechazada después se explica en la parada.
    testWidgets('la activación que el servidor rechazó después se explica en la parada', (tester) async {
      final arnes = Arnes(sesion: sesionConductor);
      await arnes.cola.agregar(
        _activacionGuardada(_ocho).conIntento(intentos: 1, error: 'texto crudo', codigo: MotivoParada.entregaTomada, fallido: true),
      );
      await _abrir(tester, EntregasRepositorioFalso()..puntos = [_sinNadie()], arnes: arnes);

      expect(find.text('No se activó «No hay inspector»'), findsOneWidget);
      expect(find.text('Mientras se enviaba, un inspector del punto tomó la entrega: firma con él'), findsOneWidget);
      expect(find.text('texto crudo'), findsNothing);
      expect(find.text('Esperando al inspector del punto'), findsNothing);
      await _tocar(tester, find.widgetWithText(OutlinedButton, 'Quitar el envío'));
      expect(arnes.cola.jobs, isEmpty);
    });

    // CA-6: lo que agotó sus intentos sin respuesta no se dice como un rechazo del servidor.
    testWidgets('en «Cola», el envío sin respuesta no se dice como rechazado', (tester) async {
      final arnes = Arnes(sesion: sesionConductor);
      await arnes.cola.agregar(
        EntregaPuntoJob(
          id: 'e1',
          etiqueta: 'Entrega Puerto',
          creadoEn: 1,
          usuarioId: sesionConductor.usuario.id,
          viajeId: 'v1',
          lugarId: 'l-Puerto',
          firma: 'M0 0L10 10',
        ).conIntento(intentos: 8, error: 'No se pudo conectar con el servidor.', codigo: codigoSinRespuesta, fallido: true),
      );
      await _abrir(tester, EntregasRepositorioFalso()..puntos = [_sinNadie()], arnes: arnes, pantalla: const ColaPage());

      expect(find.text('No hubo respuesta del servidor tras varios intentos: vuelve a intentarlo'), findsOneWidget);
      expect(find.text('El servidor rechazó la entrega'), findsNothing);
    });

    // CA-4: con el plan B, quién recibe ya no pide un inspector del punto.
    testWidgets('con el plan B, «quién recibe» no sigue pidiendo un inspector del punto', (tester) async {
      _ahora = _a(50);
      await _abrir(tester, EntregasRepositorioFalso()..puntos = [_sinNadie(espera: _espera())]);

      expect(find.text('Nadie tomó la entrega: ya puedes cerrar sin inspector del punto.'), findsOneWidget);
      expect(find.text('Este punto tiene inspector: pídele a un inspector del punto que tome la entrega en su app'), findsNothing);
      await _tocar(tester, find.widgetWithText(FilledButton, 'Recibe una persona del lugar'));
      expect(find.text('Aquí recibe la persona del lugar'), findsOneWidget);
      expect(find.text('Nadie tomó la entrega: su nombre, su cédula y su firma van aquí mismo.'), findsOneWidget);
    });

    // CA-3: con la espera del servidor, «ya fue avisado» lo dice el servidor; pasada la hora se le pregunta.
    testWidgets('sin el aviso del servidor no dice que el coordinador ya fue avisado, y le pregunta', (tester) async {
      _ahora = _a(20);
      final entregas = EntregasRepositorioFalso()..puntos = [_sinNadie(espera: _espera())];
      await _abrir(tester, entregas);
      final consultas = entregas.consultas;

      expect(find.text('El coordinador ya fue avisado'), findsNothing);
      expect(find.text('Avisaremos al coordinador en un momento'), findsOneWidget);
      entregas.puntos = [_sinNadie(espera: _espera(avisoAt: _a(15)))];
      await tester.pump(const Duration(seconds: 16));
      await _avanzar(tester);

      expect(entregas.consultas, greaterThan(consultas));
      expect(find.text('El coordinador ya fue avisado'), findsOneWidget);
    });

    // CA-10: el servidor también rechaza la firma del conductor mientras falten daños por calificar.
    testWidgets('el conductor no firma con el inspector mientras falten daños por calificar, y lo dice', (tester) async {
      final arnes = Arnes(sesion: sesionConductor);
      await arnes.cola.agregar(
        EntregaPuntoJob(
          id: 'e1',
          etiqueta: 'Entrega Puerto',
          creadoEn: 1,
          usuarioId: sesionConductor.usuario.id,
          viajeId: 'v1',
          lugarId: 'l-Puerto',
          firma: 'M0 0L10 10',
        ).conIntento(intentos: 1, error: 'x', codigo: MotivoParada.entregaTomada, fallido: true),
      );
      await _abrir(
        tester,
        EntregasRepositorioFalso()
          ..puntos = [
            puntoDe(
              'Puerto',
              receptor: ReceptorParada.inspectorDelPunto,
              receptorNombre: 'Pedro',
              danosPorCalificar: const [DanoPorCalificar(hallazgoId: 'h1', vin: _vin1)],
            ),
          ],
        arnes: arnes,
      );
      await _firmarEn(tester, 0);

      expect(_boton(tester, 'Entregar los 2 carros').onPressed, isNull);
      expect(find.text('El inspector del punto tiene que calificar antes los daños que marcaste: falta 1'), findsNWidgets(2));
      expect(find.widgetWithText(FilledButton, 'Firmar con él con mi firma'), findsNothing);
      expect(find.text('Por calificar en esta parada'), findsNothing, reason: 'el conductor no ve la lista');
    });

    // La firma sola no lleva las fotos del cierre: se borran del teléfono en vez de quedar sin dueño.
    testWidgets('al firmar con el inspector con la firma guardada, las fotos del cierre se borran', (tester) async {
      final arnes = Arnes(sesion: sesionConductor);
      arnes.archivos.existentes.add('/tmp/cierre.jpg');
      await arnes.cola.agregar(
        EntregaPuntoJob(
          id: 'e1',
          etiqueta: 'Entrega Puerto',
          creadoEn: 1,
          usuarioId: sesionConductor.usuario.id,
          viajeId: 'v1',
          lugarId: 'l-Puerto',
          firma: 'M0 0L10 10',
          motivoCodigo: MotivoUnaFirma.sinInspectorDelPunto,
          fotos: const [FotoLocal(ruta: '/tmp/cierre.jpg', selloFecha: '2026-11-26T14:20:00.000Z')],
          vines: const [_vin1, _vin2],
        ).conIntento(intentos: 1, error: 'x', codigo: MotivoParada.entregaTomada, fallido: true),
      );
      await _abrir(
        tester,
        EntregasRepositorioFalso()..puntos = [puntoDe('Puerto', receptor: ReceptorParada.inspectorDelPunto, receptorNombre: 'Pedro')],
        arnes: arnes,
      );

      await _tocar(tester, find.widgetWithText(FilledButton, 'Firmar con él con mi firma'));

      expect(arnes.archivos.borrados, contains('/tmp/cierre.jpg'));
      expect(arnes.gateway.entregasEnviadas.single.motivoCodigo, isNull);
      expect(arnes.gateway.entregasEnviadas.single.fotos, isEmpty);
    });

    // Lo entregado con el plan B lo dice en la lista de paradas.
    testWidgets('el carro entregado con el plan B dice «Entregado sin inspector del punto»', (tester) async {
      await _abrir(
        tester,
        EntregasRepositorioFalso()
          ..puntos = [
            const EntregaPunto(
              viajeId: 'v1',
              lugarId: 'l-Puerto',
              lugar: 'Puerto',
              unidades: [
                UnidadEntrega(vin: _vin1, estado: EstadoEntrega.cerrada, sinInspectorDelPunto: true),
                UnidadEntrega(vin: _vin2, estado: EstadoEntrega.cerrada),
              ],
              pendientes: 0,
              entregada: true,
              tieneInspector: true,
            ),
          ],
        pantalla: const EntregasViajePage(viajeId: 'v1'),
      );

      expect(find.text('Entregado sin inspector del punto'), findsOneWidget);
      expect(find.text('Entregado'), findsOneWidget);
    });
  });
}
