# Indicadores del tablero

Los reportes del tablero (`GET /api/reportes/*`, ver [CONTRATO.md](../CONTRATO.md) «Reportes») leen cinco vistas de la base de datos. Cada una calcula su formula sobre las **inspecciones cerradas**, toma el **cliente del vehiculo**
(no el del viaje) y expresa los dias en **hora de Colombia** (UTC-5, sin horario de verano). Las crea `database/cambios-esquema/20-vistas-de-indicadores.sql` (y `ddl.sql`); solo crean vistas, no cambian ningun dato.

## Vistas

| Vista | Una fila por | Formula |
|---|---|---|
| `vwCobertura` | unidad despachada | VIN de un viaje con la **salida del Cargue cerrada**, sin las unidades `no_sale`. `RegistrosCerrados` (0 a 6) cuenta las parejas etapa + momento (salida y llegada de cada etapa) con una inspeccion cerrada; `Completa = 1` solo con los 6. `FechaDespacho` es el primer cierre de la salida del Cargue y `Fecha` su dia en Colombia |
| `vwAdopcion` | dia, cliente, operacion, origen, destino, marca y trailer | `Programados` = VIN de los viajes no cancelados creados ese dia; `ConSalidaDigital` = de esos, los que tienen una inspeccion de salida en el sistema (borrador o cerrada); `Porcentaje`. **Formula propuesta, pendiente de confirmar por Calidad.** El dia de un viaje es el de su creacion porque `tblViajes` aun no guarda la fecha programada |
| `vwTiempoCaptura` | inspeccion cerrada de Cargue o Descargue | `Minutos` = cierre - inicio (las de Transporte no entran; tampoco las que tienen el cierre antes del inicio) |
| `vwAveriasPorConfiguracion` | hallazgo revisado de una inspeccion cerrada | con placa, trailer, tipo de unidad, conductor (nombre y cedula), VIN, marca, operacion, cliente, clase, tamano, nivel, proceso, momento y zona (lado + numero, o la provisional). Excluye las novedades del conductor aun sin revisar |
| `vwIndicadorDiario` | dia, cliente, operacion, origen, destino, marca y trailer | `Despachados` = las unidades de `vwCobertura` despachadas ese dia; `ConNovedad` = de esas mismas unidades, las que tienen al menos un hallazgo de nivel **V1** o **V1+** de una inspeccion cerrada ese dia (V1+ cuenta como propuesta, pendiente de confirmar por el negocio); `Porcentaje` |

`vwAdopcion` y `vwIndicadorDiario` guardan una fila por cada combinacion de las columnas de filtro (para recalcular el reporte con los filtros del tablero): el total de un dia es la **suma** de sus filas y el porcentaje se recalcula
con esas sumas. `vwPatronesPorCuadrante` (la de los mapas de calor anteriores) no cambia.

## Aplicar el cambio en una base existente

Con un usuario que pueda crear vistas (no el de la aplicacion), conectado a la base de IA Autos, **antes de desplegar la API**:
[`database/cambios-esquema/20-vistas-de-indicadores.sql`](../database/cambios-esquema/20-vistas-de-indicadores.sql). Exige que `10-zona-dano-y-hallazgo.sql`, `13-acciones-niveles-severidad.sql` y `14-sello-y-unidad-no-sale.sql` ya esten aplicados.
Ajusta `@esquema` (`dbo`, o `db_IA_AUTOS` en la base compartida de pruebas), se ejecuta completo en **una sola ejecucion de script**, es una sola transaccion y se puede repetir. Resultado esperado: 5 vistas.
