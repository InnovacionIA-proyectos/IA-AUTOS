# Configuracion y variables de entorno

La API se configura solo con variables de entorno (o `appsettings`). El doble guion bajo separa secciones: `Jwt__Secret` equivale a `Jwt:Secret`. Nunca se versionan valores reales: `.env` esta en `.gitignore` y `.env.example` solo trae marcadores.

## Variables

| Variable | Obligatoria | Descripcion |
|---|---|---|
| `ConnectionStrings__DefaultConnection` | si | cadena de conexion a SQL Server 2019 (base `IAAutos`), con el usuario de aplicacion |
| `Jwt__Secret` | si | secreto HS256, minimo 32 caracteres; la API no arranca si falta o es corto |
| `Jwt__ExpiracionDias` | no (30) | vigencia del token del login directo (app movil) |
| `Jwt__Issuer`, `Jwt__Audience` | no | emisor y destinatario del JWT. Si se definen, todos los tokens nuevos los llevan y la API EXIGE que coincidan: los tokens emitidos antes (sin emisor) dejan de valer y las personas deben iniciar sesion otra vez. Valores sugeridos: `IA-AUTOS` y `IA-AUTOS.Clientes` |
| `Sesion__AccessMinutos` | no (60) | vigencia del access token de la sesion del portal (minutos) |
| `Sesion__RefreshDias` | no (30) | vigencia del refresh token (dias); viaja en el cuerpo, igual que en EmbarQA |
| `Sesion__RefreshGraciaSegundos` | no (10) | margen en el que un refresh recien rotado no se toma como robo (dos pestanas que refrescan a la vez); 0 lo desactiva |
| `PortalTi__ConnectionString` | para verificar contra TI | cadena de conexion de SOLO LECTURA a la base de usuarios del portal (`dbSolucionesTI` en produccion; `dbSolucionesTI_Prueba` en pruebas). Sin ella, `login-portal` solo acepta ids ya vinculados |
| `PortalTi__BaseDatos` | no | base con las tablas del portal; si se omite se usa la de la `ConnectionString` de TI |
| `PortalTi__DominioCorreo` | no | dominio (sin @) para crear la cuenta de quien no tiene correo en el portal; vacio = esa persona no entra |
| `Jwt__ExpiracionDias` | no (30) | vigencia del token emitido |
| `Etl__IngestToken` | si, si se usa el ETL | token compartido con el ETL; sin el, toda ingesta se rechaza |
| `Cors__Origins` | recomendada | origenes EXACTOS de la web separados por comas (con protocolo, sin barra final). **No admite comodin `*`** (se ignora y se advierte en el log): registre el origen exacto del portal y de la web, o el navegador bloqueara las llamadas. La API expone `Content-Disposition` (nombre del PDF de inventario). Vacio = sin CORS (la app movil no lo necesita) |
| `Storage__S3__Endpoint`, `__Bucket`, `__Region`, `__AccessKey`, `__SecretKey` | no | almacenamiento S3 compatible; si Endpoint o Bucket faltan se usa disco |
| `Storage__LocalPath` | no (`.uploads`) | carpeta para el almacenamiento en disco |
| `Storage__MaximoBytes` | no (10 MB) | tamano maximo de una imagen |
| `Storage__LecturaPublica` | no (`true`) | `false` exige token para leer imagenes |
| `Seguridad__PasswordMinimo` | no (8) | longitud minima de contrasenas asignadas |
| `Seguridad__ValidarPropiedadInspeccion` | no (`true`) | solo el creador o un administrador modifica o cierra un informe |
| `Seguridad__LoginMaxIntentosPorMinuto` | no (10) | limite por IP en el login |
| `Seguridad__LoginPortalMaxIntentosPorMinuto` | no (30) | limite por IP en `login-portal` (mas alto: detras del proxy corporativo muchas personas comparten IP) |
| `Seguridad__RefreshMaxPorMinuto` | no (60) | limite por IP en `refresh` |
| `Seguridad__LoginConductorMaxIntentosPorMinuto` | no (60) | limite por IP en `login-conductor` (alto: en una bodega muchos conductores comparten la misma red) |
| `Seguridad__PinMaxIntentos` | no (5) | PIN errados seguidos que bloquean la cuenta de una persona |
| `Seguridad__PinBloqueoMinutos` | no (15) | minutos que dura el bloqueo por PIN errados |
| `Proxy__ConfiarEnEncabezadosReenviados` | no (`false`) | `true` solo detras de un proxy de confianza (Railway, nginx): usa `X-Forwarded-For`. Tambien decide la IP que se guarda en cada firma de una transferencia: sin esto, detras del proxy se registra la IP del proxy y no la de quien firma |
| `Documentacion__Habilitada` | no (`false`) | publica `/openapi/v1.json` y la interfaz Swagger en `/swagger` |
| `Catalogos__CacheSegundos` | no (60) | segundos que la API conserva los catalogos en memoria; un cambio hecho por la API los renueva al instante, uno hecho directamente en la base tarda hasta este tiempo |
| `Contrasenas__Iteraciones` | no (600000) | iteraciones de PBKDF2 para hashes nuevos |
| `MSSQL_SA_PASSWORD` | solo compose | contrasena del SQL Server local de desarrollo |
| `SUPERADMIN_PASSWORD` | solo comando | contrasena para `crear-superadmin`; no dejarla en el entorno permanente |

