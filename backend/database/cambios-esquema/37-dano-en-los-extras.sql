/* =============================================================================
   Dano en un extra del vehiculo, para una base de IA Autos YA EXISTENTE (con 36-dano-sin-calificar-en-los-conteos.sql aplicado). Se ejecuta UNA vez (si ya esta aplicado, no cambia nada) y todo
   ocurre en UNA transaccion: si algo falla se revierte completo.

   Que hace:
     1. tblHallazgos: columna ExtraId, el extra del que nace un hallazgo (un extra marcado como danado guarda su clase, tamano y nivel como un hallazgo de siempre, sin zona del dibujo).
     2. tblHallazgos: llave foranea FK_tblHallazgos_Extra hacia tblInspeccionExtras, restriccion CK_tblHallazgos_Extra (el hallazgo de un extra no tiene zona y es del inspector) e indice unico
        filtrado UQ_tblHallazgos_Extra (un extra tiene a lo sumo un hallazgo).

   Seguridad de los datos: no cambia ningun dato; los extras y hallazgos que ya existen siguen iguales (ExtraId queda nulo: los extras de antes siguen validos y sin dano).

   Como ejecutarlo: abrir en SSMS, DBeaver o sqlcmd CONECTADO a la base de IA Autos, ajustar @esquema y ejecutar completo, en UNA sola ejecucion de script, con un usuario con permiso para
   modificar tablas (no el usuario de la aplicacion). Ejecutarlo ANTES de desplegar la API.
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
IF OBJECT_ID(@q + N'.tblHallazgos', N'U') IS NULL OR OBJECT_ID(@q + N'.tblInspeccionExtras', N'U') IS NULL
    THROW 50001, N'Faltan las tablas tblHallazgos o tblInspeccionExtras de IA Autos en el esquema indicado: revise @esquema.', 1;

-- Pasos en orden. {S} = esquema entre corchetes; ~ = comilla simple dentro del texto.
DECLARE @paso TABLE (n int IDENTITY(1, 1) PRIMARY KEY, descripcion nvarchar(100), s nvarchar(max));

INSERT @paso (descripcion, s) VALUES
(N'tblHallazgos: columna ExtraId', N'
IF COL_LENGTH(N~{S}.tblHallazgos~, N~ExtraId~) IS NULL
    ALTER TABLE {S}.tblHallazgos ADD ExtraId uniqueidentifier NULL'),
(N'tblHallazgos: llave foranea hacia el extra', N'
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N~FK_tblHallazgos_Extra~ AND parent_object_id = OBJECT_ID(N~{S}.tblHallazgos~))
    ALTER TABLE {S}.tblHallazgos ADD CONSTRAINT FK_tblHallazgos_Extra FOREIGN KEY (ExtraId) REFERENCES {S}.tblInspeccionExtras (Id)'),
(N'tblHallazgos: el hallazgo de un extra no tiene zona y es del inspector', N'
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N~CK_tblHallazgos_Extra~ AND parent_object_id = OBJECT_ID(N~{S}.tblHallazgos~))
    ALTER TABLE {S}.tblHallazgos ADD CONSTRAINT CK_tblHallazgos_Extra CHECK (ExtraId IS NULL OR (ZonaId IS NULL AND Origen = ~inspector~))'),
(N'tblHallazgos: un extra tiene a lo sumo un hallazgo', N'
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N~UQ_tblHallazgos_Extra~ AND object_id = OBJECT_ID(N~{S}.tblHallazgos~))
    CREATE UNIQUE INDEX UQ_tblHallazgos_Extra ON {S}.tblHallazgos (ExtraId) WHERE ExtraId IS NOT NULL');

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

-- Resultado (esperado: Columna = 1, LlaveForanea = 1, Restriccion = 1, IndiceUnico = 1).
DECLARE @cuentas nvarchar(max) = REPLACE(N'
SELECT
    (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N~{S}.tblHallazgos~) AND name = N~ExtraId~) AS Columna,
    (SELECT COUNT(*) FROM sys.foreign_keys WHERE name = N~FK_tblHallazgos_Extra~ AND parent_object_id = OBJECT_ID(N~{S}.tblHallazgos~)) AS LlaveForanea,
    (SELECT COUNT(*) FROM sys.check_constraints WHERE name = N~CK_tblHallazgos_Extra~ AND parent_object_id = OBJECT_ID(N~{S}.tblHallazgos~)) AS Restriccion,
    (SELECT COUNT(*) FROM sys.indexes WHERE name = N~UQ_tblHallazgos_Extra~ AND object_id = OBJECT_ID(N~{S}.tblHallazgos~)) AS IndiceUnico;', N'{S}', @q);
SET @cuentas = REPLACE(@cuentas, N'~', N'''');
EXEC (@cuentas);
