# Exportaciones a Excel y PDF

Las exportaciones y los PDF se generan **sin librerias de terceros** (sin licencias): un escritor de `.xlsx` propio y un constructor de PDF propio. Las rutas y las columnas estan en [CONTRATO.md](../CONTRATO.md)
(«Exportaciones a Excel», `GET /api/inspecciones/{id}/pdf` y `GET /api/vehiculos/{vin}/inventario/pdf`).

## Excel (`clsGeneradorExcel`)

Escribe un paquete OpenXML minimo (un libro de una hoja) con `System.IO.Compression`: la fila de titulos en negrita y congelada, filtros automaticos y anchos de columna.

- Los textos van como cadenas incrustadas (`inlineStr`): nunca se evaluan como formula aunque empiecen con `=`, `+`, `-` o `@`. Los caracteres que XML no admite se descartan.
- Los numeros van como numeros y los si/no como «Si» / «No».
- Las fechas con hora van como fechas de Excel **en hora de Colombia** (UTC-5, sin horario de verano), con el formato `yyyy-mm-dd hh:mm:ss`.
- Un archivo no pasa de **50 000 filas** (`clsExportacionesService.MAXIMO_FILAS`): con mas, la API responde 400 y pide acotar los filtros.

## Que exporta cada archivo

| Archivo | Una fila por | Origen de los filtros |
|---|---|---|
| `viajes.xlsx` | unidad (VIN), etapa y momento (salida o llegada) de cada viaje: los seis registros de la unidad, con su estado (`sin_inspeccion`, `borrador` o `cerrada`) | listado de viajes |
| `inspecciones.xlsx` | inspeccion (cada version), con el inspector, las firmas de la transferencia y la columna `Extras` (cuantos extras del vehiculo reporto) | listado de inspecciones |
| `hallazgos.xlsx` | hallazgo, con su zona y la fecha, hora y ubicacion de su foto | listado de hallazgos |

Cada exportacion reutiliza el analisis de filtros de su listado (mismos nombres, misma validacion y mismo `400`) y solo trae los datos del cliente del usuario: el cliente de cada fila es el del **vehiculo**.

## PDF (`clsDocumentoPdf`)

Constructor comun de los PDF (constancia de la transferencia, informe de la inspeccion e inventario del VIN): paginas A4 con texto en Helvetica (fuentes base-14), imagenes **JPEG** (tal cual) y **PNG** de 8 bits sin entrelazado
(gris, color o paleta) incrustadas, firmas dibujadas a partir de un trazo SVG, **galerias** de fotos con su pie (el pie de una foto es su sello: fecha y hora, ubicacion, sello del servidor y hash SHA-256) y **paneles** con regiones resaltadas.

- Una imagen en otro formato (por ejemplo, un PNG con canal alfa) o cuyo archivo ya no esta en el almacen se indica en su lugar en vez de omitirse.
- Las imagenes salen del almacen (`IAlmacenArchivos`); no se reescalan (no hay libreria de imagenes), asi que el tamano del PDF depende del de las fotos. Por eso un PDF lleva **hasta 60 fotos**, primero las de los hallazgos, y avisa cuantas dejo fuera.
- El **panel de danos** dibuja, sobre la imagen de cada panel (`paneles-dano`), el poligono de la region (`cuadrante-regiones`) de cada zona con hallazgos y escribe en su centro el numero del hallazgo (informe) o cuantos hay (inventario). Una zona es lado + numero de
  cuadrante y se dibuja en todos los paneles donde aparece; las zonas que no tienen region en ningun panel se listan aparte. El informe y el inventario muestran la zona como lado + numero + pieza con su codigo AIAG («Izquierdo 4 · Guardafango delantero (AIAG 14/16)») y la exportacion de hallazgos trae la columna «Codigo AIAG». Los hallazgos historicos con un punto sobre la imagen anterior (`coords_diagrama`) no se dibujan.
