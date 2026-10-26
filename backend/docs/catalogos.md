# Catálogos maestros: modelo y mapeo del modelo anterior al nuevo

Las listas que antes estaban fijas en el código (puntos de custodia, clases y tamaños de daño) y las que faltaban (niveles de severidad,
vistas de foto, cuadrantes, tipos de unidad) viven ahora en tablas y se administran desde el portal con la API
(`/api/catalogos`, ver [CONTRATO.md](../CONTRATO.md#catálogos-maestros)). Cambiarlas no exige desplegar código.

## Tablas

| Tabla | Contenido | Valores iniciales |
|---|---|---|
| `tblEtapasCatalogo` | Etapas del proceso. Es el `proceso` de cada tramo de la ruta de un vehículo | `cargue`, `transporte`, `descargue` |
| `tblPuntosCustodia` | **Lugares** físicos donde se hace una etapa (la tabla se rehízo) | `cedi_1`, `origen`, `certificadora`, `destino`, `cedi_2`, `concesionario` |
| `tblTiposUnidadTransportadora` | Tipos de unidad transportadora | `ninera`, `nodriza`, `quintinaria`, `planchon_grua` |
| `tblClientes` | Clientes (empresas) a los que pertenecen los usuarios y los vehículos (ver [identidad.md](identidad.md)) | `sofasa` |
| `tblClasesDano` | Clases de daño de un hallazgo | 10 (`deformacion`, `rotura`, `cortado`, `perforacion`, `golpe`, `mancha`, `raya_pintar`, `desconchado`, `particulas`, `faltante`) |
| `tblTamanosDano` | Tamaños de daño de un hallazgo | 6 (`1_5cm`, `5_10cm`, `10_20cm`, `mas_20cm`, `requiere_reemplazo`, `mas_10_particulas`) |
| `tblEstadosSofasa` | Estados de las órdenes Sofasa: `Codigo` es el `StatusCode` **real** de la orden (texto libre, con sus mayúsculas, hasta 100 caracteres, único sin distinguir mayúsculas) y `Nombre` su etiqueta. Sin llave foránea desde `tblOrdenesSofasa` (la ingesta del ETL no debe fallar por un estado nuevo): «en uso» se valida en la API | los `StatusCode` distintos de las órdenes existentes, con el código como etiqueta provisional; vacío si aún no hay órdenes |
| `tblNivelesSeveridad` | Niveles V1+, V1, V2, V3 con su `Accion` | 4, con su acción: «Se recupera antes del despacho», «Se rechaza el vehículo», «Validación por Sofasa» y «Se deroga (se acepta el defecto)» (script 13) |
| `tblVistasFoto` | Vistas de foto; `Obligatoria` = 1 se toma siempre, 0 solo con faltante o daño; `TipoVehiculo` opcional | 9: 7 obligatorias (`frontal`, `trasera`, `lateral_derecha`, `lateral_izquierda`, `diagonal_trasera_izquierda`, `diagonal_delantera_derecha`, `superior`) y 2 adicionales (`piezas_baul`, `cojineria`) |
| `tblCuadrantes` | Las **zonas** numeradas de la carrocería, por tipo de vehículo (la puerta delantera es una zona aunque se vea desde varias vistas) | 36 de la ficha del vehículo genérico (script 04); los números 4 y 12 no existen en la ficha |
| `tblCuadranteVistas` | El polígono de cada zona por vista de foto, **del modelo anterior**: la API ya no lo publica (lo reemplazan `tblPanelesDano` y `tblRegionesDano`, ver [zonas-dano.md](zonas-dano.md)) y queda como respaldo | 97 polígonos en 5 vistas (script 04) |

Todos comparten `Codigo` (clave estable, única, en minúsculas), `Nombre`, `Orden`, `Activo`, `CreatedAt` y `UpdatedAt`. Un valor **en uso no se
borra** (la base lo impide con una llave foránea y la API responde `409`): se inactiva con `Activo = 0`.

## Mapeo de los 7 valores anteriores

Antes, `tblPuntosCustodia` mezclaba etapas y lugares en una sola lista de 7 valores y `tblEtapas` apuntaba a ella como «proceso». Ahora se separan:

| Valor anterior | Ahora es | Dónde queda |
|---|---|---|
| `cargue` | **etapa** | `tblEtapasCatalogo` `cargue` |
| `transporte` | **etapa** | `tblEtapasCatalogo` `transporte` |
| `descargue` | **etapa** | `tblEtapasCatalogo` `descargue` |
| `punto_origen` | **lugar** | `tblPuntosCustodia` `origen` (Origen) |
| `certificadora` | **lugar** | `tblPuntosCustodia` `certificadora` (Certificadora) |
| `punto_destino` | **lugar** | `tblPuntosCustodia` `destino` (Destino) |
| `concesionario` | **lugar** | `tblPuntosCustodia` `concesionario` (Concesionario) |
| *(nuevo)* | lugar | `tblPuntosCustodia` `cedi_1` (CEDI 1) y `cedi_2` (CEDI 2) |

## Cómo se guardan las referencias

| Tabla | Antes | Ahora |
|---|---|---|
| `tblEtapas` | `PuntoCustodiaId` (NOT NULL): el «proceso» | `EtapaCatalogoId` (NOT NULL, FK a `tblEtapasCatalogo`) y `PuntoCustodiaId` (**NULL**, FK al lugar) |
| `tblHallazgos` | `ClaseDano` y `TamanoDano` con `CHECK` de valores fijos | mismas columnas (guardan el **código**) con `FK` a `tblClasesDano(Codigo)` y `tblTamanosDano(Codigo)` |

Los datos existentes migran sin pérdida: cada etapa conserva su proceso (en la base de pruebas todas las etapas son `cargue`, `transporte` o
`descargue`) y queda sin lugar hasta que se asigne; los hallazgos no cambian porque ya guardaban el código.

## Qué cambia en la API

| Antes | Ahora |
|---|---|
| `proceso` salía de los 7 puntos de custodia | `proceso` es el `codigo` de una etapa del catálogo (`cargue`, `transporte`, `descargue`); el filtro `proceso` de los informes valida contra `etapas` |
| `POST /api/etapas` recibía `punto_custodia_id` (el proceso) | recibía `etapa_catalogo_id` y `punto_custodia_id` (el **lugar**); ya no existe: las etapas nacen al agregar un VIN a un viaje (ver [viajes.md](viajes.md)) |
| `clase_dano` y `tamano_dano` validados contra listas fijas | validados contra los valores **activos** de `clases-dano` y `tamanos-dano` |
| Los vehículos nuevos recibían siempre Cargue, Transporte y Descargue | cada VIN que entra a un viaje recibe una etapa por cada valor **activo** de `etapas`, en su `orden` |
| inventario e informe | agregan `lugar` (nombre del lugar o `null`) |

## Aplicar el cambio en una base existente

Con un usuario que pueda crear tablas (no el de la aplicación), conectado a la base de IA Autos:

1. Ejecutar [`database/cambios-esquema/02-catalogos-maestros.sql`](../database/cambios-esquema/02-catalogos-maestros.sql) ajustando `@esquema`
   (`dbo`, o `db_IA_AUTOS` en la base compartida de pruebas). Es una sola transacción y se puede repetir: si ya está migrada no hace nada. Aborta
   sin cambiar nada si alguna etapa usa uno de los 4 lugares antiguos.
   Si la base ya tenía los catálogos con un cuadrante por vista, ejecutar después [`03-cuadrantes-por-zona.sql`](../database/cambios-esquema/03-cuadrantes-por-zona.sql)
   (separa la zona de su polígono por vista; también se puede repetir).
2. Dar permisos al usuario de la aplicación sobre las tablas nuevas si no los hereda por esquema.
3. Desplegar la API **después** de la migración (la API nueva espera las tablas nuevas).

Una base nueva se crea con `ddl.sql`, que ya trae el esquema y las semillas. `tests/verificar-ddl.sql` comprueba las semillas, las llaves foráneas y que un
valor en uso no se borra.

## Estados de las órdenes Sofasa

`tblEstadosSofasa` la crea [`22-catalogo-estados-sofasa.sql`](../database/cambios-esquema/22-catalogo-estados-sofasa.sql) (ajustando `@esquema`, en una sola ejecución, una transacción, repetible) y la llena con los `StatusCode` distintos que ya tienen las
órdenes (sin distinguir mayúsculas ni espacios de los bordes), con el código como etiqueta provisional. **La etiqueta de cada estado la define Calidad/Operaciones (PAB-032)** y se cambia desde el portal (`PATCH /api/catalogos/estados-sofasa/{id}` con `nombre`). Mientras no haya
órdenes el catálogo queda vacío; cuando lleguen, se vuelve a ejecutar el script para agregar los estados que falten (no cambia una etiqueta ya editada) o se agregan desde el portal. Resultado esperado: `Tabla = 1` y `SinEstado = 0`.

## Motivos por los que no está el inspector

`tblMotivosSinInspector` (`Codigo`, `Nombre`, `RequiereTexto`, `Orden`, `Activo`) la crea [`23-novedad-de-proceso.sql`](../database/cambios-esquema/23-novedad-de-proceso.sql) con tres valores iniciales: `inspector_no_llego` «El inspector no llegó», `inspector_no_disponible` «El inspector no está disponible» y `otro` «Otro» (`RequiereTexto = 1`: quien lo elige escribe cuál es). **Calidad confirma los valores** y se editan como los demás catálogos. Repetir el script no toca los que ya existen. El script también agrega `MotivoSinInspector` y `MotivoSinInspectorTexto` a `tblInspecciones`, la tabla `tblNovedadesProceso` (ver [evidencia-inmutable.md](evidencia-inmutable.md)), el tipo de notificación `novedad_proceso` y el cierre `recibo_sin_entrega` de la transferencia (ver [transferencias.md](transferencias.md)). Resultado esperado: `Tablas = 2`, `Motivos = 3`, `Triggers = 3`, `ColumnasInspeccion = 2`, `CierreNuevo = 1`, `EntregaNula = 1`, `TipoNotificacion = 1` y `NovedadesProceso = 0`.

## Cargar los cuadrantes y las acciones de los niveles

Los cuadrantes de la ficha del vehículo genérico (36 zonas y 97 polígonos) se cargan con
[`database/cambios-esquema/04-cargar-cuadrantes-ficha.sql`](../database/cambios-esquema/04-cargar-cuadrantes-ficha.sql), que se ejecuta después
del 03 y se puede repetir: lo que ya existe no se duplica ni se pisa. Los puntos son proporciones (0 a 1) de la imagen de cada vista.

| Vista de la ficha | Vista de foto |
|---|---|
| Vista superior | `superior` |
| Costado lateral izquierdo | `lateral_izquierda` |
| Costado lateral derecho | `lateral_derecha` |
| Perfil posterior izquierdo | `diagonal_trasera_izquierda` |
| Perfil posterior derecho | `diagonal_delantera_derecha` |

Pendiente de validar por calidad: los **nombres** de las zonas (la ficha solo trae el número; se corrigen desde el portal con
`PATCH /api/catalogos/cuadrantes/{id}`), el 15 (se dibujó como «Manija de puerta trasera») y el lado del capó y del techo (en la ficha, el lado
de arriba de la vista superior es el **derecho** del vehículo, porque el frente apunta a la izquierda). Las vistas `frontal` y `trasera` no tienen
cuadrantes dibujados: un golpe en el parachoques delantero no tiene zona a la cual asociarse hasta que se dibujen.

Las acciones de los niveles de severidad las carga [`13-acciones-niveles-severidad.sql`](../database/cambios-esquema/13-acciones-niveles-severidad.sql) (solo donde aún no hay una: no pisa lo editado); para
cambiar una después, desde el portal (`PATCH /api/catalogos/niveles-severidad/{id}` con `accion`) o con SQL:

```sql
UPDATE db_IA_AUTOS.tblNivelesSeveridad SET Accion = N'<qué hacer con una unidad V1+>', UpdatedAt = SYSDATETIMEOFFSET() WHERE Codigo = 'v1_plus';
```

Para agregar una zona o su región en otro panel desde el portal: `POST /api/catalogos/cuadrantes` (la zona) y `POST /api/catalogos/cuadrante-regiones`
(su región en un panel: `panel`, `cuadrante_id`, `lado` si el panel es el superior y `geometria`). La imagen de fondo de un panel se cambia con `PUT /api/catalogos/paneles-dano/{codigo}/imagen`.

La API guarda los catálogos en memoria hasta un minuto (`Catalogos__CacheSegundos`): los cambios hechos directamente en la base pueden tardar ese
tiempo en verse; los hechos por la API se ven al instante.
