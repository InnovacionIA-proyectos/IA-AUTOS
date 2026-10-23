/* =============================================================================
   Indices de consulta de los hallazgos, para una base de IA Autos YA EXISTENTE (con 10-zona-dano-y-hallazgo.sql aplicado). Se ejecuta UNA vez
   (si ya esta aplicado, no hace nada) y todo ocurre en UNA transaccion: si algo falla se revierte completo.

   Que hace:
     1. Reemplaza IX_tblHallazgos_Parte_Clase (parte del vehiculo y clase, del modelo anterior) por IX_tblHallazgos_Zona_Clase: zona (lado + numero) y
        clase, con la inspeccion incluida. Lo usan la propuesta de hallazgo preexistente y el mapa de calor.
     2. Crea IX_tblHallazgos_Seguimiento: estado de seguimiento, origen y fecha de revision. Lo usan la bandeja del coordinador y la de las novedades
        del conductor por revisar.

   Seguridad de los datos: no cambia ningun dato; solo cambia indices.

   Como ejecutarlo: abrir en SSMS, DBeaver o sqlcmd CONECTADO a la base de IA Autos, ajustar @esquema y ejecutar completo con un usuario con
   permiso para crear y borrar indices (no el usuario de la aplicacion).
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
IF OBJECT_ID(@q + N'.tblHallazgos', N'U') IS NULL
    THROW 50001, N'Falta tblHallazgos en el esquema indicado: revise @esquema.', 1;
IF COL_LENGTH(@q + N'.tblHallazgos', N'ZonaId') IS NULL OR COL_LENGTH(@q + N'.tblHallazgos', N'EstadoSeguimiento') IS NULL
    THROW 50002, N'Falta la zona del hallazgo: ejecute antes 10-zona-dano-y-hallazgo.sql.', 1;

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblHallazgos_Zona_Clase' AND object_id = OBJECT_ID(@q + N'.tblHallazgos'))
   AND EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblHallazgos_Seguimiento' AND object_id = OBJECT_ID(@q + N'.tblHallazgos'))
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblHallazgos_Parte_Clase' AND object_id = OBJECT_ID(@q + N'.tblHallazgos'))
BEGIN
    PRINT N'Los indices de los hallazgos ya estan aplicados: no se hace nada.';
    RETURN;
END

-- Pasos en orden. {S} = esquema entre corchetes; ~ = comilla simple dentro del texto.
DECLARE @paso TABLE (n int IDENTITY(1, 1) PRIMARY KEY, descripcion nvarchar(100), s nvarchar(max));

INSERT @paso (descripcion, s) VALUES
(N'se quita el indice por parte del vehiculo y clase', N'
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N~IX_tblHallazgos_Parte_Clase~ AND object_id = OBJECT_ID(N~{S}.tblHallazgos~))
    DROP INDEX IX_tblHallazgos_Parte_Clase ON {S}.tblHallazgos'),
(N'indice por zona y clase, con la inspeccion incluida', N'
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N~IX_tblHallazgos_Zona_Clase~ AND object_id = OBJECT_ID(N~{S}.tblHallazgos~))
    CREATE INDEX IX_tblHallazgos_Zona_Clase ON {S}.tblHallazgos (ZonaId, ClaseDano) INCLUDE (InspeccionId)'),
(N'indice por estado de seguimiento, origen y fecha de revision', N'
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N~IX_tblHallazgos_Seguimiento~ AND object_id = OBJECT_ID(N~{S}.tblHallazgos~))
    CREATE INDEX IX_tblHallazgos_Seguimiento ON {S}.tblHallazgos (EstadoSeguimiento, Origen, RevisadoAt)');

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

-- Resultado (esperado: los dos indices nuevos y ninguno del modelo anterior).
DECLARE @cuentas nvarchar(max) = REPLACE(N'
SELECT i.name AS Indice, CAST(STRING_AGG(c.name, N~, ~) WITHIN GROUP (ORDER BY ic.key_ordinal) AS nvarchar(200)) AS Columnas
FROM sys.indexes i
JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 0
JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
WHERE i.object_id = OBJECT_ID(N~{S}.tblHallazgos~) AND i.name IN (N~IX_tblHallazgos_Zona_Clase~, N~IX_tblHallazgos_Seguimiento~, N~IX_tblHallazgos_Parte_Clase~)
GROUP BY i.name', N'{S}', @q);
SET @cuentas = REPLACE(@cuentas, N'~', N'''');
EXEC (@cuentas);
