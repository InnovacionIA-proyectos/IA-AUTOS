# Plan de migración del backend: C# .NET 10 + SQL Server 2019

Proyecto: Traza Autos 360° / Inventario Visual por VIN. Documento de planeación; no modifica el proyecto original. Fecha: 2026-10-01.

Planes hermanos en esta carpeta: PLAN-MIGRACION-FLUTTER.md (móvil) y PLAN-MIGRACION-ANGULAR.md (web).

---

## 0. Resumen ejecutivo

El sistema actual es un monorepo TypeScript (Next.js 14 con API REST propia, Expo/React Native, ETL Node, PostgreSQL, MinIO). Se migra a:

- Backend: ASP.NET Core (.NET 10), Clean Architecture de 4 capas, monolito modular, **SQL Server 2019** como base de datos tanto para la API como para el Worker.
- ETL Sofasa: Worker .NET (BackgroundService).
- Móvil: Flutter (Clean Architecture por funcionalidad, Riverpod, Drift).
- Web: sigue siendo cliente de la API. El mecanismo de login web está **pendiente** de los artefactos de requerimientos.

### Decisiones confirmadas

| # | Decisión |
|---|---|
| 1 | Las tablas se renombran con prefijo `tbl` durante la migración. |
| 2 | Auditoría de acciones: requisito. Logs estructurados con correlación: opcionales (la skill los reserva a solicitud explícita). |
| 3 | Login móvil: usuario y contraseña contra la API. Login web: según artefactos de requerimientos (pendiente). |
| 4 | Estado en Flutter: Riverpod. |
| 5 | Base de datos: **SQL Server 2019** para el backend y el Worker. |
| 6 | Convenciones de la skill: clases `cls*`, variables camelCase, constantes MAYÚSCULAS_SOSTENIDAS, tablas `tbl*` (`tblSap*` solo si son de SAP; hoy no aplica). |

### Supuestos que requieren confirmación

1. Los datos actuales viven en PostgreSQL 16. Se asume que **se migran a SQL Server 2019** (carga de datos única). Si los datos productivos ya están en SQL Server, la fase F3 se reduce.
2. El SQL Server 2019 de destino es una instancia distinta de la fuente del ETL (`tbsdatacenter`, tabla `tblOrdenes_Carga_Sofasa`). El Worker abre dos conexiones: origen (lectura) y destino (escritura vía API o directa, ver sección 2).
3. Nota de ciclo de vida: SQL Server 2019 salió de soporte principal en febrero de 2025 y tiene soporte extendido hasta enero de 2030. Es válido para este proyecto, pero conviene dejar el acceso a datos desacoplado para facilitar un salto futuro a 2022 o 2025.

---

## 1. Diagnóstico del sistema actual (resumen)

### 1.1 Stack actual

| Capa | Tecnología |
|---|---|
| Lenguaje | TypeScript 5.5 (ETL y scripts en JS ESM) |
| Web y API | Next.js 14.2 (App Router), React 18, Tailwind 3.4. API REST en `app/api/[[...segments]]` con router propio (`src/server/router.ts`) |
| Móvil | Expo SDK 52, React Native 0.76, React Navigation 7, Zustand, cola offline propia (`jobQueue.ts`) |
| Compartido | `packages/shared` con Zod y tipos |
| Base de datos | PostgreSQL 16, SQL crudo con `pg`, 8 migraciones y `seed.sql` |
| Auth | Propia: scrypt y JWT HS256 de 30 días (`jose`) |
| Archivos | MinIO (S3) con AWS SDK, o disco si no hay configuración |
| PDF / Excel | `pdf-lib` (servidor), `xlsx` 0.18.5 (navegador) |
| ETL | Node con `mssql`: lee SQL Server local y envía lotes por HTTP cada 60 min |
| Tests | Vitest, 7 archivos de integración contra Postgres local |
| CI / Despliegue | GitHub Actions (lint, typecheck, build), Docker multi-stage, Railway, EAS |

Documentado pero no implementado: Supabase Auth/Storage/Realtime, RLS, Vercel, Sentry, push, `audit_log`, regla de "solo mis unidades asignadas".

### 1.2 Endpoints actuales (contrato a preservar)

| Método y ruta | Acceso |
|---|---|
| POST `/api/auth/login`, GET `/api/auth/me` | pública / autenticado |
| POST/GET `/api/inspecciones`, GET `/:id`, POST `/:id/evidencias`, `/hallazgos`, `/transferencia`, `/cerrar` | inspector, conductor, admin (escritura) |
| POST `/api/vehiculos/cargar` (máx. 5000), GET `/api/vehiculos`, `/listado`, `/:vin/inventario`, `/:vin/inventario/pdf` | admin / autenticado |
| GET `/api/patrones`, `/patrones/analisis`, `/metricas/rechazo` | autenticado |
| POST `/api/asignaciones`, POST `/api/etapas` | admin |
| GET/POST/PATCH/DELETE `/api/usuarios` | lectura autenticado, escritura super_admin |
| POST `/api/uploads`, GET `/api/uploads/:file` | escritura / pública |
| POST `/api/etl/sofasa/ordenes` | token de servicio (Bearer) |
| GET `/api/sofasa/ordenes`, GET `/health` | autenticado / pública |
| GET `/api/dev/token` | pública (solo no producción) |

