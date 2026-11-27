import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/features/viajes/domain/viaje.dart';

Viaje con(String estado) => Viaje(id: 'v1', codigoMtrack: 'MTK-1', placa: 'ABC123', estado: estado);

/// Un viaje terminado conserva sus etapas y sus informes, pero ya no admite inspecciones nuevas: el servidor
/// responde 409. La app tiene que saberlo para no ofrecer capturar.
void main() {
  test('se trabaja mientras está programado o en curso', () {
    expect(con('programado').admiteTrabajo, isTrue);
    expect(con('en_curso').admiteTrabajo, isTrue);
  });

  test('terminado o cancelado, ya no', () {
    expect(con('finalizado').admiteTrabajo, isFalse);
    expect(con('cancelado').admiteTrabajo, isFalse);
  });

  test('un estado que la app no conozca no habilita a trabajar', () {
    expect(con('').admiteTrabajo, isFalse);
    expect(con('otro_estado_nuevo').admiteTrabajo, isFalse);
  });

  // S8-HU-03.2 CA-4 y CA-5: el muelle, el lugar de cargue y el destino de cada carro, también sin señal.
  group('el muelle y el destino', () {
    final delServidor = {
      'id': 'v1',
      'codigo_mtrack': 'MTK-1',
      'placa': 'ABC123',
      'estado': 'en_curso',
      'lugar_cargue_id': 'cedi',
      'muelle_id': 'm3',
      'muelle': 'Muelle 3',
      'cargue_firmado_por_ninera_at': null,
      'cargue_no_firmado_at': null,
      'vehiculos': [
        {'vin': 'VIN1', 'lugar_destino': 'Patio Guayabal', 'lugar_destino_cambiado_at': '2026-11-24T15:00:00Z'},
      ],
    };

    test('se leen del servidor', () {
      final viaje = Viaje.fromJson(delServidor);

      expect(viaje.lugarCargueId, 'cedi');
      expect(viaje.muelleId, 'm3');
      expect(viaje.muelle, 'Muelle 3');
      expect(viaje.cargueCerrado, isFalse);
      expect(viaje.vehiculos.single.lugarDestino, 'Patio Guayabal');
      expect(viaje.vehiculos.single.destinoCambiadoAt, DateTime.utc(2026, 11, 24, 15));
    });

    test('el cargue queda cerrado con la firma de la niñera o con la constancia', () {
      for (final campo in ['cargue_firmado_por_ninera_at', 'cargue_no_firmado_at']) {
        expect(Viaje.fromJson({...delServidor, campo: '2026-11-24T16:00:00Z'}).cargueCerrado, isTrue, reason: campo);
      }
    });

    test('la copia guardada los conserva', () {
      final cerrado = Viaje.fromJson({...delServidor, 'cargue_no_firmado_at': '2026-11-24T16:00:00Z'});
      final copia = Viaje.fromJson(cerrado.toJson());

      expect(copia.lugarCargueId, 'cedi');
      expect(copia.muelleId, 'm3');
      expect(copia.muelle, 'Muelle 3');
      expect(copia.cargueCerrado, isTrue);
      expect(copia.vehiculos.single.lugarDestino, 'Patio Guayabal');
      expect(copia.vehiculos.single.destinoCambiadoAt, DateTime.utc(2026, 11, 24, 15));
    });

    test('un viaje de antes se lee sin muelle ni destino', () {
      final viaje = Viaje.fromJson({'id': 'v1', 'vehiculos': [{'vin': 'VIN1'}]});

      expect(viaje.muelle, isNull);
      expect(viaje.cargueCerrado, isFalse);
      expect(viaje.vehiculos.single.destinoCambiadoAt, isNull);
    });
  });
  // S8-HU-10.2 CA-3, CA-6 y CA-9: el papel del inspector viene del servidor, se guarda en la copia y nunca se muestra
  // como código crudo.
  group('el papel del inspector', () {
    test('se lee y sobrevive a la copia sin señal', () {
      final viaje = Viaje.fromJson({'id': 'v1', 'papel': 'recibe', 'vehiculos': <Object>[]});
      final copia = Viaje.fromJson(viaje.toJson());

      expect(viaje.papel, PapelInspector.recibe);
      expect(copia.papel, PapelInspector.recibe);
    });

    test('un viaje de antes o del conductor no lo trae', () {
      expect(Viaje.fromJson({'id': 'v1', 'vehiculos': <Object>[]}).papel, isNull);
    });

    test('cada valor tiene su etiqueta y uno desconocido no tiene ninguna', () {
      expect(etiquetaPapel(PapelInspector.carga), 'Tú cargas');
      expect(etiquetaPapel(PapelInspector.recibe), 'Tú recibes');
      expect(etiquetaPapel(PapelInspector.cargaYRecibe), 'Tú cargas y recibes');
      expect(etiquetaPapel('supervisa'), isNull);
      expect(etiquetaPapel(null), isNull);
    });
  });

  // S8-HU-07.3 CA-6: si el lugar de cargue admite cargue sin inspector lo dice el viaje, y sin señal también.
  group('el lugar de cargue admite cargue sin inspector', () {
    test('se lee y sobrevive a la copia sin señal', () {
      final viaje = Viaje.fromJson({'id': 'v1', 'lugar_cargue_admite_sin_inspector': true, 'vehiculos': <Object>[]});

      expect(viaje.cargueAdmiteSinInspector, isTrue);
      expect(Viaje.fromJson(viaje.toJson()).cargueAdmiteSinInspector, isTrue);
    });

    test('un servidor anterior no lo manda: no lo admite, como todos los lugares de hoy', () {
      expect(Viaje.fromJson({'id': 'v1', 'vehiculos': <Object>[]}).cargueAdmiteSinInspector, isFalse);
    });
  });
}
