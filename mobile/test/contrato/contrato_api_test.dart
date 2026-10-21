import 'dart:convert';
import 'dart:io';

import 'package:flutter_test/flutter_test.dart';

/// Contrato con la API: los DTO de la app están escritos a mano (no hay generador de clientes Dart para este
/// OpenAPI), así que esta prueba los ata al contrato versionado del backend. Si la API cambia un campo que la app lee
/// o envía, falla aquí en lugar de fallar en campo.
///
/// Necesita ver backend/docs/openapi.json: se ejecuta desde la raíz del monorepo montada (ver docs/desarrollo.md).
/// Se puede indicar otra ruta con la variable OPENAPI_JSON.
File? _localizar() {
  final candidatos = [
    Platform.environment['OPENAPI_JSON'],
    '../backend/docs/openapi.json',
    'backend/docs/openapi.json',
  ].whereType<String>();
  for (final ruta in candidatos) {
    final archivo = File(ruta);
    if (archivo.existsSync()) return archivo;
  }
  return null;
}

void main() {
  final archivo = _localizar();
  if (archivo == null) {
    test('contrato OpenAPI', () {}, skip: 'No se encontró backend/docs/openapi.json (monta la raíz del monorepo).');
    return;
  }

  final documento = jsonDecode(archivo.readAsStringSync()) as Map<String, dynamic>;
  final esquemas = (documento['components'] as Map<String, dynamic>)['schemas'] as Map<String, dynamic>;
  final rutas = (documento['paths'] as Map<String, dynamic>).map((k, v) => MapEntry(k.replaceAll(RegExp(r'\{[^}]+\}'), '{}'), v));

  Set<String> campos(String esquema) {
    final definicion = esquemas[esquema];
    expect(definicion, isNotNull, reason: 'El esquema $esquema ya no existe en el contrato');
    return ((definicion as Map<String, dynamic>)['properties'] as Map<String, dynamic>).keys.toSet();
  }

  // Campos que la app LEE de cada respuesta.
  const lecturas = <String, List<String>>{
    'LoginResponse': ['token', 'usuario'],
    'UsuarioAutenticadoDto': ['id', 'nombre', 'email', 'rol'],
    'UsuarioDto': ['id', 'nombre', 'rol'],
    'InventarioDto': ['id', 'vin', 'marca', 'modelo', 'operacion', 'etapas'],
    'InventarioEtapaDto': ['id', 'orden', 'proceso', 'requiere_inspeccion', 'inspecciones'],
    'InventarioInspeccionDto': ['id', 'momento', 'fecha', 'estado', 'notas', 'hallazgos', 'evidencias', 'transferencia'],
    'TransferenciaDto': ['firmada'],
    'HallazgoDto': ['id', 'parte_vehiculo', 'clase_dano', 'tamano_dano', 'coords_diagrama'],
    'CoordsDiagramaDto': ['x', 'y'],
    'EvidenciaDto': ['id', 'url'],
    'InspeccionListadoDto': ['id', 'vin', 'momento', 'fecha', 'estado'],
    // Los catálogos maestros no son listas fijas en la app: se descargan y se guardan con su versión.
    'CatalogosDto': ['version', 'catalogos'],
    'InspeccionDto': ['id'],
    'ArchivoSubidoDto': ['storage_path'],
    'OrdenSofasaDto': [
      'id', 'vin', 'status_code', 'model_description', 'orden_number', 'orden_sap', 'origin_cedi', 'destination_city',
      'destination_address', 'loading_date', 'appointment', 'loading_dock', 'weight',
    ],
  };

  // Campos que la app ENVÍA en cada petición.
  const escrituras = <String, List<String>>{
    'LoginRequest': ['email', 'password'],
    'CrearInspeccionRequest': ['etapa_id', 'momento', 'fecha', 'lat', 'lng', 'conductor_nombre', 'placa', 'trailer', 'notas'],
    'CrearEvidenciaRequest': ['storage_path', 'sello_fecha', 'sello_lat', 'sello_lng'],
    'CrearHallazgoRequest': ['parte_vehiculo', 'clase_dano', 'tamano_dano', 'coords_diagrama'],
    'FirmarTransferenciaRequest': ['entrega_usuario_id', 'recibe_usuario_id', 'firma_entrega', 'firma_recibe'],
    'SubirArchivoRequest': ['data', 'ext'],
  };

  group('campos que la app lee', () {
    lecturas.forEach((esquema, esperados) {
      test(esquema, () => expect(campos(esquema), containsAll(esperados)));
    });
  });

  group('campos que la app envía', () {
    escrituras.forEach((esquema, esperados) {
      test(esquema, () => expect(campos(esquema), containsAll(esperados)));
    });
  });

  group('rutas y métodos que la app usa', () {
    const usos = <(String, String)>[
      ('post', '/api/auth/login'),
      ('get', '/api/usuarios'),
      ('get', '/api/catalogos'),
      ('get', '/api/vehiculos/{}/inventario'),
      ('get', '/api/inspecciones'),
      ('post', '/api/inspecciones'),
      ('post', '/api/inspecciones/{}/evidencias'),
      ('post', '/api/inspecciones/{}/hallazgos'),
      ('post', '/api/inspecciones/{}/cerrar'),
      ('post', '/api/inspecciones/{}/transferencia'),
      ('post', '/api/uploads'),
      ('get', '/api/sofasa/ordenes'),
    ];
    for (final (metodo, ruta) in usos) {
      test('${metodo.toUpperCase()} $ruta', () {
        final operaciones = rutas[ruta] as Map<String, dynamic>?;
        expect(operaciones, isNotNull, reason: 'La ruta $ruta ya no existe en el contrato');
        expect(operaciones!.containsKey(metodo), isTrue, reason: 'Ya no existe $metodo en $ruta');
      });
    }

    test('el listado de inspecciones admite filtrar por usuarioId', () {
      final parametros = ((rutas['/api/inspecciones'] as Map<String, dynamic>)['get'] as Map<String, dynamic>)['parameters'] as List<dynamic>;
      expect(parametros.map((p) => (p as Map<String, dynamic>)['name']), contains('usuarioId'));
    });
  });
}
