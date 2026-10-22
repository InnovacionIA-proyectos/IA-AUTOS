/* =============================================================================
   Notas aclaratorias, para una base de IA Autos YA EXISTENTE (con 09-evidencia-sin-borrado.sql aplicado). Se ejecuta UNA vez (si ya esta aplicado, no cambia nada) y todo ocurre en UNA transaccion:
   si algo falla se revierte completo.

   Que hace:
     1. tblNotasAclaratorias: la aclaracion de un registro ya guardado (una inspeccion, un hallazgo o una transferencia) es un registro NUEVO: Id, Entidad (inspeccion, hallazgo o transferencia),
        EntidadId, AutorId, Texto (hasta 1000, no vacio) y CreatedAt, con un indice por (Entidad, EntidadId). El registro original no cambia.
     2. TR_tblNotasAclaratorias_NoModificar: un UPDATE falla con el error 51005.
     3. TR_tblNotasAclaratorias_NoBorrar: un DELETE falla con el error 51003 (la nota es evidencia).

   Seguridad de los datos: no borra ni cambia ningun dato; solo crea la tabla y sus triggers.

   Como ejecutarlo: abrir en SSMS, DBeaver o sqlcmd CONECTADO a la base de IA Autos, ajustar @esquema y ejecutar completo, en UNA sola ejecucion de script, con un usuario con permiso para crear
   tablas y triggers (no el usuario de la aplicacion). Ejecutarlo ANTES de desplegar la API.
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
IF OBJECT_ID(@q + N'.tblUsuarios', N'U') IS NULL
    THROW 50001, N'Falta la tabla tblUsuarios de IA Autos en el esquema indicado: revise @esquema.', 1;

-- Pasos en orden. {S} = esquema entre corchetes; ~ = comilla simple dentro del texto.
DECLARE @paso TABLE (n int IDENTITY(1, 1) PRIMARY KEY, descripcion nvarchar(100), s nvarchar(max));

INSERT @paso (descripcion, s) VALUES
(N'tblNotasAclaratorias', N'
IF OBJECT_ID(N~{S}.tblNotasAclaratorias~, N~U~) IS NULL
BEGIN
    CREATE TABLE {S}.tblNotasAclaratorias
    (
        Id         uniqueidentifier  NOT NULL CONSTRAINT PK_tblNotasAclaratorias PRIMARY KEY,
        Entidad    varchar(15)       NOT NULL,
        EntidadId  uniqueidentifier  NOT NULL,
        AutorId    uniqueidentifier  NOT NULL,
        Texto      nvarchar(1000)    NOT NULL,
        CreatedAt  datetimeoffset(3) NOT NULL CONSTRAINT DF_tblNotasAclaratorias_CreatedAt DEFAULT (SYSDATETIMEOFFSET()),
        CONSTRAINT FK_tblNotasAclaratorias_Autor FOREIGN KEY (AutorId) REFERENCES {S}.tblUsuarios (Id),
        CONSTRAINT CK_tblNotasAclaratorias_Entidad CHECK (Entidad IN (~inspeccion~, ~hallazgo~, ~transferencia~)),
        CONSTRAINT CK_tblNotasAclaratorias_Texto CHECK (LEN(Texto) > 0)
    );
    CREATE INDEX IX_tblNotasAclaratorias_Entidad ON {S}.tblNotasAclaratorias (Entidad, EntidadId);
END'),
(N'trigger de las notas: no se modifican', N'
CREATE OR ALTER TRIGGER {S}.TR_tblNotasAclaratorias_NoModificar
ON {S}.tblNotasAclaratorias
INSTEAD OF UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 51005, ~Una nota aclaratoria no se modifica~, 1;
END'),
(N'trigger de las notas: no se borran', N'
CREATE OR ALTER TRIGGER {S}.TR_tblNotasAclaratorias_NoBorrar
ON {S}.tblNotasAclaratorias
INSTEAD OF DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 51003, ~Este registro es evidencia y no se puede borrar~, 1;
END');

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

-- Resultado (esperado: 1 tabla, 2 triggers y 0 notas).
DECLARE @cuentas nvarchar(max) = REPLACE(N'
SELECT
    (SELECT COUNT(*) FROM sys.tables WHERE name = N~tblNotasAclaratorias~ AND schema_id = SCHEMA_ID(N~{E}~)) AS Tabla,
    (SELECT COUNT(*) FROM sys.triggers WHERE name IN (N~TR_tblNotasAclaratorias_NoModificar~, N~TR_tblNotasAclaratorias_NoBorrar~)) AS Triggers,
    (SELECT COUNT(*) FROM {S}.tblNotasAclaratorias) AS Notas', N'{S}', @q);
SET @cuentas = REPLACE(REPLACE(@cuentas, N'{E}', @esquema), N'~', N'''');
EXEC (@cuentas);
