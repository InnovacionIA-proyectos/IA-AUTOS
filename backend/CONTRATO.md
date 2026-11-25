# Contrato de la API (para frontend)

Describe cómo consumir hoy los endpoints del backend. Es una guía de consumo; la fuente de verdad técnica es el contrato
OpenAPI ([docs/openapi.json](docs/openapi.json)), que la API genera y una prueba vigila para que no se desvíe. Con
`Documentacion__Habilitada=true` también se ve en `GET /swagger` (interfaz) y `GET /openapi/v1.json`.

> Esta es la referencia de la API. La especificación de negocio, las historias de usuario y el índice de la documentación por tema están en [../docs/README.md](../docs/README.md); la guía para pasar de la versión 1 a la 2 del contrato, en [docs/migracion-v1-v2.md](docs/migracion-v1-v2.md).

## Convenciones

| Tema | Regla |
|---|---|
| URL base | La del despliegue, sin barra final (por ejemplo `https://<dominio>`). Todas las rutas empiezan por `/api`, salvo `/health` |
| Formato | JSON (`Content-Type: application/json`), codificación UTF-8 |
| Nombres de campo | `snake_case` (`etapa_id`, `created_at`). **Excepciones en `camelCase`** (solo estas): el cuerpo de `login-portal` (lo fija el portal), la respuesta de la ingesta Sofasa y el endpoint `/api/metricas/rechazo` (los `/api/reportes/*` van en `snake_case`) |
| Fechas | ISO 8601 UTC con milisegundos: `2026-07-17T12:30:15.123Z`. **Las fechas que se envían deben llevar zona horaria** (`Z` o `+hh:mm`) |
| Identificadores | UUID (`string`), salvo el VIN (texto) y `source_id` de Sofasa (entero largo) |
| Nulos | Los campos opcionales sin valor llegan como `null` (nunca se omiten) |
| VIN | 17 caracteres alfanuméricos en mayúscula, sin las letras `I`, `O` ni `Q` (o un chasis de Sofasa de 6 a 20 alfanuméricos al cargar y buscar; ver «Paginación y filtros»); se normaliza a mayúscula y sin espacios |
| Listados | Por páginas: `?pagina=&tamano=&orden=campo:asc` → `{ items, total, pagina, tamano }` (50 por defecto, máximo 200). Ver «Paginación y filtros» |
| Imágenes | Se referencian por `url` **relativa** (`/api/uploads/<archivo>`): anteponga la URL base |
| CORS | La API solo acepta los orígenes **exactos** de `Cors__Origins` (sin comodín `*`), acepta las cabeceras `Content-Type`, `Authorization` y `X-Dispositivo-Id`, y expone `Content-Disposition` (nombre del PDF de inventario). La web debe estar en esa lista |
| Dispositivo | La app y la web pueden enviar `X-Dispositivo-Id` (identificador estable del dispositivo) en cualquier solicitud: queda en la auditoría junto con la IP. Sin esa cabecera se guarda el `User-Agent` |

## Autenticación

1. `POST /api/auth/login` (correo y contraseña) o `POST /api/auth/login-conductor` (cédula y PIN, para el personal de campo) devuelven un `token` (JWT HS256, vigencia de 30 días por defecto).
2. Cada solicitud protegida envía `Authorization: Bearer <token>`.
3. Si responde `401`, el token falta, es inválido o venció, o el usuario fue retirado: lleve al usuario al login.
4. Si responde `403`, el rol no alcanza, la inspección es ajena o el VIN, viaje o informe no es de uno de sus viajes (inspector y conductor). No es un problema de sesión.
5. Si responde `403` con `code: "PIN_CHANGE_REQUIRED"`, el PIN es temporal: lleve a la persona a cambiarlo con `POST /api/auth/cambiar-pin` (hasta entonces solo funcionan `cambiar-pin`, `me` y `logout`). El `token` sigue siendo válido: tras cambiar el PIN se usa el mismo.

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
| Administración | `admin`, `super_admin` | carga masiva de VIN |
| Gestión de viajes | `admin`, `super_admin`, `coordinador_calidad` | crear y administrar viajes, asignar VIN, cargar viajes por Excel |
| Gestión de usuarios | `admin`, `super_admin`, `coordinador_calidad` | alta, cambio, baja y PIN de usuarios, carga de conductores por Excel. Solo `super_admin` asigna o modifica el nivel `super_admin` y cambia el cliente de alguien |

**TI (`admin` y `super_admin`) revisa, ve todo y da soporte.** El `super_admin` no hace trabajo de campo: no corrige inspecciones ni registra informes, hallazgos, fotos o firmas (las rutas de campo son del `inspector`, el `conductor` y el `admin`, como siempre) y ningún permiso nuevo se le agrega. El `admin` conserva solo las rutas de campo que ya tiene (el grupo *Campo*).

"Autenticado" significa cualquier rol con sesión válida. Cada usuario pertenece a un **cliente** (empresa): quien no es `super_admin`
solo ve y administra a los usuarios de su cliente; el `super_admin` no tiene cliente y ve todos. `GET /api/auth/me` devuelve las
`capacidades` del usuario, que sirven para armar el menú según el rol.

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
| `FORBIDDEN` | 403 | rol insuficiente, inspección ajena, o VIN, viaje o informe fuera de los viajes del usuario |
| `PORTAL_USER_NOT_LINKED` | 403 | la persona del portal no tiene acceso a IA Autos (solo en `login-portal`) |
| `ETAPA_PREVIA_SIN_CIERRE` | 409 | la etapa anterior del VIN no está terminada: no tiene su llegada cerrada (`details.motivo: "etapa_previa_sin_cierre"`) o su transferencia no tiene tipo de cierre (`"transferencia_sin_cierre"`); no se puede crear la salida de la siguiente (`details.etapa_previa` dice cuál) |
| `LLEGADA_YA_CERRADA` | 409 | la llegada de Transporte de esa unidad ya estaba confirmada: es un éxito para un reintento (ver «Conductor en ruta») |
| `MOMENTO_NO_ADMITIDO` | 409 | el momento de la inspección no existe en esa etapa: la **salida del Descargue** ya no se crea ni se corrige (`details.motivo: "salida_descargue"`; ver `POST /api/inspecciones`) |
| `HALLAZGO_YA_REVISADO` | 409 | la novedad del conductor ya fue revisada: su zona, clase y nivel ya no se cambian (ver «Revisión de la novedad») |
| `UNIDAD_NO_SALE` | 409 | la unidad se marcó «no sale» del viaje: no admite novedades ni llegada |
| `CORRECCION_SOLO_AUTOR` | 403 | corregir una inspección que hizo otra persona sin ser administrador: la corrigen quien la hizo y los administradores (ver «Corregir una inspección») |
| `DESCARTE_SOLO_AUTOR_O_COORDINADOR` | 403 | descartar un informe en borrador que hizo otra persona sin ser el coordinador de calidad: lo descartan quien lo hizo y el coordinador; el administrador y el super administrador no (ver «Descartar un informe en borrador») |
| `INFORME_CERRADO_NO_SE_DESCARTA` | 409 | descartar un informe cerrado, o un borrador cuya transferencia ya tiene una firma: es evidencia y no se descarta; `details.motivo` dice `informe_cerrado` o `informe_con_firmas` (ver «Descartar un informe en borrador») |
| `INSPECCION_YA_FIRMADA` | 409 | alguna transferencia de la **etapa** (de la llegada o de la salida) ya tiene una firma o un cierre: ya no se corrige, sea quien sea quien lo intente (ver «Corregir una inspección») |

**El motivo fijo de los rechazos.** Los rechazos (`409`, `403` y `404`) de **viajes**, **usuarios**, **vehículos** y de la **descarga de la constancia** traen además, en `details.motivo`, un valor fijo en minúsculas (el mismo patrón de `ETAPA_PREVIA_SIN_CIERRE`): el portal y la app muestran el mensaje que corresponde a partir del
motivo y no de la frase, que puede cambiar. El `code` y el `message` no cambian. Algunos traen datos propios junto al motivo (por ejemplo el viaje que impide la baja de un carro).

| Motivo | Cuándo |
|---|---|
| `viaje_programado`, `viaje_en_curso`, `viaje_finalizado`, `viaje_cancelado` | el viaje, en ese estado, ya no admite el cambio (por ejemplo corregir uno `finalizado` o `cancelado`; la fecha y el lugar de cargue solo se corrigen programado) |
| `transicion_de_estado_invalida` | el viaje no puede pasar a ese estado |
| `viaje_no_existe`, `viaje_no_asignado`, `viaje_sin_unidades_del_cliente` | el viaje no existe, no es del conductor o inspector que lo pide, o no tiene unidades de su cliente |
| `viaje_con_inspecciones`, `vin_con_inspecciones` | el viaje o el VIN ya tiene inspecciones y no se elimina ni se quita |
| `codigo_mtrack_duplicado` | ya existe un viaje con ese código MTrack |
| `vin_ya_en_el_viaje`, `vin_en_otro_viaje_activo` | el VIN ya está en este viaje o en otro viaje programado o en curso |
| `vehiculo_no_existe`, `vin_fuera_del_viaje` | el vehículo no existe o su VIN no está en ese viaje |
| `hallazgo_de_version_reemplazada` | «no sale» con un hallazgo de una inspección que ya reemplazó una corrección cerrada |
| `cargue_firmado`, `sin_cupo`, `tiene_reemplazo` | «vuelve a salir» y «bajar del viaje»: la entrega del cargue ya se cerró, la niñera no tiene puesto libre o el carro ya tiene reemplazo (ver «Unidad que no sale, vuelve a salir y bajar del viaje») |
| `unidad_no_sale`, `unidad_bajada`, `motivo_no_aplica` | un carro que no sale o se bajó no se revisa ni se firma, y el motivo no aplica en el lugar de cargue |
| `capacidad_no_baja`, `posicion_ocupada`, `sin_segunda_inspeccion_en_curso`, `descargue_con_informe_cerrado`, `conductor_sin_permiso_de_excepcion` | reglas de la niñera, el destino y el cargue sin inspector del viaje |
| `cargue_incompleto`, `firma_por_ninera`, `acomodacion_cerrada` | el cargue por niñera: falta cerrar la llegada o la salida de algún carro, la firma carro por carro del cargue (se firma por niñera) y liberar un puesto con la acomodación cerrada (ver «Firma del cargue por niñera») |
| `otro_cliente` | se pidió lo de un cliente que no es el suyo |
| `vehiculo_en_viaje_activo` | el carro está en un viaje programado o en curso y no se da de baja (`details.viaje_id` y `details.codigo_mtrack` nombran el viaje) |
| `usuario_no_existe`, `usuario_propio`, `usuario_de_otro_cliente` | el usuario no existe (o es de otro cliente), o se intentó eliminar o desactivar el propio |
| `solo_super_admin_cambia_cliente`, `solo_super_admin_modifica_super_admin`, `solo_super_admin_asigna_super_admin` | solo el super administrador puede hacerlo |
| `rol_sin_permiso_de_puntos`, `punto_no_asignado` | los puntos del inspector |
| `inspeccion_no_existe`, `inspeccion_de_otro_cliente`, `inspeccion_sin_transferencia` | la descarga de la constancia: la inspección no existe, es de un VIN de otro cliente o no tiene transferencia |
| `URL_FIRMADA_INVALIDA` | 403 | la URL de una foto no tiene firma, la firma no coincide o ya venció (ver «URL firmada de una foto») |
| `PIN_CHANGE_REQUIRED` | 403 | el PIN fue asignado o reiniciado por un administrador: hay que cambiarlo (`POST /api/auth/cambiar-pin`) antes de usar el resto de la API |
| `ACCOUNT_LOCKED` | 423 | la cuenta está bloqueada por PIN errados seguidos; `details.bloqueado_hasta` (ISO 8601) dice hasta cuándo, o un administrador la desbloquea |
| `NOT_FOUND` | 404 | recurso o ruta inexistente |
| `METHOD_NOT_ALLOWED` | 405 | método no admitido en una ruta existente |
| `CONFLICT` | 409 | duplicados (por ejemplo un código MTrack repetido), inspección cerrada, transferencia ya cerrada (firmada, con reservas o con constancia de no firma), firmantes que no se pueden cambiar, un registro que es evidencia y no se puede borrar, viaje finalizado o cancelado |
| `RATE_LIMITED` | 429 | demasiados intentos de login (10 por minuto por IP en `login`, 60 en `login-conductor`; configurable) |
| `INTERNAL_ERROR` | 500 | error inesperado, sin detalles internos |
| `DB_UNAVAILABLE` | 503 | no se pudo verificar la identidad en la base de usuarios del portal (TI) |

`details` en un `VALIDATION_ERROR` tiene la forma:

```json
{ "formErrors": [], "fieldErrors": { "campo": ["mensaje"] } }
```

Al cerrar una inspección que no cumple los requisitos, `details` es la lista de requisitos incumplidos. Muestre `message`
al usuario y use `code` (no el texto) para decidir qué hacer.

## Paginación y filtros

Los listados de **vehículos** (`GET /api/vehiculos/listado`), **inspecciones** (`GET /api/inspecciones`), **viajes** (`GET /api/viajes`), **órdenes Sofasa** (`GET /api/sofasa/ordenes`) y **usuarios** (`GET /api/usuarios`) usan el mismo formato, igual que los de
hallazgos, notificaciones y auditoría: se piden por páginas y responden **un objeto** (ya no una lista ni `rows`/`page`/`pageSize`).

```json
{ "items": [ { "...fila..." } ], "total": 17342, "pagina": 1, "tamano": 50 }
```

| Parámetro | Descripción |
|---|---|
| `pagina` | número de página desde 1 (por defecto 1; un valor menor se toma como 1) |
| `tamano` | filas por página: **50 por defecto y máximo 200** (uno mayor se reduce a 200; ya no hay páginas fijas de 200 ni de 500). `total` cuenta todas las filas que cumplen los filtros |
| `orden` | `campo:asc` o `campo:desc` (sin sentido, `asc`). Cada listado admite solo los campos de la tabla de abajo; otro campo o un formato distinto responde `400` con el motivo en `fieldErrors.orden`. Sin `orden` cada listado usa el suyo |

Un valor de `pagina` o `tamano` que no es un número entero, un filtro o un orden mal formados responden `400` con `fieldErrors` por campo (todos los errores juntos). Las filas de una página siguen un orden estable: dos páginas seguidas no repiten ni saltan filas.

| Listado | Filtros (todos opcionales, en `snake_case`) | Campos de `orden` | Orden por defecto |
|---|---|---|---|
| `GET /api/vehiculos/listado` | `tipo` (`movilizados` por defecto, `pendientes` o `novedad`), `vin`, `operacion`, `marca`, `cliente_id`, `estado_unidad` (`activa` o `no_sale`), `activo` (`true` o `false`), `desde`, `hasta` | `vin`, `marca`, `modelo`, `color`, `operacion`, `activo`, `created_at` | `vin:asc` |
| `GET /api/inspecciones` | `vin`, `viaje_id`, `usuario_id`, `momento`, `estado`, `proceso`, `operacion`, `marca`, `cliente_id`, `origen_id`, `destino_id`, `desde`, `hasta` | `fecha`, `vin`, `momento`, `estado`, `proceso`, `version`, `created_at`, `cerrada_at` | `fecha:desc` |
| `GET /api/viajes` | `estado`, `codigo_mtrack`, `vin`, `conductor_id`, `inspector_id`, `cliente_id`, `origen_id`, `destino_id`, `desde`, `hasta` | `created_at`, `updated_at`, `codigo_mtrack`, `estado`, `placa`, `trailer` | `created_at:desc` |
| `GET /api/sofasa/ordenes` | `vin`, `estado` (el `status_code`), `ciudad`, `desde`, `hasta` | `fecha`, `vin`, `estado`, `ciudad`, `orden`, `marca`, `created_at` | `fecha:desc` (las sin fecha al final) |
| `GET /api/usuarios` | `q`, `rol`, `activo`, `cliente_id` | `nombre`, `email`, `rol`, `cedula`, `activo`, `created_at`, `ultimo_acceso` | `nombre:asc` |

Los nombres `codigoMtrack`, `conductorId`, `usuarioId`, `viajeId` y `clienteId` de los listados de viajes, inspecciones y usuarios **ya no se aceptan** (se ignoran como cualquier parámetro desconocido): use `codigo_mtrack`, `conductor_id`, `usuario_id`, `viaje_id` y `cliente_id`.
`desde` y `hasta` son fechas ISO con zona y acotan, según el listado, la fecha de la inspección (inspecciones), la creación del viaje (viajes), el evento de la orden (Sofasa) o el alta del vehículo (vehículos). `cliente_id` lo elige solo el `super_admin`: quien se limita a su cliente
recibe siempre el suyo y un `cliente_id` distinto responde `403`. `origen_id` y `destino_id` son los del viaje.

**Filtros de texto: exactos o parciales.** Cada filtro de texto dice cuál es, y el servidor lo normaliza (recorta los espacios de los bordes) para que no dependa de lo que mande cada pantalla. Un filtro vacío o solo con espacios **no filtra**, y cero coincidencias responde `200` con `0` elementos (nunca `400` ni `404`).

| Filtro | Búsqueda |
|---|---|
| `marca` (vehículos, informes, exportación de informes y reportes del tablero), `trailer` (reportes del tablero) | **parcial**: por contenido, sin distinguir mayúsculas («rena», «RENA» y « rena » encuentran los mismos Renault) |
| `estado` de `GET /api/sofasa/ordenes` (sobre el `status_code`) | **parcial**: «entreg» encuentra ENTREGADA y ENTREGA PARCIAL |
| `ciudad` de Sofasa, `codigo_mtrack` | parcial (como antes) |
| `vin` | un VIN de 17 caracteres, **exacto**; un chasis (de 6 a 20), por contenido (como antes) |
| `operacion`, `momento`, `estado` de un informe, `proceso`, `nivel` y los de catálogo | **exacto** (un valor de la lista) |

**Listas para los filtros.** Para que el portal ofrezca todos los valores de un filtro de texto:
- `GET /api/vehiculos/marcas`: las **marcas distintas** de los vehículos del cliente de quien pregunta, por orden alfabético y sin repetir (una lista de textos). Un cliente sin vehículos recibe `[]`.
- `GET /api/viajes/trailers`: los **trailers distintos** de los viajes del cliente, sin nulos ni vacíos y por orden alfabético.

Las dos las leen `coordinador_calidad`, `admin` y `super_admin` (`403` al resto); `cliente_id` solo lo elige el `super_admin` (sin él ve las de todos); los demás reciben las de su cliente y `403` si piden otro; un `cliente_id` que no es un UUID responde `400`. No traen ningún dato personal. Los estados de Sofasa no tienen lista aparte: son el catálogo `estados-sofasa`. Los nombres de las dos rutas están **a confirmar por backend** con el portal.

**VIN.** Un solo criterio para cargar y buscar un VIN: **17 caracteres alfanuméricos sin las letras `I`, `O` ni `Q`** (ISO 3779) **o un chasis de Sofasa de 6 a 20 caracteres alfanuméricos**; se normaliza a mayúscula y sin espacios en los bordes. Lo aplican la carga
(`POST /api/vehiculos/cargar`, por fila: una fila inválida responde `400`), el filtro `vin` de los listados de arriba y de sus exportaciones, y el autocompletado `GET /api/vehiculos?vin=` cuando el texto tiene 17 caracteres alfanuméricos. Un VIN de 17 caracteres se busca exacto; un chasis
(de 6 a 20), por contenido. Un valor que no cumple responde `400` con el motivo, por ejemplo: `VIN inválido: los de 17 caracteres son alfanuméricos y no llevan las letras I, O ni Q`. El autocompletado de Inventario sigue buscando por **fragmento** de VIN o de marca.

## Catálogos (valores permitidos)

| Catálogo | Valores |
|---|---|
| `rol` | `inspector`, `conductor`, `coordinador_calidad`, `comercial`, `admin`, `super_admin` |
| `subtipo_inspector` | `importacion`, `nacional`, `export_preembarque`, `export_recibido` |
| `operacion` (vehículo) | `importacion`, `exportacion`, `nacional` |
| `momento` (inspección) | `salida`, `llegada` (en pantalla: «inspección de salida» y «inspección de llegada») |
| `estado` (inspección) | `borrador`, `cerrada` |
| `tipo` (listado de vehículos) | `movilizados`, `pendientes`, `novedad` |

> **Catálogos administrables.** El `proceso` (etapa), los lugares, la `clase_dano`, el `tamano_dano`, los tipos de unidad, los niveles de severidad,
> las vistas de foto y los cuadrantes **no son listas fijas**: se leen de [`GET /api/catalogos`](#catálogos-maestros) y los administra TI o el
> coordinador de calidad. Los valores iniciales de `proceso` son `cargue`, `transporte` y `descargue`.

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
  "usuario": { "id": "uuid", "nombre": "Ana Pérez", "email": "ana@empresa.com", "rol": "inspector", "cedula": null, "debe_cambiar_pin": false }
}
```

Correo y contraseña son obligatorios (`400` si faltan). Credenciales incorrectas: `401`. Quien entra con cédula y PIN no puede
entrar por aquí (`401`), ni quien entra con correo y contraseña por `login-conductor`.

### `POST /api/auth/login-conductor`
Público, con límite de intentos. Para el conductor y el inspector que entran con **cédula y PIN** (pueden no tener correo).

```json
{ "cedula": "1.234.567", "pin": "482915" }
```

La cédula se normaliza (puntos y espacios fuera, mayúscula). `200`: igual que `login`, con `usuario.email` en `null` si no tiene correo y el
`rol` real (`conductor` o `inspector`):

```json
{
  "token": "eyJhbGciOi...",
  "usuario": { "id": "uuid", "nombre": "Luis Gómez", "email": null, "rol": "conductor", "cedula": "1234567", "debe_cambiar_pin": false }
}
```

- Cédula o PIN incorrectos (exista o no la cédula): `401` «Cédula o PIN incorrectos»; cédula o PIN vacíos: `400`; usuario retirado: `401`.
- **Bloqueo.** Cada PIN errado de una cuenta existente se cuenta y queda en la auditoría. Al llegar a **5 seguidos** (`Seguridad__PinMaxIntentos`) la
  cuenta se bloquea **15 minutos** (`Seguridad__PinBloqueoMinutos`): ese intento y los siguientes responden `423 ACCOUNT_LOCKED` con
  `details.bloqueado_hasta`, aunque el PIN sea el correcto. El bloqueo se levanta solo al vencer, con un ingreso correcto posterior o cuando un
  administrador usa `POST /api/usuarios/{id}/desbloquear`. Un ingreso correcto reinicia el contador.
- **PIN temporal.** Si `usuario.debe_cambiar_pin` es `true`, el PIN lo asignó o reinició un administrador: el `token` sirve, pero el resto de la
  API responde `403 PIN_CHANGE_REQUIRED` hasta que la persona llame a `cambiar-pin`.

### `POST /api/auth/cambiar-pin`
Autenticado (también con PIN temporal). Cambia el PIN de quien entra con cédula y PIN.

```json
{ "pin_actual": "482915", "pin_nuevo": "730194" }
```

`204` sin cuerpo. El PIN nuevo son **solo números, de 4 a 8 dígitos**, distinto del actual y no trivial (nada de `1111` ni `1234`): si no, `400` con
`details.fieldErrors.pin_nuevo`. Un `pin_actual` errado es `400` (`fieldErrors.pin_actual`) y **cuenta como intento fallido** (puede bloquear la cuenta, `423`).
Quien entra con contraseña no tiene PIN: `409`. Tras el cambio, `debe_cambiar_pin` pasa a `false` y el mismo `token` ya sirve para todo.

### `GET /api/auth/me`
Autenticado (también con PIN temporal). `200`: el perfil del usuario de la sesión, con su cliente y lo que puede hacer:

```json
{
  "id": "uuid", "nombre": "Luis Gómez", "email": null, "rol": "conductor", "subtipo_inspector": null,
  "cedula": "1234567", "tipo_credencial": "pin", "debe_cambiar_pin": false,
  "cliente": { "id": "uuid", "codigo": "sofasa", "nombre": "Sofasa" },
  "capacidades": ["ver_inspecciones", "ver_viajes", "ver_reportes", "registrar_inspecciones"]
}
```

`cliente` es `null` para el `super_admin`. `tipo_credencial` es `password` o `pin`. Las `capacidades` salen del rol (las mismas listas que
autorizan los endpoints), así que el menú nunca ofrece algo que la API rechace:

| Capacidad | Roles |
|---|---|
| `ver_inspecciones`, `ver_viajes` | todos |
| `ver_reportes` | `coordinador_calidad`, `comercial`, `admin`, `super_admin` |
| `registrar_inspecciones` | `inspector`, `conductor`, `admin` |
| `gestionar_viajes` | `admin`, `super_admin`, `coordinador_calidad` |
| `gestionar_usuarios` | `admin`, `super_admin`, `coordinador_calidad` |
| `administrar_catalogos` | `admin`, `super_admin`, `coordinador_calidad` |
| `cargar_vehiculos` | `admin`, `super_admin` |
| `gestionar_seguimiento` | `coordinador_calidad`, `comercial`, `admin`, `super_admin` |
| `ver_alertas` | `coordinador_calidad`, `admin`, `super_admin` (alertas preventivas y avisos operativos) e `inspector` y `conductor` (los avisos operativos que les tocan) |
| `ver_auditoria` | `admin`, `super_admin` |
| `exportar_soportes` | `coordinador_calidad`, `comercial`, `admin`, `super_admin` |

### Sesión del portal (inicio de sesión único)

Para quien ya inició sesión en el portal de administración: entra a IA Autos **sin una segunda pantalla de login**. Es el mismo
esquema de sesión que EmbarQA: el `access` y el `refresh` viajan **en el cuerpo** de las respuestas (no hay cookies, así que no
hace falta `credentials: 'include'`). Guárdelos con cuidado: el `refresh` dura 30 días.

| Tiempo | Valor por defecto |
|---|---|
| access token | 60 minutos |
| refresh token | 30 días |

#### `POST /api/auth/login-portal`
Anónimo, con límite de intentos por IP. El cuerpo es **el arreglo del usuario del portal tal cual**, sin objeto envolvente. Se
usa únicamente el primer elemento y, de él, **solo el `id`**: el servidor no cree el nombre ni los permisos (los puede editar el
navegador) y revalida el id contra TI.

```json
[ { "id": 10001, "nombreUsuario": "usuario.portal", "permisos": [ { "id": "53", "descripcion": "AdminIAAUTOS", "idOcion": "56", "opcion": "AdminIAAUTOS" } ] } ]
```

> **Excepción a la convención de nombres.** Este endpoint NO usa `snake_case`: lee los campos tal como los guarda el portal
> (`nombreUsuario`, `idOcion`) y **distingue mayúsculas**. Respete además estos tipos, porque la lectura es estricta y un tipo
> distinto responde `400`:
>
> | Campo | Tipo | Notas |
> |---|---|---|
> | raíz | arreglo | un objeto o un arreglo vacío responde `400` |
> | `id` (del usuario) | **entero** | número JSON, no texto ni decimal; obligatorio |
> | `nombreUsuario` | texto | se ignora para autorizar |
> | `permisos` | **arreglo** de objetos | no un objeto |
> | `permisos[].id` | **texto** (`"53"`) | no número |
> | `permisos[].descripcion`, `permisos[].opcion` | texto | se ignoran para autorizar |
> | `permisos[].idOcion` | **texto** (`"56"`) | el campo se llama `idOcion`, **sin la «p»** |
>
> Los campos que no están en la tabla se ignoran. Las respuestas sí usan `snake_case` como el resto de la API.

`200`:

```json
{ "access": "eyJhbGciOi...", "refresh": "kQ3x...", "user": { "id": "uuid", "nombre": "Juan Pérez", "email": "jperez@empresa.com", "rol": "admin" } }
```

La clave es **`user`** (no `usuario`, que es la de `POST /api/auth/login`). `rol` es uno de los valores del catálogo de roles.

- **Rol:** sale de TI. Quien tiene el rol o la opción de permiso `<Rol>IAAUTOS` entra con ese rol: `SuperAdminIAAUTOS`, `AdminIAAUTOS`,
  `CoordinadorCalidadIAAUTOS`, `ComercialIAAUTOS` o `InspectorIAAUTOS`. Con varios, gana el de mayor privilegio (en ese orden). El
  conductor no entra por el portal.
- **Primera vez:** si la persona no existe en IA Autos pero tiene un rol de IA Autos en el portal, se crea su cuenta con ese rol. Si ya
  existía una cuenta con el mismo correo, se vincula. El rol se sincroniza en cada ingreso.
- `403` con **`code: "PORTAL_USER_NOT_LINKED"`**: la persona no existe en el portal, no tiene ningún rol de IA Autos o fue retirada de
  IA Autos. **Es distinto de `FORBIDDEN`** (rol insuficiente): muestre la pantalla de acceso no habilitado, no un error genérico. El mensaje
  es siempre el mismo: *«Tu usuario no tiene acceso habilitado a IA Autos. Solicita el permiso al administrador del portal.»*
- `503` con `code: "DB_UNAVAILABLE"`: no se pudo verificar en TI. No se concede acceso; reintente en unos minutos.
- `400` si el cuerpo no respeta el formato de arriba. `429` si excede el límite de intentos.

#### `POST /api/auth/refresh`
Anónimo. Recibe `{ "refresh": "..." }` y responde `200` `{ "access": "...", "refresh": "..." }` (sin `user`). **Rota el refresh**: el
anterior queda revocado y usarlo otra vez responde `401`; si ya pasaron unos segundos desde que rotó, se toma como robo de sesión y se
**revoca la sesión completa** (también el access vigente). Guarde siempre el refresh nuevo.

- `401` si el refresh venció, fue revocado o no existe. Lleve al usuario de vuelta al portal.
- `400` si falta `refresh` o el cuerpo no es un objeto JSON.
- Renueve el access antes de que venza (o ante un `401` por expiración, una sola vez). Si dos pestañas refrescan a la vez con el mismo
  refresh, la segunda recibe `401` sin que se pierda la sesión: reintente una vez con el refresh que guardó la primera.

#### `POST /api/auth/logout`
Anónimo. Recibe `{ "refresh": "..." }` y responde `204` vacío. Revoca el refresh y, con él, el access de esa sesión: usarlos después
responde `401`. Si además envía el `Authorization: Bearer`, también se cierra la sesión de ese access (sirve para salir aunque ya no tenga
el refresh; en ese caso puede omitir el cuerpo). Siempre responde `204`, haya o no sesión. Cerrar una sesión no afecta a las otras del
mismo usuario.

#### Cuando expira la sesión del portal
Si el portal cierra su sesión, el módulo debe llamar a `logout`, borrar sus tokens, mostrar su pantalla de acceso con el mensaje y el
botón «Volver al portal», y no intentar `refresh`.

### `GET /health`
Público. `200` si la base responde, `503` si no:

```json
{ "status": "ok", "db": { "connected": true, "latencyMs": 41 }, "timestamp": "2026-07-17T12:30:15.123Z" }
```

---

## Usuarios

Un usuario entra de una de dos formas (`tipo_credencial`): con **correo y contraseña** (`password`, para todos los roles) o con **cédula y PIN**
(`pin`, solo `conductor` e `inspector`, sin necesidad de correo). El inspector que ya entraba con correo y contraseña lo sigue haciendo.

| Regla | Detalle |
|---|---|
| Con contraseña | el correo es obligatorio y único |
| Con PIN | la cédula es obligatoria; el correo es opcional (muchos pueden no tenerlo) y, si lo tienen, único |
| Conductor | siempre tiene cédula (con ella se le asignan viajes y entra a la app) |
| Quién administra | `admin`, `coordinador_calidad` y `super_admin`; cada uno solo a los usuarios de **su cliente** (los de otro cliente responden `404`) |
| Nivel `super_admin` | solo lo asigna o modifica un `super_admin` (`403` para los demás; un `super_admin` no existe para quien no lo es: `404`) |
| Cliente | al crear, el del que crea; solo el `super_admin` puede elegir otro (`cliente_id`) o cambiar el de alguien. El `super_admin` no tiene cliente |

### `GET /api/usuarios`
Autenticado. La respuesta depende del rol:

- `admin`, `coordinador_calidad` y `super_admin`: **una página** de `UsuarioDto` completos, ordenados por nombre. Filtros opcionales: `q` (texto en nombre,
  correo o cédula), `rol`, `activo` (`true` o `false`), `cliente_id` (solo `super_admin`; los demás siempre ven su cliente), `pagina` (desde 1), `tamano`
  (50 por defecto, máximo 200) y `orden` (`campo:asc|desc`; ver «Paginación y filtros»). Un filtro mal formado es `400`.
- Los demás roles: solo usuarios activos de su cliente con `{ id, nombre, rol }`, sin paginar (sirve para el selector de quién entrega y quién recibe).

```json
{ "items": [ { "...UsuarioDto..." } ], "total": 132, "pagina": 1, "tamano": 50 }
```

`UsuarioDto` (la lista reducida para los demás roles no trae nada de esto):

```json
{
  "id": "uuid", "nombre": "Luis Gómez", "email": null, "rol": "conductor",
  "subtipo_inspector": null, "cedula": "1234567", "activo": true,
  "tipo_credencial": "pin", "debe_cambiar_pin": true,
  "bloqueado_hasta": null, "ultimo_acceso_at": null, "cliente_id": "uuid",
  "created_at": "2026-07-17T12:30:15.123Z", "updated_at": "2026-07-17T12:30:15.123Z",
  "pin_temporal": null
}
```

`bloqueado_hasta` tiene fecha mientras la cuenta está bloqueada por PIN errados. `pin_temporal` solo viene en las respuestas que **generan** un PIN
(alta sin PIN, asignar o reiniciar sin `pin`) y se muestra **una única vez**: no se guarda en claro ni se puede volver a consultar.

### `POST /api/usuarios`
`admin`, `coordinador_calidad` y `super_admin`. `201` con el `UsuarioDto` creado.

| Campo | Obligatorio | Regla |
|---|---|---|
| `nombre` | sí | no vacío |
| `rol` | sí | uno de los roles |
| `password` | con contraseña | mínimo 8 caracteres (configurable). Si se envía, entra con correo y contraseña; si no, con cédula y PIN |
| `email` | con contraseña | formato de correo; único entre quienes lo tienen. Opcional con PIN |
| `cedula` | con PIN y para el conductor | de 4 a 20 letras, números o guion; se guarda sin puntos ni espacios y en mayúscula; **única** entre quienes la tienen (`409` si se repite) |
| `pin` | no | solo con PIN: de 4 a 8 dígitos, no trivial. Si no se envía se **genera uno de 6 dígitos** y viene en `pin_temporal`. Siempre nace temporal: la persona debe cambiarlo |
| `subtipo_inspector` | no | uno de los subtipos (solo se conserva en inspectores) |
| `cliente_id` | no | solo el `super_admin`; por defecto, el cliente de quien crea (o el primero activo) |

Sin contraseña, solo el conductor y el inspector pueden crearse (`400` para los demás roles, que necesitan contraseña). Errores `400` por campo en
`details.fieldErrors` (por ejemplo, conductor sin `cedula`).

### `GET /api/usuarios/{id}`
Un usuario por su identificador, con la **misma visibilidad por cliente que el listado** (la pantalla que abre un enlace filtrado por un usuario muestra su nombre sin traer la lista): `admin`, `coordinador_calidad` y `super_admin` reciben el `UsuarioDto` completo, igual que cada elemento de `GET /api/usuarios`; los demás roles reciben solo `{ id, nombre, rol }` y solo de un usuario **activo** de su cliente.
Un usuario de otro cliente (para quien no es `super_admin`, que ve todos) o inexistente responde `404` con `details.motivo: "usuario_no_existe"`, como si no existiera; sin token, `401`.

### `PATCH /api/usuarios/{id}`
Mismos roles. Cambia únicamente los campos enviados: `nombre`, `email`, `rol`, `subtipo_inspector`, `password`, `activo`, `cedula` y, solo `super_admin`,
`cliente_id`. Un campo desconocido es `400`.

- **`null` limpia** `subtipo_inspector` (y también `email` y `cedula`, siempre que el usuario conserve con qué entrar: el correo si entra con contraseña, la
  cédula si entra con PIN o es conductor). `""` equivale a `null` en `cedula` y `email`. `nombre`, `rol`, `password` y `activo` no admiten `null` (`400`).
- Al cambiar el `rol` a uno que no es `inspector`, el `subtipo_inspector` se limpia solo.
- Una `password` nueva pasa al usuario a entrar con contraseña (requiere correo) y descarta su PIN.
- No se puede desactivar el propio usuario (`409`).

`200` con el `UsuarioDto` actualizado.

### `DELETE /api/usuarios/{id}`
Mismos roles. `200`: `{ "modo": "eliminado" }` (borrado real) o `{ "modo": "retirado" }` (baja lógica, porque tiene
historial). No permite eliminarse a sí mismo.

### PIN y bloqueo

Mismos roles que arriba, sobre usuarios de su cliente. Todos devuelven `200` con el `UsuarioDto`.

| Ruta | Qué hace |
|---|---|
| `POST /api/usuarios/{id}/pin` | **Asigna o reinicia** el PIN. Cuerpo opcional `{ "pin": "730194" }`; sin él se genera uno temporal (`pin_temporal`, una sola vez). El usuario pasa a entrar con cédula y PIN (si tenía contraseña, se descarta), **debe cambiarlo antes de continuar** y se levanta su bloqueo. Exige que sea conductor o inspector con cédula (`400`) |
| `POST /api/usuarios/{id}/pin/forzar-cambio` | Obliga a cambiar el PIN en el próximo uso, sin cambiarlo (`400` si no entra con PIN) |
| `POST /api/usuarios/{id}/desbloquear` | Levanta el bloqueo por PIN errados antes de que venza |

### Carga de conductores por Excel

Igual que la carga de viajes: la web lee el Excel y envía las filas como JSON; **primero la vista previa** (valida todo y no guarda nada) y luego la
confirmación. Máximo 500 filas por carga. Mismos roles que la gestión de usuarios.

```json
{ "filas": [ { "fila": 2, "nombre": "Ana Ruiz", "cedula": "2.000.001", "email": "ana@empresa.com" } ] }
```

`fila` (opcional) es el número de fila del archivo, para mostrar el error donde está; `email` es opcional. Columnas del Excel esperadas: nombre, cédula, correo.

- `POST /api/usuarios/cargar/previsualizar` → `200`.
- `POST /api/usuarios/cargar` → `201`. Crea **un conductor con PIN temporal por cada fila válida**, en el cliente de quien carga; las filas inválidas no frenan a las demás.

```json
{
  "confirmado": true, "total": 3, "validas": 2, "invalidas": 1, "creados": 2,
  "filas": [
    { "fila": 2, "nombre": "Ana Ruiz", "cedula": "2000001", "valida": true, "errores": [], "usuario_id": "uuid", "pin_temporal": "730194" },
    { "fila": 4, "nombre": "Repetido", "cedula": "1000001", "valida": false, "errores": ["Ya existe un usuario con la cédula 1000001"], "usuario_id": null, "pin_temporal": null }
  ]
}
```

Una fila es inválida si falta el nombre o la cédula, la cédula o el correo no tienen formato válido, ya existen en la base o están repetidos en el archivo. Los
`pin_temporal` salen **una sola vez** en la respuesta de confirmar: entrégueselos a cada conductor (o descargue la lista) antes de cerrar la pantalla. Cada conductor
debe cambiar el suyo en su primer ingreso.

---

## Vehículos

### `POST /api/vehiculos/cargar`
Administración. Carga masiva (máximo **5000** filas), idempotente por VIN. Solo crea o actualiza el vehículo: **las etapas (Cargue → Transporte → Descargue) nacen
cuando el VIN se agrega a un viaje**, no aquí. `201`.

```json
{
  "items": [
    { "vin": "9BWZZZ377VT004251", "marca": "Renault", "modelo": "Duster", "color": "Gris", "operacion": "nacional" }
  ]
}
```

| Campo | Obligatorio |
|---|---|
| `vin` | sí: 17 caracteres alfanuméricos sin `I`, `O` ni `Q`, o un chasis de 6 a 20 alfanuméricos (ver «Paginación y filtros»); si alguna fila no cumple, la carga responde `400` con el motivo de esa fila |
| `marca` | sí |
| `operacion` | sí (`importacion`, `exportacion` o `nacional`) |
| `modelo`, `color` | no |

Respuesta:

```json
{ "recibidos": 1, "creados": 1, "actualizados": 0, "fallidos": 0, "errores": [ { "vin": "…", "error": "…" } ] }
```

Los errores por fila no cancelan la carga: vienen en `errores` y suman a `fallidos`.

Los vehículos **nuevos** quedan en el cliente de quien carga (el `super_admin`, que no tiene, usa el cliente por defecto); un VIN que ya existe conserva el suyo.

### `GET /api/vehiculos?vin=`
Autenticado. Autocompletado por fragmento de VIN o marca (máximo **8** resultados). Inspector y conductor solo encuentran los VIN de sus viajes. Un texto de **17 caracteres alfanuméricos** se toma como un VIN completo y debe cumplir la regla del VIN (sin `I`, `O` ni `Q`),
si no responde `400` con el motivo; un fragmento más corto o una marca se aceptan tal cual.

```json
[ { "vin": "9BWZZZ377VT004251", "marca": "Renault", "modelo": "Duster", "color": "Gris", "operacion": "nacional", "activo": true } ]
```

Un vehículo dado de baja (`activo: false`) **no aparece** en esta búsqueda.

### `GET /api/vehiculos/listado`
Autenticado. Listado paginado (`{ items, total, pagina, tamano }`, 50 por defecto, máximo 200; ver «Paginación y filtros» para `pagina`, `tamano` y `orden`). `movilizados` son los VIN que completaron su ruta (todas sus etapas con informe cerrado) en algún viaje; `pendientes`, el resto,
incluidos los que aún no están en un viaje. Inspector y conductor solo ven los VIN de sus viajes.

| Parámetro | Descripción |
|---|---|
| `tipo` | `movilizados` (por defecto), `pendientes` o `novedad`; otro valor da `400` |
| `vin` | VIN o chasis válido (ver «Paginación y filtros»); un VIN de 17 caracteres coincide exacto y un chasis, por contenido |
| `operacion` | `importacion`, `exportacion` o `nacional` |
| `marca` | la marca del vehículo |
| `cliente_id` | UUID del cliente (solo el `super_admin` elige otro; los demás reciben `403`) |
| `estado_unidad` | `activa` o `no_sale`: vehículos que tienen o tuvieron ese estado en algún viaje |
| `activo` | `true` o `false`: sin la baja lógica o dados de baja |
| `desde`, `hasta` | fecha ISO del alta del vehículo |

```json
{
  "items": [
    { "id": "uuid", "vin": "…", "marca": "Renault", "modelo": "Duster", "color": "Gris", "operacion": "nacional", "activo": true,
      "etapas": 3, "completas": 3, "novedades": 1, "sin_calificar": 0 }
  ],
  "total": 120, "pagina": 1, "tamano": 50
}
```

`novedades` cuenta los hallazgos **con nivel de la versión vigente** de cada informe del carro (los de una versión que una corrección cerrada ya reemplazó no cuentan: un carro con 4 daños en la versión 1 y 1 en la versión 2 devuelve 1) y `sin_calificar`, **aparte**, los de la versión vigente que aún no tienen nivel; un daño sin calificar pasa a `novedades` cuando se califica. Para el mismo carro y día coinciden con `sin_calificar` del indicador diario. El nombre `sin_calificar` es una propuesta, por confirmar.

**Cambio frente a la versión anterior:** el listado ya no responde `rows`, `page` ni `pageSize`, ni acepta `page`, ni trae páginas fijas de 500; usa `items`, `pagina` y `tamano` (50 por defecto, máximo 200).

### `GET /api/vehiculos/{vin}/inventario`
Autenticado. Inventario completo de un VIN: etapas (de todos sus viajes), y dentro de cada una sus inspecciones con hallazgos, evidencias y
transferencia. Un inspector o conductor recibe `403` si el VIN no está en sus viajes (`404` si no existe) y, del VIN, ve solo las etapas de sus viajes.

`novedades` y `sin_calificar` son las mismas dos cifras del listado de vehículos (versión vigente de cada informe; con nivel y sin nivel), sobre las etapas que se devuelven.

Cada etapa trae `posicion`: la posición (de 1 a la capacidad de la niñera de ese viaje) que tuvo el VIN en ella, o `null` si no se registró (los viajes anteriores al cambio y las unidades que no salen).

```json
{
  "id": "uuid", "vin": "…", "marca": "…", "tipo_vehiculo": null, "modelo": null, "chasis": null, "color": null,
  "operacion": "nacional", "activo": true, "created_at": "…", "updated_at": "…", "novedades": 1, "sin_calificar": 0,
  "etapas": [
    {
      "id": "uuid", "orden": 1, "proceso": "cargue", "lugar": "CEDI 1", "requiere_inspeccion": true,
      "viaje_id": "uuid", "codigo_mtrack": "MTK-001",
      "estado_unidad": "activa", "no_sale": null, "posicion": 4,
      "inspecciones": [
        { "id": "uuid", "etapa_id": "uuid", "viaje_id": "uuid", "usuario_id": "uuid", "momento": "salida", "fecha": "…", "lat": 6.2, "lng": -75.5,
          "estado": "cerrada", "conductor_nombre": null, "placa": null, "trailer": null, "notas": null,
          "created_at": "…", "updated_at": "…",
          "hallazgos": [ /* HallazgoDto */ ], "evidencias": [ /* EvidenciaDto */ ], "transferencia": null }
      ]
    }
  ]
}
```

`estado_unidad` es el de la unidad **en el viaje de esa etapa**: `activa` o `no_sale`. Cuando es `no_sale`, `no_sale` trae el detalle; si no, es `null`:

```json
"no_sale": { "motivo": "Golpe estructural en el frente", "hallazgo_id": "uuid", "marcado_por": "uuid", "marcado_por_nombre": "Ana Pérez",
             "fecha": "2026-07-17T12:30:15.123Z", "vin_reemplazo": "9BWZZZ377VT004252" }
