/* =============================================================================
   IA-AUTOS - Usuario de base de datos de la aplicacion (minimo privilegio)

   La API NO debe conectarse como sa ni como propietario de la base: solo necesita leer y
   escribir datos (EF Core no crea ni modifica el esquema; eso lo hace ddl.sql).

   Uso (la contrasena se pasa por variable de sqlcmd; no la escriba en este archivo):
     sqlcmd -S <servidor> -U <usuario_admin> -P <password_admin> -I -b ^
            -v USUARIO_APP="iaautos_app" PASSWORD_APP="<contrasena_robusta>" BASE="IAAutos" ^
            -i 01-crear-usuario-app.sql

   Es idempotente: si el usuario ya existe solo asegura los permisos.
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

PRINT 'Usuario de aplicacion listo: $(USUARIO_APP) en $(BASE) (db_datareader y db_datawriter).';
GO