### 1.3 Riesgos del sistema actual (ubicaciones, sin valores)

**Críticos**
- El archivo de permisos de Claude Code en la carpeta padre del proyecto (`.claude/settings.json`) contiene en su lista de permisos un JWT, contraseñas de login y credenciales de BD/MinIO de otro proyecto. Sanear y rotar.
- La migración `20260626130000_auth_password.sql` asigna una contraseña de desarrollo conocida a todo usuario sin password. Verificar si corrió en producción.
- `/api/dev/token` es pública y emite JWT con cualquier rol cuando `NODE_ENV` no es `production`.
- El build release del APK se firma con la `debug.keystore`.
- Sin control de acceso por objeto: cualquier usuario autenticado lee todas las inspecciones y usuarios, y escribe en inspecciones ajenas; no se valida que quien firma sea quien dice ser.

**Altos**
- JWT de 30 días sin revocación; `requireAuth` no consulta la BD; sin rate limiting en login.
- Inmutabilidad solo en código: `firmarTransferencia` no verifica inspección cerrada; sin triggers ni RLS; `audit_log` nunca se escribe.
- `GET /api/uploads/:file` es público (protegido solo por UUID).
- ETL con `TOP (1000)` sin marca de agua: pérdida de datos si entran más de 1000 órdenes entre ciclos.
- El sello de fecha y GPS lo fija el cliente, y todas las fotos de un informe llevan el del cierre.
- Datos posiblemente reales versionados: `ejemplo documentos cargue/*` y `services/etl-sofasa.7z`.

**Medios**
- Uploads sin límite de tamaño ni validación de contenido; `xlsx@0.18.5` con CVEs; `minio:latest`; Dockerfile como root.
- `super_admin` no puede usar `/vehiculos/cargar`, `/asignaciones` ni `/etapas`.
- Mensajes crudos de la BD devueltos al cliente; filtros ILIKE sin escapar comodines.
- CI no corre tests; tests existentes posiblemente desfasados; sin tests de login, usuarios, ETL, carga masiva ni móvil.
- Subconsulta de "movilizado" correlacionada y repetida.
- La cola móvil descarta el job ante cualquier 4xx no transitorio.

Lo que está bien: todo el SQL está parametrizado, el manejo global de errores no filtra stacks y hay protección contra path traversal en uploads.

---

## 2. Análisis de requerimiento

### 2.1 Alcance

- Se migra la API (`apps/web/src/server`) y el ETL Sofasa a .NET 10.
- La base de datos pasa a SQL Server 2019 (ver sección 8).
- El móvil se reescribe en Flutter (Parte 2).
- La web sigue como cliente de la API, con contrato OpenAPI. `packages/shared` (Zod) se archiva al finalizar.

### 2.2 Restricciones

- Hay APKs instalados en campo. Rutas, métodos, códigos HTTP y formas de error deben ser compatibles bit a bit hasta que todos actualicen. La cola offline trata 401, 408, 429 y 5xx como reintentables y un 409 al cerrar como éxito.
- MinIO se reutiliza (no se migran objetos). Las URLs `/api/uploads/:file` se mantienen.
- La arquitectura no se prepara para microservicios (criterio de la skill).

### 2.3 Rol del Worker (ETL)

El Worker usa SQL Server en dos puntos:

1. **Origen:** `tbsdatacenter` / `dbSolucionesTI.dbo.tblOrdenes_Carga_Sofasa` (lectura).
2. **Destino:** SQL Server 2019 de la aplicación. Se recomienda que el Worker **no escriba directo** a la BD de la aplicación, sino que siga enviando a `POST /api/etl/sofasa/ordenes` con token de servicio, para reutilizar las reglas de dominio e idempotencia. Alternativa (a decidir): que el Worker referencie la capa de Aplicación y ejecute el mismo caso de uso `IngestarOrdenes` en proceso, sin HTTP. Se recomienda la primera por aislamiento y porque el Worker corre on-premise.

---

## 3. Diseño de arquitectura

Monolito modular, Clean Architecture de 4 capas, un solo despliegue de API más un Worker. Sin microservicios.

Regla de dependencias: API -> Aplicación -> Dominio; Infraestructura -> Aplicación y Dominio; el Dominio no depende de nadie.

| Capa | Responsabilidad |
|---|---|
| Dominio | Entidades `cls*`, reglas de negocio, interfaces de repositorios, excepciones de dominio |
| Aplicación | Casos de uso, DTOs, validadores (FluentValidation), mappers, interfaces de servicios externos |
| Infraestructura | EF Core (SqlServer), Dapper, repositorios, cliente S3, QuestPDF, emisión JWT, hash de contraseñas, auditoría |
| API | Controllers, middlewares, autenticación, autorización por políticas, CORS, health checks, OpenAPI |

### 3.1 Módulos

