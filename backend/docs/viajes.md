# El viaje como eje: modelo, migración y reglas

Un **viaje** agrupa lo que antes estaba suelto: una unidad transportadora (tipo, placa, tráiler) lleva uno o varios VIN con un conductor y, si hay, un
inspector, entre un origen y un destino. Cada etapa e inspección cuelga de un viaje, y inspectores y conductores solo ven lo de sus viajes. Los endpoints
están en [CONTRATO.md](../CONTRATO.md#viajes).

## Tablas

| Tabla | Contenido |
|---|---|
| `tblViajes` | El viaje: `CodigoMtrack` (único entre los que lo tienen), `TipoUnidadId` (catálogo `tipos-unidad`), `Placa`, `Trailer`, `ConductorId`, `InspectorId`, `OrigenId` y `DestinoId` (catálogo `puntos-custodia`), `Estado` y `CreadoPor` |
| `tblViajeVehiculos` | Los VIN de un viaje (único por viaje y vehículo). Un VIN puede estar en varios viajes con el tiempo; la aplicación no admite dos viajes **activos** con el mismo VIN |
| `tblEtapas` | Gana `ViajeId` (obligatorio) y `ConPersonalApoyo` (solo en el Cargue). La unicidad pasa a (viaje, vehículo, orden) |
| `tblInspecciones` | Gana `ViajeId` (obligatorio): el viaje de la etapa. `ConductorNombre`, `Placa` y `Trailer` solo conservan lo capturado antes de los viajes |
| `tblUsuarios` | Gana `Cedula` (única entre quienes la tienen): identifica al conductor en la carga por Excel |
| `tblOrdenesSofasa` | Gana `ViajeId` y `CodigoMtrack`: el viaje al que pertenece la orden |
| `vwViajesCompletos` | Reemplaza a `vwVehiculosMovilizados`: una fila por VIN y viaje en el que **todas** las etapas del VIN tienen informe cerrado. Un vehículo está movilizado si tiene al menos una fila |
| `tblAsignaciones` | Se retira: sus datos pasan a viajes |

Estados de un viaje: `programado` → `en_curso` → `finalizado`, o `cancelado` (desde `programado` o `en_curso`). La primera inspección de un viaje
programado lo pasa a `en_curso`. Un viaje cancelado deja de ser visible para inspectores y conductores.

## Reglas

- **Visibilidad.** `inspector` y `conductor` ven solo los VIN, viajes, informes y órdenes Sofasa de los viajes en los que son el conductor o el inspector (y que no
  están cancelados). Un VIN ajeno responde `403`. Coordinador, comercial y administradores ven todo.
- **Etapas.** Nacen al agregar un VIN a un viaje, una por cada valor activo del catálogo `etapas`: el Cargue en el origen del viaje, el Descargue en su destino, el
  Transporte sin lugar. La carga masiva de VIN y la ingesta Sofasa ya **no** crean etapas.
- **Inspección.** Toma del viaje de su etapa la placa, el tráiler y el conductor (ya no se piden). No se crea en un viaje finalizado o cancelado ni en uno ajeno.
- **Sofasa.** La ingesta vincula la orden al viaje del VIN (y del `codigo_mtrack`, si la orden lo trae). Si el VIN entra a un viaje después, la orden se vincula al
  agregarlo o en la siguiente ingesta.
- **Clientes del viaje.** El viaje no tiene un cliente propio (puede llevar VIN de varias marcas): su cliente se toma de sus VIN. `GET /api/viajes?clienteId=` trae los viajes con
  al menos un VIN de ese cliente, y el listado y el detalle traen `clientes`. Quien no es super administrador, conductor ni inspector del viaje ve solo los viajes y los VIN de su
  cliente; el conductor y el inspector del viaje ven todos sus VIN. No se agregó `ClienteId` a `tblViajes`: los indicadores por cliente o marca se cuentan por vehículo.
- **Fuera de este servicio:** la integración con MTrack (el código se digita o llega en el Excel) y la visibilidad por cliente del resto de recursos (vehículos, inspecciones, órdenes).

## Mapeo del modelo anterior

| Antes | Ahora |
|---|---|
| Una asignación por VIN (`Ninera` = placa, conductor) | Un viaje con ese VIN, con la placa y el conductor de la asignación y la unidad **Niñera** |
| Varios VIN con la misma placa y conductor | **El mismo viaje** (vehículos con la misma placa, el mismo conductor y el mismo día de asignación) |
| Varias asignaciones de un VIN | Se toma la **más reciente**; la anterior deja de existir como dato suelto |
| Informes de un VIN sin asignación | Un viaje propio por VIN, con la placa de su primer informe |
| Quién hizo los informes | El inspector (o conductor) que hizo informes de un viaje queda como su inspector (o conductor) si el viaje no tenía uno: no pierde el acceso |
| Etapas de un VIN sin asignación ni informes | Se borran (eran un esqueleto sin información); nacen de nuevo al agregar el VIN a un viaje |
| `POST /api/asignaciones`, `POST /api/etapas` | Retirados: `POST /api/viajes` y `POST /api/viajes/{id}/vehiculos` |
| `proceso`, lugares, `vwVehiculosMovilizados` | Sin cambio de significado; la vista pasa a `vwViajesCompletos` |

Los viajes migrados quedan con estado `finalizado` (todas las etapas de sus VIN con informe cerrado), `en_curso` (algún informe) o `programado` (ninguno), y
pueden no tener `codigo_mtrack`, `tipo_unidad_id` ni `placa` (el código se completa después con `PATCH /api/viajes/{id}`).

## Aplicar el cambio en una base existente

Con un usuario que pueda crear tablas y vistas y desactivar triggers (no el de la aplicación), conectado a la base de IA Autos, **antes de desplegar la API**:

1. Ejecutar [`database/cambios-esquema/05-viajes.sql`](../database/cambios-esquema/05-viajes.sql) ajustando `@esquema` (`dbo`, o `db_IA_AUTOS` en la base compartida de
   pruebas). Es una sola transacción: si algo falla, no cambia nada. Se puede repetir (si ya está aplicado, no hace nada). Aborta sin cambiar nada si falta el tipo de
   unidad Niñera, si alguna placa de asignación tiene más de 50 caracteres o si algún vehículo con asignación no quedara en un viaje.
2. Dar permisos al usuario de la aplicación sobre las tablas nuevas si no los hereda por esquema (`GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::<esquema> TO <usuario>`).
3. Comprobar el resultado que muestra el script: `etapas sin viaje` e `inspecciones sin viaje` deben ser 0.

El trigger de inmutabilidad de las inspecciones se desactiva **solo** mientras se llena `tblInspecciones.ViajeId` (ningún otro dato de un informe cerrado cambia) y se
vuelve a activar dentro de la misma transacción.

Una base nueva se crea con `ddl.sql`, que ya trae el esquema. `tests/verificar-ddl.sql` comprueba las restricciones de los viajes, la vista y la cascada.
