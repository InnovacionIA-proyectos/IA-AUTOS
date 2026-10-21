# Notificaciones, dispositivos y novedad del conductor

El sistema avisa con **notificaciones** (alertas y avisos operativos) y guarda los **dispositivos** donde cada usuario recibe las notificaciones push. Este documento describe el modelo de datos;
los endpoints que lo usan estan en [CONTRATO.md](../CONTRATO.md) («Conductor en ruta» y «Notificaciones»).

## Notificaciones

`tblNotificaciones` guarda **una fila por destinatario y por canal**: una misma alerta para un coordinador son dos filas, una en la bandeja del portal y otra por push.

| Columna | Contenido |
|---|---|
| `Tipo` | `novedad_conductor`, `respuesta_conductor`, `hallazgo_critico`, `umbral_patron`, `captura_incompleta`, `transferencia_pendiente` o `reporte_diario` |
| `Urgencia` | `alta` o `media` |
| `UsuarioId` | el destinatario; nulo solo en `reporte_diario` (el reporte diario no tiene un destinatario propio) |
| `ClienteId`, `ViajeId`, `VehiculoId`, `HallazgoId`, `InspeccionId` | a que se refiere la notificacion (todos opcionales) |
| `Titulo`, `Mensaje` | lo que ve el usuario (hasta 200 y 1000 caracteres) |
| `Canal` | `bandeja`, `push` o `correo`; el **correo solo se admite en el reporte diario** |
| `EnviadaAt`, `ErrorEnvio` | cuando se entrego al canal y, si fallo (por ejemplo FCM), el motivo; se guardan al crear la fila |
| `LeidaAt` | cuando la leyo el destinatario |
| `RespuestaTexto`, `RespuestaMedio`, `RespondidaPor`, `RespondidaAt` | la respuesta del coordinador a una `novedad_conductor` (`RespuestaMedio`: `app` o `llamada`); completa o vacia, y solo sobre ese tipo |
| `ClaveDedupe` | clave **unica** para que una alerta no se repita; incluye la alerta, el destinatario y el canal (hasta 300 caracteres) |

- **Append-only salvo la lectura y la respuesta.** Solo se pueden cambiar `LeidaAt` y las cuatro columnas de la respuesta: cambiar otra columna falla con el error **51006** y borrar una fila con el **51003**
  (la misma regla que protege el resto de la evidencia, ver [evidencia-inmutable.md](evidencia-inmutable.md)).
- El indice `IX_tblNotificaciones_Usuario_Leida` (`UsuarioId`, `LeidaAt`) sirve a la bandeja y al conteo de no leidas.

## Dispositivos

`tblDispositivos` guarda el celular (o navegador) donde un usuario recibe el push: `UsuarioId`, `TokenFcm` (**unico**), `Plataforma` (`android`, `ios` o `web`), `Modelo`, `UltimoUsoAt` y `Activo`.
Un dispositivo que se retira queda inactivo; el token no se repite aunque cambie de usuario.

## La novedad del conductor

La novedad es un **hallazgo** con `Origen = 'conductor'` y `TipoNovedad` (`golpe`, `piedra`, `granizo` o `incidente_vial`), con su foto, el tamano de dano elegido de la lista y el GPS y la hora de la foto, sobre la
inspeccion de **salida de Transporte** del VIN en el viaje. La base garantiza:

| Regla | Como |
|---|---|
| Sin zona, clase, nivel ni texto libre hasta que un inspector la revise | `CK_tblHallazgos_NovedadSinRevisar` (mientras `RevisadoAt` es nulo) |
| Al revisarla se completan la zona, la clase y el nivel | `CK_tblHallazgos_NovedadRevisada` |
| El conductor nunca trae el nivel V (lo asigna quien revisa) | `CK_tblHallazgos_NivelDelConductor`, para todo hallazgo de origen conductor |
| Una novedad solo es de la salida de Transporte | trigger `TR_tblHallazgos_NovedadDeTransporte`, error **51007** |

Los hallazgos que el conductor registra en un Cargue sin personal de apoyo no son novedades (no llevan `TipoNovedad`): conservan su zona y su clase y quedan sin nivel hasta que se revisan.
Que haya foto, que el tamano sea un codigo activo y que existan el GPS y la fecha lo valida la API al crear la novedad.

## Como se crean y se envian

`clsEmisorNotificaciones` crea las notificaciones de una alerta: una fila de **bandeja** y una de **push** por destinatario. El push va por Firebase Cloud Messaging a cada dispositivo activo del destinatario (`IEnviadorPush`, implementado por `clsEnviadorFcm`):

- El envio nunca rompe la peticion que lo origino. Como `EnviadaAt` y `ErrorEnvio` solo se fijan al crear la fila, el push se intenta **antes** de guardarla: si llego a algun dispositivo queda `EnviadaAt`; si no, queda `ErrorEnvio`
  (FCM respondio con error, FCM no esta configurado o el usuario no tiene dispositivos activos). Un token que FCM ya no reconoce (`UNREGISTERED`) deja el dispositivo inactivo.
- Si crear las notificaciones falla, se registra en el log y la peticion sigue: el hecho que se avisa (la novedad) ya quedo guardado.
- La clave de dedupe es `{alerta}:{destinatario}:{canal}` (por ejemplo `novedad_conductor:{hallazgo}:{usuario}:bandeja`): una alerta con la misma clave no se repite.
- La respuesta a una novedad se guarda en **todas** las filas de esa novedad (de cada coordinador y administrador y de cada canal) con un solo `UPDATE` condicionado a que aun no tengan respuesta: asi la primera respuesta gana aunque dos lleguen a la vez.

Configuracion de FCM (cuenta de servicio de Firebase; sin ella el push no se envia y queda `ErrorEnvio`): `Notificaciones__FcmProyectoId`, `Notificaciones__FcmClientEmail` y `Notificaciones__FcmClavePrivada` (los saltos de linea de la clave pueden ir como `\n`).

## Aplicar el cambio en una base existente

Con un usuario que pueda crear tablas y triggers (no el de la aplicacion), conectado a la base de IA Autos, **antes de desplegar la API**:

1. Ejecutar [`database/cambios-esquema/15-notificaciones-y-dispositivos.sql`](../database/cambios-esquema/15-notificaciones-y-dispositivos.sql) ajustando `@esquema` (`dbo`, o `db_IA_AUTOS` en la base compartida de pruebas), en **una sola
   ejecucion de script**. Es una sola transaccion: si algo falla, no cambia nada. Se puede repetir (si ya esta aplicado, no hace nada). Exige que `10-zona-dano-y-hallazgo.sql` ya este aplicado.
2. Comprobar el resultado que muestra el script: 2 tablas, 3 restricciones del hallazgo, 3 triggers y 0 notificaciones y dispositivos.

No borra ni cambia ningun dato. Las restricciones del hallazgo se validan contra lo ya guardado: si algun hallazgo no las cumpliera, el script se detiene sin cambiar nada. `tests/verificar-ddl.sql` prueba cada
restriccion y cada trigger.
