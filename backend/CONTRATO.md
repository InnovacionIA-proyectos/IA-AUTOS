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
| CORS | La API solo acepta los orígenes **exactos** de `Cors__Origins` (sin comodín `*`) y expone `Content-Disposition` (nombre del PDF de inventario). La web debe estar en esa lista |

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
| `ETAPA_PREVIA_SIN_CIERRE` | 409 | la etapa anterior del VIN no tiene su llegada cerrada: no se puede crear la salida de la siguiente (`details.etapa_previa` dice cuál) |
| `PIN_CHANGE_REQUIRED` | 403 | el PIN fue asignado o reiniciado por un administrador: hay que cambiarlo (`POST /api/auth/cambiar-pin`) antes de usar el resto de la API |
| `ACCOUNT_LOCKED` | 423 | la cuenta está bloqueada por PIN errados seguidos; `details.bloqueado_hasta` (ISO 8601) dice hasta cuándo, o un administrador la desbloquea |
| `NOT_FOUND` | 404 | recurso o ruta inexistente |
| `METHOD_NOT_ALLOWED` | 405 | método no admitido en una ruta existente |
| `CONFLICT` | 409 | duplicados (por ejemplo un código MTrack repetido), inspección cerrada, transferencia ya firmada, viaje finalizado o cancelado |
| `RATE_LIMITED` | 429 | demasiados intentos de login (10 por minuto por IP en `login`, 60 en `login-conductor`; configurable) |
| `INTERNAL_ERROR` | 500 | error inesperado, sin detalles internos |
| `DB_UNAVAILABLE` | 503 | no se pudo verificar la identidad en la base de usuarios del portal (TI) |

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
| `ver_inspecciones`, `ver_viajes`, `ver_reportes` | todos |
| `registrar_inspecciones` | `inspector`, `conductor`, `admin` |
| `gestionar_viajes` | `admin`, `super_admin`, `coordinador_calidad` |
| `gestionar_usuarios` | `admin`, `super_admin`, `coordinador_calidad` |
| `administrar_catalogos` | `admin`, `super_admin`, `coordinador_calidad` |
| `cargar_vehiculos` | `admin`, `super_admin` |

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
  correo o cédula), `rol`, `activo` (`true` o `false`), `clienteId` (solo `super_admin`; los demás siempre ven su cliente), `pagina` (desde 1) y `tamano`
  (50 por defecto, máximo 200). Un filtro mal formado es `400`.
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
| `vin` | sí |
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
Autenticado. Autocompletado por fragmento de VIN o marca (máximo **8** resultados). Inspector y conductor solo encuentran los VIN de sus viajes.

```json
[ { "vin": "9BWZZZ377VT004251", "marca": "Renault", "modelo": "Duster", "color": "Gris", "operacion": "nacional" } ]
```

### `GET /api/vehiculos/listado?tipo=&vin=&page=`
Autenticado. Listado paginado, **500** filas por página. `movilizados` son los VIN que completaron su ruta (todas sus etapas con informe cerrado) en algún viaje; `pendientes`, el resto,
incluidos los que aún no están en un viaje. Inspector y conductor solo ven los VIN de sus viajes.

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
Autenticado. Inventario completo de un VIN: etapas (de todos sus viajes), y dentro de cada una sus inspecciones con hallazgos, evidencias y
transferencia. Un inspector o conductor recibe `403` si el VIN no está en sus viajes (`404` si no existe) y, del VIN, ve solo las etapas de sus viajes.

