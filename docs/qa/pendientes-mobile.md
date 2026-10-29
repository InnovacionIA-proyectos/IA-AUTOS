# Mobile: dónde vamos y qué falta

Estado al 26-oct-2026, al cerrar la rama `S5-HU-21-23`. El detalle de cada decisión, con sus pruebas, está en
[matriz-trazabilidad.md](matriz-trazabilidad.md); aquí solo queda lo que hace falta para retomar el trabajo.

## Lo que entra con esta rama

Sub-HU de S5 completas en mobile, todas con prueba y **verificadas en dispositivo** contra la API local:

| | qué quedó |
|---|---|
| S5-HU-21 a 23 | catálogos, viajes, identidad con PIN, menú por capacidades, cola etiquetada por usuario |
| S5-HU-24 | checklist de 7 vistas con guía de cámara, foto por novedad, reglas de cierre, dos momentos por etapa |
| S5-HU-24.12 (#112) | pautas del Entendimiento: acuse en color, monoespaciada, zonas táctiles |
| S5-HU-25.4 (#116) | firmantes del viaje, segunda firma que hereda las partes, estado de cada firma, el conductor no ve los hallazgos del inspector |
| S5-HU-25.5 (#117) | recibo con reservas y constancia de no firma |
| #151 | la firma va en un solo campo, y el contrato cubre lo nuevo |

La rama trae `QA` dentro, así que va al día con el contrato v2.

## Lo que falta, en orden

### 1. S6-HU-01 en mobile — bloquea registrar novedades

**Con el contrato v2 la app no puede registrar un hallazgo.** `CrearHallazgoRequest` cambió de raíz: salieron
`parte_vehiculo` y `coords_diagrama`, entraron `cuadrante_numero`, `lado`, `zona_provisional`,
`nivel_severidad`, `es_preexistente`, `hallazgo_origen_id` y `nota`. El servidor exige **exactamente una**
zona: o cuadrante con su lado, o una zona provisional. Por eso no es un ajuste del cuerpo: sin el panel no hay
con qué elegir la zona.

- **#193** — panel de daño: 5 paneles con su imagen descargada, 97 regiones tocables, `CustomPainter` que
  reemplaza `diagrama_vehiculo.dart` y `partes.dart`. Depende de los catálogos `paneles-dano` y
  `cuadrante-regiones`.
- **#199** — captura y confirmación: selector de clase, tamaño y nivel V; hallazgos previos con «ya estaba
  registrado»; flujo «No sale».

Mientras no estén, la prueba de contrato `CrearHallazgoRequest` queda **en rojo a propósito**: taparla con un
`skip` escondería que la app de campo no puede registrar una novedad. Leer los hallazgos ya registrados sí
funciona; `HallazgoDto` no cambió.

### 2. Preguntas para Calidad, antes de implementar nada

- **¿La llegada exige su salida?** Hoy se puede cerrar la llegada de una etapa sin que exista su salida: la
  regla `ETAPA_PREVIA_SIN_CIERRE` solo mira la etapa anterior y solo en el momento de salida. Como la etapa
  siguiente solo pide que la llegada de la previa esté cerrada, la cadena avanza sin registro de la salida.
- **¿Sobre qué informe va la transferencia?** Hoy cuelga del primer informe que se cierra, que en la práctica
  es el de salida, y por eso se puede firmar el cambio de responsabilidad antes de que exista la llegada.
- **Una etapa sin ninguna transferencia ya no bloquea la siguiente**, porque es lo que hace la API. Si debe
  exigirse, la regla entra primero en el servidor: una más estricta solo en la app bloquea trabajo que la API
  acepta.
- **El umbral de precisión baja (30 m)** es un valor inicial, en `precisionBajaMetros`.
- **La vista superior**: confirmar cómo se toma en la práctica, porque nadie se sube encima del vehículo y una
  guía cenital imposible de cumplir se ignora.

### 3. Pendientes menores

- **Comprimir las fotos** (S5-HU-24.8): necesita una dependencia nueva y hoy no urge, porque la cámara captura
  a 720p, muy por debajo del límite de 10 MB.
- **El conductor en un cargue con personal de apoyo**: la app no le ofrece capturar, pero no hay datos de
  prueba para ejercitarlo en dispositivo.
- **El almacén S3 local**: `adobe/s3mock` no es compatible con el SDK nuevo («response ended prematurely»)
  aunque la cabecera del compose lo recomiende; en las pruebas se usó almacenamiento en disco.

## Lo que el conductor no ve del hallazgo (aclarado el 29-oct)

`GET /api/vehiculos/{vin}/inventario` devuelve `nivel_severidad`, `estado_seguimiento` y `evidencia_completa`
en **null cuando quien pregunta es el conductor**. No es un defecto: lo hace a propósito
`clsVisibilidadConductor.ParaConductor`, que además le oculta la nota, el hallazgo de origen, quién confirmó y
quién revisó. Es la regla de S5-HU-25.4: el conductor revisa en lectura los hallazgos del inspector (zona,
clase, tamaño y foto) **sin nivel V ni seguimiento**.

Con el mismo hallazgo y la misma compilación:

| Rol | `nivel_severidad` | `estado_seguimiento` | `evidencia_completa` |
|---|---|---|---|
| super_admin | `v2` | `pendiente` | `false` |
| conductor | `null` | `null` | `null` |

Es reciente: la imagen de la API de hace unos días no ocultaba nada en el inventario, y por eso al
reconstruirla pareció una regresión. Lo era al revés: la redacción es lo nuevo.

**Qué significa para la app:** cuando mira el conductor, sus propios hallazgos se pintan azules «por revisar»
aunque un inspector ya les haya puesto nivel. Es lo que el contrato quiere, pero «Por revisar» dice algo que
no es: no es que nadie lo haya clasificado, es que a él no se le muestra.

La ficha ya distingue los dos casos: dice **«Revisado»** cuando el hallazgo trae fecha de revisión y no trae
nivel. Hoy eso solo lo ve el inspector o el administrador, porque `ParaConductor` también pone `RevisadoAt` y
`RevisadoPor` en null.

**Lo que falta, y es una línea de backend:** dejar pasar `revisado_at` (o un `revisado` booleano) al conductor,
sin soltar el nivel. Con eso el conductor sabría que su novedad ya se revisó, sin saber con qué nivel, que es
justo la regla. La app no necesita ningún cambio: el día que llegue el campo, la etiqueta cambia sola.

Queda para Calidad el color: el azul «por revisar» en el dibujo sigue diciendo lo mismo que la etiqueta vieja.
Si molesta, se le puede dar un color propio a «revisado sin nivel a la vista».

## Cómo se probó

Con la API local y tres VIN en un viaje, un usuario por rol (Ana inspectora, Luis conductor, Marta
coordinadora) y un desenlace por VIN. Todo lo que dice «verificado en dispositivo» en la matriz se comprobó
además contra la base de datos, no solo en pantalla.
