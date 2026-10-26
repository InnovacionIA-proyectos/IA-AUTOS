# Zona del dano y hallazgo

Un hallazgo se atribuye a una **zona** del vehiculo, con su clase de dano, su tamano y su nivel de severidad (V), en lugar de una parte en texto libre con un
punto sobre una sola imagen. Este documento describe el modelo de datos y el mapeo de lo que ya existia. Los endpoints que lo exponen estan en
[CONTRATO.md](../CONTRATO.md).

## La zona

Una zona es una de estas dos cosas:

| Tipo | Como se identifica | Dibujo |
|---|---|---|
| Zona numerada | cuadrante (numero) + lado | tiene region en uno o varios paneles |
| Zona provisional | codigo: `frente`, `llanta_delantera_izquierda`, `llanta_delantera_derecha`, `llanta_trasera_izquierda`, `llanta_trasera_derecha` | sin region: se elige de una lista |

- Los numeros **1 a 25** llevan lado (`izquierdo` o `derecho`); del **26 en adelante** no llevan lado. La base lo garantiza (`CK_tblZonasDano_Forma`).
- La zona es unica por cuadrante y lado, y el hallazgo la guarda por **lado + numero**, no por el panel donde se toco: «izquierdo 7» es la misma zona se toque donde se toque.
- Los numeros 4 y 12 no existen hasta que el negocio los defina. Las zonas provisionales no tienen numero y, el dia que el negocio les de uno, se migran.
- Hay una fila por zona en `tblZonasDano`. Cada cuadrante nuevo trae sus zonas: el trigger `TR_tblCuadrantes_CrearZonas` crea dos (una por lado) del 1 al 25 y una del 26 en adelante;
  al borrar o renumerar un cuadrante sus zonas lo siguen. Una zona con hallazgos no se borra.
- `tblCuadrantes` conserva `Numero`, `Nombre` y `TipoVehiculo` (36 zonas numeradas en la ficha del vehiculo generico). Los nombres son una lectura de la imagen y los valida calidad.

## Paneles y regiones

El dibujo de danos son **5 paneles** de la ficha, separados del catalogo de vistas de foto:

| Panel (`tblPanelesDano.Codigo`) | Lado de sus regiones | Vista de foto de la que salen los poligonos ya cargados |
|---|---|---|
| `superior` | sin lado | `superior` |
| `costado_izquierdo` | izquierdo | `lateral_izquierda` |
| `costado_derecho` | derecho | `lateral_derecha` |
| `perfil_posterior_izquierdo` | izquierdo | `diagonal_trasera_izquierda` |
| `perfil_posterior_derecho` | derecho | `diagonal_delantera_derecha` |

- Cada panel tiene su imagen de fondo (`ImagenPath`, el archivo en el almacen de imagenes). Es nula hasta que se carga la imagen de la ficha.
- Una **region** (`tblRegionesDano`) es el poligono de una zona numerada en un panel, con geometria normalizada `{"puntos":[[x,y],...]}` (x e y entre 0 y 1 sobre la imagen del panel).
  Una zona se dibuja a lo sumo una vez por panel; una zona provisional no tiene region (lo garantiza la base).
- En los costados y perfiles el lado de la region sale del panel. En el panel superior cada numero del 1 al 25 necesita **dos** regiones (una por lado, segun la mitad de la imagen).

### Migracion de los poligonos ya cargados

Los 97 poligonos de `tblCuadranteVistas` pasan a `tblRegionesDano` con la correspondencia de la tabla anterior (`tblCuadranteVistas` no se modifica y queda como respaldo). Resultado con la ficha actual:

| Panel | Regiones |
|---|---|
| `superior` | 13 (numeros 26 a 38) |
| `costado_izquierdo` | 19 |
| `costado_derecho` | 19 |
| `perfil_posterior_izquierdo` | 23 |
| `perfil_posterior_derecho` | 23 |

**Revision manual del panel superior.** Hoy el panel superior solo tiene dibujados los numeros 26 a 38, asi que no hay nada que partir por lado. Si en el futuro se carga un poligono del panel superior
con un numero del 1 al 25, el script de migracion no lo asigna a un lado por si solo: lo deja sin migrar y lo lista en su reporte final para dibujar sus dos regiones a mano.

## El hallazgo

`tblHallazgos` guarda: `ZonaId`, `ClaseDano`, `TamanoDano`, `NivelSeveridad` (nivel V), `TipoNovedad`, `Origen`, `Nota`, `EsPreexistente`, `HallazgoOrigenId`, `ConfirmadoPor`,
`EstadoSeguimiento`, `EvidenciaCompleta`, `RevisadoPor`, `RevisadoAt` y `MigracionPendiente`.

| Campo | Regla |
|---|---|
| `Origen` | `inspector` (por defecto) o `conductor` |
| `ClaseDano` | solo puede faltar en una novedad del conductor sin revisar |
| `NivelSeveridad` | codigo de `tblNivelesSeveridad`; el conductor nunca lo asigna, lo asigna el inspector. Nulo en una novedad sin revisar y en los hallazgos historicos |
| `TipoNovedad` | solo del conductor: `golpe`, `piedra`, `granizo` o `incidente_vial` |
| `EsPreexistente` + `HallazgoOrigenId` | un hallazgo preexistente siempre apunta al hallazgo previo del mismo VIN que ya lo tenia |
| `EstadoSeguimiento` | `pendiente` (por defecto), `validado`, `rechazado`, `recuperado` o `derogado` |
| `EvidenciaCompleta` | se recalcula al cambiar el seguimiento o la transferencia |
| `ParteVehiculo`, `CoordsDiagrama` | del modelo anterior: ahora admiten nulo y solo quedan en los hallazgos historicos |