```json
{
  "id": "uuid", "vin": "…", "marca": "…", "tipo_vehiculo": null, "modelo": null, "chasis": null, "color": null,
  "operacion": "nacional", "created_at": "…", "updated_at": "…",
  "etapas": [
    {
      "id": "uuid", "orden": 1, "proceso": "cargue", "lugar": "CEDI 1", "requiere_inspeccion": true,
      "viaje_id": "uuid", "codigo_mtrack": "MTK-001", "con_personal_apoyo": false,
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

### `GET /api/vehiculos/{vin}/inventario/pdf`
Autenticado. Devuelve el inventario en PDF (`Content-Type: application/pdf`, nombre `inventario-{vin}.pdf`). Es una
descarga binaria, no JSON; en errores sí devuelve el JSON de error. La solicitud necesita el header `Authorization`, así que
use `fetch` con el token y descargue el `blob` (un enlace directo no lo envía).

---

## Viajes

El **viaje es el eje del modelo**: una unidad transportadora (tipo, placa, tráiler) lleva uno o varios VIN con un conductor (y, si hay, un
inspector) entre un origen y un destino. Cada etapa e inspección cuelga de un viaje, y **inspectores y conductores solo ven lo de sus
viajes** (ver [Visibilidad por viaje](#visibilidad-por-viaje)). No hay integración con MTrack: el `codigo_mtrack` se digita o llega en el Excel.

Crear y administrar viajes es de `coordinador_calidad`, `admin` y `super_admin` (grupo *Gestión de viajes*); cualquier otro rol recibe `403`.
Las lecturas son para cualquier usuario autenticado, filtradas por la visibilidad.

Los VIN **no se crean aquí**: deben existir ya en el inventario (`POST /api/vehiculos/cargar` o la ingesta Sofasa). Al agregar un VIN a un viaje se
crean sus etapas, una por cada valor **activo** del catálogo `etapas` (hoy Cargue, Transporte y Descargue): el **Cargue** se hace en el
`origen` del viaje, el **Descargue** en su `destino` y el Transporte no tiene lugar. Si el viaje tiene `con_personal_apoyo`, el Cargue queda marcado
(con personal de apoyo la inspección de salida la hace un inspector; sin él, la hace el conductor).

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
  "estado": "programado", "con_personal_apoyo": false,
  "vehiculos": [
    { "vehiculo_id": "uuid", "vin": "9BWZZZ377VT004251", "marca": "Renault", "modelo": "Duster", "color": "Gris", "operacion": "nacional",
      "etapas": 3, "completas": 0 }
  ],
  "created_at": "…", "updated_at": "…"
}
```

`codigo_mtrack`, `tipo_unidad_id` y `placa` pueden ser `null` solo en los viajes migrados desde las asignaciones anteriores. `etapas` y `completas`
de cada VIN cuentan sus etapas del viaje y las que ya tienen informe cerrado.

### `POST /api/viajes`
Gestión de viajes. `201` con el `ViajeDto`.

```json
{
  "codigo_mtrack": "MTK-001", "tipo_unidad_id": "uuid", "placa": "ABC123", "trailer": "T-9",
  "conductor_id": "uuid", "inspector_id": null, "origen_id": "uuid", "destino_id": "uuid",
  "con_personal_apoyo": false, "vines": ["9BWZZZ377VT004251", "9BWZZZ377VT004252"]
}
```

| Campo | Obligatorio | Regla |
|---|---|---|
| `codigo_mtrack` | sí | letras, números y `. _ - /`, máximo 50; **único** (sin distinguir mayúsculas) |
| `tipo_unidad_id` | sí | `id` de un valor **activo** del catálogo `tipos-unidad` |
| `placa` | sí | máximo 50 caracteres |
| `trailer` | no | máximo 50 caracteres |
| `conductor_id` | no | usuario activo con rol `conductor` |
| `inspector_id` | no | usuario activo con rol `inspector` |
| `origen_id`, `destino_id` | no | `id` de un lugar **activo** del catálogo `puntos-custodia` |
| `con_personal_apoyo` | no | por defecto `false` |
| `vines` | no | hasta 500 VIN que ya existan; sin repetidos |

Errores: `409` si el `codigo_mtrack` ya existe (el viaje **no se crea**) o si algún VIN ya está en otro viaje activo; `400` con `details.fieldErrors` si un
campo es inválido, una referencia no existe, está inactiva o es de otro rol, o algún VIN no existe (`vines`). Todo o nada: si algo falla no se crea el viaje ni
ninguna etapa.

### `GET /api/viajes`
Autenticado. Hasta **200** viajes, los más recientes primero, como `ViajeResumenDto` (los campos del viaje sin `vehiculos`, más `total_vehiculos` y
`vehiculos_completos`: cuántos VIN ya completaron su ruta en el viaje). Filtros opcionales en la consulta: `estado`, `codigoMtrack` (fragmento),
`vin` (fragmento), `conductorId`, `desde`, `hasta` (fecha ISO sobre la creación). Un filtro mal formado responde `400` con `fieldErrors`. Inspectores y
conductores solo reciben sus viajes.

### `GET /api/viajes/mios`
Autenticado. Los viajes del usuario (donde es conductor o inspector) **con sus VIN**, como `ViajeDto`. Sin parámetros trae los `programado` y `en_curso`;
con `?estado=` trae los de ese estado (`finalizado`, por ejemplo). Es la entrada de «Mis viajes» en la app.

### `GET /api/viajes/{id}`
Autenticado. El `ViajeDto`. `403` si es un inspector o conductor y el viaje no es suyo; `404` si no existe.

