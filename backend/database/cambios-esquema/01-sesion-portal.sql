/* =============================================================================
   Sesion del portal (inicio de sesion unico) para una base de IA Autos YA EXISTENTE.
   Es idempotente: se puede ejecutar mas de una vez.

   Agrega:
     1. tblUsuarios.PortalUsuarioId (bigint NULL): id de la persona en el portal; NULL para quien no entra por el portal
        (los conductores).
     2. Indice unico filtrado UX_tblUsuarios_PortalUsuarioId: no se repite el id, pero admite muchos NULL.
     3. tblRefreshTokens: refresh tokens como hash, con vencimiento, fecha de revocacion, token que lo reemplazo, familia
        (detecta el reuso de un token robado), IP y dispositivo.
     4. (opcional) carga inicial del id del portal en los usuarios existentes, cruzando por correo.

   Como ejecutarlo: abrir en SSMS (o sqlcmd) CONECTADO a la base de IA Autos, ajustar los dos parametros y ejecutar con un
   usuario con permiso para crear tablas e indices (no el usuario de la aplicacion).
   ============================================================================= */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;

/* ---------------------------------- PARAMETROS ---------------------------------- */
-- Esquema donde estan las tablas de IA Autos: dbo en una base creada con ddl.sql; db_IA_AUTOS en la base compartida de pruebas.
DECLARE @esquema sysname = N'db_IA_AUTOS';
-- Base de TI con las tablas del portal (dbSolucionesTI en produccion, dbSolucionesTI_Prueba en pruebas) para el paso 4.
-- NULL = omitir la carga inicial. El usuario que ejecuta debe poder leer tblSAPUsuariosApp en esa base.
DECLARE @basePortal sysname = NULL;
/* -------------------------------------------------------------------------------- */

DECLARE @q nvarchar(300) = QUOTENAME(@esquema);
DECLARE @sql nvarchar(max);

IF SCHEMA_ID(@esquema) IS NULL
    THROW 50000, N'El esquema indicado en @esquema no existe en esta base.', 1;
IF OBJECT_ID(@q + N'.tblUsuarios', N'U') IS NULL
    THROW 50001, N'No existe tblUsuarios en el esquema indicado: conectese a la base de IA Autos y revise @esquema.', 1;

-- 1) Columna
IF COL_LENGTH(@q + N'.tblUsuarios', N'PortalUsuarioId') IS NULL
BEGIN
    SET @sql = N'ALTER TABLE ' + @q + N'.tblUsuarios ADD PortalUsuarioId bigint NULL;';
    EXEC (@sql);
    PRINT N'Columna tblUsuarios.PortalUsuarioId creada.';
END
ELSE PRINT N'Columna tblUsuarios.PortalUsuarioId ya existia.';

-- 2) Indice unico que admite nulos
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblUsuarios_PortalUsuarioId' AND object_id = OBJECT_ID(@q + N'.tblUsuarios'))
BEGIN
    SET @sql = N'CREATE UNIQUE INDEX UX_tblUsuarios_PortalUsuarioId ON ' + @q + N'.tblUsuarios (PortalUsuarioId) WHERE PortalUsuarioId IS NOT NULL;';
    EXEC (@sql);
    PRINT N'Indice UX_tblUsuarios_PortalUsuarioId creado.';
END
ELSE PRINT N'Indice UX_tblUsuarios_PortalUsuarioId ya existia.';

-- 3) Refresh tokens
IF OBJECT_ID(@q + N'.tblRefreshTokens', N'U') IS NULL
BEGIN
    SET @sql = N'
    CREATE TABLE ' + @q + N'.tblRefreshTokens
    (
        Id               uniqueidentifier  NOT NULL CONSTRAINT PK_tblRefreshTokens PRIMARY KEY,
        UsuarioId        uniqueidentifier  NOT NULL,
        TokenHash        char(64)          NOT NULL,
        FamiliaId        uniqueidentifier  NOT NULL,
        ExpiraAt         datetimeoffset(3) NOT NULL,
        RevocadoAt       datetimeoffset(3) NULL,
        ReemplazadoPorId uniqueidentifier  NULL,
        Ip               varchar(45)       NULL,
        Dispositivo      nvarchar(300)     NULL,
        CreatedAt        datetimeoffset(3) NOT NULL CONSTRAINT DF_tblRefreshTokens_CreatedAt DEFAULT (SYSDATETIMEOFFSET()),
        UpdatedAt        datetimeoffset(3) NOT NULL CONSTRAINT DF_tblRefreshTokens_UpdatedAt DEFAULT (SYSDATETIMEOFFSET()),
        CONSTRAINT UQ_tblRefreshTokens_TokenHash UNIQUE (TokenHash),
        CONSTRAINT FK_tblRefreshTokens_Usuario FOREIGN KEY (UsuarioId) REFERENCES ' + @q + N'.tblUsuarios (Id)
    );
    CREATE INDEX IX_tblRefreshTokens_Familia ON ' + @q + N'.tblRefreshTokens (FamiliaId);
    CREATE INDEX IX_tblRefreshTokens_Usuario ON ' + @q + N'.tblRefreshTokens (UsuarioId);';
    EXEC (@sql);
    PRINT N'Tabla tblRefreshTokens creada.';
END
ELSE PRINT N'Tabla tblRefreshTokens ya existia.';

-- 4) Carga inicial del id del portal (opcional): cruza por correo y solo vincula cuando el correo identifica a UNA persona
--    del portal y ese id no lo tiene otro usuario. Los conductores no se vinculan (no tienen usuario del portal).
IF @basePortal IS NOT NULL
BEGIN
    DECLARE @bp nvarchar(300) = QUOTENAME(@basePortal);
    SET @sql = N'
    UPDATE u
       SET u.PortalUsuarioId = p.IdPortal, u.UpdatedAt = SYSDATETIMEOFFSET()
      FROM ' + @q + N'.tblUsuarios u
      JOIN (SELECT CAST(MIN(ID) AS bigint) AS IdPortal, LOWER(LTRIM(RTRIM(EMAIL))) COLLATE DATABASE_DEFAULT AS Email
              FROM ' + @bp + N'.dbo.tblSAPUsuariosApp
             WHERE EMAIL IS NOT NULL AND LTRIM(RTRIM(EMAIL)) <> ''''
             GROUP BY LOWER(LTRIM(RTRIM(EMAIL))) COLLATE DATABASE_DEFAULT
            HAVING COUNT(*) = 1) p
        ON p.Email = LOWER(u.Email) COLLATE DATABASE_DEFAULT
     WHERE u.PortalUsuarioId IS NULL
       AND u.Rol <> ''conductor''
       AND NOT EXISTS (SELECT 1 FROM ' + @q + N'.tblUsuarios o WHERE o.PortalUsuarioId = p.IdPortal);
    SELECT @@ROWCOUNT AS UsuariosVinculados;';
    EXEC (@sql);

    -- Lo que quedo sin vincular, para revisarlo a mano (conductores y personas sin correo coincidente en el portal).
    SET @sql = N'SELECT Rol, COUNT(*) AS SinVincular FROM ' + @q + N'.tblUsuarios WHERE PortalUsuarioId IS NULL GROUP BY Rol;';
    EXEC (@sql);
END
ELSE PRINT N'Carga inicial omitida (@basePortal es NULL).';
