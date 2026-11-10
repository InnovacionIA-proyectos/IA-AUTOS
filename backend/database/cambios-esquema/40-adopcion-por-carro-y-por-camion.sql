/* =============================================================================
   Adopcion por carro y por camion, solo con salidas cerradas, para una base de IA Autos YA EXISTENTE (con 39-permiso-sin-inspector.sql aplicado). Se ejecuta UNA vez (si ya esta aplicado, no cambia nada) y
   todo ocurre en UNA transaccion: si algo falla se revierte completo.

   Que hace (solo vistas; no cambia ninguna tabla ni ningun dato):
     1. vwAdopcion (por carro): una inspeccion de salida en BORRADOR deja de contar como adoptada (solo cuentan las cerradas) y la vista gana LugarDestinoId, el destino de cada unidad, para contar cada carro
        en su propio destino. El resto de la formula no cambia: programados por dia programado (los viajes anteriores, por la fecha de creacion) y los viajes cancelados no cuentan.
     2. vwAdopcionPorCamion (nueva): lo mismo contando cada viaje (camion) una sola vez: adoptado si alguna de sus unidades tiene su salida cerrada, agrupado por el destino del viaje (el de sus unidades si es uno
        solo; nulo si son varios o a alguna le falta).

   Seguridad de los datos: solo reemplaza y crea vistas (CREATE OR ALTER); repetirlo deja lo mismo. La cifra de adopcion que ya se muestra puede bajar al dejar de contar los borradores.

   Como ejecutarlo: abrir en SSMS, DBeaver o sqlcmd CONECTADO a la base de IA Autos, ajustar @esquema y ejecutar completo, en UNA sola ejecucion de script, con un usuario con permiso para
   crear vistas (no el usuario de la aplicacion). Ejecutarlo ANTES de desplegar la API.
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
IF OBJECT_ID(@q + N'.tblViajeVehiculos', N'U') IS NULL OR OBJECT_ID(@q + N'.tblInspecciones', N'U') IS NULL
    THROW 50001, N'Faltan las tablas tblViajeVehiculos o tblInspecciones de IA Autos en el esquema indicado: revise @esquema.', 1;
IF COL_LENGTH(@q + N'.tblViajeVehiculos', N'LugarDestinoId') IS NULL OR COL_LENGTH(@q + N'.tblViajes', N'FechaProgramada') IS NULL
    THROW 50002, N'Faltan el destino por unidad o la fecha programada del viaje: ejecute antes los scripts de cambios anteriores.', 1;

-- Pasos en orden. {S} = esquema entre corchetes; ~ = comilla simple dentro del texto.
DECLARE @paso TABLE (n int IDENTITY(1, 1) PRIMARY KEY, descripcion nvarchar(100), s nvarchar(max));

INSERT @paso (descripcion, s) VALUES
(N'vista vwAdopcion', N'
CREATE OR ALTER VIEW {S}.vwAdopcion
AS
SELECT x.Fecha, x.ClienteId, x.Operacion, x.OrigenId, x.DestinoId, x.Marca, x.Trailer, x.LugarDestinoId,
       COUNT(*) AS Programados,
       SUM(x.ConSalidaDigital) AS ConSalidaDigital,
       CAST(ROUND(SUM(x.ConSalidaDigital) * 100.0 / COUNT(*), 2) AS decimal(5, 2)) AS Porcentaje
FROM (
    SELECT CAST(SWITCHOFFSET(COALESCE(vj.FechaProgramada, vj.CreatedAt), N~-05:00~) AS date) AS Fecha, v.ClienteId, v.Operacion, vj.OrigenId, vj.DestinoId, v.Marca, vj.Trailer, vv.LugarDestinoId,
           CASE WHEN EXISTS (SELECT 1
                             FROM {S}.tblInspecciones i
                             JOIN {S}.tblEtapas e ON e.Id = i.EtapaId
                             WHERE e.ViajeId = vv.ViajeId AND e.VehiculoId = vv.VehiculoId AND i.Momento = ~salida~ AND i.Estado = ~cerrada~)
                THEN 1 ELSE 0 END AS ConSalidaDigital
    FROM {S}.tblViajeVehiculos vv
    JOIN {S}.tblViajes vj ON vj.Id = vv.ViajeId
    JOIN {S}.tblVehiculos v ON v.Id = vv.VehiculoId
    WHERE vj.Estado <> ~cancelado~
) AS x
GROUP BY x.Fecha, x.ClienteId, x.Operacion, x.OrigenId, x.DestinoId, x.Marca, x.Trailer, x.LugarDestinoId'),
(N'vista vwAdopcionPorCamion', N'
CREATE OR ALTER VIEW {S}.vwAdopcionPorCamion
AS
SELECT x.Fecha, x.ClienteId, x.Operacion, x.OrigenId, x.DestinoId, x.Marca, x.Trailer, x.LugarDestinoId,
       COUNT(*) AS Programados,
       SUM(x.ConSalidaDigital) AS ConSalidaDigital,
       CAST(ROUND(SUM(x.ConSalidaDigital) * 100.0 / COUNT(*), 2) AS decimal(5, 2)) AS Porcentaje
FROM (
    SELECT CAST(SWITCHOFFSET(COALESCE(vj.FechaProgramada, vj.CreatedAt), N~-05:00~) AS date) AS Fecha, p.ClienteId, p.Operacion, vj.OrigenId, vj.DestinoId, p.Marca, vj.Trailer,
           CASE WHEN d.Destinos = 1 AND d.SinDestino = 0 THEN CAST(d.DestinoTexto AS uniqueidentifier) END AS LugarDestinoId,
           CASE WHEN EXISTS (SELECT 1
                             FROM {S}.tblInspecciones i
                             JOIN {S}.tblEtapas e ON e.Id = i.EtapaId
                             WHERE e.ViajeId = vj.Id AND i.Momento = ~salida~ AND i.Estado = ~cerrada~)
                THEN 1 ELSE 0 END AS ConSalidaDigital
    FROM {S}.tblViajes vj
    CROSS APPLY (SELECT TOP 1 v.ClienteId, v.Operacion, v.Marca
                 FROM {S}.tblViajeVehiculos vv
                 JOIN {S}.tblVehiculos v ON v.Id = vv.VehiculoId
                 WHERE vv.ViajeId = vj.Id
                 ORDER BY v.Vin) p
    CROSS APPLY (SELECT COUNT(DISTINCT vv.LugarDestinoId) AS Destinos,
                        SUM(CASE WHEN vv.LugarDestinoId IS NULL THEN 1 ELSE 0 END) AS SinDestino,
                        MAX(CAST(vv.LugarDestinoId AS varchar(36))) AS DestinoTexto
                 FROM {S}.tblViajeVehiculos vv
                 WHERE vv.ViajeId = vj.Id) d
    WHERE vj.Estado <> ~cancelado~
) AS x
GROUP BY x.Fecha, x.ClienteId, x.Operacion, x.OrigenId, x.DestinoId, x.Marca, x.Trailer, x.LugarDestinoId');

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

-- Resultado (esperado: todo en 1).
DECLARE @cuentas nvarchar(max) = REPLACE(N'
SELECT
    (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N~{S}.vwAdopcion~) AND name = N~LugarDestinoId~) AS AdopcionConDestinoDeLaUnidad,
    (SELECT COUNT(*) FROM sys.sql_modules m WHERE m.object_id = OBJECT_ID(N~{S}.vwAdopcion~) AND m.definition LIKE N~%i.Estado = ~~cerrada~~%~) AS AdopcionSoloCerradas,
    (SELECT COUNT(*) FROM sys.views WHERE name = N~vwAdopcionPorCamion~ AND schema_id = SCHEMA_ID(N~{E}~)) AS AdopcionPorCamion;', N'{S}', @q);
SET @cuentas = REPLACE(REPLACE(@cuentas, N'{E}', @esquema), N'~', N'''');
EXEC (@cuentas);
