# Semilla de desarrollo

Datos de prueba para que frontend (y quien desarrolle) vea y pruebe inventario, inspecciones, transferencias, hallazgos, avisos y auditoría **sin la app móvil**. Son scripts de SQL Server en `database/semilla-desarrollo/`.
Cubren S5-HU-31.5 (#123), S6-HU-09.18 (#174) y S6-HU-01.12 (#194).

**Solo para desarrollo y pruebas. No se ejecuta contra QA ni producción** (allí no hay semilla): los scripts no tienen ninguna protección contra eso, por lo que la responsabilidad es de quien los ejecuta.

## Orden

| Script | Qué hace |
|---|---|
| `00-limpiar-semilla.sql` | Borra todo lo sembrado y nada más (ver «Limpieza»). Se puede repetir. |
| `01-semilla-base.sql` | (#123) 5 usuarios de `sofasa`, 4 lugares, 10 vehículos y un viaje **finalizado** (`SEED-MTK-0001`) de 3 unidades con sus 3 etapas: 18 inspecciones cerradas (salida y llegada), 108 fotos de vistas (las 9, con nota en baúl y cojinería) y 9 transferencias (7 firmadas, 1 con reservas, 1 con constancia de no firma, con testigo y foto). |
| `02-semilla-ampliacion.sql` | (#174) Segundo cliente `seed_prueba` con sus 5 usuarios y 4 vehículos; un conductor con **cambio de PIN pendiente** y otro **bloqueado**; viajes en los 4 estados (`SEED-MTK-0002` en curso con VIN de los dos clientes, `-0003` programado, `-0004` cancelado), un **Cargue sin personal de apoyo** (`-0005`: salida del conductor con su recibo sin entrega), una **inspección corregida** (`-0006`: versión 1 y 2), fotos **sin ubicación**, inspecciones de Transporte sin vistas, una **transferencia abierta** que bloquea la salida siguiente y órdenes Sofasa vinculadas por código MTrack. |
| `03-semilla-hallazgos.sql` | (#194) Hallazgos de los 4 niveles con lado + número (la pareja izquierdo 7 / derecho 7, zonas centrales y una llanta), preexistente confirmado, un hallazgo en cada estado de seguimiento (validado con foto del sello, rechazado con evidencia completa y con evidencia incompleta, recuperado y derogado sobre un V3), una unidad que **no sale** con reemplazo (`-0007`) y ese mismo VIN en un **viaje posterior** (`-0008`), novedades del conductor sin revisar y revisada, 15 avisos de cada tipo y urgencia (leídos, sin leer y uno con respuesta), notas aclaratorias y registros de auditoría con detalle. Los parámetros de las alertas ya vienen con sus valores del `ddl.sql`. |

Cada parte exige la anterior y se detiene si ya está cargada (errores 50010 a 50017): para volver a empezar, ejecute primero `00-limpiar-semilla.sql`.

## Cómo ejecutarla

1. Aplique antes `ddl.sql` y los scripts de `cambios-esquema/` hasta el 34 como mínimo (la semilla necesita las zonas del dibujo).
2. Conéctese a la base de pruebas con un usuario **propietario** (no el usuario de la aplicación) y ejecute cada script completo, **en una sola ejecución de script**, en el orden de la tabla.
3. Los scripts usan el esquema `db_IA_AUTOS` (la base compartida de pruebas). En una base creada con `ddl.sql` el esquema es `dbo`: reemplace `db_IA_AUTOS.` por `dbo.` en todo el archivo.

Cada script es una sola transacción (si algo falla no queda nada) y termina con una consulta de resultado con los conteos esperados. Desactivan los triggers de la evidencia inmutable **solo dentro de la transacción** (necesario para insertar inspecciones ya cerradas) y los dejan activos.

## Acceso de prueba

Cuentas inventadas, sin datos reales (ni nombres, ni cédulas, ni placas):

- Administrador, coordinador y comercial entran con su correo y la clave `Seed-Dev-2026!`: `seed.sof_admin@seed.iaautos.test`, `seed.sof_coord@…`, `seed.sof_comercial@…` y, para el cliente de prueba, `seed.pru_admin@…`, `seed.pru_coord@…`, `seed.pru_comercial@…`.
- Inspector y conductor entran con su cédula y el PIN `135790`: inspector `90000001`, conductor `90000002` (sofasa); inspector `90000011`, conductor `90000012` (cliente de prueba); conductor con PIN pendiente `90000003`; conductor bloqueado `90000004`.

Las fotos apuntan a rutas `seed/...` del almacén de archivos: si no existen allí, la foto se ve como no disponible (la fila y su sello sí están).

## Limpieza

`00-limpiar-semilla.sql` parte de los prefijos y borra lo que cuelga de ellos: vehículos con VIN `SEED…`, usuarios con correo `@seed.iaautos.test`, viajes con código `SEED-…`, lugares `SEED …` y el cliente `seed_prueba`; de ahí, sus etapas, inspecciones, fotos, hallazgos, seguimientos, transferencias, notas, avisos, auditoría y órdenes Sofasa. Los datos reales no se tocan.

## Pruebas

`SemillaDeDesarrolloTests` (en `tests/IA-AUTOS.Tests.Integracion/Infraestructura/`) carga las tres partes sobre una base con `ddl.sql` y el script 34, comprueba lo que pide cada Issue, que no se pueden cargar dos veces, y que la limpieza deja todo en cero, los triggers activos y **sin tocar** un vehículo, un usuario y un lugar «reales» sembrados aparte.
