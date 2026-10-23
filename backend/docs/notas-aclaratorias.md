# Notas aclaratorias

Una inspección cerrada, un hallazgo de ella o su transferencia **no se modifican**: son evidencia. Para aclarar algo después de cerrado se agrega una **nota aclaratoria**, un registro nuevo con su autor y su fecha. El original no cambia y nadie edita ni borra una nota (no hay `PATCH` ni `DELETE`, y
la base lo impide con triggers). Las rutas y los cuerpos están en [CONTRATO.md](../CONTRATO.md) («Notas aclaratorias»).

## Qué se puede aclarar

| Ruta | Aclara |
|---|---|
| `POST /api/inspecciones/{id}/notas` | la inspección |
| `POST /api/hallazgos/{id}/notas` | un hallazgo |
| `POST /api/inspecciones/{id}/transferencia/notas` | la transferencia de la inspección |

El cuerpo es `{ "texto": "…" }` (obligatorio, hasta 1000 caracteres). Cada lista se lee con el mismo camino con `GET`, de la más antigua a la más reciente, y las notas también vienen en `notas_aclaratorias[]` del informe, de cada inspección del inventario, del detalle del hallazgo y de la
transferencia.

## Quién agrega

El **autor del registro** (el usuario de la inspección; para un hallazgo, el de su inspección; para la transferencia, quien la firmó), el `admin`, el `super_admin` y el `coordinador_calidad` de su cliente (esta última, propuesta pendiente de confirmar por el negocio). Cualquier otro rol recibe `403`, y también
con un registro de un VIN de otro cliente.

## Reglas que conviene recordar

- **Se puede con la inspección cerrada**: es justamente su uso. Lo que no se permite es cambiar lo que ya está.
- Cada nota queda en la [auditoría](auditoria.md) (`nota.crear`).
- El conductor no ve las notas de los hallazgos que registraron otros (ver [conductor-en-ruta.md](conductor-en-ruta.md)).
- Las notas aparecen en el PDF del informe.

## Código y pruebas

`clsNotasAclaratoriasService`, `clsConsultasNotas` y la tabla `tblNotasAclaratorias` ([`19-notas-aclaratorias.sql`](../database/cambios-esquema/19-notas-aclaratorias.sql); ver también [evidencia-inmutable.md](evidencia-inmutable.md)). Pruebas: `NotasAclaratoriasApiTests` y `NotasAclaratoriasTests`.
