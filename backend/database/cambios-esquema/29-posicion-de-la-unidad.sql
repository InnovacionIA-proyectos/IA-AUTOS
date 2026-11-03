/* =============================================================================
   Posicion de la unidad en la ninera, para una base de IA Autos YA EXISTENTE (con 28-sin-personal-de-apoyo.sql aplicado). Se ejecuta UNA vez (si ya esta aplicado, no cambia nada) y todo ocurre en UNA
   transaccion: si algo falla se revierte completo.

   Que hace:
     1. tblViajeVehiculos: agrega la columna Posicion (tinyint, nula). La posicion de la unidad dentro de la ninera la registra la segunda inspeccion del cargue (la de momento "salida" de la etapa Cargue)
        y se libera al marcar la unidad "no sale" (lo hace la aplicacion). Todas las unidades existentes quedan sin posicion.
     2. CK_tblViajeVehiculos_Posicion: la posicion es nula o esta entre 1 y 7.
     3. UX_tblViajeVehiculos_Posicion: indice unico filtrado sobre (ViajeId, Posicion) para las filas con posicion: dos unidades del mismo viaje no comparten posicion y la misma posicion en viajes
        distintos es valida. Las unidades sin posicion no cuentan.

   Seguridad de los datos: no cambia ningun dato existente; solo agrega la columna (nula), su restriccion y su indice.

   Como ejecutarlo: abrir en SSMS, DBeaver o sqlcmd CONECTADO a la base de IA Autos, ajustar @esquema y ejecutar completo, en UNA sola ejecucion de script, con un usuario con permiso para
   alterar tablas (no el usuario de la aplicacion). Ejecutarlo ANTES de desplegar la API.
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
IF OBJECT_ID(@q + N'.tblViajeVehiculos', N'U') IS NULL
    THROW 50001, N'Falta la tabla tblViajeVehiculos de IA Autos en el esquema indicado: revise @esquema.', 1;

-- Pasos en orden. {S} = esquema entre corchetes; ~ = comilla simple dentro del texto.
DECLARE @paso TABLE (n int IDENTITY(1, 1) PRIMARY KEY, descripcion nvarchar(100), s nvarchar(max));

INSERT @paso (descripcion, s) VALUES
(N'tblViajeVehiculos: columna Posicion', N'
IF COL_LENGTH(N~{S}.tblViajeVehiculos~, N~Posicion~) IS NULL
    ALTER TABLE {S}.tblViajeVehiculos ADD Posicion tinyint NULL'),
(N'tblViajeVehiculos: la posicion esta entre 1 y 7', N'
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N~CK_tblViajeVehiculos_Posicion~ AND parent_object_id = OBJECT_ID(N~{S}.tblViajeVehiculos~))
    ALTER TABLE {S}.tblViajeVehiculos ADD CONSTRAINT CK_tblViajeVehiculos_Posicion CHECK (Posicion IS NULL OR Posicion BETWEEN 1 AND 7)'),
(N'tblViajeVehiculos: una posicion por unidad dentro del viaje', N'
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N~UX_tblViajeVehiculos_Posicion~ AND object_id = OBJECT_ID(N~{S}.tblViajeVehiculos~))
    CREATE UNIQUE INDEX UX_tblViajeVehiculos_Posicion ON {S}.tblViajeVehiculos (ViajeId, Posicion) WHERE Posicion IS NOT NULL');

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

-- Resultado 1 (esperado: Columna = 1, Restriccion = 1, Indice = 1 y UnidadesConPosicion = 0 en una base recien migrada).
DECLARE @cuentas nvarchar(max) = REPLACE(N'
SELECT
    (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N~{S}.tblViajeVehiculos~) AND name = N~Posicion~ AND is_nullable = 1) AS Columna,
    (SELECT COUNT(*) FROM sys.check_constraints WHERE name = N~CK_tblViajeVehiculos_Posicion~ AND parent_object_id = OBJECT_ID(N~{S}.tblViajeVehiculos~)) AS Restriccion,
    (SELECT COUNT(*) FROM sys.indexes WHERE name = N~UX_tblViajeVehiculos_Posicion~ AND object_id = OBJECT_ID(N~{S}.tblViajeVehiculos~) AND is_unique = 1 AND has_filter = 1) AS Indice,
    (SELECT COUNT(*) FROM {S}.tblViajeVehiculos WHERE Posicion IS NOT NULL) AS UnidadesConPosicion', N'{S}', @q);
SET @cuentas = REPLACE(@cuentas, N'~', N'''');
EXEC (@cuentas);
