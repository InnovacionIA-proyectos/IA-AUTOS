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
  [notificaciones.md](notificaciones.md). La novedad de un carro acepta `id_envio` (UUID del celular): un reenvío con el mismo identificador responde `200` con la misma novedad y no duplica la novedad, la foto ni el aviso.
- **Novedad del viaje.** `POST /api/viajes/{id}/novedades-viaje` (conductor del viaje e inspector que participa) guarda lo que le pasa al **viaje** y no a un carro: `tipo` del catálogo `tipos-novedad-viaje` para su rol, `texto` de 1 a 300 caracteres, `fecha` y GPS del celular (opcional: sin GPS queda «sin ubicación»), foto opcional e `id_envio` opcional (un reenvío no duplica). Solo el inspector indica `proceso` (`cargue` o `entrega`) y, en la entrega, la parada (`lugar_id`); el inspector sin ese proceso recibe `403` con el motivo `inspector_sin_ese_proceso`. No toca ningún carro, ninguna inspección ni el estado del viaje: se lee en `novedades_viaje` del detalle del viaje y de `mios`, y avisa (`novedad_viaje`) a los coordinadores de calidad y administradores de cada cliente con carros en el viaje con la urgencia de su tipo. La excepción «No hay inspector» deja un solo aviso por viaje con la lista de los VIN. Ver [CONTRATO.md](../CONTRATO.md).
- **Programación y destino por unidad.** La carga por Excel exige por fila la fecha y hora programada del cargue (sin zona horaria es hora de Colombia, UTC-5), el lugar de cargue y el lugar de destino de esa unidad; ambos lugares se buscan por nombre en el catálogo `lugares` (activos). Las filas de un mismo `codigo_mtrack` deben coincidir en fecha y lugar de cargue, y cada VIN conserva su destino (`tblViajeVehiculos.LugarDestinoId`). `PATCH /api/viajes/{id}` corrige `fecha_programada` y `lugar_cargue_id` solo mientras el viaje está `programado` (en curso, `409`). `POST /api/viajes` no cambia: no recibe estos campos.
- **El punto tiene inspector.** El viaje trae `lugar_cargue_tiene_inspector` y cada entrega por punto, `lugar_tiene_inspector`: si ese lugar tiene al menos un inspector **activo** asignado, con la misma regla (`clsReglaInspectorDelPunto`) con que el servidor decide quién firma la entrega y si el cargue sin inspector se pide con permiso; sin nombres ni ids de inspectores. No cambia el esquema ni hay script de base de datos.
- **El lugar admite cargue sin inspector.** Cada lugar trae `admite_cargue_sin_inspector` (falso en todos los existentes, incluido CEDI Envigado; lo activa el coordinador de calidad con el `PATCH` del catálogo, y admin y super admin como soporte) y el viaje, `lugar_cargue_admite_sin_inspector`. El servidor lo aplica en cada petición: donde no se admite, la salida y la llegada del cargue del conductor con la excepción «No hay inspector» y `POST /api/viajes/{id}/permiso-sin-inspector` responden `409` con el motivo `lugar_no_admite_cargue_sin_inspector`. La llegada del cargue del conductor exige la excepción activa (el permiso concedido donde el punto tiene inspectores; el motivo donde no los tiene). Cada unidad trae además `puede_registrar_ruta` y `motivo_sin_ruta` (la entrega del cargue cerrada y su transferencia con un tipo de cierre), la misma regla de «Reportar novedad» y «Llegué». Ver [CONTRATO.md](../CONTRATO.md).
- **Carga sin código del viaje ni unidad.** Si el archivo no trae `codigo_mtrack`, las filas con la misma placa y fecha programada forman un solo viaje y su código se genera (`AUTO-{PLACA}-{AAAAMMDDHHmm}`, estable: cargar dos veces el mismo archivo no duplica viajes); si no trae `unidad`, el viaje toma la niñera (`ninera`). La regla de agrupación es una propuesta pendiente de confirmar por el negocio (DEC-003) y vive en `clsCargaViajesService`. No cambia el esquema: no hay script de base de datos.
- **El viaje nace con fecha, lugar y tipo de origen.** `POST /api/viajes` exige `fecha_programada` (ISO 8601 con zona horaria) y `lugar_cargue_id` (lugar activo del catálogo `lugares`) y ya no recibe `origen_id`: el tipo de origen del viaje es el tipo (`puntos-custodia`) del lugar de cargue, tanto al crear por la API como por la carga por archivo, y se recalcula al corregir `lugar_cargue_id`; el Cargue de cada unidad se crea en ese tipo, así que el tablero no lo cuenta bajo «SIN PUNTO». Crear rechaza con `400` «Campo no admitido» todo campo que no declara (`origen_id`, `destino_id`, `vines`, `con_personal_apoyo`...), igual que corregir. Los viajes anteriores, sin fecha ni lugar, se siguen leyendo y corrigiendo.
- **El listado trae los destinos.** Cada fila de `GET /api/viajes` trae `destinos` (los destinos distintos de las unidades con cuántas van a cada uno) y el filtro `destino_unidad_id` busca por el lugar de destino de las unidades (un viaje cuenta una sola vez); `destino_id` es el tipo de destino del viaje, un dato de los viajes anteriores.
- **Destino por unidad y entrega por punto.** El viaje ya no tiene destino único: cada unidad entra con su `lugar_destino_id` (catálogo `lugares`; sin él, `400` «La unidad requiere un destino») y su Descargue se crea en el tipo de ese lugar. `PATCH /api/viajes/{id}/vehiculos/{vin}` cambia el destino de UNA unidad y solo mueve su Descargue; `PATCH /api/viajes/{id}` ya no acepta `destino_id`. La entrega se firma por punto (`/api/viajes/{id}/entregas`, ver [transferencias.md](transferencias.md)) y el viaje se finaliza cuando todas sus unidades activas tienen el Descargue cerrado. `tblViajes.DestinoId` queda solo como dato histórico.
- **Fuera de este servicio:** la integración con MTrack (el código se digita o llega en el Excel) y la visibilidad por cliente del resto de recursos (vehículos, inspecciones, órdenes).

