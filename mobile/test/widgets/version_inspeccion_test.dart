import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intl/date_symbol_data_local.dart';
import 'package:traza_autos/core/tema/tema.dart';
import 'package:traza_autos/features/catalogos/providers.dart';
import 'package:traza_autos/features/inspecciones/domain/inspeccion_resumen.dart';
import 'package:traza_autos/features/vehiculos/data/vehiculos_repositorio.dart';
import 'package:traza_autos/features/vehiculos/domain/inventario.dart';
import 'package:traza_autos/features/vehiculos/presentation/vin_page.dart';
import 'package:traza_autos/features/vehiculos/providers.dart';
import 'package:traza_autos/features/viajes/providers.dart';

import '../support/arnes.dart';
import '../support/dobles_catalogos.dart';
import '../support/dobles_viajes.dart';

/// S6-HU-03.14 (parte de la app): la cadena de versiones de una inspección corregida se ve en la tarjeta del informe,
/// en el inventario y en el listado. Corregir no cambia lo cerrado: crea la versión siguiente.

class _InventarioFalso implements VehiculosRepositorio {
  _InventarioFalso(this.respuesta);

  final Map<String, dynamic> respuesta;

  @override
  Future<Inventario> inventario(String vin) async => Inventario.fromJson(respuesta);
}

Map<String, dynamic> _inspeccion(String id, int version, {String? corrige, String fecha = '2026-10-23T08:30:00Z'}) => {
      'id': id,
      'momento': 'llegada',
      'momento_nombre': 'Inspección de llegada',
      'fecha': fecha,
      'cerrada_at': fecha,
      'estado': 'cerrada',
      'usuario_id': 'u1',
      'hallazgos': [],
      'evidencias': [],
      'transferencia': null,
      'version': version,
      'corrige_inspeccion_id': corrige,
    };

Map<String, dynamic> _inventario(List<Map<String, dynamic>> inspecciones) => {
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
          'inspecciones': inspecciones,
        },
      ],
    };

Future<void> abrir(WidgetTester tester, List<Map<String, dynamic>> inspecciones) async {
  await tester.binding.setSurfaceSize(const Size(600, 3000));
  addTearDown(() => tester.binding.setSurfaceSize(null));
  await tester.pumpWidget(
    ProviderScope(
      retry: (_, _) => null,
      overrides: [
        ...Arnes().overrides,
        catalogosRepositorioProvider.overrideWithValue(CatalogosRepositorioFalso()..guardado = catalogosCon('Golpe')),
        vehiculosRepositorioProvider.overrideWithValue(_InventarioFalso(_inventario(inspecciones))),
        viajesRepositorioProvider.overrideWithValue(ViajesRepositorioFalso()),
      ],
      child: MaterialApp(theme: crearTema(), home: const VinPage(vin: 'VIN00000000000001')),
    ),
  );
  for (var i = 0; i < 12; i++) {
    await tester.pump(const Duration(milliseconds: 50));
  }
}

void main() {
  setUpAll(() => initializeDateFormatting('es_CO'));

  group('lo que trae la API', () {
    test('lee la versión y la inspección que corrige; sin ellas es la versión 1', () {
      final v2 = InspeccionVista.fromJson(_inspeccion('i2', 2, corrige: 'i1'));
      expect((v2.version, v2.corrigeInspeccionId), (2, 'i1'));
      final vieja = InspeccionVista.fromJson(_inspeccion('i1', 1)..remove('version')..remove('corrige_inspeccion_id'));
      expect((vieja.version, vieja.corrigeInspeccionId), (1, null));
    });

    test('el listado también la lee y la conserva en la copia guardada', () {
      final fila = InspeccionResumen.fromJson({..._inspeccion('i2', 2, corrige: 'i1'), 'vin': 'VIN1'});
      expect((fila.version, fila.corrigeInspeccionId), (2, 'i1'));
      final guardada = InspeccionResumen.fromJson(fila.toJson());
      expect((guardada.version, guardada.corrigeInspeccionId), (2, 'i1'));
      expect(InspeccionResumen.fromJson({..._inspeccion('i0', 1)..remove('version')}).version, 1);
    });

    test('el texto de la cadena solo aparece donde hay algo que decir', () {
      final v1 = InspeccionVista.fromJson(_inspeccion('i1', 1));
      final v2 = InspeccionVista.fromJson(_inspeccion('i2', 2, corrige: 'i1'));
      expect(textoDeVersion(v1), isNull);
      expect(textoDeVersion(v1, corregida: true), 'Versión 1 · ya fue corregida');
      expect(textoDeVersion(v2), 'Versión 2 · corrige la versión 1');
    });

    test('la etapa sabe qué versión ya fue corregida', () {
      final etapa = Inventario.fromJson(_inventario([_inspeccion('i1', 1), _inspeccion('i2', 2, corrige: 'i1')])).etapas.single;
      expect(etapa.fueCorregida(etapa.inspecciones.first), isTrue);
      expect(etapa.fueCorregida(etapa.inspecciones.last), isFalse);
    });

    test('la versión que vale en cada momento es la más alta, venga en el orden que venga', () {
      final inv = Inventario.fromJson(_inventario([_inspeccion('i2', 2, corrige: 'i1'), _inspeccion('i1', 1)]));
      final momentos = momentosDeEtapa(inv.etapas.single, inv);
      expect(momentos.firstWhere((m) => m.momento == 'llegada').inspeccion!.id, 'i2');
    });
  });

  group('la pantalla del VIN', () {
    testWidgets('una corrección dice que es la versión 2 y que corrige la 1', (tester) async {
      await abrir(tester, [_inspeccion('i1', 1), _inspeccion('i2', 2, corrige: 'i1', fecha: '2026-10-24T08:30:00Z')]);

      expect(find.text('Versión 2 · corrige la versión 1'), findsWidgets);
    });

    testWidgets('la tarjeta del informe es la de la versión que vale, no la anterior', (tester) async {
      await abrir(tester, [_inspeccion('i1', 1), _inspeccion('i2', 2, corrige: 'i1', fecha: '2026-10-24T08:30:00Z')]);

      expect(find.text('Versión 1 · ya fue corregida'), findsNothing);
      expect(find.textContaining('· Inspección de llegada'), findsOneWidget, reason: 'una sola tarjeta de informe, la de la versión 2');
    });

    testWidgets('una inspección sin corregir no dice nada de versiones', (tester) async {
      await abrir(tester, [_inspeccion('i1', 1)]);

      expect(find.textContaining('Versión'), findsNothing);
    });
  });
}
