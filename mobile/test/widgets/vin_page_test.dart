import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:intl/date_symbol_data_local.dart';
import 'package:traza_autos/core/errores/error_api.dart';
import 'package:traza_autos/core/tema/tema.dart';
import 'package:traza_autos/core/ui/widgets.dart';
import 'package:traza_autos/features/auth/domain/sesion.dart';
import 'package:traza_autos/features/catalogos/providers.dart';
import 'package:traza_autos/features/inspecciones/domain/novedad_pendiente.dart';
import 'package:traza_autos/features/dibujo_dano/presentation/selector_zona_dano.dart';
import 'package:traza_autos/features/vehiculos/data/vehiculos_repositorio.dart';
import 'package:traza_autos/features/vehiculos/domain/consulta.dart';
import 'package:traza_autos/features/vehiculos/domain/inventario.dart';
import 'package:traza_autos/features/vehiculos/presentation/vin_page.dart';
import 'package:traza_autos/features/vehiculos/providers.dart';
import 'package:traza_autos/features/viajes/domain/viaje.dart';
import 'package:traza_autos/features/viajes/providers.dart';

import '../support/arnes.dart';
import '../support/dobles_catalogos.dart';
import '../support/dobles_viajes.dart';

/// La pantalla del VIN con su ruta de custodia. Es la que abre el inspector en el patio: si revienta, no hay
/// informe, y en una compilación de release revienta en blanco, sin decir nada.

class VehiculosRepositorioFalso implements VehiculosRepositorio {
  VehiculosRepositorioFalso(this.respuesta, {this.error});

  final Map<String, dynamic> respuesta;

  /// Lo que lanza en vez de responder; un 403 es el VIN de un viaje ajeno.
  final Object? error;

  /// Cuántas veces se pidió el inventario: con eso se ve si la ficha se recargó al volver de otra pantalla.
  int llamadas = 0;

  /// No se busca en estas pruebas: la pantalla que la usa tiene las suyas.
  @override
  Future<List<VehiculoBusqueda>> buscar(String termino) async => const [];

  @override
  Future<Inventario> inventario(String vin) async {
    llamadas++;
    if (error != null) throw error!;
    return Inventario.fromJson(respuesta);
  }
}

/// El inventario tal como lo sirve la API: dos etapas, la primera con su salida cerrada.
Map<String, dynamic> inventarioJson() => {
      'id': 'v1',
      'vin': 'VIN00000000000001',
      'marca': 'Renault',
      'modelo': 'Kwid',
      'operacion': 'nacional',
      'etapas': [
        {
          'id': 'e1',
          'orden': 1,
          'proceso': 'cargue',
          'lugar': 'CEDI 1',
          'requiere_inspeccion': true,
          'viaje_id': 'viaje-1',
          'con_personal_apoyo': false,
          'inspecciones': [
            {
              'id': 'i1',
              'momento': 'salida',
              'momento_nombre': 'Inspección de salida',
              'fecha': '2026-01-02T10:00:00Z',
              'cerrada_at': '2026-01-02T11:00:00Z',
              'estado': 'cerrada',
              // Lo registró el inspector del viaje, no quien mira.
              'usuario_id': 'inspector-1',
              'notas': 'Llovía al cargar',
              'notas_aclaratorias': [
                {'id': 'n1', 'texto': 'El golpe del capó resultó ser V1+', 'autor_nombre': 'Ana Pérez'},
              ],
              'hallazgos': [
                {'id': 'h1', 'parte_vehiculo': 'Capot', 'clase_dano': 'golpe', 'tamano_dano': 'mas_20cm'},
              ],
              'evidencias': [],
              'transferencia': null,
            },
          ],
        },
        {
          'id': 'e2',
          'orden': 2,
          'proceso': 'transporte',
          'lugar': null,
          'requiere_inspeccion': true,
          'viaje_id': 'viaje-1',
          'con_personal_apoyo': false,
          'inspecciones': [],
        },
      ],
    };

/// Quien está en sesión. Por defecto la inspectora del arnés; las pruebas del recibo entran como conductor.
const conductor = Sesion(
  token: 'jwt',
  usuario: Usuario(id: 'conductor-1', nombre: 'Luis Gómez', email: '', rol: 'conductor'),
);

/// Corrige lo que hizo otra persona, mientras nadie haya firmado.
const administrador = Sesion(
  token: 'jwt',
  usuario: Usuario(id: 'admin-1', nombre: 'Marta Ruiz', email: '', rol: 'admin'),
);

/// Consulta los carros de su cliente: no captura, no firma y no corrige.
const coordinador = Sesion(
  token: 'jwt',
  usuario: Usuario(id: 'coord-1', nombre: 'Sara Calderón', email: '', rol: 'coordinador_calidad'),
);

/// Una transferencia con la firma de quien entrega y nada más: basta una para que no se corrija.
const firmaDeEntrega = <String, dynamic>{
  'entrega_usuario_id': 'u-entrega',
  'recibe_usuario_id': 'u-recibe',
  'entrega_firmada_at': '2026-01-02T12:00:00Z',
  'recibe_firmada_at': null,
  'tipo_cierre': null,
};

