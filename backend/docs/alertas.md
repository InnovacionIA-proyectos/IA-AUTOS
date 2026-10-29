# Alertas preventivas y avisos operativos

El sistema avisa de lo que requiere atencion sin que nadie tenga que revisar listados. Hay dos clases:

- **Alertas preventivas**: van al `coordinador_calidad` y a los `admin` **activos del cliente** de la unidad (nunca a otro cliente).
- **Avisos operativos**: van a quien tiene la tarea pendiente (quien inicio la inspeccion o el firmante que falta) y no son alertas preventivas.

Todas se guardan como notificaciones (ver [notificaciones.md](notificaciones.md)): una fila de **bandeja** y una de **push** por destinatario, **nunca por correo** (el unico correo es el reporte diario). Cada una lleva su
**clave de dedupe**, asi que correr un proceso otra vez, o dos instancias a la vez, no repite nada. Si el push falla, la notificacion queda con su `ErrorEnvio`.

| Tipo | Clase | Urgencia | Destinatarios | Cuando | Clave de dedupe |
|---|---|---|---|---|---|
| `hallazgo_critico` | preventiva | alta | coordinador de calidad y administradores del cliente | al **cerrar** una inspeccion con un hallazgo de nivel critico y al **revisar** una novedad con ese nivel | `hallazgo_critico:{inspeccion}`: una por inspeccion |
| `umbral_patron` | preventiva | media | coordinador de calidad y administradores del cliente | proceso **cada hora** | `umbral_patron:{cliente}:{zona o proceso}:{ventana}`: una por zona o proceso y ventana |
| `captura_incompleta` | operativo | media | quien inicio la inspeccion | proceso **cada 10 minutos** | `captura_incompleta:{inspeccion}`: una por inspeccion |
| `transferencia_pendiente` | operativo | media | el firmante que falta | proceso **cada 10 minutos** | `transferencia_pendiente:{inspeccion}`: una por inspeccion y destinatario |
| `cargue_sin_reclamar` | operativo | media | coordinador de calidad del cliente | proceso **cada 10 minutos** | `cargue_sin_reclamar:{viaje}`: una por viaje |
| `propuesta_ubicacion` | operativo | media | coordinador de calidad del cliente | proceso **cada hora** | `propuesta_ubicacion:{propuesta}`: una por propuesta |

## Parametros

Se leen en cada ejecucion con una cache de **un minuto**: un cambio se aplica en un minuto o menos, sin desplegar (ver [parametros.md](parametros.md)).

| Parametro | Inicial | Para que sirve |
|---|---|---|
| `niveles_criticos` | v1_plus,v1 | niveles que alertan como hallazgo critico (lista de codigos) |
| `umbral_patron_cantidad` | 10 | hallazgos por zona o por proceso en la ventana a partir de los cuales se alerta |
| `umbral_patron_ventana_dias` | 30 | dias de la ventana |
| `captura_incompleta_minutos` | 30 | minutos que una captura puede seguir abierta antes de avisar |
| `transferencia_pendiente_minutos` | 240 | minutos que una llegada puede seguir con la transferencia sin cierre antes de avisar |
| `cargue_sin_reclamar_minutos` | 30 | minutos pasada la hora programada de un cargue sin reclamar antes de avisar |
| `valvula_precision_m` | 500 | precision (metros) desde la que una foto ya no sirve para la autocalibracion de la ubicacion |

Si la tabla de parametros no se puede leer, los procesos usan estos valores iniciales y lo registran en el log.

## Hallazgo critico

`clsAlertaHallazgoCritico`. Al cerrar una inspeccion (`POST /api/inspecciones/{id}/cerrar`) y al revisar una novedad (`POST /api/hallazgos/{id}/revision`) se buscan los hallazgos de la inspeccion cuyo `nivel_severidad` esta en `niveles_criticos`. Si hay alguno
se crea **una** alerta por inspeccion que dice cuantos hay y de que nivel. Un segundo hallazgo critico de la misma inspeccion, o la revision de otra novedad de ella, no crea otra. Un fallo al avisar no deshace el cierre ni la revision.

## Patrones por umbral

`clsAlertasPeriodicas.EjecutarUmbralesAsync` cuenta sobre `vwHallazgosVentanaUmbral` los hallazgos de inspecciones **cerradas** de los ultimos `umbral_patron_ventana_dias` dias, **por cliente** y por **zona** (lado + numero, o la zona provisional)
y por **proceso**. Cuando una cuenta **llega** a `umbral_patron_cantidad` (mayor o igual) avisa a los coordinadores de calidad y administradores de ese cliente, con la zona o el proceso y el total.

- **Una vez por ventana.** La ventana es un bloque de `umbral_patron_ventana_dias` dias contado desde el 1 de enero de 2000: dentro de un bloque una zona o un proceso alerta una sola vez aunque siga creciendo; en el bloque siguiente, si sigue en el umbral, vuelve a alertar.
  El conteo es de los ultimos N dias hasta el momento de correr.
- Los hallazgos de otro cliente no se mezclan. Las novedades del conductor sin revisar no cuentan hasta que se revisan.

El reporte `GET /api/reportes/patrones` marca `supera_umbral` con estos mismos parametros (`umbral_patron_cantidad` y `umbral_patron_ventana_dias`).

## Captura incompleta

Cada 10 minutos se buscan las inspecciones en **borrador** de **Cargue o Descargue** de viajes activos, iniciadas hace mas de `captura_incompleta_minutos`, a las que **aun les falta la foto de alguna vista obligatoria** (el mismo calculo que usa el cierre,
`clsCalculadoraVistas`). Se avisa a quien la inicio, con las vistas que faltan: el inspector o, **solo en la salida de un Cargue**, el conductor (un Descargue iniciado por el conductor no avisa). Un aviso por inspeccion.
No avisa de una inspeccion cerrada, ni de Transporte, ni de un usuario retirado.

