# Guía de migración v1 → v2

La versión **2.1.0** del contrato (`docs/openapi.json`, `info.version`) es la que consumen la app y el módulo del portal; la 2.0.0 sigue registrada en `docs/openapi-versiones.json`. **No hay prefijo de versión**: la API sigue en `/api` (no hay clientes externos de la v1). Esta guía es la lista **cerrada** de los cambios de
ruptura entre lo que ya estaba publicado (v1) y la v2: lo que no está aquí no cambió de forma que rompa a un cliente. El detalle de cada endpoint está en [CONTRATO.md](../CONTRATO.md); el contrato máquina-legible, con ejemplos y los códigos de error por endpoint, en
[openapi.json](openapi.json).

## Cómo leerla

| Tipo | Qué significa |
|---|---|
| Ruptura | un cliente que usaba lo anterior deja de funcionar o recibe otra forma: hay que cambiarlo |
| Retiro | lo anterior se quita; se anuncia aquí y se hace en la versión que se indica |

## Lista de cambios de ruptura

### 1. Momentos de una inspección: `salida` y `llegada`

| v1 | v2 |
|---|---|
| `momento`: `check_in`, `check_out` o `informe` | `momento`: **`salida`** o **`llegada`** |

`informe` y `check_in` pasan a `salida`, y `check_out` a `llegada`. Para mostrar, use `momento_nombre` («Inspección de salida» / «Inspección de llegada»). Las inspecciones ganan `iniciada_at`, `cerrada_at`, `version` y `corrige_inspeccion_id`; una inspección cerrada
se corrige con `POST /api/inspecciones/{id}/corregir` (versión nueva). La salida de una etapa exige la llegada cerrada de la etapa anterior del VIN (`409 ETAPA_PREVIA_SIN_CIERRE`). Ver [inspecciones.md](inspecciones.md).

### 2. Firma de la transferencia por su titular (`firma`)

| v1 | v2 |
|---|---|
| El cuerpo traía los dos usuarios y las dos firmas (`firma_entrega`, `firma_recibe`) en una sola llamada | El cuerpo trae **una** `firma`: la identidad sale del token y el servidor la guarda en la parte que corresponde a quien firma (entrega o recibe) |

`POST /api/inspecciones/{id}/transferencia` (y `/reservas`) reciben `{ entrega_usuario_id, recibe_usuario_id, firma }` y responden `403` si quien firma no es la parte que firma. Los firmantes deben ser el conductor o el inspector del viaje (`GET /api/viajes/{id}/firmantes`).
La transferencia tiene tipo de cierre (`firmada`, `con_reservas`, `no_firma`) y cada firma guarda su fecha e IP. Ver [transferencias.md](transferencias.md).

### 3. Hallazgo por región (sin `parte_vehiculo` ni `coords_diagrama` en el request)

| v1 | v2 |
|---|---|
| `POST /api/inspecciones/{id}/hallazgos` con `parte_vehiculo` (texto libre) y `coords_diagrama` (punto sobre la imagen) | `cuadrante_numero` + `lado` (solo si el cuadrante lo lleva), `clase_dano`, `tamano_dano`, `nivel_severidad`, `es_preexistente`, `hallazgo_origen_id`, `nota` y la `evidencia` |

`parte_vehiculo` y `coords_diagrama` **ya no se envían**; en las respuestas solo aparecen (con valor) en los hallazgos históricos. El hallazgo se ubica en una zona (lado + número de un cuadrante) y lleva nivel de severidad y estado de seguimiento. Ver [zonas-dano.md](zonas-dano.md).

### 4. `cuadrante-vistas` → `paneles-dano` + `cuadrante-regiones`

| v1 | v2 |
|---|---|
| Catálogo `cuadrante-vistas`: el polígono de cada cuadrante por vista de foto | Catálogos **`paneles-dano`** (los 5 paneles del dibujo de daños, con su imagen) y **`cuadrante-regiones`** (la región tocable de cada zona en cada panel) |

`GET /api/catalogos/cuadrante-vistas` responde `404`. Los paneles ya no son vistas de foto. El catálogo `cuadrantes` describe las piezas y sus zonas (nombre completo y código AIAG; las zonas provisionales ya no existen). Se agrega además el catálogo **`estados-sofasa`** (el `status_code` real de las órdenes Sofasa con su etiqueta). Ver [catalogos.md](catalogos.md).

### 5. Listados con `{items, total, pagina, tamano}` y filtros en `snake_case`

| v1 | v2 |
|---|---|
| `GET /api/vehiculos/listado`: `{ rows, total, page, pageSize }`, 500 por página, parámetro `page` | `{ items, total, pagina, tamano }`, 50 por defecto y máximo 200, parámetros `pagina`, `tamano` y `orden` |
| `GET /api/inspecciones`, `GET /api/viajes` y `GET /api/sofasa/ordenes`: una lista (hasta 200 o 500) | un objeto `{ items, total, pagina, tamano }` |
| Filtros `codigoMtrack`, `conductorId`, `usuarioId`, `viajeId`, `clienteId` (viajes, inspecciones y usuarios) | `codigo_mtrack`, `conductor_id`, `usuario_id`, `viaje_id`, `cliente_id` |
| El VIN de los filtros se escribía como fuera | Una sola regla: 17 caracteres sin `I`, `O` ni `Q`, o un chasis de 6 a 20 alfanuméricos (`400` con el motivo, también en `POST /api/vehiculos/cargar`) |