```

`vin_reemplazo` es el VIN que reemplaza a la unidad, o `null` si aún no se asignó. Una unidad que no sale **conserva su vínculo con el viaje** y el inventario devuelve las etapas de ese viaje igual que las demás, con su historial completo (incluido el hallazgo que la sacó del viaje).

### `DELETE /api/vehiculos/{vin}`
Baja lógica. Solo `admin`, `super_admin` y `coordinador_calidad` (de un VIN de su cliente: `403` si es de otro). Responde `200` con `{ "modo": "inactivado" }`: el vehículo queda con `activo: false` y **no se borra**; conserva sus
etapas, inspecciones, hallazgos y evidencias.

- Un vehículo inactivo **no aparece** en `GET /api/vehiculos?vin=` y **no se puede agregar a un viaje** (`400` en `vines`).
- **Sigue apareciendo** en `GET /api/vehiculos/listado`, en el inventario (`GET /api/vehiculos/{vin}/inventario`), en los informes y en los reportes, con `activo: false`.
- Repetir la baja responde igual y no cambia nada. `404` si el VIN no existe; `403` para cualquier otro rol.
- **Un carro que está en un viaje programado o en curso no se da de baja**: `409` con `details.motivo: "vehiculo_en_viaje_activo"` y los datos del viaje (`details.viaje_id`, `details.codigo_mtrack`); el mensaje lo nombra («El vehículo … está en el viaje MT-2026-0417»). Se saca del viaje (o el viaje termina) y luego se da de baja; el carro sigue activo.
- Queda en la auditoría (`vehiculo.baja`, con `activo` antes y después).

### `POST /api/vehiculos/{vin}/reactivar`
Deshace la baja. Los mismos roles que la baja (`admin`, `super_admin` y `coordinador_calidad`, de un VIN de su cliente: `403` si es de otro; `403` para cualquier otro rol). Sin cuerpo. Responde `200` con `{ "modo": "reactivado" }`: el vehículo vuelve a `activo: true`, se busca de nuevo en `GET /api/vehiculos?vin=` y se puede agregar a un viaje; conserva todo su historial.
Reactivar uno que ya está activo responde igual y no cambia nada. `404` si el VIN no existe. Queda en la auditoría (`vehiculo.reactivar`, con quién y cuándo, y `activo` antes y después).

### `GET /api/vehiculos/{vin}/inventario/pdf`
Autenticado. Devuelve el inventario en PDF (`Content-Type: application/pdf`, nombre `inventario-{vin}.pdf`). Es una
descarga binaria, no JSON; en errores sí devuelve el JSON de error. La solicitud necesita el header `Authorization`, así que
use `fetch` con el token y descargue el `blob` (un enlace directo no lo envía).

El PDF trae el inventario (cada proceso con sus inspecciones, hallazgos y evidencias) **más las fotos de cada inspección con su sello** (fecha y hora, ubicación, sello del servidor y hash SHA-256) y, al final, los **paneles del dibujo de daños**
con la imagen del panel y cada zona con hallazgos resaltada y marcada con **cuántos hallazgos tiene**; las zonas sin dibujo en los paneles (el frente, las llantas) se listan aparte. Un vehículo dado de baja lo dice. Cada proceso trae «Posición en niñera: N» cuando el VIN fue despachado con posición registrada. Un PDF lleva hasta **60 fotos**
(primero las de los hallazgos): si hay más, el PDF avisa cuántas dejó fuera. Lo ve quien ve el inventario.

---

## Viajes

El **viaje es el eje del modelo**: una unidad transportadora (tipo, placa, tráiler) lleva uno o varios VIN con un conductor (y, si hay, un
inspector) entre un origen y un destino. Cada etapa e inspección cuelga de un viaje, y **inspectores y conductores solo ven lo de sus
viajes** (ver [Visibilidad por viaje](#visibilidad-por-viaje)). No hay integración con MTrack: el `codigo_mtrack` se digita o llega en el Excel.

Crear y administrar viajes es de `coordinador_calidad`, `admin` y `super_admin` (grupo *Gestión de viajes*); cualquier otro rol recibe `403`.
Las lecturas son para cualquier usuario autenticado, filtradas por la visibilidad.

Los VIN **no se crean aquí**: deben existir ya en el inventario (`POST /api/vehiculos/cargar` o la ingesta Sofasa). Al agregar un VIN a un viaje se
crean sus etapas, una por cada valor **activo** del catálogo `etapas` (hoy Cargue, Transporte y Descargue): el **Cargue** se hace en el
`origen` del viaje, el **Descargue** en el destino de la unidad y el Transporte no tiene lugar. El Cargue nace **sin ninguna marca previa**: la inspección de salida la hace siempre un inspector y, si no hay inspector,
el conductor activa en el momento la excepción «No hay inspector».

Estados: `programado` → `en_curso` → `finalizado`; un viaje `programado` o `en_curso` también puede pasar a `cancelado`. `finalizado` y `cancelado`
son finales: el viaje ya no admite cambios ni inspecciones nuevas. La primera inspección de un viaje `programado` lo pasa a `en_curso`. Un viaje
`cancelado` deja de ser visible para inspectores y conductores.

Un VIN puede estar en varios viajes a lo largo del tiempo, pero **no en dos viajes activos** (`programado` o `en_curso`) a la vez.

`ViajeDto`:

```json
{
  "id": "uuid", "codigo_mtrack": "MTK-001", "tipo_unidad_id": "uuid", "unidad": "Niñera", "placa": "ABC123", "trailer": "T-9",
  "conductor_id": "uuid", "conductor_nombre": "Carlos Gómez", "inspector_id": null, "inspector_nombre": null,
  "origen_id": "uuid", "origen": "CEDI 1", "destino_id": "uuid", "destino": "Concesionario",
  "estado": "programado",
  "vehiculos": [
    { "vehiculo_id": "uuid", "vin": "9BWZZZ377VT004251", "marca": "Renault", "modelo": "Duster", "color": "Gris", "operacion": "nacional",
      "etapas": 3, "completas": 0, "estado_unidad": "activa", "no_sale": null, "estado_transporte": "sin_registro", "novedades": 0,
      "lugar_destino_id": "uuid", "lugar_destino": "Concesionario Autolarte Medellín" }
  ],
  "clientes": [ { "id": "uuid", "codigo": "sofasa", "nombre": "Sofasa" } ],
  "created_at": "…", "updated_at": "…",
  "fecha_programada": "2026-11-03T11:00:00.000Z", "lugar_cargue_id": "uuid", "lugar_cargue": "CEDI Envigado",
  "capacidad": 9,
  "disposicion": { "puestos": [ { "numero": 1, "etiqueta": "1", "vin": "9FB45RFL8NM201001" }, { "numero": 2, "etiqueta": "2", "vin": null } ] },
  "fotos_acomodacion": [ { /* EvidenciaDto con es_acomodacion: true */ } ],
  "papel": null
}
```

**La niñera del viaje (capacidad y disposición).** `capacidad` es el número de **puestos** de la niñera de ESE viaje, de 1 a 20. Un viaje nuevo la toma de los `puestos` de su tipo de unidad (catálogo `tipos-unidad`; **7** si el tipo no los define) y se puede indicar otra
al crearlo (`capacidad` en `POST /api/viajes`) o cambiar después (`PATCH /api/viajes/{id}` con `capacidad`). Queda **guardada en el viaje**: cambiar luego los puestos de un tipo **no mueve ningún viaje ya creado**. Un viaje anterior a este cambio, sin capacidad guardada, **rige como de 7 puestos** y
su `capacidad` sale 7 (no se migra). `disposicion.puestos` es la **disposición como dato**: un puesto por cada número de 1 a la capacidad, en orden, con `numero` (el que se envía como posición), `etiqueta` (cómo se muestra; hoy el mismo número, y es donde cabrá otra numeración sin cambiar la forma) y `vin`
(la unidad que lo ocupa, o `null` si está libre; solo de las unidades que quien consulta puede ver). La app dibuja esos puestos tal como llegan y no lleva ningún esquema propio. `fotos_acomodacion` son las fotos de la acomodación del viaje (ver `es_acomodacion`), de la más antigua a la más reciente, para llegar a ellas
sin pasar por el inventario del VIN; es `[]` si no hay. `capacidad` y `disposicion` salen en el detalle del viaje (`ViajeDto`), no en el listado.

`fecha_programada` es el día y la hora en que se programó el cargue (UTC), `lugar_cargue_id` / `lugar_cargue` el lugar **real** del cargue (catálogo `lugares`) y `lugar_destino_id` / `lugar_destino`
el lugar de entrega de **cada unidad**; los tres pueden ser `null` en los viajes que no vienen de la carga por Excel. El listado trae `fecha_programada`, `lugar_cargue_id` y `lugar_cargue`.

**El punto tiene inspector.** El viaje (el detalle, `GET /api/viajes/mios` y el listado) trae `lugar_cargue_tiene_inspector` y cada entrega por punto trae `lugar_tiene_inspector`: **`true` si ese lugar tiene al menos un inspector activo asignado** (`POST /api/usuarios/{id}/puntos`) y `false` si no. Es **la misma regla con la que el servidor ya decide**: quién firma la entrega
(en un punto con inspector la firma él y la persona externa se rechaza con `400`; sin inspector, la firma esa persona), si el cargue sin inspector se activa en el momento o se pide con permiso (`403` en un punto con inspectores) y a quién se avisa; el dato no tiene una regla propia, así que **no puede discrepar** de esas decisiones. Un inspector **inactivo no cuenta**
y el dato sigue lo que asigna el coordinador: asignar o quitar el punto cambia el valor en la **siguiente lectura**. Un viaje sin lugar de cargue (anteriores a la programación) dice `false`, como el servidor lo trata. Lo lee quien ya puede leer ese viaje o esa entrega, ni más ni menos, y **no trae nombres ni identificadores de inspectores**. Es un campo nuevo que se suma: las respuestas anteriores se leen igual y los rechazos de la entrega, del cargue y del permiso no cambian.

`novedades_proceso` son las novedades de proceso de las unidades del viaje (la excepción «No hay inspector» que activó el conductor y el recibo sin entrega), de la más antigua a la más reciente, cada una con `{ tipo, motivo_codigo, motivo, texto, usuario_nombre, fecha, vin, inspeccion_id }`: es un registro aparte, que no cuenta en los patrones por zona ni en la tasa diaria de novedad.
`codigo_mtrack`, `tipo_unidad_id` y `placa` pueden ser `null` solo en los viajes migrados desde las asignaciones anteriores. `etapas` y `completas`
de cada VIN cuentan sus etapas del viaje y las que ya tienen informe cerrado. `estado_unidad` es `activa` o `no_sale` y `no_sale` trae el motivo, el hallazgo, quién la marcó y
cuándo (el mismo objeto del inventario); una unidad que no sale **sigue en la lista de VIN del viaje**. `estado_transporte` (`sin_registro`, `con_novedad`, `sin_novedad` o `llegada_con_novedad`) y `novedades`
dicen cómo va la unidad en su recorrido según lo que registra el conductor (ver [Conductor en ruta](#conductor-en-ruta)).

**Clientes del viaje.** El viaje no tiene un cliente propio: un viaje puede llevar VIN de más de una marca, así que su cliente se toma de sus VIN. `clientes` es la lista, sin
repetidos y por nombre, de los clientes de los VIN del viaje, con el mismo objeto que `cliente` en `GET /api/auth/me` (`id`, `codigo`, `nombre`). Va en `ViajeDto` y en `ViajeResumenDto`
y es `[]` si el viaje aún no tiene VIN. Los indicadores por cliente o marca se cuentan por vehículo, no por viaje.

**Qué VIN ve cada quien.** `vehiculos`, `clientes` y, en el listado, `total_vehiculos` y `vehiculos_completos` se calculan sobre los VIN que quien consulta puede ver:

| Quien consulta | Ve |
|---|---|
| El conductor y el inspector del viaje | todos los VIN del viaje y todos sus clientes |
| El super administrador | todos los VIN y todos los clientes |
| Cualquier otro rol (coordinador, administrador, comercial…) | solo los VIN de **su cliente** y solo su cliente en `clientes` |

### `POST /api/viajes`
Gestión de viajes. `201` con el `ViajeDto`.

```json
{
  "codigo_mtrack": "MTK-001", "tipo_unidad_id": "uuid", "placa": "ABC123", "trailer": "T-9",
  "conductor_id": "uuid", "inspector_id": null,
  "fecha_programada": "2026-11-18T06:00:00-05:00", "lugar_cargue_id": "uuid",
  "unidades": [
    { "vin": "9BWZZZ377VT004251", "lugar_destino_id": "uuid" },
    { "vin": "9BWZZZ377VT004252", "lugar_destino_id": "uuid" }
  ]
}
```

El destino es de **cada unidad**: el viaje ya no lleva un destino único. **Todo campo que esta solicitud no declara se rechaza** con `400` «Campo no admitido» en ese campo (por ejemplo `destino_id`, `vines`, `origen_id` o `con_personal_apoyo`),
igual que al corregir: ninguno se ignora en silencio ni se guarda.

| Campo | Obligatorio | Regla |
|---|---|---|
| `codigo_mtrack` | sí | letras, números y `. _ - /`, máximo 50; **único** (sin distinguir mayúsculas) |
| `tipo_unidad_id` | sí | `id` de un valor **activo** del catálogo `tipos-unidad` |
| `placa` | sí | máximo 50 caracteres |
| `trailer` | no | máximo 50 caracteres |
| `conductor_id` | no | usuario activo con rol `conductor` |
| `inspector_id` | no | usuario activo con rol `inspector` |
| `fecha_programada` | sí | fecha y hora ISO 8601 **con zona horaria** (por ejemplo `2026-11-18T06:00:00-05:00`); sin ella `400` «La fecha programada es obligatoria» en `fecha_programada`; una fecha sin zona, `400` |
| `lugar_cargue_id` | sí | `id` de un lugar **activo** del catálogo `lugares`; sin él `400` «El lugar de cargue es obligatorio» en `lugar_cargue_id`; un lugar inexistente o inactivo, `400` |
| `capacidad` | no | los puestos de la niñera, de 1 a 20 (`400` en `capacidad` fuera de rango); sin ella toma los `puestos` del tipo de unidad (o 7) |
| `unidades` | no | hasta 500; cada una con su `vin` (que ya exista) y su `lugar_destino_id` (obligatorio: un lugar **activo** del catálogo `lugares`); sin VIN repetidos |

**El tipo de origen no se envía: se deduce.** `origen_id` del viaje (y `origen` en la respuesta) es el tipo (`puntos-custodia`) del lugar de cargue; el Cargue de cada unidad se crea en ese tipo. Lo mismo vale para el viaje que nace de la carga por archivo
(el archivo trae el lugar de cargue por nombre, no su tipo): el tablero lo cuenta bajo ese tipo y no bajo «SIN PUNTO». Los viajes anteriores a este cambio, sin fecha ni lugar de cargue, se siguen leyendo y corrigiendo igual.

El Descargue de cada unidad se crea en el tipo (`puntos-custodia`) de **su** lugar de destino.

Errores: `409` si el `codigo_mtrack` ya existe (el viaje **no se crea**) o si algún VIN ya está en otro viaje activo; `400` con `details.fieldErrors` si un
campo es inválido, una referencia no existe, está inactiva o es de otro rol, o alguna unidad no existe (`unidades`). Una unidad sin destino responde `400` con
`fieldErrors.unidades`: «La unidad requiere un destino»; un lugar que no existe o está inactivo, o un VIN con dos destinos distintos, también. Todo o nada: si algo falla no se crea el viaje ni
ninguna etapa.

### `GET /api/viajes`
Autenticado. Una **página** de viajes (`{ items, total, pagina, tamano }`; 50 por defecto, máximo 200; ver «Paginación y filtros»), los más recientes primero, con cada fila como `ViajeResumenDto` (los campos del viaje sin `vehiculos`, más `total_vehiculos` y
`vehiculos_completos`: cuántos VIN ya completaron su ruta en el viaje). Filtros opcionales en la consulta: `estado`, `codigo_mtrack` (fragmento),
`vin` (VIN o chasis válido), `conductor_id`, `inspector_id`, `cliente_id`, `origen_id`, `destino_id`, `destino_unidad_id`, `desde`, `hasta` (fecha ISO sobre la creación); además `pagina`, `tamano` y `orden`. Un filtro mal formado responde `400` con `fieldErrors`. Inspectores y
conductores solo reciben sus viajes. Cada fila trae `clientes` (ver «Clientes del viaje»).

**Dos destinos distintos.** `destino_id` es el tipo de destino **del viaje** (`puntos-custodia`), un dato de los viajes anteriores: los viajes nuevos no lo tienen porque el destino es de cada unidad. `destino_unidad_id` es un **lugar** del catálogo `lugares` y trae los viajes que llevan
**al menos una unidad** a ese lugar; el viaje cuenta **una sola vez** (y una sola vez en `total`) aunque varias de sus unidades vayan al mismo lugar. Cada fila trae `destinos`: los destinos **distintos** de sus unidades, con cuántas van a cada uno, en la misma respuesta (no hace falta una petición por fila):

```json
"destinos": [ { "lugar_id": "uuid", "lugar": "Concesionario Norte", "unidades": 5 }, { "lugar_id": "uuid", "lugar": "Concesionario Sur", "unidades": 3 } ]
```

Van ordenados por nombre; las unidades sin destino (viajes anteriores) salen al final con `lugar_id` y `lugar` en `null`. Quien se limita a su cliente solo cuenta las unidades de su cliente. `lugar_cargue` sigue siendo el origen que muestra la fila.

`cliente_id` (uuid) trae los viajes que tienen **al menos un VIN de ese cliente**: un viaje con VIN de dos clientes aparece al filtrar por cualquiera de los dos. Un cliente sin
viajes o que no existe da una página vacía; un valor que no es uuid, `400`. **Solo el super administrador elige el cliente**: quien no lo es siempre recibe los viajes de su cliente
(con o sin `cliente_id`; si pide otro, `403`), igual que en `GET /api/usuarios`. Un viaje **sin VIN** todavía no tiene cliente: lo ven los de su cliente
si lo creó alguien de su cliente (así quien lo crea lo encuentra antes de asignarle unidades) y el super administrador, pero no sale al filtrar por `cliente_id`.

### `GET /api/viajes/mios`
Autenticado. Los viajes del usuario (donde es conductor, inspector del viaje o, si es inspector, **donde tiene un reclamo activo de entrega** de alguna parada) **con sus VIN**, como `ViajeDto`. Sin parámetros trae los `programado` y `en_curso`;
con `?estado=` trae los de ese estado (`finalizado`, por ejemplo). Es la entrada de «Mis viajes» en la app. Un viaje sale **una sola vez** aunque el inspector tenga el reclamo de varias paradas, y deja de salir en la consulta siguiente a liberar su último reclamo de entrega,
salvo que además sea su inspector del viaje (el que cargó recibe solo si está asignado a esa parada). El reclamo del cargue no cambia nada: reclamarlo ya lo vuelve inspector del viaje.

**El papel del inspector.** Cada viaje de `mios` y el detalle del viaje traen `papel`, que dice qué hace en él **quien consulta si es inspector**: `carga` (es el inspector del viaje o tiene el reclamo del cargue), `recibe` (solo tomó la entrega de una o más paradas) o `carga_y_recibe`
(el inspector del viaje que además tomó la entrega de una parada suya). Es `null` para el conductor y para los roles de oficina (coordinador, comercial, administrador y super administrador) y en los viajes donde un inspector no hace ninguna de las dos cosas. Es un campo nuevo: ninguno de los anteriores cambia.
Los nombres (`papel` y sus tres valores) son propuestos y quedan **por confirmar por backend**.

### `GET /api/viajes/{id}`
Autenticado. El `ViajeDto`. `403` si es un inspector o conductor y el viaje no es suyo, o si el rol no es el super administrador y el viaje no tiene unidades de su cliente;
`404` si no existe. Los VIN y los clientes que trae dependen de quien consulta (ver «Qué VIN ve cada quien»).

### `GET /api/viajes/{id}/firmantes`
Autenticado. Los usuarios del viaje que pueden firmar una transferencia: su **conductor** y su **inspector** activos, como lista de `{ id, nombre, rol }`. Es la lista de la que la app elige
quién entrega y quién recibe. `403` si es un inspector o conductor y el viaje no es suyo; `404` si no existe.

### `PATCH /api/viajes/{id}`
Gestión de viajes. Cambia **solo los campos enviados** de un viaje `programado` o `en_curso`; responde `200` con el `ViajeDto`. Campos: `codigo_mtrack`,
`tipo_unidad_id`, `placa`, `trailer`, `conductor_id`, `origen_id`, `fecha_programada`, `lugar_cargue_id` y `capacidad` (mismas reglas que al crear). **La capacidad no baja por debajo de las unidades activas del viaje ni de la posición más alta ya usada**: `409` diciendo cuál lo impide («No se puede bajar la capacidad a 8: hay una unidad en el puesto 9 en el viaje»,
«…: hay 9 unidades…» o las dos); `400` en `capacidad` si no es un entero de 1 a 20 o es `null`.
`trailer`, `conductor_id` y `origen_id` admiten `null` para quitarlos; `inspector_id` **ya no se acepta** (lo llena el reclamo del cargue: ver «Reclamo del camión»); `destino_id` **ya no se acepta** (el destino es de cada unidad: ver `PATCH /api/viajes/{id}/vehiculos/{vin}`); `con_personal_apoyo` **ya no existe** (el cargue no lleva ninguna marca previa); `codigo_mtrack`, `tipo_unidad_id` y `placa` no admiten `null`. Un campo que no existe responde `400`; un `codigo_mtrack` repetido, `409`; un viaje finalizado o cancelado, `409`.

Mientras el viaje está `programado` también se corrige su programación: `fecha_programada` (ISO 8601 **con zona horaria**, por ejemplo `2026-11-03T06:00:00-05:00`; `null` la quita) y
`lugar_cargue_id` (un lugar **activo** del catálogo `lugares`; `null` lo quita). Una fecha sin zona o un lugar inexistente o inactivo responde `400` con `details.fieldErrors`;
en un viaje `en_curso` cambiarlos responde `409` (lo demás del viaje sí se sigue editando).

Al cambiar `lugar_cargue_id` el **tipo de origen** (`origen_id`) se recalcula con el tipo del lugar nuevo (y manda sobre un `origen_id` enviado en la misma solicitud). Al cambiar el tipo de origen se actualiza el lugar del Cargue de los VIN del viaje; las etapas que
ya tienen un informe cerrado no cambian.

### `PATCH /api/viajes/{id}/estado`
Gestión de viajes (`coordinador_calidad`, `admin` y `super_admin`; el inspector y el conductor reciben `403`). Cuerpo `{ "estado": "en_curso", "motivo": "…" }`. `200` con el `ViajeDto`. Un estado desconocido responde `400`; un cambio que el ciclo no permite
(por ejemplo `programado` a `finalizado`, o salir de `finalizado`), `409`. Cancelar o finalizar a mano un viaje **anula** sus esperas del inspector del punto abiertas (estado `anulada`; ver «La espera del inspector del punto»).

**Finalizar a mano con carros pendientes pide un motivo.** `motivo` (1 a 300 caracteres, largo por confirmar) es **obligatorio** cuando el viaje está `en_curso` y se pasa a `finalizado` con al menos un carro activo sin entregar (sin la revisión de llegada del Descargue cerrada o sin su entrega cerrada, o con la entrega rechazada): sin él, `400` con `fieldErrors.motivo` «El motivo es obligatorio para finalizar un viaje con carros pendientes»; un motivo en blanco cuenta como no enviado y uno de más de 300 caracteres responde `400`. Sin carros pendientes el motivo es opcional, y en cualquier otro cambio de estado (por ejemplo cancelar) tampoco se pide. Cuando llega, queda en la auditoría (`viaje.estado.{estado}`) junto con el estado de antes y el de después (`despues.motivo`). Los roles no cambian: ni el administrador ni el super admin reciben ninguna ruta ni permiso nuevo. Lo normal sigue siendo que el viaje se finalice solo al entregar el último carro (ver «Entrega por parada»).

### `POST /api/viajes/{id}/vehiculos`
Gestión de viajes. Asigna unidades a un viaje activo y crea sus etapas, con el Descargue en el destino de cada una. Cuerpo `{ "unidades": [ { "vin": "…", "lugar_destino_id": "uuid" } ] }` (de 1 a 500; `vines` **ya
no se acepta**). `200` con el `ViajeDto`. **Todo o nada:** `400` si alguna unidad no trae destino («La unidad requiere un destino»), si el lugar no existe o está inactivo o si algún VIN no existe; `409` si alguno ya está
en este viaje o en otro viaje activo (el mensaje dice cuáles). Una unidad que reemplaza a otra que no sale hereda el destino de la reemplazada.

### `PATCH /api/viajes/{id}/vehiculos/{vin}`
Gestión de viajes. Cambia el destino de **una** unidad: cuerpo `{ "lugar_destino_id": "uuid" }` (un lugar **activo** del catálogo `lugares`; `400` si falta). Solo se mueve el Descargue de esa unidad al tipo del lugar nuevo; las demás
unidades del viaje no se tocan. `200` con el `ViajeDto`. `404` si el VIN no está en el viaje; `409` si el viaje no está activo, si la unidad no sale del viaje o si su Descargue ya tiene un informe cerrado.

### Reclamo del camión
El inspector reclama el camión que va a revisar («revisar este»). Los **puntos** de cada inspector (lugares del catálogo `lugares`) los asignan el coordinador de calidad y los administradores desde el portal (ver «Puntos del inspector»); el inspector solo reclama lo de sus puntos y, asignado un punto, ve sus pendientes sin otro paso.

- `POST /api/viajes/{id}/reclamos` con `{ "proceso": "cargue" | "entrega", "lugar_id"?, "lat"?, "lng"?, "precision"? }` (solo `inspector`) → `201` con el `ReclamoDto`, o `200` si el reclamo ya era suyo. `cargue`: un cargue **programado para hoy** (hora de Colombia) en uno de sus puntos (`lugar_cargue_id`); el inspector queda como
  **inspector del viaje** (de ahí salen los firmantes de la transferencia del cargue) y el reclamo **no cambia**. `entrega`: el reclamo es **por parada** (un lugar de destino del viaje): **`lugar_id` es obligatorio** (`400` sin él), debe ser **uno de sus puntos** (`403`
  con el motivo `no_es_su_punto` si no) y tener **carros activos por entregar** de ese viaje (`404` si no hay). Hay **un solo reclamo activo por viaje, proceso y parada**: otra parada del mismo viaje la toma su inspector, y un segundo inspector de la misma parada recibe `409` con el
  motivo `entrega_tomada` y el mensaje de siempre («Ya fue tomado por otro inspector»). Si dos lo intentan a la vez gana exactamente uno (lo decide un índice único de la base) y el otro recibe ese `409`. Quien reclama una parada **recibe la entrega de esa parada** y solo puede firmar esa
  (ver «Entrega por parada»); el reclamo de una parada no da acceso a firmar otra (`403`). `400` con un `proceso` inválido; `403` si el viaje no es de ninguno de sus puntos; `409` si el viaje no está activo o el cargue no es de hoy («El cargue no está programado para hoy»).
  **La posición del inspector al tomar la entrega** (`lat` y `lng` juntas, de -90 a 90 y de -180 a 180, y `precision` en metros, mayor o igual a 0; las tres son opcionales y `400` con la que no cumple, sin crear el reclamo). Con `proceso = entrega`, posición enviada y un lugar de destino con **ubicación confirmada**, el servidor compara la distancia con el radio del lugar (`radio_metros`, o el parámetro `radio_destino_m`) con **la misma regla de las fotos** (`valvula_precision_m` incluida; ver «Ubicación con rango y confirmación en sitio»). Si queda **fuera de rango** emite el aviso `fuera_de_rango` (urgencia `alta`, bandeja y push, al `coordinador_calidad` del cliente) con el título «Fuera del rango: {lugar}», el mensaje con el viaje, quién tomó la entrega, la distancia y el rango, y `datos`: `hecho` (`reclamo_entrega`, nombre por confirmar), `viaje_id`, `lugar_id`, `lugar`, `usuario_nombre`, `distancia_m`, `radio_m` y `precision_m`; uno por reclamo. **Nunca bloquea**: el reclamo queda creado en todos los casos. Sin posición, sin ubicación confirmada del lugar o con una precisión que llega a la válvula no se evalúa ni se avisa. El resultado (`confirmacion_sitio`) queda en la auditoría del reclamo (`viaje.reclamar`). El reclamo del cargue no cambia aunque traiga posición.
- `DELETE /api/viajes/{id}/reclamos/{proceso}` → `200` con el reclamo ya liberado; **la entrega se libera por parada con `?lugar_id=`** (`400` sin él). Lo libera el inspector que lo reclamó, el coordinador de calidad o un administrador (`403` cualquier otro; el coordinador y el administrador, solo de viajes de su cliente). El viaje queda sin inspector (si era el del
  cargue), el cargue vuelve a estar pendiente y lo que el inspector capturó (inspecciones cerradas, hallazgos) **conserva su autor**. `404` si no hay un reclamo activo. **No hay caducidad automática**: ningún proceso libera reclamos por tiempo.
- `GET /api/viajes/{id}/reclamos` → lista de `ReclamoDto`, del más reciente al más antiguo (activos y liberados): `{ id, viaje_id, proceso, inspector_id, inspector_nombre, reclamado_at, activo, liberado_at, liberado_por, liberado_por_nombre, lugar_id, lugar }`. `lugar_id` y `lugar` son la parada de un reclamo de entrega y son `null` en el del cargue. Es donde queda registrado quién liberó. Conductor e
  inspector solo lo ven de sus viajes o de los que reclamaron (`403`); coordinador y administrador, de su cliente.
- Reclamar y liberar quedan en la auditoría (`viaje.reclamar` y `viaje.liberar_reclamo`) con el usuario, la fecha, el viaje y la parada.
- **El reclamo y la espera del inspector del punto.** El reclamo de una parada se acepta **a cualquier hora** mientras la entrega no esté cerrada, **sin mirar ningún reloj**: si el conductor esperaba al inspector de esa parada (ver «La espera del inspector del punto»), la espera pasa a `inspector_llego` con la hora y el inspector, en la misma
  confirmación que crea el reclamo (auditoría `espera_inspector_punto.inspector_llego`). Si el conductor ya **cerró la parada sin inspector del punto**, el reclamo responde `409` con el motivo `entrega_cerrada_sin_inspector`, «Esta entrega se cerró sin inspector del punto a las 09:20: el cierre es definitivo» (hora de Colombia) y la hora completa en `details.cerrada_at`; nada se reabre. Si el inspector suelta
  después el reclamo, la espera anterior queda `inspector_llego` en el historial y el conductor puede activar una nueva.
- `GET /api/viajes/pendientes` (solo `inspector`) → lista de `PendienteDto`: los camiones pendientes de **sus puntos**. Son los **cargues programados para hoy** (hora de Colombia) en un punto suyo y las **entregas** de viajes `en_curso` con unidades por entregar en un punto suyo
  (sin las ya cerradas ni las rechazadas), **una fila por parada**: el estado de cada una (`disponible`, `en_revision`, `mio`) es el del reclamo de ESA parada. Cada fila: `{ viaje_id, codigo_mtrack, placa, proceso, lugar_id, lugar, fecha_programada, estado, reclamado_por, reclamado_por_nombre, reclamado_at, unidades }`. `estado` es `disponible` (nadie lo reclamó), `en_revision` (lo tiene otro inspector:
  «En revisión por» `reclamado_por_nombre`) o `mio`. Los cargues de otros días o de otros puntos no salen; un inspector sin puntos recibe `[]`. Desde ahí se reclama con `POST /api/viajes/{id}/reclamos`.
- **Visibilidad del inspector.** Ve los viajes en los que es el inspector, los que **reclamó** (reclamo activo) y los **pendientes de sus puntos** (un cargue de hoy en su punto o una entrega en curso que llega a su punto): el detalle de un pendiente de su punto responde `200` aunque no lo haya
  reclamado y el de otro punto, `403`. Ver no es actuar: para iniciar inspecciones o firmar hay que participar en el viaje o haberlo reclamado (`403` si no). La visibilidad del **conductor no cambia**: solo ve sus viajes.
- **Aviso de camión reclamado sin actividad.** Si un camión reclamado lleva `reclamo_sin_actividad_minutos` (parámetro, 30 por defecto; ver «Parámetros») sin ninguna inspección del viaje **iniciada o cerrada** después del reclamo, los `coordinador_calidad` del cliente del inspector reciben **una** notificación
  `reclamo_sin_actividad` (urgencia `media`, solo bandeja) con la placa, el viaje, el proceso (y, en la entrega, **la parada**) y el nombre del inspector. Un aviso por reclamo (es decir, por parada); **el reclamo no se libera**.

### Entrega por parada
Cada unidad se entrega en **su** lugar de destino y cada lugar de destino de un viaje es una **parada**. En cada parada se firma **una vez** entre quien entrega (**el conductor del viaje**) y quien recibe: **el inspector que tomó la entrega de ESA parada** (el reclamo es por parada) o, donde el punto
no tiene inspectores, **la persona del lugar**. La firma se guarda en la transferencia de **cada unidad pendiente de la parada** (la del Descargue, sobre su inspección de llegada), con la misma firma, fecha e IP y la hora y el GPS del momento, y cada unidad conserva su transferencia propia. Las unidades que
no salen del viaje no se entregan. **El inspector del viaje (quien cargó) no recibe por defecto ni tiene un trato aparte**: toma y firma la entrega de una parada solo si está asignado a ella, como cualquier inspector. Admin, super admin y coordinador no reciben ninguna ruta ni permiso
nuevo (`GET /api/viajes/{id}/entregas` se lee como siempre); la firma por parada y el cierre con una sola firma siguen siendo solo de `inspector` y `conductor`.

**El orden del destino.** Un solo orden, que el servidor hace cumplir: (1) el conductor marca «Llegué» (la llegada de Transporte de cada carro), (2) se **cierra la revisión de llegada del Descargue** de cada carro y (3) se firma la parada.
- Crear la revisión de llegada del Descargue de un carro sin su llegada de Transporte confirmada responde `409` con el motivo `sin_llegue` y «El conductor aún no marca «Llegué» para el VIN …», **para cualquier rol**.
- Firmar o cerrar una parada con la revisión de llegada de algún carro pendiente de la parada sin cerrar (o sin crear), **también la de un carro que se rechaza**, responde `409` con el motivo `llegada_abierta` y «Termina la revisión de llegada del VIN … antes de firmar».
- La firma por parada **de un inspector** antes de que algún carro de la parada tenga su «Llegué» responde `409` con el motivo `conductor_sin_llegada` y «El conductor aún no marca «Llegué» en esta parada». El detalle de la parada dice si el conductor ya llegó (`conductor_llego`).

- `GET /api/viajes/{id}/entregas` → `{ viaje_id, viaje_estado, unidades_sin_destino, puntos: [ EntregaPuntoDto ] }`: una entrega por parada. `unidades_sin_destino` cuenta las unidades de viajes anteriores al destino por unidad.
- `GET /api/viajes/{id}/entregas/{lugar}` → `EntregaPuntoDto`: `{ viaje_id, viaje_estado, lugar_id, lugar, ciudad, unidades_total, unidades_pendientes, entregada, lugar_tiene_inspector, estado_parada, receptor, receptor_nombre, conductor_llego, espera, espera_aviso_minutos, espera_plan_b_minutos, danos_por_calificar, unidades: [ EntregaUnidadDto ] }`. `404` si ninguna unidad activa del viaje va a ese lugar.
  `espera` es la última espera del inspector del punto de la parada (`null` si el conductor nunca activó «No hay inspector» en ella), `espera_aviso_minutos` y `espera_plan_b_minutos` son X e Y vigentes (para que la app cuente los plazos sin señal) y `danos_por_calificar` los daños sin nivel que el conductor marcó en las revisiones de llegada de la parada (lista vacía si no hay); ver «La espera del inspector del punto».
  - `estado_parada` *(nombres a confirmar por backend)*: `pendiente`, `entregada`, `entregada_con_rechazos` o `firmada_todos_rechazados` (la parada se firmó como constancia del rechazo de cada carro y ninguno se entregó). `entregada` es verdadero cuando ninguna unidad está pendiente **y al menos una se entregó**.
  - `receptor` *(a confirmar por backend)*: `inspector_del_punto` (el que tomó la entrega de esta parada, con su `receptor_nombre`), `persona_del_lugar` (el punto no tiene inspectores; `receptor_nombre` es el de quien ya recibió) o `sin_receptor` (el punto tiene inspectores y nadie tomó la entrega: no se puede firmar).
  - Cada unidad: `{ vehiculo_id, vin, estado, inspeccion_id, tipo_cierre, entrega_firmada_at, recibe_firmada_at, no_firma_parte, motivo_cierre, recibe_externo_nombre, recibe_externo_cedula, motivo_codigo, reservas, fotos_de_cierre, testigo_nombre, entrega_momento, recibe_momento, danos_nuevos, rechazo, entregada_sin_inspector_del_punto }`.
    `estado` es `sin_inspeccion` (aún no tiene la revisión de llegada del Descargue, donde se firma), `pendiente`, `cerrada` (entregada: `firmada`, `con_reservas`, `no_firma` o `recibo_sin_entrega`) o `rechazado` (no se entregó: ver «El carro rechazado»).
    `motivo_cierre` es el motivo escrito del cierre con una sola firma y `motivo_codigo` su motivo codificado (`se_nego_a_firmar`, `no_habia_nadie`, `sin_inspector_del_punto`, u **`otro`** en los cierres de antes de que el motivo fuera codificado, que se leen igual y sin foto); ambos `null` en cualquier otro cierre.
    `reservas` son las observaciones de quien recibe sobre ese carro; `fotos_de_cierre` cuenta las fotos del cierre con una sola firma; `testigo_nombre` es el testigo sin usuario (**nunca su cédula**); `entrega_momento` y `recibe_momento` son `{ momento, lat, lng, precision_m, sin_ubicacion }`: la hora y el GPS del **momento** en que firmó cada parte, aparte de
    `entrega_firmada_at` y `recibe_firmada_at`, que son cuando **llegó la firma al servidor**; `danos_nuevos` cuenta los daños nuevos de su revisión de llegada. `no_firma_parte` dice cuál parte **no** firmó. `recibe_externo_nombre` y `recibe_externo_cedula` son los de la persona que recibió cuando no es un usuario. Las entregas cerradas antes de este cambio se leen igual.
  - `lugar_tiene_inspector` dice si el **punto de destino tiene inspector** (ver «El punto tiene inspector» más abajo).

- `POST /api/viajes/{id}/entregas/{lugar}/firma` con `{ "firma": "…", "reservas"?, "rechazos"?, "recibe_nombre"?, "recibe_cedula"?, "firma_recibe"?, "fecha_momento"?, "lat"?, "lng"?, "precision_m"? }` → `200` con la entrega: la firma del usuario autenticado (quien entrega o quien recibe) se guarda en **cada unidad pendiente de la parada**; con
  las dos firmas cada transferencia queda `firmada` (o `con_reservas` si quien recibe dejó observaciones sobre ese carro, o `rechazado`). `400` sin `firma`. **`motivo_parcial`, `entrega_usuario_id` y `recibe_usuario_id` ya no se aceptan** (`400` «Campo no admitido»): la firma cubre **todos** los carros pendientes de la parada y quien recibe lo decide la parada.
  - **Quién recibe y quién puede firmar.** Si la parada **tiene inspectores asignados** recibe el inspector que tiene el reclamo activo de **esa parada**. Si nadie la tomó, la firma responde `409` con el motivo `sin_receptor` y «Este punto tiene inspector: pídele a un inspector del punto que tome la entrega en su app» (sin ningún aviso; **nunca se cae al inspector del viaje**); **la única salida es el plan B** de «La espera del inspector del punto»: con la espera activada y sus plazos cumplidos el conductor firma con la persona del lugar aunque la parada tenga inspectores, y antes del plazo responde `409 plan_b_no_habilitado`.
    Un inspector que no es de la parada recibe `403` con el motivo `no_es_su_punto` (también el que cargó el viaje sin estar asignado); el que la tomó otro inspector, `403` con `entrega_tomada`; uno de sus puntos que no la tomó, `409 sin_receptor`. Si la parada **no tiene inspectores** recibe la persona del lugar (siguiente apartado), traiga o no inspector el viaje. Solo el conductor del viaje y el inspector de la parada firman (`403` el resto).
  - **Reservas.** `reservas` es una lista de `{ "vin", "observaciones" }` (de 1 a 1 000 caracteres cada una; `400` con un VIN repetido, que no es de la parada o sin observaciones). Las deja **quien recibe** (`403` si las manda otra parte); con la persona del lugar las deja el conductor en su nombre y **no se borran** al firmar la persona externa. El carro con reservas cierra `con_reservas` cuando también firma
    quien entrega y los demás `firmada`; si quien entrega ya había firmado antes de que aparecieran reservas en un carro, **ese carro queda esperando su firma otra vez**.
  - **Hora y GPS del momento.** `fecha_momento` (ISO 8601 con zona), `lat`, `lng` y `precision_m` (opcional) son la hora y el lugar del momento en que se firmó: una firma hecha sin señal llega después y conserva la hora del momento y el GPS del patio, y la hora de envío queda aparte (`entrega_firmada_at`, `recibe_firmada_at`). Todos son opcionales: **sin GPS la firma se acepta y queda «sin ubicación»**
    (`sin_ubicacion: true`); `lat` y `lng` van juntas (`400` si no). Lo mismo vale para el cierre con una sola firma y para el recibo sin entrega del cargue.
  - **409** si no hay unidades pendientes en la parada, o por `sin_receptor`, `conductor_sin_llegada`, `llegada_abierta`, `sin_novedad_para_rechazar`, `plan_b_no_habilitado`, `entrega_tomada` o `danos_sin_calificar` (ver arriba, «El carro rechazado» y «La espera del inspector del punto»).
- `POST /api/viajes/{id}/entregas/{lugar}/una-firma` con `{ "motivo_codigo": "…", "motivo_texto"?, "firma"?, "fotos"?, "testigo_nombre"?, "testigo_cedula"?, "fecha_momento"?, "lat"?, "lng"?, "precision_m"? }` → `200` con la entrega: **una sola firma no bloquea**, pero ahora lleva su **motivo codificado y su evidencia**. Cada unidad pendiente de la parada queda `recibo_sin_entrega` con
  la parte que no firmó (`no_firma_parte`), se crea su **novedad de proceso** `recibo_sin_entrega` **con el código del motivo** (ya no `otro`) y los coordinadores de calidad y administradores del cliente reciben **un solo aviso** por entrega y parada. No lleva `rechazos` (`400` «Campo no admitido»): un rechazo solo viaja en la firma por parada.
  - `motivo_codigo` *(códigos a confirmar por backend)* es obligatorio (`400` sin él o con otro): `se_nego_a_firmar` (quien recibe se negó a firmar: **exige al menos una foto**, `400` «Con este motivo la foto es obligatoria»), `no_habia_nadie` (no exige fotos propias del cierre: su evidencia son las 7 fotos de la revisión de llegada de cada carro, que ya debe estar cerrada; **en una parada con inspectores responde `409 sin_receptor`**) o
    `sin_inspector_del_punto` (el cierre del plan B: solo lo cierra el conductor con la espera de la parada activada y sus plazos cumplidos; antes del plazo `409 plan_b_no_habilitado`, sin espera `409 sin_receptor` en una parada con inspectores y `409 motivo_no_aplica` en una sin ellos, y con un inspector que ya tomó la entrega `409 entrega_tomada`). `motivo_texto` es opcional (hasta 300 caracteres); sin él el cierre guarda el texto del motivo.
  - `fotos` es una lista de `{ "vin", "storage_path", "sello_fecha", "sello_lat", "sello_lng", "precision"?, "dispositivo_id"?, "nota"?, "hash_sha256"? }`: evidencias selladas **subidas antes** (`POST /api/uploads`), cada una con el VIN de un carro pendiente de la parada (`400` nombrando los que no). Se guardan como evidencia (constancia) de la revisión de llegada de ese carro.
  - `testigo_nombre` y `testigo_cedula` son **opcionales y van juntos** (mismas reglas de nombre y cédula de la persona externa; `400` en su campo): el testigo **no es un usuario** (por ejemplo, el vigilante). Su cédula es un dato personal y no viaja en ningún aviso.
  - La `firma` solo hace falta si quien cierra aún no había firmado; `409` si la otra parte ya firmó o si el punto tiene inspectores y nadie tomó la entrega. Quien cierra es quien entrega o quien recibe de esa parada (`403` el resto). En una parada **sin inspectores** ni quien reciba, la transferencia queda **sin quien reciba** y solo con la firma de quien entrega (`no_firma_parte: "recibe"`).
  - **El aviso** sigue siendo `novedad_proceso`, urgencia `alta`, a los coordinadores de calidad (y administradores) del cliente, con `datos.hecho = "entrega_con_una_firma"` y además `motivo_codigo`, `fotos` (cuántas) y `testigo` (solo el nombre): `{ "hecho": "entrega_con_una_firma", "lugar_id": "uuid", "lugar": "Concesionario Norte", "parte_que_firmo": "entrega", "motivo": "…", "motivo_codigo": "no_habia_nadie", "unidades": 3, "fotos": 0, "testigo": "Andrés Pardo" }`.
    El título dice el motivo: «No había nadie para recibir: Concesionario Norte» o «Quien recibe se negó a firmar: Concesionario Norte». **Ninguna cédula viaja en el aviso.** La bandeja se filtra por ese tipo con `GET /api/notificaciones?tipo=novedad_proceso`.

#### El carro rechazado
Un carro solo se queda sin entregar si se **rechaza por una novedad de su revisión de llegada**: el inspector que tomó la entrega (o, donde la parada no tiene inspectores, el conductor a pedido de la persona del lugar) lo marca en `rechazos` de la firma por parada: `[ { "vin", "hallazgo_id" } ]`, con el **daño de la revisión de llegada de ese carro** que lo motiva.
- Cada carro pendiente de la parada se entrega o va en `rechazos`. `400` («Para no entregar un carro elige el daño de su revisión de llegada que lo motiva») si falta el `hallazgo_id`, si es de otra revisión o si no tiene foto; `409` con el motivo `sin_novedad_para_rechazar` («El VIN … no tiene un daño que justifique el rechazo: entrégalo en esta parada») si el carro no tiene ningún daño con foto; `400` nombrando un VIN que no es de la parada o ya está entregado.
  En una parada con inspectores solo los manda el inspector que tomó la entrega (`403` el conductor); en una sin inspectores, el conductor.
- El carro rechazado queda con el estado de entrega `rechazado` y el motivo `rechazado_por_novedad`, unido al daño (`rechazo: { hallazgo_id, evidencia_id, marcado_por, marcado_por_nombre, motivo }`, con la foto del daño); **sigue `activa` en el viaje, en el camión y a cargo del conductor, y no cuenta como entregado**. Su transferencia cierra `rechazado` con las dos firmas (la parte que no marcó firma después). La auditoría guarda
  (`entrega.rechazar`) quién lo marcó, el daño, la hora y el GPS. El daño queda con la gravedad que le puso el inspector en la revisión, o sin calificar si lo registró el conductor.
- **Si se rechazan todos los carros, la parada igual se firma**, por las dos partes, como constancia del rechazo de cada uno: ningún carro queda entregado, todos siguen a cargo del conductor y `estado_parada` es `firmada_todos_rechazados`.
- **El aviso.** Cada rechazo emite `carro_rechazado_en_destino` *(tipo nuevo, a confirmar por backend)* de urgencia `alta` a los coordinadores de calidad del cliente: «Carro rechazado en destino: Concesionario Norte», con `datos`: `{ viaje_id, vin, lugar_id, lugar, hallazgo_id, parte, marcado_por }` (el nombre de quien lo marcó; ninguna cédula).
- **El viaje no se finaliza** mientras un carro rechazado no se entregue en su destino nuevo (un carro rechazado cuenta como no entregado).
- **Cambiar el destino del carro rechazado** (`PATCH /api/viajes/{id}/vehiculos/{vin}`, el de siempre): **se acepta aunque su revisión de llegada esté cerrada** (el `409` `descargue_con_informe_cerrado` no cambia para ningún otro carro) y deja el carro pendiente en el destino nuevo (otra parada del viaje, otro lugar o el lugar de origen). *(PAB-009, propuesta de backend.)* La revisión del rechazo **se conserva como evidencia**:
  la etapa Descargue del carro sigue siendo la misma y su revisión nueva es **otro ciclo** (`Ciclo` 2, 3...) de la misma etapa y momento; la última revisión de cada etapa y momento es la del ciclo más alto. Mientras el carro no recibe otro destino, su rechazo sigue vigente. Con el destino nuevo el carro vuelve a `sin_inspeccion`, se revisa y se entrega siguiendo las reglas de esta sección.

#### La espera del inspector del punto
Cuando la parada **tiene inspectores asignados y nadie tomó la entrega**, el conductor no puede firmar ni revisar la llegada solo: tiene una espera con dos plazos que **ajusta TI** (`espera_inspector_aviso_minutos` = X, 15 por defecto, y `espera_inspector_plan_b_minutos` = Y, 30 por defecto; ver «Parámetros de los tiempos y los avisos»). Los plazos se leen **en el momento** y se evalúan **dentro de cada petición** que los necesita, sin esperar a ningún proceso
periódico: nunca se responde «espere» después del plazo. *(Rutas, estados, motivos y campos de esta sección: por confirmar por backend.)*

- **«Llegué» no crea nada.** Marcar «Llegué» no crea ninguna espera ni ningún aviso: sin activación, ninguna parada genera el aviso ni habilita el plan B por mucho tiempo que pase.
- `POST /api/viajes/{id}/entregas/{lugar}/sin-inspector` con `{ "motivo_sin_inspector": "…", "motivo_sin_inspector_texto"?, "fecha_momento"?, "lat"?, "lng"?, "precision_m"? }` (solo el **conductor del viaje**: `403` a inspector, coordinador, admin y super admin) → `201` con la espera, o `200` con **la misma espera** si ya estaba abierta (el reloj no se reinicia y reenviar la activación no crea otra).
  - `motivo_sin_inspector` es obligatorio: el código de un motivo **activo** de `motivos-sin-inspector` (`400` «Motivo inválido…» en su campo); `motivo_sin_inspector_texto` solo con un motivo que lo pide (hasta 300 caracteres). `fecha_momento` (ISO 8601 con zona), `lat`, `lng` y `precision_m` son la hora y el GPS **del celular** en el momento de activar: la hora **nunca es posterior a la del servidor** (si el celular la manda adelantada se guarda la del servidor); `lat` y `lng` van juntas.
  - `409` con `details.motivo`: `parada_sin_inspectores` (la parada no tiene inspectores asignados: ahí recibe la persona del lugar), `entrega_tomada` (un inspector ya tomó la entrega), `entrega_cerrada` (la parada ya está entregada) o `sin_llegue_en_parada` (ningún carro de la parada tiene «Llegué»); `409` también si el viaje no está activo.
  - La espera guarda el viaje, la parada, el conductor, el motivo, la hora del celular (`activada_at`), la hora en que llegó al servidor (`recibida_at`) y el GPS. Queda en la auditoría (`espera_inspector_punto.activar`).
- **`EsperaDelPuntoDto`**: `{ id, viaje_id, lugar_id, estado, conductor_id, motivo_sin_inspector, motivo_sin_inspector_texto, activada_at, recibida_at, lat, lng, precision_m, aviso_desde, aviso_at, plan_b_desde, plan_b_habilitado, inspector_id, inspector_nombre, inspector_llego_at, cerrada_at }`. `estado`: `esperando`, `inspector_llego` (con `inspector_id`, `inspector_nombre` e `inspector_llego_at`),
  `cerrada_sin_inspector_del_punto` (con `cerrada_at`) o `anulada` (el viaje se canceló o se finalizó a mano con la espera abierta; auditoría `espera_inspector_punto.anular`). `aviso_desde` y `plan_b_desde` son `activada_at` más X y más X + Y minutos con los valores vigentes; `aviso_at` es cuándo se avisó (`null` si aún no); `plan_b_habilitado` es verdadero con la espera abierta y el plazo cumplido.
  Una parada tiene a lo sumo **una espera abierta** (un índice único de la base lo garantiza); las que terminaron quedan de historial.
- **El aviso a los X minutos.** Cumplidos `espera_inspector_aviso_minutos` desde `activada_at` **sin reclamo de esa parada**, los `coordinador_calidad` del cliente reciben **una sola vez por espera** el aviso `espera_inspector_punto` (urgencia `alta`, bandeja y push) con el título «Nadie ha tomado la entrega: Puerto de Cartagena», el viaje, el conductor y desde qué hora espera, y `datos`:
  `{ "hecho": "espera_inspector_punto", "viaje_id", "lugar_id", "lugar", "activada_at", "minutos_esperando", "plan_b_desde", "unidades" }`. Lo revisa el proceso de cada minuto **y también la petición de activación**: si al llegar (activada sin señal) ya pasaron X minutos, sale en esa misma petición. Si un inspector tomó la entrega antes, no sale. *(No hay un segundo aviso al habilitarse el plan B ni un aviso a los inspectores de la parada al activar.)*
- **El plan B: X más Y minutos.** Con la espera `esperando`, sin reclamo y con la hora del servidor igual o posterior a `activada_at` + X + Y:
  - la **firma por parada** acepta a la persona del lugar (`recibe_nombre`, `recibe_cedula`, `firma_recibe`) aunque la parada tenga inspectores, y el **cierre con una sola firma** acepta el motivo `sin_inspector_del_punto`; antes del plazo ambos responden `409` con `plan_b_no_habilitado` y «Todavía no puedes cerrar sin inspector del punto: faltan N minutos» (`details.faltan_minutos` y `details.plan_b_desde`); sin espera, `409 sin_receptor`. Las reglas de la entrega por parada (revisión de llegada cerrada, rechazos por novedad, hora y GPS del momento) siguen valiendo.
  - el cierre deja la espera `cerrada_sin_inspector_del_punto` y **marca cada carro entregado** como «entregada sin inspector del punto» (`entregada_sin_inspector_del_punto: true`; un carro rechazado nunca la lleva). La auditoría (`espera_inspector_punto.cerrar`) guarda la hora de activación, la de habilitación (`activada_at` + X + Y), la del cierre y el nombre de quien recibió (nunca su cédula).
  - **Con su sola firma** (`sin_inspector_del_punto`) todos los carros pendientes de la parada quedan entregados con esa marca y **no lleva rechazos** (`400` «Campo no admitido»), igual que todo cierre con una sola firma.
- **Si el inspector llega.** Si un inspector toma la entrega **antes del cierre**, aunque ya haya pasado el plazo, la espera termina como `inspector_llego` y desde entonces el plan B responde `409 entrega_tomada`; **después del cierre**, el inspector recibe `409 entrega_cerrada_sin_inspector` (ver «Reclamo del camión») y nada se reabre. Si plan B y reclamo llegan **a la vez**, gana exactamente uno (el estado de la espera lo decide en la misma confirmación) y el otro recibe su `409`: `entrega_tomada` el conductor o `entrega_cerrada_sin_inspector` el inspector.
- **La revisión de llegada del conductor.** En una parada **con inspectores**, crear la revisión de llegada del Descargue como **conductor** (`POST /api/inspecciones`) responde `409` con el motivo `llegada_del_inspector_del_punto` salvo que la espera de la parada tenga el plan B habilitado (la misma regla de tiempo, evaluada en esa petición): con espera, «La revisión de llegada en esta parada la hace el inspector del punto: podrás hacerla sin él en N minutos»;
  sin espera, «La revisión de llegada en esta parada la hace el inspector del punto: si no llega, activa «No hay inspector»». El inspector que tomó la entrega la crea siempre; en una parada sin inspectores el conductor la crea como siempre.
- **El inspector que llega tarde.** Si un inspector toma la entrega cuando el conductor, con el plan B, ya cerró la revisión de llegada de algunos carros, **esas revisiones cuentan** como la revisión de llegada de esos carros (no se crea otra ni se reabren) y el inspector crea las de los que faltan. El inspector que tiene el reclamo de la parada **califica los daños sin nivel que el conductor marcó en esas revisiones** (`POST /api/hallazgos/{id}/revision`; con los demás daños del viaje sigue la regla de siempre).
  `EntregaPuntoDto.danos_por_calificar` los lista: `{ hallazgo_id, vin, parte, clase_dano, evidencia_id }`. Con la parada **tomada por un inspector**, la firma por parada y el cierre con una sola firma responden `409` con el motivo `danos_sin_calificar` y «Antes de firmar, califica los daños que marcó el conductor en esta parada: falta N» (`details.faltan`) mientras quede alguno sin nivel; una parada sin revisiones del conductor firma como antes.
- **Rechazar un carro con el plan B.** Con el plan B habilitado la firma por parada del conductor con la persona del lugar acepta `rechazos` con las mismas reglas de «El carro rechazado» (cada rechazo unido a un daño con foto de la revisión de llegada del descargue de ese carro; sin daño, `409 sin_novedad_para_rechazar`). El daño queda sin calificar, el carro rechazado **no** lleva la marca «entregada sin inspector del punto», sigue `activa` a cargo del conductor y emite `carro_rechazado_en_destino`; los demás carros quedan con la marca.

#### Quien recibe es una persona externa

Si el **punto de entrega no tiene inspector asignado** (ningún inspector activo asignado a ese lugar en los puntos de los inspectores), quien recibe es la **persona del lugar**, que no es usuario (por ejemplo, quien recibe en el concesionario), **traiga o no inspector el viaje**.
El conductor la registra en el mismo `POST …/firma`: `firma` es la de quien entrega y

| Campo | Obligatorio | Regla |
|---|---|---|
| `recibe_nombre` | sí | de 3 a 150 caracteres |
| `recibe_cedula` | sí | hasta 30 caracteres: números, letras, espacios, puntos o guiones, con al menos un número (por ejemplo `1 020 304 050`) |
| `firma_recibe` | sí | el trazo de la persona que recibe |

Las dos firmas cierran cada unidad pendiente de la parada como `firmada`, con el nombre y la cédula guardados en **cada** transferencia (`recibe_usuario_id` queda `null` y `TransferenciaDto` trae `recibe_externo_nombre` y `recibe_externo_cedula`; `TransferenciaDetalleDto`, `recibe_nombre` con ese nombre y `recibe_externo_cedula`).
Si falta alguno, `400` con `details.fieldErrors` **nombrando todo lo que falta** (`recibe_nombre`, `recibe_cedula`, `firma_recibe`); un valor inválido responde `400` en su campo. Solo quien entrega puede registrarla (`403` el resto).

Si el punto **sí tiene inspector asignado**, la entrega se firma entre el conductor y el inspector que la tomó y los tres campos de la persona externa se **rechazan** con `400` (`El punto de entrega tiene inspector asignado: la entrega la firma él, no una persona externa`).

**Auditoría.** Cada unidad firmada así deja la acción `transferencia.firmar_externo` con quién la registró, cuándo y, en `despues`, la transferencia (con el nombre y la cédula de quien recibió) y el `punto` (`lugar_id`, `lugar`, `vin`). **La cédula es un dato personal: no viaja en el texto ni en los campos de ningún aviso.**
La constancia en PDF muestra a esa persona como quien recibe, con su documento y, en cada firma, la hora y el lugar del momento y la hora de envío.
- **El viaje se finaliza** solo: cuando todas sus unidades activas tienen el Descargue cerrado y entregado (inspección de llegada cerrada y transferencia con cierre que no es un rechazo) y estaba `en_curso`, pasa a `finalizado` (auditoría `viaje.estado.finalizado`) con **cualquier** cierre de la entrega que deje al último carro entregado: la firma de la parada (con o sin reservas) o el cierre con una sola firma, con cualquiera de sus motivos (también el plan B del punto sin inspector). Con entregas pendientes o carros rechazados sigue en curso: un carro rechazado cuenta como pendiente hasta que se entregue en su destino nuevo. La firma carro por carro del descargue ya no existe, así que no hay otro camino. Si hay carros pendientes, `PATCH /api/viajes/{id}/estado` solo finaliza con motivo (ver esa ruta).

### `DELETE /api/viajes/{id}/vehiculos/{vin}`
Gestión de viajes. Quita un VIN del viaje y borra sus etapas. `200` con el `ViajeDto`. `404` si el VIN no está en el viaje; `409` si el VIN ya tiene inspecciones
en este viaje.

### `DELETE /api/viajes/{id}`
Gestión de viajes. Borra el viaje con sus VIN y etapas, solo si **no tiene inspecciones** (`204`); si las tiene, `409` y debe cancelarse con el cambio de estado.

### Carga de viajes por Excel
El archivo tiene **una fila por VIN**: VIN, placa, tráiler, cédula del conductor, unidad, código MTrack, fecha y hora programada del cargue, lugar de cargue (origen) y lugar de destino de esa unidad. El cliente lee el Excel y envía las filas como JSON; las filas con el
mismo `codigo_mtrack` forman **un solo viaje**; si el archivo **no trae el código**, las filas con la misma **placa y fecha programada** forman un viaje y el sistema le genera el código (ver «Archivo sin código del viaje»). Son dos pasos: primero una **vista previa** con el error de cada fila (no guarda nada) y, al confirmar,
**solo se guardan los viajes de las filas válidas**.

```json
{
  "filas": [
    { "fila": 2, "vin": "9BWZZZ377VT004251", "placa": "ABC123", "trailer": "T-9", "cedula_conductor": "1.234.567", "unidad": "Niñera", "codigo_mtrack": "MTK-001",
      "fecha_programada": "2026-11-03 06:00", "origen": "CEDI Envigado", "destino": "Concesionario Autolarte Medellín" },
    { "fila": 3, "vin": "9BWZZZ377VT004252", "placa": "ABC123", "trailer": "T-9", "cedula_conductor": "1.234.567", "unidad": "Niñera", "codigo_mtrack": "MTK-001",
      "fecha_programada": "2026-11-03 06:00", "origen": "CEDI Envigado", "destino": "Concesionario Caminos Cali" }
  ]
}
```

| Campo | Obligatorio | Regla |
|---|---|---|
| `fila` | no | número de la fila en el archivo, para mostrar el error donde está; si falta se usa la posición (desde 1) |
| `vin` | sí | debe existir en el inventario |
| `placa` | sí | máximo 50 caracteres |
| `trailer` | no | máximo 50 caracteres |
| `cedula_conductor` | no | con o sin puntos; debe ser la de un usuario activo con rol `conductor`. Vacía = viaje sin conductor |
| `unidad` | no | el `codigo` o el nombre de un valor **activo** de `tipos-unidad` (sin distinguir tildes ni mayúsculas). **Si no se trae, el viaje toma la niñera** (`ninera`, el valor por defecto) |
| `codigo_mtrack` | no | formato y unicidad como en `POST /api/viajes`. **Si no se trae, el sistema lo genera** de la placa y la fecha programada |
| `fecha_programada` | sí | día y hora del cargue: `2026-11-03 06:00` (también `AAAA-MM-DDTHH:mm`); **sin zona horaria es hora de Colombia (UTC-5)** y con zona (`Z`, `-05:00`) se respeta |
| `origen` | sí | nombre de un lugar **activo** del catálogo `lugares` (sin distinguir tildes ni mayúsculas): el lugar de cargue del viaje |
| `destino` | sí | nombre de un lugar **activo** del catálogo `lugares`: el destino de **esa** unidad (cada VIN del viaje puede ir a uno distinto) |

Un lugar que no existe o está inactivo marca solo su fila («El lugar X no existe en el catálogo»); el coordinador lo crea con `POST /api/catalogos/lugares` y vuelve a previsualizar.

Una fila es inválida si falla un campo, si su `codigo_mtrack` ya existe, si su VIN ya está en un viaje activo, si repite un VIN dentro de su viaje, si el mismo VIN
aparece bajo dos códigos del archivo, o si no coincide en placa, tráiler, conductor, unidad, fecha programada o lugar de cargue con la primera fila válida de su mismo `codigo_mtrack` (el destino es de cada unidad y no se compara). Máximo **5000** filas;
una lista vacía o más grande responde `400`.

**Archivo sin código del viaje** *(regla confirmada por el negocio, DEC-003)*. Las filas **sin** `codigo_mtrack` se agrupan por **placa** (sin distinguir mayúsculas, guiones ni espacios) y **fecha y hora programada**: todas las que coinciden forman **un solo viaje** y no uno por fila.
El código del viaje lo genera el sistema y es **estable**: `AUTO-{PLACA}-{AAAAMMDDHHmm}` (la placa en mayúsculas y sin símbolos; la fecha, en hora de Colombia), por ejemplo `AUTO-WXY123-202511190600`; lo mismo vale para una placa con varios viajes en días u horas distintos (cada fecha y hora es otro viaje). Por eso **cargar dos veces el mismo
archivo no crea viajes duplicados ni códigos distintos**: la segunda vez cada fila se rechaza con «Ya existe el viaje de la placa … y esa fecha: este camión ya se cargó». Esas filas deben coincidir en tráiler, conductor, unidad y lugar de cargue (la placa y la fecha ya coinciden por construcción). Una fila con código sigue
como siempre, y en un mismo archivo pueden venir filas con código y filas sin él. Cada fila responde con el código que se usó y `codigo_generado` (`true` si lo generó el sistema).

- `POST /api/viajes/cargar/previsualizar` (gestión de viajes) → `200`, sin guardar nada.
- `POST /api/viajes/cargar` (gestión de viajes) → `201`, guarda los viajes de las filas válidas; cada viaje se confirma por separado.

Ambos responden con la misma forma:

```json
{
  "confirmado": false, "total": 3, "validas": 2, "invalidas": 1, "viajes": 1,
  "filas": [
    { "fila": 2, "vin": "9BWZZZ377VT004251", "codigo_mtrack": "MTK-001", "codigo_generado": false, "valida": true, "errores": [], "viaje_id": null },
    { "fila": 3, "vin": "9BWZZZ377VT004252", "codigo_mtrack": "MTK-001", "codigo_generado": false, "valida": true, "errores": [], "viaje_id": null },
    { "fila": 4, "vin": "NO-EXISTE", "codigo_mtrack": "AUTO-DEF456-202611030600", "codigo_generado": true, "valida": false, "errores": ["El VIN NO-EXISTE no existe en el inventario: cárguelo antes"], "viaje_id": null }
  ]
}
```

`confirmado` es `false` en la vista previa; `viajes` es cuántos viajes se crearían (vista previa) o se crearon (confirmación). Al confirmar, `viaje_id` trae el viaje de cada
fila guardada. La confirmación vuelve a validar todo: no depende de la vista previa.

**Conductor de una fila sin cédula** *(regla confirmada)*. La fila que **no trae `cedula_conductor`** crea su viaje **sin conductor**: no se busca por nombre ni por ningún otro dato, y la fila se guarda como válida. El conductor se asigna después **desde el viaje** (`PATCH /api/viajes/{id}` con `conductor_id`). El portal puede mostrar en esa fila «Sin conductor: se asigna desde el viaje». Una fila **con** cédula sigue asignando al conductor (activo, con rol `conductor`) y una cédula que no corresponde a uno sigue marcando solo esa fila. La búsqueda por nombre (cuando la columna trae el nombre y no la cédula) **no existe**: si se agrega, con 0 coincidencias o con 2 o más el viaje quedará sin conductor y la fila lo dirá, nunca se elige uno al azar (PAB-004, a confirmar por negocio).

#### Mapeo de columnas compartido por cliente
El portal lee el archivo, decide qué columna es cada campo y envía las filas ya mapeadas (la carga no cambia). Para que **no tenga que repetir esa decisión** cada vez ni en cada computador, el servidor guarda el mapeo **por cliente y por «forma del archivo»**. *(Rutas y forma a confirmar por backend con el portal.)*

- **Forma del archivo.** Es la lista de sus columnas, cada una como `{ "encabezado": "Placa Camión", "ocurrencia": 1 }`: el título y su número de repetición (1 la primera vez que el título aparece, 2 la segunda...). El servidor **normaliza** el título (sin tildes, en minúscula y con los espacios simples, recortados) y calcula la **huella**: los títulos normalizados con su repetición, **ordenados**, así que **el orden de las columnas no importa** (el mismo archivo con las columnas en otro orden es el mismo archivo). La clave es el cliente más la huella: dos archivos del mismo cliente con títulos distintos (por ejemplo uno de 28 y otro de 35 columnas) **conviven** y guardar uno no pisa al otro.
- **Qué se guarda.** Solo los títulos y el campo elegido para cada uno. Un cuerpo con cualquier otro campo (también dentro de una columna) responde `400` «Campo no admitido», y **ninguna respuesta ni registro de auditoría contiene valores de las filas** (VIN, placa, nombres ni fechas).

`POST /api/viajes/cargar/mapeo/buscar` — **gestión de viajes** (`admin`, `super_admin`, `coordinador_calidad`; `403` al resto). Cuerpo `{ "columnas": [ { "encabezado": "VIN", "ocurrencia": 1 }, ... ] }`, en cualquier orden. `200` con el mapeo guardado de ese cliente para esa forma, o `404` «Sin mapeo» si no hay (también si lo hubiera de **otro** cliente: nunca se distingue).

```json
{ "version": 1,
  "columnas": [ { "encabezado": "vin", "ocurrencia": 1 }, { "encabezado": "placa camion", "ocurrencia": 1 }, { "encabezado": "observaciones", "ocurrencia": 1 } ],
  "campos": [ "vin", "placa", null ],
  "actualizado_por": "…", "actualizado_por_nombre": "Carlos Coordinador", "actualizado_at": "2026-11-23T14:05:00.000Z" }
