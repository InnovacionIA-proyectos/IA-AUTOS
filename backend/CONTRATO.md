# Contrato de la API (para frontend)

Describe cómo consumir hoy los endpoints del backend. Es una guía de consumo; la fuente de verdad técnica es el contrato
OpenAPI ([docs/openapi.json](docs/openapi.json)), que la API genera y una prueba vigila para que no se desvíe. Con
`Documentacion__Habilitada=true` también se ve en `GET /swagger` (interfaz) y `GET /openapi/v1.json`.

## Convenciones

| Tema | Regla |
|---|---|
| URL base | La del despliegue, sin barra final (por ejemplo `https://<dominio>`). Todas las rutas empiezan por `/api`, salvo `/health` |
| Formato | JSON (`Content-Type: application/json`), codificación UTF-8 |
| Nombres de campo | `snake_case` (`etapa_id`, `created_at`). **Excepciones en `camelCase`:** todo lo de Reportes (`/api/patrones`, `/api/patrones/analisis`, `/api/metricas/rechazo`), la respuesta de la ingesta Sofasa y `pageSize` del listado de vehículos |
| Fechas | ISO 8601 UTC con milisegundos: `2026-07-17T12:30:15.123Z`. **Las fechas que se envían deben llevar zona horaria** (`Z` o `+hh:mm`) |
| Identificadores | UUID (`string`), salvo el VIN (texto) y `source_id` de Sofasa (entero largo) |
| Nulos | Los campos opcionales sin valor llegan como `null` (nunca se omiten) |
| VIN | 17 caracteres alfanuméricos en mayúscula, sin las letras `I`, `O` ni `Q`; se normaliza a mayúscula y sin espacios |
| Imágenes | Se referencian por `url` **relativa** (`/api/uploads/<archivo>`): anteponga la URL base |
| CORS | La API solo acepta los orígenes configurados en `Cors__Origins`; la web debe estar en esa lista |

## Autenticación

1. `POST /api/auth/login` devuelve un `token` (JWT HS256, vigencia de 30 días por defecto).
2. Cada solicitud protegida envía `Authorization: Bearer <token>`.
3. Si responde `401`, el token falta, es inválido o venció, o el usuario fue retirado: lleve al usuario al login.
4. Si responde `403`, el rol no alcanza o la inspección es ajena. No es un problema de sesión.

El rol efectivo se lee de la base de datos en cada solicitud, no del token; un cambio de rol aplica de inmediato.

### Roles

| Rol | Valor |
|---|---|
| Inspector | `inspector` |
| Conductor | `conductor` |
| Coordinador de calidad | `coordinador_calidad` |
| Comercial | `comercial` |
| Administrador | `admin` |
| Superadministrador | `super_admin` |

| Grupo | Roles | Se usa en |
|---|---|---|
| Campo | `inspector`, `conductor`, `admin` | informes, hallazgos, evidencias, firmas, subida de fotos |
| Administración | `admin`, `super_admin` | carga masiva de VIN, asignaciones, etapas |
| Gestión de usuarios | `super_admin` | alta, cambio y baja de usuarios |

"Autenticado" significa cualquier rol con sesión válida.

## Errores

Todos los errores son JSON con la misma forma:

```json
{
  "error": "Mensaje",
  "code": "VALIDATION_ERROR",
  "message": "Mensaje",
  "timestamp": "2026-07-17T12:30:15.123Z",
  "details": {}
}
```

| `code` | HTTP | Cuándo |
|---|---|---|
| `VALIDATION_ERROR` | 400 | cuerpo inválido, datos que no cumplen las reglas, filtros mal formados |
| `UNAUTHORIZED` | 401 | sin token, token inválido o vencido, usuario retirado, credenciales incorrectas |
| `FORBIDDEN` | 403 | rol insuficiente o inspección ajena |
| `NOT_FOUND` | 404 | recurso o ruta inexistente |
| `METHOD_NOT_ALLOWED` | 405 | método no admitido en una ruta existente |
| `CONFLICT` | 409 | duplicados, inspección cerrada, transferencia ya firmada |
| `RATE_LIMITED` | 429 | demasiados intentos de login (10 por minuto por IP, configurable) |
| `INTERNAL_ERROR` | 500 | error inesperado, sin detalles internos |
| `DB_UNAVAILABLE` | 503 | reservado |

