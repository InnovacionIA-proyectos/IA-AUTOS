# Visibilidad por cliente

Cada usuario de un cliente ve **solo las unidades de su cliente** y lo que cuelga de ellas. El cliente de una unidad es el de su vehículo (`tblVehiculos.ClienteId`); el del usuario sale de su sesión (`cliente` en `GET /api/auth/me`). La tabla completa de endpoints está en [CONTRATO.md](../CONTRATO.md) («Visibilidad por
cliente»).

## Quién ve qué

| Rol | Alcance |
|---|---|
| `coordinador_calidad`, `comercial`, `admin` | lo de **su cliente**; un recurso de otro cliente responde `403` |
| `super_admin` | **todos** los clientes; es el único que puede elegir uno con `cliente_id` |
| `inspector`, `conductor` | no se limitan por cliente sino por **viaje**: ven lo de los viajes en los que participan |

Un usuario de un cliente **sin cliente asignado** no ve ninguna unidad.

## Cómo se aplica

- **Listados.** Vehículos, inspecciones, viajes, órdenes Sofasa, usuarios, hallazgos, notificaciones, auditoría, reportes y exportaciones filtran por el cliente de quien consulta; el total y la paginación cuentan solo esas filas.
- **Un recurso por id** de otro cliente responde `403` (no `404`): el inventario de un VIN, un informe, su PDF, la constancia de transferencia…
- **`cliente_id`.** Quien se limita a su cliente puede enviar el suyo (no cambia nada); enviar otro responde `403`. Solo el `super_admin` ve un cliente a elección o todos.
- **Alertas y notificaciones.** Las alertas preventivas llegan solo a los `coordinador_calidad` y `admin` activos del cliente de la unidad, nunca de otro.
- **Viaje con unidades de dos clientes.** El usuario de un cliente ve en el detalle solo los VIN de su cliente (`vehiculos`, `clientes`, `total_vehiculos` y `vehiculos_completos` se calculan sobre ellos); el conductor y el inspector del viaje ven todos (propuesta pendiente de confirmar por el negocio).
  Un viaje **sin VIN** lo ven los de su cliente si lo creó alguien de su cliente, y el super administrador.
- **Auditoría.** Cada registro lleva el `cliente_id` de lo auditado: el `admin` consulta solo el suyo.

## En el código

`clsAlcanceCliente` (`ClienteRestringido` y `ClienteDelFiltro`) decide el alcance de cada servicio y las consultas lo aplican; `clsAlcanceViajes` resuelve los viajes y VIN de un inspector o conductor. Todo endpoint de lectura nuevo debe usarlos. Pruebas: `VisibilidadClienteApiTests`,
`ListadosPaginadosApiTests` y las de cada módulo.
