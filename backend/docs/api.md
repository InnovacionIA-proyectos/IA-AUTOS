# API

La especificación de negocio, las historias de usuario y el índice de toda la documentación están en [../../docs/README.md](../../docs/README.md).

Todas las rutas, los metodos, los codigos HTTP y la forma de las respuestas conservan el contrato del sistema anterior, de modo que la app movil y el ETL ya desplegados siguen funcionando. Las diferencias intencionales estan en `docs/seguridad.md`.

Autenticacion: `Authorization: Bearer <token>` (JWT HS256). Los nombres de campo van en `snake_case`; las unicas excepciones en `camelCase` son el cuerpo de `login-portal` (lo fija el portal), la respuesta de la ingesta Sofasa y el endpoint `/api/metricas/rechazo`. El contrato esta versionado (`info.version` 2.11.0, sin prefijo en la ruta) y la lista de cambios respecto a la v1 esta en `docs/migracion-v1-v2.md`. Las fechas son ISO 8601 UTC con milisegundos (`2026-07-17T12:30:15.123Z`) y las de entrada exigen zona horaria. La especificacion OpenAPI se publica en `/openapi/v1.json` cuando `Documentacion__Habilitada=true` (siempre en Development).

## Roles

| Grupo | Roles | Se usa en |
|---|---|---|
| Campo | inspector, conductor, admin | informes, hallazgos, evidencias, firmas, subida de fotos |
| Administracion | admin, super_admin | carga masiva de VIN |
| Gestion de viajes | coordinador_calidad, admin, super_admin | crear y administrar viajes, asignar VIN, cargar viajes por Excel |
| Gestion de usuarios | admin, super_admin, coordinador_calidad | alta, cambio, baja y PIN de usuarios, carga de conductores por Excel (cada uno solo los de su cliente; solo super_admin asigna el nivel super_admin) |

## Endpoints

