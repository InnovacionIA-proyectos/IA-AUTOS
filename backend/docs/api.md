# API

Todas las rutas, los metodos, los codigos HTTP y la forma de las respuestas conservan el contrato del sistema anterior, de modo que la app movil y el ETL ya desplegados siguen funcionando. Las diferencias intencionales estan en `docs/seguridad.md`.

Autenticacion: `Authorization: Bearer <token>` (JWT HS256). Los nombres de campo van en `snake_case`, salvo los de Reportes y la respuesta de la ingesta, que van en `camelCase` como antes. Las fechas son ISO 8601 UTC con milisegundos (`2026-07-17T12:30:15.123Z`) y las de entrada exigen zona horaria. La especificacion OpenAPI se publica en `/openapi/v1.json` cuando `Documentacion__Habilitada=true` (siempre en Development).

## Roles

| Grupo | Roles | Se usa en |
|---|---|---|
| Campo | inspector, conductor, admin | informes, hallazgos, evidencias, firmas, subida de fotos |
| Administracion | admin, super_admin | carga masiva de VIN, asignaciones, etapas |
| Gestion de usuarios | super_admin | alta, cambio y baja de usuarios |

## Endpoints

| Metodo y ruta | Acceso | Descripcion |
|---|---|---|
| POST `/api/auth/login` | publico, con limite de intentos | correo y contrasena; devuelve `{ token, usuario }` |
| POST `/api/auth/login-portal` | publico, con limite de intentos | inicio de sesion unico: recibe el arreglo del usuario del portal (usa solo el id y lo revalida en TI); devuelve `{ access, refresh, user }`. 403 `PORTAL_USER_NOT_LINKED` si no tiene acceso a IA Autos |
| POST `/api/auth/refresh` | publico | recibe `{ refresh }`; devuelve `{ access, refresh }` y rota el refresh (el anterior queda revocado) |
| POST `/api/auth/logout` | publico | recibe `{ refresh }`; revoca la sesion (refresh y access) y responde 204 |
| GET `/api/auth/me` | autenticado | datos del usuario actual |
| GET `/api/usuarios` | autenticado | administradores: datos completos; los demas roles: `id`, `nombre`, `rol` de usuarios activos |
| POST `/api/usuarios` | super_admin | crea usuario (`nombre`, `email`, `rol`, `password`) |
| PATCH `/api/usuarios/{id}` | super_admin | cambia solo los campos enviados (incluye `password` y `activo`) |
| DELETE `/api/usuarios/{id}` | super_admin | `{ modo: "eliminado" \| "retirado" }`; no permite auto-eliminacion |
| POST `/api/vehiculos/cargar` | administracion | hasta 5000 VIN con su ruta Cargue, Transporte, Descargue; idempotente |
| GET `/api/vehiculos?vin=` | autenticado | autocompletado por fragmento de VIN o marca (maximo 8) |
| GET `/api/vehiculos/listado?tipo=&vin=&page=` | autenticado | `tipo` = movilizados, pendientes o novedad; 500 por pagina |
| GET `/api/vehiculos/{vin}/inventario` | autenticado | etapas, inspecciones, hallazgos, evidencias y transferencia |
| GET `/api/vehiculos/{vin}/inventario/pdf` | autenticado | inventario en PDF (descarga) |
| POST `/api/asignaciones` | administracion | asigna un VIN a una ninera (placa) y opcionalmente a un conductor |
| POST `/api/etapas` | administracion | define un tramo de la ruta: `etapa_catalogo_id`, `punto_custodia_id` (lugar, opcional), `orden`, `requiere_inspeccion` |
| POST `/api/inspecciones` | campo | crea el informe de una etapa (siempre en borrador; el usuario sale del token) |
| GET `/api/inspecciones` | autenticado | listado (maximo 200) con filtros `vin`, `usuarioId`, `momento`, `estado`, `proceso`, `desde`, `hasta` |
| GET `/api/inspecciones/{id}` | autenticado | informe completo con firmas y nombres |
| POST `/api/inspecciones/{id}/evidencias` | campo, creador o administrador | agrega una foto ya subida |
| POST `/api/inspecciones/{id}/hallazgos` | campo, creador o administrador | agrega un hallazgo |
| POST `/api/inspecciones/{id}/transferencia` | campo | firma de entrega y/o recibo (parcial o completa) |
| POST `/api/inspecciones/{id}/cerrar` | campo, creador o administrador | cierra el informe; `{ inspeccion, resumen }` |
| POST `/api/uploads` | campo | sube una foto en base64 (JPG o PNG); `{ storage_path, url }` |
| GET `/api/uploads/{archivo}` | publico por defecto | sirve la imagen |
| POST `/api/etl/sofasa/ordenes` | token del ETL | ingesta idempotente por `source_id` |
| GET `/api/sofasa/ordenes` | autenticado | ordenes mas recientes primero (maximo 500) |
| GET `/api/catalogos` | autenticado | todos los catalogos maestros (etapas, lugares, clases y tamanos de dano, niveles, vistas, cuadrantes...) con version (`ETag`, `304` con `If-None-Match`) |
| GET `/api/catalogos/{nombre}` | autenticado | un catalogo con su version |
| POST `/api/catalogos/{nombre}`, PATCH `/api/catalogos/{nombre}/{id}`, DELETE `/api/catalogos/{nombre}/{id}` | admin, super_admin, coordinador_calidad | crea, edita o borra un valor; uno en uso no se borra (`409`), se inactiva |
| GET `/api/patrones` | autenticado | hallazgos por proceso y zona de unidades movilizadas |
| GET `/api/patrones/analisis` | autenticado | clases y tamanos de dano con sugerencias |
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
| NOT_FOUND | 404 | recurso o ruta inexistente |
| METHOD_NOT_ALLOWED | 405 | metodo no admitido en una ruta existente |
| CONFLICT | 409 | duplicados, inspeccion cerrada, transferencia ya firmada |
| RATE_LIMITED | 429 | demasiados intentos de login |
| INTERNAL_ERROR | 500 | error inesperado (sin detalles internos) |
| DB_UNAVAILABLE | 503 | reservado |

`details` en errores de validacion tiene la forma `{ "formErrors": [], "fieldErrors": { "campo": ["mensaje"] } }`; en el cierre de una inspeccion es la lista de requisitos incumplidos. La app movil clasifica por codigo HTTP: 401, 408, 429 y 5xx se reintentan; un 409 al cerrar se toma como exito.