Las reglas que combinan varias tablas o dependen del usuario (zona obligatoria para el inspector, nivel obligatorio, que el nivel y la zona sean coherentes) las aplica la API al crear el hallazgo; la base solo
garantiza la forma de cada dato y las relaciones.

### Migracion de los hallazgos historicos

Los hallazgos anteriores no tenian zona ni nivel. Se ubican por el **texto** de `ParteVehiculo` solo cuando la equivalencia es segura:

| `ParteVehiculo` (texto de la app anterior) | Zona |
|---|---|
| `Fachada delantera`, `Parachoque delantero` | `frente` |
| `Llantas/rines delantero izquierdo` / `delantero derecho` | `llanta_delantera_izquierda` / `llanta_delantera_derecha` |
| `Llantas/rines trasero izquierdo` / `trasero derecho` | `llanta_trasera_izquierda` / `llanta_trasera_derecha` |

Las coordenadas del modelo anterior eran sobre una sola imagen distinta a los paneles nuevos y no hay una equivalencia imagen anterior a region, asi que **no** se usan para ubicar un hallazgo. Los demas
(techo, capot, parabrisas, stop, puertas, guardafangos...) quedan con zona nula y `MigracionPendiente = 1`; conservan su parte y sus coordenadas, y el script los lista en su reporte para ubicarlos a mano.
Todos quedan con `Origen = inspector`, `EstadoSeguimiento = pendiente` y sin nivel.

## Indices de consulta del hallazgo

| Indice | Columnas | Lo usan |
|---|---|---|
| `IX_tblHallazgos_Zona_Clase` | `ZonaId`, `ClaseDano`, con `InspeccionId` incluida | la propuesta de hallazgo preexistente (misma zona y clase en el VIN) y el mapa de calor |
| `IX_tblHallazgos_Seguimiento` | `EstadoSeguimiento`, `Origen`, `RevisadoAt` | la bandeja del coordinador y la de las novedades del conductor por revisar |

Reemplazan a `IX_tblHallazgos_Parte_Clase` (parte del vehiculo y clase, del modelo anterior), que se quita. Los indices de las demas tablas son de las tareas que las crean.

## La unidad que no sale

`tblViajeVehiculos` gana `EstadoUnidad` (`activa` por defecto o `no_sale`), `NoSaleMotivo` (hasta 500 caracteres), `NoSaleHallazgoId`, `NoSaleMarcadoPor`, `NoSaleAt` y `ReemplazoVehiculoId`.
Una unidad que no sale exige su motivo, el hallazgo que lo causa, quien la marco y cuando; una unidad activa no lleva ninguno de esos datos. El vinculo del vehiculo con el viaje no se borra, para que la unidad
conserve su trazabilidad cuando vuelva a otro viaje. Las reglas de quien puede marcarla y como cuenta en el viaje las aplica la API.

## Cómo lo expone la API

- `POST /api/inspecciones/{id}/hallazgos` recibe `cuadrante_numero` + `lado` (o `zona_provisional`), `clase_dano`, `tamano_dano`, `nivel_severidad`, `es_preexistente` + `hallazgo_origen_id` y `nota`. La API resuelve la zona
  con el cuadrante del tipo de vehículo del VIN (o el genérico si no hay uno propio), valida el lado por número (obligatorio del 1 al 25, prohibido del 26 en adelante), exige el nivel al inspector y al administrador
  (el conductor no lo envía y su novedad queda sin nivel) y comprueba que un hallazgo preexistente apunte a un hallazgo de una **inspección cerrada anterior del mismo VIN** con la **misma zona y clase**.
- `GET /api/inspecciones/{id}/hallazgos-previos` entrega esa lista (con el viaje, el proceso, el momento, el cierre y las fotos de cada hallazgo) para que la app proponga el preexistente comparando zona y clase.
- `GET /api/catalogos` publica `paneles-dano` (con la URL de su imagen) y `cuadrante-regiones`, y las zonas provisionales dentro de `cuadrantes` (con su `codigo`, sin número ni región). `cuadrante-vistas` ya no existe y responde `404` como cualquier nombre desconocido.
- El texto de la zona para mostrar se arma en la API: «Izquierdo 7 · Puerta delantera», «Techo 30», «Frente».
- El inventario y el detalle del viaje traen `estado_unidad` y `no_sale` por unidad (la columna del viaje que dejó la migración 10). Marcar una unidad como «no sale» es de otra tarea.
- Los reportes de patrones cuentan por la zona en texto; un hallazgo histórico sin ubicar cuenta por la parte que se escribió entonces.

El detalle de los campos está en [CONTRATO.md](../CONTRATO.md) («Hallazgo por región» y «Paneles de daño»).

## Aplicar el cambio en una base existente

Con un usuario que pueda alterar tablas y crear triggers (no el de la aplicacion), conectado a la base de IA Autos, **antes de desplegar la API**:

1. Ejecutar [`database/cambios-esquema/10-zona-dano-y-hallazgo.sql`](../database/cambios-esquema/10-zona-dano-y-hallazgo.sql) ajustando `@esquema` (`dbo`, o `db_IA_AUTOS` en la base compartida de pruebas).
   Es una sola transaccion: si algo falla, no cambia nada. Se puede repetir (si ya esta aplicado, no hace nada). Exige que `09-evidencia-sin-borrado.sql` ya este aplicado.
2. Revisar el reporte que muestra el script: paneles, zonas, regiones migradas frente a los poligonos de origen, poligonos sin migrar, hallazgos con zona y hallazgos pendientes de ubicar (con su lista).

No borra ni cambia ningun dato: crea las tablas nuevas, agrega columnas con valores por defecto y copia los poligonos. `tests/verificar-ddl.sql` comprueba las reglas de las zonas, las regiones, el hallazgo y la unidad del viaje.