## Camión reclamado sin actividad

Cada 10 minutos, junto con los demás avisos operativos, se buscan los **reclamos activos** de viajes activos hechos hace más de `reclamo_sin_actividad_minutos` (30 por defecto, ajustable sin desplegar) sin ninguna **inspección del viaje iniciada o cerrada después del reclamo** (esa es la «actividad»).
Se avisa a los `coordinador_calidad` activos del cliente del inspector con una notificación `reclamo_sin_actividad` (urgencia media, bandeja y push) que trae la placa, el viaje, el proceso y el nombre del inspector. Es **un aviso por reclamo** (la clave de dedupe lleva el id del reclamo).
El reclamo **no se libera**: lo libera una persona (ver [viajes.md](viajes.md)). Necesita el script [`27-aviso-reclamo-sin-actividad.sql`](../database/cambios-esquema/27-aviso-reclamo-sin-actividad.sql) aplicado (el tipo de aviso nuevo en `tblNotificaciones`).

## Fuera del rango del lugar

No es un proceso periódico: se dispara con la **primera foto con ubicación** de una inspección de Cargue o Descargue. El servidor calcula la distancia al centro del lugar de la etapa y, si supera el radio más la precisión de la foto, la inspección queda `fuera_de_rango` y se avisa a los `coordinador_calidad` activos del cliente con una notificación `fuera_de_rango` (urgencia alta, bandeja y push) con el viaje, el VIN, el lugar y la distancia. Es **un aviso por inspección** (clave de dedupe: la inspección) y **nunca bloquea** la captura. Con una foto sin ubicación, una precisión mala (la válvula `valvula_precision_m`), un lugar sin ubicación o con la ubicación pendiente de confirmar, el resultado es `sin_confirmacion_posible` y no hay marca ni aviso. Ver CONTRATO.md (`confirmacion_sitio`) y el script [`32-ubicacion-con-rango-y-confirmacion-en-sitio.sql`](../database/cambios-esquema/32-ubicacion-con-rango-y-confirmacion-en-sitio.sql).

## Cargue sin reclamar

Cada 10 minutos, junto con los demas avisos operativos, se buscan los viajes **programados** cuya hora programada paso hace mas de `cargue_sin_reclamar_minutos` (30 por defecto, ajustable sin desplegar) y dentro de las ultimas 24 horas, con unidades activas, **sin ningun reclamo activo del cargue** y **sin ninguna inspeccion iniciada**.
Se avisa a los `coordinador_calidad` activos del cliente de las unidades con una notificacion `cargue_sin_reclamar` (urgencia media, bandeja y push) con el viaje, el lugar y la hora programada. Es **un aviso por viaje**: correr el proceso otra vez no lo repite.
Si el viaje se reclama o se inicia una inspeccion antes de que pase el tiempo, no se avisa. Necesita el script `32-ubicacion-con-rango-y-confirmacion-en-sitio.sql` (parametro y tipo de aviso).

## Propuesta de ubicacion del lugar (autocalibracion)

Cada hora se toman las fotos selladas de las vistas de Cargue y Descargue con ubicacion y con precision menor que `valvula_precision_m`, agrupadas por el lugar de cargue del viaje o el destino de la unidad, de los lugares **activos** cuya ubicacion **no esta confirmada**.
Con al menos **3 visitas distintas** (viajes) se propone el **centro en la mediana** de las coordenadas (latitud y longitud por separado, para que una foto lejana no lo arrastre) y el **radio que cubre al 90 %** de las fotos (redondeado hacia arriba a 10 m, entre 100 y 5.000 m). Se crea una propuesta `pendiente` y se avisa a los `coordinador_calidad` del cliente (`propuesta_ubicacion`, urgencia media).
**El lugar no cambia** hasta que el coordinador la confirme (el lugar queda con ese centro y radio como ubicacion confirmada) o la descarte. Mientras haya una propuesta pendiente no se crea otra, y tras una decision solo se vuelve a proponer si hay fotos nuevas. Las visitas minimas (3) son una constante del codigo, valor ilustrativo que confirma el negocio. Necesita el script `33-tipo-de-aviso-propuesta-de-ubicacion.sql`.

## Transferencia pendiente

Cada 10 minutos se buscan las **llegadas cerradas** hace mas de `transferencia_pendiente_minutos` cuya transferencia no tiene tipo de cierre, de unidades que siguen en el viaje (no «no sale») y de viajes activos:

| Situacion | A quien se avisa |
|---|---|
| La transferencia existe | al firmante que falta: quien entrega si no firmo y quien recibe si no firmo (a los dos si faltan los dos) |
| No existe y la etapa siguiente es Transporte | al conductor del viaje |
| No existe y la etapa siguiente es Descargue | al inspector del viaje o, si no hay, al conductor |
| No existe y no hay etapa siguiente | a nadie |

## Procesos periodicos

`clsProcesoAlertas` es un servicio de fondo que cada minuto revisa si toca correr los avisos operativos (cada `Alertas__AvisosCadaMinutos`, 10) o los patrones y la autocalibracion de ubicaciones (cada `Alertas__UmbralesCadaMinutos`, 60); corre una primera vez al arrancar. Un fallo se registra y se reintenta en la
siguiente ejecucion. `Alertas__ProcesosActivos=false` lo apaga. Cada ejecucion procesa hasta 500 pendientes. La logica esta en `clsAlertasPeriodicas`, que las pruebas ejecutan directamente.
