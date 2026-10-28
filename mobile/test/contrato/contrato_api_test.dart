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
      // Con el inspector del viaje se sabe a quién se le ofrece marcar que una unidad no sale.
      'inspector_id',
    ],
    // Una unidad puede dejar de salir del viaje sin bloquearlo.
    'ViajeVehiculoDto': ['vin', 'marca', 'modelo', 'color', 'operacion', 'estado_unidad', 'no_sale'],
    'NoSaleDto': ['motivo', 'hallazgo_id', 'marcado_por_nombre', 'fecha', 'vin_reemplazo'],
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
    // La zona del hallazgo es cuadrante + lado; `zona` es el texto listo para mostrar. `parte_vehiculo` solo se lee
    // como respaldo en los hallazgos históricos sin ubicar.
    'HallazgoDto': [
      'id', 'parte_vehiculo', 'clase_dano', 'tamano_dano', 'cuadrante_numero', 'lado', 'zona', 'nivel_severidad',
      // De dónde salió y si ya venía de antes: los dos se muestran en la ficha del daño.
      'origen', 'es_preexistente',
    ],
    // La novedad del conductor pendiente de revisión, tal como la lista `GET /api/hallazgos`: lleva dónde se
    // registró y su foto con el sello, que es con lo que se juzga sin tener el vehículo delante.
    'HallazgoListadoDto': ['vin', 'viaje_id', 'proceso', 'momento', 'evidencia', 'tipo_novedad'],
    // El sello de la foto de la novedad: cuándo y dónde se tomó.
    'EvidenciaDto': ['id', 'url', 'hallazgo_id', 'sello_fecha', 'sello_lat', 'sello_lng', 'sin_ubicacion'],

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
    // La zona sale del dibujo de daños: cuadrante + lado. El nivel lo asigna el inspector (el conductor lo manda nulo).
    'CrearHallazgoRequest': [
      'cuadrante_numero', 'lado', 'clase_dano', 'tamano_dano', 'nivel_severidad', 'es_preexistente',
      'hallazgo_origen_id', 'evidencia',
    ],
    // Una sola firma: el servidor la atribuye al usuario del token.
    'FirmarTransferenciaRequest': ['entrega_usuario_id', 'recibe_usuario_id', 'firma'],
    // Revisar la novedad del conductor: se la ubica, se la clasifica y se le pone el nivel, y el tamaño que
    // eligió el conductor se puede corregir.
    'RevisarHallazgoRequest': [
      'cuadrante_numero', 'lado', 'zona_provisional', 'clase_dano', 'nivel_severidad', 'tamano_dano', 'nota',
      'es_preexistente', 'hallazgo_origen_id',
    ],
    // La unidad que no sale del viaje: su motivo, el hallazgo V1+ que lo causa y el VIN que la reemplaza.
    'MarcarNoSaleRequest': ['motivo', 'hallazgo_id', 'vin_reemplazo'],
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
      // La imagen de fondo de cada panel del dibujo de daños.
      ('get', '/api/catalogos/paneles-dano/{}/imagen'),
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
      // Las novedades del conductor pendientes de revisión, y la revisión que las completa.
      ('get', '/api/hallazgos'),
      ('post', '/api/hallazgos/{}/revision'),
      // La unidad que no sale del viaje.
      ('post', '/api/viajes/{}/vehiculos/{}/no-sale'),
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