### `PATCH /api/viajes/{id}`
Gestión de viajes. Cambia **solo los campos enviados** de un viaje `programado` o `en_curso`; responde `200` con el `ViajeDto`. Campos: `codigo_mtrack`,
`tipo_unidad_id`, `placa`, `trailer`, `conductor_id`, `inspector_id`, `origen_id`, `destino_id`, `con_personal_apoyo` (mismas reglas que al crear).
`trailer`, `conductor_id`, `inspector_id`, `origen_id` y `destino_id` admiten `null` para quitarlos; `codigo_mtrack`, `tipo_unidad_id`, `placa` y
`con_personal_apoyo` no. Un campo que no existe responde `400`; un `codigo_mtrack` repetido, `409`; un viaje finalizado o cancelado, `409`.

Al cambiar `origen_id` o `destino_id` se actualiza el lugar del Cargue o del Descargue de los VIN del viaje, y `con_personal_apoyo` marca el Cargue; las etapas que
ya tienen un informe cerrado no cambian.

### `PATCH /api/viajes/{id}/estado`
Gestión de viajes. Cuerpo `{ "estado": "en_curso" }`. `200` con el `ViajeDto`. Un estado desconocido responde `400`; un cambio que el ciclo no permite
(por ejemplo `programado` a `finalizado`, o salir de `finalizado`), `409`.

### `POST /api/viajes/{id}/vehiculos`
Gestión de viajes. Asigna VIN a un viaje activo y crea sus etapas. Cuerpo `{ "vines": ["…"] }` (de 1 a 500). `200` con el `ViajeDto`. **Todo o nada:** `400`
si algún VIN no existe; `409` si alguno ya está en este viaje o en otro viaje activo (el mensaje dice cuáles).

### `DELETE /api/viajes/{id}/vehiculos/{vin}`
Gestión de viajes. Quita un VIN del viaje y borra sus etapas. `200` con el `ViajeDto`. `404` si el VIN no está en el viaje; `409` si el VIN ya tiene inspecciones
en este viaje.

### `DELETE /api/viajes/{id}`
Gestión de viajes. Borra el viaje con sus VIN y etapas, solo si **no tiene inspecciones** (`204`); si las tiene, `409` y debe cancelarse con el cambio de estado.

### Carga de viajes por Excel
El archivo tiene **una fila por VIN**: VIN, placa, tráiler, cédula del conductor, unidad y código MTrack. El cliente lee el Excel y envía las filas como JSON; las filas con el
mismo `codigo_mtrack` forman **un solo viaje**. Son dos pasos: primero una **vista previa** con el error de cada fila (no guarda nada) y, al confirmar,
**solo se guardan los viajes de las filas válidas**.

