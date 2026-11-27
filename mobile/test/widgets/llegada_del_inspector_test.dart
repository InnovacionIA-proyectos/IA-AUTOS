import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intl/date_symbol_data_local.dart';
import 'package:traza_autos/core/tema/tema.dart';
import 'package:traza_autos/features/auth/domain/sesion.dart';
import 'package:traza_autos/features/catalogos/providers.dart';
import 'package:traza_autos/features/sync/domain/job.dart';
import 'package:traza_autos/features/vehiculos/presentation/vin_page.dart';
import 'package:traza_autos/features/vehiculos/providers.dart';
import 'package:traza_autos/features/viajes/domain/entrega_punto.dart';
import 'package:traza_autos/features/viajes/domain/espera_del_punto.dart';
import 'package:traza_autos/features/viajes/domain/viaje.dart';
import 'package:traza_autos/features/viajes/providers.dart';

import '../support/arnes.dart';
import '../support/dobles_catalogos.dart';
import '../support/dobles_viajes.dart';
import 'vin_page_test.dart' show VehiculosRepositorioFalso;

/// S8-HU-05.2 CA-9 (C10): en una parada con inspectores, la ficha del carro no le ofrece al conductor «Capturar» la
/// llegada del descargue hasta que el plan B se habilite. En una parada sin inspectores no cambia nada.

const _vin = 'VIN00000000000001';

const _conductor = Sesion(
  token: 'jwt',
  usuario: Usuario(id: 'conductor-1', nombre: 'Luis Gómez', email: '', rol: 'conductor'),
);

/// Las 08:00 de Colombia: cuando el conductor tocó «No hay inspector».
final _ocho = DateTime.utc(2026, 11, 26, 13);

/// Un carro cuyo único paso pendiente es la revisión de llegada del descargue.
Map<String, dynamic> _soloElDescargue() => {
      'id': 'v1',
      'vin': _vin,
      'marca': 'Renault',
      'modelo': 'Kwid',
      'operacion': 'nacional',
      'etapas': [
        {
          'id': 'e3',
          'orden': 1,
          'proceso': 'descargue',
          'lugar': 'Puerto de Cartagena',
          'requiere_inspeccion': true,
          'viaje_id': 'viaje-1',
          'inspecciones': <dynamic>[],
        },
      ],
    };

ViajesRepositorioFalso _viaje() => ViajesRepositorioFalso()
  ..viajeDeEtapa = Viaje(
    id: 'viaje-1',
    codigoMtrack: 'MTK-1',
    placa: 'ABC123',
    estado: 'en_curso',
    conductorId: 'conductor-1',
    vehiculos: const [
      ViajeVehiculo(
        vin: _vin,
        lugarDestino: 'Puerto de Cartagena',
        lugarDestinoId: 'l-Puerto',
        estadoTransporte: EstadoTransporte.sinNovedad,
      ),
    ],
  );

EsperaDelPunto _espera() => EsperaDelPunto(
      estado: EstadoEspera.esperando,
      activadaAt: _ocho,
      avisoDesde: _ocho.add(const Duration(minutes: 15)),
      planBDesde: _ocho.add(const Duration(minutes: 45)),
    );

Future<Arnes> _abrir(WidgetTester tester, EntregaPunto parada, {required DateTime ahora, Sesion sesion = _conductor, Arnes? arnes}) async {
  await tester.binding.setSurfaceSize(const Size(600, 2400));
  addTearDown(() => tester.binding.setSurfaceSize(null));
  final usado = arnes ?? Arnes(sesion: sesion);
  await tester.pumpWidget(
    ProviderScope(
      retry: (_, _) => null,
      overrides: [
        ...usado.overrides,
        catalogosRepositorioProvider.overrideWithValue(CatalogosRepositorioFalso()..guardado = catalogosCon('Golpe')),
        vehiculosRepositorioProvider.overrideWithValue(VehiculosRepositorioFalso(_soloElDescargue())),
        viajesRepositorioProvider.overrideWithValue(_viaje()),
        entregasRepositorioProvider.overrideWithValue(EntregasRepositorioFalso()..puntos = [parada]),
        horaDelTelefonoProvider.overrideWithValue(() => ahora),
      ],
      child: MaterialApp(theme: crearTema(), home: const VinPage(vin: _vin)),
    ),
  );
  for (var i = 0; i < 12; i++) {
    await tester.pump(const Duration(milliseconds: 50));
  }
  return usado;
}

Finder get _capturar => find.widgetWithText(OutlinedButton, 'Capturar');