`details` en un `VALIDATION_ERROR` tiene la forma:

```json
{ "formErrors": [], "fieldErrors": { "campo": ["mensaje"] } }
```

Al cerrar una inspección que no cumple los requisitos, `details` es la lista de requisitos incumplidos. Muestre `message`
al usuario y use `code` (no el texto) para decidir qué hacer.

## Catálogos (valores permitidos)

| Catálogo | Valores |
|---|---|
| `rol` | `inspector`, `conductor`, `coordinador_calidad`, `comercial`, `admin`, `super_admin` |
| `subtipo_inspector` | `importacion`, `nacional`, `export_preembarque`, `export_recibido` |
| `operacion` (vehículo) | `importacion`, `exportacion`, `nacional` |
| `proceso` (punto de custodia) | `cargue`, `punto_origen`, `certificadora`, `punto_destino`, `transporte`, `descargue`, `concesionario` |
| `momento` (inspección) | `check_in`, `check_out`, `informe` |
| `estado` (inspección) | `borrador`, `cerrada` |
| `clase_dano` | `deformacion`, `rotura`, `cortado`, `perforacion`, `golpe`, `mancha`, `raya_pintar`, `desconchado`, `particulas`, `faltante` |
| `tamano_dano` | `1_5cm`, `5_10cm`, `10_20cm`, `mas_20cm`, `requiere_reemplazo`, `mas_10_particulas` |
| `tipo` (listado de vehículos) | `movilizados`, `pendientes`, `novedad` |

---

## Autenticación y salud

### `POST /api/auth/login`
Público, con límite de intentos.

```json
{ "email": "ana@empresa.com", "password": "********" }
```

`200`:

```json
{
  "token": "eyJhbGciOi...",
  "usuario": { "id": "uuid", "nombre": "Ana Pérez", "email": "ana@empresa.com", "rol": "inspector" }
}
```

Correo y contraseña son obligatorios (`400` si faltan). Credenciales incorrectas: `401`.

### `GET /api/auth/me`
Autenticado. `200`: `{ "id", "nombre", "email", "rol" }` del usuario de la sesión.

### `GET /health`
Público. `200` si la base responde, `503` si no:

```json
{ "status": "ok", "db": { "connected": true, "latencyMs": 41 }, "timestamp": "2026-07-17T12:30:15.123Z" }
```

---

## Usuarios

### `GET /api/usuarios`
Autenticado. La respuesta depende del rol:

- `admin` y `super_admin`: lista completa de `UsuarioDto`.
- Los demás roles: solo usuarios activos con `{ id, nombre, rol }` (sirve para el selector de quién entrega y quién recibe).

`UsuarioDto`:

```json
{
  "id": "uuid", "nombre": "Ana Pérez", "email": "ana@empresa.com", "rol": "inspector",
  "subtipo_inspector": null, "activo": true,
  "created_at": "2026-07-17T12:30:15.123Z", "updated_at": "2026-07-17T12:30:15.123Z"
}
```

### `POST /api/usuarios`
Solo `super_admin`. `201` con el `UsuarioDto` creado.

| Campo | Obligatorio | Regla |
|---|---|---|
| `nombre` | sí | no vacío |
| `email` | sí | formato de correo; único |
| `rol` | sí | uno de los roles |
| `subtipo_inspector` | no | uno de los subtipos o `null` |
| `password` | sí | mínimo 8 caracteres (configurable) |

### `PATCH /api/usuarios/{id}`
Solo `super_admin`. Cambia únicamente los campos enviados: `nombre`, `email`, `rol`, `subtipo_inspector`, `password`,
`activo` (mismas reglas que al crear). `200` con el `UsuarioDto` actualizado.

### `DELETE /api/usuarios/{id}`
Solo `super_admin`. `200`: `{ "modo": "eliminado" }` (borrado real) o `{ "modo": "retirado" }` (baja lógica, porque tiene
historial). No permite eliminarse a sí mismo.