```

`PUT /api/viajes/cargar/mapeo` — mismos roles. Cuerpo `{ "version": 1, "columnas": [...], "campos": [...] }`: `campos` lleva **uno por columna, en el mismo orden**, y cada uno es `vin`, `placa`, `trailer`, `cedula_conductor`, `unidad`, `codigo_mtrack`, `fecha_programada`, `origen` o `destino` (`null` = la columna se ignora). Guarda el mapeo de esa forma o **reemplaza** el que ya había, y responde `200` con el mismo cuerpo de la lectura (con quién lo guardó por última vez y cuándo; los títulos como quedaron normalizados). `400` si faltan las columnas o los campos, si no van en la misma cantidad, si hay más de 200 columnas o un título vacío o de más de 200 caracteres, si un campo no existe o está elegido para dos columnas, si las repeticiones de un título no van de 1 a N sin saltos, o si `version` no es 1.

**Cliente.** Quien pertenece a un cliente recibe y guarda **siempre el suyo** y `403` si pide otro con `cliente_id`; el `super_admin` **indica el cliente** con `cliente_id` (`400` si no lo hace o no es un UUID), como en los listados.

**Auditoría.** Cada guardado deja la acción `viajes.mapeo.guardar` (entidad `mapeos_carga_viajes`) con quién lo hizo, cuándo, el cliente y, en `despues`, la huella y las columnas con su campo.

### Visibilidad por viaje
`inspector` y `conductor` ven **solo lo de los viajes en los que participan** (son su conductor o su inspector, y el viaje no está cancelado); el resto de roles ven todo.
Se aplica a:

| Recurso | Para un inspector o conductor |
|---|---|
| `GET /api/viajes`, `GET /api/viajes/{id}` | solo sus viajes (con todos sus VIN); el detalle de otro viaje responde `403` |
| `GET /api/vehiculos?vin=` y `GET /api/vehiculos/listado` | solo los VIN de sus viajes |
| `GET /api/vehiculos/{vin}/inventario` (y `/pdf`) | `403` si el VIN no está en sus viajes (`404` si no existe); del VIN ve solo las etapas de sus viajes |
| `GET /api/inspecciones` | solo los informes de sus viajes |
| `GET /api/inspecciones/{id}` | `403` si el informe es de otro viaje |
| `POST /api/inspecciones` | `403` si la etapa es de otro viaje |
| `GET /api/sofasa/ordenes` | solo las órdenes vinculadas a sus viajes |

Los reportes (`/api/reportes/*`, `/api/metricas/rechazo`) son agregados y no se filtran por viaje. Los demás roles (coordinador de calidad, comercial, administradores) no se limitan por viaje sino por **cliente**: ver «Consultar un carro desde la app» y «Visibilidad por cliente».

#### Consultar un carro desde la app
El `coordinador_calidad` (y el `comercial` y el `admin`) consulta **los carros de su cliente aunque no tenga ningún viaje asignado y el carro no esté en ningún viaje**, en lectura y con la misma regla de cliente que el listado de hallazgos:

- `GET /api/vehiculos?vin=` encuentra el carro por su VIN (o un fragmento) y `GET /api/vehiculos/{vin}/inventario` (y `/pdf`) abre su ficha con sus inspecciones, sus daños y su estado: `200` con un VIN de su cliente.
- Con un VIN de **otro cliente** el inventario responde `403` («El VIN no es de su cliente») y la búsqueda no lo devuelve; para que la app lo explique, el `message` dice que ese carro no es de su cliente.
- Sus informes salen en `GET /api/inspecciones` y se abren con `GET /api/inspecciones/{id}` aunque no sean de un viaje suyo; uno de un VIN de otro cliente responde `403`.
- El **inspector y el conductor no cambian**: siguen viendo solo los carros de sus viajes (tabla de arriba), aunque el carro sea de su mismo cliente; un VIN de otro viaje responde `403` («El VIN no pertenece a ninguno de sus viajes»).
- Es **solo lectura**: abrir la captura o la edición a estos roles no forma parte de esta regla (crear inspecciones, firmar o cambiar viajes sigue siendo de sus roles).

> **Retirados.** `POST /api/asignaciones` y `POST /api/etapas` ya no existen (responden `404`): las asignaciones pasaron a ser viajes y las etapas nacen al
> agregar un VIN a un viaje.

---

## Inspecciones (informes)

Por cada etapa de un VIN hay una **inspección de salida** (al entregar) y una **inspección de llegada** (al recibir). En pantalla se llaman así: el
`InspeccionDto` trae `momento_nombre` («Inspección de salida» o «Inspección de llegada») listo para mostrar.

Flujo típico: crear la inspección (siempre en `borrador`) → subir las fotos con `POST /api/uploads` → registrarlas como evidencias de cada vista y
agregar hallazgos (cada uno con su foto) → cerrar → cerrar la transferencia (cada parte firma lo suyo, antes o después del cierre de la inspección; ver «Transferencia de custodia»). Un error en una inspección cerrada **no se edita**: se corrige con una
versión nueva (`POST /api/inspecciones/{id}/corregir`).

| Etapa | Qué exige el cierre |
|---|---|
| Cargue y Descargue | la foto de **cada una de las 7 vistas obligatorias** (las activas de `vistas-foto` con `obligatoria: true` para el tipo del vehículo); una foto propia por hallazgo; ubicación en toda foto. Las 2 vistas adicionales (piezas en el baúl y cojinería) **no bloquean** el cierre |
| Transporte (registro del conductor) | no exige las 7 vistas, pero sí la **ubicación (GPS) y la hora del propio registro** (`lat`/`lng` de la inspección); si hay una novedad, el hallazgo lleva su foto. Cuenta para la cobertura |

Las mismas reglas valen para quien haga la inspección: cuando el conductor hace la salida del cargue por la excepción «No hay inspector», con las mismas 7 vistas, sello y hallazgos.

### `POST /api/inspecciones`
Campo. `201` con la inspección. El usuario sale del token (no se envía) y **la placa, el tráiler y el conductor salen del viaje de la etapa**: ya no se piden como texto.
Inspector y conductor reciben `403` si la etapa es de un viaje que no es suyo; un viaje finalizado o cancelado responde `409`; una etapa inexistente, `404`.

**El conductor no crea la salida de un Cargue… salvo que active la excepción.** Si la etapa es el Cargue, la salida la crea el inspector: el conductor recibe `403` con «La inspección de salida del cargue la hace un inspector; si no hay inspector, active la excepción» (no hay ninguna marca previa en el cargue).
En Transporte y Descargue el conductor sí la crea. La regla es de la salida del Cargue; la llegada y el resto no cambian. La excepción «No hay inspector» está en [Excepción «No hay inspector»](#excepción-no-hay-inspector-y-novedades-de-proceso) (donde el punto de cargue tiene inspectores se pide y se concede; ver «Permiso de la excepción»).

**En una parada con inspectores la revisión de llegada es del inspector.** Crear la revisión de llegada del Descargue como **conductor** en una parada con inspectores asignados responde `409` con el motivo `llegada_del_inspector_del_punto` salvo que la espera de esa parada tenga el plan B habilitado (X más Y minutos desde que el conductor activó «No hay inspector», evaluados en esa misma petición); el inspector que tomó la entrega la crea siempre y en una parada sin inspectores el conductor la crea como siempre. Ver «La espera del inspector del punto».

**El Descargue tiene una sola revisión: la de llegada.** Crear la inspección de **salida del Descargue** (`momento: "salida"` en una etapa de descargue), con cualquier rol, responde `409` con `code: "MOMENTO_NO_ADMITIDO"` y `details.motivo: "salida_descargue"` (código y motivo por confirmar) y no guarda nada; la app y el portal no ofrecen ese momento. La llegada del Descargue (con sus 7 vistas) es el único registro del destino. Una salida del Descargue que quedó **abierta antes del cambio** sigue pudiendo cerrarse (`POST /api/inspecciones/{id}/cerrar`, sin exigir las vistas), y una ya cerrada se sigue leyendo, exportando y mostrando; no se corrige (ver `corregir`).

**La salida exige la etapa anterior terminada.** Si el VIN tiene una etapa anterior en el viaje (por su `orden`) y esa etapa no tiene su **llegada cerrada**, o tiene una
**transferencia sin tipo de cierre**, la salida responde `409` con `code: "ETAPA_PREVIA_SIN_CIERRE"` y `details` con `motivo` (`etapa_previa_sin_cierre` si falta cerrar la llegada,
`transferencia_sin_cierre` si falta cerrar la transferencia), `etapa_previa_id` y `etapa_previa` (el nombre de la etapa que falta terminar), para que la app muestre el bloqueo. La primera
etapa y la llegada no tienen este bloqueo. Una etapa ya tiene su inspección original de ese momento: crear otra responde `409` (para
cambiarla, se corrige), salvo que la original sea un borrador **descartado** o **superado** (ver «Descartar un informe en borrador»).

```json
{
  "etapa_id": "uuid", "momento": "salida", "fecha": "2026-07-17T12:30:15.123Z",
  "lat": 6.2442, "lng": -75.5812, "notas": "…"
}
```

| Campo | Obligatorio | Regla |
|---|---|---|
| `etapa_id` | sí | UUID |
| `momento` | sí | `salida` o `llegada` (los valores `check_in`, `check_out` e `informe` se retiraron: `400`) |
| `fecha` | sí | ISO 8601 con zona horaria |
| `lat` / `lng` | no (sí para cerrar en Transporte) | −90 a 90 / −180 a 180 |
| `estado` | no | si se envía, solo `borrador`; **no puede crearse cerrada** |
| `notas` | no | máximo 1000 caracteres |
| `motivo_sin_inspector` | solo en la excepción «No hay inspector» | un código **activo** de `motivos-sin-inspector`; en cualquier otro caso `400` «Campo no admitido» |
| `motivo_sin_inspector_texto` | solo si el motivo lo pide (`requiere_texto`) | de 1 a 300 caracteres; con un motivo que no lo pide, `400` |

```json
{
  "id": "uuid", "etapa_id": "uuid", "viaje_id": "uuid", "usuario_id": "uuid", "momento": "salida", "fecha": "…", "lat": 6.2442, "lng": -75.5812,
  "estado": "borrador", "conductor_nombre": "Juan", "placa": "ABC123", "trailer": "T-01", "notas": "…",
  "created_at": "…", "updated_at": "…",
  "iniciada_at": "…", "cerrada_at": null, "version": 1, "corrige_inspeccion_id": null, "momento_nombre": "Inspección de salida",
  "motivo_sin_inspector": null, "novedades_proceso": []
}
```

`conductor_nombre`, `placa` y `trailer` de la respuesta son los del viaje (en los informes anteriores a los viajes, lo que se capturó a mano). `iniciada_at` y `cerrada_at`
son la hora del servidor al crear y al cerrar (`cerrada_at` es `null` en borrador). `version` es 1 en la original y 2, 3… en las correcciones, que traen en
`corrige_inspeccion_id` la versión que corrigen. `motivo_sin_inspector` y `novedades_proceso` son los de la excepción «No hay inspector» (`null` y `[]` en el resto).

### Puntos del inspector: `GET|POST /api/usuarios/{id}/puntos` y `DELETE /api/usuarios/{id}/puntos/{lugar}`
Los **puntos** de un inspector son los lugares reales (catálogo `lugares`) donde trabaja; un inspector puede tener varios. Los administran `coordinador_calidad`, `admin` y `super_admin` (cada uno, los inspectores de **su** cliente; el super administrador, los de todos); el inspector solo **consulta los suyos**; a los demás roles responde `403` (el rechazo de siempre).

| Llamada | Qué hace |
|---|---|
| `GET /api/usuarios/{id}/puntos` | `{ "usuario_id": "uuid", "puntos": [ { "lugar_id": "uuid", "lugar": "CEDI Envigado", "ciudad": "Envigado", "asignado_at": "…" } ] }`, por nombre de lugar |
| `POST /api/usuarios/{id}/puntos` con `{ "lugar_ids": ["uuid", "uuid"] }` | asigna uno o varios puntos (los que ya tenía se conservan; repetir uno no cambia nada) y responde con sus puntos |
| `DELETE /api/usuarios/{id}/puntos/{lugar}` | quita un punto y responde con sus puntos (`404` si no lo tenía) |

`404` si el usuario no existe (o es de otro cliente); `400` en `usuario` si no es inspector; `400` en `lugar_ids` si la lista viene vacía o algún lugar no existe o está inactivo (se nombran juntos). Asignado un punto, el inspector ve **sin otro paso** los pendientes de ese punto (`GET /api/viajes/pendientes`). Cada asignación y cada retiro
quedan en la auditoría con quién y cuándo (`usuario.puntos_asignar` y `usuario.punto_quitar`, entidad `usuarios`).

### Excepción «No hay inspector» y novedades de proceso

Si no hay inspector en el Cargue de una unidad, el **conductor del viaje** activa la excepción y crea la salida con `motivo_sin_inspector`. **Donde el punto de cargue del viaje no tiene inspectores asignados la activa en el momento**; **donde tiene inspectores la excepción se pide y se concede** (ver «Permiso de la excepción»):

```json
{ "etapa_id": "uuid", "momento": "salida", "fecha": "2026-10-23T08:30:00-05:00", "lat": 6.2442, "lng": -75.5812, "motivo_sin_inspector": "otro", "motivo_sin_inspector_texto": "El inspector se enfermó" }
```

| Caso | Respuesta |
|---|---|
| conductor del viaje, salida del Cargue con apoyo, motivo válido (y su texto si lo pide) | `201` con la inspección en borrador |
| el mismo caso **sin motivo** | `403` con el mensaje de siempre |
| motivo inexistente o inactivo; texto que falta cuando el motivo lo pide, o que sobra cuando no lo pide; texto de más de 300 caracteres | `400` con `details.fieldErrors` (`motivo_sin_inspector` o `motivo_sin_inspector_texto`) |
| motivo en cualquier otra salida, en una llegada, en otra etapa, en un Cargue sin apoyo o de un inspector o administrador | `400` «Campo no admitido» |
| conductor de otro viaje | `403` |
| el inspector ya creó esa salida | `409`, como hoy |
| el mismo caso en un punto de cargue **con inspectores** y sin el permiso concedido | `403` («pida el permiso de la excepción…») |

La inspección sigue las reglas de la salida que hace el conductor (las 7 vistas con sello, hallazgos del conductor sin nivel V). Al crearla, en la misma confirmación:

- la inspección guarda `motivo_sin_inspector` y su texto (los conserva también la versión que la corrija);
- se crea la **novedad de proceso** `cargue_sin_inspector` con el motivo, quién la registró y la `fecha`, `lat` y `lng` **del cuerpo** (los del momento en que el conductor eligió el motivo, aunque la envíe después por falta de señal);
- se crea la notificación `novedad_proceso` (urgencia `alta`, por bandeja y push) para los `coordinador_calidad` y `admin` activos del **cliente del vehículo**, nunca de otro cliente; un fallo del push no deshace nada;
- queda en la auditoría: `inspeccion.crear` (con el motivo) y `novedad_proceso.crear` (entidad `novedades_proceso`).

Una novedad de proceso **no es un hallazgo**: no tiene zona, clase ni nivel y no cuenta en los patrones, el mapa de calor ni la tasa de rechazo.

`InspeccionDto`, el informe (`GET /api/inspecciones/{id}`) y cada inspección del inventario traen:

```json
{
  "motivo_sin_inspector": { "codigo": "otro", "nombre": "Otro", "texto": "El inspector se enfermó" },
  "novedades_proceso": [
    { "tipo": "cargue_sin_inspector", "motivo_codigo": "otro", "motivo": "Otro", "texto": "El inspector se enfermó", "usuario_nombre": "Carlos Gómez", "fecha": "2026-10-23T08:30:00-05:00" }
  ]
}
```

`motivo_sin_inspector` es `null` y `novedades_proceso` es `[]` en las demás inspecciones. `motivo` es el nombre del motivo y `motivo_codigo` su código. Las novedades se listan de la más antigua a la más reciente.

Cada novedad trae además `permiso` (en el viaje, la inspección y el inventario): `null` si la excepción se activó directamente (el punto no tenía inspectores) o, si se pidió el permiso, `{ solicitado_por_nombre, solicitado_at, solicitado_lat, solicitado_lng, concedido_por_nombre, concedido_at, autorizado_por_tiempo }`: **quién pidió, cuándo y desde dónde**, y **quién concedió o, si
nadie respondió, que lo concedió el reloj** (`autorizado_por_tiempo: true`, sin quien).

#### Permiso de la excepción

Donde el punto de cargue **tiene inspectores** asignados (ver «Puntos del inspector») el conductor **pide el permiso** antes de activar la excepción:

- `POST /api/viajes/{id}/permiso-sin-inspector` (solo el conductor del viaje; `403` el resto) con `{ "motivo_sin_inspector": "inspector_no_llego", "motivo_sin_inspector_texto"?, "fecha": "2026-11-10T08:30:00-05:00", "lat"?, "lng"? }`: `motivo_sin_inspector` es un motivo activo de `motivos-sin-inspector` (y su texto si lo pide), `fecha` es ISO 8601 **con zona** y `lat`/`lng`
  son el GPS del momento en que lo pide (los mismos errores `400` por campo que la excepción). `201` con el `PermisoSinInspectorDto`; repetir la solicitud devuelve la misma (una por viaje). `409` si el punto de cargue **no** tiene inspectores (ahí la excepción se activa directamente) o el viaje no está activo.
  Se avisa (`permiso_sin_inspector`, urgencia `alta`, bandeja y push) a los **inspectores del punto** y a los **coordinadores y administradores del cliente**.
- `POST /api/viajes/{id}/permiso-sin-inspector/conceder` (`coordinador_calidad`, `admin`, `super_admin`): concede el permiso y el **conductor queda habilitado de inmediato**, sin esperar al inspector; se le avisa a él. `404` si no hay solicitud; `409` si ya está concedida.
- `GET /api/viajes/{id}/permiso-sin-inspector`: el estado (el conductor y el inspector del viaje, los coordinadores y administradores del cliente; `404` si no hay).
- **Si nadie responde dentro del plazo** el permiso queda **concedido por tiempo**. El plazo es el parámetro `permiso_sin_inspector_minutos` (10 por defecto; 10 también si la fila no existe), se lee **al pedir el permiso** y **se guarda en la solicitud** (`vence_at`): cambiar el parámetro después no cambia el plazo de una solicitud ya pedida, y la base impide conceder por tiempo antes de ese plazo (`CK_tblPermisosSinInspector_Reloj`). Lo concede un proceso periódico cada minuto (no depende de que alguien abra una pantalla) y **también dentro de la petición que lo necesita**: crear la salida del Cargue con la excepción y registrar la posición de una unidad (`PUT …/posicion`) con una solicitud pendiente y ya vencida la conceden por tiempo en esa misma petición, con la misma auditoría (`permiso_sin_inspector.conceder_por_tiempo`) y el mismo aviso, y la aceptan; antes del plazo siguen respondiendo el `403` de hoy («… o a que pasen N minutos sin respuesta», con el plazo de la solicitud) y nunca responden «espere» después de él. Queda `autorizado_por_tiempo: true` y sin `concedido_por`; se avisa al conductor y al coordinador.

`PermisoSinInspectorDto`: `{ id, viaje_id, estado ("pendiente" o "concedido"), solicitado_at, vence_at (solicitado_at + el plazo con que nació la solicitud: `permiso_sin_inspector_minutos`, 10 por defecto), conductor_id, conductor_nombre, lugar_id, lugar, motivo_codigo, motivo, motivo_texto, fecha, lat, lng, concedido_at, concedido_por, concedido_por_nombre, autorizado_por_tiempo }`;
`fecha`, `lat` y `lng` son los del momento en que el conductor lo pidió. Con el permiso concedido el conductor crea la salida del Cargue con la excepción como siempre (`POST /api/inspecciones` con `motivo_sin_inspector`) y la novedad de proceso queda ligada a su permiso.

**El conductor con permiso cierra la salida del Cargue.** Con el permiso concedido (o con la excepción ya activa donde no hay inspectores) el conductor puede registrar las posiciones de la niñera (`PUT /api/viajes/{id}/vehiculos/{vin}/posicion`, antes solo de inspector, coordinador y administradores) y cerrar esa salida (`POST /api/inspecciones/{id}/cerrar`) con sus posiciones y las fotos de la acomodación,
sin que se le pida ningún inspector. Sin el permiso, el conductor recibe `403` en la posición. Todo queda en la auditoría: `permiso_sin_inspector.solicitar`, `permiso_sin_inspector.conceder` o `permiso_sin_inspector.conceder_por_tiempo` (entidad `permisos_sin_inspector`) y `novedad_proceso.crear` con su permiso.

### `POST /api/inspecciones/{id}/corregir`
Campo (quien participa en el viaje, o un administrador). Corrige una inspección **cerrada**: crea la **versión siguiente** (en `borrador` y vacía: se vuelven a tomar las fotos)
que referencia a la anterior; **la anterior no cambia**. `201` con la `InspeccionDto` nueva. Cuerpo opcional (`fecha`, `lat`, `lng`, `notas`; por defecto la fecha es ahora).

**Quién corrige y hasta cuándo.** El servidor hace cumplir dos reglas antes de crear la versión; los roles que llaman no cambian (`inspector`, `conductor`, `admin`) y ningún rechazo crea una versión ni deja rastro de corrección:

| Regla | Rechazo | Código |
|---|---|---|
| Solo corrige **quien hizo la inspección** (su `usuario_id`) **o un administrador** (`admin`; el `super_admin` no corrige: la ruta no es suya). Quien participa en el viaje pero no la hizo (por ejemplo el conductor con una inspección del inspector) no corrige | `403` «Esta inspección solo la corrigen quien la hizo o un administrador» | `CORRECCION_SOLO_AUTOR` |
| Solo **mientras nadie la haya firmado**: si alguna transferencia de la **etapa** (la de la llegada o la de la salida, de cualquier versión) ya tiene una firma (de quien entrega o de quien recibe) o un cierre (firmada, con reservas, constancia de no firma o recibo sin entrega), **nadie** corrige, tampoco quien la hizo ni un administrador | `409` «Esta etapa ya fue firmada…» | `INSPECCION_YA_FIRMADA` |

Los dos códigos son estables y distintos, con `details.motivo` (`solo_autor_o_administrador` e `inspeccion_firmada`), para que la app muestre el mensaje que corresponde. Si ambas se cumplen a la vez, gana la de la firma. Una transferencia ya registrada pero **sin ninguna firma** no impide corregir. **Después de firmar se sigue aclarando**: la nota aclaratoria
(`POST /api/inspecciones/{id}/notas`) y la firma de quien recibe con reservas (`POST /api/inspecciones/{id}/transferencia/reservas`) funcionan igual y no cambian la inspección.

- `409` si la inspección no está cerrada (se sigue editando), si ya fue corregida (se corrige la última versión) o si el viaje está finalizado o cancelado; también `409` con `code: "MOMENTO_NO_ADMITIDO"` (`details.motivo: "salida_descargue"`) si es una salida del Descargue de antes del cambio. `403` si el viaje no es del usuario. `404` si no existe.
- La versión nueva tiene sus propias fotos, hallazgos, transferencia y reglas de cierre. Una etapa puede tener varias versiones; los listados e informes las muestran todas con su `version`.

### Descartar un informe en borrador: `POST /api/inspecciones/{id}/descartar`
Autenticado, **sin cuerpo**. Descarta un informe que sigue en `borrador` (por ejemplo, el que se abrió por error o la versión vacía de una corrección abandonada): pasa a `estado: "descartada"`, **se conserva con su autor y con sus fotos y daños** (la evidencia no se borra) y su
momento queda libre para una inspección original nueva. `200` con la `InspeccionDto` descartada. *La operación y sus códigos son propuestas por confirmar con el negocio (PAB-001: se marca y no se borra, porque la base impide borrar un borrador con fotos, hallazgos o transferencia).*

| Regla | Rechazo | Código |
|---|---|---|
| Solo descarta **quien creó el informe** (su `usuario_id`) y el **coordinador de calidad** del cliente del VIN. Cualquier otro rol —otro inspector, el conductor, el comercial, el administrador y el super administrador— recibe el rechazo: ninguno gana la acción | `403` «No puedes descartar este informe: lo creó otra persona» con `details.motivo: "solo_autor_o_coordinador"` | `DESCARTE_SOLO_AUTOR_O_COORDINADOR` |
| Solo mientras está en `borrador`: un informe **cerrado** es evidencia y nunca se descarta, y un borrador cuya transferencia ya tiene una firma tampoco | `409` «Este informe ya está cerrado: es evidencia y no se descarta» (`details.motivo: "informe_cerrado"`) o «Este informe ya tiene una firma: es evidencia y no se descarta» (`"informe_con_firmas"`) | `INFORME_CERRADO_NO_SE_DESCARTA` |
| Uno que ya está descartado o superado | `409` con `details.motivo: "informe_descartado"` | `CONFLICT` |

- `404` si el informe no existe; el coordinador de calidad recibe `403` si el VIN es de otro cliente. Si ambas se cumplen, gana la de la autoría.
- **La auditoría del viaje** registra `inspeccion.descartar` con quién, cuándo y, en `detalle.antes` y `detalle.despues`, el `momento`, el `estado` y cuántas `fotos` y `danos` tenía el borrador.
- **Cómo distinguen la app y el portal un descartado.** El estado nuevo `descartada` (y `superada`, abajo) **no sale en ningún listado**: ni `GET /api/inspecciones`, ni el detalle del viaje, ni el inventario del carro, ni la cobertura, ni los avisos; el momento vuelve a ofrecer «Capturar». Solo se abre **por su id** (`GET /api/inspecciones/{id}`, que trae `estado: "descartada"` y la auditoría lo enlaza): el portal lo muestra con la etiqueta «Descartado» y no con el código crudo. El filtro `estado` del listado sigue admitiendo solo `borrador` y `cerrada`. Las respuestas anteriores se leen igual: los informes de siempre son `borrador` o `cerrada`.
- Un descartado **no admite** fotos, hallazgos, extras ni cierre (`409`, `details.motivo: "informe_descartado"`) y no genera el aviso «Captura incompleta».
- Descartar la versión vacía de una corrección abandonada deja la inspección cerrada otra vez **corregible** (la restricción de «una sola corrección» ya no la cuenta).

**Quien toma el carro con un borrador ajeno empieza de cero.** Cuando un **inspector** crea la inspección de un momento (`POST /api/inspecciones`) que ya tiene el borrador **de otro inspector**, si este **ya no tiene el reclamo** del viaje y su transferencia **no tiene ninguna firma**, el borrador anterior pasa a `estado: "superada"`: se conserva con su autor, no se borra y no es el informe vigente de ese momento (queda `inspeccion.superar` en la auditoría), y la inspección nueva se crea (`201`). Si el autor sigue reclamando, o el borrador ya tiene una firma, nada cambia y la creación responde `409` como hasta ahora. Quien repite su propio borrador, y el conductor que crea la salida con la excepción «No hay inspector» frente al borrador de un inspector, siguen recibiendo `409`. *(Por confirmar: que «sin el reclamo» sea la condición de dejar atrás un borrador ajeno.)*

### `GET /api/inspecciones`
Autenticado. Una **página** de informes (`{ items, total, pagina, tamano }`; 50 por defecto, máximo 200; los más recientes primero; ver «Paginación y filtros» para `pagina`, `tamano` y `orden`). Todos los filtros son opcionales y van en la consulta:

| Filtro | Valor |
|---|---|
| `vin` | VIN (17 caracteres sin `I`, `O` ni `Q`) o chasis de 6 a 20 alfanuméricos |
| `usuario_id` | UUID del inspector |
| `viaje_id` | UUID del viaje |
| `momento` | `salida` o `llegada` |
| `estado` | `borrador` o `cerrada` (los informes `descartada` y `superada` no salen en el listado) |
| `proceso` | `codigo` de una etapa del catálogo `etapas` (por ejemplo `cargue`, `transporte` o `descargue`), activa o no |
| `operacion` | `importacion`, `exportacion` o `nacional` (la del vehículo) |
| `marca` | la marca del vehículo |
| `cliente_id` | UUID del cliente del vehículo (solo el `super_admin` elige otro; los demás reciben `403`) |
| `origen_id`, `destino_id` | UUID del origen y del destino del viaje |
| `desde`, `hasta` | fecha ISO sobre la fecha de la inspección |

Un filtro mal formado responde `400` con `fieldErrors` por campo. Cada fila es el `InspeccionDto` más `vin` y `proceso`, y `lugar` (el punto de custodia de la etapa, el mismo que trae el detalle del informe), `codigo_mtrack` (el del viaje) y `unidad` (el tipo de unidad transportadora del viaje), para no abrir cada informe;
los tres son `null` cuando no hay (una etapa sin punto de custodia, un viaje sin código o sin tipo de unidad). Trae además `usuario_nombre`: **el nombre de quien hizo el informe**, con el mismo join y el mismo alcance por cliente que `conductor_nombre` (`null` si el usuario ya no existe; una fila de otro cliente no sale, así que su autor tampoco).
El nombre va solo en este campo del listado: no se agrega a ningún aviso, texto ni exportación. Inspector y conductor solo reciben los informes de sus viajes.

### `GET /api/inspecciones/{id}/pdf`
Autenticado. El **informe de la inspección en PDF** (`Content-Type: application/pdf`, nombre `informe-{vin}-{proceso}-{momento}-v{version}.pdf`), como descarga binaria (use `fetch` con el token y descargue el `blob`). Lleva:

- los datos de la **unidad** (VIN, marca, modelo, tipo de vehículo, color, operación y cliente), del **viaje** (código MTrack, placa, tráiler, unidad, conductor y cédula) y de la **etapa** (proceso, lugar, momento, versión, inicio, cierre e inspector);
- las **fotos de cada vista** con su sello: fecha y hora, ubicación (y precisión), sello del servidor y hash SHA-256; en el informe de la segunda inspección del cargue (la salida de la etapa Cargue) la sección «Acomodación en la niñera» lista cada unidad del viaje con su posición («Posición N — VIN …»; «Sin posición» o «No sale del viaje» si no la tiene) y las fotos van como «Fotos de la acomodación»;
- los **hallazgos** con su zona (lado + número + nombre), clase, tamaño, nivel (con su acción), origen, si es preexistente, su estado de seguimiento, su nota y su foto, con las notas aclaratorias de cada uno;
- el **panel de daños**: la imagen de cada panel con las zonas de los hallazgos resaltadas y marcadas con el número del hallazgo (las zonas sin dibujo se listan aparte);
- la **transferencia** con las dos firmas (imagen o trazo, con su fecha e IP), las reservas o la constancia de no firma (motivo, testigo y foto) y sus notas aclaratorias;
- las **notas aclaratorias** de la inspección y el **historial de seguimiento** de cada hallazgo (con la foto del sello de quien valida).

Las imágenes salen del almacén; una foto cuyo archivo ya no está se indica en su lugar. Lleva hasta **60 fotos** (primero las de los hallazgos): si hay más, el PDF avisa cuántas dejó fuera y se ven en el informe. Lo ve quien puede ver el informe:
`403` si es de otro cliente o de un viaje ajeno (inspector y conductor), y el conductor lo recibe con los hallazgos en lectura (sin nivel, seguimiento ni historial); `404` si no existe. Reemplaza al PDF de texto plano.

### `GET /api/inspecciones/{id}`
Autenticado. Informe completo para revisión (`InformeDetalleDto`): los datos de la inspección más el contexto y sus partes. Inspector y conductor reciben `403` si el informe es de un viaje que no es suyo. Abre también un informe `descartada` o `superada` (su `estado` lo dice).

```json
{
  "id": "uuid", "etapa_id": "uuid", "viaje_id": "uuid", "usuario_id": "uuid", "momento": "salida", "fecha": "…", "lat": null, "lng": null,
  "estado": "cerrada", "conductor_nombre": "Carlos Gómez", "placa": "ABC123", "trailer": "T-9", "notas": null, "created_at": "…", "updated_at": "…",
  "vin": "…", "marca": "…", "modelo": null, "proceso": "cargue", "lugar": null, "requiere_inspeccion": true,
  "inspector_nombre": "Ana Pérez", "ninera": "ABC123", "codigo_mtrack": "MTK-001", "unidad": "Niñera",
  "hallazgos": [ /* HallazgoDto */ ],
  "evidencias": [ /* EvidenciaDto, cada una con su propio sello */ ],
  "transferencia": { /* TransferenciaDetalleDto o null */ },
  "iniciada_at": "…", "cerrada_at": "…", "version": 1, "corrige_inspeccion_id": null, "momento_nombre": "Inspección de salida",
  "notas_aclaratorias": [], "motivo_sin_inspector": null, "novedades_proceso": [], "extras": [],
  "confirmacion_sitio": { "resultado": "confirmado_en_sitio", "nombre": "Confirmado en sitio", "distancia_m": 400.0, "evidencia_id": "uuid" }
}
```

**Confirmación «en sitio» (`confirmacion_sitio`).** Con la **primera foto con ubicación** de una inspección de **Cargue o Descargue** el servidor calcula la distancia (círculo máximo) al centro del lugar de la etapa (el lugar de cargue del viaje o el destino de la unidad) y guarda el resultado en la inspección mientras está en borrador; es `null` mientras no se determina y en el Transporte. Es una **marca: nunca bloquea ni cambia la captura** (la foto se guarda y se sella igual). `resultado` es:

- `confirmado_en_sitio`: la distancia no supera el radio **más la precisión de la foto** (la duda del GPS juega a favor: a 1.200 m con precisión de 200 m y radio de 1.000 m queda confirmada; a 1.201 m, no).
- `fuera_de_rango`: la supera. Avisa **una sola vez por inspección** al `coordinador_calidad` del cliente por bandeja y push (tipo `fuera_de_rango`, urgencia alta, con el viaje, el VIN, el lugar y la distancia).
- `sin_confirmacion_posible`, sin marca ni aviso: la foto no trae ubicación (0, 0), el lugar no tiene ubicación o su ubicación sigue **pendiente de confirmar** (`estado_ubicacion` distinto de `confirmada`), el viaje o la unidad no tienen lugar, la precisión no se reportó o es de `valvula_precision_m` (500 m) o más, o el cálculo falló. Una foto con ubicación posterior sí puede determinarlo; una vez `confirmado_en_sitio` o `fuera_de_rango`, no cambia.

`distancia_m` es la distancia de la foto que lo determinó (`evidencia_id`); es `null` en `sin_confirmacion_posible`. El radio es el `radio_metros` del lugar y, si no lo tiene, el parámetro `radio_lugar_cargue_m` (1.000 m) en el cargue o `radio_destino_m` (500 m) en el descargue; se leen en cada evaluación.

La placa, el conductor y el tráiler son los **del viaje**; en un informe anterior a los viajes, lo que se capturó a mano tiene prioridad. `ninera` es la placa del viaje (nombre heredado, igual a `placa`).

### `POST /api/inspecciones/{id}/hallazgos`
Campo (creador de la inspección o administrador). **Un hallazgo exige su foto propia**: se guarda junto con ella (`evidencia`, obligatoria; sin ella `400` y no se guarda nada). `201` con el hallazgo y su foto. La zona del daño, el nivel de severidad y si el daño ya existía se explican en [Hallazgo por región](#hallazgo-por-región).

```json
{
  "cuadrante_numero": 7, "lado": "izquierdo", "clase_dano": "rotura", "tamano_dano": "5_10cm", "nivel_severidad": "v2",
  "es_preexistente": false, "nota": "Rayón largo sobre la puerta",
  "evidencia": { "storage_path": "3f2b…c1.png", "sello_fecha": "2026-07-17T12:30:15.123Z", "sello_lat": 6.24, "sello_lng": -75.58, "precision": 4.5, "dispositivo_id": "tablet-7", "nota": null, "hash_sha256": "…" }
}
```

Un cuadrante que **no lleva lado** (por ejemplo el 54 «Parabrisas») va sin `lado`: `{ "cuadrante_numero": 54, "clase_dano": "golpe", "tamano_dano": "1_5cm", "nivel_severidad": "v1", "evidencia": { … } }`.

| Campo | Obligatorio | Regla |
|---|---|---|
| `cuadrante_numero` | sí | número de un cuadrante **activo** del catálogo `cuadrantes` (el del tipo de vehículo del VIN; si no hay, el genérico). Sin él, o si el cuadrante no existe, `400` en `cuadrante_numero` («La zona indicada no existe» si falta; «El cuadrante indicado no existe o está inactivo») |
| `lado` | si el cuadrante lleva lado | `izquierdo` o `derecho`, según `lleva_lado` del cuadrante en el catálogo (**ninguna regla usa números**). Si el cuadrante lleva lado y falta, `400` en `lado`; si no lo lleva y se envía, `400` en `lado` («Este cuadrante no lleva lado») |
| `clase_dano` | sí | `codigo` **activo** del catálogo `clases-dano` |
| `tamano_dano` | sí | `codigo` **activo** del catálogo `tamanos-dano` |
| `nivel_severidad` | inspector y administrador | `codigo` **activo** de `niveles-severidad` (`v1_plus`, `v1`, `v2`, `v3`). El conductor **no lo envía**: la regla es una sola para el daño de una zona y el de un extra (ver «Nivel que manda el conductor») y su daño queda sin nivel hasta que un inspector lo califique |
| `es_preexistente` | no | `true` si el daño ya existía. Exige `hallazgo_origen_id` |
| `hallazgo_origen_id` | con `es_preexistente` | un hallazgo de una **inspección cerrada anterior del mismo VIN** (la lista de [`hallazgos-previos`](#get-apiinspeccionesidhallazgos-previos)) con la **misma zona y la misma clase**; si no, `400` en `hallazgo_origen_id`. Sin `es_preexistente` verdadero no se envía |
| `nota` | no | máximo 500 caracteres |
| `evidencia` | sí | la foto del hallazgo, con los mismos campos y reglas que una evidencia (sin `vista_id` ni `hallazgo_id`) |

`parte_vehiculo` y `coords_diagrama` **ya no se aceptan**: si llegan, `400` con «Campo no admitido» en cada uno. Las **zonas provisionales** (`zona_provisional`: `frente` y las cuatro llantas) **desaparecieron**: un hallazgo que llega con `zona_provisional` y sin `cuadrante_numero` responde `400` con «La zona indicada no existe». El `origen` del hallazgo (`inspector` o `conductor`) sale del rol de quien lo registra.

Respuesta (`HallazgoCreadoDto`): el `HallazgoDto` más su `evidencia` (`EvidenciaDto`):

```json
{ "id": "uuid", "inspeccion_id": "uuid", "parte_vehiculo": null, "clase_dano": "rotura", "tamano_dano": "5_10cm", "coords_diagrama": null,
  "cuadrante_numero": 7, "lado": "izquierdo", "cuadrante_nombre": "Puerta delantera", "zona": "Izquierdo 7 · Puerta delantera", "aiag_area": "10/12",
  "tipo_novedad": null, "nivel_severidad": "v2", "nivel_nombre": "V2", "nivel_accion": "Validación por Sofasa",
  "origen": "inspector", "nota": "Rayón largo sobre la puerta", "es_preexistente": false, "hallazgo_origen_id": null, "confirmado_por": null,
  "estado_seguimiento": "pendiente", "evidencia_completa": false, "evidencia_faltante": [], "revisado_por": null, "revisado_at": null,
  "created_at": "…", "updated_at": "…", "evidencia": { /* EvidenciaDto */ } }
