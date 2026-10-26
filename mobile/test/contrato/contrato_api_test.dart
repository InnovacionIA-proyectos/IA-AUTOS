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
    'UsuarioAutenticadoDto': ['id', 'nombre', 'email', 'rol', 'cedula', 'debe_cambiar_pin'],
    // De `/me` la app solo lee las capacidades: de ahí sale el menú.
    'PerfilDto': ['capacidades'],
    'UsuarioDto': ['id', 'nombre', 'rol'],
    'InventarioDto': ['id', 'vin', 'marca', 'modelo', 'operacion', 'etapas'],
    // `lugar` se pinta en la tarjeta de la etapa y `viaje_id` es lo que lleva a la captura los datos del
    // transporte: sin él la captura no tiene de dónde sacar la placa ni el conductor.
    'InventarioEtapaDto': [
      'id', 'orden', 'proceso', 'lugar', 'requiere_inspeccion', 'viaje_id', 'con_personal_apoyo', 'inspecciones',
    ],
    'ViajeDto': [
      'id', 'codigo_mtrack', 'placa', 'estado', 'unidad', 'trailer', 'conductor_nombre', 'inspector_nombre',
      'origen', 'destino', 'vehiculos',
    ],
    'ViajeVehiculoDto': ['vin', 'marca', 'modelo', 'color', 'operacion'],
    // `usuario_id` distingue el informe propio del ajeno: es lo que decide si al conductor se le muestra el
    // detalle de lo que marcó el inspector.
    'InventarioInspeccionDto': [
      'id', 'momento', 'momento_nombre', 'fecha', 'cerrada_at', 'estado', 'notas', 'hallazgos', 'evidencias',
      'transferencia', 'usuario_id',
    ],
    // Las dos fechas de firma son lo que permite decir quién firmó y quién falta: entre una y otra pueden
    // pasar horas, porque cada parte firma desde su sesión.
    'TransferenciaDto': [
      'firmada', 'tipo_cierre', 'entrega_usuario_id', 'recibe_usuario_id', 'entrega_firmada_at', 'recibe_firmada_at',
    ],
    // Del detalle del informe salen además los nombres, para no mostrar un id a quien va a firmar.
    'TransferenciaDetalleDto': ['entrega_nombre', 'recibe_nombre'],
    'InformeDetalleDto': ['transferencia'],
    // Quiénes pueden firmar este viaje: su conductor y su inspector, y nadie más.
    'FirmanteDto': ['id', 'nombre', 'rol'],
    'HallazgoDto': ['id', 'parte_vehiculo', 'clase_dano', 'tamano_dano', 'coords_diagrama'],
    'CoordsDiagramaDto': ['x', 'y'],
    'EvidenciaDto': ['id', 'url'],
    'InspeccionListadoDto': ['id', 'vin', 'momento', 'momento_nombre', 'fecha', 'estado'],
    // Los catálogos maestros no son listas fijas en la app: se descargan y se guardan con su versión.
    'CatalogosDto': ['version', 'catalogos'],
    'InspeccionDto': ['id'],
    'ArchivoSubidoDto': ['storage_path', 'hash_sha256'],
    'OrdenSofasaDto': [
      'id', 'vin', 'status_code', 'model_description', 'orden_number', 'orden_sap', 'origin_cedi', 'destination_city',
      'destination_address', 'loading_date', 'appointment', 'loading_dock', 'weight',
    ],
  };

  // Campos que la app ENVÍA en cada petición.
  const escrituras = <String, List<String>>{
    'LoginRequest': ['email', 'password'],
    'LoginConductorRequest': ['cedula', 'pin'],
    'CambiarPinRequest': ['pin_actual', 'pin_nuevo'],
    'CrearInspeccionRequest': ['etapa_id', 'momento', 'fecha', 'lat', 'lng', 'notas'],
    // Las diez claves van siempre, aunque algunas puedan ir nulas: la API las exige presentes.
    'CrearEvidenciaRequest': [
      'hallazgo_id', 'vista_id', 'storage_path', 'sello_fecha', 'sello_lat', 'sello_lng',
      'precision', 'dispositivo_id', 'nota', 'hash_sha256',
    ],
    // La evidencia va DENTRO: el servidor no acepta un hallazgo sin su foto.
    'CrearHallazgoRequest': ['parte_vehiculo', 'clase_dano', 'tamano_dano', 'coords_diagrama', 'evidencia'],
    // Una sola firma: el servidor la atribuye al usuario del token.
    'FirmarTransferenciaRequest': ['entrega_usuario_id', 'recibe_usuario_id', 'firma'],
    // Quien recibe firma dejando sus observaciones: cierra la transferencia como `con_reservas`.
    'RecibirConReservasRequest': ['entrega_usuario_id', 'recibe_usuario_id', 'firma', 'reservas'],
    // La constancia de no firma, con su evidencia dentro, igual que un hallazgo.
    'RegistrarNoFirmaRequest': [
      'entrega_usuario_id', 'recibe_usuario_id', 'parte', 'motivo', 'testigo_usuario_id', 'evidencia',
    ],
    // La app sube multipart, no base64: `SubirArchivoRequest` ya no se usa.
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
      ('post', '/api/auth/login-conductor'),
      ('post', '/api/auth/cambiar-pin'),
      ('get', '/api/auth/me'),
      ('get', '/api/usuarios'),
      ('get', '/api/catalogos'),
      ('get', '/api/viajes/mios'),
      ('get', '/api/viajes/{}'),
      ('get', '/api/vehiculos/{}/inventario'),
      ('get', '/api/viajes/{}/firmantes'),
      ('get', '/api/inspecciones'),
      ('post', '/api/inspecciones'),
      ('get', '/api/inspecciones/{}'),
      ('post', '/api/inspecciones/{}/evidencias'),
      ('post', '/api/inspecciones/{}/hallazgos'),
      ('post', '/api/inspecciones/{}/cerrar'),
      ('post', '/api/inspecciones/{}/transferencia'),
      ('post', '/api/inspecciones/{}/transferencia/reservas'),
      ('post', '/api/inspecciones/{}/transferencia/no-firma'),
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

    test('la subida de archivos acepta multipart', () {
      final contenido = ((rutas['/api/uploads'] as Map<String, dynamic>)['post'] as Map<String, dynamic>)['requestBody'] as Map<String, dynamic>;
      expect((contenido['content'] as Map<String, dynamic>).keys, contains('multipart/form-data'));
    });

    test('el listado de inspecciones admite filtrar por usuario_id', () {
      final parametros = ((rutas['/api/inspecciones'] as Map<String, dynamic>)['get'] as Map<String, dynamic>)['parameters'] as List<dynamic>;
      expect(parametros.map((p) => (p as Map<String, dynamic>)['name']), contains('usuario_id'));
    });
  });
}