---

## Vehículos

### `POST /api/vehiculos/cargar`
Administración. Carga masiva (máximo **5000** filas), idempotente por VIN. Cada VIN queda con la ruta
Cargue → Transporte → Descargue. `201`.

```json
{
  "items": [
    { "vin": "9BWZZZ377VT004251", "marca": "Renault", "modelo": "Duster", "color": "Gris", "operacion": "nacional" }
  ]
}
```

| Campo | Obligatorio |
|---|---|
| `vin` | sí |
| `marca` | sí |
| `operacion` | sí (`importacion`, `exportacion` o `nacional`) |
| `modelo`, `color` | no |

Respuesta:

```json
{ "recibidos": 1, "creados": 1, "actualizados": 0, "fallidos": 0, "errores": [ { "vin": "…", "error": "…" } ] }
```

Los errores por fila no cancelan la carga: vienen en `errores` y suman a `fallidos`.

### `GET /api/vehiculos?vin=`
Autenticado. Autocompletado por fragmento de VIN o marca (máximo **8** resultados).

```json
[ { "vin": "9BWZZZ377VT004251", "marca": "Renault", "modelo": "Duster", "color": "Gris", "operacion": "nacional" } ]
```

### `GET /api/vehiculos/listado?tipo=&vin=&page=`
Autenticado. Listado paginado, **500** filas por página.

| Parámetro | Descripción |
|---|---|
| `tipo` | `movilizados` (por defecto), `pendientes` o `novedad`; otro valor da `400` |
| `vin` | filtro por VIN (opcional) |
| `page` | número de página desde 1 (si no es un entero, usa 1) |

```json
{
  "rows": [
    { "id": "uuid", "vin": "…", "marca": "Renault", "modelo": "Duster", "color": "Gris", "operacion": "nacional",
      "etapas": 3, "completas": 3, "novedades": 1 }
  ],
  "total": 120, "page": 1, "pageSize": 500
}
```

### `GET /api/vehiculos/{vin}/inventario`
Autenticado. Inventario completo de un VIN: etapas, y dentro de cada una sus inspecciones con hallazgos, evidencias y
transferencia.

```json
{
  "id": "uuid", "vin": "…", "marca": "…", "tipo_vehiculo": null, "modelo": null, "chasis": null, "color": null,
  "operacion": "nacional", "created_at": "…", "updated_at": "…",
  "etapas": [
    {
      "id": "uuid", "orden": 1, "proceso": "cargue", "requiere_inspeccion": true,
      "inspecciones": [
        { "id": "uuid", "etapa_id": "uuid", "usuario_id": "uuid", "momento": "informe", "fecha": "…", "lat": 6.2, "lng": -75.5,
          "estado": "cerrada", "conductor_nombre": null, "placa": null, "trailer": null, "notas": null,
          "created_at": "…", "updated_at": "…",
          "hallazgos": [ /* HallazgoDto */ ], "evidencias": [ /* EvidenciaDto */ ], "transferencia": null }
      ]
    }
  ]
}
```

### `GET /api/vehiculos/{vin}/inventario/pdf`
Autenticado. Devuelve el inventario en PDF (`Content-Type: application/pdf`, nombre `inventario-{vin}.pdf`). Es una
descarga binaria, no JSON; en errores sí devuelve el JSON de error. La solicitud necesita el header `Authorization`, así que
use `fetch` con el token y descargue el `blob` (un enlace directo no lo envía).

---

## Asignaciones y etapas

### `POST /api/asignaciones`
Administración. `201` con la asignación.

```json
{ "vehiculo_id": "uuid", "ninera": "ABC123", "conductor_id": "uuid" }
```

`vehiculo_id` y `ninera` (placa) son obligatorios; `conductor_id` es opcional.

```json
{ "id": "uuid", "vehiculo_id": "uuid", "ninera": "ABC123", "conductor_id": "uuid", "asignado_por": "uuid", "created_at": "…", "updated_at": "…" }
```

### `POST /api/etapas`
Administración. Define una etapa de la ruta de un VIN. `201`.

