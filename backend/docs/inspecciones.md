# Inspección de salida y de llegada con fotos selladas

Por cada **etapa** de un VIN en un viaje hay una **inspección de salida** (cuando se entrega) y una **inspección de llegada** (cuando se recibe). Cada una lleva las fotos de
las vistas obligatorias, cada foto con su propio sello, y no se cierra hasta cumplir lo que exige su etapa. Los endpoints están en [CONTRATO.md](../CONTRATO.md#inspecciones-informes).

## Tablas

| Tabla | Cambio |
|---|---|
| `tblInspecciones` | `Momento` es `salida` o `llegada` (ya no `check_in`, `check_out` ni `informe`); gana `IniciadaAt`, `CerradaAt`, `Version` y `CorrigeInspeccionId`. El `UNIQUE (EtapaId, Momento)` pasa a dos índices filtrados: una sola inspección **original** por etapa y momento, y cada versión se corrige una sola vez |
| `tblEvidencias` | gana `VistaId` (la vista de foto), `HashSha256`, `SelloServidorAt`, `Precision`, `DispositivoId` y `Nota`; `SelloLat` y `SelloLng` pasan a ser obligatorios |

Restricciones: una inspección cerrada tiene `CerradaAt` y una en borrador no; la versión 1 no corrige a nadie y las siguientes sí; el hash son 64 caracteres hexadecimales; la precisión no es negativa.
El trigger de inmutabilidad sigue impidiendo modificar una cerrada y agregarle hallazgos o fotos.

## Qué exige cada etapa para cerrar

| Etapa | Requisitos |
|---|---|
| **Cargue y Descargue** | la foto de **cada una de las 7 vistas obligatorias** (las vistas activas de `vistas-foto` con `obligatoria`, que no tienen tipo de vehículo o lo tienen igual al del vehículo); una foto propia por cada hallazgo; ubicación en toda foto |
| **Transporte** | no exige las 7 vistas: la salida y la llegada son el registro del conductor y exigen el **GPS y la hora del propio registro**; una novedad lleva su hallazgo con foto. Cuentan para la cobertura |
| Cualquier otra etapa que agregue el catálogo | se trata como Transporte (no exige las vistas) |

Las dos **vistas adicionales** (piezas en el baúl y cojinería) se toman solo cuando hay un faltante o daño, y su foto exige una **nota** con ese faltante o daño. Una inspección sin faltantes ni
daños se cierra sin ellas. Si el cargue no tiene personal de apoyo, la inspección de salida la hace el conductor con exactamente las mismas reglas. Si lo tiene, el servidor no deja que el conductor la cree (403: «Este cargue tiene personal de apoyo: el conductor solo firma el recibo»): ya no depende de que la app lo bloquee.

Un cierre incompleto responde `400` con la lista de lo que falta, **una línea por cada vista, con su nombre** («Falta la foto de la vista «Frontal»»).

## El sello de cada foto

Cada foto guarda tres cosas distintas, y ninguna se pisa con otra:

| Qué | De dónde sale |
|---|---|
| **Sello del dispositivo**: `sello_fecha`, `sello_lat`, `sello_lng`, `precision`, `dispositivo_id` | la app, al tomar la foto |
| **Sello del servidor**: `sello_servidor_at` | el servidor, al recibir la evidencia (el cliente no lo puede fijar) |
| **Hash SHA-256** del archivo | el servidor lo calcula del archivo guardado; si la app envía el suyo, debe coincidir (si no, `400`) |

El archivo se sube primero (`POST /api/uploads`, en base64 o como `multipart/form-data`) y la evidencia lo referencia por su `storage_path`: si el archivo no existe, la evidencia se rechaza.
En el informe del portal cada foto se muestra con su propia fecha, hora y ubicación y con el sello del servidor.

**Foto sin ubicación.** Una foto sin GPS completo se guarda con (0, 0) y `sin_ubicacion: true`. La app la muestra marcada y pide repetirla: la **última foto de cada vista reemplaza a la anterior**
para el cierre (las anteriores quedan guardadas, son evidencia). Mientras la última siga sin ubicación, el cierre responde `400`.

## Un hallazgo exige su foto

`POST /api/inspecciones/{id}/hallazgos` recibe el hallazgo **y** su foto (`evidencia`) y los guarda juntos: sin foto, `400` y no se guarda nada. Al cerrar, un hallazgo histórico sin foto también bloquea.

## La etapa anterior debe estar terminada

Para crear la **salida** de una etapa, la etapa anterior del mismo VIN en el viaje (la de mayor `orden` menor) debe tener su **llegada cerrada** y ninguna **transferencia sin tipo de cierre**. Si no, `409` con `code: "ETAPA_PREVIA_SIN_CIERRE"`
y `details`: `motivo` (`etapa_previa_sin_cierre` si falta cerrar la llegada, `transferencia_sin_cierre` si falta cerrar la transferencia, ver [transferencias.md](transferencias.md)), `etapa_previa_id` y `etapa_previa` (el nombre de la etapa que falta terminar), para que la app muestre el bloqueo. La primera etapa y la llegada
no tienen este bloqueo.

## Lo que ve el conductor

El conductor no ve los hallazgos que registraron otros: en `GET /api/vehiculos/{vin}/inventario` y `GET /api/inspecciones/{id}` toda inspección que **no creó** llega con `hallazgos: []` y sin las fotos de hallazgos ni las del sello (`clsVisibilidadConductor`); las
que sí creó (sus novedades) llegan completas. `GET /api/inspecciones/{id}/hallazgos-previos` y `GET /api/hallazgos` responden `403`. Las novedades que registra en ruta están en [notificaciones.md](notificaciones.md).

## Corrección sin modificar lo cerrado

Una inspección cerrada no se edita. `POST /api/inspecciones/{id}/corregir` crea la **versión siguiente** (`version` 2, 3…, en borrador y vacía) con `corrige_inspeccion_id` apuntando a la
anterior; la anterior no cambia (sigue cerrada, con sus fotos y su hora de cierre). La nueva tiene sus propias fotos, hallazgos, transferencia y reglas de cierre. Solo se corrige la
**última** versión, y mientras el viaje esté activo (`409` en otro caso). Quien corrige es quien participa en el viaje o un administrador.

## Nombres en pantalla

El `momento` viaja como `salida` o `llegada`; para mostrar, use `momento_nombre`: «Inspección de salida» o «Inspección de llegada» (también aparece así en el PDF del inventario).

## Lo que no cambia ni está en este servicio

- **Qué es «movilizado».** La vista `vwViajesCompletos` sigue contando una etapa como completa cuando tiene **alguna inspección cerrada** (de salida o de llegada, de cualquier versión).
  No exige todavía la llegada; los reportes y listados no cambian.
- **Tipo de cierre de la transferencia.** Lo comprueba la regla de la etapa anterior; su modelo y sus endpoints están en [transferencias.md](transferencias.md).
- **Fotos de la app.** Capturar el GPS y la hora, pedir repetir la foto sin ubicación y mostrar el bloqueo son de la app móvil y de la web; aquí está la regla y el dato.

## Aplicar el cambio en una base existente

Con un usuario que pueda alterar tablas y desactivar triggers (no el de la aplicación), conectado a la base de IA Autos, **antes de desplegar la API**:

1. Ejecutar [`database/cambios-esquema/07-inspecciones-salida-llegada.sql`](../database/cambios-esquema/07-inspecciones-salida-llegada.sql) ajustando `@esquema` (`dbo`, o `db_IA_AUTOS` en la
   base compartida de pruebas). Es una sola transacción: si algo falla, no cambia nada. Se puede repetir (si ya está aplicado, no hace nada). Exige que `06-identidad.sql` ya esté aplicado.
2. Comprobar el resultado que muestra el script.

Qué hace con los datos existentes: `informe` y `check_in` pasan a `salida` y `check_out` a `llegada`. Si una etapa tenía varias inspecciones para el mismo momento nuevo (por ejemplo un
informe y un check_in), la más antigua queda como la original y las demás como **versiones siguientes encadenadas** (2, 3…) en orden de fecha: no se borra ni se pierde ninguna. `IniciadaAt` toma
la fecha de creación y `CerradaAt` (solo en las cerradas) la última modificación. Las fotos históricas sin ubicación quedan en (0, 0) y se muestran «sin ubicación»; su `SelloServidorAt` es su fecha de creación.
Las inspecciones cerradas siguen cerradas: el trigger de inmutabilidad se desactiva **solo** mientras se actualizan y se vuelve a activar dentro de la misma transacción.

Una base nueva se crea con `ddl.sql`, que ya trae el esquema. `tests/verificar-ddl.sql` comprueba las restricciones de las inspecciones, las versiones y las fotos.
