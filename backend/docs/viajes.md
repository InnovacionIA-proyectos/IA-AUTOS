# El viaje como eje: modelo, migración y reglas

Un **viaje** agrupa lo que antes estaba suelto: una unidad transportadora (tipo, placa, tráiler) lleva uno o varios VIN con un conductor y, si hay, un
inspector, entre un origen y un destino. Cada etapa e inspección cuelga de un viaje, y inspectores y conductores solo ven lo de sus viajes. Los endpoints
están en [CONTRATO.md](../CONTRATO.md#viajes).

## Tablas

| Tabla | Contenido |
|---|---|
| `tblViajes` | El viaje: `CodigoMtrack` (único entre los que lo tienen), `TipoUnidadId` (catálogo `tipos-unidad`), `Placa`, `Trailer`, `ConductorId`, `InspectorId`, `OrigenId` y `DestinoId` (catálogo `puntos-custodia`), `Estado` y `CreadoPor` |
| `tblViajes` (programación) | `FechaProgramada` (cuándo está programado el cargue, con zona horaria; nulo) y `LugarCargueId` (llave foránea al lugar real de cargue de `tblLugares`; nulo). Los viajes anteriores quedan con nulos: no se migra nada |
| `tblViajeVehiculos` | Los VIN de un viaje (único por viaje y vehículo); `LugarDestinoId` es el destino de **cada** unidad (lugar real; nulo en lo anterior). Un VIN puede estar en varios viajes con el tiempo; la aplicación no admite dos viajes **activos** con el mismo VIN |
| `tblEtapas` | Gana `ViajeId` (obligatorio). La unicidad pasa a (viaje, vehículo, orden). La marca `ConPersonalApoyo` que tuvo en el Cargue se eliminó (script 28): el Cargue no lleva ninguna marca previa |
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
- **Clientes del viaje.** El viaje no tiene un cliente propio (puede llevar VIN de varias marcas): su cliente se toma de sus VIN. `GET /api/viajes?cliente_id=` trae los viajes con
  al menos un VIN de ese cliente, y el listado y el detalle traen `clientes`. Quien no es super administrador, conductor ni inspector del viaje ve solo los viajes y los VIN de su
  cliente; el conductor y el inspector del viaje ven todos sus VIN. No se agregó `ClienteId` a `tblViajes`: los indicadores por cliente o marca se cuentan por vehículo.
- **Conductor en ruta.** El conductor del viaje registra las novedades de cada unidad (`POST /api/viajes/{id}/vehiculos/{vin}/novedades`) y confirma su llegada (`.../llegada`) sobre la etapa de Transporte del VIN; cada VIN del viaje trae su
  `estado_transporte` (`sin_registro`, `con_novedad`, `sin_novedad` o `llegada_con_novedad`) y sus `novedades`. Una unidad marcada «no sale» no admite ninguna de las dos (`409 UNIDAD_NO_SALE`). Ver [CONTRATO.md](../CONTRATO.md) («Conductor en ruta») y
  [notificaciones.md](notificaciones.md).
- **Programación y destino por unidad.** La carga por Excel exige por fila la fecha y hora programada del cargue (sin zona horaria es hora de Colombia, UTC-5), el lugar de cargue y el lugar de destino de esa unidad; ambos lugares se buscan por nombre en el catálogo `lugares` (activos). Las filas de un mismo `codigo_mtrack` deben coincidir en fecha y lugar de cargue, y cada VIN conserva su destino (`tblViajeVehiculos.LugarDestinoId`). `PATCH /api/viajes/{id}` corrige `fecha_programada` y `lugar_cargue_id` solo mientras el viaje está `programado` (en curso, `409`). `POST /api/viajes` no cambia: no recibe estos campos.
- **Destino por unidad y entrega por punto.** El viaje ya no tiene destino único: cada unidad entra con su `lugar_destino_id` (catálogo `lugares`; sin él, `400` «La unidad requiere un destino») y su Descargue se crea en el tipo de ese lugar. `PATCH /api/viajes/{id}/vehiculos/{vin}` cambia el destino de UNA unidad y solo mueve su Descargue; `PATCH /api/viajes/{id}` ya no acepta `destino_id`. La entrega se firma por punto (`/api/viajes/{id}/entregas`, ver [transferencias.md](transferencias.md)) y el viaje se finaliza cuando todas sus unidades activas tienen el Descargue cerrado. `tblViajes.DestinoId` queda solo como dato histórico.
- **Fuera de este servicio:** la integración con MTrack (el código se digita o llega en el Excel) y la visibilidad por cliente del resto de recursos (vehículos, inspecciones, órdenes).

## `Completa` e indices de los listados

`tblViajeVehiculos.Completa` (bit, 0 por defecto) guarda si el VIN ya completo su ruta en ese viaje, con **la misma regla de `vwViajesCompletos`** (que sigue siendo la definicion de referencia): todas las etapas del VIN en el viaje, y al menos una, con
informe cerrado, y la unidad no esta marcada `no_sale`. Asi un listado de miles de unidades lee una columna en lugar de calcular la vista. No es una columna calculada de SQL Server (no admite consultas a otras tablas): la mantienen tres triggers.

| Cambia | Trigger que recalcula la fila |
|---|---|
| un informe se crea, se cierra o se borra | `TR_tblInspecciones_Completa` |
| una etapa se agrega o se quita | `TR_tblEtapas_Completa` |
| un VIN entra al viaje o cambia el estado de su unidad (`activa` / `no_sale`) | `TR_tblViajeVehiculos_Completa` |

`EXEC dbo.usp_RecalcularCompleta` recalcula todas las filas con la regla de la vista (solo toca las que difieren). **Hay que ejecutarlo despues de cualquier carga o limpieza que apague los triggers** (`DISABLE TRIGGER`).

Indices para los filtros de los listados paginados: `tblVehiculos (ClienteId, Operacion, Marca, CreatedAt)` y `(Activo)`, `tblOrdenesSofasa (StatusCode, DestinationCity, DatetimeEvent)`, `tblInspecciones (ViajeId, Momento, Estado, Fecha)` y
`tblViajeVehiculos (EstadoUnidad)`.

Los crea [`database/cambios-esquema/21-indices-de-filtros-y-completa.sql`](../database/cambios-esquema/21-indices-de-filtros-y-completa.sql) (ajustando `@esquema`, en una sola ejecucion, una sola transaccion, repetible; exige `14-sello-y-unidad-no-sale.sql`).
Resultado esperado: 5 indices, 1 columna, 1 procedimiento, 3 triggers y 0 filas distintas de la vista.

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

## Destino por unidad (script 25)

`tblViajeVehiculos.LugarDestinoId` (script 24) es el destino de **cada unidad**, hacia el catálogo `lugares`; `tblViajes.DestinoId` queda como dato histórico, sin uso nuevo. El script
[`25-destino-por-unidad-y-cierre-con-una-firma.sql`](../database/cambios-esquema/25-destino-por-unidad-y-cierre-con-una-firma.sql) (una transacción, repetible, `@esquema` ajustable) migra los viajes anteriores: como su destino era un **tipo** de lugar
(`tblPuntosCustodia`) y no un lugar real, crea **un lugar histórico inactivo** por cada tipo que algún viaje usaba de destino («Concesionario (por definir)», ciudad «Sin definir») y se lo copia a cada unidad del viaje. Tras migrar,
ninguna unidad de un viaje con destino queda sin lugar de destino y el de cada una corresponde al de su viaje; quien administra el catálogo reasigna después cada unidad a su lugar real. Un viaje sin destino no inventa uno.

## Reclamo del camión por el inspector (script 26)

El inspector reclama el camión que va a revisar. Los datos: `tblInspectorLugares` (los puntos de cada inspector: usuario y lugar del catálogo `lugares`, único por pareja; un inspector puede tener varios) y `tblReclamos` (quién reclamó un `cargue` o una `entrega`
—la etapa Descargue—, cuándo y, al liberarse, quién y cuándo: `LiberadoAt` y `LiberadoPor` van juntos o ninguno). Un índice único filtrado (`UX_tblReclamos_Activo`) impide dos reclamos **activos** (sin liberar) del mismo viaje y proceso, de modo que el
segundo en reclamar falla en la base; los reclamos liberados quedan como historial y no se borran (error 51003). `tblViajes.InspectorId` se conserva: lo llena el reclamo del cargue. El script
[`26-reclamo-inspector.sql`](../database/cambios-esquema/26-reclamo-inspector.sql) (una transacción, repetible, `@esquema` ajustable) también agrega el parámetro `reclamo_sin_actividad_minutos` (número, 30; ver [parametros.md](parametros.md)); no toca ningún dato existente.
**Reclamar y liberar** (`POST /api/viajes/{id}/reclamos` y `DELETE /api/viajes/{id}/reclamos/{proceso}`, ver [CONTRATO.md](../CONTRATO.md#reclamo-del-camión)): el cargue se reclama si está programado para hoy en un punto del inspector; la entrega, si el viaje llega a un punto suyo. Reclamar llena `tblViajes.InspectorId` (en el cargue) y
deja el reclamo activo; dos reclamos simultáneos los resuelve `UX_tblReclamos_Activo` (el segundo recibe 409 «Ya fue tomado por otro inspector»). Liberar vacía el inspector y no toca lo capturado; no hay caducidad automática. `PATCH /api/viajes/{id}` ya no acepta `inspector_id` (corrección administrativa pendiente de la decisión de negocio PAB-002; al crear el viaje sigue admitiéndose).
**Pendientes, visibilidad y aviso.** `GET /api/viajes/pendientes` lista los cargues de hoy de los puntos del inspector y las entregas en curso que llegan a ellos, con su estado (`disponible`, `en_revision` con el nombre del otro inspector, `mio`). El inspector ve (no actúa sobre) los pendientes de sus puntos
además de los suyos y los que reclamó; el conductor no cambia. Un camión reclamado que lleva `reclamo_sin_actividad_minutos` sin inspecciones avisa una vez al coordinador (tipo `reclamo_sin_actividad`, script [`27-aviso-reclamo-sin-actividad.sql`](../database/cambios-esquema/27-aviso-reclamo-sin-actividad.sql)); el reclamo no se libera. Ver [alertas.md](alertas.md).

## Posición de la unidad en la niñera (script 29)

`tblViajeVehiculos.Posicion` (`tinyint`, nula) es la posición de la unidad dentro de la niñera, de **1 a 7**. La registra la **segunda inspección del cargue** (la de momento `salida` de la etapa Cargue) y se libera al marcar la unidad «no sale» (lo hace la aplicación). La base garantiza el rango
(`CK_tblViajeVehiculos_Posicion`) y que dos unidades de un mismo viaje no compartan posición (`UX_tblViajeVehiculos_Posicion`, índice único filtrado sobre `(ViajeId, Posicion)` para las filas con posición); la misma posición en viajes distintos es válida y las unidades sin posición no cuentan. El script
[`29-posicion-de-la-unidad.sql`](../database/cambios-esquema/29-posicion-de-la-unidad.sql) (una transacción, repetible, `@esquema` ajustable) no cambia ningún dato: todas las unidades quedan sin posición.

**Registro y cierre de la acomodación.** `PUT /api/viajes/{id}/vehiculos/{vin}/posicion` con `{ "posicion": 1..7 }` (nulo la libera) lo hacen el inspector del viaje (o quien reclamó el cargue), el coordinador de calidad y los administradores, y solo mientras el viaje tiene su segunda inspección del cargue en borrador
(409 si no). Fuera de 1 a 7 responde `400` «La posición debe estar entre 1 y 7»; una posición que ya tiene otra unidad del viaje, `409` «La posición N ya está ocupada por otra unidad de este viaje» (con varias solicitudes simultáneas por la misma gana exactamente una: lo resuelve el índice único); una unidad
que no sale no admite posición (409). La respuesta es el viaje, cuyas unidades traen `posicion`. Cada cambio queda auditado (`viaje.posicion_unidad`). El informe PDF de la segunda inspección del cargue lista cada unidad con su posición y trae las fotos de la acomodación con su sello, y el inventario del VIN (JSON y PDF) muestra «Posición en niñera: N» en las etapas de los viajes en que fue despachado con posición; un viaje sin posiciones genera ambos documentos igual, sin el dato. El cierre de la segunda inspección del cargue exige la posición de **cada unidad activa** y las fotos de la acomodación (ver [inspecciones.md](inspecciones.md)).

## Aplicar el cambio en una base existente

Con un usuario que pueda crear tablas y vistas y desactivar triggers (no el de la aplicación), conectado a la base de IA Autos, **antes de desplegar la API**:

1. Ejecutar [`database/cambios-esquema/05-viajes.sql`](../database/cambios-esquema/05-viajes.sql) ajustando `@esquema` (`dbo`, o `db_IA_AUTOS` en la base compartida de
   pruebas). Es una sola transacción: si algo falla, no cambia nada. Se puede repetir (si ya está aplicado, no hace nada). Aborta sin cambiar nada si falta el tipo de
   unidad Niñera, si alguna placa de asignación tiene más de 50 caracteres o si algún vehículo con asignación no quedara en un viaje.
2. Dar permisos al usuario de la aplicación sobre las tablas nuevas si no los hereda por esquema (`GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::<esquema> TO <usuario>`).
3. Comprobar el resultado que muestra el script: `etapas sin viaje` e `inspecciones sin viaje` deben ser 0.

El trigger de inmutabilidad de las inspecciones se desactiva **solo** mientras se llena `tblInspecciones.ViajeId` (ningún otro dato de un informe cerrado cambia) y se
vuelve a activar dentro de la misma transacción.

Una base nueva se crea con `ddl.sql`, que ya trae el esquema. `tests/verificar-ddl.sql` comprueba las restricciones de los viajes y la vista. Un viaje sin inspecciones se borra junto con la lista de sus VIN (esa cascada se conserva); el vehiculo, las etapas y las inspecciones no se borran en cascada (ver [evidencia-inmutable.md](evidencia-inmutable.md)).
