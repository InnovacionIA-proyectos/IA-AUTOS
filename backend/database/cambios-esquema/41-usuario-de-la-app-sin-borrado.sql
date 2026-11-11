/* =============================================================================
   El usuario de la aplicacion no borra evidencia ni historial, para una base de IA Autos YA EXISTENTE. Se ejecuta UNA vez (repetirlo no cambia nada) y todo ocurre en UNA transaccion.

   Que hace (solo permisos; no cambia ninguna tabla ni ningun dato):
     El usuario de la aplicacion es miembro de db_datawriter, que incluye DELETE sobre TODAS las tablas. Aqui se le NIEGA (DENY) el DELETE, tabla por tabla, sobre las 8 que son evidencia o historial:
     tblEvidencias, tblHallazgos, tblHallazgoSeguimiento, tblNotasAclaratorias, tblTransferenciasCustodia, tblNotificaciones, tblAuditLog y tblVehiculos (un vehiculo se da de baja, no se borra).
     Conserva SELECT, INSERT y UPDATE sobre ellas y todo lo demas (la API sigue borrando lo que si borra: etapas y viajes sin inspecciones, usuarios sin historial, catalogos). Los triggers con error 51003
     siguen siendo la defensa de la propia base; esto agrega el permiso: ni un cambio en la aplicacion puede borrar esas filas. El DENY pesa mas que cualquier GRANT o rol.

   Seguridad de los datos: solo ejecuta DENY (idempotente). Para quitarlo: REVOKE DELETE ON <tabla> FROM <usuario>.

   Como ejecutarlo: abrir en SSMS, DBeaver o sqlcmd CONECTADO a la base de IA Autos, ajustar @esquema y @usuarioApp y ejecutar completo, en UNA sola ejecucion de script, con un usuario administrador de la base
   (no el usuario de la aplicacion). Una base nueva ya queda asi con 01-crear-usuario-app.sql. Ejecutarlo ANTES de desplegar la API (no cambia su comportamiento: ninguna de esas tablas se borra desde la API).
   ============================================================================= */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;

/* ---------------------------------- PARAMETROS ---------------------------------- */
-- Esquema donde estan las tablas de IA Autos: dbo en una base creada con ddl.sql; db_IA_AUTOS en la base compartida de pruebas.
DECLARE @esquema sysname = N'db_IA_AUTOS';
-- El usuario de base de datos con el que se conecta la API (en 01-crear-usuario-app.sql, USUARIO_APP; por ejemplo iaautos_app).
DECLARE @usuarioApp sysname = N'iaautos_app';
/* -------------------------------------------------------------------------------- */

DECLARE @q nvarchar(300) = QUOTENAME(@esquema), @u nvarchar(300) = QUOTENAME(@usuarioApp);

IF SCHEMA_ID(@esquema) IS NULL
    THROW 50000, N'El esquema indicado en @esquema no existe en esta base.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = @usuarioApp AND type IN (N'S', N'U', N'G'))
    THROW 50002, N'El usuario indicado en @usuarioApp no existe en esta base: revise el nombre (el de USUARIO_APP de 01-crear-usuario-app.sql).', 1;

DECLARE @tablas TABLE (n int IDENTITY(1, 1) PRIMARY KEY, tabla sysname);
INSERT @tablas (tabla) VALUES (N'tblEvidencias'), (N'tblHallazgos'), (N'tblHallazgoSeguimiento'), (N'tblNotasAclaratorias'), (N'tblTransferenciasCustodia'), (N'tblNotificaciones'), (N'tblAuditLog'), (N'tblVehiculos');

IF EXISTS (SELECT 1 FROM @tablas t WHERE OBJECT_ID(@q + N'.' + QUOTENAME(t.tabla), N'U') IS NULL)
    THROW 50001, N'Falta alguna de las tablas de evidencia en el esquema indicado: revise @esquema o ejecute antes los scripts de cambios anteriores.', 1;

DECLARE @n int, @tabla sysname, @sql nvarchar(max);
BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE c CURSOR LOCAL FAST_FORWARD FOR SELECT n, tabla FROM @tablas ORDER BY n;
    OPEN c;
    FETCH NEXT FROM c INTO @n, @tabla;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        SET @sql = N'DENY DELETE ON ' + @q + N'.' + QUOTENAME(@tabla) + N' TO ' + @u + N';';
        EXEC (@sql);
        PRINT CONCAT(N'OK  ', @n, N'. sin DELETE: ', @tabla);
        FETCH NEXT FROM c INTO @n, @tabla;
    END
    CLOSE c;
    DEALLOCATE c;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    PRINT CONCAT(N'FALLO en la tabla ', @tabla, N': ', ERROR_MESSAGE());
    THROW;
END CATCH

-- Resultado (esperado: 8 y 8).
SELECT
    (SELECT COUNT(*) FROM @tablas) AS TablasDeEvidencia,
    (SELECT COUNT(DISTINCT p.major_id)
     FROM sys.database_permissions p
     JOIN @tablas t ON OBJECT_ID(@q + N'.' + QUOTENAME(t.tabla), N'U') = p.major_id
     WHERE p.grantee_principal_id = DATABASE_PRINCIPAL_ID(@usuarioApp) AND p.permission_name = N'DELETE' AND p.state = N'D') AS TablasSinDeleteParaLaApp;
