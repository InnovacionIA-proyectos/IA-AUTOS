/* =============================================================================
   IA-AUTOS - Usuario de base de datos de la aplicacion (minimo privilegio)

   La API NO debe conectarse como sa ni como propietario de la base: solo necesita leer y
   escribir datos (EF Core no crea ni modifica el esquema; eso lo hace ddl.sql).

   Uso (la contrasena se pasa por variable de sqlcmd; no la escriba en este archivo):
     sqlcmd -S <servidor> -U <usuario_admin> -P <password_admin> -I -b ^
            -v USUARIO_APP="iaautos_app" PASSWORD_APP="<contrasena_robusta>" BASE="IAAutos" ^
            -i 01-crear-usuario-app.sql

   Es idempotente: si el usuario ya existe solo asegura los permisos. Ejecutelo DESPUES de ddl.sql (el DENY necesita las tablas).
   ============================================================================= */
SET NOCOUNT ON;
GO

USE [master];
GO

IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'$(USUARIO_APP)')
BEGIN
    CREATE LOGIN [$(USUARIO_APP)] WITH PASSWORD = N'$(PASSWORD_APP)', CHECK_POLICY = ON, DEFAULT_DATABASE = [$(BASE)];
END
GO

USE [$(BASE)];
GO

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$(USUARIO_APP)')
BEGIN
    CREATE USER [$(USUARIO_APP)] FOR LOGIN [$(USUARIO_APP)] WITH DEFAULT_SCHEMA = [dbo];
END
GO

-- Solo datos: lectura y escritura. Sin db_owner y sin permisos de DDL.
ALTER ROLE db_datareader ADD MEMBER [$(USUARIO_APP)];
ALTER ROLE db_datawriter ADD MEMBER [$(USUARIO_APP)];
GO

-- Lo que es evidencia o historial no se borra: db_datawriter incluye DELETE sobre todas las tablas, asi que se NIEGA tabla por tabla (el DENY pesa mas que el rol). La aplicacion conserva SELECT, INSERT
-- y UPDATE sobre ellas; un vehiculo se da de baja, no se borra. Los triggers con error 51003 siguen siendo la defensa de la propia base. Idempotente; si el esquema no es dbo, ajuste @esquema. Para una base ya
-- existente (con el usuario creado) use cambios-esquema/41-usuario-de-la-app-sin-borrado.sql.
DECLARE @esquema sysname = N'dbo';
DECLARE @tablas TABLE (tabla sysname);
INSERT @tablas (tabla) VALUES (N'tblEvidencias'), (N'tblHallazgos'), (N'tblHallazgoSeguimiento'), (N'tblNotasAclaratorias'), (N'tblTransferenciasCustodia'), (N'tblNotificaciones'), (N'tblAuditLog'), (N'tblVehiculos');
IF EXISTS (SELECT 1 FROM @tablas t WHERE OBJECT_ID(QUOTENAME(@esquema) + N'.' + QUOTENAME(t.tabla), N'U') IS NULL)
    THROW 50001, N'Falta alguna de las tablas de evidencia: ejecute ddl.sql antes que este script.', 1;
DECLARE @tabla sysname, @sql nvarchar(max);
DECLARE c CURSOR LOCAL FAST_FORWARD FOR SELECT tabla FROM @tablas;
OPEN c;
FETCH NEXT FROM c INTO @tabla;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @sql = N'DENY DELETE ON ' + QUOTENAME(@esquema) + N'.' + QUOTENAME(@tabla) + N' TO [$(USUARIO_APP)];';
    EXEC (@sql);
    FETCH NEXT FROM c INTO @tabla;
END
CLOSE c;
DEALLOCATE c;
GO

PRINT 'Usuario de aplicacion listo: $(USUARIO_APP) en $(BASE) (db_datareader y db_datawriter, sin DELETE sobre la evidencia y el historial).';
GO