```

En el informe y el inventario, los hallazgos llegan como `HallazgoDto` (el mismo objeto sin `evidencia`) y sus fotos en `evidencias`, con `hallazgo_id`.

**Nivel que manda el conductor.** El conductor no asigna el nivel de severidad, ni en el daño de una **zona** (`POST /api/inspecciones/{id}/hallazgos`) ni en el de un **extra** (`POST` y `PUT /api/inspecciones/{id}/extras`): si lo envía responde `400` con `details.fieldErrors.nivel_severidad`
«El conductor no asigna el nivel de severidad: lo asigna el inspector», el mismo texto en los dos casos, y no se guarda nada (decisión propia sobre cómo responder, por confirmar con el negocio: la alternativa era ignorar el nivel). El daño del conductor nace **sin nivel** y con `origen: "conductor"`; un inspector lo califica después con `POST /api/hallazgos/{id}/revision`.

### Hallazgo por región
Un hallazgo se atribuye a una **zona** del vehículo, no a una parte en texto libre. La zona es **lado + número** de un cuadrante (`izquierdo 7`); se guarda por lado + número, no por el panel donde se tocó: «izquierdo 7» es la misma zona se toque donde se toque. Que un cuadrante lleve lado o no lo declara el catálogo (`lleva_lado`): los laterales (por ejemplo del 1 al 35) llevan lado y las piezas centrales (50 a 59) no. **`izquierdo` es el lado del conductor, visto sentado al volante.** Ya no existen las zonas provisionales: los hallazgos de ese tipo se desvincularon (ver «Cuadrantes, regiones y paneles de daño»).

Campos del `HallazgoDto`:

| Campo | Significado |
|---|---|
| `cuadrante_numero`, `lado` | la zona; en un cuadrante que no lleva lado `lado` es `null`. Ambos son `null` en un hallazgo sin ubicar |
| `cuadrante_nombre`, `zona`, `aiag_area` | el nombre de la pieza (del cuadrante), el texto listo para mostrar («Izquierdo 4 · Guardafango delantero», «54 · Parabrisas») y el código AIAG de esa zona («14/16»; texto, sin formato fijo; `null` si no tiene). En un hallazgo histórico sin ubicar, `zona` es la parte que se escribió entonces |
| `nivel_severidad`, `nivel_nombre`, `nivel_accion` | el nivel V (`v1_plus`…`v3`), su nombre («V1+») y la acción del catálogo («Se rechaza el vehículo»). Los tres son `null` mientras el daño no se califique (la novedad o el daño de zona o de extra del conductor, o uno migrado sin nivel) |
| `sin_calificar`, `calificado` | `sin_calificar` es `true` cuando el hallazgo no tiene nivel y `calificado` es lo contrario (`true` cuando ya lo tiene). Ven los dos todos los roles; el conductor recibe `sin_calificar: false` y `calificado`, que es lo único que sabe de la calificación (nunca el nivel) |
| `extra_id` | el extra del que nace el daño (`null` en los demás hallazgos); ese daño no tiene zona |
| `origen` | `inspector` o `conductor` |
| `tipo_novedad` | solo en una novedad del conductor: `golpe`, `piedra`, `granizo` o `incidente_vial`; si no, `null` |
| `nota` | texto libre, máximo 500 |
| `es_preexistente`, `hallazgo_origen_id`, `confirmado_por` | si el daño ya existía, el hallazgo previo del VIN que ya lo tenía y quién lo confirmó |
| `estado_seguimiento` | `pendiente` (al crearse), `validado`, `rechazado`, `recuperado` o `derogado`; lo cambia `POST /api/hallazgos/{id}/seguimiento` |
| `evidencia_completa`, `evidencia_faltante` | `evidencia_faltante` lista lo que impide la evidencia completa (`foto_hallazgo`, `transferencia_sin_cierre`, `transferencia_no_firma`, `transferencia_sin_entrega`); ver «Seguimiento del hallazgo» |
| `revisado_por`, `revisado_at` | quién revisó el hallazgo y cuándo; `null` mientras no se revise |
| `parte_vehiculo`, `coords_diagrama` | **solo en los hallazgos históricos** (anteriores a este modelo): la parte en texto y el punto sobre la imagen anterior. En los nuevos son `null` |

Los hallazgos anteriores se ubicaron por el texto de su parte solo cuando la equivalencia era segura (`Fachada delantera`, `Parachoque delantero` y las llantas); los demás, y todos los que estaban en una zona del dibujo anterior, tienen `zona` con la parte escrita (si la tienen) y `cuadrante_numero` y `lado` en `null` hasta que Calidad apruebe una equivalencia entre la numeración vieja y la nueva.

### `GET /api/inspecciones/{id}/hallazgos-previos`
`inspector`, `coordinador_calidad`, `admin` y `super_admin` (`403` al conductor y al comercial). El inspector solo la pide de inspecciones de **sus viajes** y quien se limita a su cliente (el `coordinador_calidad` y el `admin`), de VIN de **su cliente** (`403` si es de otro); `404` si la inspección no existe. El coordinador la usa para proponer el preexistente al revisar a distancia.

Devuelve los hallazgos de todas las **inspecciones cerradas anteriores del mismo VIN**: las etapas anteriores del mismo viaje y los viajes anteriores, **incluido uno del que la unidad «no salió»**. Es una lista de `HallazgoDto`
(sin `evidencia`) ordenada del cierre más reciente al más antiguo, y cada elemento agrega el contexto de dónde salió:

```json
[
  { "id": "uuid", "inspeccion_id": "uuid", "clase_dano": "rotura", "zona": "Izquierdo 7 · Puerta delantera", "cuadrante_numero": 7, "lado": "izquierdo", "nivel_severidad": "v2", "…": "…",
    "viaje_id": "uuid", "codigo_mtrack": "MTK-001", "proceso": "cargue", "momento": "salida", "cerrada_at": "2026-07-17T12:30:15.123Z",
    "evidencias": [ { /* EvidenciaDto, con su url */ } ] }
]
```

No cuenta lo que aún no se cerró, ni las inspecciones de otro VIN, ni la inspección que se consulta; una inspección corregida aporta su última versión cerrada y no la que reemplazó. Las `evidencias` traen la `url` actual de cada foto
(la URL firmada llegará con la tarea de las fotos privadas). Sin hallazgos previos, `[]`.

**La propuesta de preexistente la hace el cliente.** La app descarga esta lista y, al registrar un hallazgo, la compara por **zona (lado + número) y clase**: si hay uno igual, lo propone como preexistente y el inspector lo confirma
(`es_preexistente: true` con `hallazgo_origen_id` de esa lista) o lo corrige (queda nuevo). En una revisión de un hallazgo del conductor, `POST /api/hallazgos/{id}/revision` admite además `es_preexistente` y `hallazgo_origen_id` con la misma regla. Funciona sin señal con la lista ya descargada; un hallazgo en `derecho 7` no se propone como preexistente de uno en `izquierdo 7`.
Al crear el hallazgo el servidor valida que el origen sea de esa lista, con la misma zona y clase.

### `POST /api/inspecciones/{id}/evidencias`
Campo (creador o administrador). Registra una foto **ya subida** con `POST /api/uploads`: de una **vista** (`vista_id`), de un **hallazgo** (`hallazgo_id`) o de un **extra del vehículo** (`extra_id`), de **una sola** de las tres cosas (`400` en `vista_id` si vienen varias; `404` si el hallazgo o el extra no son de esta inspección). `201` con el `EvidenciaDto`, que trae `extra_id` (nulo en las demás fotos).

```json
{
  "vista_id": "uuid", "storage_path": "3f2b…c1.png", "sello_fecha": "2026-07-17T12:30:15.123Z", "sello_lat": 6.24, "sello_lng": -75.58,
  "precision": 4.5, "dispositivo_id": "tablet-7", "nota": null, "hash_sha256": "…"
}
```

| Campo | Obligatorio | Regla |
|---|---|---|
| `vista_id` | sí, si no hay `hallazgo_id` (salvo en la acomodación) | UUID de una vista **activa** del catálogo `vistas-foto` (una de las 7 obligatorias o de las 2 adicionales) |
| `hallazgo_id` | sí, si no hay `vista_id` (salvo en la acomodación) | UUID de un hallazgo **de esta inspección** (`404` si es de otra) |

En la **segunda inspección del cargue** (la salida de la etapa Cargue) la foto se envía **sin** `vista_id` ni `hallazgo_id`: es una foto de la acomodación en la niñera y cuenta para el mínimo `acomodacion_fotos_minimas` del cierre; las fotos se cuentan **por viaje** (una foto de la niñera sirve para todos los carros). En cualquier otra inspección esa foto responde `400` en `vista_id`.
| `storage_path` | sí | el `storage_path` que devolvió la subida (nombre plano, sin carpetas). **El archivo debe existir**: si no se subió, `400` |
| `sello_fecha` | sí | ISO 8601 con zona horaria: fecha y hora de la foto según el dispositivo |
| `sello_lat` / `sello_lng` | no | −90 a 90 / −180 a 180. Si falta alguna, la foto queda **sin ubicación** (`sin_ubicacion: true`) |
| `precision` | no | precisión del GPS en metros (≥ 0) |
| `dispositivo_id` | no | máximo 100 caracteres |
| `nota` | **sí en las vistas adicionales** | el faltante o daño (piezas en el baúl, cojinería); máximo 500 caracteres |
| `hash_sha256` | no | 64 caracteres hexadecimales. Si se envía, debe coincidir con el del archivo recibido (`400` si no) |

El servidor agrega a cada foto su **sello de servidor** (`sello_servidor_at`, la hora a la que la recibió, que el cliente no puede fijar) y el **hash SHA-256** del archivo tal como quedó
guardado (el mismo que devolvió la subida). Cada foto conserva así su propio sello: fecha, hora y ubicación del dispositivo, y el del servidor.

**Repetir una foto.** Se puede volver a tomar la foto de una vista: la última de cada vista es la que cuenta para el cierre; las anteriores quedan guardadas (son evidencia) pero ya no bloquean.
Es lo que hace la app cuando una foto quedó «sin ubicación».

`EvidenciaDto`:

```json
{ "id": "uuid", "inspeccion_id": "uuid", "hallazgo_id": null, "sello_fecha": "…", "sello_lat": 6.24, "sello_lng": -75.58,
  "url": "/api/uploads/3f2b…c1.png", "created_at": "…", "updated_at": "…",
  "vista_id": "uuid", "hash_sha256": "…", "sello_servidor_at": "…", "precision": 4.5, "dispositivo_id": "tablet-7", "nota": null, "sin_ubicacion": false, "es_constancia": false, "es_sello": false, "extra_id": null, "es_acomodacion": false }
