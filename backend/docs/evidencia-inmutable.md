# La evidencia no se borra

La evidencia de una inspeccion (fotos, hallazgos, transferencias y el registro de auditoria) no se puede borrar de la base de datos: ni por la
aplicacion, ni por un usuario con acceso directo, ni por un borrado en cascada. Es la ultima defensa; la aplicacion ya no ofrece ningun camino que
borre estos registros.

## Que protege la base

| Tabla | Regla | Error |
|---|---|---|
| `tblEvidencias`, `tblHallazgos`, `tblTransferenciasCustodia`, `tblAuditLog` | Ninguna fila se puede borrar (trigger `INSTEAD OF DELETE`) | 51003 |
| `tblInspecciones` | No se borra si esta cerrada o si tiene fotos, hallazgos o transferencia; un borrador vacio si se borra | 51003 |

Los triggers de las inspecciones cerradas (51000, 51001 y 51002) se conservan: una inspeccion cerrada no se modifica ni admite hallazgos o fotos nuevas
(la foto de una constancia de no firma y la del sello de quien valida un hallazgo si se admiten: `EsConstancia` y `EsSello`).

Cuando la aplicacion recibe el error 51003 responde `409 CONFLICT` con el mensaje «El registro es evidencia y no se puede borrar».

## Sin borrado en cascada

Las relaciones que llevaban `ON DELETE CASCADE` ya no lo tienen, porque una cascada borraba evidencia sin pasar por ninguna regla:

| Relacion | Antes | Ahora |
|---|---|---|
| vehiculo -> etapa, vehiculo -> sus viajes (`tblViajeVehiculos`) | cascada | sin cascada |
| etapa -> inspeccion | cascada | sin cascada |
| inspeccion -> hallazgo, evidencia y transferencia | cascada | sin cascada |
| viaje -> la lista de sus VIN (`tblViajeVehiculos`) | cascada | se conserva |
| cuadrante -> sus regiones | cascada | se conserva |

Borrar un vehiculo, una etapa o una inspeccion que tenga registros debajo falla (error 547): hay que quitar lo de abajo de forma explicita, y lo que es
evidencia no se puede quitar. Un viaje se borra solo si no tiene inspecciones, y en ese caso arrastra la lista de sus VIN.

Un trigger `INSTEAD OF DELETE` no puede existir en una tabla con una relacion en cascada: por eso `ddl.sql` solo crea estos triggers en las tablas que ya no
tienen cascadas (las bases nuevas, o las ya migradas).

## Baja logica del vehiculo

`tblVehiculos.Activo` (bit, 1 por defecto, con indice) permite dar de baja un vehiculo sin perder su historia: queda inactivo y conserva sus etapas,
inspecciones y evidencias. La columna ya existe en la base; el endpoint de baja y su efecto en las busquedas y en los viajes los entrega la tarea de
auditoria y baja logica.

## Aplicar el cambio en una base existente

Con un usuario que pueda alterar tablas y crear triggers (no el de la aplicacion), conectado a la base de IA Autos, **antes de desplegar la API**:

1. Ejecutar [`database/cambios-esquema/09-evidencia-sin-borrado.sql`](../database/cambios-esquema/09-evidencia-sin-borrado.sql) ajustando `@esquema` (`dbo`, o
   `db_IA_AUTOS` en la base compartida de pruebas). Es una sola transaccion: si algo falla, no cambia nada. Se puede repetir (si ya esta aplicado, no hace nada).
   Exige que `08-transferencias-custodia.sql` ya este aplicado y se detiene, sin cambiar nada, si falta alguna de las relaciones esperadas.
2. Comprobar el resultado que muestra el script: 5 triggers, 2 cascadas que se conservan y todos los vehiculos activos.

No borra ni cambia ningun dato: solo cambia relaciones, crea los triggers y agrega la columna `Activo` (todos los vehiculos quedan activos).

Una base nueva se crea con `ddl.sql`, que ya trae el esquema. `tests/verificar-ddl.sql` prueba cada trigger (un caso de error 51003 por tabla, la
inspeccion cerrada, el borrador con fotos, hallazgos o transferencia, y el borrador vacio que si se borra) y que un vehiculo o una etapa con registros no se
borra. Para limpiar sus datos de prueba apaga los triggers solo mientras borra y los vuelve a encender, aunque falle.