| Módulo | Absorbe |
|---|---|
| Auth | login, perfil, JWT, scrypt legado, proveedor web (pendiente) |
| Usuarios | CRUD, roles, baja lógica o física |
| Vehiculos | carga masiva, listados, inventario, PDF |
| Inspecciones | crear, evidencias, hallazgos, cerrar, informe |
| Custodia | transferencias y firmas |
| Asignaciones | asignaciones y etapas |
| Sofasa | ingesta y consulta |
| Reportes | patrones, análisis, tasa de rechazo |
| Archivos | uploads S3 o disco |
| Auditoria | escritura de `tblAuditLog` |

### 3.2 CQRS

No se implementa por defecto. Única excepción justificada: Reportes (lecturas analíticas pesadas), con consultas dedicadas en Dapper sobre SQL Server. El resto usa servicios de aplicación convencionales.

### 3.3 Acceso a datos con SQL Server 2019

- EF Core con `Microsoft.EntityFrameworkCore.SqlServer` para CRUD.
- Dapper con `Microsoft.Data.SqlClient` para Reportes y operaciones masivas.
- Nivel de compatibilidad de la base: 150 (SQL Server 2019). Permite `OPENJSON`, `STRING_AGG`, `STRING_SPLIT` y columnas `datetimeoffset`.
- Resiliencia: `EnableRetryOnFailure` de EF Core para errores transitorios.
- No hay tipo JSON nativo en SQL Server 2019. `coords_diagrama` se guarda en `nvarchar(max)` con `CHECK (ISJSON(...) = 1)`.

---

## 4. Estructura de carpetas

```
BoteroTraza/
  src/
    Dominio/
      Inspecciones/    clsInspeccion, clsHallazgo, clsEvidencia, Excepciones/, IInspeccionRepository
      Custodia/        clsTransferenciaCustodia, ITransferenciaRepository
      Vehiculos/       clsVehiculo, clsEtapa, IVehiculoRepository
      Usuarios/        clsUsuario, IUsuarioRepository
      Asignaciones/    clsAsignacion
      Sofasa/          clsOrdenSofasa, IOrdenSofasaRepository
    Aplicacion/
      Auth/            Login/, RefreshToken/, DTOAuth/, Validators/, Services/
      Usuarios/        Commands/, Queries/, DTOUsuarios/, Validators/
      Vehiculos/       CargaMasiva/, Listados/, Inventario/, DTOVehiculos/, Validators/
      Inspecciones/    Commands/, Queries/, DTOInspecciones/, Validators/
      Custodia/        Commands/, DTOCustodia/, Validators/
      Asignaciones/    Commands/, DTOAsignaciones/, Validators/
      Sofasa/          Ingesta/, Queries/, DTOSofasa/
      Reportes/        Queries/, DTOReportes/
      Archivos/        Commands/, Queries/
      Auditoria/       Services/
    Infraestructura/
      Persistencia/    Contexto (DbContext), Configuraciones/ (tbl*), Repositorios/, Migraciones/
      Archivos/        clsAlmacenS3, clsAlmacenDisco
      Reportes/        clsPdfInventario (QuestPDF), consultas Dapper
      Seguridad/       clsPasswordHasher (scrypt legado), clsEmisorJwt
      Auditoria/       clsAuditoriaRepository
    API/
      Controllers/     Auth/, Usuarios/, Vehiculos/, Inspecciones/, Custodia/ ...
      Middlewares/     manejo global de errores (ProblemDetails), rate limiting
      Program.cs
    EtlSofasa/         Worker .NET (BackgroundService)
  tests/
    Unitarios/  Integracion/ (Testcontainers MsSql)  Contrato/  E2E/
  Dockerfile  docker-compose.yml  .gitignore  .env.example  docs/
```

---

## 5. Modelo de dominio

Entidades `cls*`. Propiedades públicas y métodos en PascalCase; miembros privados y parámetros en camelCase; constantes en MAYÚSCULAS_SOSTENIDAS.

| Entidad | Relación |
|---|---|
| `clsVehiculo` | VIN único, operación, marca; tiene 3 `clsEtapa` |
| `clsEtapa` | cargue, transporte, descargue; orden único por vehículo; tiene `clsInspeccion` |
| `clsInspeccion` | única por etapa y momento; estados abierta o cerrada; tiene hallazgos y evidencias |
| `clsHallazgo` | parte, clase de daño, tamaño, coordenadas, requiere reemplazo |
| `clsEvidencia` | ruta de archivo, GPS, sello de fecha |
| `clsTransferenciaCustodia` | firmas de quien entrega y de quien recibe |
| `clsUsuario` | rol, activo, hash de contraseña |
| `clsAsignacion` | vínculo usuario-etapa |
| `clsOrdenSofasa` | clave de origen única (idempotente) |

### 5.1 Reglas que pasan al Dominio (con tests)

- Una inspección cerrada es inmutable, incluidas las firmas (hoy no se valida en `firmarTransferencia`).
- Cierre válido: si hay hallazgos se exige al menos una evidencia, y toda evidencia debe tener latitud y longitud.
- "Movilizado": VIN con todas sus etapas (y al menos una) con inspección cerrada.
- Tasa de rechazo: porcentaje de unidades movilizadas con al menos un hallazgo, por operación, frente a una línea base configurable (hoy hardcodeada: importación 8.81, exportación 8.34, nacional 1.95).
- Firma parcial acumulada; el sello de firma se fija solo cuando existen ambas.
- Baja de usuario: eliminar y, ante conflicto de clave foránea, baja lógica. Se bloquea la auto-eliminación.
- Máquina de estados explícita para Inspección.

