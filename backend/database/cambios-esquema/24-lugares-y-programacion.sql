/* =============================================================================
   Lugares reales, fecha programada del viaje y destino por unidad, para una base de IA Autos YA EXISTENTE (con 23-novedad-de-proceso.sql aplicado). Se ejecuta UNA vez (si ya esta aplicado, no
   cambia nada) y todo ocurre en UNA transaccion: si algo falla se revierte completo.

   Que hace:
     1. tblLugares: el catalogo de lugares REALES (CEDI Envigado, Concesionario Autolarte Medellin...). Nombre (unico), TipoId (llave foranea a tblPuntosCustodia, que queda como catalogo de TIPOS:
        CEDI 1, Origen, Certificadora, Destino, CEDI 2 y Concesionario), Ciudad, Activo y Orden; mas Latitud, Longitud y RadioMetros, que admiten nulo (un lugar sin ubicacion es valido; las coordenadas
        van juntas o ninguna). NO se siembra ningun lugar: la lista la entrega el negocio (PAB-001).
     2. tblViajes gana FechaProgramada (cuando esta programado el cargue, con zona horaria; nulo) y LugarCargueId (llave foranea al lugar de cargue real; nulo). Los viajes que ya existen quedan con nulos
        y nada se migra: OrigenId y DestinoId siguen intactos para el historico.
     3. tblViajeVehiculos gana LugarDestinoId (el destino de CADA unidad; llave foranea al lugar; nulo).
     4. vwAdopcion agrupa por el DIA PROGRAMADO (hora de Colombia) cuando el viaje tiene FechaProgramada y por el dia de creacion cuando no (SUP-001). Mantiene la agrupacion por origen y destino del viaje
        (la dimension se revisa con Calidad, PAB-002).

   Seguridad de los datos: no borra ni cambia ningun dato; solo crea la tabla, agrega columnas nulas y reemplaza la vista.

   Como ejecutarlo: abrir en SSMS, DBeaver o sqlcmd CONECTADO a la base de IA Autos, ajustar @esquema y ejecutar completo, en UNA sola ejecucion de script, con un usuario con permiso para
   crear tablas y vistas (no el usuario de la aplicacion). Ejecutarlo ANTES de desplegar la API.
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
IF OBJECT_ID(@q + N'.tblPuntosCustodia', N'U') IS NULL OR OBJECT_ID(@q + N'.tblViajes', N'U') IS NULL OR OBJECT_ID(@q + N'.tblViajeVehiculos', N'U') IS NULL
    THROW 50001, N'Faltan las tablas tblPuntosCustodia, tblViajes o tblViajeVehiculos de IA Autos en el esquema indicado: revise @esquema.', 1;
IF OBJECT_ID(@q + N'.vwAdopcion', N'V') IS NULL
    THROW 50002, N'Falta la vista vwAdopcion: ejecute antes 20-vistas-de-indicadores.sql.', 1;

-- Pasos en orden. {S} = esquema entre corchetes; ~ = comilla simple dentro del texto.
DECLARE @paso TABLE (n int IDENTITY(1, 1) PRIMARY KEY, descripcion nvarchar(100), s nvarchar(max));

INSERT @paso (descripcion, s) VALUES
(N'tblLugares', N'
IF OBJECT_ID(N~{S}.tblLugares~, N~U~) IS NULL
BEGIN
    CREATE TABLE {S}.tblLugares
    (
        Id         uniqueidentifier  NOT NULL CONSTRAINT PK_tblLugares PRIMARY KEY,
        Nombre     nvarchar(100)     NOT NULL,
        TipoId     uniqueidentifier  NOT NULL,
        Ciudad     nvarchar(100)     NOT NULL,
        Latitud    float             NULL,
        Longitud   float             NULL,
        RadioMetros int              NULL,
        Orden      int               NOT NULL CONSTRAINT DF_tblLugares_Orden DEFAULT (0),
        Activo     bit               NOT NULL CONSTRAINT DF_tblLugares_Activo DEFAULT (1),
        CreatedAt  datetimeoffset(3) NOT NULL CONSTRAINT DF_tblLugares_CreatedAt DEFAULT (SYSDATETIMEOFFSET()),
        UpdatedAt  datetimeoffset(3) NOT NULL CONSTRAINT DF_tblLugares_UpdatedAt DEFAULT (SYSDATETIMEOFFSET()),
        CONSTRAINT UQ_tblLugares_Nombre UNIQUE (Nombre),
        CONSTRAINT FK_tblLugares_Tipo FOREIGN KEY (TipoId) REFERENCES {S}.tblPuntosCustodia (Id),
        CONSTRAINT CK_tblLugares_Nombre CHECK (LEN(LTRIM(RTRIM(Nombre))) > 0),
        CONSTRAINT CK_tblLugares_Ciudad CHECK (LEN(LTRIM(RTRIM(Ciudad))) > 0),
        CONSTRAINT CK_tblLugares_Orden CHECK (Orden >= 0),
        CONSTRAINT CK_tblLugares_Latitud CHECK (Latitud IS NULL OR (Latitud BETWEEN -90 AND 90)),
        CONSTRAINT CK_tblLugares_Longitud CHECK (Longitud IS NULL OR (Longitud BETWEEN -180 AND 180)),
        -- Las coordenadas van juntas o ninguna.
        CONSTRAINT CK_tblLugares_Coordenadas CHECK ((Latitud IS NULL AND Longitud IS NULL) OR (Latitud IS NOT NULL AND Longitud IS NOT NULL)),
        CONSTRAINT CK_tblLugares_Radio CHECK (RadioMetros IS NULL OR RadioMetros > 0)
    );
    CREATE INDEX IX_tblLugares_Tipo ON {S}.tblLugares (TipoId);
END'),
(N'tblViajes: fecha programada y lugar de cargue', N'
IF COL_LENGTH(N~{S}.tblViajes~, N~FechaProgramada~) IS NULL
    ALTER TABLE {S}.tblViajes ADD FechaProgramada datetimeoffset(3) NULL;
IF COL_LENGTH(N~{S}.tblViajes~, N~LugarCargueId~) IS NULL
    ALTER TABLE {S}.tblViajes ADD LugarCargueId uniqueidentifier NULL'),
(N'tblViajes: llave foranea e indices', N'
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N~FK_tblViajes_LugarCargue~ AND parent_object_id = OBJECT_ID(N~{S}.tblViajes~))
    ALTER TABLE {S}.tblViajes ADD CONSTRAINT FK_tblViajes_LugarCargue FOREIGN KEY (LugarCargueId) REFERENCES {S}.tblLugares (Id);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N~IX_tblViajes_LugarCargue~ AND object_id = OBJECT_ID(N~{S}.tblViajes~))
    CREATE INDEX IX_tblViajes_LugarCargue ON {S}.tblViajes (LugarCargueId);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N~IX_tblViajes_FechaProgramada~ AND object_id = OBJECT_ID(N~{S}.tblViajes~))
    CREATE INDEX IX_tblViajes_FechaProgramada ON {S}.tblViajes (FechaProgramada)'),
(N'tblViajeVehiculos: lugar de destino de la unidad', N'
IF COL_LENGTH(N~{S}.tblViajeVehiculos~, N~LugarDestinoId~) IS NULL
    ALTER TABLE {S}.tblViajeVehiculos ADD LugarDestinoId uniqueidentifier NULL'),
(N'tblViajeVehiculos: llave foranea e indice', N'
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N~FK_tblViajeVehiculos_LugarDestino~ AND parent_object_id = OBJECT_ID(N~{S}.tblViajeVehiculos~))
    ALTER TABLE {S}.tblViajeVehiculos ADD CONSTRAINT FK_tblViajeVehiculos_LugarDestino FOREIGN KEY (LugarDestinoId) REFERENCES {S}.tblLugares (Id);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N~IX_tblViajeVehiculos_LugarDestino~ AND object_id = OBJECT_ID(N~{S}.tblViajeVehiculos~))
    CREATE INDEX IX_tblViajeVehiculos_LugarDestino ON {S}.tblViajeVehiculos (LugarDestinoId)'),
(N'vwAdopcion: por dia programado', N'
CREATE OR ALTER VIEW {S}.vwAdopcion
AS
SELECT x.Fecha, x.ClienteId, x.Operacion, x.OrigenId, x.DestinoId, x.Marca, x.Trailer,
       COUNT(*) AS Programados,
       SUM(x.ConSalidaDigital) AS ConSalidaDigital,
       CAST(ROUND(SUM(x.ConSalidaDigital) * 100.0 / COUNT(*), 2) AS decimal(5, 2)) AS Porcentaje
FROM (
    SELECT CAST(SWITCHOFFSET(COALESCE(vj.FechaProgramada, vj.CreatedAt), N~-05:00~) AS date) AS Fecha, v.ClienteId, v.Operacion, vj.OrigenId, vj.DestinoId, v.Marca, vj.Trailer,
           CASE WHEN EXISTS (SELECT 1
                             FROM {S}.tblInspecciones i
                             JOIN {S}.tblEtapas e ON e.Id = i.EtapaId
                             WHERE e.ViajeId = vv.ViajeId AND e.VehiculoId = vv.VehiculoId AND i.Momento = ~salida~)
                THEN 1 ELSE 0 END AS ConSalidaDigital
    FROM {S}.tblViajeVehiculos vv
    JOIN {S}.tblViajes vj ON vj.Id = vv.ViajeId
    JOIN {S}.tblVehiculos v ON v.Id = vv.VehiculoId
    WHERE vj.Estado <> ~cancelado~
) AS x
GROUP BY x.Fecha, x.ClienteId, x.Operacion, x.OrigenId, x.DestinoId, x.Marca, x.Trailer');

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

-- Resultado 1 (esperado: Tabla = 1; ColumnasViaje = 2; ColumnaUnidad = 1; Lugares = 0 en una base recien migrada; ViajesConFecha = 0 (nada se migra); DiaProgramadoEnLaVista = 1).
DECLARE @cuentas nvarchar(max) = REPLACE(N'
SELECT
    (SELECT COUNT(*) FROM sys.tables WHERE name = N~tblLugares~ AND schema_id = SCHEMA_ID(N~{E}~)) AS Tabla,
    (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N~{S}.tblViajes~) AND name IN (N~FechaProgramada~, N~LugarCargueId~)) AS ColumnasViaje,
    (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N~{S}.tblViajeVehiculos~) AND name = N~LugarDestinoId~) AS ColumnaUnidad,
    (SELECT COUNT(*) FROM {S}.tblLugares) AS Lugares,
    (SELECT COUNT(*) FROM {S}.tblViajes WHERE FechaProgramada IS NOT NULL) AS ViajesConFecha,
    (SELECT COUNT(*) FROM sys.sql_modules WHERE object_id = OBJECT_ID(N~{S}.vwAdopcion~) AND definition LIKE N~%FechaProgramada%~) AS DiaProgramadoEnLaVista', N'{S}', @q);
SET @cuentas = REPLACE(REPLACE(@cuentas, N'{E}', @esquema), N'~', N'''');
EXEC (@cuentas);

-- Resultado 2: un viaje de cada caso (el primero que haya) con el dia que le toca en la adopcion. Si todavia no hay viajes con fecha programada, la fila queda vacia y el ejemplo de abajo muestra la formula.
DECLARE @casos nvarchar(max) = REPLACE(N'
SELECT * FROM (
    SELECT TOP 1 N~Con fecha programada~ AS Caso, vj.CodigoMtrack, vj.CreatedAt, vj.FechaProgramada, CAST(SWITCHOFFSET(COALESCE(vj.FechaProgramada, vj.CreatedAt), N~-05:00~) AS date) AS DiaEnLaAdopcion
    FROM {S}.tblViajes vj WHERE vj.FechaProgramada IS NOT NULL ORDER BY vj.CreatedAt DESC) AS a
UNION ALL
SELECT * FROM (
    SELECT TOP 1 N~Sin fecha programada (por creacion)~ AS Caso, vj.CodigoMtrack, vj.CreatedAt, vj.FechaProgramada, CAST(SWITCHOFFSET(COALESCE(vj.FechaProgramada, vj.CreatedAt), N~-05:00~) AS date) AS DiaEnLaAdopcion
    FROM {S}.tblViajes vj WHERE vj.FechaProgramada IS NULL ORDER BY vj.CreatedAt DESC) AS b', N'{S}', @q);
SET @casos = REPLACE(@casos, N'~', N'''');
EXEC (@casos);

-- Resultado 3: la formula con dos viajes de ejemplo (no se guardan). Esperado: 2026-11-03 para el programado y 2026-11-01 para el que no tiene fecha programada.
SELECT x.Caso, x.CreatedAt, x.FechaProgramada, CAST(SWITCHOFFSET(COALESCE(x.FechaProgramada, x.CreatedAt), N'-05:00') AS date) AS DiaEnLaAdopcion
FROM (VALUES
    (N'Ejemplo con fecha programada', CAST('2026-11-01T20:00:00-05:00' AS datetimeoffset(3)), CAST('2026-11-03T06:00:00-05:00' AS datetimeoffset(3))),
    (N'Ejemplo sin fecha programada', CAST('2026-11-01T20:00:00-05:00' AS datetimeoffset(3)), CAST(NULL AS datetimeoffset(3)))) AS x (Caso, CreatedAt, FechaProgramada);