| Metodo y ruta | Acceso | Descripcion |
|---|---|---|
| POST `/api/auth/login` | publico, con limite de intentos | correo y contrasena; devuelve `{ token, usuario }` |
| POST `/api/auth/login-conductor` | publico, con limite de intentos | cedula y PIN (conductor o inspector sin correo); devuelve `{ token, usuario }`. 401 con datos incorrectos; 423 `ACCOUNT_LOCKED` tras 5 PIN errados seguidos (15 minutos) |
| POST `/api/auth/cambiar-pin` | autenticado (tambien con PIN temporal) | `{ pin_actual, pin_nuevo }`; 204. Obligatorio mientras el PIN sea temporal (el resto de la API responde 403 `PIN_CHANGE_REQUIRED`) |
| POST `/api/auth/login-portal` | publico, con limite de intentos | inicio de sesion unico: recibe el arreglo del usuario del portal (usa solo el id y lo revalida en TI); devuelve `{ access, refresh, user }`. 403 `PORTAL_USER_NOT_LINKED` si no tiene acceso a IA Autos |
| POST `/api/auth/refresh` | publico | recibe `{ refresh }`; devuelve `{ access, refresh }` y rota el refresh (el anterior queda revocado) |
| POST `/api/auth/logout` | publico | recibe `{ refresh }`; revoca la sesion (refresh y access) y responde 204 |
| GET `/api/auth/me` | autenticado (tambien con PIN temporal) | perfil del usuario actual: datos, cedula, cliente y `capacidades` para armar el menu segun el rol |
| GET `/api/usuarios` | autenticado | quien gestiona usuarios: pagina de usuarios completos con filtros `q`, `rol`, `activo`, `cliente_id`, `pagina`, `tamano` y `orden`; los demas roles: `id`, `nombre`, `rol` de los usuarios activos de su cliente |
| POST `/api/usuarios` | gestion de usuarios | crea usuario: con `password` (y `email`) entra con correo; sin ella, con `cedula` y PIN (conductor o inspector; `pin` opcional, si no se envia se genera uno temporal) |
| PATCH `/api/usuarios/{id}` | gestion de usuarios | cambia solo los campos enviados; `null` limpia `subtipo_inspector`, `email` y `cedula` |
| DELETE `/api/usuarios/{id}` | gestion de usuarios | `{ modo: "eliminado" \| "retirado" }`; no permite auto-eliminacion |
| POST `/api/usuarios/{id}/pin` | gestion de usuarios | asigna o reinicia el PIN (temporal: la persona debe cambiarlo); levanta el bloqueo |
| POST `/api/usuarios/{id}/pin/forzar-cambio` | gestion de usuarios | obliga a cambiar el PIN en el proximo uso |
| POST `/api/usuarios/{id}/desbloquear` | gestion de usuarios | levanta el bloqueo por PIN errados |
| POST `/api/usuarios/cargar/previsualizar`, POST `/api/usuarios/cargar` | gestion de usuarios | carga de conductores por Excel: vista previa con el error de cada fila y confirmacion que crea los validos con su PIN temporal |
| POST `/api/vehiculos/cargar` | administracion | hasta 5000 VIN; idempotente (las etapas nacen cuando el VIN entra a un viaje) |
| GET `/api/vehiculos?vin=` | autenticado | autocompletado por fragmento de VIN o marca (maximo 8); inspector y conductor solo ven los VIN de sus viajes; un vehiculo dado de baja no aparece |
| DELETE `/api/vehiculos/{vin}` | admin, super_admin, coordinador_calidad (de un VIN de su cliente) | baja logica: `{ modo: "inactivado" }`, `activo: false`; conserva etapas, inspecciones y evidencias; no se busca ni se agrega a un viaje (`400`); sigue en el listado, el inventario y los reportes; `404` si no existe |
| GET `/api/vehiculos/listado` | autenticado | pagina `{items, total, pagina, tamano}` (50 por defecto, maximo 200) con `orden=campo:asc|desc` y filtros `tipo` (movilizados, pendientes o novedad), `vin`, `operacion`, `marca`, `cliente_id`, `estado_unidad`, `activo`, `desde`, `hasta`; ver CONTRATO.md «Paginacion y filtros» |
| GET `/api/vehiculos/{vin}/inventario` | autenticado | etapas (de todos sus viajes), inspecciones, hallazgos, evidencias y transferencia; `403` si inspector o conductor y el VIN no esta en sus viajes |
| GET `/api/vehiculos/{vin}/inventario/pdf` | autenticado | inventario en PDF (descarga) con las fotos de sus inspecciones (con su sello) y los paneles de danos con las zonas con hallazgos marcadas; hasta 60 fotos |
| POST `/api/viajes` | gestion de viajes | crea un viaje con unidad, placa, trailer, conductor, codigo MTrack (unico, `409` si se repite) y sus VIN; crea las etapas de cada VIN |
| GET `/api/viajes` | autenticado | pagina `{items, total, pagina, tamano}` (50 por defecto, maximo 200) con `orden` y filtros `estado`, `codigo_mtrack`, `vin`, `conductor_id`, `inspector_id`, `cliente_id`, `origen_id`, `destino_id`, `desde`, `hasta`; inspector y conductor solo ven los suyos; `cliente_id` trae los viajes con algun VIN de ese cliente (solo el super_admin elige; el resto siempre recibe los de su cliente); cada fila trae `clientes` |
| GET `/api/viajes/mios` | autenticado | los viajes del usuario (conductor o inspector) con sus VIN; por defecto programados y en curso |
| GET `/api/viajes/{id}` | autenticado | un viaje con sus VIN y sus `clientes`; `403` si no es de un inspector o conductor, o si quien consulta no es super_admin y el viaje no tiene unidades de su cliente; fuera del conductor, el inspector del viaje y el super_admin, solo ve los VIN de su cliente |
| GET `/api/viajes/{id}/firmantes` | autenticado | los usuarios del viaje que pueden firmar una transferencia (su conductor y su inspector activos) |
| PATCH `/api/viajes/{id}` | gestion de viajes | cambia los campos enviados de un viaje programado o en curso |
| PATCH `/api/viajes/{id}/estado` | gestion de viajes | programado a en curso a finalizado, o cancelar |
| POST `/api/viajes/{id}/vehiculos`, DELETE `/api/viajes/{id}/vehiculos/{vin}` | gestion de viajes | asigna o quita VIN (todo o nada; no se quita un VIN con inspecciones) |
| DELETE `/api/viajes/{id}` | gestion de viajes | borra un viaje sin inspecciones (`409` si las tiene) |
| POST `/api/viajes/cargar/previsualizar`, POST `/api/viajes/cargar` | gestion de viajes | carga por Excel: vista previa con el error de cada fila y confirmacion que guarda solo las filas validas |
| POST `/api/inspecciones` | campo | crea la inspeccion de salida o de llegada de una etapa (`momento`: `salida` o `llegada`; siempre en borrador; el usuario sale del token; placa, trailer y conductor salen del viaje; `403` si la etapa es de un viaje ajeno; `409 ETAPA_PREVIA_SIN_CIERRE` si es una salida y la etapa anterior no tiene su llegada cerrada o su transferencia no tiene tipo de cierre) |
| POST `/api/inspecciones/{id}/corregir` | campo | corrige una inspeccion cerrada: crea la version siguiente (borrador, vacia) que referencia a la anterior; la anterior no cambia; `409` si no esta cerrada o ya fue corregida |
| GET `/api/inspecciones` | autenticado | pagina `{items, total, pagina, tamano}` (50 por defecto, maximo 200) con `orden` y filtros `vin`, `viaje_id`, `usuario_id`, `momento`, `estado`, `proceso`, `operacion`, `marca`, `cliente_id`, `origen_id`, `destino_id`, `desde`, `hasta`; inspector y conductor solo ven los informes de sus viajes |
| GET `/api/inspecciones/{id}` | autenticado | informe completo con firmas, nombres y el sello de cada foto |
| GET `/api/inspecciones/{id}/hallazgos-previos` | inspector, admin, super_admin (no conductor); el inspector solo de sus viajes | los hallazgos de las inspecciones cerradas anteriores del mismo VIN (incluido un viaje del que la unidad no salio), del mas reciente al mas antiguo, con viaje, proceso, momento, cierre y fotos: la lista con la que el cliente propone un hallazgo preexistente |
| GET `/api/hallazgos` | inspector (sus viajes), coordinador_calidad, comercial, admin (su cliente), super_admin; conductor 403 | pagina de hallazgos (`items`, `total`, `pagina`, `tamano` hasta 200) con filtros `estado_seguimiento`, `origen`, `revisado`, `cliente_id` (solo super_admin), `viaje_id`, `vin`, `inspeccion_id`, `nivel`, `cuadrante_numero`, `lado`, `proceso`, `desde`, `hasta` y `orden`; cada fila es el hallazgo con su VIN, viaje, proceso, momento, inspeccion y foto propia |
| GET `/api/hallazgos/{id}` | los mismos | la misma fila con `seguimiento` (historial) y `notas_aclaratorias` |
| POST `/api/hallazgos/{id}/revision` | inspector (de un VIN de sus viajes), admin, super_admin; coordinador_calidad solo en la salida de un Cargue que hizo el conductor por la excepción «No hay inspector» | revisa el hallazgo que registro el conductor (`cuadrante_numero` + `lado` o `zona_provisional`, `clase_dano` y `nivel_severidad` obligatorios, `tamano_dano` y `nota` opcionales); responde el `HallazgoDto` completo; `409 CONFLICT` si no es del conductor y `409 HALLAZGO_YA_REVISADO` si ya se reviso; un nivel critico alerta al cliente |
| POST `/api/inspecciones/{id}/notas` | autor de la inspeccion, admin, super_admin, coordinador_calidad (de su cliente) | nota aclaratoria de la inspeccion (`texto` hasta 1000); `201 NotaAclaratoriaDto`, tambien con la inspeccion cerrada; `403` los demas |
| POST `/api/hallazgos/{id}/notas` | autor de la inspeccion del hallazgo, admin, super_admin, coordinador_calidad | nota aclaratoria del hallazgo |
| POST `/api/inspecciones/{id}/transferencia/notas` | quien firmo la transferencia, admin, super_admin, coordinador_calidad | nota aclaratoria de la transferencia (`404` si no existe) |
| GET `/api/inspecciones/{id}/notas`, `/api/hallazgos/{id}/notas`, `/api/inspecciones/{id}/transferencia/notas` | quien ve el registro | la lista de notas, de la mas antigua a la mas reciente (no hay PATCH ni DELETE) |
| POST `/api/hallazgos/{id}/seguimiento` | gestionar_seguimiento (coordinador_calidad, comercial, admin, super_admin), del cliente del usuario | cambia el estado del hallazgo (`validado`, `rechazado`, `recuperado`, `derogado`) con nota y, en un V2 validado, la foto del sello (`evidencia_sello`); responde el `HallazgoDto` con `evidencia_completa` y `evidencia_faltante`; `409` si la inspeccion esta en borrador o es una novedad sin revisar |
| GET `/api/hallazgos/{id}/seguimiento` | gestionar_seguimiento | el historial del hallazgo (cambios de seguimiento, revisiones y «no sale») |
| POST `/api/inspecciones/{id}/evidencias` | campo, creador o administrador | registra una foto ya subida de una vista (`vista_id`) o de un hallazgo (`hallazgo_id`), con su sello (fecha, GPS, precision, dispositivo); el servidor agrega su sello y el hash SHA-256; las vistas adicionales exigen `nota` |
| POST `/api/inspecciones/{id}/hallazgos` | campo, creador o administrador | agrega un hallazgo con su **zona** (`cuadrante_numero` + `lado`, o `zona_provisional`), clase, tamano, `nivel_severidad` (el conductor no lo envia), `es_preexistente` con `hallazgo_origen_id` y `nota`, **con su foto propia** (`evidencia` obligatoria; sin ella `400`). `parte_vehiculo` y `coords_diagrama` ya no se aceptan (`400`) |
| POST `/api/inspecciones/{id}/transferencia` | campo | firma la propia parte (quien entrega firma la entrega y quien recibe, el recibo; la identidad sale del token, `403` si no es ninguna de las dos partes); la primera firma crea la transferencia con los dos ids, que deben ser usuarios del viaje; guarda la fecha y la IP de cada firma |
| POST `/api/inspecciones/{id}/transferencia/reservas` | campo | quien recibe firma con reservas: observaciones obligatorias (`400` sin ellas, `403` si no es quien recibe) |
| POST `/api/inspecciones/{id}/transferencia/no-firma` | campo | constancia de no firma: la registra la otra parte con motivo, testigo con usuario y foto; cierra la transferencia como `no_firma` |
| GET `/api/inspecciones/{id}/pdf` | quien ve el informe (`403` de otro cliente o de un viaje ajeno) | informe en PDF: unidad, viaje y etapa; fotos por vista con su sello; hallazgos con zona, clase, tamano, nivel y foto; panel de danos con las zonas marcadas; transferencia con firmas, reservas o constancia; notas aclaratorias e historial de seguimiento; hasta 60 fotos |
| GET `/api/exportaciones/viajes.xlsx`, `/inspecciones.xlsx`, `/hallazgos.xlsx` | exportar_soportes (coordinador_calidad, comercial, admin, super_admin; `403` los demas) | Excel con los datos de su cliente (el super_admin elige uno) y los mismos filtros del listado; nombre y fecha en `Content-Disposition`; maximo 50000 filas |
| GET `/api/inspecciones/{id}/transferencia/constancia/pdf` | autenticado | constancia en PDF: firmas con fecha e IP, reservas o motivo de la no firma, testigo y foto; `403` si es de un viaje ajeno, `404` sin transferencia |
| POST `/api/viajes/{id}/vehiculos/{vin}/novedades` | conductor del viaje (403 a otro) | registra una novedad del recorrido (`fecha`, `lat`, `lng`, `tipo_novedad`, `tamano_dano`, `evidencia` con su sello completo; sin texto libre: `nota` responde 400 «Campo no admitido»); crea la salida de Transporte en borrador si no existe (`409 ETAPA_PREVIA_SIN_CIERRE` si el Cargue no esta cerrado); `201` con el hallazgo; avisa a los coordinadores y administradores del cliente por bandeja y push; `409 UNIDAD_NO_SALE` |
| POST `/api/viajes/{id}/vehiculos/{vin}/llegada` | conductor del viaje | confirma la llegada (`fecha`, `lat`, `lng`): cierra la salida de Transporte y crea y cierra la llegada; `200` con `inspeccion`, `novedades` y `sin_novedad`; `400` sin GPS; `409 LLEGADA_YA_CERRADA` o `UNIDAD_NO_SALE` |
| GET `/api/parametros` | administrar_catalogos (admin, super_admin, coordinador_calidad; `403` los demas) | todos los parametros ordenados por clave: `clave`, `valor` (nulo si no tiene), `tipo`, `descripcion`, `actualizado_por`, `updated_at` |
| PATCH `/api/parametros/{clave}` | administrar_catalogos | cambia el `valor` (texto o numero) y responde el parametro; `400` en `valor` si no cumple su tipo (numero, porcentaje 0-100, lista de codigos existentes, hora HH:mm, correos validos); `404` si la clave no existe; no hay API para crear ni borrar claves; se aplica en un minuto o menos y queda en la auditoria con antes y despues |
| GET `/api/auditoria` | ver_auditoria (admin de su cliente, super_admin de todos) | una pagina de la auditoria, la mas reciente primero, con `usuario_id`, `entidad`, `entidad_id`, `viaje_id`, `accion`, `desde`, `hasta`, `pagina` y `tamano`: `items`, `total`, `pagina`, `tamano`; `400` con un filtro mal formado; `403` los demas roles |
| GET `/api/auditoria/{id}` | ver_auditoria | un registro con su `detalle` (`antes` y `despues`); `404` si no existe, `403` si es de otro cliente |
| GET `/api/notificaciones` | ver_alertas (coordinador_calidad, admin, super_admin, inspector, conductor) | la bandeja del usuario (el super_admin ve las de todos los clientes) con `leida`, `tipo`, `urgencia`, `pagina` y `tamano`: `items`, `total`, `pagina`, `tamano` y `no_leidas` |
| POST `/api/notificaciones/{id}/leida` | el destinatario | marca la notificacion como leida |
| POST `/api/notificaciones/{id}/respuesta` | coordinador_calidad, admin, super_admin, de una novedad de su cliente | responde la novedad del conductor (`texto`, `medio`: `app` o `llamada`) y le llega al conductor del viaje; `409` si ya fue respondida |
| POST `/api/dispositivos` | autenticado | registra o actualiza el token de FCM del dispositivo (`token_fcm`, `plataforma`, `modelo`) |
| DELETE `/api/dispositivos/{token_fcm}` | autenticado | retira el token al cerrar sesion (siempre `204`) |
| POST `/api/viajes/{id}/vehiculos/{vin}/no-sale` | inspector del viaje y coordinador_calidad (403 a los demas) | marca que la unidad no sale del viaje por un hallazgo V1+ de una inspeccion cerrada (`motivo`, `hallazgo_id`, `vin_reemplazo` opcional); responde el viaje; `409` si no es V1+, no es de ese VIN, la inspeccion no esta cerrada, ya no sale o el reemplazo esta en un viaje activo o antes no salio y no esta recuperado |
| POST `/api/inspecciones/{id}/cerrar` | campo, creador o administrador | cierra el informe; `{ inspeccion, resumen }`. En Cargue y Descargue exige las 7 vistas obligatorias (`400` con el nombre de la que falta), una foto propia por hallazgo y ubicacion en toda foto; en Transporte exige el GPS del registro |
| POST `/api/uploads` | campo | sube una foto en base64 (JSON) o como `multipart/form-data` (campo `archivo`); JPG o PNG; `{ storage_path, url, url_expira_at, hash_sha256 }` con la URL absoluta y firmada |
| GET `/api/uploads/{archivo}?exp=&firma=` | URL firmada (sin token) | sirve la imagen; `403 URL_FIRMADA_INVALIDA` sin firma, con firma invalida o vencida; `404` si no existe |
| POST `/api/etl/sofasa/ordenes` | token del ETL | ingesta idempotente por `source_id` |
| GET `/api/sofasa/ordenes` | autenticado | pagina `{items, total, pagina, tamano}` (50 por defecto, maximo 200; mas recientes primero) con `orden` y filtros `vin`, `estado`, `ciudad`, `desde`, `hasta`; cada orden trae `viaje_id` y `codigo_mtrack`; inspector y conductor solo ven las de sus viajes |
| GET `/api/catalogos` | autenticado | todos los catalogos maestros (etapas, lugares, clases y tamanos de dano, niveles, vistas, cuadrantes con las zonas provisionales, paneles de dano y sus regiones, clientes...) con version (`ETag`, `304` con `If-None-Match`) |
| GET `/api/catalogos/estados-sofasa` | autenticado | los estados de las ordenes Sofasa (`codigo` = el `status_code` real, `nombre`, `orden`, `activo`); se administra como los demas catalogos y un estado que alguna orden usa no se borra (`409`); `GET /api/sofasa/ordenes` sigue devolviendo `status_code` sin traducir |
| GET `/api/catalogos/{nombre}` | autenticado | un catalogo con su version (`cuadrante-vistas` ya no existe: `404`; lo reemplazan `paneles-dano` y `cuadrante-regiones`) |
| GET `/api/catalogos/paneles-dano/{codigo}/imagen` | publico | la imagen de fondo de un panel de dano, con cache larga; `404` si aun no tiene |
| PUT `/api/catalogos/paneles-dano/{codigo}/imagen` | admin, super_admin | cambia la imagen del panel con un archivo ya subido (`{ "archivo": "…png" }`) |
| POST `/api/catalogos/{nombre}`, PATCH `/api/catalogos/{nombre}/{id}`, DELETE `/api/catalogos/{nombre}/{id}` | admin, super_admin, coordinador_calidad | crea, edita o borra un valor; uno en uso no se borra (`409`), se inactiva |
| GET `/api/reportes/cobertura`, `/adopcion`, `/tiempo-captura`, `/averias`, `/novedades-proceso`, `/patrones`, `/indicador-diario` | ver_reportes (coordinador_calidad, comercial, admin, super_admin; `403` inspector y conductor) | los reportes del tablero, en snake_case, con los filtros `desde`, `hasta` (ultimos 30 dias por defecto), `operacion`, `cliente_id` (solo super_admin), `punto_id`, `marca`, `trailer`, `proceso` y `nivel`; cuentan hallazgos de inspecciones cerradas aunque la unidad no haya terminado el viaje y el cliente del vehiculo |
| GET `/api/metricas/rechazo` | autenticado | tasa de rechazo frente a la linea base |
| GET `/health` | publico | `200` si la base responde, `503` si no |

