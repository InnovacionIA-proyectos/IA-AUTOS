# Arquitectura

## Capas

Clean Architecture de cuatro capas con monolito modular. Un solo despliegue de la API; sin microservicios ni CQRS general.

```
API  ->  Aplicacion  ->  Dominio
Infraestructura  ->  Aplicacion y Dominio      (la API enlaza la infraestructura solo para componer las dependencias)
```

| Capa | Responsabilidad | Contenido |
|---|---|---|
| Dominio | Reglas de negocio sin tecnologia | entidades `cls*`, catalogos, excepciones, interfaces de repositorios |
| Aplicacion | Casos de uso | servicios por modulo, DTO, validadores, puertos (`IConsultas*`, `IAlmacenArchivos`, `IGeneradorPdfInventario`) |
| Infraestructura | Detalles tecnicos | EF Core sobre SQL Server, repositorios, consultas de lectura, S3/disco, JWT, hash de contrasenas, PDF |
| API | Entrada HTTP | controladores, autenticacion, errores, CORS, limite de intentos, OpenAPI |

## Modulos

Auth, Usuarios, Vehiculos (carga masiva, listados, inventario, PDF), Viajes (alta, asignacion de VIN, estados, carga por Excel, visibilidad), Inspecciones (informes, hallazgos, evidencias, cierre, custodia), Conductor (novedades y llegada en ruta), Notificaciones (bandeja, dispositivos y envio push por FCM), Archivos, Sofasa (ingesta y consulta), Reportes (patrones, analisis, tasa de rechazo) y Auditoria (transversal).

Cada capa se organiza por grupo funcional (por ejemplo `Aplicacion/Inspecciones/{DTOInspecciones, Validators}`), no por tipo tecnico.

## Convenciones

Clases con prefijo `cls`, variables en camelCase, constantes en MAYUSCULAS_SOSTENIDAS, tablas con prefijo `tbl` y columnas en PascalCase. Los DTO y las interfaces no llevan prefijo `cls`. Los proyectos se llaman `IA-AUTOS.*` y el espacio de nombres es `IAAutos.*` (el guion no es valido en C#).

## Reglas de negocio en el dominio

- Una inspeccion cerrada es inmutable: no admite hallazgos ni evidencias. El cierre exige al menos una foto si hay hallazgos y que toda foto este geolocalizada. Cerrarla dos veces responde 409.
- La transferencia de custodia la firma cada parte por su cuenta: la identidad sale del token y nadie firma por otro; cada firma guarda su fecha e IP, y los firmantes son del viaje. Se cierra como firmada, con reservas o con constancia de no firma (motivo, testigo y foto); cerrada es inmutable (repetir exactamente la misma firma es idempotente) y, sin tipo de cierre, bloquea la salida de la etapa siguiente. Se puede firmar despues del cierre de la inspeccion. Detalle en [transferencias.md](transferencias.md).
- El viaje es el eje: cada etapa e inspeccion cuelga de un viaje, y la inspeccion toma de el la placa, el trailer y el conductor. Inspectores y conductores solo ven (y abren) los VIN, viajes e informes de los viajes en los que participan; el resto de roles ve todo. Un VIN no puede estar en dos viajes activos a la vez.
- Un vehiculo esta movilizado cuando completa su ruta en un viaje: todas sus etapas de ese viaje (y al menos una) tienen un informe cerrado. La definicion vive en la vista `vwViajesCompletos` (una fila por VIN y viaje) y la usan los listados y todos los reportes.
- La tasa de rechazo es el porcentaje de unidades movilizadas con al menos un hallazgo, por operacion, frente a la linea base del PRD (importacion 8,81; exportacion 8,34; nacional 1,95).
- La carga masiva de VIN y la ingesta de Sofasa son idempotentes: re-procesar no duplica vehiculos, y un error en una fila no detiene el lote. Las etapas no se crean ahi: nacen al agregar el VIN a un viaje. La ingesta vincula cada orden al viaje del VIN (y del codigo MTrack, si lo trae).
- Baja de usuario: se elimina si no tiene historial; si lo tiene, se marca como retirado.
- Inspecciones: por etapa hay una inspeccion de salida y una de llegada, y cada foto lleva su sello (el del dispositivo y el del servidor) y su hash. Detalle en [inspecciones.md](inspecciones.md) y, para la transferencia de custodia, en [transferencias.md](transferencias.md).
- Hallazgos: cada hallazgo se atribuye a una zona del vehiculo (cuadrante + lado, o una zona provisional) con su clase, tamano y nivel de severidad; una unidad con un hallazgo grave puede no salir del viaje sin bloquearlo. Modelo y mapeo en [zonas-dano.md](zonas-dano.md); su historial de seguimiento (que nunca se modifica) y la vista de patrones por zona, en [seguimiento-hallazgos.md](seguimiento-hallazgos.md).
- Identidad: un usuario entra con correo y contrasena o con cedula y PIN (conductor e inspector, el correo es opcional). Cada usuario y cada vehiculo pertenecen a un cliente (catalogo `clientes`); el super administrador no tiene cliente. Detalle en [identidad.md](identidad.md).

## Base de datos

El esquema lo crea `database/ddl.sql` (no se usan migraciones de EF). Ademas de las tablas incluye la vista de viajes completos y triggers de ultima defensa: no se puede modificar una inspeccion cerrada ni agregarle hallazgos o evidencias, y la evidencia no se puede borrar (ver [evidencia-inmutable.md](evidencia-inmutable.md)). Las relaciones entre vehiculo, etapa, inspeccion y sus registros no borran en cascada. `tblEvidencias.HallazgoId` usa `NO ACTION` (la aplicacion no elimina hallazgos).

## Auditoria

Toda accion relevante se registra en `tblAuditLog` dentro de la misma transaccion del caso de uso (login, altas y cambios de usuarios, carga de VIN, viajes y su carga por Excel, inspecciones, hallazgos, evidencias, firmas, cierres). La ingesta del ETL se registra como accion del sistema (usuario nulo). Los logs estructurados con correlacion de solicitudes no se implementan por defecto (solo si el requerimiento lo solicita).

## Decisiones respecto al plan inicial

| Decision | Motivo |
|---|---|
| Sin Dapper | EF Core con proyecciones resuelve todas las consultas; una dependencia menos |
| PDF con escritor propio en lugar de QuestPDF | La licencia gratuita de QuestPDF esta limitada por tamano de organizacion; el PDF original es texto simple |
| scrypt propio solo para verificar hashes heredados | .NET no incluye scrypt; se valida con los vectores de la RFC 7914 y con hashes generados por el sistema anterior. Los hashes nuevos usan PBKDF2-SHA256 y los heredados se regeneran al primer ingreso |
| Almacen S3 de desarrollo `adobe/s3mock` | La imagen oficial de MinIO ya no se publica en los registros publicos. La API usa el protocolo S3 estandar y funciona contra el MinIO desplegado |
| Entidades sin navegaciones | El dominio solo guarda identificadores; las relaciones se declaran en la configuracion de EF para ordenar las inserciones |
