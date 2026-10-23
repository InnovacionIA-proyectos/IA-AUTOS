# Auditoría

Toda acción de escritura (y el inicio de sesión) queda en `tblAuditLog`, un registro **que nadie modifica ni borra**: no hay `PATCH`, `PUT` ni `DELETE`, y la base lo impide con triggers (`TR_tblAuditLog_NoModificar` y `TR_tblAuditLog_NoBorrar`). Las rutas y la forma de las respuestas están en
[CONTRATO.md](../CONTRATO.md) («Auditoría»).

## Qué guarda cada registro

| Campo | Contenido |
|---|---|
| `usuario_id`, `usuario_nombre`, `rol` | quién actuó; `null` en las acciones del sistema (la ingesta Sofasa) |
| `accion`, `entidad`, `entidad_id` | qué se hizo (`inspeccion.cerrar`, `catalogo.clases-dano.actualizar`…) y sobre qué registro |
| `ip`, `dispositivo` | la IP de la conexión (detrás de un proxy, con `Proxy__ConfiarEnEncabezadosReenviados=true`) y el dispositivo (`X-Dispositivo-Id` o, si no llega, el `User-Agent`) |
| `viaje_id`, `codigo_mtrack`, `cliente_id` | el viaje y el cliente de lo auditado, para consultar por ellos |
| `detalle` | `{ antes, despues }` del cambio: `antes` es `null` en un alta y `despues` en una baja; `null` en lo que no cambia datos |
| `created_at` | cuándo |

**El detalle nunca guarda secretos.** `clsDetalleAuditoria` descarta las claves de contraseña, PIN, token y secreto (`password`, `contrasena`, `pin`, `token`, `secret`, `clave`…) a cualquier profundidad; solo se conserva `debe_cambiar_pin`, que es un indicador y no un secreto.

## Qué se audita

Inicio de sesión, PIN errado y bloqueo (`auth.*`); usuarios (`usuario.*`); catálogos (`catalogo.{nombre}.*`); parámetros (`parametro.actualizar`, con el valor antes y después); vehículos (`vehiculos.cargar`, `vehiculo.baja`); viajes (`viaje.*`, `viajes.cargar`); inspecciones (`inspeccion.crear`,
`inspeccion.cerrar`, `inspeccion.corregir`, `llegada.registrar`); hallazgos y novedades (`hallazgo.crear`, `novedad.crear`, `hallazgo.revision`, `hallazgo.seguimiento`); evidencias; transferencias; notas aclaratorias (`nota.crear`); notificaciones; dispositivos y la ingesta Sofasa. La acción se registra
**en la misma transacción** que el cambio: si el cambio no se guarda, tampoco queda su registro, y viceversa.

## Quién la consulta

La capacidad `ver_auditoria` (de `GET /api/auth/me`): el `admin` ve la de **su cliente** y el `super_admin` la de todos; cualquier otro rol recibe `403`. `GET /api/auditoria` filtra por `usuario_id`, `entidad`, `entidad_id`, `viaje_id`, `accion`, `desde` y `hasta` y pagina con `pagina` y `tamano`
(50 por defecto, máximo 200); `GET /api/auditoria/{id}` trae un registro (`404` si no existe, `403` si es de otro cliente).

## Cómo se agrega una acción nueva

Un caso de uso llama a `IAuditor.Registrar(accion, entidad, entidadId, detalle: new DetalleAuditoria(antes, despues), clienteId: …)` antes de confirmar la unidad de trabajo. Para lo auditado de un viaje, `RegistrarDeViajeAsync` completa el viaje y su cliente.

## Código y pruebas

`clsAuditor`, `clsDetalleAuditoria`, `clsOrigenAuditoria` (IP y dispositivo), `clsAuditoriaService` y `clsAuditoriaController`; migración [`18-auditoria-completa.sql`](../database/cambios-esquema/18-auditoria-completa.sql). Pruebas: `AuditoriaApiTests` y `AuditoriaTests`.
