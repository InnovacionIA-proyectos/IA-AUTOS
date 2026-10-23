# Rendimiento de los listados, el inventario y el informe

Medición de los listados paginados, el inventario de un VIN y el informe de una inspección con una semilla de **17 000 VIN** (el tamaño de la operación), y qué cambió para que no dependan de subconsultas por fila.

## Cómo se mide

`MedicionRendimientoApiTests` (en `tests/IA-AUTOS.Tests.Integracion`) arma la base de pruebas real (SQL Server 2019 en un contenedor) con la semilla, llama a cada ruta de la API como un `super_admin` (una vez para calentar y cinco para medir) y reporta la **mediana** y el
**máximo** del tiempo de respuesta (la API completa, no solo la consulta: autenticación, consulta, serialización y lectura del cuerpo). No corre en la corrida normal porque siembra decenas de miles de filas: se activa con una variable de entorno.

```powershell
$env:MEDIR_RENDIMIENTO = "1"
$env:RENDIMIENTO_SALIDA = "rendimiento.md"   # opcional: deja la tabla en este archivo
dotnet test tests/IA-AUTOS.Tests.Integracion --filter "FullyQualifiedName~MedicionRendimientoApiTests"
```

La prueba falla si alguna consulta pasa de 5 000 ms de mediana (una cota holgada: avisa de una regresión grande, no de la variación normal de cada máquina).

### La semilla

| Qué | Cantidad |
|---|---:|
| Vehículos (4 marcas, 3 operaciones) | 17 000 |
| Viajes (cuatro unidades cada uno) | 4 250 |
| Etapas (Cargue, Transporte y Descargue de cada unidad) | 51 000 |
| Informes cerrados (seis de cada diez unidades con sus seis informes, es decir, movilizadas; dos de cada diez solo el Cargue; dos sin informes) | unos 68 000 |
| Hallazgos (una de cada veinte unidades) | 850 |
| Órdenes Sofasa | 17 000 |

Los triggers que mantienen `tblViajeVehiculos.Completa` se apagan mientras se siembra y se recalcula al final con `usp_RecalcularCompleta`. La semilla de QA (S6-HU-05.4) puede tener otra distribución: la prueba sirve para comparar antes y después de un cambio, y los números absolutos
dependen del servidor.

## Resultados

Medidos en un equipo de desarrollo con SQL Server 2019 en Docker. «Antes» es la consulta con subconsultas por fila y la vista `vwViajesCompletos`; «ahora», la consulta de esta versión.

| Consulta | Antes: mediana / máximo (ms) | Ahora: mediana / máximo (ms) |
|---|---:|---:|
| Vehiculos: movilizados, pagina 1 de 50 | 412 / 430 | 121 / 163 |
| Vehiculos: movilizados, pagina 1 de 200 | 406 / 476 | 114 / 152 |
| Vehiculos: movilizados, ultima pagina de 200 | 843 / 872 | 267 / 293 |
| Vehiculos: pendientes | 191 / 201 | 81 / 83 |
| Vehiculos: con novedad | 94 / 114 | 109 / 177 |
| Vehiculos: por marca y operacion, orden por marca | 197 / 276 | 74 / 94 |
| Vehiculos: por chasis | 125 / 156 | 128 / 162 |
| Vehiculos: busqueda por fragmento | 7 / 9 | 14 / 14 |
| Inspecciones: pagina 1 de 50 | 34 / 62 | 39 / 70 |
| Inspecciones: por VIN | 8 / 10 | 20 / 24 |
| Inspecciones: cerradas de una operacion | 82 / 122 | 77 / 104 |
| Viajes: pagina 1 de 50 | 75 / 93 | 53 / 87 |
| Viajes: pagina 1 de 200 por codigo | 227 / 246 | 100 / 126 |
| Sofasa: pagina 1 de 50 | 48 / 62 | 49 / 49 |
| Sofasa: por estado y ciudad | 18 / 31 | 27 / 32 |
| Usuarios: pagina 1 de 50 | 13 / 15 | 18 / 21 |
| Inventario de un VIN | 38 / 53 | 54 / 72 |
| Informe de una inspeccion | 20 / 25 | 30 / 31 |

Mejoran los listados de vehículos de movilizados (a la tercera parte), de pendientes y de los filtrados por marca y operación (a la mitad o menos), y el de viajes (a la mitad con 200 filas). El de «con novedad» ya era rápido (el `IN` y el `EXISTS` se resuelven igual con 850 hallazgos) y el resto no usaba subconsultas por fila: las diferencias de unos milisegundos son variación de la medición.

## Qué cambió

- **Listado de vehículos.** Los conteos de cada fila (etapas, etapas con informe cerrado y novedades) eran tres subconsultas correlacionadas por vehículo. Ahora se elige la página (solo los datos del vehículo) y después se piden los conteos de **esos** vehículos con una consulta agrupada por tabla
  (etapas, etapas con informe cerrado y hallazgos): tres consultas en total, sin importar el tamaño de la página. El filtro `tipo=novedad` pasó de un `EXISTS` anidado de tres tablas por vehículo a un `IN` con la lista de vehículos con hallazgos, y `estado_unidad` también usa `IN`.
- **Movilizada.** El listado ya no calcula `vwViajesCompletos` sobre las 17 000 unidades: lee `tblViajeVehiculos.Completa`, que guardan los triggers con la misma regla de la vista (ver [viajes.md](viajes.md)). La vista sigue siendo la definición de referencia y la que usan los reportes.
- **Listado de viajes.** Los conteos por viaje (unidades, unidades completas y si tiene personal de apoyo) también salen de consultas agrupadas sobre la página y de `Completa`.
- **Inventario e informe.** Ya se resolvían con una consulta por tabla (etapas con sus lugares y viajes, inspecciones, datos del viaje, hallazgos, evidencias, transferencias y notas): no hay consultas por fila y solo se miden.

## Índices

Los filtros usan los índices de `21-indices-de-filtros-y-completa.sql`: `tblVehiculos (ClienteId, Operacion, Marca, CreatedAt)` y `(Activo)`, `tblOrdenesSofasa (StatusCode, DestinationCity, DatetimeEvent)`, `tblInspecciones (ViajeId, Momento, Estado, Fecha)` y
`tblViajeVehiculos (EstadoUnidad)`. Un filtro de VIN por contenido (un chasis o un fragmento de 6 a 16 caracteres) recorre la tabla porque la búsqueda `LIKE '%texto%'` no puede usar un índice; con 17 000 filas toma unos 130 ms.