```

`sin_ubicacion` es `true` cuando la foto se tomó sin señal de GPS (`sello_lat` y `sello_lng` valen 0): muéstrela marcada y pida repetirla antes de cerrar. `es_constancia` es `true` en la foto de
una constancia de no firma (se registra con `POST /api/inspecciones/{id}/transferencia/no-firma`, no con este endpoint) y esa foto no cuenta como foto de una vista. `es_acomodacion` es `true` en la foto de la **acomodación** de las unidades en la niñera (la de la segunda inspección del cargue, sin vista, hallazgo ni extra); las fotos anteriores a esta marca quedan en `false` y las del viaje salen en `fotos_acomodacion` de su detalle.

### Extras del vehículo: `POST|PUT|DELETE /api/inspecciones/{id}/extras`
Un **extra** es un elemento del vehículo fuera del dibujo estándar (barras de techo, volco, platón…) que reporta una inspección **en borrador**, con su tipo, sus fotos selladas, una observación y, si está dañado, su daño. **Un extra sin daño no es un hallazgo**: no toca el dibujo y no cuenta en los patrones, el mapa de calor ni la tasa de rechazo. **Un extra dañado sí cuenta** (ver «Daño de un extra»). Lo hacen `inspector`, `conductor` y `admin` (el creador del informe o un administrador); `409` si la inspección está cerrada (los extras quedan inmutables al cierre).

```json
{ "tipo": "otro", "texto": "portabicicletas", "observacion": "en el techo" }
```

| Campo | Obligatorio | Regla |
|---|---|---|
| `tipo` | sí | el `codigo` de un tipo **activo** de `tipos-extra` (`400` en `tipo` si no existe o está inactivo) |
| `texto` | solo con `otro` | obligatorio con el tipo `otro` (máximo 100 caracteres) y **no se admite** con los demás (`400` en `texto`) |
| `observacion` | no | máximo 500 caracteres |
| `danado` | no | `true` marca el extra como dañado; sin él (o en `false`) el extra no tiene daño |
| `clase_dano`, `tamano_dano`, `nivel_severidad` | con `danado: true`, los **tres** para el inspector, el administrador y el super administrador; el **conductor** envía solo `clase_dano` y `tamano_dano` | un valor **activo** de `clases-dano`, `tamanos-dano` y `niveles-severidad` (`400` en cada campo que falte o sea inválido, todos juntos, con los mensajes de siempre); el `nivel_severidad` que envía el conductor responde `400` (ver «Nivel que manda el conductor»); sin `danado: true` no se admiten (`400` en ese campo) |

`POST` responde `201` con el `ExtraDto`: `{ id, inspeccion_id, tipo, tipo_nombre, texto, nombre, observacion, created_at, updated_at, fotos: [EvidenciaDto], dano }`, con `dano` en `null` si no está dañado; `nombre` es cómo se muestra («Platón», «Otro: portabicicletas»). `PUT /api/inspecciones/{id}/extras/{extra_id}` reemplaza los tres campos (`200` con el `ExtraDto`; el tipo actual de un extra se conserva aunque se haya inactivado después). `DELETE` responde `204`; un extra **con fotos o con daño no se quita** (`409`: la foto y el daño son evidencia y no se borran). `404` si el extra no es de esta inspección. Las **fotos** del extra se envían a `POST /api/inspecciones/{id}/evidencias` con `extra_id`, selladas como las demás (fecha, ubicación, precisión, dispositivo, hash y sello del servidor). Cada cambio queda auditado (`extra.crear|actualizar|quitar`).

El detalle de la inspección (`GET /api/inspecciones/{id}`) y el inventario del VIN (`GET /api/vehiculos/{vin}/inventario`, en cada inspección) traen `extras` con el `ExtraDto` de cada uno y sus fotos. El informe PDF y el PDF del inventario los presentan en una sección propia («Extras del vehículo») con su galería sellada, dentro del máximo de 60 fotos. `inspecciones.xlsx` gana la columna **Extras** (cuántos reportó cada inspección). Un extra **sin daño** no entra en ninguna otra exportación, ni en el mapa de calor, los patrones, los indicadores o la tasa de rechazo; el **daño** de un extra entra como cualquier hallazgo.

**Daño de un extra.** Con `danado: true` el extra guarda su daño con **clase, tamaño y nivel de severidad** (los tres obligatorios para el inspector y el administrador; el conductor da la clase y el tamaño y su daño nace **sin nivel**, con `origen: "conductor"` y el nombre del extra como parte, como el daño de una zona suya). El daño es un **hallazgo de siempre** ligado al extra (`extra_id`), del inspector o del conductor: aparece en `GET /api/hallazgos` y en el detalle del informe, cuenta en los patrones, el mapa de calor, las averías y los
indicadores, y cuenta en la tasa de rechazo como cualquier hallazgo (cuando su evidencia está completa). Como un extra no está en el dibujo, **el daño no tiene zona**: en el `HallazgoDto` `cuadrante_numero`, `lado`, `cuadrante_nombre` y `aiag_area` vienen `null` y `zona` trae el **nombre del extra** («Platón», «Otro: portabicicletas»),
para que las pantallas no lo lean como un error; `extra_id` lo identifica como daño de un extra (`null` en los demás). Su foto es la del extra (no pide una foto propia: el extra ya exige al menos una para cerrar la inspección). `ExtraDto.dano` es `{ hallazgo_id, clase_dano, tamano_dano, nivel_severidad }` (`nivel_severidad` es `null` en el daño del conductor mientras no se califique). El daño sin nivel de un extra se califica con `POST /api/hallazgos/{id}/revision` **sin zona** (ver «Revisión de la novedad»); una vez calificado conserva su extra y su nombre y sigue sin zona.
`PUT` puede **agregar** el daño a un extra que aún no lo tiene; uno ya guardado es evidencia y **no se quita ni se cambia** (`409` con `danado: false` o con valores distintos; sin mencionarlo o con los mismos valores se conserva). Los extras reportados **antes** de este cambio siguen válidos y sin daño, sin migración. Un daño de extra queda auditado como `hallazgo.crear`.

**La lista de evidencias.** La lista general de evidencias del informe (`evidencias` de `GET /api/inspecciones/{id}`) **incluye las fotos de los extras**, marcadas con su `extra_id` (y sin `vista_id` ni `hallazgo_id`); además cada extra trae las suyas en `fotos`. No son fotos libres de la acomodación.

### `POST /api/inspecciones/{id}/cerrar`
Campo (creador o administrador). Cierra el informe; después ya no admite cambios. Sin cuerpo. `200`:

```json
{ "inspeccion": { /* InspeccionDto, estado "cerrada", con cerrada_at */ }, "resumen": { "evidencias": 8, "hallazgos": 1 } }
```

Si no cumple los requisitos de la tabla de arriba, responde `400` («La inspección no cumple los requisitos para cerrarse») con la lista de requisitos incumplidos en `details`, **uno por
cada vista que falta, con su nombre**:

```json
{ "code": "VALIDATION_ERROR", "message": "La inspección no cumple los requisitos para cerrarse",
  "details": [ "Falta la foto de la vista «Superior»", "1 foto(s) sin ubicación: repítalas con GPS antes de cerrar" ] }
