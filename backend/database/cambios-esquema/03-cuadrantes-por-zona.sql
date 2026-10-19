/* =============================================================================
   Cuadrantes por zona, para una base de IA Autos que YA tiene los catalogos maestros (02-catalogos-maestros.sql).
   Se ejecuta UNA vez (si ya esta aplicado, no hace nada) y todo ocurre en UNA transaccion: si algo falla se revierte completo.

   Por que: un mismo cuadrante (por ejemplo la puerta delantera) se ve desde varias vistas, pero tblCuadrantes tenia una sola vista
   por fila y el numero unico, asi que no podia dibujarse en mas de una. Ahora:
     - tblCuadrantes queda como la ZONA: tipo de vehiculo, numero, nombre, orden y estado.
     - tblCuadranteVistas guarda el POLIGONO de cada zona en cada vista (una fila por aparicion).

   Que hace:
     1. Crea tblCuadranteVistas (borrar un cuadrante borra sus poligonos; una vista con poligonos no se puede borrar).
     2. Copia a ella la vista y la geometria de los cuadrantes que ya existan (los que no tengan geometria no tenian poligono que
        conservar y quedan como zonas sin dibujar).
     3. Quita de tblCuadrantes las columnas VistaFotoId y Geometria y sus restricciones.

   Como ejecutarlo: abrir en SSMS (o sqlcmd) CONECTADO a la base de IA Autos, ajustar @esquema y ejecutar con un usuario con
   permiso para crear tablas (no el usuario de la aplicacion). Luego dar permisos a la aplicacion si no los tiene por esquema:
     GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::<esquema> TO <usuario_de_la_app>;
   Despues se cargan los cuadrantes con 04-cargar-cuadrantes-ficha.sql.
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
IF OBJECT_ID(@q + N'.tblCuadrantes', N'U') IS NULL OR OBJECT_ID(@q + N'.tblVistasFoto', N'U') IS NULL
    THROW 50001, N'Falta tblCuadrantes o tblVistasFoto: ejecute antes 02-catalogos-maestros.sql (y revise @esquema).', 1;

IF COL_LENGTH(@q + N'.tblCuadrantes', N'VistaFotoId') IS NULL
BEGIN
    PRINT N'Los cuadrantes ya estan por zona: no se hace nada.';
    RETURN;
END

-- Pasos en orden. {S} = esquema entre corchetes; ~ = comilla simple dentro del texto.
DECLARE @paso TABLE (n int IDENTITY(1, 1) PRIMARY KEY, descripcion nvarchar(100), s nvarchar(max));

INSERT @paso (descripcion, s) VALUES
(N'tblCuadranteVistas', N'
CREATE TABLE {S}.tblCuadranteVistas
(
    Id            uniqueidentifier  NOT NULL CONSTRAINT PK_tblCuadranteVistas PRIMARY KEY,
    CuadranteId   uniqueidentifier  NOT NULL,
    VistaFotoId   uniqueidentifier  NOT NULL,
    Geometria     nvarchar(max)     NOT NULL,
    CreatedAt     datetimeoffset(3) NOT NULL CONSTRAINT DF_tblCuadranteVistas_CreatedAt DEFAULT (SYSDATETIMEOFFSET()),
    UpdatedAt     datetimeoffset(3) NOT NULL CONSTRAINT DF_tblCuadranteVistas_UpdatedAt DEFAULT (SYSDATETIMEOFFSET()),
    CONSTRAINT FK_tblCuadranteVistas_Cuadrante FOREIGN KEY (CuadranteId) REFERENCES {S}.tblCuadrantes (Id) ON DELETE CASCADE,
    CONSTRAINT FK_tblCuadranteVistas_Vista FOREIGN KEY (VistaFotoId) REFERENCES {S}.tblVistasFoto (Id),
    CONSTRAINT UQ_tblCuadranteVistas_Cuadrante_Vista UNIQUE (CuadranteId, VistaFotoId),
    CONSTRAINT CK_tblCuadranteVistas_Geometria CHECK (ISJSON(Geometria) = 1)
)'),
(N'tblCuadranteVistas: indice por vista', N'CREATE INDEX IX_tblCuadranteVistas_Vista ON {S}.tblCuadranteVistas (VistaFotoId)'),
(N'copiar la vista y la geometria de los cuadrantes existentes', N'
INSERT INTO {S}.tblCuadranteVistas (Id, CuadranteId, VistaFotoId, Geometria)
SELECT NEWID(), Id, VistaFotoId, Geometria FROM {S}.tblCuadrantes WHERE Geometria IS NOT NULL AND ISJSON(Geometria) = 1'),
(N'quitar la restriccion de geometria de tblCuadrantes', N'ALTER TABLE {S}.tblCuadrantes DROP CONSTRAINT CK_tblCuadrantes_Geometria'),
(N'quitar la llave foranea a la vista de tblCuadrantes', N'ALTER TABLE {S}.tblCuadrantes DROP CONSTRAINT FK_tblCuadrantes_Vista'),
(N'quitar el indice por vista de tblCuadrantes', N'DROP INDEX IX_tblCuadrantes_Vista ON {S}.tblCuadrantes'),
(N'quitar la columna Geometria de tblCuadrantes', N'ALTER TABLE {S}.tblCuadrantes DROP COLUMN Geometria'),
(N'quitar la columna VistaFotoId de tblCuadrantes', N'ALTER TABLE {S}.tblCuadrantes DROP COLUMN VistaFotoId');

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

-- Resultado: zonas y poligonos que quedaron.
DECLARE @cuentas nvarchar(max) = REPLACE(N'
SELECT N~cuadrantes (zonas)~ AS Catalogo, COUNT(*) AS Valores FROM {S}.tblCuadrantes UNION ALL
SELECT N~poligonos por vista~, COUNT(*) FROM {S}.tblCuadranteVistas', N'{S}', @q);
SET @cuentas = REPLACE(@cuentas, N'~', N'''');
EXEC (@cuentas);
