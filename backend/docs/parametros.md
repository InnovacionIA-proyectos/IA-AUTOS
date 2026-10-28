# Parametros

Los valores que el negocio ajusta sin desplegar codigo viven en `tblParametros`: el objetivo diario, los destinatarios y la hora del reporte diario, el horario de operacion, la retencion, la precision minima del GPS,
la vigencia de las URL firmadas y los umbrales de las alertas. Los valores iniciales los confirma Calidad; este documento describe el modelo de datos. La API para consultarlos y cambiarlos (`GET /api/parametros` y `PATCH /api/parametros/{clave}`, para quien administra los catalogos) esta en [CONTRATO.md](../CONTRATO.md) («Parametros»): valida el valor segun su tipo, no crea ni borra claves,
se aplica en un minuto o menos sin desplegar y cada cambio queda en la auditoria con el valor antes y despues.

## Tabla

`tblParametros` guarda **una fila por parametro**. Las filas las crea el script de base de datos; la API solo cambia su valor.

| Columna | Contenido |
|---|---|
| `Clave` | clave del parametro (llave primaria), por ejemplo `reporte_diario_hora` |
| `Valor` | el valor como texto; **nulo = sin valor** (por ejemplo, ningun destinatario del reporte o la retencion que aun no se define). Un valor vacio no se admite |
| `Tipo` | como se valida el valor: `numero`, `porcentaje` (0 a 100, con punto decimal), `texto`, `lista` (codigos separados por coma), `hora` (`HH:mm`) o `correos` (direcciones separadas por coma) |
| `Descripcion` | para que sirve, en una frase |
| `ActualizadoPor`, `UpdatedAt` | quien cambio el valor por ultima vez (nulo en el valor inicial) y cuando |

## Parametros iniciales

| Clave | Tipo | Valor inicial | Para que sirve |
|---|---|---|---|
| `objetivo_diario_novedad` | porcentaje | 1.2 | objetivo diario de vehiculos con novedad (al menos un hallazgo V1) |
| `reporte_diario_destinatarios` | correos | sin valor | correos que reciben el reporte diario; vacio = no se envia |
| `reporte_diario_hora` | hora | 06:00 | hora (hora de Colombia) a la que se envia el reporte diario |
| `horario_operacion_inicio` | hora | 08:00 | inicio del horario de operacion |
| `horario_operacion_fin` | hora | 21:00 | fin del horario de operacion |
| `retencion_dias` | numero | sin valor | dias que se conserva la evidencia, hasta que se defina la retencion |
| `gps_precision_minima_m` | numero | 30 | precision minima del GPS, en metros, para no avisar de precision baja |
| `url_firmada_minutos` | numero | 15 | vigencia de las URL firmadas de las fotos |
| `niveles_criticos` | lista | v1_plus,v1 | niveles de severidad criticos: al asignarlos a un hallazgo se alerta al coordinador y al administrador del cliente |
| `umbral_patron_cantidad` | numero | 10 | hallazgos por zona o por proceso, dentro de la ventana, a partir de los cuales se alerta de un patron |
| `umbral_patron_ventana_dias` | numero | 30 | dias de la ventana en la que se cuentan los hallazgos de un patron |
| `captura_incompleta_minutos` | numero | 30 | minutos que una inspeccion de Cargue o Descargue puede seguir en borrador sin todas las fotos antes de avisar a quien la inicio (la inspeccion FLASH AVES dura maximo 5 minutos) |
| `transferencia_pendiente_minutos` | numero | 240 | minutos que una llegada cerrada puede seguir con la transferencia sin cierre antes de avisar al firmante que falta |
| `reclamo_sin_actividad_minutos` | numero | 30 | minutos que un camion reclamado puede seguir sin ninguna inspeccion iniciada o cerrada antes de avisar al coordinador; el reclamo no se libera solo (el valor definitivo lo confirma Calidad) |

No hay filas de linea base de rechazo ni de fotos minimas (las vistas obligatorias estan en el catalogo `vistas-foto`). Una fila que ya existe **nunca se sobrescribe** al repetir los scripts.

## Vista de la ventana de umbral

`vwHallazgosVentanaUmbral` devuelve **una fila por hallazgo** de una inspeccion **cerrada**, para contar patrones por zona o por proceso dentro de una ventana de dias:

| Columna | Contenido |
|---|---|
| `HallazgoId`, `InspeccionId`, `ViajeId`, `VehiculoId` | el hallazgo y donde se registro |
| `ClienteId` | el cliente del vehiculo |
| `ZonaId`, `Numero`, `Lado`, `ZonaCodigo` | la zona: lado + numero, o el codigo de la zona provisional (nulos en un hallazgo historico sin zona) |
| `Proceso` | el codigo de la etapa (`cargue`, `transporte`, `descargue`) |
| `Fecha` | cuando se cerro la inspeccion |

Excluye las novedades del conductor aun sin revisar (no tienen zona, clase ni nivel): entran cuando un inspector las revisa. Es la misma regla de `vwPatronesPorCuadrante`.

## Aplicar el cambio en una base existente

Con un usuario que pueda crear tablas y vistas (no el de la aplicacion), conectado a la base de IA Autos, **antes de desplegar la API** y en este orden:

1. [`database/cambios-esquema/16-tabla-parametros.sql`](../database/cambios-esquema/16-tabla-parametros.sql): crea `tblParametros` y sus ocho parametros iniciales. Resultado esperado: 1 tabla, 8 parametros, 6 con valor y 2 sin valor.
2. [`database/cambios-esquema/17-parametros-de-alertas.sql`](../database/cambios-esquema/17-parametros-de-alertas.sql): agrega los cinco parametros de las alertas y crea la vista. Exige que el 16 y el
   `10-zona-dano-y-hallazgo.sql` ya esten aplicados. Resultado esperado: 5 parametros de alertas y 1 vista.

Cada script ajusta `@esquema` (`dbo`, o `db_IA_AUTOS` en la base compartida de pruebas), se ejecuta completo en **una sola ejecucion de script**, es una sola transaccion y se puede repetir. No cambian ningun dato existente.
`tests/verificar-ddl.sql` prueba las restricciones de la tabla, los valores iniciales y la vista.
