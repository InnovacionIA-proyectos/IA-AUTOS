/* =============================================================================
   Acciones de los niveles de severidad, para una base de IA Autos YA EXISTENTE. Se ejecuta UNA vez (si ya esta aplicado, no hace nada) y todo
   ocurre en UNA transaccion: si algo falla se revierte completo.

   Que hace:
     Completa tblNivelesSeveridad.Accion con lo que se hace con una unidad de cada nivel:
       v1_plus (V1+): Se recupera antes del despacho
       v1      (V1) : Se rechaza el vehiculo
       v2      (V2) : Validacion por Sofasa
       v3      (V3) : Se deroga (se acepta el defecto)
     Solo completa los niveles que aun no tienen accion: no pisa un texto que ya se haya editado en el catalogo.

   Seguridad de los datos: no borra nada ni cambia ninguna otra columna; solo escribe la accion donde esta vacia.

   Como ejecutarlo: abrir en SSMS, DBeaver o sqlcmd CONECTADO a la base de IA Autos, ajustar @esquema y ejecutar completo con un usuario con
   permiso para modificar datos (no el usuario de la aplicacion).
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
IF OBJECT_ID(@q + N'.tblNivelesSeveridad', N'U') IS NULL
    THROW 50001, N'Falta tblNivelesSeveridad en el esquema indicado: revise @esquema.', 1;
IF COL_LENGTH(@q + N'.tblNivelesSeveridad', N'Accion') IS NULL
    THROW 50002, N'Falta la columna Accion de tblNivelesSeveridad: ejecute antes 02-catalogos-maestros.sql.', 1;

-- Acciones por nivel (los acentos se escriben con NCHAR para que el script sea ASCII). La tabla temporal es visible desde el SQL dinamico.
IF OBJECT_ID(N'tempdb..#acciones') IS NOT NULL DROP TABLE #acciones;
CREATE TABLE #acciones (Codigo varchar(30) NOT NULL PRIMARY KEY, Accion nvarchar(500) NOT NULL);
INSERT #acciones (Codigo, Accion) VALUES
    ('v1_plus', N'Se recupera antes del despacho'),
    ('v1',      N'Se rechaza el veh' + NCHAR(237) + N'culo'),
    ('v2',      N'Validaci' + NCHAR(243) + N'n por Sofasa'),
    ('v3',      N'Se deroga (se acepta el defecto)');

DECLARE @pendientes int, @sql nvarchar(max);
SET @sql = REPLACE(N'
SELECT @n = COUNT(*) FROM {S}.tblNivelesSeveridad n JOIN #acciones a ON a.Codigo = n.Codigo WHERE n.Accion IS NULL', N'{S}', @q);
EXEC sp_executesql @sql, N'@n int OUTPUT', @n = @pendientes OUTPUT;

IF @pendientes = 0
BEGIN
    PRINT N'Las acciones de los niveles de severidad ya estan aplicadas: no se hace nada.';
    DROP TABLE #acciones;
    RETURN;
END

BEGIN TRY
    BEGIN TRANSACTION;
    SET @sql = REPLACE(N'
UPDATE n SET Accion = a.Accion, UpdatedAt = SYSDATETIMEOFFSET()
FROM {S}.tblNivelesSeveridad n JOIN #acciones a ON a.Codigo = n.Codigo
WHERE n.Accion IS NULL', N'{S}', @q);
    EXEC (@sql);
    PRINT CONCAT(N'OK  1. acciones completadas: ', @@ROWCOUNT);
    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    PRINT CONCAT(N'FALLO al completar las acciones: ', ERROR_MESSAGE());
    THROW;
END CATCH

-- Resultado (esperado: los cuatro niveles con su accion).
SET @sql = REPLACE(N'SELECT Codigo, Nombre, Accion FROM {S}.tblNivelesSeveridad ORDER BY Orden', N'{S}', @q);
EXEC (@sql);
DROP TABLE #acciones;