---

## 6. Casos de uso

| Módulo | Casos de uso |
|---|---|
| Auth | Login, ObtenerPerfil, RefreshToken |
| Usuarios | Listar, Crear, Actualizar, Eliminar (solo super_admin) |
| Vehiculos | CargarVins (máx. 5000), BuscarPorVin, ListarMovilizados, ListarPendientes, ListarNovedad, ObtenerInventario, GenerarInventarioPdf |
| Inspecciones | Crear, AgregarEvidencia, AgregarHallazgo, Cerrar, Listar, ObtenerInforme |
| Custodia | RegistrarTransferencia |
| Asignaciones | CrearAsignacion, CrearEtapa |
| Sofasa | IngestarOrdenes, ListarOrdenes |
| Reportes | ObtenerPatrones, ObtenerAnalisis, ObtenerTasaRechazo |
| Archivos | SubirImagen, ObtenerImagen |
| Auditoria | RegistrarAccion (invocada por los casos de uso de escritura) |

---

## 7. Implementación por fases (backend)

### F0. Saneamiento previo

- Sanear y rotar lo expuesto en el archivo de permisos de `.claude` de la carpeta padre.
- Verificar el contenido de `services/etl-sofasa.7z` y de `ejemplo documentos cargue/*`; purgar del historial si hay datos reales.
- Confirmar si la migración de contraseña de desarrollo corrió en producción; forzar rotación de esas cuentas.
- Inventariar el esquema real de producción contra las 8 migraciones (detectar deriva).
- Exportar rutas, códigos y formas de error actuales como línea base de contrato.

### F1. Red de seguridad

- Tests de caracterización sobre la API actual: login, cierre, firmas, carga masiva, ingesta Sofasa, movilizado, tasa de rechazo, baja de usuario.
- Estos tests se convierten en los tests de contrato de la API nueva.

### F2. Esqueleto .NET 10

- Solución de 4 capas, Dockerfile no-root, docker-compose (API, SQL Server 2019, MinIO), `.gitignore`, `.env.example`, CI con build y tests, endpoint `/health` con verificación de BD.

### F3. Datos: de PostgreSQL a SQL Server 2019

1. Diseñar el esquema destino con prefijo `tbl` (sección 8) y crear las migraciones (EF Core Migrations o DbUp).
2. Escribir el script de carga de datos único (extracción desde Postgres, transformación de tipos, carga a SQL Server). Herramientas candidatas: un programa .NET de migración con `SqlBulkCopy`, o SSIS/Azure Data Factory si ya existen en la organización.
3. Reconciliación: conteos por tabla, sumas de verificación de campos clave, muestreo de inspecciones cerradas con sus hallazgos, evidencias y firmas.
4. Triggers que impidan modificar filas de inspecciones cerradas.
5. Optimizar "movilizado" (vista indexada, columna calculada persistida o tabla de estado mantenida por el caso de uso de cierre).
6. Limpiar de las migraciones los UPDATE con datos de desarrollo y el correo real embebido.
7. Opcional: crear vistas con los nombres antiguos durante la ventana de convivencia.

Nota: la ventana de convivencia entre API vieja (Postgres) y nueva (SQL Server) exige una estrategia de datos. Opciones: (a) corte único con ventana de mantenimiento, (b) replicación temporal. Se recomienda (a): el volumen es bajo y el sistema opera por turnos de campo. Definir fecha de corte con el negocio y congelar escrituras durante la carga final.

### F4. Módulos (de menor a mayor riesgo)

1. Reportes, Vehiculos (lectura), Sofasa (consulta).
2. Auth y Usuarios, con JWT compatible para no invalidar sesiones.
3. Archivos: S3 sobre el MinIO existente, ruta `/api/uploads/:file` intacta.
4. Inspecciones y Custodia: núcleo con reglas de dominio y auditoría.
5. Carga masiva, Asignaciones y el Worker ETL.

Nota sobre el corte: como la base de datos cambia de motor, el patrón strangler por ruta (API vieja y nueva contra la misma BD) no es posible tal cual. Hay dos caminos:

- **Corte por bloques de datos:** la API nueva y la vieja no comparten BD, por lo que el corte se hace al final de F4 con la ventana de mantenimiento de F3.
- **Strangler en lectura:** se despliega la API nueva en paralelo con datos replicados y se compara el resultado de las lecturas (tráfico en sombra), pero las escrituras siguen en la API vieja hasta el corte.

### F5. Corte

- Ventana de mantenimiento, carga final de datos, reconciliación, redirección del proxy al backend .NET y rollback documentado (volver al proxy viejo y a Postgres, que no se apaga hasta superar un periodo de observación).

### F6. Cierre

- Retirar `src/server` de Next.js, regenerar clientes desde OpenAPI, archivar `packages/shared`, apagar Postgres tras el periodo de observación.

---

## 8. Base de datos: SQL Server 2019

