/* =============================================================================
   El dato del lugar "admite cargue sin inspector", para una base de IA Autos YA EXISTENTE. Se ejecuta UNA vez (si ya esta aplicado, no cambia nada) y todo ocurre en UNA transaccion: si algo falla se revierte completo.

   Que hace:
     1. tblLugares.AdmiteCargueSinInspector (bit, 0 por defecto): si el lugar admite que el cargue lo haga el conductor cuando no hay inspector (la excepcion "No hay inspector" y su permiso). TODOS los lugares que ya existen quedan
        en 0 (no lo admiten, incluido CEDI Envigado): el coordinador de calidad lo activa lugar por lugar desde el catalogo de lugares.
     2. CK_tblInspecciones_MotivoSinInspector: el motivo de la excepcion "No hay inspector" puede guardarse tambien en una inspeccion de LLEGADA (la llegada del cargue que hace el conductor con la excepcion), no solo en la
        de salida. Las inspecciones que ya existen no cambian.

   Seguridad de los datos: solo agrega una columna con valor por defecto; no modifica ni borra ningun dato existente. Se ejecuta ANTES de desplegar la API.

   Como ejecutarlo: abrir en SSMS, DBeaver o sqlcmd CONECTADO a la base de IA Autos, ajustar @esquema y ejecutar completo, en UNA sola ejecucion de script, con un usuario con permiso para
   modificar tablas (no el usuario de la aplicacion).
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
IF OBJECT_ID(@q + N'.tblLugares', N'U') IS NULL OR OBJECT_ID(@q + N'.tblInspecciones', N'U') IS NULL
    THROW 50001, N'Faltan tblLugares o tblInspecciones de IA Autos en el esquema indicado: revise @esquema.', 1;

-- Pasos en orden. {S} = esquema entre corchetes; ~ = comilla simple dentro del texto.
DECLARE @paso TABLE (n int IDENTITY(1, 1) PRIMARY KEY, descripcion nvarchar(100), s nvarchar(max));

INSERT @paso (descripcion, s) VALUES
(N'tblLugares: AdmiteCargueSinInspector', N'
IF COL_LENGTH(N~{S}.tblLugares~, N~AdmiteCargueSinInspector~) IS NULL
    ALTER TABLE {S}.tblLugares ADD AdmiteCargueSinInspector bit NOT NULL CONSTRAINT DF_tblLugares_AdmiteCargueSinInspector DEFAULT (0)'),
(N'tblInspecciones: el motivo de la excepcion tambien en la llegada', N'
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N~CK_tblInspecciones_MotivoSinInspector~ AND parent_object_id = OBJECT_ID(N~{S}.tblInspecciones~) AND definition LIKE N~%llegada%~)
BEGIN
    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N~CK_tblInspecciones_MotivoSinInspector~ AND parent_object_id = OBJECT_ID(N~{S}.tblInspecciones~))
        ALTER TABLE {S}.tblInspecciones DROP CONSTRAINT CK_tblInspecciones_MotivoSinInspector;
    ALTER TABLE {S}.tblInspecciones ADD CONSTRAINT CK_tblInspecciones_MotivoSinInspector CHECK (MotivoSinInspector IS NULL OR Momento IN (~salida~, ~llegada~));
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

-- Resultado (esperado: ColumnaDelLugar = 1, MotivoEnLaLlegada = 1 y LugaresQueLoAdmiten = 0 en una base que no lo ha activado).
DECLARE @cuentas nvarchar(max) = REPLACE(N'
SELECT
    (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N~{S}.tblLugares~) AND name = N~AdmiteCargueSinInspector~ AND is_nullable = 0) AS ColumnaDelLugar,
    (SELECT COUNT(*) FROM sys.check_constraints WHERE name = N~CK_tblInspecciones_MotivoSinInspector~ AND parent_object_id = OBJECT_ID(N~{S}.tblInspecciones~) AND definition LIKE N~%llegada%~) AS MotivoEnLaLlegada,
    (SELECT COUNT(*) FROM {S}.tblLugares WHERE AdmiteCargueSinInspector = 1) AS LugaresQueLoAdmiten;', N'{S}', @q);
SET @cuentas = REPLACE(@cuentas, N'~', N'''');
EXEC (@cuentas);