```

Si ya estaba cerrada, `409`; **un cliente que reintenta el cierre puede tomar ese `409` como éxito.**

---

## Listado de hallazgos

### `GET /api/hallazgos`
Una página de hallazgos para la bandeja del coordinador y las novedades por revisar. Lo consultan el `inspector` (solo los de **sus viajes**), el `coordinador_calidad`, el `comercial` y los administradores (solo los de **su cliente**) y el `super_admin` (todos); el `conductor` recibe `403`.
Ver «Visibilidad por cliente».

```json
{ "items": [ { /* HallazgoListadoDto */ } ], "total": 120, "pagina": 1, "tamano": 50 }
```

| Filtro | Qué trae |
|---|---|
| `estado_seguimiento` | `pendiente`, `validado`, `rechazado`, `recuperado` o `derogado` |
| `origen` | `inspector` o `conductor` |
| `revisado` | `true` o `false`: **solo aplica a las novedades del conductor** (revisadas por un inspector o pendientes de revisar); con `revisado` no entran los hallazgos del inspector |
| `cliente_id` | un cliente: solo lo elige el `super_admin`; quien se limita a su cliente puede enviar el suyo y enviar otro responde `403` |
| `viaje_id`, `vin`, `inspeccion_id` | los de ese viaje, VIN (sin distinguir mayúsculas) o inspección |
| `nivel` | `v1_plus`, `v1`, `v2` o `v3` |
| `sin_calificar` | `true`: los hallazgos **sin nivel** de la versión vigente del informe, de cualquier origen (zona o extra del conductor, novedad, daño migrado del inspector); `false`: los que ya tienen nivel. Cualquier otro valor responde `400`. Se combina con `viaje_id`, `vin`, `proceso`, `inspeccion_id`, `origen`, `revisado` y los demás filtros como una intersección (`sin_calificar=true&revisado=true` no trae nada, porque lo revisado ya tiene nivel); con `nivel` no tiene sentido y `sin_calificar=true&nivel=…` responde `400`. El nombre del filtro es una propuesta, por confirmar |
| `cuadrante_numero`, `lado` | la zona numerada (`lado`: `izquierdo` o `derecho`); un lado sin número trae todas las zonas de ese lado |
| `proceso` | el proceso de la etapa: `cargue`, `transporte`, `descargue`... |
| `desde`, `hasta` | fecha ISO 8601 con zona: el **cierre de la inspección** del hallazgo (una inspección en borrador no tiene cierre y no entra en un rango) |
| `orden` | `recientes` (por defecto: el cierre más reciente primero), `antiguos` o `severidad` (V1+, V1, V2, V3 y al final las novedades sin nivel) |
| `pagina`, `tamano` | página desde 1 (por defecto 1) y tamaño (por defecto 50, máximo **200**: uno mayor se reduce a 200) |

Un filtro con un valor inválido responde `400` con todos los errores juntos en `details.fieldErrors`.

Cada fila (`HallazgoListadoDto`) es el `HallazgoDto` más el contexto de su inspección: `vin`, `viaje_id`, `codigo_mtrack`, `proceso`, `momento`, `inspeccion_estado`, `cerrada_at` (nulo si la inspección está en borrador), `evidencia`, la **foto propia** del hallazgo como `EvidenciaDto` con su `url`
(nula si no tiene; no cuenta la foto del sello), `conductor_nombre` (el conductor de la inspección: el capturado en ella o, si no, el del viaje) y `motivo_sin_inspector` (`{ codigo, nombre, texto }`: por qué esa inspección se hizo sin inspector, como en el detalle del informe). Los dos son `null` cuando no aplican,
así «Por revisar» no obliga a abrir cada informe. El nombre del conductor es un dato personal: va solo en este campo, nunca en textos libres.

**Sin calificar.** El `HallazgoDto` (en el listado, el detalle y el informe) trae `sin_calificar`: `true` cuando el daño **no tiene nivel** (una novedad del conductor aún sin revisar o un daño sin nivel), para que las pantallas lo distingan a simple vista. Un viaje finalizado lo sigue mostrando, y
finalizar un viaje con daños sin calificar funciona igual que siempre. El conductor no ve el nivel y recibe siempre `sin_calificar: false`; el dato `calificado` (verdadero cuando ya tiene nivel) lo ve él también, sin el nivel, y llega en la ficha, el detalle del informe y el inventario.

### `GET /api/hallazgos/{id}`
La misma fila más `seguimiento` (el historial, como en `GET /api/hallazgos/{id}/seguimiento`) y `notas_aclaratorias` (un arreglo que va vacío hasta que existan las notas aclaratorias de una inspección cerrada). `404` si no existe; `403` si es de un viaje ajeno (inspector) o de otro cliente.

## Revisión de la novedad

La novedad que registra el conductor nace sin zona, clase ni nivel V, y el daño que registra en una zona o en un extra nace sin nivel: un inspector lo revisa (lo **califica**), lo ubica si hace falta y le asigna el nivel, que el conductor nunca asigna. A partir de la revisión el hallazgo entra al seguimiento y a los patrones. También se revisan
así los hallazgos que el conductor registra en la salida de un Cargue que él hizo con la excepción «No hay inspector» y en la **llegada del Descargue que hace él** sin inspector en destino (que tampoco traen nivel). La revisión acepta **cualquier hallazgo sin nivel**, venga de donde venga: el daño de una zona del conductor, el de un extra, una novedad o un daño migrado de origen inspector.
Al revisar se puede confirmar que el daño **ya estaba registrado** (`es_preexistente` y `hallazgo_origen_id`); si el conductor lo anotó otra vez en la llegada, no queda como un daño del transporte.

### `POST /api/hallazgos/{id}/revision`

```json
{ "cuadrante_numero": 7, "lado": "izquierdo", "clase_dano": "golpe", "nivel_severidad": "v1", "tamano_dano": "5_10cm", "nota": "Abolladura sobre la puerta" }
```

El daño de un **extra** se califica sin zona: `{ "clase_dano": "golpe", "nivel_severidad": "v2" }`.

Además del inspector del viaje, **el inspector que tiene el reclamo de la entrega de la parada** donde está el carro califica los daños sin nivel que el conductor marcó en su revisión de llegada del Descargue (la que hizo con el plan B antes de que el inspector llegara; ver «La espera del inspector del punto»); con los demás daños de ese viaje otro inspector sigue recibiendo `403` («El VIN del hallazgo no pertenece a ninguno de sus viajes»).

Con «ya estaba registrado»: `{ …, "es_preexistente": true, "hallazgo_origen_id": "uuid" }`.

| Campo | Obligatorio | Regla |
|---|---|---|
| `cuadrante_numero` (+ `lado` si el cuadrante lo lleva) | sí, salvo en un daño que ya tiene zona o que es de un extra | las mismas reglas de la zona de `POST /api/inspecciones/{id}/hallazgos` (el lado lo decide el cuadrante; cuadrante **activo**). Un daño **con zona conserva la suya** si la petición no manda otra (con otra, la cambia); el daño de un **extra no lleva zona**: no se exige y enviarla responde `400` en `cuadrante_numero`; la novedad sin zona sí la exige |
| `clase_dano` | sí | un código **activo** de `clases-dano` (el del daño de una zona o de un extra es el que ya tiene; se vuelve a enviar) |
| `nivel_severidad` | sí | un código **activo** de `niveles-severidad` (`v1_plus`, `v1`, `v2`, `v3`) |
| `tamano_dano` | no | corrige el que eligió el conductor (un código activo de `tamanos-dano`); sin él se conserva el del conductor |
| `nota` | no | máximo 500 caracteres |
| `es_preexistente` + `hallazgo_origen_id` | no | la misma validación de cuando se crea un hallazgo (S6-HU-01.3): `es_preexistente: true` exige `hallazgo_origen_id`, y `hallazgo_origen_id` solo se envía con `es_preexistente: true` (`400` si no). El origen debe ser un hallazgo de una **inspección cerrada anterior del mismo VIN** (cerrada antes de que empezara la que se revisa) con **la misma zona (lado + número) y la misma clase** que se asignan en esa revisión; si no, `400` con `details.fieldErrors.hallazgo_origen_id` |

Responde `200` con el `HallazgoDto` ya completo (un daño de extra conserva su `extra_id`, su nombre en `zona` y sus `cuadrante_numero` y `lado` en `null`): `revisado_por` y `revisado_at` quedan guardados y `estado_seguimiento` sigue `pendiente`. Queda una fila `revision` en el historial del hallazgo (`GET /api/hallazgos/{id}/seguimiento`) con quién la hizo, su `nota` y en
`detalle` lo asignado: `zona_id` (nulo en un extra), `cuadrante_numero`, `lado`, `clase_dano`, `nivel_severidad`, `tamano_dano` y `tamano_dano_anterior` (solo si se corrigió el tamaño); si se confirmó como ya registrado, también `es_preexistente: true`, `hallazgo_origen_id` y `confirmado_por`. El hallazgo queda con `es_preexistente: true`, su `hallazgo_origen_id` y `confirmado_por` (quien revisó) y **no cuenta como daño del transporte**. Queda en la auditoría (`hallazgo.revision`). Sin la revisión, el seguimiento del hallazgo responde `409`.

**Quién revisa.**

| Rol | Qué revisa |
|---|---|
| `inspector` | hallazgos de un VIN que está en alguno de sus viajes (`403` si no) |
| `admin` y `super_admin` | los de su cliente (el `super_admin`, los de cualquiera) |
| `coordinador_calidad` | **solo** los hallazgos **del conductor** (zona, extra o novedad) de la salida de un Cargue que el conductor hizo con la excepción «No hay inspector» o de la **llegada del Descargue que hizo el conductor** (`403` en cualquier otro caso, con el mismo mensaje; un daño sin nivel de origen inspector tampoco): los revisa a distancia con las fotos y les asigna el nivel V antes de que salga el viaje, de modo que un V1+ todavía se puede recuperar o marcar «no sale». Propuesta pendiente de confirmar por el negocio |
| los demás roles | `403` |

Un inspector que no tiene el VIN en sus viajes recibe `403` «El VIN del hallazgo no pertenece a ninguno de sus viajes». Quien se limita a su cliente recibe `403` con un hallazgo de otro cliente. El inspector que tiene el reclamo de una parada del plan B también califica los daños del conductor de las revisiones de esa parada (se agrega con la entrega por parada).

**Nada obliga a calificar.** Ningún proceso exige calificar ni pone plazo: un daño sin nivel no impide cerrar informes, firmar ni finalizar el viaje.

**Errores.** `400` si falta o es inválida la zona, la clase o el nivel (todos los errores juntos en `details.fieldErrors`); `404` si el hallazgo no existe; `409` con `code: "CONFLICT"` y el mensaje «El hallazgo ya tiene nivel» si el hallazgo **ya tiene nivel** (el del inspector nace con él; el texto es una propuesta, por confirmar); `409` con `code: "HALLAZGO_YA_REVISADO"`
si ya fue calificado, también si dos usuarios lo revisan a la vez (solo la primera revisión se guarda y se conserva).

**Hallazgo crítico.** Si el `nivel_severidad` asignado es crítico, se crea una notificación `hallazgo_critico` de urgencia **alta** para cada `coordinador_calidad` y `admin` activo del cliente del vehículo, por bandeja y push (ver [Notificaciones](#notificaciones)).
Los niveles críticos son el parámetro `niveles_criticos` (por defecto `v1_plus,v1`).

## Seguimiento del hallazgo

El coordinador da seguimiento a cada hallazgo y todo cambio queda en un historial que nunca se modifica. Lo hacen `coordinador_calidad`, `comercial`, `admin` y `super_admin` (la capacidad `gestionar_seguimiento` de `GET /api/auth/me`); inspector y conductor reciben `403`.
Solo ven y cambian los hallazgos del **cliente** de su usuario (`403` con uno de otro cliente, ver «Visibilidad por cliente»).

### `POST /api/hallazgos/{id}/seguimiento`
Cambia el estado del hallazgo y responde `200` con el `HallazgoDto` ya actualizado. El listado y el detalle de los hallazgos están en «Listado de hallazgos».

```json
{
  "estado": "validado", "nota": "Validado por Sofasa",
  "evidencia_sello": { "storage_path": "3f2b…c1.png", "sello_fecha": "2026-07-17T12:30:15.123Z", "sello_lat": 6.24, "sello_lng": -75.58, "precision": 4.5, "dispositivo_id": "tablet-7", "hash_sha256": "…" }
}
```

| Campo | Obligatorio | Regla |
|---|---|---|
| `estado` | sí | `validado`, `rechazado`, `recuperado` o `derogado` (`pendiente` es el de partida y no se asigna) |
| `nota` | no | máximo 1000 caracteres |
| `evidencia_sello` | en `validado` de un hallazgo **V2** | la foto del sello de quien valida (Sofasa), ya subida con `POST /api/uploads` (con los mismos campos y reglas que la foto de un hallazgo); sin ella `400` en `evidencia_sello`. Se acepta en cualquier cambio |

**Cada nivel tiene su estado de llegada**, que el servidor expone en `nivel_accion` y **no impone**: el coordinador decide y se admite cualquier estado.

| Nivel | `nivel_accion` | Estado de llegada |
|---|---|---|
| `v1_plus` (V1+) | Se recupera antes del despacho | `recuperado` |
| `v1` (V1) | Se rechaza el vehículo | `rechazado` |
| `v2` (V2) | Validación por Sofasa | `validado` (significa que **Sofasa lo validó**) |
| `v3` (V3) | Se deroga (se acepta el defecto) | `derogado` (propuesta pendiente de confirmar por el negocio) |

Errores: `400` con un estado inválido, una nota larga, un V2 validado sin `evidencia_sello` o una foto no subida; `403` sin la capacidad o con un hallazgo de otro cliente; `404` si el hallazgo no existe; `409` si la inspección del hallazgo está en **borrador**
(el seguimiento empieza al cerrarla) o si es una **novedad del conductor sin revisar** (sin nivel, ningún inspector la ha revisado).

**La foto del sello** queda como una evidencia de la inspección con `es_sello: true`: no es de una vista ni de un hallazgo, no cuenta para el cierre de la inspección y se admite aunque la inspección ya esté **cerrada** (como la constancia de no firma). Aparece en el historial y en el
informe de la inspección. `POST /api/uploads` admite a quien tiene `gestionar_seguimiento`.

**Evidencia completa.** Marcar un hallazgo como rechazado **nunca se bloquea por la evidencia** (un V1 se rechaza en preembarque, antes de que exista una transferencia firmada): la respuesta dice si la evidencia está completa y qué le falta.

| Campo del `HallazgoDto` | Significado |
|---|---|
| `evidencia_completa` | `true` si el hallazgo tiene su **foto propia** (no cuenta la foto del sello) y la transferencia de su inspección se cerró **firmada** o **con reservas** (en las dos firman las dos partes). Una transferencia cerrada como «no firma» no cuenta: la no firma no es evidencia completa (confirmado por el negocio) |
| `evidencia_faltante` | lo que falta: `foto_hallazgo`, `transferencia_sin_cierre` (la transferencia no existe o sigue abierta), `transferencia_no_firma` y `transferencia_sin_entrega` (solo firmó una parte, como en la no firma) |

Se calcula al leer el hallazgo, se guarda al cambiar su seguimiento y se **recalcula cuando se cierra la transferencia** de su inspección. Un hallazgo rechazado sin evidencia completa no cuenta en la tasa de rechazo mientras no la tenga.

### `GET /api/hallazgos/{id}/seguimiento`
Mismos roles. El historial del hallazgo, del más antiguo al más reciente:

```json
[
  { "id": "uuid", "accion": "seguimiento", "estado_anterior": "pendiente", "estado_nuevo": "validado", "usuario_id": "uuid", "usuario_nombre": "Ana Pérez",
    "nota": "Validado por Sofasa", "evidencia_sello": { /* EvidenciaDto con es_sello: true */ }, "detalle": null, "created_at": "2026-07-17T12:30:15.123Z" }
]
```

`accion` es `seguimiento` (el coordinador cambia el estado), `revision` (un inspector revisa una novedad del conductor, con `detalle` en JSON) o `no_sale` (la unidad no sale del viaje por ese hallazgo; su `nota` es el motivo). Una fila no se modifica ni se borra.

## El viaje y el patio: muelle, cupo, destino y eliminar

Los nombres de las rutas, de los avisos y de los motivos (`sin_cupo`, `cargue_firmado`) están **a confirmar por backend** con la app.

### El catálogo `muelles`
Cada lugar tiene sus muelles. Se lee en `GET /api/catalogos` junto a los lugares y se administra con las mismas rutas y los mismos roles que ellos (`coordinador_calidad`, `admin` y `super_admin`; el resto solo lee). Un muelle trae `id`, `codigo` (único **dentro del lugar**: dos lugares pueden tener su «m1»), `nombre`, `orden`, `activo` y `lugar_id`. El `lugar_id` es obligatorio al crear (`400` si falta o el lugar no existe o está inactivo) y **no se cambia** después (`400`: se crea otro muelle y se inactiva este). Un muelle que un viaje usa no se borra (`409`): se inactiva. El catálogo **no viene sembrado**: los muelles de cada lugar los carga el negocio desde Catálogos.

### El muelle del viaje
`ViajeDto` y `ViajeResumenDto` traen `muelle_id` y `muelle` (el nombre); `null` mientras nadie lo asigna. En un lugar de cargue con **al menos un muelle activo**, el cierre de la acomodación (`POST /api/inspecciones/{id}/cerrar` de la segunda inspección del cargue) responde `400` con «Falta el muelle del viaje» en la misma lista de faltantes que «Falta la posición de la unidad …»; en un lugar sin muelles no se exige. Los viajes anteriores se leen con `muelle` nulo.

### `PATCH /api/viajes/{id}/cargue`
El inspector del viaje (o quien reclamó el cargue) y el `coordinador_calidad` cambian el `muelle_id` y el `trailer` durante el cargue. Conductor, `admin` y `super_admin` reciben `403` (TI conserva `PATCH /api/viajes/{id}`). `200` con el `ViajeDto`.

```json
{ "muelle_id": "uuid", "trailer": "TRX-202" }
```

| Campo | Regla |
|---|---|
| `muelle_id` | un muelle **activo del lugar de cargue del viaje** (`400` «El muelle no pertenece al lugar de cargue del viaje»); `null` lo quita |
| `trailer` | el mismo texto de siempre; vacío lo borra |

Se cambia solo lo que se envía; un cuerpo sin ninguno responde `400`. **Al inspector se le acepta solo mientras la entrega del cargue no se haya cerrado**: después responde `409` con `details.motivo: "cargue_firmado"` y «El tráiler y el muelle solo los cambia el inspector durante el cargue»; el coordinador sí puede. Ninguna novedad ni «Llegué» cuenta para esa regla. Queda en la auditoría (`viaje.cambiar_cargue`).

**Aviso `cambio_cargue_inspector`.** Cuando lo cambia el **inspector**, el coordinador de calidad y los administradores del cliente reciben un aviso por cada cosa que cambió, de urgencia `media` y **solo en la bandeja (sin push)**: «{inspector} asignó el muelle 3 al viaje {código}», «… cambió el muelle del viaje {código} de {antes} a {después}» o «… cambió el tráiler del viaje {código} de TRX-101 a TRX-202». `datos`: `viaje_id`, `codigo_mtrack`, `campo` (`muelle` o `trailer`), `antes`, `despues` y `usuario_nombre`. Repetir lo mismo no avisa otra vez; los cambios del propio coordinador no generan aviso.

### No hay más carros que puestos
Crear un viaje, agregar unidades (`POST /api/viajes/{id}/vehiculos`), la carga por Excel y el reemplazo de un «no sale» responden `409` con `details.motivo: "sin_cupo"` cuando las unidades **activas** superarían la capacidad de la niñera, y no agregan ninguna: «La niñera tiene 7 puestos y ya hay 7 unidades: no cabe el VIN 9BWZZZ377VT004251». Una unidad que **no sale** y una **bajada** no cuentan (por eso el reemplazo cabe). En la carga por Excel, la previsualización marca **todas las filas** del viaje con «El viaje {código} tiene 8 carros para 7 puestos: la niñera no los lleva todos» y la carga no lo crea.

### El destino de una unidad cambió
`ViajeVehiculoDto` trae `lugar_destino_cambiado_at`: cuándo se cambió por última vez el destino de esa unidad (`null` si nunca), también en `GET /api/viajes/mios`. `PATCH /api/viajes/{id}/vehiculos/{vin}` con un destino nuevo emite el aviso **`cambio_destino_unidad`** (urgencia `media`, bandeja y push) al **conductor del viaje** y a los **inspectores asignados al lugar nuevo**: «Cambió el destino del VIN {vin}: ahora va a {lugar nuevo}», con `datos` `viaje_id`, `vin`, `lugar_anterior` y `lugar_nuevo`. El conductor lo lee en su bandeja.

### El viaje dice si es eliminable
`ViajeDto` y `ViajeResumenDto` traen `eliminable`: `true` solo si el viaje **no tiene ninguna inspección, en borrador o cerrada**. `DELETE /api/viajes/{id}` no cambia: con una inspección sigue respondiendo su `409`; la app decide además que un viaje finalizado o cancelado no ofrece eliminar.

## Unidad que no sale, vuelve a salir y bajar del viaje

Un carro tiene un **estado dentro del viaje** (`estado_unidad`): `activa`, `no_sale` o `bajado`. Solo una unidad **activa** se acomoda, se revisa, se firma y cuenta en la cobertura, las entregas, los pendientes del inspector, los avisos y la niñera.

- **`no_sale`**: el carro se retira del viaje con un **motivo de catálogo**. Conserva su vínculo, sus etapas e inspecciones y el viaje sigue; libera su puesto. **Puede volver a salir** mientras no se firme el cargue.
- **`bajado`**: el carro **sale del viaje** (un cambio de pedido) conservando su historia (etapas, inspecciones, fotos y firmas) con la marca de cuándo, quién y por qué. Libera su puesto y no cuenta en nada del viaje; el mismo VIN se puede agregar a otro viaje abierto.

Los nombres de las rutas y de los motivos (`cargue_firmado`, `sin_cupo`, `tiene_reemplazo`, `unidad_no_sale`, `unidad_bajada`, `motivo_no_aplica`) están **a confirmar por backend** con la app, y la lista de motivos la confirma el negocio.

### El catálogo `motivos-no-sale`
Se lee en `GET /api/catalogos` y se administra con los mismos roles y rutas que los demás catálogos. Cada motivo trae `codigo`, `nombre`, `orden`, `activo` y tres marcas: `exige_hallazgo`, `exige_texto` y `solo_sin_inspector`.

| `codigo` | `nombre` | `exige_hallazgo` | `exige_texto` | `solo_sin_inspector` |
|---|---|---|---|---|
| `dano_v1_plus` | Daño V1+ | sí | no | no |
| `dano_reportado_conductor` | Daño reportado por el conductor | sí | no | **sí** |
| `no_llego_al_patio` | Carro no llegó al patio | no | no | no |
| `faltan_documentos` | Faltan documentos | no | no | no |
| `no_esta_listo` | Carro no está listo | no | no | no |
| `otro` | Otro | no | **sí** | no |

### `POST /api/viajes/{id}/vehiculos/{vin}/no-sale`
Solo el **inspector del viaje** y el `coordinador_calidad` (de un viaje de su cliente); `403` a los demás, incluidos `admin`, `super_admin`, `comercial` y el conductor.

```json
{ "motivo_codigo": "dano_v1_plus", "motivo_texto": "Golpe estructural en el frente", "hallazgo_id": "uuid", "vin_reemplazo": "9BWZZZ377VT004252" }
```

| Campo | Obligatorio | Regla |
|---|---|---|
| `motivo_codigo` | sí | un código **activo** de `motivos-no-sale`; otro responde `400` «Motivo desconocido» |
| `motivo_texto` | solo con `otro` | máximo 300 caracteres; `otro` sin texto responde `400` «Escribe el motivo». En los demás motivos es opcional |
| `hallazgo_id` | solo en los motivos que lo exigen | `dano_v1_plus`: un hallazgo **V1+** de una inspección **cerrada** de ese VIN en ese viaje y de su **versión vigente**. `dano_reportado_conductor`: un hallazgo de origen conductor, con foto y sin calificar, de ese carro en ese viaje. En los demás motivos no se usa |
| `vin_reemplazo` | no | un VIN que existe en el inventario (`400` en `vin_reemplazo` si no); entra al viaje con sus etapas nuevas, con las reglas de `POST /api/viajes/{id}/vehiculos` |

> Cambio respecto a la versión anterior: el cuerpo ya no trae `motivo` (texto libre de hasta 500 caracteres) y el hallazgo ya no es obligatorio siempre. Los «no sale» marcados antes del cambio se leen con `motivo_codigo: "dano_v1_plus"` y su texto de siempre en `motivo_texto`.

Responde `200` con el `ViajeDto`: la unidad queda con `estado_unidad: "no_sale"`, sin posición, y `no_sale { motivo_codigo, motivo, motivo_texto, hallazgo_id, marcado_por, marcado_por_nombre, fecha, vin_reemplazo }` (`motivo` es la etiqueta legible del catálogo). Con un hallazgo, queda una fila `no_sale` en su historial; siempre queda en la auditoría.

`409` si el hallazgo no corresponde al motivo (no es V1+, no es de ese VIN en ese viaje, su inspección no está cerrada, es de una versión reemplazada con `details.motivo: "hallazgo_de_version_reemplazada"`, o el daño del conductor ya está calificado o no tiene foto), si la unidad **ya no sale** o se bajó, si el VIN de reemplazo **ya está en este viaje o en otro viaje activo** o antes no salió y su hallazgo V1+ no está recuperado. `dano_reportado_conductor` responde `409` con `details.motivo: "motivo_no_aplica"` y «Ese motivo no aplica en este lugar de cargue» cuando el lugar de cargue del viaje no admite el cargue sin inspector (hoy ningún lugar lo admite). `404` si el viaje o el VIN no existen o el VIN no está en el viaje.

**El conductor** recibe el «no sale» sin `hallazgo_id` ni ningún dato del nivel, y con una etiqueta que no lo revela: `motivo` dice «Daño que impide viajar» en el motivo `dano_v1_plus` (el inspector y el coordinador reciben «Daño V1+») y «Daño reportado por el conductor» en el que él reportó; en los demás motivos recibe la etiqueta del catálogo.

### `POST /api/viajes/{id}/vehiculos/{vin}/vuelve-a-salir`
«Vuelve a salir»: devuelve al carro que no sale a `activa`, **sin posición** (se acomoda otra vez) y borra la marca «no sale»; el histórico (motivo, texto, hallazgo, quién y cuándo) queda en la auditoría (`viaje.vuelve_a_salir`). Sin cuerpo. Solo el **inspector del viaje** y el `coordinador_calidad`; `403` a los demás, incluidos `admin` y `super_admin`. Responde `200` con el `ViajeDto`.

`409` si el carro no está «no sale»; con `details.motivo: "cargue_firmado"` y «La entrega del cargue ya está cerrada: el carro va en otro viaje» si **la entrega del cargue del viaje ya se cerró** (la firma por niñera, es decir, todos sus carros activos con la transferencia del cargue cerrada); con `"tiene_reemplazo"` y «El carro ya tiene reemplazo: quita primero el reemplazo» si el carro ya tiene su reemplazo en el viaje; y con `"sin_cupo"` y «La niñera tiene 7 puestos y ya hay 7 unidades» si no hay puesto libre.

### `POST /api/viajes/{id}/vehiculos/{vin}/bajar`
«Bajar del viaje» (un cambio de pedido): saca al carro del viaje.

```json
{ "motivo": "Cambio de pedido" }
```

`motivo` es obligatorio (máximo 300 caracteres, `400` si falta o se pasa). Solo el `coordinador_calidad` (de un viaje de su cliente); `403` a inspector, conductor, `admin` y `super_admin`. Responde `200` con el `ViajeDto`: el carro queda con `estado_unidad: "bajado"`, sin posición, y `bajado { motivo, bajado_por, bajado_por_nombre, fecha }`; conserva sus etapas, inspecciones, fotos y firmas; la auditoría guarda `viaje.bajar_vin`. Un carro que no sale **también se puede bajar**, con o sin revisiones.

`409` con `details.motivo: "cargue_firmado"` y «La entrega del cargue ya está cerrada: el camión ya salió y no se puede bajar ningún carro» si la entrega del cargue **del viaje o la de ese carro** ya se cerró (el mismo motivo que «vuelve a salir»: es lo mismo); ninguna novedad ni «Llegué» cuenta para esta regla. `409` con `"unidad_bajada"` si el carro ya estaba bajado.

### Qué cuenta una unidad bajada
Una unidad `bajado` **no cuenta**: en `vehiculos_completos` ni `total_vehiculos`, en `vwViajesCompletos` ni en `vwCobertura`, en la entrega por punto, en los pendientes del inspector, en «mis viajes» del conductor y del inspector (el viaje no la trae en `vehiculos`), en las unidades activas que limitan la capacidad de la niñera, en los avisos por carro (`transferencia_pendiente`, `captura_incompleta`, `firma_cargue_pendiente`), en la firma por niñera, ni en el informe de la niñera; y su VIN no cuenta como «ya está en otro viaje activo»: el mismo VIN se agrega a otro viaje mientras este sigue abierto. El detalle del viaje (`GET /api/viajes/{id}`) sí la lista, con `estado_unidad: "bajado"` y su `bajado`.

### Un carro que no sale no se revisa ni se firma
Crear, corregir, agregar hallazgos, extras o fotos, cerrar una inspección y firmar o cerrar la transferencia de un carro «no sale» responden `409` con `details.motivo: "unidad_no_sale"` y «La unidad no sale de este viaje»; de un carro bajado, `"unidad_bajada"` y «La unidad se bajó de este viaje». Vuelven a aceptarse después de «vuelve a salir». Una unidad que no sale no cuenta en `vehiculos_completos` ni en `vwViajesCompletos`, y su vínculo no cuenta como «viaje activo» del VIN: se puede asignar a otro viaje, y su inventario muestra el historial completo, incluido el viaje del que no salió.

### `PUT /api/viajes/{id}/vehiculos/{vin}/posicion`
Registra la **posición de la unidad en la niñera** durante la segunda inspección del cargue. Lo hacen el inspector del viaje (o quien reclamó el cargue), el `coordinador_calidad` y los administradores; `403` a los demás.

```json
{ "posicion": 4 }
```

| Campo | Obligatorio | Regla |
|---|---|---|
| `posicion` | sí | un entero de **1 a la capacidad** de la niñera del viaje (7 si no la tiene definida); `null` libera la posición de la unidad |

Responde `200` con el `ViajeDto`: cada unidad trae `posicion` (nulo si no la tiene). `400` en `posicion` («La niñera tiene 8 puestos: la posición debe estar entre 1 y 8») si está fuera de la capacidad del viaje, con el mensaje que nombra esa capacidad.

`409` si la posición **ya la ocupa otra unidad del viaje** («La posición N ya está ocupada por otra unidad de este viaje»; con solicitudes simultáneas por la misma gana exactamente una), si la unidad **no sale** del viaje o si el viaje **no tiene su segunda inspección del cargue en curso** (la de momento `salida` de la etapa Cargue, en borrador). `404` si el viaje o el VIN no existen o el VIN no está en el viaje.

El cierre de esa inspección (`POST /api/inspecciones/{id}/cerrar`) deja de exigir las fotos de las vistas de estado y exige la posición de **cada unidad activa** y al menos `acomodacion_fotos_minimas` fotos de la acomodación **del viaje** (fotos sin vista ni hallazgo de las salidas del cargue de todos sus carros). Si falta algo responde `400` con una línea por cada unidad sin posición («Falta la posición de la unidad {VIN}») y, si no alcanzan las fotos, «Faltan fotos de la acomodación: se exige al menos {n} y hay {k}». Las unidades que no salen no exigen posición.

## Conductor en ruta

El conductor responde por lo que pasa en el recorrido de cada unidad de **su** viaje: registra las **novedades** (golpe, piedra, granizo o incidente vial) y confirma la **llegada**. Las dos viven en la etapa de **Transporte** del VIN:
las novedades van en su inspección de **salida** (que se crea en borrador al registrar la primera) y la llegada cierra esa salida y crea y cierra la inspección de **llegada**. Ambas son solo del `conductor` del viaje (`403` a cualquier otro
usuario, también a un administrador); `404` si el viaje o el VIN no existen o el VIN no está en el viaje, y `409` si el viaje está finalizado o cancelado.

El celular guarda la novedad y la llegada con su fecha, hora y GPS aunque no haya señal y las envía al recuperarla conservando esos datos: la `fecha`, `lat` y `lng` son los del **momento del registro**, no los del envío.

### `POST /api/viajes/{id}/vehiculos/{vin}/novedades`

```json
{
  "fecha": "2026-10-21T08:30:00-05:00", "lat": 4.7, "lng": -74.2,
  "tipo_novedad": "golpe", "tamano_dano": "5_10cm",
  "evidencia": { "storage_path": "abc.png", "sello_fecha": "2026-10-21T08:30:00-05:00", "sello_lat": 4.7, "sello_lng": -74.2, "precision": 5.0, "dispositivo_id": "celular-1", "hash_sha256": "…" }
}
```

| Campo | Obligatorio | Regla |
|---|---|---|
| `fecha`, `lat`, `lng` | sí | fecha ISO 8601 con zona y el GPS del momento (`400` si falta alguno o está fuera de rango) |
| `tipo_novedad` | sí | `golpe`, `piedra`, `granizo` o `incidente_vial` |
| `tamano_dano` | sí | un código **activo** del catálogo `tamanos-dano` |
| `evidencia` | sí | la foto de la novedad ya subida con `POST /api/uploads`, con su sello **completo** (`sello_fecha`, `sello_lat` y `sello_lng`; sin ubicación no se podría cerrar el registro). `nota` no se admite (`400`) |

**No hay texto libre ni se indican cuadrante, clase o nivel**: `nota`, `clase_dano`, `nivel_severidad`, `cuadrante_numero`, `lado`, `zona_provisional`, `es_preexistente` y `hallazgo_origen_id` responden `400` con `Campo no admitido` en ese campo.
El cuadrante, la clase y el nivel V quedan vacíos hasta que un inspector revisa la novedad.

Si el VIN no tiene inspección de **salida de Transporte** en ese viaje, la crea en borrador con la `fecha`, `lat` y `lng` del cuerpo, a nombre del conductor, y para eso exige que la etapa anterior (el Cargue) esté cerrada: `409` con
`code: "ETAPA_PREVIA_SIN_CIERRE"` si no tiene su llegada cerrada o su transferencia no tiene tipo de cierre (la app deja la novedad en espera hasta que el inventario muestre el Cargue cerrado). Las novedades siguientes van a la misma salida.
Un viaje `programado` pasa a `en_curso`. Todo queda en la auditoría (`novedad.crear`, `evidencia.crear`).

Responde `201` con el `HallazgoCreadoDto`: `origen: "conductor"`, `tipo_novedad`, `tamano_dano`, `clase_dano`, `cuadrante_numero`, `lado`, `nivel_severidad`, `nota` y `revisado_at` en `null`, `estado_seguimiento: "pendiente"` y la `evidencia` con su `url`.

`409` más con `code: "UNIDAD_NO_SALE"` si la unidad se marcó «no sale» del viaje, y `409` (`CONFLICT`) si la salida de Transporte ya está cerrada (por la llegada): ya no se agregan novedades.

**Aviso.** Al guardar la novedad se crea una notificación `novedad_conductor` de urgencia **alta** para cada `coordinador_calidad` y `admin` **activo del cliente del vehículo** (ninguno de otro cliente, ni el `comercial`, el `inspector` o el conductor), por
**bandeja** y por **push** a sus dispositivos activos. Si el envío por FCM falla, la petición responde igual y la notificación de push queda con su motivo en `ErrorEnvio`. No se envía correo (el correo es solo del reporte diario).

### `POST /api/viajes/{id}/vehiculos/{vin}/llegada`

```json
{ "fecha": "2026-10-21T14:10:00-05:00", "lat": 4.9, "lng": -74.4 }
```

Confirma la llegada de la unidad: cierra la salida de Transporte si está abierta (con sus novedades) y crea y cierra la inspección de **llegada** de Transporte con la fecha, hora y ubicación recibidas; no pide las vistas de foto. **Sin novedades en el recorrido** (no hay salida de Transporte) también queda la salida: se crea y se cierra «sin novedades» (0 hallazgos) con la misma fecha y el mismo GPS del «Llegué», sin pedir otra ubicación; así una unidad que llega tiene siempre sus dos registros de Transporte.
Responde `200`:

```json
{ "inspeccion": { /* InspeccionDto de la llegada, ya cerrada */ }, "novedades": 2, "sin_novedad": false }
```

`novedades` es cuántas novedades registró el conductor de esa unidad y `sin_novedad` es `novedades == 0`. `400` sin `fecha`, `lat` o `lng` (la app no habilita «Llegué» sin GPS) o si una foto de la salida quedó sin ubicación (se repite antes de confirmar).

| `409` con `code` | Cuándo |
|---|---|
| `LLEGADA_YA_CERRADA` | la llegada de esa unidad ya estaba confirmada. **Es un éxito para un reintento**: la app lo toma como llegada confirmada; el servidor conserva la primera y no repite nada |
| `UNIDAD_NO_SALE` | la unidad se marcó «no sale» del viaje: no se transporta y no admite llegada |

### Estado de transporte de la unidad

Cada VIN de `ViajeDto.vehiculos` agrega `estado_transporte` y `novedades`:

| `estado_transporte` | Cuándo |
|---|---|
| `sin_registro` | el conductor no ha registrado nada de esa unidad |
| `con_novedad` | tiene novedades y aún no confirmó la llegada |
| `sin_novedad` | confirmó la llegada y no tiene novedades |
| `llegada_con_novedad` | confirmó la llegada y tiene novedades |

`novedades` es el número de novedades de la unidad. La llegada de Transporte cerrada cuenta como etapa completa de la unidad.

### Lo que el conductor ve y lo que no ve

El conductor ve, **en modo lectura**, los hallazgos de las inspecciones de **sus viajes** (también los que registró el inspector), para revisar lo registrado y, en el cargue con apoyo, firmar que recibe las unidades en ese estado (decisión del equipo a partir de la validación del conductor, secciones 2.1 y 2.2).
Nada de otros viajes del mismo VIN: el inventario solo trae las etapas de sus viajes y el informe de un viaje ajeno responde `403`.

| Endpoint | Para el conductor |
|---|---|
| `GET /api/vehiculos/{vin}/inventario` y `GET /api/inspecciones/{id}` | los hallazgos de las inspecciones de **sus viajes**, con `zona` (texto y `cuadrante_numero` + `lado`) y `aiag_area`, `clase_dano`, `tamano_dano`, `tipo_novedad`, `origen`, `es_preexistente` y `created_at`, y su foto (`evidencias` con `hallazgo_id` y su URL firmada). Llegan en `null`: `nivel_severidad`, `nivel_nombre`, `nivel_accion`, `estado_seguimiento`, `evidencia_completa`, `evidencia_faltante`, `revisado_por`, `revisado_at`, `nota`, `hallazgo_origen_id` y `confirmado_por`; y sin la foto del sello de quien valida (`es_sello: true`). Trae `calificado`: `true` cuando el hallazgo ya tiene nivel (el conductor ve «Pendiente de calificar» y después «Calificado», nunca el nivel) |
| `GET /api/inspecciones/{id}/pdf` | el PDF lleva esos hallazgos sin nivel, sin seguimiento, sin historial y sin notas aclaratorias de hallazgos |
| `GET /api/inspecciones` | el listado no trae hallazgos |
| `GET /api/inspecciones/{id}/hallazgos-previos`, `GET /api/hallazgos` y `GET /api/hallazgos/{id}` | `403` |

## Notificaciones

Las alertas y los avisos llegan a una **bandeja** (el portal y la app) y, las urgentes, también por **push** al celular. La capacidad `ver_alertas` de `GET /api/auth/me` la tienen `coordinador_calidad`, `admin` y `super_admin` (las alertas preventivas
de su cliente), `inspector` (solo los avisos operativos dirigidos a él: `captura_incompleta` de una inspección que inició y `transferencia_pendiente` de su firma) y `conductor` (solo `respuesta_conductor`, `captura_incompleta` de una inspección que
él inició en la salida de un cargue y `transferencia_pendiente` dirigida a él). `comercial` no la tiene y recibe `403` en estos endpoints. Se generan `reclamo_sin_actividad` (urgencia media, al coordinador de calidad, cuando un camión reclamado lleva `reclamo_sin_actividad_minutos` sin actividad; ver «Reclamo del camión»), `novedad_conductor` y `respuesta_conductor` (ver «Conductor en ruta»), `novedad_proceso` (urgencia alta, por bandeja y push, cuando el conductor hace el cargue sin inspector; ver [Excepción «No hay inspector»](#excepción-no-hay-inspector-y-novedades-de-proceso)), `permiso_sin_inspector` (urgencia alta: el conductor pide el permiso de esa excepción, al inspector del punto y al coordinador del cliente, o se avisa que se concedió; con `datos`: `hecho` `solicitud` o `concesion`), `hallazgo_critico`, `umbral_patron`, `captura_incompleta` y `transferencia_pendiente`, `cargue_sin_reclamar`, `propuesta_ubicacion`, `ubicacion_fijada`, `firma_cargue_pendiente`, `cambio_cargue_inspector`, `cambio_destino_unidad` y `espera_inspector_punto` (urgencia alta, a los coordinadores de calidad del cliente: nadie tomó la entrega de una parada pasados X minutos desde que el conductor activó «No hay inspector»; ver «La espera del inspector del punto») (ver [docs/alertas.md](docs/alertas.md)); `reporte_diario` lo crea el reporte diario.
Todo lo que se hace aquí queda en la auditoría.

`NotificacionDto`:

```json
{
  "id": "uuid", "tipo": "novedad_conductor", "urgencia": "alta", "titulo": "Novedad en ruta: 9BWZZZ377VT004251", "mensaje": "Carlos Gómez registró una novedad de tipo golpe (tamaño 5_10cm) en la unidad … (viaje MTK-001).",
  "viaje_id": "uuid", "codigo_mtrack": "MTK-001", "vin": "9BWZZZ377VT004251", "hallazgo_id": "uuid", "inspeccion_id": "uuid",
  "leida_at": null,
  "respuesta": { "texto": "Detente en el siguiente peaje", "medio": "app", "usuario_nombre": "Ana Ruiz", "fecha": "2026-10-21T14:12:00Z" },
  "created_at": "2026-10-21T13:31:00Z",
  "datos": null
}
```

`datos` son los **campos propios del aviso**, además del texto, para que el portal y la app no tengan que leer el mensaje: un objeto con claves en `snake_case` y `null` en los avisos que no los llevan. Hoy los llevan dos avisos. El de la entrega con una sola firma (`novedad_proceso`, ver «Entrega por punto»):
`{ "hecho": "entrega_con_una_firma", "lugar_id": "…", "lugar": "…", "parte_que_firmo": "entrega", "motivo": "…", "unidades": 3 }`. Y el de **umbral superado** (`umbral_patron`): `{ "tipo_umbral": "zona", "cuadrante_numero": 4, "lado": "izquierdo", "proceso": null, "total": 12, "umbral": 10, "ventana_dias": 30 }`
cuando la zona (lado y número de cuadrante) llega al umbral, o `{ "tipo_umbral": "proceso", "cuadrante_numero": null, "lado": null, "proceso": "cargue", … }` cuando es el proceso; con esos campos el portal abre el tablero ya filtrado (`/api/reportes/patrones?proceso=…`). Un dato personal (como la cédula) nunca va en `datos`.

`tipo`: `novedad_conductor`, `respuesta_conductor`, `hallazgo_critico`, `umbral_patron`, `captura_incompleta`, `transferencia_pendiente`, `reporte_diario`, `novedad_proceso`, `reclamo_sin_actividad`, `fuera_de_rango`, `cargue_sin_reclamar`, `propuesta_ubicacion`, `permiso_sin_inspector`, `firma_cargue_pendiente`, `cambio_cargue_inspector`, `cambio_destino_unidad`, `carro_rechazado_en_destino`, `ubicacion_fijada`, `espera_inspector_punto` o `viaje_sin_movimiento` (la lista completa de lo que el servidor emite). `urgencia`: `alta` o `media`. `respuesta` es `null` mientras nadie responde la novedad.

### `GET /api/notificaciones`
Una página de la bandeja, las más recientes primero: `{ "items": [NotificacionDto], "total": 3, "pagina": 1, "tamano": 50, "no_leidas": 2 }`.

| Filtro | Qué trae |
|---|---|
| `leida` | `true` o `false` |
| `tipo` | un tipo de notificación |
| `urgencia` | `alta` o `media` |
| `pagina`, `tamano` | página desde 1 (por defecto 1) y tamaño (por defecto 50, máximo **200**: uno mayor se reduce a 200) |

Cada usuario ve **solo las suyas**; el `super_admin` ve las de **todos los clientes**, pero **una sola copia de cada aviso**: si una novedad se envió a tres personas del cliente, él la ve una vez y no tres veces iguales (de cada aviso queda la primera copia enviada; cada destinatario conserva la suya). `no_leidas` es cuántas hay sin leer en ese alcance, sin contar los filtros. Un filtro inválido responde `400` con todos los errores juntos en `details.fieldErrors`.
Solo se listan las de la bandeja: las filas de push son el registro del envío.

### `POST /api/notificaciones/{id}/leida`
Marca la notificación como leída y responde `200` con el `NotificacionDto`. Solo su destinatario (`403` si es de otro, también para el `super_admin`); leerla otra vez no cambia la primera `leida_at`. `404` si no existe.

### `POST /api/notificaciones/{id}/respuesta`

```json
{ "texto": "Detente en el siguiente peaje y espera al inspector", "medio": "app" }
```

Responde la **novedad del conductor**. Solo `coordinador_calidad`, `admin` y `super_admin`, de una novedad de su cliente (`403` a los demás roles y a quien es de otro cliente; el `super_admin` responde la de cualquier cliente).

| Campo | Obligatorio | Regla |
|---|---|---|
| `texto` | sí | máximo 1000 caracteres |
| `medio` | sí | `app` (respuesta escrita) o `llamada` (atendió por teléfono) |

Responde `200` con el `NotificacionDto` ya con su `respuesta`. La respuesta queda en la **novedad**: la ven (y la alerta queda atendida para) todos los coordinadores y administradores del cliente. **Solo la primera respuesta se guarda**: otra respuesta a la misma
novedad, aunque la envíe otro coordinador o llegue a la vez, responde `409` y conserva la primera. `409` también si la notificación no es de una novedad del conductor. Al responder se crea una notificación `respuesta_conductor` de urgencia **alta** para el conductor del
viaje, por bandeja y por push (el texto de la respuesta es su mensaje).

### Dispositivos

El celular del usuario registra su token de FCM para recibir los push.

#### `POST /api/dispositivos`

```json
{ "token_fcm": "…", "plataforma": "android", "modelo": "Pixel 8" }
```

Cualquier usuario autenticado. `plataforma`: `android`, `ios` o `web`; `modelo` es opcional (máximo 100 caracteres) y `token_fcm` máximo 512; `400` si falta el token o la plataforma es inválida. Registra el dispositivo o, si el token ya existe, lo **actualiza**:
queda activo y a nombre de quien lo registra (un celular que cambia de usuario pasa al nuevo). Responde `200` con `{ id, plataforma, modelo, activo, ultimo_uso_at }`.

#### `DELETE /api/dispositivos/{token_fcm}`
Se llama al cerrar sesión: el dispositivo deja de recibir push. Responde siempre `204`, también si el token no existe, ya estaba retirado o es de otro usuario (no hace nada).

### Envío del push

Solo los avisos de urgencia `alta` salen por push; los de urgencia `media` quedan solo en la bandeja (no dejan fila de push). El push sale por **Firebase Cloud Messaging** a cada dispositivo activo del destinatario, con `tipo`, `notificacion_id`, `hallazgo_id` y `viaje_id` como datos para abrir la alerta. Si FCM falla, no está configurado o el usuario no tiene dispositivos, la petición que originó
la alerta responde igual y la notificación de push guarda el motivo en `ErrorEnvio` (un token que FCM ya no reconoce deja el dispositivo inactivo). Una misma alerta no se repite: su clave (alerta, destinatario y canal) es única. Se configura con
`Notificaciones__FcmProyectoId`, `Notificaciones__FcmClientEmail` y `Notificaciones__FcmClavePrivada` (ver [docs/configuracion.md](docs/configuracion.md)).

## Ubicación de los lugares

La ubicación de un lugar (`latitud`, `longitud`, `radio_metros`) tiene un estado, `estado_ubicacion`, que el catálogo `lugares` devuelve junto con quién la fijó (`ubicacion_fijada_por`, y su nombre en `ubicacion_fijada_por_nombre`, para leer «Ubicación confirmada por Ana Restrepo» sin ningún identificador) y cuándo (`ubicacion_fijada_at`); ambos son `null` mientras nadie la fija:

| `estado_ubicacion` | Qué significa | Cómo se llega |
|---|---|---|
| `sin_ubicacion` | el lugar no tiene coordenadas | estado inicial; también al quitar las coordenadas |
| `pendiente_confirmar` | alguien en el sitio dejó su posición con «fijar ubicación aquí»; **todavía no genera marcas de «fuera del rango»** | `POST /api/lugares/{id}/ubicacion` |
| `confirmada` | la ubicación es la oficial: la «confirmación en sitio» la usa | la carga manual por el catálogo o confirmar una propuesta de autocalibración |

**Carga manual.** `PATCH /api/catalogos/lugares/{id}` (y el `POST` de creación) aceptan `latitud` (−90 a 90), `longitud` (−180 a 180) y `radio_metros` (1 a 100.000); los pueden cambiar `admin`, `super_admin` y `coordinador_calidad`, como el resto del catálogo. Las coordenadas van **juntas o ninguna** (`400`), el radio necesita coordenadas (`400`) y lo que no se envía se conserva. Con coordenadas la ubicación queda `confirmada` con el autor y la fecha; enviando `latitud`, `longitud` y `radio_metros` en `null` el lugar vuelve a `sin_ubicacion`.

### `POST /api/lugares/{id}/ubicacion`
«Fijar ubicación aquí»: quien está en un lugar **sin ubicación** deja la suya. Roles: `inspector`, `conductor`, `coordinador_calidad`, `admin` y `super_admin` (`comercial` recibe `403`).

```json
{ "lat": 6.1706, "lng": -75.5874, "precision": 18.5 }
```
`lat` y `lng` son obligatorios (`400` si faltan o están fuera de rango); `precision` (metros, opcional, no negativa) es la del GPS: queda **guardada en el lugar** (`precision_m`, nula en los lugares fijados antes, cargados a mano o con otras coordenadas) y en la auditoría (`lugar.fijar_ubicacion`). `200` con el lugar: `{ "id", "nombre", "latitud", "longitud", "radio_metros": null, "estado_ubicacion": "pendiente_confirmar", "ubicacion_fijada_por", "ubicacion_fijada_por_nombre", "ubicacion_fijada_at", "precision_m" }`. `404` si el lugar no existe. `409` si el lugar ya tiene ubicación (pendiente o confirmada): una ubicación existente la cambia quien administra el catálogo.

**Aviso `ubicacion_fijada`** (código por confirmar). Al fijar la ubicación se avisa a los `coordinador_calidad` y `admin` activos del cliente de quien la fijó (los mismos destinatarios que `propuesta_ubicacion`, más los administradores), de urgencia `media` y **solo en la bandeja**: título «Ubicación fijada: {lugar}», mensaje con quién, cuándo y «precisión N m», y `datos`: `lugar_id`, `lugar`, `usuario_nombre`, `precision_m` y `fijada_at`. Uno por lugar (clave de deduplicación por lugar); el `409` de la segunda fijación no cambia y no avisa. `precision_m` también sale en el catálogo `lugares` (`GET /api/catalogos/lugares`) y en cada propuesta de `GET /api/lugares/propuestas` (la precisión con que se fijó la ubicación actual del lugar; `null` si no se guardó).

### Aviso `viaje_sin_movimiento`
Al coordinador de calidad de cada cliente de las unidades del viaje (urgencia `media`, solo bandeja; código por confirmar). Título «Viaje sin movimiento: {código_mtrack}» y un mensaje con las horas que lleva sin movimiento. `datos`: `viaje_id`, `codigo_mtrack`, `ultimo_movimiento_at` (ISO 8601) y `horas_sin_movimiento` (entero). **El último movimiento** es lo más reciente entre una inspección creada o cerrada del viaje (la llegada y la salida de Transporte incluidas), un daño o novedad de una de ellas, una novedad de proceso y una firma de entrega o de recibo; si no hay ninguno, la última vez que se tocó el viaje. Un solo aviso por viaje y último movimiento: no se repite mientras no haya un movimiento nuevo (que fija otro «último movimiento» y puede avisar de nuevo pasadas otras 24 horas), y nunca se emite para un viaje programado, finalizado o cancelado.

### Autocalibración: propuestas de ubicación
Cada hora el servidor toma las fotos selladas de las vistas de Cargue y Descargue (lugar de cargue del viaje y destino de la unidad) con ubicación y con precisión menor que `valvula_precision_m`, de los lugares **activos cuya ubicación no está confirmada**. Si un lugar reúne fotos de **al menos `propuesta_ubicacion_visitas` visitas distintas** (viajes; 3 por defecto, ajustable sin desplegar), propone el **centro** en la mediana de las coordenadas y el **radio** que cubre al 90 % de las fotos (redondeado hacia arriba a 10 m, entre 100 y 5.000 m), crea una propuesta `pendiente` y avisa al `coordinador_calidad` del cliente (`propuesta_ubicacion`, urgencia media, solo bandeja). **El lugar no cambia** mientras la propuesta esté pendiente. No se repite una propuesta pendiente ni se vuelve a proponer tras una decisión sin fotos nuevas.

`PropuestaUbicacionDto`: `{ "id", "lugar_id", "lugar", "centro_lat", "centro_lng", "radio_metros", "fotos", "visitas", "estado", "decidida_por", "decidida_por_nombre", "decidida_at", "created_at" }` (`decidida_por`, `decidida_por_nombre` y `decidida_at` son `null` mientras está pendiente).

| Endpoint | Roles | Qué hace |
|---|---|---|
| `GET /api/lugares/propuestas?estado=pendiente` | `coordinador_calidad`, `admin`, `super_admin` | lista, las más recientes primero; `estado` (`pendiente`, `confirmada` o `descartada`) es opcional (`400` si es otro) |
| `POST /api/lugares/propuestas/{id}/confirmar` | los mismos | el lugar queda con ese centro y radio como ubicación `confirmada` (auditoría `lugar.confirmar_propuesta`) |
| `POST /api/lugares/propuestas/{id}/descartar` | los mismos | la propuesta queda `descartada` y el lugar no cambia (auditoría `lugar.descartar_propuesta`) |

`404` si la propuesta no existe; `409` si ya estaba decidida (una propuesta decidida no se decide otra vez, ni se modifica ni se borra).

## Alertas

El sistema avisa de lo que requiere atención sin que nadie revise listados. Las alertas y los avisos **no tienen endpoints propios**: se guardan como notificaciones y se leen en la bandeja (`GET /api/notificaciones`, ver «Notificaciones»); aquí está qué genera cada una y a quién llega. El detalle de los procesos está en
[docs/alertas.md](docs/alertas.md).

| `tipo` | Clase | Urgencia | A quién llega | Cuándo |
|---|---|---|---|---|
| `hallazgo_critico` | preventiva | `alta` | `coordinador_calidad` y `admin` activos del cliente de la unidad | al **cerrar** una inspección con un hallazgo de nivel crítico y al **revisar** una novedad del conductor con ese nivel; una por inspección |
| `umbral_patron` | preventiva | `media` | `coordinador_calidad` y `admin` activos del cliente | cada hora, cuando los hallazgos de una zona (lado + número) o de un proceso, de ese cliente, llegan al umbral dentro de la ventana; una vez por ventana |
| `captura_incompleta` | operativo | `media` | quien inició la inspección (el inspector o, solo en un Cargue sin personal de apoyo, el conductor) | cada 10 minutos, si la **llegada** del Cargue o la **llegada** del Descargue sigue en borrador y le faltan fotos de vistas obligatorias; una por inspección. **La acomodación** (la salida del Cargue) **no cuenta**: no exige vistas (sus fotos son las de la niñera) |
| `transferencia_pendiente` | operativo | `media` | el firmante que falta (o, si aún no hay transferencia, el conductor o el inspector del viaje) | cada 10 minutos, si una llegada cerrada sigue sin transferencia con tipo de cierre; una por inspección y destinatario |
| `cargue_sin_reclamar` | operativo | `media` | `coordinador_calidad` activos del cliente de las unidades del viaje | cada 10 minutos, si un cargue programado lleva `cargue_sin_reclamar_minutos` (30) pasada su hora programada, sin reclamo activo ni inspección iniciada (solo si esa hora fue en las últimas 24 horas); uno por viaje |
| `firma_cargue_pendiente` | operativo | `media` | el inspector del viaje (si falta quien entrega) o su conductor (si falta quien recibe) | cada 10 minutos, si la salida del cargue de un carro lleva `firma_cargue_pendiente_minutos` (30) cerrada sin que su transferencia esté cerrada; uno por viaje y parte, con los VIN que faltan (ver «Firma del cargue por niñera») |
| `propuesta_ubicacion` | operativo | `media` | `coordinador_calidad` activos del cliente | cada hora, cuando la autocalibración propone el centro y el radio de un lugar (ver «Ubicación de los lugares»); uno por propuesta |
| `viaje_sin_movimiento` | operativo | `media` | `coordinador_calidad` activos del cliente de las unidades del viaje | cada 10 minutos, si un viaje `en_curso` lleva `viaje_sin_movimiento_horas` (24 por defecto) sin ningún movimiento: ni una inspección creada o cerrada (la llegada y la salida de Transporte son inspecciones), ni un daño o novedad, ni una novedad de proceso, ni una firma de entrega o de recibo; solo bandeja, uno por viaje y último movimiento (código por confirmar) |

Los umbrales y los tiempos de espera son **parámetros** que se cambian sin desplegar (`niveles_criticos`, `umbral_patron_cantidad`, `umbral_patron_ventana_dias`, `captura_incompleta_minutos`, `transferencia_pendiente_minutos` y `firma_cargue_pendiente_minutos` y `viaje_sin_movimiento_horas`; ver «Parámetros»). Las alertas preventivas nunca llegan a un usuario de otro cliente
(ver «Visibilidad por cliente»). Cada una lleva su clave de dedupe: correr un proceso otra vez, o dos instancias a la vez, no repite nada. Llegan por **bandeja** y, solo si su urgencia es `alta`, también por **push** a los dispositivos activos del destinatario (los de urgencia `media` quedan solo en la bandeja y no dejan fila de push); **nunca por correo** (el único correo es el reporte diario).

## Notas aclaratorias

Una aclaración de un registro ya guardado (una inspección, un hallazgo o una transferencia) es un **registro nuevo** con su autor y su fecha: **el original no cambia** (aunque su inspección esté cerrada) y **nadie edita ni borra una nota**
(no existen `PATCH` ni `DELETE`; la base de datos también lo impide). Cada nota queda en la auditoría (`nota.crear`).

`NotaAclaratoriaDto`:

```json
{ "id": "uuid", "entidad": "hallazgo", "entidad_id": "uuid", "autor_id": "uuid", "autor_nombre": "Ines Pérez", "autor_rol": "inspector", "texto": "Se aclara que el golpe ya estaba al recibir", "created_at": "2026-10-21T14:12:00.000Z" }
```

| Ruta | Agrega una nota a | `entidad_id` |
|---|---|---|
| `POST /api/inspecciones/{id}/notas` | la inspección | el id de la inspección |
| `POST /api/hallazgos/{id}/notas` | el hallazgo | el id del hallazgo |
| `POST /api/inspecciones/{id}/transferencia/notas` | la transferencia de la inspección | el id de la transferencia (`404` si la inspección aún no tiene) |

Cuerpo: `{ "texto": "…" }`, obligatorio y de **hasta 1000 caracteres** (`400` en `texto` si falta o excede). Responde `201` con el `NotaAclaratoriaDto`, también con la inspección cerrada.

**Quién agrega.** El **autor del registro** (el usuario de la inspección; para un hallazgo, el de su inspección; para la transferencia, quien la **firmó**: quien entrega si ya firmó su parte y quien recibe si ya firmó la suya), el
`admin`, el `super_admin` y el `coordinador_calidad` (confirmado por el negocio) de su cliente. Cualquier otro rol o usuario recibe `403` y la nota no se guarda; `403` también con un registro de un VIN de otro cliente.
`404` si la inspección, el hallazgo o la transferencia no existen.

**Dónde se ven.** `GET /api/inspecciones/{id}/notas`, `GET /api/hallazgos/{id}/notas` y `GET /api/inspecciones/{id}/transferencia/notas` devuelven la lista, de la más antigua a la más reciente, a quien puede ver el registro (el inspector y el
conductor, solo en sus viajes; el conductor no ve las notas de los hallazgos que registraron otros; el resto, de su cliente). Además traen `notas_aclaratorias[]` el informe (`InformeDetalleDto`, las de la inspección), cada inspección del inventario
(`InventarioInspeccionDto`), el detalle del hallazgo (`GET /api/hallazgos/{id}`) y la transferencia del informe (`TransferenciaDetalleDto`).

## Parámetros

Los valores que el negocio ajusta **sin desplegar código** (el objetivo diario, los destinatarios y la hora del reporte diario, el horario de operación, la vigencia de las URL firmadas, los umbrales de las alertas...). Los ven y los cambian
`admin`, `super_admin` y `coordinador_calidad` (la capacidad `administrar_catalogos` de `GET /api/auth/me`); cualquier otro rol recibe `403`. Las claves las crea la base de datos: **la API solo cambia valores**, no crea ni borra claves
(`POST`, `PUT` y `DELETE` responden `405`).

### `GET /api/parametros`
Todos los parámetros, ordenados por `clave`.

```json
[
  { "clave": "reporte_diario_hora", "valor": "06:00", "tipo": "hora", "descripcion": "Hora (hora de Colombia) a la que se envía el reporte diario",
    "actualizado_por": null, "updated_at": "2026-10-01T00:00:00.000Z" },
  { "clave": "reporte_diario_destinatarios", "valor": null, "tipo": "correos", "descripcion": "…", "actualizado_por": "uuid", "updated_at": "2026-10-22T14:12:00.000Z" }
]
```

`valor` es siempre texto (o `null` si el parámetro no tiene valor). `actualizado_por` es el usuario que lo cambió por última vez (`null` en el valor inicial).

### `PATCH /api/parametros/{clave}`
Cuerpo `{ "valor": "07:30" }` (texto o número). Responde `200` con el parámetro ya actualizado. El valor se valida según el `tipo` del parámetro y se guarda normalizado; si no lo cumple responde `400` con el mensaje en `details.fieldErrors.valor` y el parámetro **no cambia**.

| `tipo` | Qué se acepta | Se guarda como |
|---|---|---|
| `numero` | entero de 1 a 1.000.000 | el entero (`007` pasa a `7`) |
| `porcentaje` | de 0 a 100, con punto decimal (hasta 4 decimales) | el número (`1.50` pasa a `1.5`) |
| `hora` | `HH:mm` de `00:00` a `23:59` | tal cual |
| `lista` | códigos separados por coma que **existan** en su catálogo (`niveles_criticos`: el catálogo `niveles-severidad`) | sin espacios ni repetidos |
| `correos` | direcciones válidas separadas por coma | en minúsculas, sin espacios ni repetidos |
| `texto` | hasta 1000 caracteres | tal cual |

- `null` (o un texto vacío) deja el parámetro **sin valor**, pero solo en `reporte_diario_destinatarios` (sin destinatarios no se envía el reporte) y `retencion_dias` (aún no definida). En los demás responde `400`.
- `400` también si falta `valor`, si no es texto ni número o si el cuerpo trae otro campo (`tipo`, `clave`...: no se cambian). `404` si la clave no existe.
- Dar el mismo valor que ya tiene responde `200` y no cambia ni audita nada.
- **Vigencia.** El cambio se aplica en **un minuto o menos** en las alertas, la vigencia de las URL firmadas y los demás procesos que leen el parámetro (la lectura se guarda un minuto; quien lo cambia lo ve de inmediato en esa instancia y las demás instancias de
  la API lo toman al vencer el minuto), sin desplegar.
- **Auditoría.** Cada cambio queda en la auditoría (`parametro.actualizar`, `entidad` `parametros`) con el valor antes y después en `detalle`: `{ "antes": { "parametro": "reporte_diario_hora", "valor": "06:00" }, "despues": { "parametro": "reporte_diario_hora", "valor": "07:30" } }`.

### Parámetros de los tiempos y los avisos
Seis parámetros numéricos (`tipo` `numero`, entero de 1 a 1.000.000; se cambian con `PATCH /api/parametros/{clave}` con la validación y la auditoría de siempre) que TI ajusta sin desplegar; cada regla los lee en el momento (caché de un minuto):

| Clave | Valor inicial | Unidad | Para qué sirve |
|---|---|---|---|
| `permiso_sin_inspector_minutos` | 10 | minutos | espera del permiso «No hay inspector» del cargue antes de que el sistema lo conceda por tiempo; **cada solicitud guarda el plazo con que nació** (ver «Excepción «No hay inspector»») |
| `espera_inspector_aviso_minutos` | 15 | minutos | X: desde que el conductor activa la excepción en un destino sin que nadie tome la entrega, antes de avisar al coordinador |
| `espera_inspector_plan_b_minutos` | 30 | minutos | Y: espera adicional, después de X, para habilitar el plan B (la revisión de llegada del conductor y el cierre sin inspector del punto) con X más Y minutos cumplidos |
| `viaje_sin_movimiento_horas` | 24 | horas | espera para avisar de un viaje en curso sin movimiento |
| `firma_cargue_pendiente_minutos` | 30 | minutos | espera para avisar de la firma del cargue pendiente |
| `propuesta_ubicacion_visitas` | 3 | visitas | visitas distintas con fotos selladas que hacen falta para proponer la ubicación de un lugar |

Hoy leen su valor el permiso del cargue, la autocalibración de la ubicación y el aviso de la firma del cargue; los plazos del destino (`espera_inspector_*`) los calcula la función `clsEsperaDelDestino` en cada petición que necesita saber si el plan B ya está habilitado (la firma por parada, el cierre con una sola firma y la revisión de llegada del conductor; sin esperar a ningún proceso periódico) y el proceso de cada minuto que avisa de la espera (ver «La espera del inspector del punto»); el aviso de viaje sin movimiento lo lee su proceso (`viaje_sin_movimiento_horas`, 24 por defecto si la fila no existe).

### `GET /api/parametros/app`
Los dos parámetros que la app lee del servidor en lugar de llevarlos fijos al compilar (nombre de la ruta por confirmar). La leen el `inspector`, el `conductor`, el `coordinador_calidad`, el `admin` y el `super_admin` (`403` al resto; `401` sin sesión). Responde `200` con **solo** estos dos, con su valor vigente (caché de un minuto; un cambio se ve en un minuto o menos):

```json
{ "acomodacion_fotos_minimas": 1, "gps_precision_minima_m": 30 }
```

`acomodacion_fotos_minimas` es el mínimo de fotos de la acomodación que exige el cierre de la segunda inspección del cargue (1 por defecto) y `gps_precision_minima_m` la precisión, en metros, a partir de la cual se avisa de precisión baja (30 por defecto).

## Auditoría

Toda acción de escritura (y el inicio de sesión) queda en un registro **que nadie modifica ni borra** (no existen `PATCH`, `PUT` ni `DELETE`; la base de datos también lo impide). Cada registro guarda quién actuó, la **IP** y el **dispositivo**
(`X-Dispositivo-Id` o, si no llega, el `User-Agent`; ambos `null` en las acciones del sistema, como la ingesta Sofasa), el **detalle** del cambio y el **viaje** y el **cliente** de lo auditado. El detalle nunca incluye contraseñas, PIN ni tokens.

Acciones registradas: inicio de sesión, PIN errado y bloqueo (`auth.*`); usuarios (`usuario.*`); catálogos (`catalogo.{nombre}.*`); vehículos (`vehiculos.cargar`, `vehiculo.baja`); viajes (`viaje.*`, `viajes.cargar`);
inspecciones (`inspeccion.crear`, `inspeccion.cerrar`, `inspeccion.corregir`, `inspeccion.descartar`, `inspeccion.superar`, `llegada.registrar`); hallazgos y novedades (`hallazgo.crear`, `novedad.crear`, `hallazgo.revision`, `hallazgo.seguimiento`); evidencias (`evidencia.crear`);
transferencias (`transferencia.firmar`, `transferencia.reservas`, `transferencia.no_firma`, `transferencia.sin_entrega`); notas aclaratorias (`nota.crear`); notificaciones (`notificacion.leer`, `notificacion.responder`); dispositivos y la ingesta Sofasa.

### `GET /api/auditoria`
Capacidad `ver_auditoria` de `GET /api/auth/me`: la tienen `admin` (ve la de **su cliente**) y `super_admin` (ve la de todos); cualquier otro rol recibe `403`. Una página, de la más reciente a la más antigua.

| Parámetro | Descripción |
|---|---|
| `usuario_id` | quien actuó (UUID) |
| `entidad`, `entidad_id` | el registro auditado: `entidad` es `inspecciones`, `hallazgos`, `viajes`, `usuarios`, `vehiculos`, `notas_aclaratorias`… y `entidad_id` su UUID |
| `viaje_id` | todo lo auditado de un viaje (UUID) |
| `accion` | una acción exacta, por ejemplo `inspeccion.cerrar` |
| `desde`, `hasta` | rango de fechas ISO con zona (`desde` no puede ser posterior a `hasta`) |
| `pagina`, `tamano` | desde 1 (50 por defecto, máximo 200) |

Un filtro mal formado responde `400` con el campo en `details.fieldErrors`.

```json
{
  "items": [
    { "id": "uuid", "usuario_id": "uuid", "usuario_nombre": "Ines Pérez", "rol": "inspector", "accion": "inspeccion.cerrar", "entidad": "inspecciones", "entidad_id": "uuid",
      "viaje_id": "uuid", "codigo_mtrack": "MTK-001", "cliente_id": "uuid", "ip": "203.0.113.7", "dispositivo": "tablet-bodega-3",
      "detalle": { "antes": { "estado": "borrador", "cerrada_at": null }, "despues": { "estado": "cerrada", "cerrada_at": "2026-07-17T12:30:15.123Z" } },
      "created_at": "2026-07-17T12:30:15.123Z" }
  ],
  "total": 1, "pagina": 1, "tamano": 50
}
```

`detalle` es `null` en lo que no cambia datos (un inicio de sesión) y, dentro, `antes` es `null` en un alta y `despues` en una baja. `usuario_id` es `null` en las acciones del sistema.

### `GET /api/auditoria/{id}`
Un registro con su detalle. `404` si no existe; `403` si es de otro cliente (el `super_admin` ve cualquiera) o el rol no tiene la capacidad.

## Transferencia de custodia

Entrega y recibo de la unidad entre dos personas **del viaje** (su conductor y su inspector; `GET /api/viajes/{id}/firmantes` dice quiénes). **Cada firma la envía su titular**: la
identidad sale del token y **nadie firma por otro** (`403`). Cada firma guarda su propia **fecha y la IP** desde la que se envió. La transferencia se **cierra** con un tipo de cierre
(`tipo_cierre`) y, cerrada, es inmutable; mientras no tiene tipo de cierre está **abierta** y **bloquea la salida de la etapa siguiente**.

| `tipo_cierre` | Cómo se llega |
|---|---|
| `firmada` | ambas partes firman |
| `con_reservas` | ambas firman y quien recibe dejó sus observaciones (`reservas`) |
| `no_firma` | una parte se negó a firmar: se deja la constancia (motivo, testigo con usuario y foto) |
| `recibo_sin_entrega` | el conductor hizo la salida del Cargue y nadie de Botero Soto firma la entrega: solo firma quien recibe (ver `POST …/transferencia/sin-entrega`). Trae `no_firma_parte: "entrega"` (la parte que no firmó) y `motivo_no_firma`: «El conductor hizo la salida del cargue y nadie de Botero Soto entregó la unidad». La base ya admite también la forma inversa (firma solo quien entrega, `no_firma_parte: "recibe"`), que usará el descargue por punto de entrega |
| `rechazado` | un carro que **no se entregó** en su parada: se rechazó por una novedad de su revisión de llegada y las dos partes firmaron la constancia del rechazo (con el daño que lo motivó en `rechazo_hallazgo_id` y quien lo marcó en `rechazo_marcado_por`). No cuenta como entregado (ver «El carro rechazado») |
| `null` | abierta: falta una firma (o ninguna se ha enviado) |

**En el destino la entrega se firma por parada.** `POST /api/inspecciones/{id}/transferencia`, `…/transferencia/reservas` y `…/transferencia/no-firma` sobre una inspección del **descargue** responden `409` con el motivo `entrega_por_parada` y «En el destino la entrega se firma por parada: usa «Entregar en esta parada»»,
**para todos los roles, también el administrador**; la firma, las reservas y la no firma del descargue se hacen con `POST /api/viajes/{id}/entregas/{lugar}/firma` y `…/una-firma` (ver «Entrega por parada»). En el cargue la firma carro por carro la rechaza la firma por niñera (`firma_por_ninera`). Las transferencias del descargue ya cerradas, sus notas aclaratorias y su
constancia PDF se siguen leyendo; la constancia muestra, en cada firma, la hora y el lugar del **momento** en que se firmó (o «sin ubicación») además de cuando llegó la firma al servidor, y la exportación de inspecciones trae las columnas `Entrega: momento`, `Entrega: ubicación`, `Recibe: momento` y `Recibe: ubicación`.

La primera llamada (sea cual sea la firma) crea la transferencia y exige `entrega_usuario_id` y `recibe_usuario_id`; en las siguientes pueden omitirse y, si se envían, deben ser los mismos
(`409` si no: los firmantes no se cambian). Quien envía debe ser uno de los dos (`403` si no) y los dos deben ser usuarios del viaje de la inspección (`400` con el campo y el motivo).
El rol de quien entrega y el de quien recibe no se fijan por el tipo de usuario: son los ids que se envían.

### `POST /api/inspecciones/{id}/transferencia`
Campo. Firma la **propia parte**: quien entrega firma la entrega y quien recibe, el recibo. `201`.

```json
{ "entrega_usuario_id": "uuid", "recibe_usuario_id": "uuid", "firma": "<trazo SVG o ruta>" }
```

| Campo | Obligatorio | Regla |
|---|---|---|
| `entrega_usuario_id`, `recibe_usuario_id` | solo en la primera llamada | UUID, usuarios distintos y del viaje |
| `firma` | sí | texto de hasta 200 000 caracteres: el trazo SVG o la ruta de la imagen |

Mientras está abierta, cada uno puede repetir su firma. Una vez cerrada responde `409` salvo que se repita exactamente la misma firma (reintento idempotente). Si quien recibe firma por este
endpoint después de haber dejado reservas, esas reservas se descartan.

> Cambio respecto a la versión anterior: ya no existen `firma_entrega` ni `firma_recibe` en el cuerpo. Ese diseño dejaba que un cliente enviara las dos firmas de golpe y cada firma
> quedaba sin dueño; ahora se envía una sola `firma` y se guarda en la parte que corresponde al usuario del token.

### `POST /api/inspecciones/{id}/transferencia/reservas`
Campo. **Quien recibe** firma «con reservas»: su firma y las observaciones (`reservas`, obligatorias, máximo 1 000 caracteres). `201`. Solo quien recibe puede (`403` para quien entrega o
cualquier otro) y sin observaciones responde `400` con el campo `reservas`. Cuando ya está la firma de quien entrega, la transferencia queda `con_reservas`.

```json
{ "entrega_usuario_id": "uuid", "recibe_usuario_id": "uuid", "firma": "<trazo SVG o ruta>", "reservas": "El parachoques tiene un rayón" }
```

### `POST /api/inspecciones/{id}/transferencia/no-firma`
Campo. **Constancia de no firma**: una parte se negó a firmar y la **otra parte** deja constancia (nadie declara su propia negativa: `403`). Exige el motivo, un **testigo con usuario** y la
**foto** de la constancia; cierra la transferencia como `no_firma`. `201`. La parte que se negó no debe haber firmado antes (`409` si ya firmó).

```json
{
  "entrega_usuario_id": "uuid", "recibe_usuario_id": "uuid",
  "parte": "recibe", "motivo": "Se niega a firmar el recibo", "testigo_usuario_id": "uuid",
  "evidencia": { "storage_path": "3f2b…c1.png", "sello_fecha": "2026-07-17T12:30:15.123Z", "sello_lat": 6.24, "sello_lng": -75.58, "precision": 4.5, "dispositivo_id": "tablet-7", "hash_sha256": "…" }
}
```

| Campo | Obligatorio | Regla |
|---|---|---|
| `parte` | sí | quién se negó: `entrega` o `recibe` (no puede ser quien envía) |
| `motivo` | sí | máximo 1 000 caracteres |
| `testigo_usuario_id` | sí | usuario **activo**, distinto de quien entrega y de quien recibe (y del mismo cliente si quien envía es de un cliente); `400` si no |
| `evidencia` | sí | la foto de la constancia, con las mismas reglas de sello que una foto de inspección (archivo ya subido, sello de fecha y ubicación, hash opcional) |

La foto se guarda como una evidencia marcada `es_constancia: true`, que **se admite aunque la inspección ya esté cerrada** y no cuenta como foto de una vista ni de un hallazgo.

### `POST /api/inspecciones/{id}/transferencia/sin-entrega`
Campo, **solo el conductor del viaje** (decisión confirmada por el negocio, P-080). Cuando el conductor hizo la salida del Cargue (por la excepción «No hay inspector») nadie de Botero Soto firma la entrega: el conductor cierra la transferencia solo con su firma. `201`.

```json
{ "firma": "<trazo SVG o ruta>" }
```

| Caso | Respuesta |
|---|---|
| el conductor del viaje, en la salida del Cargue que él hizo | `201`: la transferencia queda `recibo_sin_entrega`, con la firma de quien recibe, su fecha e IP, **sin** `entrega_usuario_id`, firma, fecha ni IP de entrega |
| cualquier otro usuario (otro conductor, un inspector, un administrador, el coordinador) | `403` |
| sin `firma` | `400` |
| la etapa no es el Cargue o no es su salida; la salida la hizo el inspector (u otro usuario) | `409` |
| la transferencia ya tiene tipo de cierre, o quien entrega ya firmó | `409` |
| la inspección no existe | `404` |

Si la transferencia seguía **abierta** (por ejemplo, quien recibe ya había firmado su parte y quien entrega no), se cierra sin entrega: quien entrega deja de figurar y las reservas que hubiera se descartan. Al cerrarse:

- **desbloquea** la salida de la etapa siguiente, como los demás cierres;
- los hallazgos de esa inspección quedan con `evidencia_completa: false` y `evidencia_faltante: ["transferencia_sin_entrega"]` (solo firmó una parte, como en la no firma): se recalcula como en los otros cierres y no cuentan en la tasa de rechazo;
- si la salida se hizo con la excepción «No hay inspector», se crea además la **novedad de proceso** `recibo_sin_entrega` (con el motivo de la inspección, sin nueva notificación); en un cargue sin apoyo planeado **no** se crea ninguna;
- queda en la auditoría (`transferencia.sin_entrega` y, si hay novedad, `novedad_proceso.crear`);
- la constancia en PDF y el PDF del informe muestran «recibo sin entrega» y «Entrega: no hubo entrega»; el informe y el inventario traen `tipo_cierre: "recibo_sin_entrega"` y `entrega_usuario_id: null`.

### `GET /api/inspecciones/{id}/transferencia/constancia/pdf`
Autenticado. La **constancia de la transferencia en PDF** (`application/pdf`): datos de la unidad, tipo de cierre, las dos firmas con su fecha e IP, las reservas o el motivo de la no firma,
el testigo y la foto de la constancia. Inspector y conductor solo la descargan de sus viajes (`403` si es de otro). `404` si la inspección no existe o no tiene transferencia.

### Respuesta de las firmas
`TransferenciaDto` (sin las firmas ni las IP):

```json
{ "id": "uuid", "inspeccion_id": "uuid", "entrega_usuario_id": "uuid", "recibe_usuario_id": "uuid",
  "firmada": false, "firmada_at": null, "created_at": "…", "updated_at": "…",
  "tipo_cierre": null, "entrega_firmada_at": "…", "recibe_firmada_at": null,
  "reservas_texto": null, "no_firma_parte": null, "motivo_no_firma": null, "testigo_usuario_id": null, "evidencia_constancia_id": null,
  "recibe_externo_nombre": null, "recibe_externo_cedula": null }
