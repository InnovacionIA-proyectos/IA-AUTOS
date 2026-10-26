# Transferencia de custodia

La transferencia es el evento de entrega y recibo de la unidad entre dos personas **del viaje**. Cada parte firma lo suyo, la transferencia se cierra de una de tres formas y, mientras
no esté cerrada, no se puede crear la salida de la etapa siguiente. Los endpoints y sus cuerpos están en [CONTRATO.md](../CONTRATO.md#transferencia-de-custodia).

## Quién firma

- **Cada firma la envía su titular.** La identidad sale del token: quien entrega firma la entrega y quien recibe firma el recibo. Cualquier otro usuario recibe `403`.
  Antes el cliente enviaba los dos ids y las dos firmas en la misma llamada, así que una firma no tenía dueño; ahora el cuerpo trae una sola `firma` y el servidor la guarda en la parte
  del usuario autenticado.
- **Los firmantes son del viaje.** Al crear la transferencia, quien entrega y quien recibe deben ser el conductor o el inspector del viaje de la inspección (`400` con el campo si no).
  `GET /api/viajes/{id}/firmantes` devuelve esa lista para que la app elija solo entre ellos. Una vez creada, los firmantes no se cambian (`409`).
- **Fecha e IP por firma.** Cada firma guarda cuándo se envió (`FirmaEntregaAt`, `FirmaRecibeAt`) y desde qué IP (`FirmaEntregaIp`, `FirmaRecibeIp`). `FirmadaAt` se sella cuando está la segunda.
  La IP es la de la conexión que recibe la API: detrás de un proxy inverso, para registrar la de quien firma hay que habilitar `Proxy__ConfiarEnEncabezadosReenviados=true`
  (ver [configuracion.md](configuracion.md)); sin ello se guarda la del proxy.

## Cómo se cierra

| `TipoCierre` | Condición | Datos que guarda |
|---|---|---|
| `firmada` | ambas partes firman | las dos firmas |
| `con_reservas` | quien recibe firma por `/transferencia/reservas` | las dos firmas y `ReservasTexto` (obligatorio, máximo 1 000 caracteres) |
| `no_firma` | una parte se niega y la otra deja constancia por `/transferencia/no-firma` | `NoFirmaParte`, `MotivoNoFirma`, `TestigoUsuarioId` y `EvidenciaConstanciaId` |
| `recibo_sin_entrega` | nadie de Botero Soto firma la entrega porque el conductor hizo la salida del Cargue | solo la firma de quien recibe con su fecha e IP; **sin** usuario, firma, fecha ni IP de entrega |
| sin valor | falta una firma | la transferencia está **abierta** |

- Solo **quien recibe** firma con reservas. Si quien recibe firma después normalmente, las reservas se descartan.
- La **constancia de no firma** la registra la otra parte (nadie declara su propia negativa) y exige el motivo, un **testigo con usuario** (activo, distinto de las dos partes) y la **foto**.
  La parte que se negó no puede haber firmado antes.
- La foto de la constancia es una evidencia con `EsConstancia = 1`: se admite aunque la inspección ya esté cerrada (el trigger de evidencias la deja pasar) y no cuenta como foto de una
  vista ni de un hallazgo (no entra en el resumen del cierre).
- Cerrada, la transferencia es **inmutable**; solo se acepta repetir exactamente la misma firma (reintento idempotente).

La base de datos refuerza lo mismo: `CK_tblTransferencias_Cierre` exige, según el tipo, las firmas, las reservas, la constancia completa o solo la firma de quien recibe, y `CK_tblTransferencias_Testigo` impide que el testigo sea
una de las partes. `EntregaUsuarioId` admite nulo, pero solo en el cierre `recibo_sin_entrega` (`CK_tblTransferencias_Entrega`).

## Qué bloquea

La salida de una etapa exige que la etapa anterior del mismo VIN esté **terminada**: su llegada cerrada **y** ninguna transferencia suya sin tipo de cierre. Si falta algo, `409` con
`code: "ETAPA_PREVIA_SIN_CIERRE"` y `details.motivo`:

| `motivo` | Falta |
|---|---|
| `etapa_previa_sin_cierre` | cerrar la inspección de llegada de la etapa anterior |
| `transferencia_sin_cierre` | cerrar la transferencia de la etapa anterior (firmarla, recibirla con reservas o registrar la no firma) |

## Constancia en PDF

`GET /api/inspecciones/{id}/transferencia/constancia/pdf` genera el documento sin dependencias de terceros: datos de la unidad, tipo de cierre, las dos firmas con su fecha e IP (la firma se
dibuja si se guardó como trazo SVG o se incrusta si se guardó como imagen), las reservas o el motivo de la no firma, el testigo y la foto de la constancia. Las fotos JPEG y las PNG de 8 bits sin
entrelazado se incrustan; otro formato se indica con una nota en lugar de omitirse en silencio. Inspector y conductor solo la descargan de sus viajes.

## Lo que no cambia ni está en este servicio

- **Qué es «movilizado».** La vista `vwViajesCompletos` no cambia: sigue contando una etapa como completa cuando tiene alguna inspección cerrada.
- **El conductor que recibe en el cargue sin revisar los hallazgos.** La firma no depende de los hallazgos y su respuesta no los incluye. Que la app no se los muestre ni se los notifique en esa
  pantalla es una regla de la app; el servicio no tiene un mecanismo de notificaciones.
- **Firmar desde el dispositivo del otro.** Cada firma se envía con la sesión de su titular; la pantalla que lo organiza es de la app.

## Aplicar el cambio en una base existente

Con un usuario que pueda alterar tablas y recrear triggers (no el de la aplicación), conectado a la base de IA Autos, **antes de desplegar la API**:

1. Ejecutar [`database/cambios-esquema/08-transferencias-custodia.sql`](../database/cambios-esquema/08-transferencias-custodia.sql) ajustando `@esquema` (`dbo`, o `db_IA_AUTOS` en la base
   compartida de pruebas). Es una sola transacción: si algo falla, no cambia nada. Se puede repetir (si ya está aplicado, no hace nada). Exige que `07-inspecciones-salida-llegada.sql` ya esté aplicado.
2. Comprobar el resumen que muestra el script (transferencias, firmadas, abiertas, fotos y fotos de constancia).

Qué hace con los datos existentes: agrega las columnas de cierre, la fecha y la IP de cada firma, y la marca de constancia de las fotos; **las transferencias que ya tenían las dos firmas quedan
como `firmada`** y la fecha de cada firma toma la que ya tenían (`FirmadaAt`, o su última modificación); las que tenían una sola firma o ninguna quedan **abiertas** (sin tipo de cierre), de modo
que bloquean la salida de la etapa siguiente hasta que se cierren. La IP de las firmas históricas queda vacía (no se registraba). Recrea el trigger de evidencias para admitir la foto de una constancia.

Una base nueva se crea con `ddl.sql`, que ya trae el esquema. `tests/verificar-ddl.sql` comprueba las restricciones del cierre, el testigo y la foto de la constancia.