/// El mismo inventario con la salida cerrada en otras manos o ya firmada: las dos cosas deciden quién corrige.
/// Por defecto la hizo quien está en sesión (`u1`), que es el caso en que sí se corrige.
Map<String, dynamic> inventarioSalida({String usuarioId = 'u1', Map<String, dynamic>? transferencia, String? momento}) {
  final json = inventarioJson();
  final etapa = (json['etapas'] as List<dynamic>).first as Map<String, dynamic>;
  final salida = (etapa['inspecciones'] as List<dynamic>).first as Map<String, dynamic>;
  salida['usuario_id'] = usuarioId;
  salida['transferencia'] = transferencia;
  // La salida del cargue es la acomodacion y se corrige distinto; con `momento` se pasa al otro caso.
  if (momento != null) {
    salida['momento'] = momento;
    salida['momento_nombre'] = momento == 'salida' ? 'Inspección de salida' : 'Inspección de llegada';
  }
  return json;
}

/// Las etapas tal cual, pero sin un solo informe: es lo que se ve al abrir una unidad de un viaje en el que
/// nunca se capturó nada.
Map<String, dynamic> inventarioSinInformes() {
  final json = inventarioJson();
  for (final etapa in json['etapas'] as List<dynamic>) {
    (etapa as Map<String, dynamic>)['inspecciones'] = <Map<String, dynamic>>[];
  }
  return json;
}

/// Una novedad del conductor a medio describir: le faltan la zona, la clase y el nivel.
const novedadSinClasificar = NovedadPendiente(
  id: 'n1',
  vin: 'VIN00000000000001',
  proceso: 'transporte',
  momento: 'llegada',
  clase: 'golpe',
  tamano: 'mas_20cm',
  foto: FotoNovedad(tomadaEn: null, sinUbicacion: true),
);

/// Dos informes cerrados: el del cargue, ya firmado, y otro posterior en el transporte. El de arriba es el
/// posterior, así que el firmado solo se ve abriéndolo desde su fila.
Map<String, dynamic> inventarioConDosInformes() {
  final json = inventarioSalida(transferencia: firmaDeEntrega);
  final transporte = (json['etapas'] as List<dynamic>)[1] as Map<String, dynamic>;
  transporte['inspecciones'] = [
    {
      'id': 'i2',
      'momento': 'salida',
      'momento_nombre': 'Inspección de salida',
      'fecha': '2026-01-05T10:00:00Z',
      'cerrada_at': '2026-01-05T11:00:00Z',
      'estado': 'cerrada',
      'usuario_id': 'u1',
      'hallazgos': <Map<String, dynamic>>[],
      'evidencias': <Map<String, dynamic>>[],
      'transferencia': null,
    },
  ];
  return json;
}

/// Los mismos pumps de [abrir], para lo que se abre después de tocar algo.
Future<void> asentar(WidgetTester tester) async {
  for (var i = 0; i < 10; i++) {
    await tester.pump(const Duration(milliseconds: 50));
  }
}

Future<void> abrir(
  WidgetTester tester, {
  Map<String, dynamic>? inventario,
  Sesion? sesion,
  ViajesRepositorioFalso? viajes,
  List<NovedadPendiente> novedades = const [],
  Object? error,
}) async {
  await tester.binding.setSurfaceSize(const Size(600, 2400));
  addTearDown(() => tester.binding.setSurfaceSize(null));
  await tester.pumpWidget(
    ProviderScope(
      retry: (_, _) => null,
      overrides: [
        ...(Arnes(sesion: sesion ?? sesionPrueba)..novedades.lista = novedades).overrides,
        catalogosRepositorioProvider.overrideWithValue(CatalogosRepositorioFalso()..guardado = catalogosCon('Golpe')),
        vehiculosRepositorioProvider
            .overrideWithValue(VehiculosRepositorioFalso(inventario ?? inventarioJson(), error: error)),
        viajesRepositorioProvider.overrideWithValue(viajes ?? ViajesRepositorioFalso()),
      ],
      child: MaterialApp(theme: crearTema(), home: const VinPage(vin: 'VIN00000000000001')),
    ),
  );
  for (var i = 0; i < 10; i++) {
    await tester.pump(const Duration(milliseconds: 50));
  }
}