```json
{
  "filas": [
    { "fila": 2, "vin": "9BWZZZ377VT004251", "placa": "ABC123", "trailer": "T-9", "cedula_conductor": "1.234.567", "unidad": "Niñera", "codigo_mtrack": "MTK-001" },
    { "fila": 3, "vin": "9BWZZZ377VT004252", "placa": "ABC123", "trailer": "T-9", "cedula_conductor": "1.234.567", "unidad": "Niñera", "codigo_mtrack": "MTK-001" }
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
| `unidad` | sí | el `codigo` o el nombre de un valor **activo** de `tipos-unidad` (sin distinguir tildes ni mayúsculas) |
| `codigo_mtrack` | sí | formato y unicidad como en `POST /api/viajes` |

Una fila es inválida si falla un campo, si su `codigo_mtrack` ya existe, si su VIN ya está en un viaje activo, si repite un VIN dentro de su viaje, si el mismo VIN
aparece bajo dos códigos del archivo, o si no coincide en placa, tráiler, conductor o unidad con la primera fila válida de su mismo `codigo_mtrack`. Máximo **5000** filas;
una lista vacía o más grande responde `400`.

- `POST /api/viajes/cargar/previsualizar` (gestión de viajes) → `200`, sin guardar nada.
- `POST /api/viajes/cargar` (gestión de viajes) → `201`, guarda los viajes de las filas válidas; cada viaje se confirma por separado.

Ambos responden con la misma forma:

```json
{
  "confirmado": false, "total": 3, "validas": 2, "invalidas": 1, "viajes": 1,
  "filas": [
    { "fila": 2, "vin": "9BWZZZ377VT004251", "codigo_mtrack": "MTK-001", "valida": true, "errores": [], "viaje_id": null },
    { "fila": 3, "vin": "9BWZZZ377VT004252", "codigo_mtrack": "MTK-001", "valida": true, "errores": [], "viaje_id": null },
    { "fila": 4, "vin": "NO-EXISTE", "codigo_mtrack": "MTK-002", "valida": false, "errores": ["El VIN NO-EXISTE no existe en el inventario: cárguelo antes"], "viaje_id": null }
  ]
}
```

`confirmado` es `false` en la vista previa; `viajes` es cuántos viajes se crearían (vista previa) o se crearon (confirmación). Al confirmar, `viaje_id` trae el viaje de cada
fila guardada. La confirmación vuelve a validar todo: no depende de la vista previa.

### Visibilidad por viaje
`inspector` y `conductor` ven **solo lo de los viajes en los que participan** (son su conductor o su inspector, y el viaje no está cancelado); el resto de roles ven todo.
Se aplica a:

| Recurso | Para un inspector o conductor |
|---|---|
| `GET /api/viajes`, `GET /api/viajes/{id}` | solo sus viajes; el detalle de otro viaje responde `403` |
| `GET /api/vehiculos?vin=` y `GET /api/vehiculos/listado` | solo los VIN de sus viajes |
| `GET /api/vehiculos/{vin}/inventario` (y `/pdf`) | `403` si el VIN no está en sus viajes (`404` si no existe); del VIN ve solo las etapas de sus viajes |
| `GET /api/inspecciones` | solo los informes de sus viajes |
| `GET /api/inspecciones/{id}` | `403` si el informe es de otro viaje |
| `POST /api/inspecciones` | `403` si la etapa es de otro viaje |
| `GET /api/sofasa/ordenes` | solo las órdenes vinculadas a sus viajes |

Los reportes (`/api/patrones`, `/api/metricas/rechazo`) son agregados y no se filtran. La visibilidad por **cliente** (unidades de un cliente y ningún otro) no
está en este servicio.

> **Retirados.** `POST /api/asignaciones` y `POST /api/etapas` ya no existen (responden `404`): las asignaciones pasaron a ser viajes y las etapas nacen al
> agregar un VIN a un viaje.

---

## Inspecciones (informes)

Por cada etapa de un VIN hay una **inspección de salida** (al entregar) y una **inspección de llegada** (al recibir). En pantalla se llaman así: el
`InspeccionDto` trae `momento_nombre` («Inspección de salida» o «Inspección de llegada») listo para mostrar.

Flujo típico: crear la inspección (siempre en `borrador`) → subir las fotos con `POST /api/uploads` → registrarlas como evidencias de cada vista y
agregar hallazgos (cada uno con su foto) → firmar la transferencia → cerrar. Un error en una inspección cerrada **no se edita**: se corrige con una
versión nueva (`POST /api/inspecciones/{id}/corregir`).

| Etapa | Qué exige el cierre |
|---|---|
| Cargue y Descargue | la foto de **cada una de las 7 vistas obligatorias** (las activas de `vistas-foto` con `obligatoria: true` para el tipo del vehículo); una foto propia por hallazgo; ubicación en toda foto. Las 2 vistas adicionales (piezas en el baúl y cojinería) **no bloquean** el cierre |
| Transporte (registro del conductor) | no exige las 7 vistas, pero sí la **ubicación (GPS) y la hora del propio registro** (`lat`/`lng` de la inspección); si hay una novedad, el hallazgo lleva su foto. Cuenta para la cobertura |

Las mismas reglas valen para quien haga la inspección: cuando el cargue no tiene personal de apoyo la hace el conductor, con las mismas 7 vistas, sello y hallazgos.

### `POST /api/inspecciones`
Campo. `201` con la inspección. El usuario sale del token (no se envía) y **la placa, el tráiler y el conductor salen del viaje de la etapa**: ya no se piden como texto.
Inspector y conductor reciben `403` si la etapa es de un viaje que no es suyo; un viaje finalizado o cancelado responde `409`; una etapa inexistente, `404`.

**La salida exige la etapa anterior cerrada.** Si el VIN tiene una etapa anterior en el viaje (por su `orden`) y esa etapa no tiene su **llegada cerrada**, la salida responde
`409` con `code: "ETAPA_PREVIA_SIN_CIERRE"` y `details` con `motivo: "etapa_previa_sin_cierre"`, `etapa_previa_id` y `etapa_previa` (el nombre de la etapa que falta cerrar), para
que la app muestre el bloqueo. La primera etapa y la llegada no tienen este bloqueo. Una etapa ya tiene su inspección original de ese momento: crear otra responde `409` (para
cambiarla, se corrige).

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

```json
{
  "id": "uuid", "etapa_id": "uuid", "viaje_id": "uuid", "usuario_id": "uuid", "momento": "salida", "fecha": "…", "lat": 6.2442, "lng": -75.5812,
  "estado": "borrador", "conductor_nombre": "Juan", "placa": "ABC123", "trailer": "T-01", "notas": "…",
  "created_at": "…", "updated_at": "…",
  "iniciada_at": "…", "cerrada_at": null, "version": 1, "corrige_inspeccion_id": null, "momento_nombre": "Inspección de salida"
}
```

`conductor_nombre`, `placa` y `trailer` de la respuesta son los del viaje (en los informes anteriores a los viajes, lo que se capturó a mano). `iniciada_at` y `cerrada_at`
son la hora del servidor al crear y al cerrar (`cerrada_at` es `null` en borrador). `version` es 1 en la original y 2, 3… en las correcciones, que traen en
`corrige_inspeccion_id` la versión que corrigen.

### `POST /api/inspecciones/{id}/corregir`
Campo (quien participa en el viaje, o un administrador). Corrige una inspección **cerrada**: crea la **versión siguiente** (en `borrador` y vacía: se vuelven a tomar las fotos)
que referencia a la anterior; **la anterior no cambia**. `201` con la `InspeccionDto` nueva. Cuerpo opcional (`fecha`, `lat`, `lng`, `notas`; por defecto la fecha es ahora).

- `409` si la inspección no está cerrada (se sigue editando), si ya fue corregida (se corrige la última versión) o si el viaje está finalizado o cancelado. `403` si el viaje no es del usuario. `404` si no existe.
- La versión nueva tiene sus propias fotos, hallazgos, transferencia y reglas de cierre. Una etapa puede tener varias versiones; los listados e informes las muestran todas con su `version`.

### `GET /api/inspecciones`
Autenticado. Listado de hasta **200** informes. Todos los filtros son opcionales y van en la consulta:

| Filtro | Valor |
|---|---|
| `vin` | VIN completo válido (17 caracteres) |
| `usuarioId` | UUID del inspector |
| `viajeId` | UUID del viaje |
| `momento` | `salida` o `llegada` |
| `estado` | `borrador` o `cerrada` |
| `proceso` | `codigo` de una etapa del catálogo `etapas` (por ejemplo `cargue`, `transporte` o `descargue`), activa o no |
| `desde`, `hasta` | fecha ISO |

Un filtro mal formado responde `400` con `fieldErrors` por campo. Cada fila es el `InspeccionDto` más `vin` y `proceso`. Inspector y conductor solo reciben los informes de sus viajes.

### `GET /api/inspecciones/{id}`
Autenticado. Informe completo para revisión (`InformeDetalleDto`): los datos de la inspección más el contexto y sus partes. Inspector y conductor reciben `403` si el informe es de un viaje que no es suyo.

```json
{
  "id": "uuid", "etapa_id": "uuid", "viaje_id": "uuid", "usuario_id": "uuid", "momento": "salida", "fecha": "…", "lat": null, "lng": null,
  "estado": "cerrada", "conductor_nombre": "Carlos Gómez", "placa": "ABC123", "trailer": "T-9", "notas": null, "created_at": "…", "updated_at": "…",
  "vin": "…", "marca": "…", "modelo": null, "proceso": "cargue", "lugar": null, "requiere_inspeccion": true,
  "inspector_nombre": "Ana Pérez", "ninera": "ABC123", "codigo_mtrack": "MTK-001", "unidad": "Niñera",
  "hallazgos": [ /* HallazgoDto */ ],
  "evidencias": [ /* EvidenciaDto, cada una con su propio sello */ ],
  "transferencia": { /* TransferenciaDetalleDto o null */ },
  "iniciada_at": "…", "cerrada_at": "…", "version": 1, "corrige_inspeccion_id": null, "momento_nombre": "Inspección de salida"
}
```

La placa, el conductor y el tráiler son los **del viaje**; en un informe anterior a los viajes, lo que se capturó a mano tiene prioridad. `ninera` es la placa del viaje (nombre heredado, igual a `placa`).

### `POST /api/inspecciones/{id}/hallazgos`
Campo (creador de la inspección o administrador). **Un hallazgo exige su foto propia**: se guarda junto con ella (`evidencia`, obligatoria; sin ella `400` y no se guarda nada). `201` con el hallazgo y su foto.

```json
{
  "parte_vehiculo": "Puerta delantera izquierda", "clase_dano": "rotura", "tamano_dano": "5_10cm", "coords_diagrama": { "x": 0.42, "y": 0.18 },
  "evidencia": { "storage_path": "3f2b…c1.png", "sello_fecha": "2026-07-17T12:30:15.123Z", "sello_lat": 6.24, "sello_lng": -75.58, "precision": 4.5, "dispositivo_id": "tablet-7", "nota": null, "hash_sha256": "…" }
}
```

| Campo | Obligatorio | Regla |
|---|---|---|
| `parte_vehiculo` | sí | máximo 80 caracteres |
| `clase_dano` | sí | `codigo` **activo** del catálogo `clases-dano` |
| `tamano_dano` | sí | `codigo` **activo** del catálogo `tamanos-dano` |
| `coords_diagrama` | no | `x` e `y` entre 0 y 1 |
| `evidencia` | sí | la foto del hallazgo, con los mismos campos y reglas que una evidencia (sin `vista_id` ni `hallazgo_id`) |

Respuesta (`HallazgoCreadoDto`): el hallazgo más su `evidencia` (`EvidenciaDto`):

```json
{ "id": "uuid", "inspeccion_id": "uuid", "parte_vehiculo": "…", "clase_dano": "rotura", "tamano_dano": "5_10cm",
  "coords_diagrama": { "x": 0.42, "y": 0.18 }, "created_at": "…", "updated_at": "…", "evidencia": { /* EvidenciaDto */ } }