```json
{ "vehiculo_id": "uuid", "punto_custodia_id": "uuid", "orden": 1, "requiere_inspeccion": true }
```

`orden` es un entero ≥ 1; `requiere_inspeccion` es opcional (por defecto `true`; `false` significa que solo exige firma).
Un `orden` repetido responde `409`; un vehículo o punto inexistente, `404`.

```json
{ "id": "uuid", "vehiculo_id": "uuid", "punto_custodia_id": "uuid", "orden": 1, "requiere_inspeccion": true, "created_at": "…", "updated_at": "…" }
```

---

## Inspecciones (informes)

Flujo típico: crear la inspección (siempre en `borrador`) → subir fotos → agregar evidencias y hallazgos → firmar la
transferencia → cerrar.

### `POST /api/inspecciones`
Campo. `201` con la inspección. El usuario sale del token (no se envía).

```json
{
  "etapa_id": "uuid", "momento": "informe", "fecha": "2026-07-17T12:30:15.123Z",
  "lat": 6.2442, "lng": -75.5812,
  "conductor_nombre": "Juan", "placa": "ABC123", "trailer": "T-01", "notas": "…"
}
```

| Campo | Obligatorio | Regla |
|---|---|---|
| `etapa_id` | sí | UUID |
| `momento` | sí | `check_in`, `check_out` o `informe` |
| `fecha` | sí | ISO 8601 con zona horaria |
| `lat` / `lng` | no | −90 a 90 / −180 a 180 |
| `estado` | no | si se envía, solo `borrador`; **no puede crearse cerrada** |
| `notas` | no | máximo 1000 caracteres |
| `conductor_nombre`, `placa`, `trailer` | no | texto |

```json
{
  "id": "uuid", "etapa_id": "uuid", "usuario_id": "uuid", "momento": "informe", "fecha": "…", "lat": 6.2442, "lng": -75.5812,
  "estado": "borrador", "conductor_nombre": "Juan", "placa": "ABC123", "trailer": "T-01", "notas": "…",
  "created_at": "…", "updated_at": "…"
}
```

### `GET /api/inspecciones`
Autenticado. Listado de hasta **200** informes. Todos los filtros son opcionales y van en la consulta:

| Filtro | Valor |
|---|---|
| `vin` | VIN completo válido (17 caracteres) |
| `usuarioId` | UUID del inspector |
| `momento` | `check_in`, `check_out` o `informe` |
| `estado` | `borrador` o `cerrada` |
| `proceso` | `cargue`, `punto_origen`, `certificadora`, `punto_destino`, `transporte`, `descargue` o `concesionario` |
| `desde`, `hasta` | fecha ISO |

Un filtro mal formado responde `400` con `fieldErrors` por campo. Cada fila es el `InspeccionDto` más `vin` y `proceso`.

### `GET /api/inspecciones/{id}`
Autenticado. Informe completo para revisión (`InformeDetalleDto`): los datos de la inspección más el contexto y sus partes.

```json
{
  "id": "uuid", "etapa_id": "uuid", "usuario_id": "uuid", "momento": "informe", "fecha": "…", "lat": null, "lng": null,
  "estado": "cerrada", "conductor_nombre": null, "placa": null, "trailer": null, "notas": null, "created_at": "…", "updated_at": "…",
  "vin": "…", "marca": "…", "modelo": null, "proceso": "cargue", "requiere_inspeccion": true,
  "inspector_nombre": "Ana Pérez", "ninera": "ABC123",
  "hallazgos": [ /* HallazgoDto */ ],
  "evidencias": [ /* EvidenciaDto */ ],
  "transferencia": { /* TransferenciaDetalleDto o null */ }
}
```

La placa, el conductor y el trailer capturados en el informe tienen prioridad sobre los de la asignación.

### `POST /api/inspecciones/{id}/hallazgos`
Campo (creador de la inspección o administrador). `201` con el `HallazgoDto`.

```json
{ "parte_vehiculo": "Puerta delantera izquierda", "clase_dano": "rotura", "tamano_dano": "5_10cm", "coords_diagrama": { "x": 0.42, "y": 0.18 } }
```

