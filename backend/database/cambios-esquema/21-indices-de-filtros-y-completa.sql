/* =============================================================================
   Indices de los filtros de los listados y estado "completa" de la unidad en el viaje, para una base de IA Autos YA EXISTENTE. Se ejecuta UNA vez (si ya esta aplicado, no hace nada) y todo
   ocurre en UNA transaccion: si algo falla se revierte completo.

   Que hace:
     1. Indices para los filtros de los listados paginados (los nombres con "Filtros" son nuevos; IX_tblVehiculos_Activo ya existia y solo se comprueba):
          tblVehiculos (ClienteId, Operacion, Marca, CreatedAt) y (Activo)
          tblOrdenesSofasa (StatusCode, DestinationCity, DatetimeEvent)
          tblInspecciones (ViajeId, Momento, Estado, Fecha)
          tblViajeVehiculos (EstadoUnidad)
     2. tblViajeVehiculos.Completa (bit, 0 por defecto): 1 cuando el VIN ya completo su ruta en ese viaje, con LA MISMA regla de vwViajesCompletos (todas sus etapas del viaje, y al menos una,
        con informe cerrado, y la unidad no esta marcada "no_sale"). Se guarda en la fila en lugar de calcularse con la vista sobre miles de unidades en cada listado. No puede ser una columna
        calculada de SQL Server (no admite consultas a otras tablas): la mantienen triggers al cambiar lo que la determina.
     3. dbo.usp_RecalcularCompleta: recalcula Completa de todas las filas con la regla de la vista (solo toca las que cambian). Se usa aqui para llenar la columna con los datos existentes y
        hay que ejecutarla despues de cualquier carga o limpieza que apague los triggers (DISABLE TRIGGER).
     4. Los triggers que la mantienen: TR_tblInspecciones_Completa (al crear, cerrar o borrar un informe), TR_tblEtapas_Completa (al agregar o quitar una etapa) y
        TR_tblViajeVehiculos_Completa (al agregar un VIN a un viaje o cambiar el estado de la unidad).

   Seguridad de los datos: no cambia ningun dato salvo la columna nueva Completa; la vista vwViajesCompletos no se toca (sigue siendo la definicion de referencia).

   Como ejecutarlo: abrir en SSMS, DBeaver o sqlcmd CONECTADO a la base de IA Autos, ajustar @esquema y ejecutar completo, en UNA sola ejecucion de script, con un usuario con permiso para
   crear indices, triggers y procedimientos (no el usuario de la aplicacion). Exige que ya este aplicado 14-sello-y-unidad-no-sale.sql (EstadoUnidad). Ejecutarlo ANTES de desplegar la API.
   Con muchas unidades los indices tardan unos segundos y el llenado de Completa lee todas las etapas una vez.
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
IF OBJECT_ID(@q + N'.tblViajeVehiculos', N'U') IS NULL OR OBJECT_ID(@q + N'.tblInspecciones', N'U') IS NULL OR OBJECT_ID(@q + N'.tblOrdenesSofasa', N'U') IS NULL
    THROW 50001, N'Faltan tablas de IA Autos en el esquema indicado: revise @esquema.', 1;
IF COL_LENGTH(@q + N'.tblViajeVehiculos', N'EstadoUnidad') IS NULL
    THROW 50002, N'Falta tblViajeVehiculos.EstadoUnidad: aplique antes 14-sello-y-unidad-no-sale.sql.', 1;
IF OBJECT_ID(@q + N'.vwViajesCompletos', N'V') IS NULL
    THROW 50003, N'Falta la vista vwViajesCompletos: aplique antes los cambios de esquema anteriores.', 1;

-- Pasos en orden. {S} = esquema entre corchetes; ~ = comilla simple dentro del texto.
DECLARE @paso TABLE (n int IDENTITY(1, 1) PRIMARY KEY, descripcion nvarchar(100), s nvarchar(max));

INSERT @paso (descripcion, s) VALUES
(N'indices de tblVehiculos', N'
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N~IX_tblVehiculos_Filtros~ AND object_id = OBJECT_ID(N~{S}.tblVehiculos~))
    EXEC (N~CREATE INDEX IX_tblVehiculos_Filtros ON {S}.tblVehiculos (ClienteId, Operacion, Marca, CreatedAt)~);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N~IX_tblVehiculos_Activo~ AND object_id = OBJECT_ID(N~{S}.tblVehiculos~))
    EXEC (N~CREATE INDEX IX_tblVehiculos_Activo ON {S}.tblVehiculos (Activo)~)'),
(N'indice de tblOrdenesSofasa', N'
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N~IX_tblOrdenesSofasa_Filtros~ AND object_id = OBJECT_ID(N~{S}.tblOrdenesSofasa~))
    EXEC (N~CREATE INDEX IX_tblOrdenesSofasa_Filtros ON {S}.tblOrdenesSofasa (StatusCode, DestinationCity, DatetimeEvent)~)'),
(N'indice de tblInspecciones', N'
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N~IX_tblInspecciones_Filtros~ AND object_id = OBJECT_ID(N~{S}.tblInspecciones~))
    EXEC (N~CREATE INDEX IX_tblInspecciones_Filtros ON {S}.tblInspecciones (ViajeId, Momento, Estado, Fecha)~)'),
(N'indice de tblViajeVehiculos', N'
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N~IX_tblViajeVehiculos_EstadoUnidad~ AND object_id = OBJECT_ID(N~{S}.tblViajeVehiculos~))
    EXEC (N~CREATE INDEX IX_tblViajeVehiculos_EstadoUnidad ON {S}.tblViajeVehiculos (EstadoUnidad)~)'),
(N'columna tblViajeVehiculos.Completa', N'
IF COL_LENGTH(N~{S}.tblViajeVehiculos~, N~Completa~) IS NULL
    EXEC (N~ALTER TABLE {S}.tblViajeVehiculos ADD Completa bit NOT NULL CONSTRAINT DF_tblViajeVehiculos_Completa DEFAULT (0)~)'),
(N'procedimiento usp_RecalcularCompleta', N'
CREATE OR ALTER PROCEDURE {S}.usp_RecalcularCompleta
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE vv SET Completa = n.Valor
    FROM {S}.tblViajeVehiculos vv
    CROSS APPLY (SELECT CAST(CASE WHEN EXISTS (SELECT 1 FROM {S}.vwViajesCompletos c WHERE c.ViajeId = vv.ViajeId AND c.VehiculoId = vv.VehiculoId) THEN 1 ELSE 0 END AS bit) AS Valor) n
    WHERE vv.Completa <> n.Valor;
END'),
(N'trigger de tblInspecciones', N'
CREATE OR ALTER TRIGGER {S}.TR_tblInspecciones_Completa
ON {S}.tblInspecciones
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    -- Solo importa un informe nuevo, uno borrado o un cambio de estado.
    IF EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted) AND NOT UPDATE(Estado) RETURN;
    UPDATE vv SET Completa = n.Valor
    FROM {S}.tblViajeVehiculos vv
    JOIN (SELECT e.ViajeId, e.VehiculoId FROM inserted i JOIN {S}.tblEtapas e ON e.Id = i.EtapaId
          UNION
          SELECT e.ViajeId, e.VehiculoId FROM deleted d JOIN {S}.tblEtapas e ON e.Id = d.EtapaId) p ON p.ViajeId = vv.ViajeId AND p.VehiculoId = vv.VehiculoId
    CROSS APPLY (SELECT CAST(CASE WHEN EXISTS (SELECT 1 FROM {S}.vwViajesCompletos c WHERE c.ViajeId = vv.ViajeId AND c.VehiculoId = vv.VehiculoId) THEN 1 ELSE 0 END AS bit) AS Valor) n
    WHERE vv.Completa <> n.Valor;
END'),
(N'trigger de tblEtapas', N'
CREATE OR ALTER TRIGGER {S}.TR_tblEtapas_Completa
ON {S}.tblEtapas
AFTER INSERT, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE vv SET Completa = n.Valor
    FROM {S}.tblViajeVehiculos vv
    JOIN (SELECT ViajeId, VehiculoId FROM inserted UNION SELECT ViajeId, VehiculoId FROM deleted) p ON p.ViajeId = vv.ViajeId AND p.VehiculoId = vv.VehiculoId
    CROSS APPLY (SELECT CAST(CASE WHEN EXISTS (SELECT 1 FROM {S}.vwViajesCompletos c WHERE c.ViajeId = vv.ViajeId AND c.VehiculoId = vv.VehiculoId) THEN 1 ELSE 0 END AS bit) AS Valor) n
    WHERE vv.Completa <> n.Valor;
END'),
(N'trigger de tblViajeVehiculos', N'
CREATE OR ALTER TRIGGER {S}.TR_tblViajeVehiculos_Completa
ON {S}.tblViajeVehiculos
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    -- Un VIN nuevo en el viaje o un cambio del estado de la unidad (una unidad "no_sale" no cuenta como completa). Cambiar solo Completa no vuelve a calcular nada.
    IF EXISTS (SELECT 1 FROM deleted) AND NOT UPDATE(EstadoUnidad) RETURN;
    UPDATE vv SET Completa = n.Valor
    FROM {S}.tblViajeVehiculos vv
    JOIN inserted p ON p.Id = vv.Id
    CROSS APPLY (SELECT CAST(CASE WHEN EXISTS (SELECT 1 FROM {S}.vwViajesCompletos c WHERE c.ViajeId = vv.ViajeId AND c.VehiculoId = vv.VehiculoId) THEN 1 ELSE 0 END AS bit) AS Valor) n
    WHERE vv.Completa <> n.Valor;
END'),
(N'Completa de los datos existentes', N'
EXEC {S}.usp_RecalcularCompleta');

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

-- Resultado (esperado: 5 indices, 1 columna, 1 procedimiento, 3 triggers y 0 filas con Completa distinto de la vista).
DECLARE @cuentas nvarchar(max) = REPLACE(N'
SELECT
    (SELECT COUNT(*) FROM sys.indexes WHERE name IN (N~IX_tblVehiculos_Filtros~, N~IX_tblVehiculos_Activo~, N~IX_tblOrdenesSofasa_Filtros~, N~IX_tblInspecciones_Filtros~, N~IX_tblViajeVehiculos_EstadoUnidad~)
        AND object_id IN (OBJECT_ID(N~{S}.tblVehiculos~), OBJECT_ID(N~{S}.tblOrdenesSofasa~), OBJECT_ID(N~{S}.tblInspecciones~), OBJECT_ID(N~{S}.tblViajeVehiculos~))) AS Indices,
    (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N~{S}.tblViajeVehiculos~) AND name = N~Completa~) AS Columna,
    (SELECT COUNT(*) FROM sys.procedures WHERE name = N~usp_RecalcularCompleta~ AND schema_id = SCHEMA_ID(N~{E}~)) AS Procedimiento,
    (SELECT COUNT(*) FROM sys.triggers WHERE name IN (N~TR_tblInspecciones_Completa~, N~TR_tblEtapas_Completa~, N~TR_tblViajeVehiculos_Completa~)) AS Triggers,
    (SELECT COUNT(*) FROM {S}.tblViajeVehiculos vv
      WHERE vv.Completa <> CASE WHEN EXISTS (SELECT 1 FROM {S}.vwViajesCompletos c WHERE c.ViajeId = vv.ViajeId AND c.VehiculoId = vv.VehiculoId) THEN 1 ELSE 0 END) AS FilasDistintasDeLaVista,
    (SELECT COUNT(*) FROM {S}.tblViajeVehiculos WHERE Completa = 1) AS Completas', N'{S}', @q);
SET @cuentas = REPLACE(REPLACE(@cuentas, N'{E}', @esquema), N'~', N'''');
EXEC (@cuentas);