```

En el informe y el inventario, los hallazgos llegan como `HallazgoDto` (el mismo objeto sin `evidencia`) y sus fotos en `evidencias`, con `hallazgo_id`.

### `POST /api/inspecciones/{id}/evidencias`
Campo (creador o administrador). Registra una foto **ya subida** con `POST /api/uploads`: de una **vista** (`vista_id`) o de un **hallazgo** (`hallazgo_id`), no de ambos. `201` con el `EvidenciaDto`.

```json
{
  "vista_id": "uuid", "storage_path": "3f2b…c1.png", "sello_fecha": "2026-07-17T12:30:15.123Z", "sello_lat": 6.24, "sello_lng": -75.58,
  "precision": 4.5, "dispositivo_id": "tablet-7", "nota": null, "hash_sha256": "…"
}
```

| Campo | Obligatorio | Regla |
|---|---|---|
| `vista_id` | sí, si no hay `hallazgo_id` | UUID de una vista **activa** del catálogo `vistas-foto` (una de las 7 obligatorias o de las 2 adicionales) |
| `hallazgo_id` | sí, si no hay `vista_id` | UUID de un hallazgo **de esta inspección** (`404` si es de otra) |
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
  "vista_id": "uuid", "hash_sha256": "…", "sello_servidor_at": "…", "precision": 4.5, "dispositivo_id": "tablet-7", "nota": null, "sin_ubicacion": false }
```

