# Conductor en ruta

El conductor responde por lo que pasa en el recorrido de cada unidad de **su** viaje: registra las **novedades** (golpe, piedra, granizo o incidente vial) y confirma la **llegada**. Las rutas, los cuerpos y las respuestas están en [CONTRATO.md](../CONTRATO.md) («Conductor en ruta»); aquí van las
reglas y cómo encajan con el resto.

## Qué hace el conductor

| Acción | Ruta | Resultado |
|---|---|---|
| Registrar una novedad | `POST /api/viajes/{id}/vehiculos/{vin}/novedades` | un hallazgo `origen = conductor` en la inspección de **salida de Transporte** de la unidad (se crea en borrador con la primera novedad) |
| Confirmar la llegada | `POST /api/viajes/{id}/vehiculos/{vin}/llegada` | cierra la salida de Transporte y crea y cierra la inspección de **llegada**; responde `{ inspeccion, novedades, sin_novedad }` |
| Ver su viaje | `GET /api/viajes/mios` | cada VIN con `estado_transporte` (`sin_registro`, `con_novedad`, `sin_novedad`, `llegada_con_novedad`) y `novedades` |

Las dos son **solo del `conductor` del viaje**: cualquier otro usuario (también un administrador) recibe `403`; `404` si el viaje o el VIN no existen o el VIN no está en el viaje; `409` si el viaje está finalizado o cancelado.

## Reglas

- **Sin texto libre.** La novedad lleva `tipo_novedad`, `tamano_dano`, la foto sellada (`evidencia`) y el GPS; `nota`, `clase_dano`, `nivel_severidad`, `cuadrante_numero`, `lado` y `zona_provisional` responden `400` («Campo no admitido»). El cuadrante, la clase y el nivel V los pone el inspector al
  **revisar** la novedad (ver [seguimiento-hallazgos.md](seguimiento-hallazgos.md) y «Revisión de la novedad» en el contrato); una novedad revisada no se vuelve a revisar (`409 HALLAZGO_YA_REVISADO`).
- **Fecha, hora y GPS del momento.** El celular guarda el registro aunque no haya señal y lo envía al recuperarla: `fecha`, `lat` y `lng` son los del momento del registro, no los del envío. La llegada exige GPS (`400` sin él).
- **Etapa anterior cerrada.** La primera novedad crea la salida de Transporte, y eso exige el Cargue de la unidad cerrado (`409 ETAPA_PREVIA_SIN_CIERRE`).
- **Unidad que no sale.** Si la unidad se marcó «no sale» del viaje, no admite novedades ni llegada (`409 UNIDAD_NO_SALE`).
- **Reintento seguro.** La llegada ya confirmada responde `409 LLEGADA_YA_CERRADA`: para el celular es un éxito (el servidor conserva la primera y no repite nada).
- **Aviso inmediato.** Cada novedad crea una notificación `novedad_conductor` de urgencia alta para los `coordinador_calidad` y `admin` activos del cliente del vehículo, por bandeja y push (ver [notificaciones.md](notificaciones.md)); el coordinador responde desde la bandeja y el conductor recibe la
  `respuesta_conductor`.

## El cargue sin inspector

En un Cargue con personal de apoyo la salida la hace el inspector y el conductor solo firma el recibo (`403` si intenta crearla). Si el inspector previsto **no está**, el conductor del viaje la crea con el motivo (`motivo_sin_inspector`, un código activo de `motivos-sin-inspector` y, para `otro`, su texto):
`POST /api/inspecciones` responde `201`. La etapa conserva `con_personal_apoyo: true`; la inspección guarda el motivo; se crea la **novedad de proceso** `cargue_sin_inspector` con la fecha, lat y lng del momento en que el conductor eligió el motivo; los `coordinador_calidad` y `admin` activos del cliente del vehículo reciben
la notificación `novedad_proceso` (alta, por bandeja y push); y todo queda en la auditoría. Sin motivo sigue el `403`; en cualquier otro caso (otra salida, una llegada, un Cargue sin apoyo, otro rol) el campo responde `400` «Campo no admitido». La inspección sigue las reglas de la salida sin apoyo y el coordinador
de calidad revisa a distancia los hallazgos del conductor de esa salida. Una novedad de proceso no es un hallazgo y no cuenta en los patrones ni en la tasa de rechazo. Es una decisión del equipo pendiente de confirmar por el negocio (P-073). El detalle está en [CONTRATO.md](../CONTRATO.md) («Excepción «No hay inspector» y novedades de proceso»).

## Lo que el conductor no ve

El conductor no ve los hallazgos que registraron otros: en una inspección que **no creó** llegan `hallazgos: []` y sin las fotos de hallazgos; `GET /api/hallazgos` y `GET /api/inspecciones/{id}/hallazgos-previos` responden `403`; y de las notas aclaratorias solo ve las de los hallazgos que él registró.

## Código

`clsConductorService` (novedades y llegada), `clsRevisionService` (revisión por el inspector), `clsEmisorNotificaciones` (avisos) y, para el cargue sin inspector, `clsInspeccionesService.CrearAsync` con `clsNovedadProceso`. Las pruebas están en `ConductorApiTests`, `RevisionHallazgosApiTests` y `ExcepcionSinInspectorApiTests`.
