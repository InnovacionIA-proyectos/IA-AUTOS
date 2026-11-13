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
      // `con_personal_apoyo` ya no viene: desde el contrato 3.0.0 la salida del cargue la hace siempre un
      // inspector, y el conductor solo por la excepción «No hay inspector». La app lo soporta con y sin la
      // marca, porque un celular puede quedar contra una API vieja.
      'id', 'orden', 'proceso', 'lugar', 'requiere_inspeccion', 'viaje_id', 'inspecciones',
    ],
    'ViajeDto': [
      'id', 'codigo_mtrack', 'placa', 'estado', 'unidad', 'trailer', 'conductor_nombre', 'inspector_nombre',
      'origen', 'destino', 'vehiculos',
      // Con el inspector del viaje se sabe a quién se le ofrece marcar que una unidad no sale.
      'inspector_id',
      // Si el punto de cargue tiene inspectores: con eso el botón de la salida pide permiso en vez de activar
      // la excepción, que allí el servidor rechaza.
      'lugar_cargue_tiene_inspector',
    ],
    // Quién recibe en un punto lo decide el servidor por punto, no por viaje.
    'EntregaPuntoDto': ['viaje_id', 'lugar_id', 'lugar', 'unidades', 'unidades_pendientes', 'entregada', 'lugar_tiene_inspector'],
    // El permiso de la excepción: su estado, hasta cuándo corre el plazo y con qué motivo se pidió, que es el
    // que se reusa al seguir con la captura.
    'PermisoSinInspectorDto': [
      'id', 'viaje_id', 'estado', 'solicitado_at', 'vence_at', 'lugar', 'motivo_codigo', 'motivo', 'motivo_texto',
      'fecha', 'lat', 'lng', 'concedido_at', 'concedido_por_nombre', 'autorizado_por_tiempo',
    ],
    // La nota se lee bajo el registro que aclara: sin su autor y su fecha no aclara nada.
    'NotaAclaratoriaDto': ['id', 'texto', 'autor_nombre', 'autor_rol', 'created_at'],
    // Una unidad puede dejar de salir del viaje sin bloquearlo.
    // `estado_transporte` y `novedades` son lo que el conductor ve de cada unidad en su viaje.
    'ViajeVehiculoDto': [
      'vin', 'marca', 'modelo', 'color', 'operacion', 'estado_unidad', 'no_sale', 'estado_transporte', 'novedades',
    ],
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

    // La fila del listado. `proceso`, `codigo_mtrack` y `lugar` son lo que distingue una fila de la siguiente
    // cuando se ven los informes de todo el mundo y no solo los propios (#394).
    'InspeccionListadoDto': [
      'id', 'vin', 'momento', 'momento_nombre', 'fecha', 'estado', 'version', 'corrige_inspeccion_id',
      'proceso', 'codigo_mtrack', 'lugar',
    ],
    // Los catálogos maestros no son listas fijas en la app: se descargan y se guardan con su versión.
    'CatalogosDto': ['version', 'catalogos'],
    'InspeccionDto': ['id'],
    'ArchivoSubidoDto': ['storage_path', 'hash_sha256'],
    'OrdenSofasaDto': [
      'id', 'vin', 'status_code', 'model_description', 'orden_number', 'orden_sap', 'origin_cedi', 'destination_city',
      'destination_address', 'loading_date', 'appointment', 'loading_dock', 'weight',
    ],

    // ---------------------------------------------------------------- la entrega por punto (S6-HU-11, S7-HU-02)
    // El sobre: la app solo saca los puntos. El resto del viaje ya lo tiene de otra parte.
    'EntregasDelViajeDto': ['puntos'],
    // Cada unidad del punto: su estado decide si falta descargarla, y las dos marcas de firma, quién ya firmó.
    // `recibe_externo_nombre` es lo que se enseña de quien recibió sin ser usuario; la cédula no se muestra.
    'EntregaUnidadDto': [
      'vin', 'estado', 'tipo_cierre', 'entrega_firmada_at', 'recibe_firmada_at', 'recibe_externo_nombre',
    ],

    // ---------------------------------------------------------------- los extras del vehículo (S6-HU-16)
    // `tipo_nombre` es el respaldo cuando el extra no trae nombre propio: un extra raro no puede dejar la ficha
    // con un renglón en blanco.
    'ExtraDto': ['id', 'nombre', 'tipo_nombre'],

    // ---------------------------------------------------------------- la acomodación en la niñera (S7-HU-05)
    // Los puestos los manda el servidor: dibujarlos con un esquema fijo pintaba siete donde había nueve.
    'DisposicionDeNiniera': ['puestos'],
    'PuestoDeNiniera': ['numero', 'etiqueta'],

    // ---------------------------------------------------------------- los avisos (S6-HU-02)
    // `no_leidas` es el número de la pestaña; sin él habría que contar la página que se trajo, que no es lo mismo.
    'ListadoNotificacionesDto': ['items', 'total', 'no_leidas'],
    'NotificacionDto': [
      'id', 'tipo', 'urgencia', 'titulo', 'mensaje', 'viaje_id', 'codigo_mtrack', 'vin', 'leida_at', 'respuesta',
      'created_at',
    ],
    'RespuestaNotificacionDto': ['texto', 'medio', 'usuario_nombre', 'fecha'],

    // ---------------------------------------------------------------- la historia de daños del VIN (S7-HU-14)
    // La foto sale de la primera evidencia, con su vencimiento: la URL va firmada y caduca.
    'HallazgoPrevioDto': [
      'id', 'zona', 'proceso', 'momento', 'cuadrante_numero', 'lado', 'clase_dano', 'tamano_dano',
      'nivel_severidad', 'codigo_mtrack', 'cerrada_at', 'evidencias',
    ],

    // ---------------------------------------------------------------- reclamar un camión (S6-HU-12)
    'PendienteDto': [
      'viaje_id', 'codigo_mtrack', 'placa', 'proceso', 'lugar', 'fecha_programada', 'estado',
      'reclamado_por_nombre', 'unidades',
    ],

    // ---------------------------------------------------------------- la ubicación de un lugar (S6-HU-17)
    // Del catálogo `lugares` la app saca el estado de ubicación de cada uno; de la respuesta al fijarla, el nuevo.
    'CatalogoDto': ['items'],
    'LugarUbicacionDto': ['estado_ubicacion'],

    // ---------------------------------------------------------------- buscar un vehículo (S7-HU-09)
    'VehiculoBusquedaDto': ['vin', 'marca', 'modelo', 'color', 'activo'],

    // ---------------------------------------------------------------- lo que pasó alrededor del informe
    // El motivo ya resuelto a su nombre: la app no traduce códigos que el servidor ya tradujo.
    'NovedadProcesoDto': ['tipo', 'motivo', 'texto', 'usuario_nombre', 'fecha'],

    // ---------------------------------------------------------------- las respuestas paginadas (S7-HU-13)
    // La app las lee por `items`; el resto de la envoltura lo usa quien pagina de verdad.
    'PaginaDtoOfInspeccionListadoDto': ['items', 'total', 'pagina', 'tamano'],
    'ListadoHallazgosDto': ['items'],
    'ListadoUsuariosDto': ['items'],
    'PaginaDtoOfOrdenSofasaDto': ['items'],

    // Lo que NO está aquí, y por qué: `HallazgoCreadoDto`, `CerrarInspeccionResponse`, `LlegadaDto` y
    // `ReclamoDto` son respuestas que la app recibe y **no lee** —envía y sigue—, así que atarlas sería fijar
    // algo que no usa. La imagen de un panel (`FileContentResult`) no es JSON: se pide por su URL.
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
    // Pedir la excepción donde el punto tiene inspectores: el motivo, y el momento y el lugar en que se pidió,
    // que son los que después documentan la novedad.
    'SolicitarPermisoSinInspectorRequest': [
      'motivo_sin_inspector', 'motivo_sin_inspector_texto', 'fecha', 'lat', 'lng',
    ],
    // Revisar la novedad del conductor: se la ubica, se la clasifica y se le pone el nivel, y el tamaño que
    // eligió el conductor se puede corregir.
    'RevisarHallazgoRequest': [
      'cuadrante_numero', 'lado', 'clase_dano', 'nivel_severidad', 'tamano_dano', 'nota',
      'es_preexistente', 'hallazgo_origen_id',
    ],
    // La unidad que no sale del viaje: su motivo, el hallazgo V1+ que lo causa y el VIN que la reemplaza.
    'MarcarNoSaleRequest': ['motivo', 'hallazgo_id', 'vin_reemplazo'],
    // Aclarar un registro que ya no se puede cambiar: solo el texto.
    'AgregarNotaRequest': ['texto'],
    // Quien recibe firma dejando sus observaciones: cierra la transferencia como `con_reservas`.
    'RecibirConReservasRequest': ['entrega_usuario_id', 'recibe_usuario_id', 'firma', 'reservas'],
    // La constancia de no firma, con su evidencia dentro, igual que un hallazgo.
    'RegistrarNoFirmaRequest': [
      'entrega_usuario_id', 'recibe_usuario_id', 'parte', 'motivo', 'testigo_usuario_id', 'evidencia',
    ],
    // La novedad del conductor en ruta: sin texto libre ni zona, clase o nivel, y la evidencia va dentro, sin `nota`.
    'RegistrarNovedadRequest': ['fecha', 'lat', 'lng', 'tipo_novedad', 'tamano_dano', 'evidencia'],
    // «Llegué»: la fecha y el lugar del momento.
    'RegistrarLlegadaRequest': ['fecha', 'lat', 'lng'],
    // Corregir un informe cerrado: nace uno nuevo con la fecha y el lugar de la corrección (S7-HU-18).
    'CorregirInspeccionRequest': ['fecha', 'lat', 'lng', 'notas'],
    // El extra del vehículo. Su daño no son columnas del extra: van `danado` y los tres campos del hallazgo,
    // que es como el servidor lo guarda (S7-HU-04).
    'CrearExtraRequest': ['tipo', 'texto', 'observacion', 'danado', 'clase_dano', 'tamano_dano', 'nivel_severidad'],
    // El recibo sin entrega: la salida que hizo el conductor sin inspector la firma solo quien recibe.
    'ReciboSinEntregaRequest': ['firma'],
    // La entrega de un punto. `recibe_nombre`, `recibe_cedula` y `firma_recibe` solo van cuando recibe alguien
    // que no es usuario de la app; el servidor los rechaza donde el punto tiene inspector (S7-HU-02).
    'FirmarEntregaRequest': ['firma', 'recibe_nombre', 'recibe_cedula', 'firma_recibe'],
    // Cerrar el punto con una sola firma, diciendo por qué falta la otra parte.
    'CerrarEntregaUnaFirmaRequest': ['motivo', 'firma'],
    // El puesto de una unidad en la niñera. Va presente aunque sea nulo: nulo es lo que libera el puesto.
    'AsignarPosicionRequest': ['posicion'],
    // Reclamar el camión de un proceso.
    'ReclamarViajeRequest': ['proceso'],
    // «Fijar ubicación aquí»: deja la posición del teléfono como la del lugar.
    'FijarUbicacionRequest': ['lat', 'lng', 'precision'],
    // Responder un aviso desde la app.
    'ResponderNotificacionRequest': ['texto', 'medio'],
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
      ('post', '/api/viajes/{}/vehiculos/{}/novedades'),
      ('post', '/api/viajes/{}/vehiculos/{}/llegada'),
      ('post', '/api/inspecciones/{}/transferencia'),
      ('post', '/api/inspecciones/{}/transferencia/reservas'),
      ('post', '/api/inspecciones/{}/transferencia/no-firma'),
      ('post', '/api/uploads'),
      // Las novedades del conductor pendientes de revisión, y la revisión que las completa.
      ('get', '/api/hallazgos'),
      ('post', '/api/hallazgos/{}/revision'),
      // La unidad que no sale del viaje.
      ('post', '/api/viajes/{}/vehiculos/{}/no-sale'),
      // El permiso de la excepción «No hay inspector» donde el punto de cargue sí tiene inspectores. Conceder no
      // está: eso se hace desde el portal, y la app solo pide y espera.
      ('post', '/api/viajes/{}/permiso-sin-inspector'),
      ('get', '/api/viajes/{}/permiso-sin-inspector'),
      // Las notas aclaratorias, una ruta por registro.
      ('post', '/api/inspecciones/{}/notas'),
      ('post', '/api/hallazgos/{}/notas'),
      ('post', '/api/inspecciones/{}/transferencia/notas'),
      ('get', '/api/sofasa/ordenes'),
      // El catálogo se pide por su nombre: la app lee `lugares` para saber cuáles tienen ubicación.
      ('get', '/api/catalogos/{}'),
      ('post', '/api/lugares/{}/ubicacion'),
      // Corregir un informe cerrado, y los extras del vehículo con su daño.
      ('post', '/api/inspecciones/{}/corregir'),
      ('post', '/api/inspecciones/{}/extras'),
      // El recibo sin entrega: la salida sin inspector la firma solo quien recibe.
      ('post', '/api/inspecciones/{}/transferencia/sin-entrega'),
      // Los avisos del inspector y del coordinador.
      ('get', '/api/notificaciones'),
      ('post', '/api/notificaciones/{}/leida'),
      ('post', '/api/notificaciones/{}/respuesta'),
      // Buscar un vehículo por VIN, y su historia completa de daños.
      ('get', '/api/vehiculos'),
      ('get', '/api/vehiculos/{}/hallazgos-previos'),
      // Reclamar un camión pendiente y soltarlo.
      ('get', '/api/viajes/pendientes'),
      ('post', '/api/viajes/{}/reclamos'),
      ('delete', '/api/viajes/{}/reclamos/{}'),
      // La entrega por punto: lo que hay que entregar y las dos maneras de firmarlo.
      ('get', '/api/viajes/{}/entregas'),
      ('post', '/api/viajes/{}/entregas/{}/firma'),
      ('post', '/api/viajes/{}/entregas/{}/una-firma'),
      // El puesto de una unidad en la niñera. Es PUT, no POST: la clave va presente aunque sea nula, y mandarla
      // nula es lo que libera el puesto.
      ('put', '/api/viajes/{}/vehiculos/{}/posicion'),
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