`sin_ubicacion` es `true` cuando la foto se tomó sin señal de GPS (`sello_lat` y `sello_lng` valen 0): muéstrela marcada y pida repetirla antes de cerrar.

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

## Catálogos maestros

Las etapas, los lugares, los tipos de unidad, las clases y tamaños de daño, los niveles de severidad, las vistas de foto, los
cuadrantes y los clientes **ya no son listas fijas en el código**: viven en tablas y los administra TI o el coordinador de calidad desde el portal.
La web y la app deben leerlos de la API (y guardar una copia para trabajar sin señal), no escribirlos a mano.

| Nombre (ruta y clave) | Qué contiene | Campos de cada valor |
|---|---|---|
| `etapas` | Etapas del proceso: `cargue`, `transporte`, `descargue`. Es el `proceso` de un informe o de un tramo de la ruta | `id`, `codigo`, `nombre`, `orden`, `activo` |
| `puntos-custodia` | Lugares donde se hace una etapa: CEDI 1, Origen, Certificadora, Destino, CEDI 2, Concesionario | `id`, `codigo`, `nombre`, `orden`, `activo` |
| `tipos-unidad` | Tipos de unidad transportadora: niñera, nodriza, quintinaria, planchón grúa | `id`, `codigo`, `nombre`, `orden`, `activo` |
| `clases-dano` | Clases de daño de un hallazgo (10 iniciales) | `id`, `codigo`, `nombre`, `orden`, `activo` |
| `tamanos-dano` | Tamaños de daño de un hallazgo (6 iniciales) | `id`, `codigo`, `nombre`, `orden`, `activo` |
| `niveles-severidad` | Niveles V1+, V1, V2 y V3 con su acción | `id`, `codigo`, `nombre`, `orden`, `activo`, `accion` (texto o `null`, máx. 500) |
| `vistas-foto` | Las 9 vistas de foto: 7 obligatorias y 2 adicionales (`piezas_baul`, `cojineria`) que solo se toman con faltante o daño | `id`, `codigo`, `nombre`, `orden`, `activo`, `obligatoria`, `tipo_vehiculo` (`null` = cualquier tipo) |
| `cuadrantes` | Las **zonas** numeradas de la carrocería (puerta delantera, pilar central, capó…). Una zona es la misma aunque se vea desde varias vistas | `id`, `tipo_vehiculo` (`null` = cualquier tipo), `numero`, `nombre`, `orden`, `activo` |
| `cuadrante-vistas` | El **polígono** de cada zona en cada vista donde aparece (una fila por aparición). Las vistas adicionales no tienen | `id`, `cuadrante_id`, `numero` (de la zona), `vista_foto_id`, `vista` (código de la vista), `geometria` |
| `clientes` | Las empresas a las que pertenecen los usuarios y los vehículos (hoy `sofasa`). El primero activo por `orden` es el cliente por defecto de lo que se crea sin cliente explícito | `id`, `codigo`, `nombre`, `orden`, `activo` |