void main() {
  setUpAll(() => initializeDateFormatting('es_CO'));

  testWidgets('dibuja la ruta de custodia con sus dos momentos por etapa', (tester) async {
    await abrir(tester);

    expect(find.text('CEDI 1'), findsOneWidget);
    expect(find.text('Inspección de salida'), findsWidgets);
    expect(find.text('Inspección de llegada'), findsWidgets);
  });

  testWidgets('la salida de la etapa siguiente espera a la anterior', (tester) async {
    await abrir(tester);

    expect(find.textContaining('Falta cerrar la llegada de'), findsOneWidget);
  });

  // S5-HU-25.4: el conductor recibe el vehículo y firma su recibo. Si antes ve lo que marcó el inspector, su
  // recibo deja de decir lo que él encontró y pasa a confirmar lo del otro.
  group('la tarjeta del informe cuando mira un conductor', () {
    testWidgets('no muestra los hallazgos del inspector ni su dibujo de daños', (tester) async {
      await abrir(tester, sesion: conductor);

      expect(find.textContaining('Capot'), findsNothing);
      expect(find.byType(SelectorZonaDano), findsNothing);
      expect(find.text('Llovía al cargar'), findsNothing);
      // Una nota aclaratoria puede decir justo lo que se le oculta antes de firmar.
      expect(find.text('Notas aclaratorias'), findsNothing);
      expect(find.textContaining('resultó ser V1+'), findsNothing);
      expect(find.widgetWithText(OutlinedButton, 'Agregar nota aclaratoria'), findsNothing);
      expect(find.textContaining('queda en tu firma'), findsOneWidget, reason: 'se dice por qué, no se calla');
    });

    testWidgets('sí muestra el informe que registró él mismo', (tester) async {
      final mio = inventarioJson();
      (((mio['etapas'] as List<dynamic>).first as Map<String, dynamic>)['inspecciones'] as List<dynamic>)
          .cast<Map<String, dynamic>>()
          .first['usuario_id'] = conductor.usuario.id;

      await abrir(tester, inventario: mio, sesion: conductor);

      expect(find.textContaining('Capot'), findsOneWidget);
      expect(find.text('Llovía al cargar'), findsOneWidget);
    });

    testWidgets('quien inspecciona lo sigue viendo entero', (tester) async {
      await abrir(tester);

      expect(find.textContaining('Capot'), findsOneWidget);
      expect(find.byType(SelectorZonaDano), findsOneWidget);
    });
  });

  // El defecto visto en el celular: la captura de un transporte pedía las 7 fotos obligatorias, que es lo que
  // ese proceso NO pide. La ruta llevaba la etiqueta del catálogo («Transporte») y `pideChecklist` compara
  // contra el código («transporte»), así que nunca coincidían.
  testWidgets('la captura recibe el código del proceso, no su etiqueta', (tester) async {
    final abiertas = <Uri>[];
    final router = GoRouter(
      initialLocation: '/vin',
      routes: [
        GoRoute(path: '/vin', builder: (_, _) => const VinPage(vin: 'VIN00000000000001')),
        GoRoute(
          path: '/inspecciones/vin/:vin/captura/:etapaId',
          builder: (_, state) {
            abiertas.add(state.uri);
            return const Scaffold(body: Text('captura'));
          },
        ),
      ],
    );
    await tester.binding.setSurfaceSize(const Size(600, 2400));
    addTearDown(() => tester.binding.setSurfaceSize(null));
    await tester.pumpWidget(
      ProviderScope(
        retry: (_, _) => null,
        overrides: [
          ...Arnes().overrides,
          catalogosRepositorioProvider.overrideWithValue(
            CatalogosRepositorioFalso()..guardado = catalogosCon('Golpe', etapas: etapasDePrueba),
          ),
          vehiculosRepositorioProvider.overrideWithValue(VehiculosRepositorioFalso(inventarioJson())),
        ],
        child: MaterialApp.router(theme: crearTema(), routerConfig: router),
      ),
    );
    for (var i = 0; i < 10; i++) {
      await tester.pump(const Duration(milliseconds: 50));
    }

    // La llegada del cargue: la única capturable de esa etapa, porque su salida ya está cerrada.
    await tester.tap(find.widgetWithText(BotonEnFila, 'Capturar').first);
    await tester.pumpAndSettle();

    expect(abiertas.single.queryParameters['proceso'], 'cargue', reason: 'el código, no «Cargue»');
  });

  group('la unidad no sale', () {
    /// Inventario con un V1+ en una inspección cerrada del viaje: la única forma de que el servidor lo acepte.
    Map<String, dynamic> conV1Plus() {
      final json = inventarioJson();
      final etapa = (json['etapas'] as List).first as Map<String, dynamic>;
      final inspeccion = (etapa['inspecciones'] as List).first as Map<String, dynamic>;
      inspeccion['hallazgos'] = <Map<String, dynamic>>[
        {'id': 'h1', 'zona': 'Izquierdo 7 · Espejo retrovisor', 'cuadrante_numero': 7, 'lado': 'izquierdo',
         'clase_dano': 'golpe', 'tamano_dano': 'mas_20cm', 'nivel_severidad': 'v1_plus'},
      ];
      return json;
    }

    ViajesRepositorioFalso delInspector(String inspectorId, {bool noSale = false}) => ViajesRepositorioFalso()
      ..viajeDeEtapa = Viaje(
        id: 'viaje-1',
        codigoMtrack: 'MTK-1',
        placa: 'ABC123',
        estado: 'en_curso',
        inspectorId: inspectorId,
        vehiculos: [
          ViajeVehiculo(
            vin: 'VIN00000000000001',
            estadoUnidad: noSale ? 'no_sale' : estadoUnidadActiva,
            noSale: noSale ? const NoSale(motivo: 'Golpe profundo en el capó', vinReemplazo: 'VIN00000000000002') : null,
          ),
        ],
      );

    testWidgets('se le ofrece al inspector del viaje con un V1+ cerrado', (tester) async {
      await abrir(tester, inventario: conV1Plus(), viajes: delInspector('u1'));

      expect(find.text('La unidad no sale'), findsOneWidget);
    });

    testWidgets('a un inspector que no es el del viaje, no', (tester) async {
      await abrir(tester, inventario: conV1Plus(), viajes: delInspector('otro-inspector'));

      expect(find.text('La unidad no sale'), findsNothing);
    });

    testWidgets('al conductor, nunca', (tester) async {
      await abrir(tester, inventario: conV1Plus(), sesion: conductor, viajes: delInspector('conductor-1'));

      expect(find.text('La unidad no sale'), findsNothing);
    });

    testWidgets('sin un V1+ no hay nada que marcar', (tester) async {
      await abrir(tester, viajes: delInspector('u1'));

      expect(find.text('La unidad no sale'), findsNothing);
    });

    testWidgets('una unidad ya marcada dice por qué, en vez del botón', (tester) async {
      await abrir(tester, inventario: conV1Plus(), viajes: delInspector('u1', noSale: true));

      expect(find.text('La unidad no sale'), findsNothing);
      expect(find.text('Esta unidad no sale'), findsOneWidget);
      expect(find.text('Golpe profundo en el capó'), findsOneWidget);
      expect(find.text('La reemplaza VIN00000000000002'), findsOneWidget);
    });
  });

  group('las novedades del conductor por revisar', () {
    // Como la registra el conductor: con su clase y su tamaño, sin nivel y, en este caso, sin GPS.
    const sinClasificar = novedadSinClasificar;

    testWidgets('quien inspecciona las ve, con lo que el conductor alcanzó a decir', (tester) async {
      await abrir(tester, novedades: const [sinClasificar]);

      expect(find.text('Novedades por revisar'), findsOneWidget);
      // Lo que el conductor alcanzó a decir: la clase que eligió y el tamaño. Sin la clase, la fila no decía qué
      // tipo de daño era y había que abrirla para saberlo.
      expect(find.text('Golpe · Más de 20 cm'), findsOneWidget);
      expect(find.text('Sin hora · Sin ubicación'), findsOneWidget);
      expect(find.widgetWithText(FilledButton, 'Revisar'), findsOneWidget);
    });

    testWidgets('el conductor no revisa la suya: el nivel lo pone otra persona', (tester) async {
      await abrir(tester, sesion: conductor, novedades: const [sinClasificar]);

      expect(find.text('Novedades por revisar'), findsNothing);
    });

    testWidgets('sin novedades pendientes no se ocupa espacio', (tester) async {
      await abrir(tester);

      expect(find.text('Novedades por revisar'), findsNothing);
    });
  });

  /// S7-HU-18/20: la ficha solo le da tarjeta al informe vigente. Los demás —el del cargue que ya se firmó, la
  /// llegada de una etapa anterior— se abren desde su fila, que es donde se lee lo que explica por qué no se
  /// corrigen. Sin esto el mensaje estaba escrito pero no se leía nunca.
  group('abrir el informe de un momento', () {
    ViajesRepositorioFalso enCurso() => ViajesRepositorioFalso()
      ..viajeDeEtapa = Viaje(
        id: 'viaje-1',
        codigoMtrack: 'MTK-1',
        placa: 'ABC123',
        estado: 'en_curso',
        inspectorId: 'u1',
        vehiculos: const [ViajeVehiculo(vin: 'VIN00000000000001')],
      );

    testWidgets('el informe firmado se abre desde su fila y dice por qué no se corrige', (tester) async {
      await abrir(tester, inventario: inventarioConDosInformes(), viajes: enCurso());

      expect(find.textContaining('ya fue firmada'), findsNothing, reason: 'arriba está el informe posterior');

      await tester.tap(find.byIcon(Icons.chevron_right).first);
      await asentar(tester);

      // El «Corregir» que queda en pantalla es el de la tarjeta de abajo, que es otro informe y sí se corrige.
      expect(find.textContaining('ya fue firmada'), findsOneWidget);
    });

    testWidgets('un momento sin informe no se abre: no hay nada que leer', (tester) async {
      await abrir(tester, inventario: inventarioConDosInformes(), viajes: enCurso());

      // Cuatro momentos (dos por etapa) y solo dos tienen informe.
      expect(find.byIcon(Icons.chevron_right), findsNWidgets(2));
    });
  });

  /// S7-HU-09: el coordinador de calidad abre la ficha para mirarla. Lo que el servidor le va a rechazar no se le
  /// ofrece, y lo que no puede ver se le explica por su cliente, no por un viaje que nunca tuvo.
  group('el coordinador consulta, no trabaja', () {
    ViajesRepositorioFalso enCurso() => ViajesRepositorioFalso()
      ..viajeDeEtapa = Viaje(
        id: 'viaje-1',
        codigoMtrack: 'MTK-1',
        placa: 'ABC123',
        estado: 'en_curso',
        inspectorId: 'u1',
        vehiculos: const [ViajeVehiculo(vin: 'VIN00000000000001')],
      );

    testWidgets('la ficha dice que es de consulta y no ofrece capturar ni firmar', (tester) async {
      await abrir(tester, viajes: enCurso(), sesion: coordinador);

      expect(find.text(mensajeSoloConsulta), findsWidgets);
      // «Capturar» es un BotonEnFila, que por dentro es un OutlinedButton: buscarlo como FilledButton no
      // encuentra nada ni cuando el botón está ahí, y la prueba pasaría sin blindaje ninguno.
      expect(find.widgetWithText(OutlinedButton, 'Capturar'), findsNothing);
      expect(find.widgetWithText(FilledButton, 'Firmar transferencia'), findsNothing);
      expect(find.widgetWithText(OutlinedButton, 'Corregir'), findsNothing);
      expect(find.widgetWithText(OutlinedButton, 'Agregar nota aclaratoria'), findsNothing);
    });

    testWidgets('los momentos se le siguen mostrando, para poder abrir lo que ya está registrado', (tester) async {
      await abrir(tester, viajes: enCurso(), sesion: coordinador);

      expect(find.text('Inspección de salida'), findsWidgets);
      expect(find.byIcon(Icons.chevron_right), findsWidgets);
      expect(find.widgetWithText(OutlinedButton, 'Capturar'), findsNothing);
    });

    // El control positivo del grupo: sobre el MISMO inventario, al inspector del viaje sí le sale el botón. Sin
    // esto, las ausencias de arriba no dicen nada: podrían serlo por el fixture y no por el rol.
    testWidgets('el inspector del viaje sí sigue viendo sus botones', (tester) async {
      await abrir(tester, viajes: enCurso());

      expect(find.text(mensajeSoloConsulta), findsNothing);
      expect(find.widgetWithText(OutlinedButton, 'Capturar'), findsWidgets);
    });

    testWidgets('un carro de otro cliente se explica por el cliente, no por el viaje', (tester) async {
      await abrir(
        tester,
        sesion: coordinador,
        error: const ErrorApi(message: 'Prohibido', code: 'FORBIDDEN', status: 403),
      );

      expect(find.text('Este carro no es de tu cliente.'), findsOneWidget);
      expect(find.textContaining('cliente al que perteneces'), findsOneWidget);
      expect(find.widgetWithText(FilledButton, 'Reintentar'), findsNothing);
    });
  });

  group('un VIN que no es de mis viajes', () {
    testWidgets('un 403 lo dice y no ofrece reintentar', (tester) async {
      await abrir(tester, error: const ErrorApi(message: 'Prohibido', code: 'FORBIDDEN', status: 403));

      expect(find.text(mensajeFueraDeMisViajes), findsOneWidget);
      // Reintentar un 403 da otro 403: ofrecerlo manda a la persona a golpear una puerta cerrada.
      expect(find.widgetWithText(FilledButton, 'Reintentar'), findsNothing);
    });

    testWidgets('un 404 sí se puede reintentar y dice otra cosa', (tester) async {
      await abrir(tester, error: const ErrorApi(message: 'No existe', code: 'NOT_FOUND', status: 404));

      expect(find.text(mensajeFueraDeMisViajes), findsNothing);
      expect(find.widgetWithText(FilledButton, 'Reintentar'), findsOneWidget);
    });
  });

  // Lo que se vio en el celular: desde un viaje ya terminado la app ofrecía capturar, y el envío habría muerto
  // con «El viaje está finalizado y no admite inspecciones nuevas» después de tomar las siete fotos.
  // Corregir es rehacer la inspección. Nada se crea en el servidor al pulsarlo: la versión siguiente se pide al
  // enviar, porque una versión vacía no se puede descartar y bloquearía volver a corregir.
  group('corregir una inspección cerrada', () {
    ViajesRepositorioFalso enEstado(String estado) => ViajesRepositorioFalso()
      ..viajeDeEtapa = Viaje(
        id: 'viaje-1',
        codigoMtrack: 'MTK-1',
        placa: 'ABC123',
        estado: estado,
        inspectorId: 'u1',
        vehiculos: const [ViajeVehiculo(vin: 'VIN00000000000001')],
      );

    testWidgets('una cerrada y sin firmas la ofrece a quien la hizo', (tester) async {
      await abrir(tester, inventario: inventarioSalida(), viajes: enEstado('en_curso'));

      expect(find.widgetWithText(OutlinedButton, 'Corregir'), findsOneWidget);
    });

    // S7-HU-19: el servidor responde 403 CORRECCION_SOLO_AUTOR. Ofrecer el botón costaría siete fotos para nada.
    testWidgets('no se la ofrece a otro inspector del mismo viaje', (tester) async {
      await abrir(tester, inventario: inventarioSalida(usuarioId: 'inspector-1'), viajes: enEstado('en_curso'));

      expect(find.widgetWithText(OutlinedButton, 'Corregir'), findsNothing);
    });

    testWidgets('el administrador sí corrige lo que hizo otra persona', (tester) async {
      await abrir(
        tester,
        inventario: inventarioSalida(usuarioId: 'inspector-1'),
        viajes: enEstado('en_curso'),
        sesion: administrador,
      );

      expect(find.widgetWithText(OutlinedButton, 'Corregir'), findsOneWidget);
    });

    // S7-HU-19: 409 INSPECCION_YA_FIRMADA. Basta una firma, y después de ella lo que queda es la nota aclaratoria.
    testWidgets('con una sola firma ya no se corrige, y lo dice', (tester) async {
      await abrir(tester, inventario: inventarioSalida(transferencia: firmaDeEntrega), viajes: enEstado('en_curso'));

      expect(find.widgetWithText(OutlinedButton, 'Corregir'), findsNothing);
      expect(find.textContaining('ya fue firmada'), findsOneWidget);
      expect(find.widgetWithText(OutlinedButton, 'Agregar nota aclaratoria'), findsOneWidget);
    });

    testWidgets('tampoco la corrige el administrador con una firma de por medio', (tester) async {
      await abrir(
        tester,
        inventario: inventarioSalida(usuarioId: 'inspector-1', transferencia: firmaDeEntrega),
        viajes: enEstado('en_curso'),
        sesion: administrador,
      );

      expect(find.widgetWithText(OutlinedButton, 'Corregir'), findsNothing);
    });

    testWidgets('con el viaje terminado dice por qué no, en vez de dejar el botón gris', (tester) async {
      await abrir(tester, inventario: inventarioSalida(), viajes: enEstado('finalizado'));

      expect(find.widgetWithText(OutlinedButton, 'Corregir'), findsNothing);
      expect(find.text('El viaje ya terminó: este informe no admite correcciones.'), findsOneWidget);
    });

    // Rehacer la inspección cuesta siete fotos: quien lo pulsa tiene que saber a qué se compromete, y que lo
    // que ya firmó no se pierde.
    testWidgets('avisa que hay que repetir las fotos y que la versión anterior no cambia', (tester) async {
      await abrir(tester, inventario: inventarioSalida(momento: 'llegada'), viajes: enEstado('en_curso'));

      await tester.tap(find.widgetWithText(OutlinedButton, 'Corregir'));
      await tester.pumpAndSettle();

      expect(find.textContaining('volver a tomar todas las fotos obligatorias'), findsOneWidget);
      expect(find.textContaining('La versión anterior no cambia'), findsOneWidget);
    });

    // La salida del cargue es la acomodacion: no pide las siete vistas, pide volver a ubicar cada unidad. El
    // aviso tiene que decir lo que de verdad va a pedir la pantalla siguiente.
    testWidgets('al corregir la salida del cargue avisa de la acomodación, no de las siete fotos', (tester) async {
      await abrir(tester, inventario: inventarioSalida(), viajes: enEstado('en_curso'));

      await tester.tap(find.widgetWithText(OutlinedButton, 'Corregir'));
      await tester.pumpAndSettle();

      expect(find.textContaining('ubicar todas las unidades en la niñera'), findsOneWidget);
      expect(find.textContaining('todas las fotos obligatorias'), findsNothing);
    });

    testWidgets('al conductor que solo recibe no se le ofrece corregir lo del inspector', (tester) async {
      await abrir(tester, viajes: enEstado('en_curso'), sesion: conductor);

      expect(find.widgetWithText(OutlinedButton, 'Corregir'), findsNothing);
    });
  });

  group('un viaje que ya terminó', () {
    ViajesRepositorioFalso enEstado(String estado) => ViajesRepositorioFalso()
      ..viajeDeEtapa = Viaje(
        id: 'viaje-1',
        codigoMtrack: 'MTK-1',
        placa: 'ABC123',
        estado: estado,
        inspectorId: 'u1',
        vehiculos: const [ViajeVehiculo(vin: 'VIN00000000000001')],
      );

    testWidgets('lo dice y no ofrece capturar', (tester) async {
      await abrir(tester, viajes: enEstado('finalizado'));

      expect(find.text(mensajeViajeTerminado), findsWidgets);
      // «Capturar» es un BotonEnFila, que por dentro es un OutlinedButton.
      expect(find.widgetWithText(OutlinedButton, 'Capturar'), findsNothing);
    });


    // Lo que se vio en el celular al abrir una unidad de un viaje cerrado: la pantalla decia «solo se puede
    // consultar» y al mismo tiempo pedia informes por todos lados. Nada de eso lo va a hacer ya nadie.
    testWidgets('ninguna etapa pide informe: ya no espera nada de nadie', (tester) async {
      await abrir(tester, viajes: enEstado('finalizado'));

      expect(find.text('Requiere informe'), findsNothing);
      expect(find.text('Solo firma'), findsNothing);
    });

    testWidgets('con el viaje en curso la insignia sigue ahi', (tester) async {
      await abrir(tester, viajes: enEstado('en_curso'));

      expect(find.text('Requiere informe'), findsWidgets);
    });

    testWidgets('sin informes en ese viaje lo dice sin sonar a pendiente', (tester) async {
      await abrir(tester, inventario: inventarioSinInformes(), viajes: enEstado('finalizado'));

      expect(find.text('En este viaje no se registró ningún informe.'), findsOneWidget);
      expect(find.textContaining('aún no tiene informes'), findsNothing);
    });

    testWidgets('sin informes y con el viaje vivo sigue diciendo que faltan', (tester) async {
      await abrir(tester, inventario: inventarioSinInformes(), viajes: enEstado('en_curso'));

      expect(find.text('Este VIN aún no tiene informes.'), findsOneWidget);
    });

    // Se piden por VIN: la que sale en un viaje cerrado es de otro viaje, el que sigue en curso.
    testWidgets('no ofrece revisar novedades de otro viaje', (tester) async {
      await abrir(tester, viajes: enEstado('finalizado'), novedades: const [novedadSinClasificar]);

      expect(find.text('Novedades por revisar'), findsNothing);
      expect(find.widgetWithText(FilledButton, 'Revisar'), findsNothing);
    });

    testWidgets('con el viaje en curso sí las ofrece', (tester) async {
      await abrir(tester, viajes: enEstado('en_curso'), novedades: const [novedadSinClasificar]);

      expect(find.text('Novedades por revisar'), findsOneWidget);
    });

    testWidgets('en curso sí se trabaja', (tester) async {
      await abrir(tester, viajes: enEstado('en_curso'));

      expect(find.text(mensajeViajeTerminado), findsNothing);
    });
  });

  /// S7-HU-18: al volver de la captura, la ficha tiene que recargarse.
  ///
  /// Hace falta un enrutador de verdad porque el defecto estaba justamente en cómo navegaba: con `go` la ficha
  /// se quedaba debajo, intacta, mostrando la versión anterior como vigente y con su botón de corregir. Si la
  /// corrección había quedado en cola —sin señal—, volver a tocarlo encolaba una segunda corrección de la misma
  /// inspección, que el servidor mata por el índice único, después de repetir las siete fotos.
  group('volver de corregir', () {
    late VehiculosRepositorioFalso vehiculos;

    ViajesRepositorioFalso enCurso() => ViajesRepositorioFalso()
      ..viajeDeEtapa = Viaje(
        id: 'viaje-1',
        codigoMtrack: 'MTK-1',
        placa: 'ABC123',
        estado: 'en_curso',
        inspectorId: 'u1',
        vehiculos: const [ViajeVehiculo(vin: 'VIN00000000000001')],
      );

    Future<void> abrirConRutas(WidgetTester tester, {Map<String, dynamic>? inventario}) async {
      await tester.binding.setSurfaceSize(const Size(600, 2400));
      addTearDown(() => tester.binding.setSurfaceSize(null));
      vehiculos = VehiculosRepositorioFalso(inventario ?? inventarioSalida());
      final rutas = GoRouter(
        initialLocation: '/inspecciones/vin/VIN00000000000001',
        routes: [
          GoRoute(
            path: '/inspecciones/vin/:vin',
            builder: (_, estado) => VinPage(vin: estado.pathParameters['vin']!),
            routes: [
              GoRoute(
                path: 'captura/:etapaId',
                builder: (contexto, _) => Scaffold(
                  body: Center(
                    child: TextButton(onPressed: () => contexto.pop(), child: const Text('cerrar la captura')),
                  ),
                ),
              ),
            ],
          ),
        ],
      );
      addTearDown(rutas.dispose);
      await tester.pumpWidget(
        ProviderScope(
          retry: (_, _) => null,
          overrides: [
            ...Arnes().overrides,
            catalogosRepositorioProvider.overrideWithValue(CatalogosRepositorioFalso()..guardado = catalogosCon('Golpe')),
            vehiculosRepositorioProvider.overrideWithValue(vehiculos),
            viajesRepositorioProvider.overrideWithValue(enCurso()),
          ],
          child: MaterialApp.router(theme: crearTema(), routerConfig: rutas),
        ),
      );
      await asentar(tester);
    }

    /// Pulsa «Corregir» y confirma. [dentroDe] acota de cuál de las dos tarjetas: la hoja tapa la de la ficha,
    /// pero las dos siguen en el árbol.
    Future<void> corregir(WidgetTester tester, {Finder? dentroDe}) async {
      final boton = find.widgetWithText(OutlinedButton, 'Corregir');
      await tester.tap(dentroDe == null ? boton.first : find.descendant(of: dentroDe, matching: boton));
      await tester.pumpAndSettle();
      await tester.tap(find.widgetWithText(TextButton, 'Corregir'));
      await tester.pumpAndSettle();
    }

    testWidgets('la ficha se vuelve a pedir, para no ofrecer corregir lo que ya se corrigió', (tester) async {
      await abrirConRutas(tester);
      final antes = vehiculos.llamadas;

      await corregir(tester);
      expect(find.text('cerrar la captura'), findsOneWidget, reason: 'se abrió la captura');

      await tester.tap(find.text('cerrar la captura'));
      await tester.pumpAndSettle();

      expect(vehiculos.llamadas, greaterThan(antes));
    });

    // La hoja es imperativa: no se va sola al navegar, y se quedaba encima de la cámara.
    testWidgets('si se corrige desde la hoja de un momento, la hoja se cierra antes', (tester) async {
      await abrirConRutas(tester);

      await tester.tap(find.byIcon(Icons.chevron_right).first);
      await tester.pumpAndSettle();
      expect(find.byType(BottomSheet), findsOneWidget);

      await corregir(tester, dentroDe: find.byType(BottomSheet));

      expect(find.text('cerrar la captura'), findsOneWidget);
      expect(find.byType(BottomSheet), findsNothing, reason: 'si no, se queda encima de la cámara');
    });
  });

  /// S7-HU-04.2: el daño de un extra cuenta como daño del carro, pero no tiene zona.
  group('el daño de un extra en la ficha', () {
    /// El informe con un daño normal (cuadrante 7) y uno del platón (sin cuadrante, con su extra_id).
    Map<String, dynamic> inventarioConExtraDanado() {
      final json = inventarioJson();
      final etapa = (json['etapas'] as List<dynamic>).first as Map<String, dynamic>;
      final inspeccion = (etapa['inspecciones'] as List<dynamic>).first as Map<String, dynamic>;
      inspeccion['extras'] = [
        {'id': 'x-1', 'nombre': 'Platón', 'tipo': 'platon', 'tipo_nombre': 'Platón'},
      ];
      // Uno de zona, que si se marca en el dibujo, y el del platon, que no tiene donde marcarse. Se reemplaza
      // la lista entera: la del fixture es de Map<String, String> y no admite ni numeros ni nulos.
      inspeccion['hallazgos'] = <Map<String, dynamic>>[
        {
          'id': 'h-zona',
          'zona': 'Izquierdo 7',
          'cuadrante_numero': 7,
          'lado': 'izquierdo',
          'clase_dano': 'golpe',
          'tamano_dano': '1_5cm',
          'nivel_severidad': 'v2',
        },
        {
          'id': 'h-extra',
          'zona': null,
          'cuadrante_numero': null,
          'lado': null,
          'clase_dano': 'golpe',
          'tamano_dano': '1_5cm',
          'nivel_severidad': 'v2',
          'extra_id': 'x-1',
        },
      ];
      return json;
    }

    testWidgets('se lista con el nombre del extra, no como «Sin zona»', (tester) async {
      await abrir(tester, inventario: inventarioConExtraDanado());

      expect(find.textContaining('Platón: Golpe'), findsOneWidget);
      expect(find.textContaining('Sin zona'), findsNothing);
    });

    testWidgets('no pone ningún punto en el dibujo: el extra no tiene zona', (tester) async {
      await abrir(tester, inventario: inventarioConExtraDanado());

      final selector = tester.widget<SelectorZonaDano>(find.byType(SelectorZonaDano).first);
      expect(
        selector.danos.length,
        1,
        reason: 'el del cuadrante sí se marca; el del platón no tiene dónde marcarse',
      );
    });
  });

  /// S7-HU-03.2 CA-2: un daño sin nivel se ve marcado, no vacío. En blanco parecía leve; es justo al revés.
  group('un daño sin calificar en la ficha', () {
    Map<String, dynamic> inventarioConSinCalificar() {
      final json = inventarioJson();
      final etapa = (json['etapas'] as List<dynamic>).first as Map<String, dynamic>;
      final inspeccion = (etapa['inspecciones'] as List<dynamic>).first as Map<String, dynamic>;
      inspeccion['hallazgos'] = <Map<String, dynamic>>[
        {
          'id': 'h-sin',
          'zona': 'Izquierdo 4',
          'cuadrante_numero': 4,
          'lado': 'izquierdo',
          'clase_dano': 'golpe',
          'tamano_dano': '1_5cm',
          'nivel_severidad': null,
        },
        {
          'id': 'h-con',
          'zona': 'Izquierdo 7',
          'cuadrante_numero': 7,
          'lado': 'izquierdo',
          'clase_dano': 'golpe',
          'tamano_dano': '1_5cm',
          'nivel_severidad': 'v2',
        },
      ];
      return json;
    }

    testWidgets('se lista como «Sin calificar», no con el nivel en blanco', (tester) async {
      await abrir(tester, inventario: inventarioConSinCalificar());

      expect(find.textContaining('Sin calificar'), findsOneWidget);
      expect(find.textContaining('Izquierdo 4: Golpe'), findsOneWidget);
    });

    // El dibujo ya lo distingue por color: lo que importa aquí es que la zona llegue marcada, con nivel nulo.
    testWidgets('en el dibujo la zona queda marcada, sin nivel', (tester) async {
      await abrir(tester, inventario: inventarioConSinCalificar());

      final selector = tester.widget<SelectorZonaDano>(find.byType(SelectorZonaDano).first);
      expect(selector.danos.length, 2, reason: 'las dos zonas tienen daño');
      expect(selector.danos.values.where((n) => n == null), hasLength(1), reason: 'la que no está calificada');
    });
  });

  /// La transferencia cuelga de un informe, no de la etapa: una etapa puede tener la llegada firmada y la salida
  /// no. La insignia sola se leía como si toda la etapa estuviera cerrada, al lado del «Corregir» de la otra.
  group('una etapa firmada dice cuál informe lo está', () {
    ViajesRepositorioFalso enCurso() => ViajesRepositorioFalso()
      ..viajeDeEtapa = Viaje(
        id: 'viaje-1',
        codigoMtrack: 'MTK-1',
        placa: 'ABC123',
        estado: 'en_curso',
        inspectorId: 'u1',
        vehiculos: const [ViajeVehiculo(vin: 'VIN00000000000001')],
      );

    const cerradaFirmada = <String, dynamic>{
      'entrega_usuario_id': 'u-entrega',
      'recibe_usuario_id': 'u-recibe',
      'entrega_firmada_at': '2026-01-02T12:00:00Z',
      'recibe_firmada_at': '2026-01-02T12:30:00Z',
      'tipo_cierre': 'firmada',
    };

    testWidgets('la insignia va con el momento del informe que se firmó', (tester) async {
      await abrir(
        tester,
        inventario: inventarioSalida(momento: 'llegada', transferencia: cerradaFirmada),
        viajes: enCurso(),
      );

      expect(find.text('Es la firma de la inspección de llegada.'), findsOneWidget);
    });

    testWidgets('y nombra la salida cuando es ella la firmada', (tester) async {
      await abrir(
        tester,
        inventario: inventarioSalida(momento: 'salida', transferencia: cerradaFirmada),
        viajes: enCurso(),
      );

      expect(find.text('Es la firma de la inspección de salida.'), findsOneWidget);
    });
  });
}