| Campo | Obligatorio | Regla |
|---|---|---|
| `parte_vehiculo` | sí | máximo 80 caracteres |
| `clase_dano` | sí | catálogo `clase_dano` |
| `tamano_dano` | sí | catálogo `tamano_dano` |
| `coords_diagrama` | no | `x` e `y` entre 0 y 1 |

`HallazgoDto`:

```json
{ "id": "uuid", "inspeccion_id": "uuid", "parte_vehiculo": "…", "clase_dano": "rotura", "tamano_dano": "5_10cm",
  "coords_diagrama": { "x": 0.42, "y": 0.18 }, "created_at": "…", "updated_at": "…" }
```

### `POST /api/inspecciones/{id}/evidencias`
Campo (creador o administrador). Agrega una foto **ya subida** con `POST /api/uploads`. `201` con el `EvidenciaDto`.

```json
{ "hallazgo_id": "uuid", "storage_path": "3f2b…c1.jpg", "sello_fecha": "2026-07-17T12:30:15.123Z", "sello_lat": 6.24, "sello_lng": -75.58 }
```

| Campo | Obligatorio | Regla |
|---|---|---|
| `storage_path` | sí | el `storage_path` que devolvió la subida (nombre plano, sin carpetas) |
| `sello_fecha` | sí | ISO 8601 con zona horaria |
| `hallazgo_id` | no | UUID del hallazgo al que pertenece |
| `sello_lat` / `sello_lng` | no | −90 a 90 / −180 a 180 |

`EvidenciaDto`:

```json
{ "id": "uuid", "inspeccion_id": "uuid", "hallazgo_id": null, "sello_fecha": "…", "sello_lat": 6.24, "sello_lng": -75.58,
  "url": "/api/uploads/3f2b…c1.jpg", "created_at": "…", "updated_at": "…" }
```

### `POST /api/inspecciones/{id}/transferencia`
Campo. Firma de entrega y/o recibo. Puede firmarse en dos pasos (parcial) y se completa cuando están ambas. `201`.

```json
{ "entrega_usuario_id": "uuid", "recibe_usuario_id": "uuid", "firma_entrega": "<trazo SVG o ruta>", "firma_recibe": null }
```

| Campo | Obligatorio | Regla |
|---|---|---|
| `entrega_usuario_id`, `recibe_usuario_id` | sí | UUID, y deben ser usuarios distintos |
| `firma_entrega`, `firma_recibe` | no | texto de hasta 200 000 caracteres |

Una transferencia ya firmada responde `409`. Respuesta (`TransferenciaDto`, sin las firmas):

```json
{ "id": "uuid", "inspeccion_id": "uuid", "entrega_usuario_id": "uuid", "recibe_usuario_id": "uuid",
  "firmada": false, "firmada_at": null, "created_at": "…", "updated_at": "…" }
```

En el detalle del informe (`GET /api/inspecciones/{id}`) la transferencia llega como `TransferenciaDetalleDto`: los mismos
campos más `entrega_nombre`, `recibe_nombre`, `firma_entrega` y `firma_recibe`.

### `POST /api/inspecciones/{id}/cerrar`
Campo (creador o administrador). Cierra el informe; después ya no admite cambios. Sin cuerpo. `200`:

```json
{ "inspeccion": { /* InspeccionDto, estado "cerrada" */ }, "resumen": { "evidencias": 4, "hallazgos": 2 } }
```

Si no cumple los requisitos (evidencias, geolocalización…), responde `400` con la lista de requisitos incumplidos en
`details`. Si ya estaba cerrada, `409`; **un cliente que reintenta el cierre puede tomar ese `409` como éxito.**

---

## Fotos

### `POST /api/uploads`
Campo. Sube una foto en base64. `201`.

```json
{ "data": "<base64, con o sin prefijo data:image/jpeg;base64,>", "ext": "jpg" }
```

| Campo | Regla |
|---|---|
| `data` | obligatorio; imagen JPG o PNG válida (se verifica el contenido, no solo la extensión); máximo 10 MB por defecto |
| `ext` | opcional: `jpg`, `jpeg` o `png` |