`codigo` es la **clave estable** (minúsculas, números y `_`, máximo 30 caracteres): es lo que viaja en el resto de la API
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
    "niveles-severidad": [ { "id": "uuid", "codigo": "v1", "nombre": "V1", "orden": 2, "activo": true, "accion": null } ],
    "vistas-foto": [ { "id": "uuid", "codigo": "piezas_baul", "nombre": "Piezas en el baúl", "orden": 8, "activo": true, "obligatoria": false, "tipo_vehiculo": null } ],
    "cuadrantes": [ { "id": "uuid", "tipo_vehiculo": null, "numero": 1, "nombre": "Puerta delantera: parte alta", "orden": 1, "activo": true } ],
    "cuadrante-vistas": [ { "id": "uuid", "cuadrante_id": "uuid", "numero": 1, "vista_foto_id": "uuid", "vista": "lateral_izquierda", "geometria": { "puntos": [ [0.34, 0.30], [0.59, 0.30] ] } } ],
    "puntos-custodia": [], "tipos-unidad": [], "tamanos-dano": [], "clientes": []
  }
}
```

### `GET /api/catalogos/{nombre}`
Un solo catálogo con **su propia versión**: `{ "version": "…", "items": [ … ] }`. `404` si el nombre no existe (el mensaje lista los
disponibles).

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

### Cuadrantes y su geometría
Un cuadrante es una **zona** de la carrocería con un número (`cuadrantes`). Esa misma zona se ve desde varias vistas (la puerta delantera
aparece de lado y en diagonal), así que **dónde se dibuja** en cada vista va aparte, en `cuadrante-vistas`: una fila por cada vista
donde aparece. Para dibujar una vista, la app y el portal toman los `cuadrante-vistas` de esa `vista` y le ponen a cada polígono el
`numero` y el nombre de su zona (`cuadrante_id` → `cuadrantes`).

Para que la app y el portal dibujen las mismas regiones, `geometria` es un **polígono normalizado**: un objeto con la lista de puntos,
cada uno `[x, y]` con `x` e `y` entre 0 y 1, medidos sobre la imagen de la vista (el origen es la esquina superior izquierda).

```json
{ "puntos": [ [0.10, 0.10], [0.55, 0.10], [0.55, 0.48], [0.10, 0.48] ] }
```

Debe tener al menos 3 puntos y llega como objeto JSON (no como texto). Es obligatoria en cada polígono. `numero` es único por `tipo_vehiculo`
(con `tipo_vehiculo: null` el número es único entre las zonas sin tipo) y un cuadrante tiene como máximo **un polígono por vista**. Hoy
están cargadas las 36 zonas de la ficha del vehículo genérico (los números 4 y 12 no existen en la ficha) con 97 polígonos en 5 vistas:
`superior`, `lateral_izquierda`, `lateral_derecha`, `diagonal_trasera_izquierda` y `diagonal_delantera_derecha`. Las vistas `frontal` y
`trasera` todavía no tienen cuadrantes dibujados.

### Administración: crear, editar, inactivar y borrar
Solo `admin`, `super_admin` y `coordinador_calidad`; los demás roles reciben `403`.

| Operación | Ruta | Respuesta |
|---|---|---|
| Crear | `POST /api/catalogos/{nombre}` | `201` con el valor creado |
| Editar | `PATCH /api/catalogos/{nombre}/{id}` | `200` con el valor; cambia solo los campos enviados |
| Inactivar | `PATCH /api/catalogos/{nombre}/{id}` con `{ "activo": false }` | `200` |
| Borrar | `DELETE /api/catalogos/{nombre}/{id}` | `204`; `409` si algo lo usa |

Cuerpo de **crear**: `codigo` y `nombre` obligatorios; `orden` (entero ≥ 0, por defecto 0) y `activo` (por defecto `true`) opcionales, más
los campos propios del catálogo. En **cuadrantes** no hay `codigo`: son obligatorios `numero` y `nombre`, y opcionales `tipo_vehiculo`, `orden` y `activo`. En
**cuadrante-vistas** (no tiene `codigo`, `nombre`, `orden` ni `activo`) son obligatorios `cuadrante_id`, `vista_foto_id` y `geometria`; al editar
solo se cambian `vista_foto_id` y `geometria` (el cuadrante no se puede cambiar). En **vistas** `obligatoria` es opcional (por defecto `true`).

```json
{ "codigo": "oxidacion", "nombre": "Oxidación", "orden": 11 }
```

Reglas:
- `400` con `details.fieldErrors` si un campo es inválido, **si llega un campo que ese catálogo no admite** (por ejemplo `accion` en
  `etapas`) o si se envía `null` en `nombre`, `orden` o `activo`.
- El **`codigo` no se puede cambiar** (`400`); se puede reenviar igual. Para reemplazar un código, cree un valor nuevo e inactive el viejo.
- `409` si el `codigo` ya existe en ese catálogo, o si el `numero` de cuadrante ya existe para ese tipo de vehículo, o si el cuadrante ya tiene un polígono en esa vista.
- **Un valor en uso no se borra:** `409` con el mensaje «El valor está en uso y no se puede borrar: solo puede inactivarlo». Está en uso una clase
  o tamaño usado por algún hallazgo, una etapa o un lugar usado por algún tramo de la ruta, un cliente con usuarios o vehículos, y una vista que tiene polígonos de cuadrantes. Borrar un cuadrante borra también sus polígonos.
- Una vista **con polígonos** no puede pasar a `obligatoria: false` (`409`), y no se pueden crear polígonos en una vista adicional ni
  inactiva (`400`).
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
{ "storage_path": "3f2b9c6e-….jpg", "url": "/api/uploads/3f2b9c6e-….jpg", "hash_sha256": "9f86d0…" }
```