### 8.1 Nombres de tablas

| Actual | Nueva |
|---|---|
| usuarios | tblUsuarios |
| vehiculos | tblVehiculos |
| puntos_custodia | tblPuntosCustodia |
| etapas | tblEtapas |
| inspecciones | tblInspecciones |
| hallazgos | tblHallazgos |
| evidencias | tblEvidencias |
| transferencias_custodia | tblTransferenciasCustodia |
| asignaciones | tblAsignaciones |
| ordenes_sofasa | tblOrdenesSofasa |
| audit_log | tblAuditLog |

`tblSap*` no aplica (no hay tablas SAP).

### 8.2 Equivalencias de tipos PostgreSQL a SQL Server 2019

| PostgreSQL | SQL Server 2019 |
|---|---|
| ENUM (operacion, punto_custodia_nombre, momento_inspeccion, estado_inspeccion, clase_dano, tamano_dano, rol_usuario, subtipo_inspector) | `varchar(n)` con `CHECK` o tabla catálogo. Se recomienda tablas catálogo para los que cambian (clases de daño, partes) y `CHECK` para los fijos. |
| `parte_vehiculo` (ya es text desde la migración 5) | `nvarchar(100)` |
| UUID | `uniqueidentifier` (con `NEWSEQUENTIALID()` si se usa como clave agrupada) |
| JSONB (`coords_diagrama`) | `nvarchar(max)` con `CHECK (ISJSON(coords_diagrama) = 1)`; lectura con `OPENJSON` si se necesita |
| TIMESTAMPTZ | `datetimeoffset(3)` |
| NUMERIC | `decimal(p,s)` |
| TEXT | `nvarchar(max)` o `nvarchar(n)` |
| BIGINT identity / serial | `bigint identity` |
| BOOLEAN | `bit` |
| Trigger `set_updated_at` | trigger `AFTER UPDATE` o asignación en la capa de aplicación (`SaveChanges` override) |
| `on conflict do nothing` / `do update` | `MERGE` con `HOLDLOCK`, o `INSERT ... WHERE NOT EXISTS` bajo `SERIALIZABLE` |
| `xmax = 0` (distinguir insertado de actualizado) | `OUTPUT $action` de `MERGE`, o consulta previa de existencia en el caso de uso |
| `ILIKE` | `LIKE` con colación `_CI_AS` (insensible a mayúsculas) |
| Escapar `%` y `_` en LIKE | `LIKE ... ESCAPE '\'` |
| `TRUNCATE ... RESTART IDENTITY CASCADE` (seed de tests) | `DELETE` en orden de dependencias y `DBCC CHECKIDENT`, o recrear la BD de pruebas por Testcontainers |
| Índices únicos parciales | índices únicos filtrados (`WHERE ...`) |

### 8.3 Puntos de atención específicos de SQL Server

- **MERGE:** tiene antecedentes de errores en casos con múltiples cláusulas. Para la carga masiva se recomienda `MERGE` con `HOLDLOCK` y pruebas intensivas, o `UPDATE` más `INSERT` explícitos en una transacción.
- **Concurrencia:** definir nivel de aislamiento. Considerar `READ_COMMITTED_SNAPSHOT` en la base para evitar bloqueos de lectura contra escrituras.
- **Idempotencia:** los índices únicos (`vin`, `etapa_id + momento`, `source_id`, `vehiculo_id + orden`) se mantienen como defensa final.
- **Inmutabilidad:** trigger `INSTEAD OF UPDATE/DELETE` o `AFTER` con `THROW` sobre `tblHallazgos`, `tblEvidencias` y `tblTransferenciasCustodia` cuando la inspección asociada está cerrada.
- **Colación:** definir una colación explícita (por ejemplo `Modern_Spanish_CI_AS`) para evitar sorpresas en comparaciones de VIN y correo. El VIN debe compararse sin distinción de mayúsculas o normalizarse a mayúsculas.
- **Tamaño de claves y filas:** `nvarchar` usa 2 bytes por carácter. Dimensionar columnas largas con cuidado.
- **Paginación:** `OFFSET ... FETCH` (500 por página como hoy) con `ORDER BY` determinista.
- **Consulta de "movilizado":** reescribir sin subconsultas correlacionadas repetidas. Opciones: vista indexada, columna calculada persistida, o tabla de estado `tblEstadoMovilizacion` mantenida por el caso de uso de cierre.
- **Zona horaria:** `datetimeoffset` conserva el desfase; definir UTC en servidor.
- **Auditoría:** `tblAuditLog` se escribe desde la capa de Aplicación en toda acción relevante, no desde triggers de datos de negocio.

### 8.4 ETL

- Marca de agua incremental por `DATETIME_EVENT` o clave de origen, persistida en una tabla de control, en lugar de `TOP (1000)`.
- Lotes de 500, con reintentos y reconciliación de conteos periódica.
- Conexión de origen de solo lectura, cifrada (`Encrypt=True`) y con credenciales por variable de entorno.

---

## 9. API