## `Completa` e indices de los listados

`tblViajeVehiculos.Completa` (bit, 0 por defecto) guarda si el VIN ya completo su ruta en ese viaje, con **la misma regla de `vwViajesCompletos`** (que sigue siendo la definicion de referencia): todas las etapas del VIN en el viaje, y al menos una, con
informe cerrado, y la unidad no esta marcada `no_sale`. Asi un listado de miles de unidades lee una columna en lugar de calcular la vista. No es una columna calculada de SQL Server (no admite consultas a otras tablas): la mantienen tres triggers.

| Cambia | Trigger que recalcula la fila |
|---|---|
| un informe se crea, se cierra o se borra | `TR_tblInspecciones_Completa` |
| una etapa se agrega o se quita | `TR_tblEtapas_Completa` |
| un VIN entra al viaje o cambia el estado de su unidad (`activa` / `no_sale` / `bajado`) | `TR_tblViajeVehiculos_Completa` |

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
**Reclamar y liberar** (`POST /api/viajes/{id}/reclamos` y `DELETE /api/viajes/{id}/reclamos/{proceso}`, ver [CONTRATO.md](../CONTRATO.md#reclamo-del-camión)): el cargue se reclama si está programado para hoy en un punto del inspector; la entrega se reclama **por parada** (`lugar_id`): una parada del viaje que sea punto suyo y tenga carros por entregar. Hay un solo reclamo activo por viaje, proceso y parada (`UX_tblReclamos_Activo` incluye `LugarId`; `CK_tblReclamos_Parada` exige la parada en toda entrega activa). Reclamar llena `tblViajes.InspectorId` (en el cargue) y
deja el reclamo activo; dos reclamos simultáneos los resuelve `UX_tblReclamos_Activo` (el segundo recibe 409 «Ya fue tomado por otro inspector»). Liberar vacía el inspector y no toca lo capturado; no hay caducidad automática. `PATCH /api/viajes/{id}` ya no acepta `inspector_id` (corrección administrativa pendiente de la decisión de negocio PAB-002; al crear el viaje sigue admitiéndose).
**«Mis viajes» y el papel del inspector.** `GET /api/viajes/mios` trae, para un inspector, además de los viajes donde es el inspector del viaje o el conductor, **los viajes donde tiene un reclamo activo de entrega** (de cualquier parada, una sola vez por viaje); liberar el último reclamo de entrega lo saca de la lista salvo que sea también el inspector del viaje. Cada viaje de `mios` y el detalle traen `papel` (`carga`, `recibe` o `carga_y_recibe`; nulo para el conductor y los roles de oficina), que sale del inspector del viaje y de los reclamos activos de quien consulta. No hay cambio de esquema.
**Pendientes, visibilidad y aviso.** `GET /api/viajes/pendientes` lista los cargues de hoy de los puntos del inspector y las entregas en curso que llegan a ellos, con su estado (`disponible`, `en_revision` con el nombre del otro inspector, `mio`). El inspector ve (no actúa sobre) los pendientes de sus puntos
además de los suyos y los que reclamó; el conductor no cambia. Un camión reclamado que lleva `reclamo_sin_actividad_minutos` sin inspecciones avisa una vez al coordinador (tipo `reclamo_sin_actividad`, script [`27-aviso-reclamo-sin-actividad.sql`](../database/cambios-esquema/27-aviso-reclamo-sin-actividad.sql)); el reclamo no se libera. Ver [alertas.md](alertas.md).

## La entrega por parada y el rechazo (script 46)

El script [`46-entrega-por-parada-rechazo-y-motivos-del-cierre.sql`](../database/cambios-esquema/46-entrega-por-parada-rechazo-y-motivos-del-cierre.sql) (ajustando `@esquema`, en una sola ejecución, repetible y **antes** de desplegar la API) hace tres cosas: (1) `tblReclamos.LugarId` (la parada del reclamo de la entrega): cada reclamo de entrega **activo** que ya existía (uno por viaje)
queda con una parada, la de cada destino del viaje que sea punto de su inspector (si el viaje tiene varias, el reclamo se copia una vez por parada), la primera parada del viaje si ninguna lo es y **se libera** si el viaje no tiene destinos; el índice único de reclamos activos pasa a ser por viaje, proceso y parada y `CK_tblReclamos_Parada` exige la parada; (2) `tblInspecciones.Ciclo` (1 por defecto)
y el índice de una inspección original por etapa, momento y ciclo, para conservar la revisión de un carro rechazado como evidencia y revisarlo de nuevo en su destino nuevo; (3) `tblTransferenciasCustodia`: `MotivoCodigo`, `TestigoNombre`, `TestigoCedula` (siempre juntos), `RechazoHallazgoId`, `RechazoMarcadoPor` y el tipo de cierre `rechazado`; además los tres motivos codificados del cierre con una sola firma
en `tblMotivosSinInspector` (**inactivos**) y el tipo de aviso `carro_rechazado_en_destino`. No cambia ningún otro dato. `ddl.sql` lo recibe en bloques protegidos (cada paso es su propio lote) y `verificar-ddl.sql` lo comprueba.

## La espera del inspector del punto (script 49)

El script [`49-espera-del-inspector-del-punto.sql`](../database/cambios-esquema/49-espera-del-inspector-del-punto.sql) (ajustando `@esquema`, en una sola ejecución, repetible y **antes** de desplegar la API) crea `tblEsperasInspectorPunto`: la espera que el conductor activa en una parada con inspectores donde nadie tomó la entrega (`POST /api/viajes/{id}/entregas/{lugar}/sin-inspector`). Guarda el viaje, la parada, el conductor, el motivo
(`tblMotivosSinInspector`), la hora y el GPS del celular (`ActivadaAt`, que `CK_tblEsperasInspectorPunto_Activacion` obliga a no ser posterior a `CreatedAt`, la hora en que llegó al servidor), cuándo se avisó al coordinador (`AvisoAt`) y cómo terminó: `esperando`, `inspector_llego` (con `InspectorId` e `InspectorLlegoAt`), `cerrada_sin_inspector_del_punto` (con `CerradaAt` y `CerradaPor`) o `anulada`; las restricciones
`CK_tblEsperasInspectorPunto_Llegada` y `_Cierre` exigen que cada estado traiga sus datos y `UX_tblEsperasInspectorPunto_Abierta` deja **una sola espera abierta por viaje y parada** (las terminadas quedan de historial). Los plazos (`espera_inspector_aviso_minutos` y `espera_inspector_plan_b_minutos`) no se guardan: se calculan con los parámetros vigentes en cada petición.
El estado es el testigo de concurrencia: si un inspector toma la entrega y el conductor cierra con el plan B a la vez, gana uno solo. El mismo script agrega `tblTransferenciasCustodia.SinInspectorDelPunto` (0 por defecto: los carros que ya existían quedan sin marca) y el tipo de aviso `espera_inspector_punto` a `CK_tblNotificaciones_Tipo`. No modifica ni borra ningún dato. Las reglas están en [CONTRATO.md](../CONTRATO.md#la-espera-del-inspector-del-punto).

## Posición de la unidad en la niñera (script 29)

`tblViajeVehiculos.Posicion` (`tinyint`, nula) es la posición de la unidad dentro de la niñera, de **1 a la capacidad del viaje** (hasta 20; 7 si el viaje no la tiene definida). La registra la **segunda inspección del cargue** (la de momento `salida` de la etapa Cargue) y se libera al marcar la unidad «no sale» (lo hace la aplicación). La base garantiza el rango
(`CK_tblViajeVehiculos_Posicion`) y que dos unidades de un mismo viaje no compartan posición (`UX_tblViajeVehiculos_Posicion`, índice único filtrado sobre `(ViajeId, Posicion)` para las filas con posición); la misma posición en viajes distintos es válida y las unidades sin posición no cuentan. El script
[`29-posicion-de-la-unidad.sql`](../database/cambios-esquema/29-posicion-de-la-unidad.sql) (una transacción, repetible, `@esquema` ajustable) no cambia ningún dato: todas las unidades quedan sin posición.

**Registro y cierre de la acomodación.** `PUT /api/viajes/{id}/vehiculos/{vin}/posicion` con `{ "posicion": 1..7 }` (nulo la libera) lo hacen el inspector del viaje (o quien reclamó el cargue), el coordinador de calidad y los administradores, y solo mientras el viaje tiene su segunda inspección del cargue en borrador
(409 si no). Fuera de 1 a la capacidad del viaje responde `400` «La niñera tiene N puestos: la posición debe estar entre 1 y N»; una posición que ya tiene otra unidad del viaje, `409` «La posición N ya está ocupada por otra unidad de este viaje» (con varias solicitudes simultáneas por la misma gana exactamente una: lo resuelve el índice único); una unidad
que no sale no admite posición (409). La respuesta es el viaje, cuyas unidades traen `posicion`. Cada cambio queda auditado (`viaje.posicion_unidad`). El informe PDF de la segunda inspección del cargue lista cada unidad con su posición y trae las fotos de la acomodación con su sello, y el inventario del VIN (JSON y PDF) muestra «Posición en niñera: N» en las etapas de los viajes en que fue despachado con posición; un viaje sin posiciones genera ambos documentos igual, sin el dato. El cierre de la segunda inspección del cargue exige la posición de **cada unidad activa** y las fotos de la acomodación (ver [inspecciones.md](inspecciones.md)).

## El detalle del viaje trae lo que el portal necesita (sin cambios de esquema)

Estos campos se calculan al leer; ninguno cambia la base ni pide un script. `GET /api/viajes/{id}` —y solo el detalle— trae:

- **`cargue_ninera`**: el cargue de la niñera completo (carros recibidos, reservas por carro, las dos firmas con su nombre, hora y trazo, y la constancia de no firma con su testigo, su foto con enlace firmado y el momento). Sale del informe **vigente** de cada carro (no de los reemplazados por una corrección cerrada, ni de los descartados o superados), reunido en una sola consulta del viaje, sin una petición por carro. Nunca trae la dirección de red de las firmas ni una cédula. Nulo si el cargue no se firmó ni tiene constancia.
- **`permiso_sin_inspector_estado`**: `pendiente` o `concedido` si el viaje tiene solicitud de «No hay inspector», nulo si no. Lee el estado guardado: no concede por tiempo (eso lo sigue haciendo la consulta del permiso y el proceso periódico).

`GET /api/viajes/mios`, el listado y las respuestas de las operaciones sobre el viaje llevan los dos campos en `null`. Además, la etapa del inventario de un carro bajado trae `bajado` (con la forma del `bajado` de cada carro del viaje) y la entrega por parada trae `rechazo.foto` y `fotos_cierre` con el enlace firmado y el sello de cada foto. Detalle en `CONTRATO.md`.

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

## Capacidad y disposición de la niñera (script 38)

`tblViajes.Capacidad` (`tinyint`, nula, 1 a 20 con `CK_tblViajes_Capacidad`) son los puestos de la niñera de ese viaje: la toma de `tblTiposUnidadTransportadora.Puestos` (nula = sin definir; el viaje nuevo toma 7) al crearse o la que se indique, y no se mueve si el tipo cambia sus puestos. Un viaje sin capacidad guardada rige como de 7 (no se migra).
`CK_tblViajeVehiculos_Posicion` pasa a 1..20 y la posición se valida en la aplicación contra la capacidad del viaje. La capacidad no baja por debajo de las unidades activas ni de la posición más alta usada (409 con la causa). `tblEvidencias.EsAcomodacion` (`bit`, 0 por defecto; `CK_tblEvidencias_Acomodacion`) marca la foto de la acomodación, que sale en `fotos_acomodacion` del detalle del viaje.
El script [`38-capacidad-de-la-ninera.sql`](../database/cambios-esquema/38-capacidad-de-la-ninera.sql) (ajustando `@esquema`, en una sola ejecución y **antes** de desplegar la API; idempotente y sin cambiar ningún dato) lo aplica; `ddl.sql` ya lo trae y `tests/verificar-ddl.sql` lo cubre. Los endpoints y los cuerpos están en [CONTRATO.md](../CONTRATO.md).

## Puntos del inspector y permiso de la excepción «No hay inspector» (script 39)

Los puntos de un inspector (`tblInspectorLugares`) se administran por la API (`GET|POST /api/usuarios/{id}/puntos`, `DELETE …/puntos/{lugar}`) y cada asignación o retiro queda en la auditoría. `tblPermisosSinInspector` guarda la solicitud del conductor cuando el punto de cargue del viaje **tiene inspectores** (una por viaje, `UQ_tblPermisosSinInspector_Viaje`):
motivo, texto, y la fecha y el GPS del momento en que la pidió. Se concede por un coordinador o administrador (`ConcedidoPor`) o, sin respuesta dentro de su plazo, por el reloj (`PorTiempo = 1`). El plazo (`VenceAt`) se guarda en la solicitud al pedirla con el parámetro `permiso_sin_inspector_minutos` vigente (10 por defecto): `CK_tblPermisosSinInspector_Reloj` impide concederla antes de `VenceAt`, `CK_tblPermisosSinInspector_Plazo` exige que no sea anterior a la solicitud y `CK_tblPermisosSinInspector_Concesion` exige quién o el reloj. `tblNovedadesProceso.PermisoId` liga la novedad con el permiso (nulo si el punto no tenía inspectores) y
`tblNotificaciones` admite el tipo `permiso_sin_inspector`. El proceso periódico de alertas concede por tiempo cada minuto y, además, la concede dentro de la petición que la necesita (crear la salida del Cargue con la excepción o registrar la posición de una unidad) quien tenga una solicitud pendiente con el plazo cumplido (`clsPermisosSinInspectorService.ConcederSiVencidoAsync`). El script [`48-parametros-de-avisos-y-tiempos.sql`](../database/cambios-esquema/48-parametros-de-avisos-y-tiempos.sql) crea `VenceAt` (los permisos que ya existían reciben su fecha más 10 minutos) y reemplaza `CK_tblPermisosSinInspector_Reloj`; `ddl.sql` ya lo trae. El script [`39-permiso-sin-inspector.sql`](../database/cambios-esquema/39-permiso-sin-inspector.sql) (ajustando `@esquema`, en una sola ejecución y **antes** de desplegar la API; idempotente y sin cambiar ningún dato) lo aplica; `ddl.sql` ya lo trae. Los endpoints están en [CONTRATO.md](../CONTRATO.md).
