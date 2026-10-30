/* =============================================================================
   Extras del vehiculo fuera del dibujo estandar, para una base de IA Autos YA EXISTENTE (con 30-parametro-fotos-de-acomodacion.sql aplicado). Se ejecuta UNA vez (si ya esta aplicado, no cambia nada)
   y todo ocurre en UNA transaccion: si algo falla se revierte completo.

   Que hace:
     1. tblTiposExtra: el catalogo de tipos de extra (barras de techo, volco, platon...), con el patron de los demas catalogos: Codigo unico, Nombre, Orden, Activo, CreatedAt y UpdatedAt. Un tipo
        que ya tiene extras reportados no se borra (la llave foranea lo impide): se inactiva y los extras conservan su tipo.
     2. Siembra el catalogo con barras_techo, volco, platon y otro (la lista se amplia desde el portal sin cambiar el esquema). Un tipo que ya existe nunca se sobrescribe.
     3. tblInspeccionExtras: los extras de una inspeccion. Cada uno exige la inspeccion y el tipo (por su Codigo); Texto (hasta 100 caracteres) es el texto libre y solo se admite cuando el tipo
        es otro; Observacion admite hasta 500 caracteres. Un extra no se agrega, cambia ni quita en una inspeccion cerrada (error 51009).
     4. tblEvidencias.ExtraId: la foto de un extra (llave foranea a tblInspeccionExtras) y su restriccion: una foto de extra no es a la vez de una vista, de un hallazgo, una constancia ni un sello.

   Seguridad de los datos: no cambia ningun dato existente; todas las columnas y tablas nuevas nacen vacias, salvo la siembra del catalogo.

   Como ejecutarlo: abrir en SSMS, DBeaver o sqlcmd CONECTADO a la base de IA Autos, ajustar @esquema y ejecutar completo, en UNA sola ejecucion de script, con un usuario con permiso para
   crear tablas y modificarlas (no el usuario de la aplicacion). Ejecutarlo ANTES de desplegar la API.
   ============================================================================= */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;

/* ---------------------------------- PARAMETRO ---------------------------------- */
-- Esquema donde estan las tablas de IA Autos: dbo en una base creada con ddl.sql; db_IA_AUTOS en la base compartida de pruebas.
DECLARE @esquema sysname = N'db_IA_AUTOS';
/* ------------------------------------------------------------------------------- */

DECLARE @q nvarchar(300) = QUOTENAME(@esquema);

IF SCHEMA_ID(@esquema) IS NULL
    THROW 50000, N'El esquema indicado en @esquema no existe en esta base.', 1;
IF OBJECT_ID(@q + N'.tblEvidencias', N'U') IS NULL
    THROW 50001, N'Falta la tabla tblEvidencias de IA Autos en el esquema indicado: revise @esquema.', 1;
IF OBJECT_ID(@q + N'.tblInspecciones', N'U') IS NULL
    THROW 50002, N'Falta la tabla tblInspecciones de IA Autos en el esquema indicado: revise @esquema.', 1;

-- Pasos en orden. {S} = esquema entre corchetes; ~ = comilla simple dentro del texto.
DECLARE @paso TABLE (n int IDENTITY(1, 1) PRIMARY KEY, descripcion nvarchar(100), s nvarchar(max));