- Mismas rutas, métodos y formas de error que hoy.
- Errores en formato compatible con el actual (la cola móvil depende de la clasificación por código). Documentar la equivalencia ProblemDetails contra el formato vigente.
- Autorización por políticas, corrigiendo los huecos del diagnóstico:
  - Propiedad del objeto (inspector -> sus unidades asignadas).
  - Quien firma es quien dice ser.
  - `super_admin` incluido en las rutas de administración.
  - Verificación de usuario activo y rol, o JWT cortos con refresh.
  - Rate limiting nativo en login.
  - Comparación de tiempo constante para el token del ETL.
  - Fotos servidas con control de acceso (hoy son públicas por UUID).
- Uploads con límite de tamaño y validación de contenido real (magic bytes).
- CORS por lista de orígenes, nunca `*`.
- Mensajes de error sin detalle interno de la base de datos.
- OpenAPI/Swagger solo en desarrollo y QA.
- **Login web: pendiente** de los artefactos de requerimientos. El módulo Auth se diseña con `IAuthService` / proveedor de identidad intercambiable para soportar login propio o SSO sin afectar el resto.

---

## 10. Testing

| Nivel | Herramienta | Alcance |
|---|---|---|
| Unitarios | xUnit y FluentAssertions | reglas de Dominio, validadores, casos de uso con mocks |
| Integración | Testcontainers (`Testcontainers.MsSql`, imagen SQL Server 2019) | repositorios, migraciones, MERGE e idempotencia, triggers de inmutabilidad |
| Contrato | WebApplicationFactory | paridad con la API actual: rutas, códigos, formas de error |
| Flujo | xUnit | inspección completa y firma parcial |
| E2E | Playwright (web) | flujos críticos de administración |

Todo corre automáticamente en CI. Hoy la CI no ejecuta tests.

---

## 11. Docker

- `Dockerfile` multi-stage: SDK 10 para compilar y runtime `aspnet:10.0` no-root.
- `docker-compose.yml` con perfiles de desarrollo y QA: API, SQL Server (`mcr.microsoft.com/mssql/server:2019-latest`, con versión/CU fija en cuanto sea posible), MinIO con versión fija, ETL opcional.
- SQL Server en contenedor requiere aceptar el EULA (`ACCEPT_EULA`) y una contraseña `sa` robusta, **siempre por variable de entorno**, nunca en el repositorio.
- Volúmenes persistentes para datos de SQL Server y MinIO en desarrollo.
- `.env.example` solo con marcadores, nunca valores reales. `.gitignore` excluye `.env`, `bin/`, `obj/`, archivos temporales y configuraciones locales.

---

## 12. DevOps e infraestructura

### 12.1 CI/CD

- GitHub Actions: restore, build, tests (incluye contrato e integración con SQL Server en contenedor), análisis de calidad, escaneo de dependencias, build de imagen.
- Corregir que el workflow actual escucha `main` y `develop` mientras el repositorio usa `master`.
- Entornos: local, desarrollo, QA, producción. Aprobación manual a producción.
- Secretos en un gestor de secretos, no en el repositorio.

### 12.2 Opciones de infraestructura (con SQL Server 2019)

**Opción 1. API y Worker en contenedores, SQL Server 2019 en infraestructura propia**
- API en contenedor (servidor propio o VM en nube); SQL Server 2019 en el servidor corporativo existente o una VM; MinIO en contenedor o servicio S3 compatible; Worker ETL on-premise junto al SQL Server de origen.
- Pros: encaja con la base ya definida, control total y menor costo si ya hay licencias; el Worker queda cerca del origen.
- Contras: mantenimiento de parches, backups y alta disponibilidad a cargo del equipo; Railway no ofrece SQL Server administrado, por lo que el despliegue actual no sirve tal cual.

**Opción 2. Contenedores en nube con SQL administrado (Azure Container Apps o App Service con Azure SQL / SQL Managed Instance)**
- Pros: backups, alta disponibilidad y parches administrados; Key Vault para secretos; observabilidad integrada.
- Contras: mayor costo; Azure SQL Database y Managed Instance están más actualizados que SQL Server 2019 (diferencias de versión a validar en pruebas); requiere conectividad segura (VPN o ExpressRoute) con el SQL Server on-premise de origen del ETL.

Recomendación: empezar con la Opción 1 si la organización ya opera SQL Server 2019, con backups probados y monitoreo; evaluar la Opción 2 si crece el volumen o los requisitos de auditoría.

### 12.3 Logging y métricas

- Auditoría de acciones: **requisito** (tabla `tblAuditLog`).
- Logs estructurados con correlación de solicitudes y métricas de uso: opcionales; no se implementan por defecto (criterio de la skill). Se agregan si el requerimiento lo solicita.

---

## 13. Documentación a entregar

Sin emojis, en formato profesional. Contenido: descripción del proyecto, arquitectura (capas y módulos), infraestructura (Docker, redes, variables de entorno, base de datos, servicios externos), flujos (principal, autenticación y de negocio), stack, ejecución local (desarrollo y QA), API (endpoints, ejemplos, códigos y errores), testing, estructura de carpetas, y una tabla de equivalencia ruta por ruta entre la API vieja y la nueva, más la guía de migración de datos.

---

## 14. Recomendaciones de producción