## Errores

Siempre JSON:

```json
{ "error": "Mensaje", "code": "CONFLICT", "message": "Mensaje", "timestamp": "2026-07-17T12:30:15.123Z", "details": {} }
```

| Codigo | HTTP | Cuando |
|---|---|---|
| VALIDATION_ERROR | 400 | cuerpo invalido, datos que no cumplen las reglas, filtros mal formados |
| UNAUTHORIZED | 401 | sin token, token invalido o vencido, usuario retirado o inexistente, credenciales incorrectas |
| FORBIDDEN | 403 | rol insuficiente o inspeccion ajena |
| PIN_CHANGE_REQUIRED | 403 | el PIN es temporal: hay que cambiarlo con `POST /api/auth/cambiar-pin` |
| ACCOUNT_LOCKED | 423 | cuenta bloqueada por PIN errados seguidos; `details.bloqueado_hasta` |
| NOT_FOUND | 404 | recurso o ruta inexistente |
| METHOD_NOT_ALLOWED | 405 | metodo no admitido en una ruta existente |
| CONFLICT | 409 | duplicados, inspeccion cerrada, transferencia ya cerrada, firmantes de la transferencia que no se pueden cambiar, registro que es evidencia y no se puede borrar |
| HALLAZGO_YA_REVISADO | 409 | la novedad del conductor ya fue revisada por un inspector |
| URL_FIRMADA_INVALIDA | 403 | la URL de una foto no tiene firma, la firma no coincide o ya vencio |
| LLEGADA_YA_CERRADA | 409 | la llegada de Transporte de esa unidad ya estaba confirmada (un reintento del conductor es un exito) |
| UNIDAD_NO_SALE | 409 | la unidad se marco «no sale» del viaje: no admite novedades ni llegada |
| ETAPA_PREVIA_SIN_CIERRE | 409 | la etapa anterior del VIN no esta terminada (sin llegada cerrada o con la transferencia sin tipo de cierre): no se puede crear la salida de la siguiente |
| RATE_LIMITED | 429 | demasiados intentos de login |
| INTERNAL_ERROR | 500 | error inesperado (sin detalles internos) |
| DB_UNAVAILABLE | 503 | reservado |

`details` en errores de validacion tiene la forma `{ "formErrors": [], "fieldErrors": { "campo": ["mensaje"] } }`; en el cierre de una inspeccion es la lista de requisitos incumplidos. La app movil clasifica por codigo HTTP: 401, 408, 429 y 5xx se reintentan; un 409 al cerrar se toma como exito.
