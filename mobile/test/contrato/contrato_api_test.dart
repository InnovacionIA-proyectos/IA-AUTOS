import 'dart:convert';
import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:traza_autos/features/avisos/domain/aviso.dart';
import 'package:traza_autos/features/catalogos/domain/catalogos.dart';
import 'package:traza_autos/features/sync/domain/procesador_cola.dart';
import 'package:traza_autos/features/viajes/domain/viaje.dart';

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
      // S8-HU-01.3 CA-9: el puesto del carro en la niñera, que el cargue cerrado muestra.
      'posicion',
    ],
    'ViajeDto': [
      'id', 'codigo_mtrack', 'placa', 'estado', 'unidad', 'trailer', 'conductor_nombre', 'inspector_nombre',
      'origen', 'destino', 'vehiculos',
      // Con el inspector del viaje se sabe a quién se le ofrece marcar que una unidad no sale.
      'inspector_id',
      // Si el punto de cargue tiene inspectores: con eso el botón de la salida pide permiso en vez de activar
      // la excepción, que allí el servidor rechaza.
      'lugar_cargue_tiene_inspector',
      // S8-HU-03.2: el muelle, el lugar de donde salen los muelles y si la entrega del cargue ya se cerró.
      'lugar_cargue_id', 'muelle_id', 'muelle', 'cargue_firmado_por_ninera_at', 'cargue_no_firmado_at',
      // S8-HU-01.3: el conductor del viaje es quien recibe en la firma de la niñera.
      'conductor_id',
      // S8-HU-10.2 CA-3: si el inspector que consulta carga, recibe o las dos cosas.
      'papel',
      // S8-HU-07.3 CA-6: «No hay inspector» y el permiso solo donde el lugar de cargue admite cargue sin inspector.
      'lugar_cargue_admite_sin_inspector',
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
      // A dónde va cada carro y cuándo cambió por última vez, para resaltarlo al conductor.
      'lugar_destino', 'lugar_destino_cambiado_at',
      // S8-HU-04.2: la parada del carro, con la que se juntan los carros para «Llegué a esta parada».
      'lugar_destino_id',
      // S8-HU-07.3 CA-5 (decisión 10): si ya se puede registrar la ruta del carro y, si no, por qué.
      'puede_registrar_ruta', 'motivo_sin_ruta',
    ],
    // S8-HU-02.2: el motivo es de catálogo; `motivo` es su etiqueta para quien pregunta.
    'NoSaleDto': ['motivo_codigo', 'motivo', 'motivo_texto', 'hallazgo_id', 'marcado_por_nombre', 'fecha', 'vin_reemplazo'],
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
    // S8-HU-04.2 CA-4: de la revisión de llegada salen si está cerrada y qué daños tienen foto para rechazar el carro.
    'InformeDetalleDto': ['transferencia', 'estado', 'hallazgos', 'evidencias'],
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
    // `vista_id`: al continuar un borrador (S8-HU-11.2 CA-2), la vista que ya está en el servidor no se vuelve a pedir.
    'EvidenciaDto': ['id', 'url', 'hallazgo_id', 'sello_fecha', 'sello_lat', 'sello_lng', 'sin_ubicacion', 'vista_id'],

    // La fila del listado. `proceso`, `codigo_mtrack` y `lugar` son lo que distingue una fila de la siguiente
    // cuando se ven los informes de todo el mundo y no solo los propios (#394).
    'InspeccionListadoDto': [
      'id', 'vin', 'momento', 'momento_nombre', 'fecha', 'estado', 'version', 'corrige_inspeccion_id',
      'proceso', 'codigo_mtrack', 'lugar',
      // S8-HU-10.2 CA-5: quién hizo el informe.
      'usuario_nombre',
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
    // El sobre: las paradas y cuántos carros no tienen destino (S8-HU-04.2 CA-8).
    'EntregasDelViajeDto': ['puntos', 'unidades_sin_destino'],
    // Cada unidad del punto: su estado decide si falta descargarla, y las dos marcas de firma, quién ya firmó.
    // `recibe_externo_nombre` es lo que se enseña de quien recibió sin ser usuario; la cédula no se muestra.
    // `inspeccion_id` es su revisión de llegada, de donde salen los daños para rechazarlo.
    'EntregaUnidadDto': [
      'vin', 'estado', 'tipo_cierre', 'entrega_firmada_at', 'recibe_firmada_at', 'recibe_externo_nombre', 'inspeccion_id',
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
      // S8-HU-07.3 CA-9: lo propio de cada tipo; del «Cargue sin inspector», los VIN del viaje.
      // S8-HU-09.2 CA-6: el hallazgo al que lleva y los datos propios del aviso (el lugar, la distancia...).
      'hallazgo_id', 'datos',
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
      // S8-HU-04.2 CA-10: la entrega se toma y se libera por parada.
      'lugar_id',
    ],

    // ---------------------------------------------------------------- la ubicación de un lugar (S6-HU-17)
    // Del catálogo `lugares` la app saca el estado de ubicación de cada uno; de la respuesta al fijarla, el nuevo.
    'CatalogoDto': ['items'],
    'LugarUbicacionDto': ['estado_ubicacion'],

    // ---------------------------------------------------------------- los parámetros que ajusta TI (S8-HU-09.2)
    // CA-1: lo que la captura exige y avisa, leído del servidor en vez de fijado al compilar.
    'ParametrosDeLaAppDto': ['acomodacion_fotos_minimas', 'gps_precision_minima_m'],

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

    // Lo que NO está aquí, y por qué: `HallazgoCreadoDto`, `CerrarInspeccionResponse`, `LlegadaDto`,
    // `ReclamoDto` y `NovedadViajeDto` son respuestas que la app recibe y **no lee** —envía y sigue—, así que atarlas
    // sería fijar algo que no usa (listar las novedades del viaje en la app no es de S8-HU-07.3). La imagen de un
    // panel (`FileContentResult`) no es JSON: se pide por su URL.
  };

  // Campos que la app ENVÍA en cada petición.
  const escrituras = <String, List<String>>{
    'LoginRequest': ['email', 'password'],
    'LoginConductorRequest': ['cedula', 'pin'],
    'CambiarPinRequest': ['pin_actual', 'pin_nuevo'],
    // S8-HU-03.2: lo que el inspector cambia del viaje durante el cargue.
    'CambiarCargueDelViajeRequest': ['muelle_id', 'trailer'],
    // S8-HU-01.3: la firma de toda la niñera, con el momento y el GPS, y su constancia de no firma.
    'FirmarCargueRequest': ['firma', 'parte', 'fecha_momento', 'lat', 'lng', 'precision_m', 'reservas'],
    'NoFirmaDelCargueRequest': ['parte', 'motivo', 'testigo_usuario_id', 'evidencia', 'fecha_momento', 'lat', 'lng', 'precision_m'],
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
    'MarcarNoSaleRequest': ['motivo_codigo', 'motivo_texto', 'hallazgo_id', 'vin_reemplazo'],
    // Aclarar un registro que ya no se puede cambiar: solo el texto.
    'AgregarNotaRequest': ['texto'],
    // Quien recibe firma dejando sus observaciones: cierra la transferencia como `con_reservas`.
    'RecibirConReservasRequest': ['entrega_usuario_id', 'recibe_usuario_id', 'firma', 'reservas'],
    // La constancia de no firma, con su evidencia dentro, igual que un hallazgo.
    'RegistrarNoFirmaRequest': [
      'entrega_usuario_id', 'recibe_usuario_id', 'parte', 'motivo', 'testigo_usuario_id', 'evidencia',
    ],
    // La novedad del conductor en ruta: sin texto libre ni zona, clase o nivel, y la evidencia va dentro, sin `nota`.
    // S8-HU-07.3 CA-4 (hueco 6): con `id_envio` el reenvío de la cola no la duplica.
    'RegistrarNovedadRequest': ['fecha', 'lat', 'lng', 'tipo_novedad', 'tamano_dano', 'evidencia', 'id_envio'],
    // S8-HU-07.3 CA-3: la novedad del viaje, con la hora y el GPS del momento, la foto opcional, el id del envío y,
    // del inspector, el proceso de su reclamo y la parada de la entrega.
    'RegistrarNovedadViajeRequest': ['tipo', 'texto', 'fecha', 'lat', 'lng', 'evidencia', 'id_envio', 'proceso', 'lugar_id'],
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
    // Cerrar la parada con una sola firma. El motivo codificado, la evidencia y el momento llegan con el PR #456: se
    // atan abajo, en «la entrega por parada».
    'CerrarEntregaUnaFirmaRequest': ['firma'],
    // El puesto de una unidad en la niñera. Va presente aunque sea nulo: nulo es lo que libera el puesto.
    'AsignarPosicionRequest': ['posicion'],
    // Reclamar el camión de un proceso. La entrega, con el GPS del momento (S8-HU-09.2 CA-4).
    'ReclamarViajeRequest': ['proceso', 'lat', 'lng', 'precision'],
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
      ('post', '/api/viajes/{}/vehiculos/{}/vuelve-a-salir'),
      // El muelle y el tráiler que el inspector cambia durante el cargue.
      ('patch', '/api/viajes/{}/cargue'),
      ('post', '/api/viajes/{}/cargue/firma'),
      ('post', '/api/viajes/{}/cargue/no-firma'),
      // El informe de la niñera en PDF (S8-HU-01.3 CA-10).
      ('get', '/api/viajes/{}/ninera/pdf'),
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
      // Una parada sola: lo que abre «Entregar en esta parada» (S8-HU-04.2).
      ('get', '/api/viajes/{}/entregas/{}'),
      ('post', '/api/viajes/{}/entregas/{}/firma'),
      ('post', '/api/viajes/{}/entregas/{}/una-firma'),
      // El puesto de una unidad en la niñera. Es PUT, no POST: la clave va presente aunque sea nula, y mandarla
      // nula es lo que libera el puesto.
      ('put', '/api/viajes/{}/vehiculos/{}/posicion'),
      // S8-HU-11.2 CA-3: descartar un informe en borrador.
      ('post', '/api/inspecciones/{}/descartar'),
      // S8-HU-07.3 CA-3: la novedad del viaje.
      ('post', '/api/viajes/{}/novedades-viaje'),
      // S8-HU-09.2 CA-1: los parámetros que TI ajusta y la app lee al iniciar sesión, al volver la señal y al capturar.
      ('get', '/api/parametros/app'),
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

    // S8-HU-08.2 CA-7: llegan con el PR #457 (#430). Se activan al quitar el `skip` cuando entre a QA.
    test('el listado de hallazgos admite filtrar por sin_calificar', () {
      final parametros = ((rutas['/api/hallazgos'] as Map<String, dynamic>)['get'] as Map<String, dynamic>)['parameters'] as List<dynamic>;
      expect(parametros.map((p) => (p as Map<String, dynamic>)['name']), contains('sin_calificar'));
    });
    test('el hallazgo dice si ya se calificó y de qué extra es', () {
      expect(campos('HallazgoDto'), containsAll(['calificado', 'extra_id']));
    });
  });

  // S8-HU-04.2: la entrega por parada. Lo de aquí llega con el PR #456 (S8-HU-04.1, también dentro del #457); se
  // activa al quitar el `skip` cuando entre a QA.
  group('la entrega por parada', () {

    // CA-2, CA-3 y CA-8: cómo va la parada, quién recibe y si el conductor ya marcó «Llegué».
    test('la parada dice su estado, quién recibe y si el conductor llegó', () {
      expect(campos('EntregaPuntoDto'), containsAll(['estado_parada', 'receptor', 'receptor_nombre', 'conductor_llego']));
    });

    test('cada carro trae sus reservas, su motivo de cierre y su rechazo', () {
      expect(campos('EntregaUnidadDto'), containsAll(['reservas', 'motivo_codigo', 'rechazo']));
      expect(campos('RechazoDeEntregaDto'), containsAll(['hallazgo_id', 'marcado_por_nombre']));
    });

    // CA-4, CA-5 y CA-7: la firma lleva los rechazos, las reservas y el momento; ya no lleva quién entrega ni quién
    // recibe (lo decide la parada) ni el motivo parcial: el servidor los rechaza con «Campo no admitido».
    test('la firma de la parada lleva rechazos, reservas, hora y GPS, y nada de quién firma', () {
      final firma = campos('FirmarEntregaRequest');
      expect(firma, containsAll(['firma', 'reservas', 'rechazos', 'fecha_momento', 'lat', 'lng', 'precision_m']));
      expect(firma, containsAll(['recibe_nombre', 'recibe_cedula', 'firma_recibe']));
      expect(firma.intersection({'entrega_usuario_id', 'recibe_usuario_id', 'motivo_parcial'}), isEmpty);
    });

    // CA-6 y CA-7: el cierre con una sola firma, con su motivo codificado y su evidencia.
    test('el cierre con una sola firma lleva motivo codificado, fotos, testigo y momento', () {
      final cierre = campos('CerrarEntregaUnaFirmaRequest');
      expect(
        cierre,
        containsAll([
          'firma', 'motivo_codigo', 'motivo_texto', 'fotos', 'testigo_nombre', 'testigo_cedula', 'fecha_momento', 'lat',
          'lng', 'precision_m',
        ]),
      );
      expect(cierre, isNot(contains('motivo')));
    });

    // CA-10: la entrega se toma por parada y se libera por parada.
    test('el reclamo de la entrega lleva la parada', () {
      expect(campos('ReclamarViajeRequest'), containsAll(['proceso', 'lugar_id']));
    });

    test('la entrega se libera con ?lugar_id=', () {
      final parametros =
          ((rutas['/api/viajes/{}/reclamos/{}'] as Map<String, dynamic>)['delete'] as Map<String, dynamic>)['parameters'] as List<dynamic>;
      expect(parametros.map((p) => (p as Map<String, dynamic>)['name']), contains('lugar_id'));
    });
  });

  // S8-HU-05.2: la espera del inspector del punto. La activación de «No hay inspector» y lo que la parada trae para
  // contar el reloj sin señal, calificar los daños del conductor y marcar lo entregado con el plan B.
  group('la espera del inspector del punto', () {
    // CA-2: la activación, con el motivo del catálogo y la hora y el GPS del celular.
    test('POST /api/viajes/{}/entregas/{}/sin-inspector', () {
      final operaciones = rutas['/api/viajes/{}/entregas/{}/sin-inspector'] as Map<String, dynamic>?;
      expect(operaciones, isNotNull, reason: 'La ruta de «No hay inspector» en la parada ya no existe en el contrato');
      expect(operaciones!.containsKey('post'), isTrue);
    });

    test('la activación lleva el motivo, su texto, la hora y el GPS del momento', () {
      expect(
        campos('ActivarSinInspectorRequest'),
        containsAll(['motivo_sin_inspector', 'motivo_sin_inspector_texto', 'fecha_momento', 'lat', 'lng', 'precision_m']),
      );
    });

    // CA-3, CA-10: la última espera, X e Y vigentes y los daños sin nivel que marcó el conductor.
    test('la parada trae su espera, los plazos y los daños por calificar', () {
      expect(
        campos('EntregaPuntoDto'),
        containsAll(['espera', 'espera_aviso_minutos', 'espera_plan_b_minutos', 'danos_por_calificar']),
      );
    });

    test('la espera dice cómo va y desde cuándo cuenta cada plazo', () {
      expect(
        campos('EsperaDelPuntoDto'),
        containsAll([
          'id', 'estado', 'motivo_sin_inspector', 'motivo_sin_inspector_texto', 'activada_at', 'aviso_desde', 'aviso_at',
          'plan_b_desde', 'plan_b_habilitado', 'inspector_nombre', 'inspector_llego_at', 'cerrada_at',
        ]),
      );
    });

    test('cada daño por calificar trae su VIN, su parte, su clase y su foto', () {
      expect(campos('DanoPorCalificarDto'), containsAll(['hallazgo_id', 'vin', 'parte', 'clase_dano', 'evidencia_id']));
    });

    test('el carro entregado con el plan B lo dice', () {
      expect(campos('EntregaUnidadDto'), contains('entregada_sin_inspector_del_punto'));
    });
  });

  // S8-HU-07.3: lo que el OpenAPI no describe porque va dentro de un objeto libre (los catálogos, `details.motivo`,
  // `datos` del aviso) se ata al contrato escrito, que está junto a él (backend/CONTRATO.md).
  group('S8-HU-07.3 en el contrato escrito', () {
    final contrato = File('${archivo.parent.parent.path}/CONTRATO.md');
    final texto = contrato.existsSync() ? contrato.readAsStringSync() : null;

    test('el catálogo de los tipos de la novedad del viaje: su nombre, quién lo elige y su urgencia', () {
      final fila = texto!.split('\n').where((l) => l.startsWith('| `${Catalogo.tiposNovedadViaje}` |')).single;
      expect(fila, allOf(contains('`rol`'), contains('`conductor`, `inspector` o `todos`'), contains('`urgencia`')));
    }, skip: texto == null ? 'No se encontró backend/CONTRATO.md' : false);

    test('los motivos que la app reconoce, con los nombres del servidor', () {
      for (final motivo in [
        motivoEntregaDelCargueSinCerrar,
        motivoLugarNoAdmiteSinInspector,
        motivoTransporteLoArmaElSistema,
        motivoConductorSinPermiso,
        motivoTransferenciaSinCierre,
      ]) {
        expect(texto, contains(motivo), reason: motivo);
      }
    }, skip: texto == null ? 'No se encontró backend/CONTRATO.md' : false);

    test('el aviso de la novedad del viaje y los VIN del cargue sin inspector', () {
      expect(texto, contains('`$tipoNovedadViaje`'));
      expect(texto, contains('"hecho": "$hechoCargueSinInspector", "vins"'));
    }, skip: texto == null ? 'No se encontró backend/CONTRATO.md' : false);
  });

  // S8-HU-09.2 CA-7: lo que la app lee y envía de la ubicación y los parámetros, atado al contrato.
  group('parámetros del servidor, ubicación al reclamar y precisión al fijar', () {
    test('los dos parámetros de la app son enteros del servidor', () {
      final propiedades = ((esquemas['ParametrosDeLaAppDto'] as Map<String, dynamic>)['properties'] as Map<String, dynamic>);
      expect((propiedades['acomodacion_fotos_minimas'] as Map<String, dynamic>)['type'], 'integer');
      expect((propiedades['gps_precision_minima_m'] as Map<String, dynamic>)['type'], 'integer');
    });

    // CA-4: `lat`, `lng` y `precision`, los tres opcionales: sin GPS el reclamo sale igual.
    test('el reclamo admite la posición y no la exige', () {
      final esquema = esquemas['ReclamarViajeRequest'] as Map<String, dynamic>;
      expect(campos('ReclamarViajeRequest'), containsAll(['lat', 'lng', 'precision']));
      expect((esquema['required'] as List<dynamic>? ?? const []).toSet().intersection({'lat', 'lng', 'precision'}), isEmpty);
    });

    // CA-3: al fijar se manda `precision` y el lugar la guarda en `precision_m`, que es lo que lee el aviso.
    test('fijar manda la precisión y el lugar la guarda como precision_m', () {
      expect(campos('FijarUbicacionRequest'), contains('precision'));
      expect(campos('LugarUbicacionDto'), contains('precision_m'));
    });
  });

  // S8-HU-09.2 CA-6 y CA-7: `datos` no tiene esquema en el OpenAPI (es un objeto libre), así que las claves que la app
  // lee de «Fuera del rango» y de «Ubicación fijada» (destino_aviso.dart: `destinoDeAviso` y `lineasDeDatos`) se atan
  // al texto del contrato, donde el backend las enumera.
  group('las claves de `datos` que la app lee, según CONTRATO.md', () {
    final contrato = [
      Platform.environment['CONTRATO_MD'],
      '../backend/CONTRATO.md',
      'backend/CONTRATO.md',
    ].whereType<String>().map(File.new).where((f) => f.existsSync()).firstOrNull;

    /// El renglón del contrato que describe el aviso: el que contiene [marca].
    String renglon(String marca) {
      final linea = contrato!.readAsLinesSync().where((l) => l.contains(marca)).firstOrNull;
      expect(linea, isNotNull, reason: 'CONTRATO.md ya no describe «$marca»');
      return linea!;
    }

    test('«Fuera del rango» del reclamo de la entrega', () {
      final linea = renglon('Fuera del rango: {lugar}');
      for (final clave in ['hecho', 'viaje_id', 'lugar_id', 'lugar', 'usuario_nombre', 'distancia_m', 'radio_m', 'precision_m']) {
        expect(linea, contains('`$clave`'), reason: 'la app lee `$clave` de los datos de fuera_de_rango');
      }
      expect(linea, contains('`reclamo_entrega`'), reason: 'con ese `hecho` el aviso lleva al viaje y no a la unidad');
    }, skip: contrato == null ? 'No se encontró backend/CONTRATO.md (monta la raíz del monorepo).' : false);

    test('«Ubicación fijada»', () {
      final linea = renglon('**Aviso `ubicacion_fijada`**');
      for (final clave in ['lugar_id', 'lugar', 'usuario_nombre', 'precision_m', 'fijada_at']) {
        expect(linea, contains('`$clave`'), reason: 'la app lee `$clave` de los datos de ubicacion_fijada');
      }
    }, skip: contrato == null ? 'No se encontró backend/CONTRATO.md (monta la raíz del monorepo).' : false);
  });
}