INSERT @paso (descripcion, s) VALUES
(N'tblTiposExtra', N'
IF OBJECT_ID(N~{S}.tblTiposExtra~, N~U~) IS NULL
BEGIN
    CREATE TABLE {S}.tblTiposExtra
    (
        Id         uniqueidentifier  NOT NULL CONSTRAINT PK_tblTiposExtra PRIMARY KEY,
        Codigo     varchar(30)       NOT NULL,
        Nombre     nvarchar(100)     NOT NULL,
        Orden      int               NOT NULL CONSTRAINT DF_tblTiposExtra_Orden DEFAULT (0),
        Activo     bit               NOT NULL CONSTRAINT DF_tblTiposExtra_Activo DEFAULT (1),
        CreatedAt  datetimeoffset(3) NOT NULL CONSTRAINT DF_tblTiposExtra_CreatedAt DEFAULT (SYSDATETIMEOFFSET()),
        UpdatedAt  datetimeoffset(3) NOT NULL CONSTRAINT DF_tblTiposExtra_UpdatedAt DEFAULT (SYSDATETIMEOFFSET()),
        CONSTRAINT UQ_tblTiposExtra_Codigo UNIQUE (Codigo),
        CONSTRAINT CK_tblTiposExtra_Nombre CHECK (LEN(LTRIM(RTRIM(Nombre))) > 0),
        CONSTRAINT CK_tblTiposExtra_Orden CHECK (Orden >= 0)
    );
END'),
(N'siembra de los tipos de extra', N'
INSERT INTO {S}.tblTiposExtra (Id, Codigo, Nombre, Orden)
SELECT NEWID(), v.Codigo, v.Nombre, v.Orden
FROM (VALUES
    (~barras_techo~, N~Barras de techo~,                      1),
    (~volco~,        N~Volco~,                                2),
    (~platon~,       N~Plat~ + NCHAR(243) + N~n~,             3),
    (~otro~,         N~Otro~,                                 4)) AS v (Codigo, Nombre, Orden)
WHERE NOT EXISTS (SELECT 1 FROM {S}.tblTiposExtra t WHERE t.Codigo = v.Codigo)'),
(N'tblInspeccionExtras', N'
IF OBJECT_ID(N~{S}.tblInspeccionExtras~, N~U~) IS NULL
BEGIN
    CREATE TABLE {S}.tblInspeccionExtras
    (
        Id            uniqueidentifier  NOT NULL CONSTRAINT PK_tblInspeccionExtras PRIMARY KEY,
        InspeccionId  uniqueidentifier  NOT NULL,
        Tipo          varchar(30)       NOT NULL,
        Texto         nvarchar(100)     NULL,
        Observacion   nvarchar(500)     NULL,
        CreatedAt     datetimeoffset(3) NOT NULL CONSTRAINT DF_tblInspeccionExtras_CreatedAt DEFAULT (SYSDATETIMEOFFSET()),
        UpdatedAt     datetimeoffset(3) NOT NULL CONSTRAINT DF_tblInspeccionExtras_UpdatedAt DEFAULT (SYSDATETIMEOFFSET()),
        CONSTRAINT FK_tblInspeccionExtras_Inspeccion FOREIGN KEY (InspeccionId) REFERENCES {S}.tblInspecciones (Id),
        CONSTRAINT FK_tblInspeccionExtras_Tipo FOREIGN KEY (Tipo) REFERENCES {S}.tblTiposExtra (Codigo),
        CONSTRAINT CK_tblInspeccionExtras_Texto CHECK (Texto IS NULL OR (Tipo = ~otro~ AND LEN(LTRIM(RTRIM(Texto))) > 0)),
        CONSTRAINT CK_tblInspeccionExtras_Observacion CHECK (Observacion IS NULL OR LEN(LTRIM(RTRIM(Observacion))) > 0)
    );
    CREATE INDEX IX_tblInspeccionExtras_Inspeccion ON {S}.tblInspeccionExtras (InspeccionId);
    CREATE INDEX IX_tblInspeccionExtras_Tipo ON {S}.tblInspeccionExtras (Tipo);
END'),
(N'tblInspeccionExtras: no se cambia en una inspeccion cerrada', N'
CREATE OR ALTER TRIGGER {S}.TR_tblInspeccionExtras_NoEnCerrada
ON {S}.tblInspeccionExtras
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1
               FROM (SELECT InspeccionId FROM inserted UNION SELECT InspeccionId FROM deleted) x
               JOIN {S}.tblInspecciones n ON n.Id = x.InspeccionId
               WHERE n.Estado = ~cerrada~)
        THROW 51009, ~La inspeccion esta cerrada y es inmutable; no admite cambios en sus extras~, 1;
END'),
(N'tblEvidencias: columna ExtraId', N'
IF COL_LENGTH(N~{S}.tblEvidencias~, N~ExtraId~) IS NULL
    ALTER TABLE {S}.tblEvidencias ADD ExtraId uniqueidentifier NULL'),
(N'tblEvidencias: ExtraId referencia al extra', N'
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N~FK_tblEvidencias_Extra~ AND parent_object_id = OBJECT_ID(N~{S}.tblEvidencias~))
    ALTER TABLE {S}.tblEvidencias ADD CONSTRAINT FK_tblEvidencias_Extra FOREIGN KEY (ExtraId) REFERENCES {S}.tblInspeccionExtras (Id)'),
(N'tblEvidencias: una foto de extra no es de otra cosa', N'
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N~CK_tblEvidencias_Extra~ AND parent_object_id = OBJECT_ID(N~{S}.tblEvidencias~))
    ALTER TABLE {S}.tblEvidencias ADD CONSTRAINT CK_tblEvidencias_Extra CHECK (ExtraId IS NULL OR (HallazgoId IS NULL AND VistaId IS NULL AND EsConstancia = 0 AND EsSello = 0))'),
(N'tblEvidencias: indice por extra', N'
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N~IX_tblEvidencias_Extra~ AND object_id = OBJECT_ID(N~{S}.tblEvidencias~))
    CREATE INDEX IX_tblEvidencias_Extra ON {S}.tblEvidencias (ExtraId) WHERE ExtraId IS NOT NULL');

DECLARE @n int, @descripcion nvarchar(100), @sql nvarchar(max);
BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE c CURSOR LOCAL FAST_FORWARD FOR SELECT n, descripcion, s FROM @paso ORDER BY n;
    OPEN c;
    FETCH NEXT FROM c INTO @n, @descripcion, @sql;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        SET @sql = REPLACE(REPLACE(@sql, N'{S}', @q), N'~', N'''');
        EXEC (@sql);
        PRINT CONCAT(N'OK  ', @n, N'. ', @descripcion);
        FETCH NEXT FROM c INTO @n, @descripcion, @sql;
    END
    CLOSE c;
    DEALLOCATE c;
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    PRINT CONCAT(N'FALLO en el paso ', @n, N' (', @descripcion, N'): ', ERROR_MESSAGE());
    THROW;
END CATCH

-- Resultado (esperado: Tipos = 4 en una base recien migrada, TablaExtras = 1, ColumnaExtraId = 1, Restricciones = 4 (dos del extra, la llave foranea y la de la foto) y Trigger = 1).
DECLARE @cuentas nvarchar(max) = REPLACE(N'
SELECT
    (SELECT COUNT(*) FROM {S}.tblTiposExtra) AS Tipos,
    (SELECT COUNT(*) FROM sys.tables WHERE name = N~tblInspeccionExtras~ AND schema_id = SCHEMA_ID(N~{E}~)) AS TablaExtras,
    (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N~{S}.tblEvidencias~) AND name = N~ExtraId~) AS ColumnaExtraId,
    (SELECT COUNT(*) FROM sys.check_constraints WHERE name IN (N~CK_tblInspeccionExtras_Texto~, N~CK_tblInspeccionExtras_Observacion~, N~CK_tblEvidencias_Extra~))
      + (SELECT COUNT(*) FROM sys.foreign_keys WHERE name = N~FK_tblEvidencias_Extra~) AS Restricciones,
    (SELECT COUNT(*) FROM sys.triggers WHERE name = N~TR_tblInspeccionExtras_NoEnCerrada~) AS Trigger_', N'{S}', @q);
SET @cuentas = REPLACE(REPLACE(@cuentas, N'{E}', @esquema), N'~', N'''');
EXEC (@cuentas);