void main() {
  setUpAll(() => initializeDateFormatting('es_CO'));

  testWidgets('sin espera, la revisión de llegada la hace el inspector del punto', (tester) async {
    await _abrir(tester, puntoDe('Puerto', receptor: ReceptorParada.sinReceptor), ahora: _ocho);

    expect(find.text('La revisión de llegada la hace el inspector del punto'), findsOneWidget);
    expect(_capturar, findsNothing);
  });

  testWidgets('con la espera activa, dice los minutos que faltan', (tester) async {
    await _abrir(
      tester,
      puntoDe('Puerto', receptor: ReceptorParada.sinReceptor, espera: _espera()),
      ahora: _ocho.add(const Duration(minutes: 40)),
    );

    expect(find.text('La revisión de llegada la hace el inspector del punto: podrás hacerla sin él en 5 min'), findsOneWidget);
    expect(_capturar, findsNothing);
  });

  // La cuenta baja sola, sin tocar nada.
  testWidgets('los minutos que faltan se actualizan solos', (tester) async {
    var ahora = _ocho.add(const Duration(minutes: 40));
    await tester.binding.setSurfaceSize(const Size(600, 2400));
    addTearDown(() => tester.binding.setSurfaceSize(null));
    final arnes = Arnes(sesion: _conductor);
    await tester.pumpWidget(
      ProviderScope(
        retry: (_, _) => null,
        overrides: [
          ...arnes.overrides,
          catalogosRepositorioProvider.overrideWithValue(CatalogosRepositorioFalso()..guardado = catalogosCon('Golpe')),
          vehiculosRepositorioProvider.overrideWithValue(VehiculosRepositorioFalso(_soloElDescargue())),
          viajesRepositorioProvider.overrideWithValue(_viaje()),
          entregasRepositorioProvider.overrideWithValue(
            EntregasRepositorioFalso()..puntos = [puntoDe('Puerto', receptor: ReceptorParada.sinReceptor, espera: _espera())],
          ),
          horaDelTelefonoProvider.overrideWithValue(() => ahora),
        ],
        child: MaterialApp(theme: crearTema(), home: const VinPage(vin: _vin)),
      ),
    );
    for (var i = 0; i < 12; i++) {
      await tester.pump(const Duration(milliseconds: 50));
    }
    expect(find.text('La revisión de llegada la hace el inspector del punto: podrás hacerla sin él en 5 min'), findsOneWidget);

    ahora = _ocho.add(const Duration(minutes: 42));
    await tester.pump(const Duration(seconds: 31));

    expect(find.text('La revisión de llegada la hace el inspector del punto: podrás hacerla sin él en 3 min'), findsOneWidget);
  });

  testWidgets('con la activación que espera señal también cuenta el reloj', (tester) async {
    final arnes = Arnes(sesion: _conductor)..red.estable = false;
    await arnes.cola.agregar(SinInspectorParadaJob(
      id: 'a1',
      etiqueta: 'No hay inspector · Puerto',
      creadoEn: 1,
      usuarioId: 'conductor-1',
      viajeId: 'viaje-1',
      lugarId: 'l-Puerto',
      motivo: 'inspector_no_llego',
      fechaMomento: isoConZona(_ocho),
      avisoMinutos: 15,
      planBMinutos: 30,
    ));

    await _abrir(
      tester,
      puntoDe('Puerto', receptor: ReceptorParada.sinReceptor),
      ahora: _ocho.add(const Duration(minutes: 30)),
      arnes: arnes,
    );

    expect(find.text('La revisión de llegada la hace el inspector del punto: podrás hacerla sin él en 15 min'), findsOneWidget);
  });

  testWidgets('con el plan B habilitado ofrece «Capturar»', (tester) async {
    await _abrir(
      tester,
      puntoDe('Puerto', receptor: ReceptorParada.sinReceptor, espera: _espera()),
      ahora: _ocho.add(const Duration(minutes: 46)),
    );

    expect(_capturar, findsOneWidget);
    expect(find.textContaining('La revisión de llegada la hace el inspector del punto'), findsNothing);
  });

  testWidgets('en una parada sin inspectores no cambia nada', (tester) async {
    await _abrir(tester, puntoDe('Puerto', tieneInspector: false, receptor: ReceptorParada.personaDelLugar), ahora: _ocho);

    expect(_capturar, findsOneWidget);
    expect(find.textContaining('La revisión de llegada la hace el inspector del punto'), findsNothing);
  });

  testWidgets('al inspector no le cambia nada', (tester) async {
    await _abrir(tester, puntoDe('Puerto', receptor: ReceptorParada.sinReceptor), ahora: _ocho, sesion: sesionPrueba);

    expect(find.textContaining('La revisión de llegada la hace el inspector del punto'), findsNothing);
  });
}