## Como obtener cada valor

| Variable | Donde conseguirla |
|---|---|
| Cadena de conexion | La entrega el administrador de la base de datos. Cree la base con `database/00-crear-base.sql`, el esquema con `database/ddl.sql` y el usuario con `database/01-crear-usuario-app.sql` (ver abajo). La cadena usa ese usuario, nunca `sa`. En produccion use `Encrypt=True` |
| `Jwt__Secret` | Para que las sesiones y APK actuales sigan validos, use el valor de `SUPABASE_JWT_SECRET` del despliegue anterior (variables del servicio web en Railway). Si prefiere uno nuevo, genere uno aleatorio (abajo); todos los usuarios deberan iniciar sesion otra vez |
| `Etl__IngestToken` | Es el `ETL_INGEST_TOKEN` del servicio anterior y debe coincidir con `INGEST_TOKEN` del `.env` del ETL. Si lo cambia, actualice ambos lados |
| `Storage__S3__*` | Del MinIO ya desplegado: equivalen a `S3_ENDPOINT`, `S3_BUCKET`, `S3_REGION`, `S3_ACCESS_KEY` y `S3_SECRET_KEY` de las variables del servicio anterior |
| `Cors__Origins` | La URL (con protocolo, sin barra final) donde se publica la web; varias separadas por coma |
| `MSSQL_SA_PASSWORD` | Invente una contrasena robusta para el SQL Server local; no se reutiliza fuera del compose |

Generar un secreto aleatorio:

```
# PowerShell
[Convert]::ToBase64String((1..48 | ForEach-Object { Get-Random -Maximum 256 }) -as [byte[]])
# Linux, macOS o Git Bash
openssl rand -base64 48
```

Crear el usuario de base de datos con minimo privilegio (la contrasena se pasa por parametro de `sqlcmd`):

```
sqlcmd -S <servidor> -U <usuario_admin> -P <password_admin> -I -b ^
       -v USUARIO_APP="iaautos_app" PASSWORD_APP="<contrasena_robusta>" BASE="IAAutos" ^
       -i database/01-crear-usuario-app.sql
```

## Correspondencia con el sistema anterior

| Antes | Ahora |
|---|---|
| `DATABASE_URL` (PostgreSQL) | `ConnectionStrings__DefaultConnection` (SQL Server) |
| `SUPABASE_JWT_SECRET` | `Jwt__Secret` |
| `ETL_INGEST_TOKEN` | `Etl__IngestToken` |
| `CORS_ORIGINS` (por defecto `*`) | `Cors__Origins` (por defecto, sin CORS) |
| `S3_ENDPOINT`, `S3_BUCKET`, `S3_REGION`, `S3_ACCESS_KEY`, `S3_SECRET_KEY` | `Storage__S3__Endpoint`, `__Bucket`, `__Region`, `__AccessKey`, `__SecretKey` |
| `LOG_LEVEL`, `NODE_ENV`, `PORT` | `Logging__LogLevel__Default`, `ASPNETCORE_ENVIRONMENT`, `ASPNETCORE_URLS` (la imagen escucha en el 8080) |
| `NEXT_PUBLIC_*`, `EXPO_PUBLIC_*`, variables de Supabase y Sentry | no aplican al backend |

Las variables de Supabase (`SUPABASE_URL`, `SUPABASE_ANON_KEY`, `SUPABASE_SERVICE_ROLE_KEY`, etc.) no se usaban para datos ni autenticacion y no se migran.

## Gestion de secretos

- No ponga valores reales en el repositorio, en la imagen ni en el `docker-compose.yml`.
- En produccion use el gestor de secretos de la plataforma (variables del servicio, Key Vault o equivalente).
- Si un secreto se expuso alguna vez (por ejemplo, en un archivo de permisos o en el historial de git), rotelo.
