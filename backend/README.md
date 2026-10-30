# IA-AUTOS: Traza Autos 360 / Inventario Visual por VIN (backend .NET 10)

API de custodia e inspeccion de vehiculos nuevos por VIN: carga masiva de unidades, informes de campo con evidencia fotografica geolocalizada, transferencia de custodia firmada y reportes de patrones de dano. Es la migracion del backend original (Next.js y PostgreSQL) a ASP.NET Core sobre SQL Server 2019, con Clean Architecture de cuatro capas y monolito modular.

El ETL de Sofasa no forma parte de esta solucion: ya esta desplegado y sigue funcionando contra esta API sin cambios de contrato (solo cambia la URL de destino; ver `docs/migracion-backend.md`).

## Stack

| Componente | Tecnologia |
|---|---|
| Plataforma | .NET 10, ASP.NET Core (controladores) |
| Base de datos | SQL Server 2019 (compatibilidad 150), EF Core 10 |
| Autenticacion | JWT HS256 (compatible con los tokens del sistema anterior) |
| Imagenes | S3 compatible (el MinIO existente) o disco |
| Validacion | FluentValidation |
| PDF | Escritor propio, sin dependencias ni licencias de terceros |
| Pruebas | xUnit, WebApplicationFactory, Testcontainers (SQL Server 2019 real) |
| Contenedores | Docker, Docker Compose |

## Estructura

```
IA-AUTOS/
  src/
    IA-AUTOS.Dominio/          entidades cls*, reglas de negocio, interfaces de repositorios, excepciones
    IA-AUTOS.Aplicacion/       casos de uso por modulo, DTO, validadores, interfaces de servicios externos
    IA-AUTOS.Infraestructura/  EF Core, repositorios, consultas, S3/disco, JWT, contrasenas, PDF
    IA-AUTOS.API/              controladores, middlewares, autenticacion, CORS, OpenAPI, comandos de operacion
  tests/
    IA-AUTOS.Tests.Unitarios/    dominio, aplicacion, seguridad, PDF
    IA-AUTOS.Tests.Integracion/  API completa sobre SQL Server 2019 y almacen S3 en contenedores
  database/
    00-crear-base.sql          crea la base IAAutos
    ddl.sql                    esquema completo, vista, indices y triggers (idempotente)
    01-crear-usuario-app.sql   usuario de la aplicacion con minimo privilegio
    tests/verificar-ddl.sql    verificacion del DDL (23 comprobaciones)
    migracion/                 traslado de datos PostgreSQL a SQL Server
  docs/                        arquitectura, API, configuracion, seguridad, migracion
```

## Requisitos

- SDK de .NET 10
- Docker (para ejecutar la solucion completa y las pruebas de integracion)

## Ejecucion local con Docker Compose

```
cp .env.example .env        # completar MSSQL_SA_PASSWORD, Jwt__Secret y Etl__IngestToken
docker compose up --build
```

El compose levanta SQL Server 2019, aplica `database/ddl.sql` (servicio `db-init`), un almacen S3 de desarrollo y la API en `http://localhost:8080`. El almacen S3 de desarrollo es efimero: las imagenes se pierden al recrear su contenedor (la base de datos si persiste).

Una base nueva no tiene usuarios. Cree el primer super administrador con el comando de la propia imagen (la contrasena se lee de una variable, nunca de un argumento):

```
docker compose run --rm -e SUPERADMIN_PASSWORD='<contrasena_robusta>' api crear-superadmin admin@empresa.com "Nombre Apellido"
```

El mismo comando restablece la contrasena y reactiva a un super administrador existente (recuperacion de acceso).

Comprobacion: `GET http://localhost:8080/health` debe responder `200` con `"status":"ok"`.

## Ejecucion sin Docker

Defina las variables de `.env.example` en el entorno (o con `dotnet user-secrets`) y ejecute:

```
dotnet run --project src/IA-AUTOS.API
```

## Pruebas

```
dotnet test IA-AUTOS.slnx
```

Las pruebas de integracion levantan contenedores (SQL Server 2019 y un almacen S3 compatible) y aplican los scripts reales de `database/`. Docker debe estar en ejecucion.

| Suite | Pruebas | Que cubre |
|---|---|---|
| Unitarias | 109 | reglas de dominio, servicios de aplicacion, scrypt (vectores RFC 7914 y hashes del sistema anterior), PDF |
| Integracion | 157 | cada endpoint (exito, validacion, permisos, errores), persistencia, triggers, CORS, limite de intentos, S3 y disco |

## Documentacion

- `docs/arquitectura.md`: capas, modulos, reglas de negocio y decisiones.
- `docs/api.md`: endpoints, roles y formato de errores.
- `docs/configuracion.md`: variables de entorno y como obtenerlas.
- `docs/pruebas.md`: cómo están armadas las pruebas de integración, cómo se ejecutan y por qué se comportan como producción.
- `docs/seguridad.md`: controles aplicados y riesgos aceptados.
- `docs/migracion-backend.md`: corte desde el sistema anterior, ETL y reversa.
- `database/migracion/README.md`: traslado de datos PostgreSQL a SQL Server.
