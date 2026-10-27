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

## Parametros

Se leen en cada ejecucion con una cache de **un minuto**: un cambio se aplica en un minuto o menos, sin desplegar (ver [parametros.md](parametros.md)).

| Parametro | Inicial | Para que sirve |
|---|---|---|
| `niveles_criticos` | v1_plus,v1 | niveles que alertan como hallazgo critico (lista de codigos) |
| `umbral_patron_cantidad` | 10 | hallazgos por zona o por proceso en la ventana a partir de los cuales se alerta |
| `umbral_patron_ventana_dias` | 30 | dias de la ventana |
| `captura_incompleta_minutos` | 30 | minutos que una captura puede seguir abierta antes de avisar |
| `transferencia_pendiente_minutos` | 240 | minutos que una llegada puede seguir con la transferencia sin cierre antes de avisar |

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
`clsCalculadoraVistas`). Se avisa a quien la inicio, con las vistas que faltan: el inspector o, **solo en un Cargue sin personal de apoyo**, el conductor (un Descargue iniciado por el conductor no avisa). Un aviso por inspeccion.
No avisa de una inspeccion cerrada, ni de Transporte, ni de un usuario retirado.

## Transferencia pendiente

Cada 10 minutos se buscan las **llegadas cerradas** hace mas de `transferencia_pendiente_minutos` cuya transferencia no tiene tipo de cierre, de unidades que siguen en el viaje (no «no sale») y de viajes activos:

| Situacion | A quien se avisa |
|---|---|
| La transferencia existe | al firmante que falta: quien entrega si no firmo y quien recibe si no firmo (a los dos si faltan los dos) |
| No existe y la etapa siguiente es Transporte | al conductor del viaje |
| No existe y la etapa siguiente es Descargue | al inspector del viaje o, si no hay, al conductor |
| No existe y no hay etapa siguiente | a nadie |

## Procesos periodicos

`clsProcesoAlertas` es un servicio de fondo que cada minuto revisa si toca correr los avisos operativos (cada `Alertas__AvisosCadaMinutos`, 10) o los patrones (cada `Alertas__UmbralesCadaMinutos`, 60); corre una primera vez al arrancar. Un fallo se registra y se reintenta en la
siguiente ejecucion. `Alertas__ProcesosActivos=false` lo apaga. Cada ejecucion procesa hasta 500 pendientes. La logica esta en `clsAlertasPeriodicas`, que las pruebas ejecutan directamente.