- Rotar todos los secretos que hayan circulado. No reutilizar el secreto JWT actual como clave de la API nueva, salvo durante una ventana de compatibilidad de tokens documentada.
- Forzar cambio de contraseña a las cuentas creadas con la contraseña de desarrollo.
- Firmar el APK de release con una clave propia.
- Eliminar `/api/dev/token` y el login sin contraseña de los builds de producción.
- Backups completos y de log de transacciones de SQL Server, con prueba periódica de restauración.
- Plan de rollback por fase; Postgres no se apaga hasta superar un periodo de observación posterior al corte.
- Cifrado de conexiones a SQL Server y usuario de mínimo privilegio para la API (sin `sa`).
- Revisar y purgar del historial los datos reales versionados si los hay.

---

## 15. Riesgos y mitigaciones (backend)

| Riesgo | Mitigación |
|---|---|
| Romper APKs instalados | Tests de contrato, cero cambios de ruta o código, versión de API compatible |
| Pérdida o corrupción de datos al pasar de PostgreSQL a SQL Server | Script de carga único, reconciliación de conteos y sumas, ensayo completo en QA antes del corte |
| Diferencias de semántica (enums, jsonb, upserts, `xmax`, colación) | Tabla de equivalencias de la sección 8, pruebas de integración sobre SQL Server real |
| Errores de `MERGE` bajo concurrencia | `HOLDLOCK`, pruebas de carga, alternativa con UPDATE más INSERT en transacción |
| Reglas de negocio no documentadas | Tests de caracterización antes de portar y revisión con el negocio |
| Renombrado `tbl*` rompe consultas externas | Inventariar consumidores de la BD, vistas de compatibilidad temporales |
| Incompatibilidad de scrypt o JWT | Pruebas cruzadas y rehash gradual en el siguiente login |
| Pérdida de datos Sofasa | Marca de agua incremental y reconciliación de conteos |
| Infraestructura actual (Railway) sin SQL Server administrado | Elegir infraestructura de la sección 12.2 antes de F2 |
| SQL Server 2019 fuera de soporte principal | Acceso a datos desacoplado (repositorios), plan de actualización posterior |
| Login web sin definir | Interfaz intercambiable; cierre al recibir los artefactos |
| Sobreingeniería | Aplicar la jerarquía de la skill: sin microservicios, CQRS solo en Reportes |


---

## Anexo A. Prompt para generar el diagrama de la arquitectura final (ChatGPT)

```
Actúa como arquitecto de software. Genera diagramas profesionales de la ARQUITECTURA FINAL de un sistema ya migrado a .NET 10 con Clean Architecture de 4 capas y monolito modular. Entrega: (1) diagrama C4 nivel 2 (Contenedores), (2) diagrama C4 nivel 3 (Componentes de la API), (3) diagrama de capas con la regla de dependencias, (4) diagrama de despliegue. Usa Mermaid C4 (o PlantUML C4) y, si puedes, una imagen limpia con colores por capa y leyenda. Sin emojis.

SISTEMA: "Traza Autos 360° / Inventario Visual por VIN". Controla la custodia e inspección de vehículos nuevos (cargue, transporte, descargue) con evidencia fotográfica geolocalizada y firmas de entrega y recepción.

ACTORES: Inspector y Conductor (app Flutter, a veces sin red), Admin y Super Admin (web), Coordinador de Calidad y Comercial (vía API).

CONTENEDORES:
1. App móvil Flutter (Android): Clean Architecture por funcionalidad (auth, vehiculos, inspecciones, custodia, sofasa, sync). Estado con Riverpod, cola offline en SQLite (Drift), almacenamiento seguro del token, cámara y GPS.
2. Cliente web: consume la API. El método de login web está por definir (login propio o SSO), así que represéntalo detrás de un componente "Proveedor de identidad" intercambiable.
3. API ASP.NET Core (.NET 10), un solo despliegue, contenedor Docker no-root.
4. Worker ETL Sofasa (.NET BackgroundService, on-premise): lee SQL Server de origen y envía lotes a la API con token de servicio.
5. SQL Server 2019 (base de datos de la aplicación): tablas con prefijo tbl (tblUsuarios, tblVehiculos, tblEtapas, tblInspecciones, tblHallazgos, tblEvidencias, tblTransferenciasCustodia, tblAsignaciones, tblOrdenesSofasa, tblAuditLog).
6. MinIO / S3: fotos de evidencia y firmas.
7. SQL Server on-premise de origen (Sofasa), distinto de la base de la aplicación.
8. CI/CD (GitHub Actions) y registro de contenedores.

ESTRUCTURA INTERNA DE LA API (Clean Architecture, 4 capas; reglas de dependencia: API -> Aplicación -> Dominio; Infraestructura -> Aplicación y Dominio; el Dominio no depende de nadie):
- Dominio: entidades clsVehiculo, clsEtapa, clsInspeccion, clsHallazgo, clsEvidencia, clsTransferenciaCustodia, clsUsuario, clsAsignacion, clsOrdenSofasa; reglas de negocio (inspección cerrada inmutable, cierre con evidencia y GPS obligatorios, definición de "movilizado"); interfaces de repositorios; excepciones de dominio.
- Aplicación: casos de uso por módulo, DTOs, validadores (FluentValidation), mappers, interfaces de servicios externos. Sin CQRS general; solo consultas analíticas dedicadas en el módulo Reportes.
- Infraestructura: EF Core (SqlServer) para CRUD, Dapper con Microsoft.Data.SqlClient para Reportes y cargas masivas, repositorios, cliente S3, generador de PDF (QuestPDF), emisión de JWT, hash de contraseñas, escritura de auditoría.
- API: controllers por módulo, middlewares (manejo global de errores con ProblemDetails, rate limiting, CORS por lista), autenticación JWT, políticas de autorización por rol y propiedad del objeto, health checks, OpenAPI.

MÓDULOS (carpetas por funcionalidad en cada capa): Auth, Usuarios, Vehiculos, Inspecciones, Custodia, Asignaciones, Sofasa, Reportes, Archivos, Auditoria.

RELACIONES: Flutter y web -> API (HTTPS, JWT Bearer); API -> SQL Server 2019 (TCP, conexión cifrada); API -> MinIO (S3 API); Worker ETL -> SQL Server de origen (TDS, solo lectura) y Worker -> API (HTTPS, token de servicio); fotos servidas por la API con control de acceso.

DESPLIEGUE: servidor o VM con contenedores (API, MinIO), SQL Server 2019 en infraestructura corporativa o administrada, red corporativa on-premise (SQL Server de origen + Worker), dispositivos Android, pipeline CI/CD. Secretos solo por variables de entorno o gestor de secretos; marca las fronteras de confianza (Internet / red corporativa / on-premise).

Incluye también: un diagrama de dependencias entre capas que muestre que ninguna flecha apunta desde Dominio hacia afuera, y una breve explicación de cada relación. No incluyas credenciales ni valores reales.
```

