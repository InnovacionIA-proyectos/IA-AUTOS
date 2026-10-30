# Indicadores del tablero

Los reportes del tablero (`GET /api/reportes/*`, ver [CONTRATO.md](../CONTRATO.md) «Reportes») leen cinco vistas de la base de datos (salvo `novedades-proceso`, que cuenta directamente la tabla `tblNovedadesProceso`: no son hallazgos y no entran en estas fórmulas). Cada una calcula su formula sobre las **inspecciones cerradas**, toma el **cliente del vehiculo**
(no el del viaje) y expresa los dias en **hora de Colombia** (UTC-5, sin horario de verano). Las crea `database/cambios-esquema/20-vistas-de-indicadores.sql` (y `ddl.sql`); solo crean vistas, no cambian ningun dato.

## Vistas

| Vista | Una fila por | Formula |
|---|---|---|
| `vwCobertura` | unidad despachada | VIN de un viaje con la **salida del Cargue cerrada**, sin las unidades `no_sale`. `RegistrosCerrados` (0 a 6) cuenta las parejas etapa + momento (salida y llegada de cada etapa) con una inspeccion cerrada; `Completa = 1` solo con los 6. `FechaDespacho` es el primer cierre de la salida del Cargue y `Fecha` su dia en Colombia |
| `vwAdopcion` | dia, cliente, operacion, origen, destino, marca y trailer | `Programados` = VIN de los viajes no cancelados cuyo **dia programado** es ese (la fecha programada en hora de Colombia y, si el viaje no la tiene, el dia de su creacion); `ConSalidaDigital` = de esos, los que tienen una inspeccion de salida en el sistema (borrador o cerrada); `Porcentaje`. **Formula propuesta, pendiente de confirmar por Calidad.** El dia de un viaje es el de su creacion porque `tblViajes` aun no guarda la fecha programada |
| `vwTiempoCaptura` | inspeccion cerrada de Cargue o Descargue | `Minutos` = cierre - inicio (las de Transporte no entran; tampoco las que tienen el cierre antes del inicio) |
| `vwAveriasPorConfiguracion` | hallazgo revisado de una inspeccion cerrada | con placa, trailer, tipo de unidad, conductor (nombre y cedula), VIN, marca, operacion, cliente, clase, tamano, nivel, proceso, momento y zona (lado + numero), su pieza y su codigo AIAG. Excluye las novedades del conductor aun sin revisar |
| `vwIndicadorDiario` | dia, cliente, operacion, origen, destino, marca y trailer | `Despachados` = las unidades de `vwCobertura` despachadas ese dia; `ConNovedad` = de esas mismas unidades, las que tienen al menos un hallazgo de nivel **V1** o **V1+** de una inspeccion cerrada ese dia (V1+ cuenta como propuesta, pendiente de confirmar por el negocio); `Porcentaje` |

`vwAdopcion` y `vwIndicadorDiario` guardan una fila por cada combinacion de las columnas de filtro (para recalcular el reporte con los filtros del tablero): el total de un dia es la **suma** de sus filas y el porcentaje se recalcula
con esas sumas. `vwPatronesPorCuadrante` (la de los mapas de calor anteriores) no cambia.

## Aplicar el cambio en una base existente

Con un usuario que pueda crear vistas (no el de la aplicacion), conectado a la base de IA Autos, **antes de desplegar la API**:
[`database/cambios-esquema/20-vistas-de-indicadores.sql`](../database/cambios-esquema/20-vistas-de-indicadores.sql). Exige que `10-zona-dano-y-hallazgo.sql`, `13-acciones-niveles-severidad.sql` y `14-sello-y-unidad-no-sale.sql` ya esten aplicados.
Ajusta `@esquema` (`dbo`, o `db_IA_AUTOS` en la base compartida de pruebas), se ejecuta completo en **una sola ejecucion de script**, es una sola transaccion y se puede repetir. Resultado esperado: 5 vistas.

## Reporte diario por correo

El proceso `clsProcesoReporteDiario` despierta cada minuto y le pregunta a `clsReporteDiarioService` si toca: desde la hora `reporte_diario_hora` (hora de Colombia) calcula **el dia anterior** con los mismos servicios que el tablero
(`clsReportesTableroService.IndicadorDiarioAsync` y `TiempoDeCapturaAsync`), asi que el correo y `GET /api/reportes/indicador-diario?fecha=` dan los mismos valores. El correo (`clsCorreoReporteDiario`) lleva el indicador, las filas por operacion
y por cliente y la mediana de captura en tablas HTML, y adjunta `hallazgos-{dia}.xlsx` (`clsExportacionesService.ExportarHallazgosAsync` con el dia de Colombia completo). **No incluye «rechazos del dia».** «Frente al objetivo» dice `Dentro` cuando el porcentaje
es igual o menor a `objetivo_diario_novedad` y `Por encima` si lo supera (se asume que el objetivo es un techo de novedad; pendiente de confirmar por el negocio).

Cada ejecucion que actua queda en `tblNotificaciones` (`reporte_diario`, canal `correo`, sin destinatario): `EnviadaAt` si salio, `ErrorEnvio` si no. Una notificacion no se modifica, asi que:

| Situacion | Fila | Clave de dedupe | El dia queda |
|---|---|---|---|
| Correo enviado | `EnviadaAt` | `reporte_diario:{dia}` | cerrado |
| Sin destinatarios | `ErrorEnvio` = «No se envió: ...» | `reporte_diario:{dia}` | cerrado (no se manda atrasado) |
| Envio fallido | `ErrorEnvio` con el motivo | `reporte_diario:{dia}:intento-{n}` | abierto; reintenta cada `ReporteDiario__ReintentoCadaMinutos` hasta `ReporteDiario__IntentosMaximos` (el ultimo intento lo cierra) |

El envio lo hace `clsEnviadorMailSender` con el servicio interno MailSender API (`POST /api/v1/emails` en formato multipart: `From`, un `To` por destinatario, `Subject`, `Body` en HTML, `IsHtml`, `Attachments` y `X-Correlation-ID` = `ia-autos-reporte-diario-{dia}-intento-{n}`; configurado con `Correo__Url` y `Correo__Remitente`). El servicio responde 202 al encolar y entrega el correo al relay por su cuenta, asi que despues se consulta `GET /api/v1/emails/{id}` hasta que quede enviado (estado 3, hasta `Correo__EsperaConfirmacionSegundos`); un correo aceptado que no se alcanza a confirmar se da por enviado (la cola es durable y reenviarlo lo duplicaria) y uno que sigue con error al terminar la espera queda como fallido. «Enviado» significa que el relay lo acepto, no que llego al buzon. Si dos instancias corren a la vez la clave unica impide registrar el mismo reporte dos veces (el segundo correo podria salir: conviene apagar el proceso en las instancias
que solo atienden peticiones con `ReporteDiario__ProcesoActivo=false`). Si el proceso arranca despues de la hora del dia (por ejemplo, tras un despliegue a media manana) envia en ese momento el reporte de ayer, si no estaba registrado.