Use `storage_path` al registrar la evidencia (el archivo debe haberse subido antes) y `url` para mostrarla. `hash_sha256` es el hash que **calculó el servidor** al recibir el archivo: es el mismo que
queda en la evidencia, y si la app lo reenvía como `hash_sha256` la evidencia comprueba que coincide.

### `GET /api/uploads/{archivo}`
Sirve la imagen (`image/jpeg` o `image/png`). Por defecto es **público** (se puede usar directo en un `<img src>` con la URL
completa); si el despliegue configura la lectura privada, exige el header `Authorization`. Responde con caché larga.

---

## Sofasa

### `GET /api/sofasa/ordenes`
Autenticado. Hasta **500** órdenes, las más recientes primero. Filtros opcionales: `vin`, `estado`, `ciudad`, `desde`, `hasta`
(fechas ISO; una fecha inválida responde `400`). Inspector y conductor solo reciben las órdenes vinculadas a sus viajes.

```json
[
  {
    "id": "uuid", "source_id": 12345, "status_code": "…", "datetime_event": "2026-07-17T12:30:15.123Z",
    "orden_number": "…", "orden_sap": "…", "origin_cedi": "…", "vin": "…", "make": "…", "model_description": "…",
    "dealer_code": "…", "destination_city": "…", "destination_address": "…",
    "loading_date": null, "appointment": null, "loading_dock": null, "weight": 1250.5,
    "vehiculo_id": "uuid", "codigo_mtrack": null, "viaje_id": "uuid", "synced_at": "…", "created_at": "…", "updated_at": "…"
  }
]
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

- **CORS.** El navegador solo podrá llamar a la API si su origen exacto está en `Cors__Origins` (registre el origen del portal: un origen no registrado se bloquea). El nombre del PDF de inventario llega en `Content-Disposition`, que la API expone.
- **Reintentos.** Reintente solo `401` (tras renovar sesión), `408`, `429` y `5xx`. No reintente `400`, `403`, `404` ni `409`
  (salvo el `409` al cerrar, ver arriba).
- **Cierre de sesión por inactividad y expiración.** Ante un `401` en una ruta protegida, borre el token y vaya al login.
- **IDs y enumeraciones.** Trate los valores de los catálogos como cadenas cerradas; un valor fuera del catálogo da `400`.
- **Este documento refleja el estado actual.** Si un endpoint cambia, se actualiza junto con `docs/openapi.json`.