## Anexo B. Prompt para entender flujos y lógica (ChatGPT)

```
Eres un analista senior. Explícame con lenguaje claro y diagramas de secuencia (Mermaid) los siguientes flujos de un sistema de inspección vehicular por VIN. Para cada uno entrega: actores, pasos numerados, validaciones, estados, casos de error y qué pasa si falla la red.

FLUJO A - Carga masiva de VINs (admin, web): el Excel se parsea en el navegador, se detectan columnas por regex, se elige operación (importación/exportación/nacional) y se envía a POST /vehiculos/cargar (máx. 5000). Por cada VIN se hace upsert del vehículo y se crean 3 etapas (cargue, transporte, descargue) de forma idempotente. Los errores se reportan por VIN.

FLUJO B - Ingesta Sofasa (ETL): cada 60 min se leen órdenes de SQL Server, se envían en lotes de 500 con token Bearer, y la API hace upsert de vehículo, etapas y órdenes por clave de origen.

FLUJO C - Inspección en campo (móvil, offline-first): login -> abrir VIN -> capturar (GPS, datos de transporte, daños sobre diagrama, fotos) -> cerrar -> la cola local ejecuta en orden: crear inspección, subir cada foto y crear su evidencia, crear hallazgos, cerrar. Hasta 8 reintentos; 401/408/429/5xx son transitorios; un 409 al cerrar se trata como éxito.

FLUJO D - Cierre y custodia: si hay hallazgos se exige al menos una evidencia; cada evidencia debe tener lat/lng; tras cerrar, la inspección es inmutable. La transferencia acumula firmas parciales y la fecha de firma se sella cuando están ambas.

FLUJO E - Analítica: un vehículo "movilizado" tiene todas sus etapas con una inspección cerrada. Tasa de rechazo = % de movilizados con al menos 1 hallazgo, por operación, frente a una línea base. Patrones = hallazgos agrupados por proceso y parte.

FLUJO F - Autenticación: login con usuario y contraseña (móvil) -> JWT -> verificación por rol y propiedad del objeto en cada ruta. Login web por definir.

Al final dame: (1) un diagrama de estados de Inspección y Etapa, (2) un modelo entidad-relación conceptual, (3) una tabla de reglas de negocio y (4) 10 preguntas que un desarrollador debería hacerle al negocio antes de reescribirlo.
```

---

## Dependencias con los otros planes y pendientes

1. El OpenAPI estable al final de F4 (Auth, Inspecciones, Custodia) habilita M0 del plan Flutter y A0 del plan Angular.
2. El corte del backend (F5) ocurre antes del retiro del APK de Expo y antes del corte de la web.
3. El login web depende de los artefactos de requerimientos y no bloquea el backend; el módulo Auth expone una interfaz de proveedor intercambiable. Si el plan Angular elige refresh token en cookie HttpOnly, el backend debe soportarlo (CORS con credenciales, cookie segura).

Pendientes de confirmación:

1. Acceso a los artefactos de requerimientos del login web.
2. Confirmar que los datos actuales están en PostgreSQL y deben migrarse a SQL Server 2019 (o que ya están en SQL Server).
3. Elegir infraestructura (sección 12.2).
4. Decidir si el Worker envía por HTTP a la API (recomendado) o ejecuta el caso de uso en proceso.
5. Definir fecha y ventana de mantenimiento para el corte de datos.
