# Seguimiento del hallazgo y patrones por zona

El coordinador da seguimiento a cada hallazgo (validado, rechazado, recuperado o derogado) y todo cambio queda en un historial que nunca se modifica. Este
documento describe el modelo de datos; el modelo del propio hallazgo y de su zona esta en [zonas-dano.md](zonas-dano.md).

## Historial de seguimiento

`tblHallazgoSeguimiento` es un registro **append-only**: cada fila es un hecho y no se corrige. Los cambios posteriores se agregan como filas nuevas.

| Columna | Contenido |
|---|---|
| `HallazgoId` | el hallazgo al que pertenece |
| `Accion` | `seguimiento` (el coordinador cambia el estado), `revision` (un inspector revisa una novedad del conductor) o `no_sale` (la unidad no sale del viaje por ese hallazgo) |
| `EstadoAnterior`, `EstadoNuevo` | `pendiente`, `validado`, `rechazado`, `recuperado` o `derogado`; un cambio de seguimiento siempre trae el estado nuevo |
| `UsuarioId` | quien lo hizo |
| `Nota` | hasta 1 000 caracteres |
| `EvidenciaSelloId` | la foto del sello de quien valida (Sofasa), una evidencia de la inspeccion |
| `Detalle` | JSON con lo asignado en una revision (zona, clase y nivel) |

- **Estado del hallazgo.** Al insertar una fila de `seguimiento`, el trigger `TR_tblHallazgoSeguimiento_Sincronizar` pone `tblHallazgos.EstadoSeguimiento` en `EstadoNuevo`
  (si en una misma sentencia llegan varios cambios del mismo hallazgo, queda el ultimo). Las filas de `revision` y de `no_sale` quedan en el historial pero no lo cambian.
- **No se modifica ni se borra.** Un `UPDATE` falla con el error **51005** y un `DELETE` con el **51003** (la misma regla que protege el resto de la evidencia, ver
  [evidencia-inmutable.md](evidencia-inmutable.md)).
- Las reglas de quien puede cambiar el estado, la correspondencia entre el nivel V y el estado de llegada, la evidencia completa y el «no sale» las aplica la API.

## Como lo expone la API

- `POST /api/hallazgos/{id}/seguimiento` agrega una fila `seguimiento` (el trigger pone el estado nuevo en el hallazgo), guarda la foto del sello si la hay y recalcula `EvidenciaCompleta`. `GET /api/hallazgos/{id}/seguimiento` lee el historial.
- La foto del sello es una evidencia de la inspeccion con `EsSello = 1`, sin vista ni hallazgo, que se admite aunque la inspeccion este cerrada (ver [evidencia-inmutable.md](evidencia-inmutable.md)).
- `EvidenciaCompleta` se guarda al cambiar el seguimiento y se recalcula al cerrarse la transferencia de la inspeccion; la API tambien la calcula al leer el hallazgo (`evidencia_faltante`).
- `POST /api/viajes/{id}/vehiculos/{vin}/no-sale` marca la unidad (columnas de `tblViajeVehiculos`) y agrega una fila `no_sale`; la vista `vwViajesCompletos` deja de contar esa unidad como completa.

## Patrones por zona

`vwPatronesPorCuadrante` alimenta el mapa de calor y los patrones. Devuelve una fila por zona, proceso, nivel, clase, cliente, operacion y fecha de cierre, con `Total` (cuantos hallazgos).

- **Cuenta por zona (lado + numero), no solo por numero**: izquierdo 7 y derecho 7 son filas distintas. Una zona provisional (frente, llantas) sale con su nombre.
- `ZonaNombre` ya viene armado para mostrar: «Izquierdo 7 · Puerta delantera», «26 · Aleron / borde trasero del techo» o «Frente».
- **Incluye los hallazgos de toda inspeccion cerrada**, aunque la unidad no haya terminado el viaje. La vista anterior de unidades movilizadas (`vwViajesCompletos`) exige las tres etapas
  cerradas, por eso el mapa de calor salia vacio con hallazgos reales; esta vista no depende de eso.
- **Excluye** las inspecciones en borrador, las novedades del conductor aun sin revisar y los hallazgos todavia sin zona (los historicos pendientes de ubicar).
- Cuenta todas las versiones cerradas de una inspeccion: si una inspeccion cerrada se corrige, sus hallazgos y los de la correccion se suman. Si el negocio quiere contar solo la ultima version, es un cambio de esta vista.
- No incluye la tasa de rechazo: depende de una regla aun sin confirmar y se define aparte.

## Aplicar el cambio en una base existente

Con un usuario que pueda crear tablas, triggers y vistas (no el de la aplicacion), conectado a la base de IA Autos, **antes de desplegar la API**. Ademas del script 11, la foto del sello y la unidad que no sale
necesitan [`14-sello-y-unidad-no-sale.sql`](../database/cambios-esquema/14-sello-y-unidad-no-sale.sql) (una columna de `tblEvidencias`, su restriccion, el trigger de evidencias y la vista `vwViajesCompletos`; no cambia ningun dato y se puede repetir).


1. Ejecutar [`database/cambios-esquema/11-seguimiento-hallazgo.sql`](../database/cambios-esquema/11-seguimiento-hallazgo.sql) ajustando `@esquema` (`dbo`, o `db_IA_AUTOS` en la base
   compartida de pruebas). Es una sola transaccion: si algo falla, no cambia nada. Se puede repetir (si ya esta aplicado, no hace nada). Exige que
   `10-zona-dano-y-hallazgo.sql` ya este aplicado.
2. Comprobar el resultado que muestra el script: 3 triggers del historial, 0 filas del historial y las filas de la vista segun los hallazgos de inspecciones cerradas con zona.

No borra ni cambia ningun dato. `tests/verificar-ddl.sql` prueba el historial (51005 y 51003, las restricciones de cada fila, el estado del hallazgo) y la vista (borrador, zonas por lado, novedad
del conductor sin revisar y revisada).
