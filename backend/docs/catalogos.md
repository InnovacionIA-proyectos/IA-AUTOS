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
| `tblClasesDano` | Clases de daño de un hallazgo | 10 (`deformacion`, `rotura`, `cortado`, `perforacion`, `golpe`, `mancha`, `raya_pintar`, `desconchado`, `particulas`, `faltante`) |
| `tblTamanosDano` | Tamaños de daño de un hallazgo | 6 (`1_5cm`, `5_10cm`, `10_20cm`, `mas_20cm`, `requiere_reemplazo`, `mas_10_particulas`) |
| `tblNivelesSeveridad` | Niveles V1+, V1, V2, V3 con su `Accion` | 4, **sin acción** (se carga desde el portal) |
| `tblVistasFoto` | Vistas de foto; `Obligatoria` = 1 se toma siempre, 0 solo con faltante o daño; `TipoVehiculo` opcional | 9: 7 obligatorias (`frontal`, `trasera`, `lateral_derecha`, `lateral_izquierda`, `diagonal_trasera_izquierda`, `diagonal_delantera_derecha`, `superior`) y 2 adicionales (`piezas_baul`, `cojineria`) |
| `tblCuadrantes` | Las **zonas** numeradas de la carrocería, por tipo de vehículo (la puerta delantera es una zona aunque se vea desde varias vistas) | 36 de la ficha del vehículo genérico (script 04); los números 4 y 12 no existen en la ficha |
| `tblCuadranteVistas` | El **polígono** de cada zona en cada vista donde aparece (JSON normalizado, una fila por aparición; una zona tiene a lo sumo un polígono por vista) | 97 polígonos en 5 vistas (script 04) |

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
| `POST /api/etapas` recibía `punto_custodia_id` (el proceso) | recibe `etapa_catalogo_id` (obligatorio) y `punto_custodia_id` (el **lugar**, opcional) |
| `clase_dano` y `tamano_dano` validados contra listas fijas | validados contra los valores **activos** de `clases-dano` y `tamanos-dano` |
| Los vehículos nuevos recibían siempre Cargue, Transporte y Descargue | reciben una etapa por cada valor **activo** de `etapas`, en su `orden` |
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

Las acciones de los niveles de severidad se cargan con SQL o desde el portal:

```sql
UPDATE db_IA_AUTOS.tblNivelesSeveridad SET Accion = N'<qué hacer con una unidad V1+>', UpdatedAt = SYSDATETIMEOFFSET() WHERE Codigo = 'v1_plus';
```

Para agregar una zona o su polígono en otra vista desde el portal: `POST /api/catalogos/cuadrantes` (la zona) y `POST /api/catalogos/cuadrante-vistas`
(su polígono en una vista).

La API guarda los catálogos en memoria hasta un minuto (`Catalogos__CacheSegundos`): los cambios hechos directamente en la base pueden tardar ese
tiempo en verse; los hechos por la API se ven al instante.