`pageSize`, `rows`, `page`, `codigoMtrack` y `conductorId` **desaparecen**. Filtros nuevos: inspecciones (`operacion`, `marca`, `cliente_id`, `origen_id`, `destino_id`), viajes (`inspector_id`, `cliente_id`, `origen_id`, `destino_id`) y vehículos (`operacion`, `marca`, `cliente_id`,
`estado_unidad`, `activo`, `desde`, `hasta`). `orden` es `campo:asc|desc` con los campos de cada listado (`400` si no). Las exportaciones a Excel de viajes e inspecciones usan los mismos nombres. Ver «Paginación y filtros» en [CONTRATO.md](../CONTRATO.md).

### 6. Fotos con URL firmada y `url_expira_at`

| v1 | v2 |
|---|---|
| `url` relativa `/api/uploads/<archivo>`, pública (configurable con `Storage__LecturaPublica`) | URL `/api/uploads/<archivo>?exp=&firma=` firmada por el sistema, con su vencimiento en **`url_expira_at`** |

La foto solo se ve con una URL firmada y vigente (`403 URL_FIRMADA_INVALIDA` si no); no hace falta el encabezado `Authorization`. Cuando vence se vuelve a pedir el recurso (informe, inventario…) para recibir una URL nueva: no se guarda la URL sino el recurso. La vigencia es el parámetro
`url_firmada_minutos` (15 por defecto). Ver «URL firmada de una foto» en [CONTRATO.md](../CONTRATO.md).

### 7. `ver_reportes` restringida y capacidades nuevas

| v1 | v2 |
|---|---|
| `ver_reportes` la tenían todos los roles | `ver_reportes` la tienen **`coordinador_calidad`, `comercial`, `admin` y `super_admin`**; el inspector y el conductor reciben `403` en `/api/reportes/*` |
| — | Capacidades nuevas en `GET /api/auth/me`: **`gestionar_seguimiento`** (`coordinador_calidad`, `comercial`, `admin`, `super_admin`), **`ver_alertas`** (`coordinador_calidad`, `admin`, `super_admin`, y `inspector` y `conductor` para los avisos que les tocan), **`ver_auditoria`** (`admin` de su cliente y `super_admin`) y **`exportar_soportes`** (`coordinador_calidad`, `comercial`, `admin`, `super_admin`) |

El menú debe armarse con `capacidades`, no con el nombre del rol.

### 8. `/api/patrones` y `/api/patrones/analisis` retirados (2.1.0)

`GET /api/patrones` y `GET /api/patrones/analisis` **ya no existen** (responden `404`) y sus esquemas (`PatronesDto`, `AnalisisDto`) salieron del contrato. Los reemplaza `GET /api/reportes/patrones` (y el resto de
`/api/reportes/*`, en `snake_case`), que trae los hallazgos por proceso, por zona y por zona y proceso, y por nivel, clase y tamaño. Hasta la 2.0.0 respondían igual pero con los encabezados `Deprecation: true` y `Link: </api/reportes/patrones>; rel="successor-version"`.

### 9. `Storage__LecturaPublica` retirada

La variable de entorno `Storage__LecturaPublica` (que dejaba leer las imágenes sin token) **ya no existe**: las fotos solo se sirven con la URL firmada del punto 6. Defina `Storage__FirmaSecreto` (mínimo 32 caracteres) en el despliegue; sin él se deriva del secreto de los tokens y se avisa en el
registro. Ver [configuracion.md](configuracion.md).

## Convención de nombres

Todo el contrato va en **`snake_case`** (cuerpos, respuestas, filtros y parámetros). Las únicas excepciones en `camelCase`:

| Dónde | Por qué |
|---|---|
| El cuerpo de `POST /api/auth/login-portal` (`nombreUsuario`…) | lo fija el portal |
| La respuesta de la ingesta Sofasa (`vehiculosCreados`, `vehiculosActualizados`) | la lee el ETL |
| El endpoint `/api/metricas/rechazo` | se retira en S6-HU-04.11 |

Un test del contrato falla si aparece un nombre en `camelCase` fuera de esa lista.

## El formato de error no cambia

Sigue siendo `{ "error", "code", "message", "timestamp", "details" }`; `details` es **opcional** (`fieldErrors` en una validación, `etapa_previa` en una etapa previa sin cierre, `bloqueado_hasta` en una cuenta bloqueada). El módulo y la app lo leen igual que en la v1. Los códigos nuevos
`HALLAZGO_YA_REVISADO` (409) y `URL_FIRMADA_INVALIDA` (403) —y `ETAPA_PREVIA_SIN_CIERRE`, `LLEGADA_YA_CERRADA`, `UNIDAD_NO_SALE`, `PIN_CHANGE_REQUIRED`, `ACCOUNT_LOCKED` y `PORTAL_USER_NOT_LINKED`— están en la tabla de errores de [CONTRATO.md](../CONTRATO.md) y en las respuestas de cada endpoint del contrato.

## Lo que esta guía no incluye

- El retiro de `/api/metricas/rechazo`: va en S6-HU-04.11 (condicionada a P-050); sigue publicado hasta entonces.
- Lo que no se retira: `parte_vehiculo` y `coords_diagrama` siguen apareciendo, con valor, en los hallazgos históricos que ya existen (el cliente que los lea no cambia); solo dejó de aceptarse al crear un hallazgo (punto 3).

## Cómo se versiona el contrato

`docs/openapi.json` se genera de la API y se publica con su versión; `docs/openapi-versiones.json` guarda la huella de cada versión publicada. Un test falla si el contrato generado difiere del publicado, y si el contrato **cambia sin cambiar `info.version`** (una versión publicada
no se modifica). Para publicar un cambio: suba `clsOpenApi.VERSION` y regenere con `ACTUALIZAR_OPENAPI=1`, `OPENAPI_PUBLICAR=1` y `OPENAPI_DESTINO=<ruta de docs/openapi.json>` al correr `ContratoOpenApiTests`.