```json
{ "storage_path": "3f2b9c6e-….jpg", "url": "/api/uploads/3f2b9c6e-….jpg" }
```

Use `storage_path` al crear la evidencia y `url` para mostrarla.

### `GET /api/uploads/{archivo}`
Sirve la imagen (`image/jpeg` o `image/png`). Por defecto es **público** (se puede usar directo en un `<img src>` con la URL
completa); si el despliegue configura la lectura privada, exige el header `Authorization`. Responde con caché larga.

---

## Sofasa

### `GET /api/sofasa/ordenes`
Autenticado. Hasta **500** órdenes, las más recientes primero. Filtros opcionales: `vin`, `estado`, `ciudad`, `desde`, `hasta`
(fechas ISO; una fecha inválida responde `400`).

```json
[
  {
    "id": "uuid", "source_id": 12345, "status_code": "…", "datetime_event": "2026-07-17T12:30:15.123Z",
    "orden_number": "…", "orden_sap": "…", "origin_cedi": "…", "vin": "…", "make": "…", "model_description": "…",
    "dealer_code": "…", "destination_city": "…", "destination_address": "…",
    "loading_date": null, "appointment": null, "loading_dock": null, "weight": 1250.5,
    "vehiculo_id": "uuid", "synced_at": "…", "created_at": "…", "updated_at": "…"
  }
]
```

### `POST /api/etl/sofasa/ordenes` *(solo el ETL; el frontend no lo usa)*
No usa el JWT de usuario: exige `Authorization: Bearer <token del ETL>` (el valor de `Etl__IngestToken`). Ingesta idempotente por
`source_id` (máximo 5000 órdenes por lote). Respuesta en `camelCase`:

```json
{ "recibidas": 2, "guardadas": 2, "vehiculosCreados": 1, "vehiculosActualizados": 1, "errores": [ { "source_id": 1, "error": "…" } ] }
```

---

## Reportes

Autenticados. Todos sus campos van en `camelCase`.

### `GET /api/patrones`
Hallazgos de unidades movilizadas por proceso y zona (mapa de calor).

```json
{
  "total": 40,
  "porProceso": [ { "proceso": "cargue", "total": 12 } ],
  "porZona": [ { "parte_vehiculo": "Puerta", "total": 9 } ],
  "porZonaProceso": [ { "proceso": "cargue", "parte_vehiculo": "Puerta", "total": 4 } ]
}
```

Las filas internas conservan `snake_case` (`parte_vehiculo`); lo que va en `camelCase` son las claves de primer nivel.

### `GET /api/patrones/analisis`
Clases y tamaños de daño, con sugerencias.

```json
{
  "porClase": [ { "clase_dano": "rotura", "total": 7 } ],
  "porTamano": [ { "tamano_dano": "5_10cm", "total": 5 } ],
  "sugerencias": [ "…" ]
}
```

### `GET /api/metricas/rechazo`
Tasa de rechazo (unidades movilizadas con al menos un hallazgo) frente a la línea base.

```json
{
  "tasaGlobal": 0.12, "totalMovilizadas": 100, "totalPendientes": 30, "totalConNovedad": 12, "totalNovedad": 12,
  "porOperacion": [ { "operacion": "nacional", "total": 60, "conNovedad": 6, "tasaActual": 0.1, "lineaBase": 0.08, "variacion": 0.02 } ]
}
```

---

## Notas para el consumo

- **Mismo origen y CORS.** El navegador solo podrá llamar a la API si su origen está en `Cors__Origins`.
- **Reintentos.** Reintente solo `401` (tras renovar sesión), `408`, `429` y `5xx`. No reintente `400`, `403`, `404` ni `409`
  (salvo el `409` al cerrar, ver arriba).
- **Cierre de sesión por inactividad y expiración.** Ante un `401` en una ruta protegida, borre el token y vaya al login.
- **IDs y enumeraciones.** Trate los valores de los catálogos como cadenas cerradas; un valor fuera del catálogo da `400`.
- **Este documento refleja el estado actual.** Si un endpoint cambia, se actualiza junto con `docs/openapi.json`.