```

`entrega_usuario_id` es `null` solo en el cierre `recibo_sin_entrega`. `recibe_usuario_id` es `null` cuando recibió una persona externa (entonces vienen `recibe_externo_nombre` y `recibe_externo_cedula`, ver «Entrega por punto») o cuando la entrega se cerró con la sola firma de quien entrega porque nadie recibió.

`firmada` es `true` cuando están las dos firmas (también si fue con reservas); `firmada_at` se sella cuando está la segunda. En el detalle del informe (`GET /api/inspecciones/{id}`) la
transferencia llega como `TransferenciaDetalleDto`: los mismos campos más `entrega_nombre`, `recibe_nombre`, `firma_entrega`, `firma_recibe`, `entrega_firma_ip`, `recibe_firma_ip` y `testigo_nombre`.

**El conductor recibe sin revisar los hallazgos.** En el cargue, quien recibe puede firmar el recibo sin abrir la lista de hallazgos: la firma no depende de ellos y su respuesta no los incluye.
Que la app no se los muestre ni se los notifique en esa pantalla es una regla de la app; la API no la impone.

## Firma del cargue por niñera

El cargue ya **no se firma carro por carro**: la niñera se firma **una sola vez por parte** para todos los carros activos del viaje. Quien entrega es el **inspector del viaje** y quien recibe es el **conductor del viaje** (nadie firma por otro: `403`). Cada firma lleva la **hora y el GPS del momento en que se firmó** (no cuándo llegó al servidor: una firma hecha sin señal se envía después y conserva el momento y el lugar del patio), además de la fecha e IP de llegada que ya guardaba. La firma se guarda en la transferencia de cada carro, colgada de **su salida del cargue** (la acomodación).

Los nombres de los endpoints y de los motivos están **a confirmar por backend** con la app que firma por niñera, que consume este contrato.

### `POST /api/viajes/{id}/cargue/firma`
Inspector o conductor del viaje. `200` con `CargueNineraDto`.

```json
{
  "firma": "<trazo SVG o ruta>", "parte": "recibe",
  "fecha_momento": "2026-11-18T14:09:00-05:00", "lat": 6.2442, "lng": -75.5812, "precision_m": 7.5,
  "reservas": [ { "vin": "9FB45RFL8NM201001", "observaciones": "Rayón en la puerta izquierda" } ]
}
```

| Campo | Obligatorio | Regla |
|---|---|---|
| `firma` | sí | el trazo, como en la firma por carro |
| `parte` | sí | `entrega` (el inspector del viaje) o `recibe` (su conductor); debe ser la parte del usuario del token |
| `fecha_momento` | sí | ISO 8601 **con zona**: la hora en que se firmó |
| `lat`, `lng` | sí | el GPS del momento (latitud de -90 a 90, longitud de -180 a 180, juntas); `precision_m` opcional, mayor o igual a 0 |
| `reservas` | no, solo `recibe` | por carro: `vin` (de un carro **activo** del viaje, sin repetir) y `observaciones` (1 a 1 000 caracteres). Quien entrega no deja reservas (`403`) |

- **Exige el cargue completo.** Cada carro activo debe tener la **llegada y la salida del cargue cerradas**; si no, `409` con `details.motivo: "cargue_incompleto"`, `details.faltantes` (una línea por lo que falta: «Falta la salida del cargue del VIN …») y el mismo texto en `message`. Los carros que **no salen** quedan fuera de lo que se exige y de la firma.
- **Cierra cada carro.** Con la firma de las dos partes el carro queda `firmada`, o `con_reservas` si quien recibe dejó reservas sobre él. Si quien entrega ya había firmado y quien recibe deja reservas **nuevas o distintas** en un carro, la firma de quien entrega **se retira** en ese carro y se le pide otra vez; los carros sin reservas no cambian.
- **Reenviar no duplica.** Repetir la misma firma, o firmar otra vez sobre carros que ya cerraron con la firma de esa parte, no cambia nada ni deja otro registro. Quien recibe no puede cambiar las reservas de un carro ya cerrado (`409`).
- Un carro que ya se cerró **carro por carro, antes de este cambio**, cuenta como entrega cerrada y la firma por niñera no lo toca: lo ya firmado se sigue leyendo igual.

`CargueNineraDto`: `{ viaje_id, parte, unidades, cerradas, cargue_firmado_por_ninera_at, detalle: [ { vin, tipo_cierre, por_ninera } ] }`. `tipo_cierre` es `abierta` mientras falta alguna firma, o `firmada`, `con_reservas`, `no_firma` o `recibo_sin_entrega`; `por_ninera` dice si se firmó por niñera; `cargue_firmado_por_ninera_at` es nulo mientras falte la otra parte.

### `POST /api/viajes/{id}/cargue/no-firma`
Inspector o conductor del viaje: la **constancia de no firma de toda la niñera**. `200` con `CargueNineraDto`.

```json
{
  "parte": "recibe", "motivo": "Se niega a firmar el cargue", "testigo_usuario_id": "uuid",
  "evidencia": { "storage_path": "3f2b…c1.png", "sello_fecha": "2026-11-18T19:09:00.000Z", "sello_lat": 6.24, "sello_lng": -75.58, "precision": 4.5, "dispositivo_id": "tablet-7", "hash_sha256": "…" },
  "fecha_momento": "2026-11-18T14:09:00-05:00", "lat": 6.2442, "lng": -75.5812, "precision_m": 7.5
}
```

`parte` es la que **se negó** (la deja la otra: `403` a quien declara su propia negativa o a quien no es del viaje). Mismas reglas del motivo, el testigo con usuario y la foto que la constancia por carro. Cierra como `no_firma` la transferencia de **cada carro activo**: no hay constancia para solo algunos carros (para eso están las reservas). Una sola foto de la constancia sirve a todos. `409` si la parte que se negó ya firmó o si el cargue está incompleto. Avisa **una vez por viaje** al coordinador de calidad y al administrador del cliente (`novedad_proceso`, `datos.hecho: "cargue_no_firmado"`, sin cédulas). Reenviar la misma constancia no crea otra.

### `GET /api/viajes/{id}/ninera/pdf`
Informe de la niñera del viaje en PDF, como descarga (`application/pdf`, archivo `ninera-{codigo_mtrack}.pdf`). Se **arma al pedirlo** con los informes del cargue de sus carros: no se guarda aparte, así que si un carro se baja del viaje el siguiente informe sale solo con los que van, sin tocar lo ya firmado. El nombre de la ruta está **a confirmar por backend** con la app.

Lleva:
- los **puestos** de la niñera en orden con el VIN de cada uno («Puesto 3 — VIN …», «Puesto 1 — libre»), los carros sin puesto y los carros que **no salen** marcados con su motivo («NO SALE — VIN …: motivo»);
- los **daños de cada carro** (de su llegada y su salida del cargue vigentes, sin versiones reemplazadas): zona, clase y tamaño y, para quien puede verlos, el **nivel** y la **nota del inspector**;
- las **fotos de la niñera** (las mismas que cuenta el cierre de la acomodación, hasta 20);
- la **firma única del cargue** de cada parte con su nombre, la **hora y el GPS del momento** en que firmó y el trazo; o la **constancia de no firma** (parte que se negó, motivo y testigo).

Permiso: **el mismo del detalle del viaje** (`GET /api/viajes/{id}`): `403` si el viaje es de otro cliente o no está asignado a quien lo pide y `404` si no existe. **Al conductor se le quitan el nivel y la nota de cada daño**; el coordinador, el inspector y los administradores lo reciben con ellos. Un viaje con la acomodación sin cerrar o el cargue sin firmar **no falla**: devuelve el PDF con lo que hay y dice «Acomodación sin cerrar», «Cargue sin firmar» o «Cargue sin firmar por esta parte» donde corresponde.

### El cargue en el viaje
`GET /api/viajes/{id}` (y el listado) trae tres campos **calculados** de las transferencias, sin datos aparte que puedan quedar desincronizados: `cargue_firmado_por_ninera_at` (cuándo se cerró la firma por niñera, nulo si no se firmó así), `cargue_no_firmado_at` (cuándo se dejó la constancia de no firma, nulo si no hay) y `cargue_no_firma_parte` (`entrega` o `recibe`, la parte que se negó).

### La firma carro por carro del cargue se rechaza
En el cargue, `POST …/transferencia`, `POST …/transferencia/reservas` y `POST …/transferencia/no-firma` responden **`409` con `details.motivo: "firma_por_ninera"`** para todos los roles: la firma, las reservas y la constancia se hacen por niñera. **El recibo sin entrega del conductor no cambia.** Estos mensajes **tienen que desplegarse junto con la app que firma por niñera o después**: una app que aún firma carro por carro recibe el rechazo.

### Avisos
Si la salida del cargue de un carro lleva cerrada `firma_cargue_pendiente_minutos` (30 por defecto, ajustable sin desplegar; el valor definitivo lo confirma el negocio) sin que su transferencia esté cerrada, la parte a la que le falta firmar recibe un aviso **`firma_cargue_pendiente`** (urgencia media, solo bandeja): el inspector del viaje si falta quien entrega, el conductor si falta quien recibe, con los VIN que faltan; **uno por viaje y parte**. La llegada del cargue **sin transferencia propia** deja de avisar como `transferencia_pendiente`.

### Qué más cambia con la niñera
- **Las fotos de la acomodación se cuentan por viaje**: el cierre de la acomodación y el PDF de cada carro cuentan y muestran las fotos de **todas** las salidas del cargue del viaje; una foto sirve para todos los carros.
- **Con la acomodación cerrada un puesto no se libera suelto** (`PUT …/vehiculos/{vin}/posicion` con `null`): `409` con `details.motivo: "acomodacion_cerrada"`; el puesto se libera desde «no sale» y desde bajar el carro del viaje. Con una corrección de la acomodación en borrador sí se puede.
- **Una etapa firmada ya no se corrige**: si cualquier transferencia de la **etapa** (la de la llegada o la de la salida, de cualquier versión) tiene una firma o un cierre, `POST …/corregir` responde `409 INSPECCION_YA_FIRMADA` aunque la inspección que se corrige no tenga firma propia.
- La transferencia (`TransferenciaDto`) trae `firma_por_ninera`, `entrega_momento_at`, `entrega_lat`, `entrega_lng`, `entrega_precision_m`, `recibe_momento_at`, `recibe_lat`, `recibe_lng` y `recibe_precision_m` (nulos si no se enviaron).

## Catálogos maestros

Las etapas, los lugares, los tipos de unidad, las clases y tamaños de daño, los niveles de severidad, las vistas de foto, los
cuadrantes, los paneles de daño y sus regiones, los clientes y los estados de las órdenes Sofasa **ya no son listas fijas en el código**: viven en tablas y los administra TI o el coordinador de calidad desde el portal.
La web y la app deben leerlos de la API (y guardar una copia para trabajar sin señal), no escribirlos a mano.

| Nombre (ruta y clave) | Qué contiene | Campos de cada valor |
|---|---|---|
| `etapas` | Etapas del proceso: `cargue`, `transporte`, `descargue`. Es el `proceso` de un informe o de un tramo de la ruta | `id`, `codigo`, `nombre`, `orden`, `activo` |
| `puntos-custodia` | Los **tipos** de lugar: CEDI 1, Origen, Certificadora, Destino, CEDI 2, Concesionario | `id`, `codigo`, `nombre`, `orden`, `activo` |
| `lugares` | Los lugares **reales** donde se carga y se entrega (CEDI Envigado, Concesionario Autolarte Medellín…). Empieza vacío: lo llena el negocio | `id`, `nombre` (único), `tipo` (el `codigo` de un valor activo de `puntos-custodia`), `tipo_nombre`, `ciudad`, `orden`, `activo`, `latitud`, `longitud`, `radio_metros` y `sin_ubicacion` (los tres primeros `null` mientras no tenga ubicación), más `estado_ubicacion` (`sin_ubicacion`, `pendiente_confirmar` o `confirmada`: solo una ubicación confirmada sirve para la confirmación «en sitio») |
| `tipos-unidad` | Tipos de unidad transportadora: niñera, nodriza, quintinaria, planchón grúa. `puestos` son los **puestos por defecto** de su niñera (1 a 20; `null` = sin definir, el viaje nuevo toma 7): un viaje toma ese valor al crearse y cambiarlo **no mueve ningún viaje ya creado** (`400` en `puestos` fuera de rango) | `id`, `codigo`, `nombre`, `orden`, `activo`, `puestos` |
| `clases-dano` | Clases de daño de un hallazgo (10 iniciales) | `id`, `codigo`, `nombre`, `orden`, `activo` |
| `tamanos-dano` | Tamaños de daño de un hallazgo (6 iniciales) | `id`, `codigo`, `nombre`, `orden`, `activo` |
| `niveles-severidad` | Niveles V1+, V1, V2 y V3 con su acción: «Se recupera antes del despacho», «Se rechaza el vehículo», «Validación por Sofasa» y «Se deroga (se acepta el defecto)» | `id`, `codigo`, `nombre`, `orden`, `activo`, `accion` (texto o `null`, máx. 500) |
| `vistas-foto` | Las 9 vistas de foto: 7 obligatorias y 2 adicionales (`piezas_baul`, `cojineria`) que solo se toman con faltante o daño | `id`, `codigo`, `nombre`, `orden`, `activo`, `obligatoria`, `tipo_vehiculo` (`null` = cualquier tipo) |
| `cuadrantes` | Las **piezas** numeradas de la carrocería (guardafango delantero, parabrisas…) del dibujo versión 6: 45 cuadrantes, 80 zonas. **Solo lectura** | `id`, `codigo` (siempre `null`), `tipo_vehiculo` (`null` = cualquier tipo), `numero`, `nombre` (la pieza), `lleva_lado`, `zonas` (una por lado, o una sola sin lado: `id`, `lado`, `nombre_completo`, `aiag_area`), `orden`, `activo` |
| `paneles-dano` | Los **7 paneles** del dibujo de daños (costados izquierdo y derecho, frontal, posterior, superior y las diagonales delantera derecha y trasera izquierda), separados de las vistas de foto. **Solo lectura**, salvo la imagen | `id`, `codigo`, `nombre`, `orden`, `activo`, `ancho_ref`, `alto_ref` (tamaño de referencia de la imagen, en píxeles), `version` (del archivo del dibujo) e `imagen_url` (la imagen de fondo del panel o `null` si aún no se cargó) |
| `cuadrante-regiones` | La **región tocable** de cada zona en cada panel (una fila por aparición; 251 en la versión 6). **Solo lectura** | `id`, `panel` (código), `cuadrante_id`, `numero`, `nombre`, `lado` (`izquierdo`, `derecho` o `null` en los cuadrantes sin lado), `prioridad`, `geometria` |
| `clientes` | Las empresas a las que pertenecen los usuarios y los vehículos (hoy `sofasa`). El primero activo por `orden` es el cliente por defecto de lo que se crea sin cliente explícito | `id`, `codigo`, `nombre`, `orden`, `activo` |
| `estados-sofasa` | Los estados de las órdenes Sofasa: el `codigo` es el `status_code` **real** de la orden y el `nombre`, su etiqueta (ver «Estados Sofasa» abajo). Empieza con los `status_code` que ya tienen las órdenes | `id`, `codigo`, `nombre`, `orden`, `activo` |
| `tipos-extra` | Los tipos de extra del vehículo fuera del dibujo estándar: `barras_techo` «Barras de techo», `volco` «Volco», `platon` «Platón» y `otro` «Otro» (el único que admite un texto libre; **está protegido**: no se inactiva, no se borra y no cambia de nombre, `409`, porque no tiene reemplazo; sí admite cambiar su orden). Lo administra Calidad: lo crean y editan `admin`, `super_admin` y `coordinador_calidad` (`403` los demás); un tipo con extras reportados **no se borra** (`409`): se inactiva y los extras conservan su tipo, pero ya no se ofrece para extras nuevos | `id`, `codigo`, `nombre`, `orden`, `activo` |
| `muelles` | Los muelles de cada lugar (hijo de `lugares`): `m1`… dentro de su lugar; no viene sembrado | `id`, `codigo`, `nombre`, `orden`, `activo`, `lugar_id` |
| `motivos-no-sale` | Los motivos por los que un carro no sale del viaje: `dano_v1_plus`, `dano_reportado_conductor`, `no_llego_al_patio`, `faltan_documentos`, `no_esta_listo` y `otro` (que exige texto) | `id`, `codigo`, `nombre`, `orden`, `activo`, `exige_hallazgo`, `exige_texto`, `solo_sin_inspector` |
| `motivos-sin-inspector` | Los motivos por los que no hay inspector en un cargue (la excepción «No hay inspector», que activa el conductor en el momento): `inspector_no_llego`, `inspector_no_disponible` y `otro` (que exige texto); trae además, **inactivos** (`activo: false`, no se ofrecen en la excepción), `se_nego_a_firmar`, `no_habia_nadie` y `sin_inspector_del_punto`: los motivos codificados del cierre de una entrega con una sola firma, que la novedad de proceso de ese cierre guarda como su `motivo_codigo` | `id`, `codigo`, `nombre`, `orden`, `activo`, `requiere_texto` |

**Estados Sofasa (`estados-sofasa`).** `GET /api/sofasa/ordenes` sigue devolviendo `status_code` **sin traducir**; el portal lo traduce con este catálogo (`items.find(i => i.codigo === orden.status_code)?.nombre`; si el estado no está, muestra el código). A diferencia de los demás
catálogos, su `codigo` **no se limita a minúsculas y `_`**: es el texto que trae la orden del origen, y se guarda tal cual (recortado, con sus mayúsculas, hasta 100 caracteres y sin saltos de línea); es único sin distinguir mayúsculas, así que `ENTREGADA` y `entregada` son el mismo. Las demás
reglas son las de los otros catálogos: lo crean y editan `admin`, `super_admin` y `coordinador_calidad` (`403` los demás), el `codigo` no se cambia después (`400`), `nombre` es obligatorio (máx. 100) y `orden` un entero ≥ 0, un valor que alguna orden usa **no se borra** (`409`, se inactiva con `activo: false`) y cada cambio queda en la auditoría
(`catalogo.estados-sofasa.crear|actualizar|eliminar`). El `status_code` de una orden sigue siendo texto libre: la ingesta del ETL **no** falla por un estado que aún no está en el catálogo.

`codigo` es la **clave estable** (minúsculas, números y `_`, máximo 30 caracteres; los de `estados-sofasa`, ver arriba): es lo que viaja en el resto de la API
(`proceso`, `clase_dano`, `tamano_dano`) y no cambia nunca. `nombre` es la etiqueta que se muestra y sí puede cambiar. Los valores
vienen ordenados por `orden`.

### `GET /api/catalogos`
Cualquier usuario autenticado. Devuelve todos los catálogos, **activos e inactivos**, y una versión global.

```json
{
  "version": "9f2c4a7b1d3e5f60",
  "catalogos": {
    "etapas": [ { "id": "uuid", "codigo": "cargue", "nombre": "Cargue", "orden": 1, "activo": true } ],
    "clases-dano": [ { "id": "uuid", "codigo": "golpe", "nombre": "Golpe", "orden": 5, "activo": true } ],
    "niveles-severidad": [ { "id": "uuid", "codigo": "v1", "nombre": "V1", "orden": 2, "activo": true, "accion": "Se rechaza el vehículo" } ],
    "vistas-foto": [ { "id": "uuid", "codigo": "piezas_baul", "nombre": "Piezas en el baúl", "orden": 8, "activo": true, "obligatoria": false, "tipo_vehiculo": null } ],
    "cuadrantes": [
      { "id": "uuid", "codigo": null, "tipo_vehiculo": null, "numero": 4, "nombre": "Guardafango delantero", "lleva_lado": true, "orden": 4, "activo": true,
        "zonas": [ { "id": "uuid", "lado": "izquierdo", "nombre_completo": "Guardafango delantero izquierdo", "aiag_area": "14/16" },
                   { "id": "uuid", "lado": "derecho", "nombre_completo": "Guardafango delantero derecho", "aiag_area": "14/16" } ] },
      { "id": "uuid", "codigo": null, "tipo_vehiculo": null, "numero": 54, "nombre": "Parabrisas", "lleva_lado": false, "orden": 54, "activo": true,
        "zonas": [ { "id": "uuid", "lado": null, "nombre_completo": "Parabrisas", "aiag_area": "20" } ] }
    ],
    "muelles": [ { "id": "uuid", "codigo": "m1", "nombre": "Muelle 1", "orden": 0, "activo": true, "lugar_id": "uuid" } ],
    "motivos-no-sale": [ { "id": "uuid", "codigo": "otro", "nombre": "Otro", "orden": 6, "activo": true, "exige_hallazgo": false, "exige_texto": true, "solo_sin_inspector": false } ],
    "motivos-sin-inspector": [ { "id": "uuid", "codigo": "otro", "nombre": "Otro", "orden": 3, "activo": true, "requiere_texto": true } ],
    "paneles-dano": [ { "id": "uuid", "codigo": "costado_izquierdo", "nombre": "Costado izquierdo", "orden": 1, "activo": true, "ancho_ref": 1071, "alto_ref": 491, "version": 6, "imagen_url": "/api/catalogos/paneles-dano/costado_izquierdo/imagen?v=3f2b…c1" } ],
    "cuadrante-regiones": [ { "id": "uuid", "panel": "costado_izquierdo", "cuadrante_id": "uuid", "numero": 1, "nombre": "Esquina del parachoques delantero", "lado": "izquierdo", "prioridad": 1, "geometria": { "puntos": [ [0.34, 0.30], [0.59, 0.30], [0.59, 0.48] ] } } ],
    "puntos-custodia": [], "tipos-unidad": [], "tamanos-dano": [], "clientes": []
  }
}
```

### `GET /api/catalogos/{nombre}`
Un solo catálogo con **su propia versión**: `{ "version": "…", "items": [ … ] }`. `404` si el nombre no existe (el mensaje lista los
disponibles). `cuadrante-vistas` (el catálogo anterior de polígonos por vista de foto) **ya no existe**: responde `404` como cualquier nombre desconocido (lo reemplazan `paneles-dano` y `cuadrante-regiones`).

`motivos-sin-inspector` trae los motivos por los que no está el inspector: `inspector_no_llego` «El inspector no llegó», `inspector_no_disponible` «El inspector no está disponible» y `otro` «Otro» (`requiere_texto: true`: quien lo elige escribe cuál es). Los valores los confirma Calidad y los administran
`admin`, `super_admin` y `coordinador_calidad` como los demás catálogos; el `codigo` no cambia y un motivo que alguna inspección usa no se borra (`409`): se inactiva.

`lugares` es el catálogo de los sitios reales que usa la carga de cargues por Excel (`origen` y `destino`, por nombre) y que muestra el viaje. Se crea con `nombre`, `tipo` y `ciudad` (los tres obligatorios; `orden` y
`activo` opcionales), lo cambian los mismos roles de los demás catálogos y cualquier usuario autenticado lo lee. Un nombre repetido (sin distinguir mayúsculas) responde `409`, y un lugar que algún viaje usa no se borra (`409`): se inactiva.
La ubicación (`latitud`, `longitud`, `radio_metros`) la cargan los mismos roles por este catálogo (el lugar queda `confirmada`) y también se fija «aquí» o se calibra sola: ver «Ubicación de los lugares». Mientras no se fija, queda `null` con `sin_ubicacion: true`; `estado_ubicacion`, `ubicacion_fijada_por` y `ubicacion_fijada_at` salen en cada lugar.

### Versión y copia sin conexión
Cada respuesta trae la versión en el encabezado `ETag` (por ejemplo `"9f2c4a7b1d3e5f60"`) y también en el cuerpo. La versión es una
huella del contenido: **cambia si y solo si cambió algún dato** del catálogo (o de cualquiera, en el global).

1. Guarde la respuesta y su `ETag`.
2. La próxima vez (al abrir la app, al abrir una inspección con señal) envíe `If-None-Match: "<ETag guardado>"`.
3. `304` sin cuerpo = su copia está al día. `200` = reemplace la copia por la respuesta nueva.
4. Sin señal, use la última copia guardada.

Un catálogo cambia al instante cuando se edita; si hay varias instancias de la API, otra puede tardar hasta un minuto en reflejarlo.

**Valores inactivos.** `activo: false` significa que ya no se ofrece, pero sigue siendo válido en lo que ya se guardó. En los selectores
muestre solo los activos; para dibujar informes viejos use todos (un hallazgo antiguo puede tener una clase hoy inactiva).

### Cuadrantes, regiones y paneles de daño
El dibujo de daños es el archivo `regiones-zonas-dano.json` (**versión 6**), que es la **única fuente** de la geometría y de los nombres de pieza. Un cuadrante es una **pieza** de la carrocería con un número (`cuadrantes`) y trae
sus **zonas** (`zonas`): una por lado (`izquierdo` y `derecho`) si `lleva_lado` es `true`, o una sola sin lado si es `false`. Cada zona trae `nombre_completo` («Guardafango delantero izquierdo») y `aiag_area` (el código AIAG
de esa pieza y lado, **texto sin formato fijo**: puede traer varios códigos, como `72-73 / 78-79`). Esa misma zona se ve desde varios paneles, así que **dónde se dibuja** en cada panel va aparte, en
`cuadrante-regiones`: una fila por cada panel donde aparece. Para dibujar un panel, la app y el portal toman las `cuadrante-regiones` de ese `panel` y le ponen a cada región el
`numero` y el nombre de su zona (`cuadrante_id` → `cuadrantes`), ordenadas por `prioridad`.

Para que la app y el portal dibujen las mismas regiones, `geometria` es un **polígono normalizado**: un objeto con la lista de puntos,
cada uno `[x, y]` con `x` e `y` entre 0 y 1, medidos sobre la imagen del panel (el origen es la esquina superior izquierda). El relleno usa la regla **par-impar (evenOdd)**: los huecos de una región son parte de su polígono.

```json
{ "puntos": [ [0.10, 0.10], [0.55, 0.10], [0.55, 0.48], [0.10, 0.48] ] }
```

Llega como objeto JSON (no como texto). `numero` es único por `tipo_vehiculo` (con `tipo_vehiculo: null` el número es único entre los cuadrantes sin tipo) y una zona tiene como máximo **una región por panel**.

**El lado.** Si un cuadrante lleva lado lo declara el catálogo (`lleva_lado`); **ninguna regla del servidor usa números**. En la versión 6 los números 1 a 35 llevan lado y del 50 al 59 (piezas centrales) no, pero la próxima versión del dibujo puede
cambiarlo sin cambiar el código. **`izquierdo` es el lado del conductor, visto sentado al volante.** Una región es de la zona «lado + número»; un panel puede tener regiones de los dos lados.

**Solo lectura.** `POST`, `PATCH` y `DELETE` sobre `cuadrantes`, `paneles-dano` y `cuadrante-regiones` responden `409` con el mensaje «El dibujo se cambia con una versión nueva del archivo». Un dibujo distinto es una **versión nueva del archivo**,
que se siembra con el guion `database/cambios-esquema/34-resembrar-dibujo-v6.sql` (generado con `database/herramientas/generar-resiembra-dibujo.py`): reemplaza el catálogo del dibujo completo (7 paneles, 45 cuadrantes, 80 zonas y 251 regiones) y los
hallazgos que estaban en una zona del dibujo anterior quedan **sin zona** (`cuadrante_numero` y `lado` en `null`) hasta que Calidad apruebe una tabla de equivalencias. El catálogo cambia de versión (`ETag`) con la resiembra. Las zonas provisionales (`frente` y las cuatro llantas) **ya no existen**.

### Paneles de daño
Los 7 paneles del dibujo de daños son la ficha donde se toca la región del daño; están separados del catálogo `vistas-foto` (las vistas de foto son otra cosa: lo que se fotografía). Cada panel trae
`imagen_url`: la imagen de fondo sobre la que se dibujan sus regiones, o `null` si aún no se cargó. La imagen debe ser la de la **misma versión del archivo** (`version`, con el tamaño de referencia `ancho_ref` × `alto_ref`): una imagen distinta desalinea los polígonos.

- `GET /api/catalogos/paneles-dano/{codigo}/imagen`: la imagen del panel. **Pública** (una etiqueta de imagen no envía token) y con caché larga (`Cache-Control: public, max-age=31536000, immutable`):
  la URL de `imagen_url` lleva el nombre del archivo (`?v=…`), así que cambia cuando se cambia la imagen. `404` si el panel no existe o aún no tiene imagen.
- `PUT /api/catalogos/paneles-dano/{codigo}/imagen`: cambia la imagen. Solo `admin` y `super_admin` (`403` para los demás). Cuerpo `{ "archivo": "3f2b…c1.png" }`, con el nombre que devolvió `POST /api/uploads` (JPG o PNG);
  `400` en `archivo` si falta o no existe, `404` si el panel no existe, `204` si se cambió. El catálogo cambia de versión.

### Administración: crear, editar, inactivar y borrar
Solo `admin`, `super_admin` y `coordinador_calidad`; los demás roles reciben `403`.

| Operación | Ruta | Respuesta |
|---|---|---|
| Crear | `POST /api/catalogos/{nombre}` | `201` con el valor creado |
| Editar | `PATCH /api/catalogos/{nombre}/{id}` | `200` con el valor; cambia solo los campos enviados |
| Inactivar | `PATCH /api/catalogos/{nombre}/{id}` con `{ "activo": false }` | `200` |
| Borrar | `DELETE /api/catalogos/{nombre}/{id}` | `204`; `409` si algo lo usa |

Cuerpo de **crear**: `codigo` y `nombre` obligatorios; `orden` (entero ≥ 0, por defecto 0) y `activo` (por defecto `true`) opcionales, más
los campos propios del catálogo. **`cuadrantes`, `paneles-dano` y `cuadrante-regiones` no se crean, editan ni borran** (`409`, ver arriba); de `paneles-dano` solo se cambia la imagen con `PUT …/imagen`. En **vistas** `obligatoria` es opcional (por defecto `true`).

```json
{ "codigo": "oxidacion", "nombre": "Oxidación", "orden": 11 }
```

Reglas:
- `400` con `details.fieldErrors` si un campo es inválido, **si llega un campo que ese catálogo no admite** (por ejemplo `accion` en
  `etapas`) o si se envía `null` en `nombre`, `orden` o `activo`.
- El **`codigo` no se puede cambiar** (`400`); se puede reenviar igual. Para reemplazar un código, cree un valor nuevo e inactive el viejo.
- `409` si el `codigo` ya existe en ese catálogo.
- **Un valor en uso no se borra:** `409` con el mensaje «El valor está en uso y no se puede borrar: solo puede inactivarlo». Está en uso una clase
  o tamaño usado por algún hallazgo, una etapa o un lugar usado por algún tramo de la ruta, un cliente con usuarios o vehículos, y una vista que tiene polígonos del modelo anterior. Borrar un cuadrante borra también sus regiones (salvo que un hallazgo ya use una de sus zonas).
- Una vista **con polígonos del modelo anterior** no puede pasar a `obligatoria: false` (`409`).
- El código `requiere_reemplazo` de `tamanos-dano` lo usa el análisis de patrones para contar los daños que obligan a reemplazar la pieza:
  no lo borre ni le cambie el código.

**Qué cambia en el resto de la API.** Un hallazgo nuevo solo admite `clase_dano` y `tamano_dano` **activos**; los vehículos nuevos reciben
como ruta las etapas **activas** de `etapas`, en su `orden`; y el filtro `proceso` de los informes acepta los códigos de `etapas`.

---

## Fotos

### `POST /api/uploads`
Campo. Sube una foto de dos formas (la app móvil puede usar la que prefiera): como **JSON con la imagen en base64**, o como **`multipart/form-data`** con el archivo en el campo
`archivo` (sin la sobrecarga de 33 % del base64). `201`.

```json
{ "data": "<base64, con o sin prefijo data:image/jpeg;base64,>", "ext": "jpg" }
```

| Campo | Regla |
|---|---|
| `data` (JSON) | obligatorio; imagen JPG o PNG válida (se verifica el contenido, no solo la extensión); máximo 10 MB por defecto |
| `ext` (JSON) | opcional: `jpg`, `jpeg` o `png` |
| `archivo` (multipart) | obligatorio; el archivo de la imagen. Mismas reglas de contenido y tamaño; `400` si falta, está vacío o no es JPG o PNG |

```json
{
  "storage_path": "3f2b9c6e-….jpg",
  "url": "https://api.ejemplo.com/api/uploads/3f2b9c6e-….jpg?exp=1792588800&firma=5d41402abc4b2a76b9719d911017c592a8c1d2e3f4a5b6c7d8e9f0a1b2c3d4e5",
  "url_expira_at": "2026-10-21T15:15:00.000Z",
  "hash_sha256": "9f86d0…"
}
```

Use `storage_path` al registrar la evidencia (el archivo debe haberse subido antes) y `url` para mostrarla. `hash_sha256` es el hash que **calculó el servidor** al recibir el archivo: es el mismo que
queda en la evidencia, y si la app lo reenvía como `hash_sha256` la evidencia comprueba que coincide.

### URL firmada de una foto
Una foto **no es pública**: solo se ve con una URL **firmada por el sistema y vigente**. Toda foto que devuelve la API (`EvidenciaDto.url`, `ArchivoSubidoDto.url`, la foto del sello, la constancia, las fotos de los hallazgos
previos...) sale como una URL **absoluta** `/api/uploads/{archivo}?exp=<epoch>&firma=<hmac>` con su vencimiento en `url_expira_at` (ISO 8601). La firma es un HMAC-SHA256 del nombre del archivo y de `exp` con un secreto del servidor
(`Storage__FirmaSecreto`), así que una firma solo sirve para ese archivo y ese vencimiento. La vigencia es el parámetro **`url_firmada_minutos`** (15 por defecto), que se aplica sin desplegar.

Como la firma va en la URL, **no hace falta el header `Authorization`**: la URL sirve directo en un `<img src>`. Cuando vence hay que volver a pedir el recurso (el informe, el inventario...) para recibir una URL nueva; no se guarda la URL
sino el recurso. Si la URL todavía no se ha abierto cuando vence, se pide de nuevo la lista y se usa la nueva.

Si la foto de una firma de transferencia (`firma_entrega`, `firma_recibe`) es una **ruta** de imagen, sale también como URL firmada; si es un trazo SVG, sale como se guardó.

### `GET /api/uploads/{archivo}?exp=&firma=`
Sirve la imagen (`image/jpeg` o `image/png`) con `Cache-Control: private, max-age=` hasta que venza la URL (máximo 1 hora). **No exige autenticación**, solo la firma:

| Respuesta | Cuándo |
|---|---|
| `200` | firma válida y vigente, y el archivo existe |
| `403` con `code: "URL_FIRMADA_INVALIDA"` | sin `exp` o sin `firma`, firma que no es la del archivo o del vencimiento, o URL vencida (también con un token válido: el token no reemplaza la firma) |
| `404` | firma válida pero el archivo no existe o su nombre no es válido |

Los PDF (inventario y constancia) incrustan las imágenes leyéndolas del almacén, no por URL.

---

## Sofasa

### `GET /api/sofasa/ordenes`
Autenticado. Una **página** de órdenes (`{ items, total, pagina, tamano }`; 50 por defecto, máximo 200; las más recientes primero; ver «Paginación y filtros» para `pagina`, `tamano` y `orden`). Filtros opcionales: `vin` (VIN o chasis válido), `estado` (el `status_code`), `ciudad`, `desde`, `hasta`
(fechas ISO, sin zona se toman como UTC; una fecha inválida responde `400`). Inspector y conductor solo reciben las órdenes vinculadas a sus viajes.

```json
{ "total": 1, "pagina": 1, "tamano": 50, "items": [
  {
    "id": "uuid", "source_id": 12345, "status_code": "…", "datetime_event": "2026-07-17T12:30:15.123Z",
    "orden_number": "…", "orden_sap": "…", "origin_cedi": "…", "vin": "…", "make": "…", "model_description": "…",
    "dealer_code": "…", "destination_city": "…", "destination_address": "…",
    "loading_date": null, "appointment": null, "loading_dock": null, "weight": 1250.5,
    "vehiculo_id": "uuid", "codigo_mtrack": null, "viaje_id": "uuid", "synced_at": "…", "created_at": "…", "updated_at": "…"
  }
] }
```

### `POST /api/etl/sofasa/ordenes` *(solo el ETL; el frontend no lo usa)*
No usa el JWT de usuario: exige `Authorization: Bearer <token del ETL>` (el valor de `Etl__IngestToken`). Ingesta idempotente por
`source_id` (máximo 5000 órdenes por lote). Cada orden materializa su VIN como vehículo, del cliente `sofasa` (sin etapas: nacen con el viaje). La orden puede traer un `codigo_mtrack` opcional;
si el VIN ya está en un viaje (y, cuando trae código, el código coincide) la orden queda vinculada a ese viaje (`viaje_id`); si el VIN entra a un viaje después, se vincula al
agregarlo o en la siguiente ingesta. Respuesta en `camelCase`:

```json
{ "recibidas": 2, "guardadas": 2, "vehiculosCreados": 1, "vehiculosActualizados": 1, "errores": [ { "source_id": 1, "error": "…" } ] }
```

---

## Exportaciones a Excel

Tres archivos `.xlsx` (`Content-Type: application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`) para el coordinador de calidad, el comercial, el `admin` y el `super_admin` (la capacidad `exportar_soportes` de `GET /api/auth/me`); el `inspector` y el
`conductor` reciben `403` y no la tienen. Traen **solo los datos del cliente del usuario** (el `super_admin` ve todos y es el único que elige uno con `cliente_id`; los demás reciben `403` si piden otro). Son descargas binarias:
use `fetch` con el token y descargue el `blob`. El nombre viene en `Content-Disposition` con el tipo y el día de Colombia (`viajes-2026-10-22.xlsx`, `inspecciones-…`, `hallazgos-…`). Las fechas con hora van en hora de Colombia (UTC-5) como fechas de Excel.
Un archivo no pasa de **50 000 filas**: si el filtro trae más responde `400` para que se acote (por ejemplo, con el rango de fechas).

Aceptan los **mismos filtros que el listado** correspondiente (con los mismos nombres y la misma validación: un filtro mal formado responde `400`).

Todas las filas traen: **VIN, marca, tipo de vehículo, operación, cliente, viaje** (código MTrack, placa, tráiler, unidad, conductor y cédula), **etapa y lugar, momento, versión, estado y fechas de inicio y cierre**.

| Ruta | Una fila por | Filtros | Además trae |
|---|---|---|---|
| `GET /api/exportaciones/viajes.xlsx` | unidad (VIN), etapa y momento (salida o llegada) de cada viaje: los cinco registros de la unidad (la llegada del Descargue es su única revisión: no hay fila de salida del Descargue, ni como `sin_inspeccion`; una salida del Descargue cerrada de antes sí se exporta) | `estado`, `codigo_mtrack`, `vin`, `conductor_id`, `inspector_id`, `cliente_id`, `origen_id`, `destino_id`, `desde`, `hasta` (sobre la creación del viaje) | estado del viaje y de la unidad, origen y destino; `Estado` es `sin_inspeccion`, `borrador` o `cerrada` (versión y fechas vacías si no hay inspección) |
| `GET /api/exportaciones/inspecciones.xlsx` | inspección (cada versión) | `vin`, `usuario_id`, `viaje_id`, `momento`, `estado`, `proceso`, `operacion`, `marca`, `cliente_id`, `origen_id`, `destino_id`, `desde`, `hasta` (sobre la fecha de la inspección) | el inspector y las **firmas** de la transferencia: tipo de cierre y, de quien entrega y de quien recibe, quién, fecha e IP |
| `GET /api/exportaciones/hallazgos.xlsx` | hallazgo | `estado_seguimiento`, `origen`, `revisado`, `cliente_id`, `viaje_id`, `vin`, `inspeccion_id`, `nivel`, `cuadrante_numero`, `lado`, `proceso`, `desde`, `hasta` (sobre el cierre de la inspección) y `sin_calificar` (el mismo filtro del listado de hallazgos) | **zona** (lado, número y nombre), clase, tamaño, nivel, si es preexistente, origen, estado de seguimiento y la **fecha, hora y ubicación de su foto** |

Los filtros llevan los nombres `snake_case` de cada listado (ver «Paginación y filtros»); la exportación no pagina ni ordena (`pagina`, `tamano` y `orden` no aplican). El filtro `vin` sigue la regla única del VIN. `cliente_id` (viajes, inspecciones y hallazgos) solo lo elige el `super_admin`.

## Reportes

### Reportes del tablero (`/api/reportes/*`)

Los ven el `coordinador_calidad`, el `comercial`, el `admin` y el `super_admin` (la capacidad `ver_reportes` de `GET /api/auth/me`); el `inspector` y el `conductor` reciben `403` y no la tienen. Van en `snake_case`.
Cuentan **los hallazgos de las inspecciones cerradas aunque la unidad no haya terminado el viaje** (una unidad que sigue en ruta ya cuenta), por zona (**lado + número**) y por el **cliente del vehículo** (no el del viaje).
Los días son días de Colombia (UTC-5). Quien no es `super_admin` recibe solo los datos de su cliente.

**Filtros** (los mismos en todos; un valor mal formado responde `400` con el campo en `details.fieldErrors`):

| Parámetro | Descripción |
|---|---|
| `desde`, `hasta` | días `AAAA-MM-DD`, ambos incluidos; por defecto los **últimos 30 días** hasta hoy; `desde` no puede ser posterior a `hasta` ni el periodo pasar de 366 días |
| `operacion` | `importacion`, `exportacion` o `nacional` |
| `cliente_id` | solo el `super_admin` elige un cliente (o ninguno: ve todos); los demás reciben su cliente y `403` si piden otro |
| `punto_id` | un punto de custodia: los viajes que **salen de él o llegan a él** |
| `marca`, `trailer` | texto **parcial**: busca por contenido, sin distinguir mayúsculas y recortando los espacios de los bordes («tr-12» encuentra «TR-123» y no «TR-145») |
| `proceso` | `cargue`, `transporte` o `descargue` (en tiempo de captura y en los hallazgos: averías y patrones) |
| `nivel` | `v1_plus`, `v1`, `v2` o `v3` (en los hallazgos: averías y patrones) |

El `proceso` y el `nivel` se ignoran en los reportes que no tienen esos datos (cobertura, adopción e indicador diario). La fecha de cada reporte es la del **despacho** (cierre de la salida del Cargue) en la cobertura y el indicador diario, la de
**programación** del viaje en la adopción y la del **cierre de la inspección** en el tiempo de captura, las averías y los patrones.

### `GET /api/reportes/cobertura`
Unidades despachadas (un VIN de un viaje con la **salida del Cargue cerrada**, sin las unidades marcadas «no sale») y cuántas tienen sus **cinco registros cerrados** (llegada del Cargue, acomodación —la salida del Cargue—, salida y llegada de Transporte y llegada del Descargue; el Descargue ya no tiene salida y la que exista de antes no cuenta). Un carro que ya tenía sus seis registros sigue completo. `cobertura` = completas / despachadas, en porcentaje con dos
decimales; `meta` es 90. Una unidad cuenta en el punto de origen y en el de destino de su viaje.

```json
{
  "despachadas": 120, "completas": 96, "cobertura": 80.0, "meta": 90,
  "por_operacion": [ { "operacion": "nacional", "despachadas": 80, "completas": 70, "cobertura": 87.5 } ],
  "por_cliente": [ { "cliente_id": "uuid", "cliente_nombre": "Sofasa", "despachadas": 120, "completas": 96, "cobertura": 80.0 } ],
  "por_punto": [ { "punto_id": "uuid", "punto_nombre": "CEDI 1", "despachadas": 60, "completas": 50, "cobertura": 83.33 } ]
}
```

### `GET /api/reportes/adopcion`
De lo **programado** cada día (viajes no cancelados, no solo los que ya salieron del Cargue: con ese denominador la adopción daría siempre 100 %), cuánto tiene su **salida del Cargue cerrada en el sistema**; lo que no la tiene se hizo en papel o no se hizo. **Solo cuenta la
salida cerrada**: una salida que quedó en borrador ya no suma. El día es el de la **fecha programada** del viaje (hora de Colombia) y, en los viajes anteriores que no la tienen, el de su creación. `meta` es 95.

Se entrega con **dos medidas**, cada una con su numerador (`con_salida_digital`), su denominador (`programados`) y su desglose por **destino**:

| Medida | Qué cuenta | Adoptado cuando |
|---|---|---|
| `por_camion` | cada **viaje (camión) una sola vez**, en el destino del viaje | **alguna** de sus unidades tiene su salida del Cargue cerrada |
| `por_carro` | cada **VIN programado** en el destino de **su** unidad | su salida del Cargue está cerrada |

`por_camion.por_destino[].lugar_id` y `lugar` son `null` donde no hay un destino único: los viajes cuyas unidades van a **varios destinos** (por camión) y las unidades de viajes anteriores que no guardaron destino. Ese grupo va siempre al final; los demás, por nombre del lugar. Los totales de arriba
(`programados`, `con_salida_digital`, `adopcion`) y `por_dia`, `por_operacion` y `por_cliente` son los **por carro**, como hasta ahora, solo que sin la salida en borrador.

```json
{
  "programados": 100, "con_salida_digital": 92, "adopcion": 92.0, "meta": 95,
  "por_dia": [ { "fecha": "2026-10-20", "programados": 40, "con_salida_digital": 38, "adopcion": 95.0 } ],
  "por_operacion": [ { "operacion": "nacional", "programados": 60, "con_salida_digital": 58, "adopcion": 96.67 } ],
  "por_cliente": [ { "cliente_id": "uuid", "cliente_nombre": "Sofasa", "programados": 100, "con_salida_digital": 92, "adopcion": 92.0 } ],
  "por_camion": {
    "programados": 20, "con_salida_digital": 19, "adopcion": 95.0,
    "por_destino": [ { "lugar_id": "uuid", "lugar": "Concesionario Norte", "programados": 12, "con_salida_digital": 12, "adopcion": 100.0 }, { "lugar_id": null, "lugar": null, "programados": 2, "con_salida_digital": 1, "adopcion": 50.0 } ]
  },
  "por_carro": {
    "programados": 100, "con_salida_digital": 92, "adopcion": 92.0,
    "por_destino": [ { "lugar_id": "uuid", "lugar": "Concesionario Norte", "programados": 60, "con_salida_digital": 58, "adopcion": 96.67 } ]
  }
}
```

### `GET /api/reportes/tiempo-captura`
La **mediana**, en minutos, entre el inicio y el cierre de las inspecciones **cerradas de Cargue y Descargue** (las de Transporte no cuentan). `mediana_minutos` es `null` si no hubo inspecciones.

```json
{
  "mediana_minutos": 15.0,
  "por_proceso": [ { "proceso": "cargue", "mediana_minutos": 15.0, "inspecciones": 5 } ],
  "por_inspector": [ { "usuario_id": "uuid", "nombre": "Ines Uno", "mediana_minutos": 20.0, "inspecciones": 5 } ]
}
```

### `GET /api/reportes/averias`
Por configuración del viaje, los VIN (`vines`: las unidades despachadas más las que tienen hallazgos aunque no se hayan despachado, como la que no sale), cuántos tienen novedad (`con_novedad`: al menos un hallazgo) y cuántos hallazgos. Cada agrupación puede traer una
fila con la clave en `null` (unidades sin trailer, sin conductor o sin tipo de unidad). Ordenadas de más a menos VIN.

```json
{
  "por_trailer": [ { "trailer": "TR-1", "vines": 12, "con_novedad": 3, "hallazgos": 4 } ],
  "por_placa": [ { "placa": "ABC123", "vines": 12, "con_novedad": 3, "hallazgos": 4 } ],
  "por_conductor": [ { "usuario_id": "uuid", "nombre": "Carlos Conductor", "cedula": "1032456789", "vines": 12, "con_novedad": 3, "hallazgos": 4 } ],
  "por_unidad": [ { "tipo_unidad_id": "uuid", "unidad": "Niñera", "vines": 12, "con_novedad": 3, "hallazgos": 4 } ],
  "por_marca": [ { "marca": "Renault", "vines": 12, "con_novedad": 3, "hallazgos": 4 } ],
  "por_clase": [ { "clase": "golpe", "total": 6 } ],
  "por_nivel": [ { "nivel": "v1", "total": 2 }, { "nivel": null, "total": 1 } ],
  "sin_calificar": 1
}
```

`sin_calificar` son los **daños sin nivel** que entran en estas cuentas (la misma fila `nivel: null` de `por_nivel`): cuentan por su zona y su clase, y una novedad del conductor aún sin revisar, que no tiene zona ni clase, cuenta por proceso y aparece con la zona y la clase en `null`. Ninguno queda fuera en silencio. El filtro `nivel` solo trae los de ese nivel.

### `GET /api/reportes/novedades-proceso`
Las **novedades de proceso** del periodo (cargue sin inspector y recibo sin entrega; no son hallazgos y no cuentan en los patrones ni en la tasa de rechazo): el total y su conteo por tipo, por punto de custodia (el de la etapa), por cliente (el del vehículo) y por mes (`AAAA-MM`, hora de Colombia). Usa los mismos filtros que los demás reportes
(`desde` y `hasta` sobre la fecha de la novedad, `operacion`, `cliente_id`, `punto_id` —origen o destino del viaje—, `marca`, `trailer` y `proceso`); el filtro `nivel` no aplica. Ordenado de mayor a menor.

```json
{
  "total": 6,
  "por_tipo": [ { "tipo": "cargue_sin_inspector", "total": 5 }, { "tipo": "recibo_sin_entrega", "total": 1 } ],
  "por_punto": [ { "punto_id": "uuid", "punto_nombre": "CEDI 1", "total": 6 } ],
  "por_cliente": [ { "cliente_id": "uuid", "cliente_nombre": "Sofasa", "total": 6 } ],
  "por_mes": [ { "mes": "2026-09", "total": 2 }, { "mes": "2026-10", "total": 4 } ]
}
```

### `GET /api/reportes/patrones`
El mapa de calor: los hallazgos por proceso, por **zona (lado + número de cuadrante)** y por zona y proceso, y por nivel, clase y tamaño. `supera_umbral` es `true` si los hallazgos del grupo **en la ventana del umbral** (los `ventana_dias`
días que terminan en `hasta`, con los mismos filtros) llegan a `umbral.cantidad`; el umbral sale de los parámetros `umbral_patron_cantidad` y `umbral_patron_ventana_dias`. Reemplaza a `GET /api/patrones` y `GET /api/patrones/analisis`, que se retiraron en la versión 2.1.0 (responden `404`).

```json
{
  "total": 40,
  "por_proceso": [ { "proceso": "cargue", "total": 12, "supera_umbral": true } ],
  "por_region": [
    { "cuadrante_numero": 7, "lado": "izquierdo", "nombre": "Puerta delantera", "zona": "Izquierdo 7 · Puerta delantera", "total": 9, "supera_umbral": false, "extra": false },
    { "cuadrante_numero": null, "lado": null, "nombre": "Extras del vehículo", "zona": "Extras del vehículo", "total": 3, "supera_umbral": false, "extra": true },
    { "cuadrante_numero": null, "lado": null, "nombre": "Sin zona en el dibujo", "zona": "Sin zona en el dibujo", "total": 1, "supera_umbral": false, "extra": false }
  ],
  "por_region_proceso": [ { "cuadrante_numero": 7, "lado": "izquierdo", "nombre": "Puerta delantera", "zona": "Izquierdo 7 · Puerta delantera", "proceso": "cargue", "total": 4, "supera_umbral": false, "extra": false } ],
  "por_nivel": [ { "nivel": "v1", "total": 5 } ],
  "por_clase": [ { "clase": "golpe", "total": 14 } ],
  "por_tamano": [ { "tamano": "5_10cm", "total": 8 } ],
  "umbral": { "cantidad": 10, "ventana_dias": 30 },
  "sin_calificar": 2
}
```

El mapa de calor y los patrones **cuentan el daño sin nivel** por su zona, su clase y su proceso, y `sin_calificar` dice cuántos de los `total` lo son («12 daños · 2 sin calificar»); en `por_nivel` salen con `nivel: null`. Las alertas de umbral también los cuentan.

**Los daños de extras y los hallazgos sin zona tienen cada uno su fila.** Además de las filas de cada zona con cuadrante numerado (que no cambian: `extra: false`), `por_region` y `por_region_proceso` traen, cuando hay datos, hasta dos grupos **sin cuadrante** (`cuadrante_numero: null` y `lado: null`) con un rótulo fijo, que
no depende de ningún hallazgo del grupo:

| Grupo | `extra` | `nombre` y `zona` | Qué cuenta |
|---|---|---|---|
| Daños de extras | `true` | «Extras del vehículo» | el daño de cualquier tipo de extra (un extra no tiene zona en el dibujo y no se dibuja en el vehículo: el portal no pinta ningún punto por él); los de todos los tipos van juntos |
| Sin zona | `false` | «Sin zona en el dibujo» | los hallazgos sin zona que no son de un extra: los históricos o desvinculados del dibujo anterior y la novedad del conductor aún sin revisar |

`extra` es un indicador explícito: el portal lo usa en lugar de leer el rótulo. En `por_region_proceso` hay una fila de cada grupo **por proceso**. Los daños de extras y los hallazgos sin zona **siguen contando** en `total`, en `por_proceso`, `por_nivel`, `por_clase`, `por_tamano`, en la tasa de rechazo y en las alertas de umbral (`supera_umbral` de cada grupo se calcula con sus propios hallazgos de la ventana).

### `GET /api/reportes/indicador-diario?fecha=`
De las unidades **despachadas el día `fecha`** (`AAAA-MM-DD`; por defecto `hasta` o, si tampoco llega, hoy), el porcentaje de **vehículos con novedad**: los que tuvieron **al menos un hallazgo de nivel V1** ese día (no cualquier hallazgo; los de nivel V1+ también cuentan, como
confirmado por el negocio). `objetivo` es el parámetro `objetivo_diario_novedad` (1,2 por defecto). `desde` se ignora.

```json
{
  "fecha": "2026-10-20", "despachados": 80, "con_novedad": 1, "porcentaje": 1.25, "objetivo": 1.2,
  "por_operacion": [ { "operacion": "nacional", "despachados": 50, "con_novedad": 1, "porcentaje": 2.0, "sin_calificar": 2 } ],
  "por_cliente": [ { "cliente_id": "uuid", "cliente_nombre": "Sofasa", "despachados": 80, "con_novedad": 1, "porcentaje": 1.25, "sin_calificar": 3 } ],
  "sin_calificar": 3
}
```

`sin_calificar` es una **cifra aparte**: los daños sin nivel (hallazgos, no unidades) de la **versión vigente** de los informes de las unidades despachadas ese día (los de una versión que una corrección cerrada ya reemplazó no cuentan). No se suma a `con_novedad` ni al porcentaje, y tampoco se descarta. La tasa de rechazo (`GET /api/metricas/rechazo`) no cambia: sigue contando las unidades con al menos un hallazgo.

La tasa de rechazo (`GET /api/metricas/rechazo`) sigue publicada como está; no hay `GET /api/reportes/rechazo`.

### Reporte diario por correo
Cada día, a la hora del parámetro `reporte_diario_hora` (hora de Colombia, `06:00` por defecto), el backend calcula **el día anterior** con el mismo cálculo de `GET /api/reportes/indicador-diario?fecha=` (despachados, con novedad, porcentaje frente a `objetivo_diario_novedad`,
por operación y por cliente) y con la mediana del tiempo de captura de `GET /api/reportes/tiempo-captura` de ese día, y lo envía por correo a `reporte_diario_destinatarios` (correos separados por coma):

- **Asunto:** `Reporte diario IA Autos {AAAA-MM-DD}` (el día reportado). **Cuerpo:** tablas en HTML con el indicador, sus filas por operación y por cliente, y la mediana de captura (global y por proceso). No trae «rechazos del día».
- **Adjunto:** `hallazgos-{AAAA-MM-DD}.xlsx`, el mismo Excel de `GET /api/exportaciones/hallazgos.xlsx` con los hallazgos de las inspecciones cerradas ese día (hora de Colombia), de todos los clientes.
- **Registro:** cada ejecución que actúa deja una fila en `tblNotificaciones` con `Tipo = reporte_diario`, `Canal = correo` y sin destinatario (`UsuarioId` nulo), con `EnviadaAt` si el correo salió o `ErrorEnvio` si no. Son filas de la tabla; no aparecen en `GET /api/notificaciones`, que muestra la bandeja de cada usuario.
- **Sin destinatarios** (el valor inicial): no se envía nada y queda una fila con `ErrorEnvio = «No se envió: el parámetro reporte_diario_destinatarios no tiene destinatarios»`. Ese día queda cerrado aunque se configuren destinatarios después.
- **Si el envío falla** (servidor de correo caído o sin configurar) queda la fila con el error y se reintenta cada 15 minutos hasta 3 intentos por día; no se repite un día ya enviado.

El correo sale por el servicio interno MailSender API, que se configura por variables de entorno (`Correo__Url` y `Correo__Remitente`; ver [docs/configuracion.md](docs/configuracion.md)). La hora y los destinatarios se cambian con `PUT /api/parametros/{clave}` y se aplican en un minuto.

### Reporte anterior (camelCase)

Autenticado. Todos sus campos van en `camelCase`.

### `GET /api/metricas/rechazo`
Tasa de rechazo (unidades movilizadas con al menos un hallazgo) frente a la línea base.

```json
{
  "tasaGlobal": 0.12, "totalMovilizadas": 100, "totalPendientes": 30, "totalConNovedad": 12, "totalNovedad": 12,
  "porOperacion": [ { "operacion": "nacional", "total": 60, "conNovedad": 6, "tasaActual": 0.1, "lineaBase": 0.08, "variacion": 0.02 } ]
}
```

---

## Visibilidad por cliente

Cada usuario de un cliente ve **solo las unidades de su cliente** y lo que cuelga de ellas. El cliente de una unidad es el de su vehículo (`tblVehiculos.ClienteId`) y el del usuario viene de su sesión (`cliente` en `GET /api/auth/me`).

| Rol | Qué ve |
|---|---|
| `coordinador_calidad`, `comercial`, `admin` | solo lo de **su cliente**; un recurso de otro cliente responde `403` |
| `super_admin` | **todos** los clientes; es el único que puede pedir un cliente distinto del suyo (`cliente_id`) |
| `inspector`, `conductor` | no se limitan por cliente sino por **viaje**: ven lo de los viajes en los que participan |

Un usuario de un cliente **sin cliente asignado** no ve ninguna unidad.

**Qué se filtra** (lectura):

| Endpoint | Para quien se limita a su cliente |
|---|---|
| `GET /api/vehiculos?vin=` y `GET /api/vehiculos/listado` | solo los VIN de su cliente (el total y la paginación cuentan solo esos) |
| `GET /api/vehiculos/{vin}/inventario` y `/pdf` | `403` con el VIN de otro cliente («El VIN no es de su cliente»); `404` si no existe |
| `GET /api/inspecciones` | solo los informes de VIN de su cliente |
| `GET /api/inspecciones/{id}`, `GET /api/inspecciones/{id}/pdf`, `GET /api/inspecciones/{id}/hallazgos-previos` y `GET /api/inspecciones/{id}/transferencia/constancia/pdf` | `403` con un informe de un VIN de otro cliente («La inspección es de un VIN de otro cliente») |
| `GET /api/viajes` y `GET /api/viajes/{id}` | solo los viajes con algún VIN de su cliente, y en el detalle solo esos VIN; `403` con un viaje sin VIN suyos |
| `GET /api/sofasa/ordenes` | solo las órdenes de VIN de su cliente |
| `GET /api/reportes/*` y `/api/metricas/rechazo` | cuentan solo las unidades de su cliente |
| `GET /api/usuarios` | solo los usuarios de su cliente |

Los endpoints de hallazgos, alertas, auditoría y exportaciones que se agreguen después aplican la misma regla: filtran por el cliente de quien consulta y responden `403` con un recurso de otro.

**El cliente se elige solo como super administrador.** `cliente_id` en `GET /api/viajes` y `GET /api/usuarios` lo usa el super administrador para ver un cliente; quien se limita a su cliente puede enviar el suyo (no cambia nada) y enviar **otro responde `403`**
(antes se ignoraba y se devolvía lo suyo).

**Viaje con unidades de dos clientes.** El usuario de un cliente ve en el detalle solo los VIN de su cliente (`vehiculos`, `clientes`, `total_vehiculos` y `vehiculos_completos` se calculan sobre ellos). El **conductor y el inspector del viaje ven todos** los VIN: es una propuesta, pendiente de confirmar por el negocio.

---

## Notas para el consumo

- **CORS.** El navegador solo podrá llamar a la API si su origen exacto está en `Cors__Origins` (registre el origen del portal: un origen no registrado se bloquea). El nombre del PDF de inventario llega en `Content-Disposition`, que la API expone. La cabecera `X-Dispositivo-Id` está permitida.
- **Reintentos.** Reintente solo `401` (tras renovar sesión), `408`, `429` y `5xx`. No reintente `400`, `403`, `404` ni `409`
  (salvo el `409` al cerrar, ver arriba).
- **Cierre de sesión por inactividad y expiración.** Ante un `401` en una ruta protegida, borre el token y vaya al login.
- **IDs y enumeraciones.** Trate los valores de los catálogos como cadenas cerradas; un valor fuera del catálogo da `400`.
- **Este documento refleja el estado actual.** Si un endpoint cambia, se actualiza junto con `docs/openapi.json`.
