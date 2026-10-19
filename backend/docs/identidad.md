# Identidad: cédula y PIN, clientes y permisos por rol

Cada persona entra a IA Autos de una de dos formas, y cada usuario y cada vehículo pertenecen a un **cliente** (empresa). Los endpoints están en
[CONTRATO.md](../CONTRATO.md#usuarios).

## Cómo entra cada quien

| Quién | Con qué | Endpoint |
|---|---|---|
| Admin, coordinador, comercial, super admin e inspectores que ya tenían usuario | correo y contraseña | `POST /api/auth/login` |
| Personal de campo (conductor, o inspector sin correo) | cédula y PIN | `POST /api/auth/login-conductor` |
| Quien ya está en el portal de administración | inicio de sesión único | `POST /api/auth/login-portal` |

El `tipo_credencial` de un usuario (`password` o `pin`) lo decide el alta y la base lo exige: con contraseña hay correo; con PIN hay cédula (y solo conductor o
inspector). El conductor siempre tiene cédula. El correo es único **solo entre quienes lo tienen**, así que muchos conductores pueden no tener correo.

## El PIN

- Solo números, de 4 a 8 dígitos, y no trivial (ni `1111` ni `1234`). El que genera la API tiene 6 dígitos.
- Se guarda con el **mismo servicio de hash que las contraseñas** (PBKDF2-SHA256; los hashes `scrypt` heredados se aceptan y se regeneran al ingresar). Nunca en claro.
- Un administrador lo **asigna o reinicia** (`POST /api/usuarios/{id}/pin`) o lo genera el alta y la carga de conductores. Nace **temporal**: la persona debe cambiarlo
  antes de seguir. Mientras sea temporal el `token` sirve, pero toda la API responde `403 PIN_CHANGE_REQUIRED` salvo `cambiar-pin`, `me` y `logout`. La marca se lee de la
  base en cada solicitud, así que no se esquiva con un token anterior.
- Un administrador también puede **forzar el cambio** sin cambiar el PIN (`.../pin/forzar-cambio`).
- El PIN que genera la API sale **una sola vez** en `pin_temporal`; no se puede volver a consultar. Si se pierde, se reinicia.

## Bloqueo por intentos

Cada PIN errado de una cuenta que existe suma un intento (y deja una fila en la auditoría: `auth.login_conductor_fallido`). Al llegar a **5 seguidos** la cuenta se bloquea
**15 minutos** (`auth.cuenta_bloqueada`): desde ese intento, `423 ACCOUNT_LOCKED` con `details.bloqueado_hasta`, aunque el PIN sea el correcto. Se levanta al vencer el tiempo,
con un ingreso correcto posterior, o cuando un administrador usa `POST /api/usuarios/{id}/desbloquear`. Un ingreso correcto reinicia el contador.

La respuesta es la misma («Cédula o PIN incorrectos») exista o no la cédula, y el tiempo de respuesta tampoco la revela. Solo se cuentan los intentos de cuentas reales y activas.
`Seguridad__PinMaxIntentos` y `Seguridad__PinBloqueoMinutos` ajustan los valores; `login-conductor` además tiene un límite por IP.

## Quién administra usuarios

`admin`, `coordinador_calidad` y `super_admin` crean, editan, desactivan, asignan PIN y desbloquean (todo queda en la auditoría: `usuario.crear`, `usuario.actualizar`,
`usuario.retirar`, `usuario.eliminar`, `usuario.pin_asignar`, `usuario.pin_forzar_cambio`, `usuario.desbloquear`). Reglas:

- **Solo el super administrador** asigna el nivel `super_admin`, modifica a otro super administrador y cambia el cliente de alguien. Para los demás, un super administrador no existe (`404`).
- Cada uno ve y administra **solo a los usuarios de su cliente** (los de otro cliente responden `404`). El super administrador ve todos.
- Nadie puede eliminar ni desactivar su propio usuario.
- `GET /api/usuarios` pagina y busca por nombre, correo o cédula; filtra por rol, estado y (super administrador) cliente.
- `PATCH` con `subtipo_inspector: null` lo limpia; al cambiar el rol a uno que no es inspector se limpia solo.
- **Carga de conductores por Excel:** vista previa (valida cada fila, no guarda) y confirmación (crea un conductor con PIN temporal por fila válida, que se devuelve una sola vez).

## Menú según el rol

`GET /api/auth/me` devuelve las `capacidades` del usuario (`ver_inspecciones`, `registrar_inspecciones`, `ver_viajes`, `gestionar_viajes`, `gestionar_usuarios`,
`administrar_catalogos`, `cargar_vehiculos`, `ver_reportes`). Salen de las mismas listas de roles que autorizan los endpoints, así que el menú nunca ofrece algo que la API rechace.

## Clientes

`tblClientes` es un catálogo más (`GET /api/catalogos`, clave `clientes`; se administra con `POST/PATCH/DELETE /api/catalogos/clientes`): `codigo` estable, `nombre`, `orden`, `activo`.
Un cliente con usuarios o vehículos no se borra (`409`): se inactiva. El **cliente por defecto** es el primero activo por `orden` (hoy `sofasa`), y es el que reciben los usuarios y vehículos que se
crean sin cliente explícito; los vehículos de la ingesta Sofasa son siempre del cliente `sofasa`.

| Dónde | Qué pasa |
|---|---|
| `tblUsuarios.ClienteId` | opcional en la base; el super administrador lo tiene nulo (ve todos los clientes) y los demás siempre tienen uno |
| `tblVehiculos.ClienteId` | obligatorio: los vehículos de una carga masiva quedan en el cliente de quien carga |
| Gestión de usuarios | cada quien solo ve y administra a los de su cliente |
| Vehículos, viajes, informes y reportes | **todavía no filtran por cliente** (hoy hay un solo cliente); el dato ya está en los vehículos para hacerlo |

## Tablas

| Tabla | Contenido |
|---|---|
| `tblClientes` | `Codigo` (único), `Nombre`, `Orden`, `Activo`. Semilla: `sofasa` |
| `tblUsuarios` | `Email` pasa a ser opcional (índice único filtrado `UX_tblUsuarios_Email`); gana `PinHash`, `TipoCredencial`, `DebeCambiarPin`, `IntentosFallidos`, `BloqueadoHasta`, `UltimoAccesoAt` y `ClienteId`; `CK_tblUsuarios_Identidad` exige correo con contraseña y cédula con PIN |
| `tblVehiculos` | gana `ClienteId` (obligatorio) |

## Aplicar el cambio en una base existente

Con un usuario que pueda crear tablas e índices (no el de la aplicación), conectado a la base de IA Autos, **antes de desplegar la API**:

1. Ejecutar [`database/cambios-esquema/06-identidad.sql`](../database/cambios-esquema/06-identidad.sql) ajustando `@esquema` (`dbo`, o `db_IA_AUTOS` en la base compartida de pruebas).
   Es una sola transacción: si algo falla, no cambia nada. Se puede repetir (si ya está aplicado, no hace nada). Exige que `05-viajes.sql` ya esté aplicado.
2. Dar permisos al usuario de la aplicación sobre la tabla nueva si no los hereda por esquema (`GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::<esquema> TO <usuario>`).
3. Comprobar el resultado que muestra el script: `vehiculos sin cliente` debe ser 0 y los `usuarios sin cliente` deben ser solo los super administradores.

Todos los usuarios existentes quedan con `TipoCredencial = 'password'` (siguen entrando igual) y en el cliente `sofasa`. Para pasar a alguien a cédula y PIN: cargue su cédula
(`PATCH /api/usuarios/{id}` con `cedula`) y asígnele un PIN (`POST /api/usuarios/{id}/pin`).

Una base nueva se crea con `ddl.sql`, que ya trae el esquema. `tests/verificar-ddl.sql` comprueba las restricciones de la identidad y de los clientes.
