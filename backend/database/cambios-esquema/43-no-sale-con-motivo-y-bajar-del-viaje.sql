/* =============================================================================
   "No sale" con motivo de catalogo y "bajar del viaje", para una base de IA Autos YA EXISTENTE. Se ejecuta UNA vez (si ya esta aplicado, no cambia nada) y todo ocurre en UNA transaccion: si algo falla se revierte completo.

   Que hace:
     1. tblMotivosNoSale: el catalogo de motivos de "no sale" (codigo, nombre, si exige hallazgo, si exige texto, si vale solo donde se admite el cargue sin inspector) con sus seis motivos: dano_v1_plus,
        dano_reportado_conductor, no_llego_al_patio, faltan_documentos, no_esta_listo y otro (una fila que ya existe no se sobrescribe).
     2. tblViajeVehiculos: NoSaleMotivoCodigo (con su llave foranea al catalogo) y, para el estado nuevo "bajado", BajadoAt, BajadoPor y BajadoMotivo. NoSaleMotivo queda como el texto del motivo.
     3. Los "no sale" que ya existen se leen con el motivo dano_v1_plus y su texto de siempre (NoSaleMotivoCodigo = dano_v1_plus).
     4. CK_tblViajeVehiculos_EstadoUnidad admite "bajado" y CK_tblViajeVehiculos_NoSale pide lo que corresponde a cada estado: un "no sale" ya no exige hallazgo ni texto (eso lo decide el motivo en la
        aplicacion) y un "bajado" exige cuando, quien y el motivo y no tiene posicion.
     5. vwViajesCompletos y vwCobertura dejan de contar a la unidad bajada, igual que a la que no sale.

   Seguridad de los datos: solo agrega una tabla, columnas nulas, llaves foraneas, un estado y cambia dos restricciones y dos vistas; los unicos datos que toca son los "no sale" existentes, a los que les pone su
   codigo de motivo. Se ejecuta ANTES de desplegar la API.

   Como ejecutarlo: abrir en SSMS, DBeaver o sqlcmd CONECTADO a la base de IA Autos, ajustar @esquema y ejecutar completo, en UNA sola ejecucion de script, con un usuario con permiso para
   modificar tablas y vistas (no el usuario de la aplicacion).
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
IF OBJECT_ID(@q + N'.tblViajeVehiculos', N'U') IS NULL OR OBJECT_ID(@q + N'.tblUsuarios', N'U') IS NULL OR OBJECT_ID(@q + N'.vwViajesCompletos', N'V') IS NULL OR OBJECT_ID(@q + N'.vwCobertura', N'V') IS NULL
    THROW 50001, N'Faltan tblViajeVehiculos, tblUsuarios, vwViajesCompletos o vwCobertura de IA Autos en el esquema indicado: revise @esquema.', 1;
IF COL_LENGTH(@q + N'.tblViajeVehiculos', N'EstadoUnidad') IS NULL
    THROW 50002, N'tblViajeVehiculos no tiene EstadoUnidad: ejecute antes los scripts de cambios anteriores.', 1;

-- Pasos en orden. {S} = esquema entre corchetes; ~ = comilla simple dentro del texto.
DECLARE @paso TABLE (n int IDENTITY(1, 1) PRIMARY KEY, descripcion nvarchar(100), s nvarchar(max));

INSERT @paso (descripcion, s) VALUES
(N'tblMotivosNoSale: la tabla', N'
IF OBJECT_ID(N~{S}.tblMotivosNoSale~, N~U~) IS NULL
    CREATE TABLE {S}.tblMotivosNoSale
    (
        Id              uniqueidentifier  NOT NULL CONSTRAINT PK_tblMotivosNoSale PRIMARY KEY,
        Codigo          varchar(30)       NOT NULL,
        Nombre          nvarchar(100)     NOT NULL,
        ExigeHallazgo   bit               NOT NULL CONSTRAINT DF_tblMotivosNoSale_ExigeHallazgo DEFAULT (0),
        ExigeTexto      bit               NOT NULL CONSTRAINT DF_tblMotivosNoSale_ExigeTexto DEFAULT (0),
        SoloSinInspector bit              NOT NULL CONSTRAINT DF_tblMotivosNoSale_SoloSinInspector DEFAULT (0),
        Orden           int               NOT NULL CONSTRAINT DF_tblMotivosNoSale_Orden DEFAULT (0),
        Activo          bit               NOT NULL CONSTRAINT DF_tblMotivosNoSale_Activo DEFAULT (1),
        CreatedAt       datetimeoffset(3) NOT NULL CONSTRAINT DF_tblMotivosNoSale_CreatedAt DEFAULT (SYSDATETIMEOFFSET()),
        UpdatedAt       datetimeoffset(3) NOT NULL CONSTRAINT DF_tblMotivosNoSale_UpdatedAt DEFAULT (SYSDATETIMEOFFSET()),
        CONSTRAINT UQ_tblMotivosNoSale_Codigo UNIQUE (Codigo),
        CONSTRAINT CK_tblMotivosNoSale_Nombre CHECK (LEN(LTRIM(RTRIM(Nombre))) > 0)
    )'),
(N'tblMotivosNoSale: los seis motivos', N'
INSERT INTO {S}.tblMotivosNoSale (Id, Codigo, Nombre, ExigeHallazgo, ExigeTexto, SoloSinInspector, Orden)
SELECT NEWID(), v.Codigo, v.Nombre, v.ExigeHallazgo, v.ExigeTexto, v.SoloSinInspector, v.Orden
FROM (VALUES
    (~dano_v1_plus~, N~Da~ + NCHAR(241) + N~o V1+~, 1, 0, 0, 1),
    (~dano_reportado_conductor~, N~Da~ + NCHAR(241) + N~o reportado por el conductor~, 1, 0, 1, 2),
    (~no_llego_al_patio~, N~Carro no lleg~ + NCHAR(243) + N~ al patio~, 0, 0, 0, 3),
    (~faltan_documentos~, N~Faltan documentos~, 0, 0, 0, 4),
    (~no_esta_listo~, N~Carro no est~ + NCHAR(225) + N~ listo~, 0, 0, 0, 5),
    (~otro~, N~Otro~, 0, 1, 0, 6)
) AS v (Codigo, Nombre, ExigeHallazgo, ExigeTexto, SoloSinInspector, Orden)
WHERE NOT EXISTS (SELECT 1 FROM {S}.tblMotivosNoSale m WHERE m.Codigo = v.Codigo)'),
(N'tblViajeVehiculos: NoSaleMotivoCodigo', N'
IF COL_LENGTH(N~{S}.tblViajeVehiculos~, N~NoSaleMotivoCodigo~) IS NULL
    ALTER TABLE {S}.tblViajeVehiculos ADD NoSaleMotivoCodigo varchar(30) NULL'),
(N'tblViajeVehiculos: BajadoAt', N'
IF COL_LENGTH(N~{S}.tblViajeVehiculos~, N~BajadoAt~) IS NULL
    ALTER TABLE {S}.tblViajeVehiculos ADD BajadoAt datetimeoffset(3) NULL'),
(N'tblViajeVehiculos: BajadoPor', N'
IF COL_LENGTH(N~{S}.tblViajeVehiculos~, N~BajadoPor~) IS NULL
    ALTER TABLE {S}.tblViajeVehiculos ADD BajadoPor uniqueidentifier NULL'),
(N'tblViajeVehiculos: BajadoMotivo', N'
IF COL_LENGTH(N~{S}.tblViajeVehiculos~, N~BajadoMotivo~) IS NULL
    ALTER TABLE {S}.tblViajeVehiculos ADD BajadoMotivo nvarchar(300) NULL'),
(N'tblViajeVehiculos: llave foranea del motivo', N'
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N~FK_tblViajeVehiculos_NoSaleMotivo~ AND parent_object_id = OBJECT_ID(N~{S}.tblViajeVehiculos~))
    ALTER TABLE {S}.tblViajeVehiculos ADD CONSTRAINT FK_tblViajeVehiculos_NoSaleMotivo FOREIGN KEY (NoSaleMotivoCodigo) REFERENCES {S}.tblMotivosNoSale (Codigo)'),
(N'tblViajeVehiculos: llave foranea de quien baja', N'
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N~FK_tblViajeVehiculos_BajadoPor~ AND parent_object_id = OBJECT_ID(N~{S}.tblViajeVehiculos~))
    ALTER TABLE {S}.tblViajeVehiculos ADD CONSTRAINT FK_tblViajeVehiculos_BajadoPor FOREIGN KEY (BajadoPor) REFERENCES {S}.tblUsuarios (Id)'),
(N'los no sale anteriores quedan con el motivo dano V1+', N'
UPDATE {S}.tblViajeVehiculos SET NoSaleMotivoCodigo = ~dano_v1_plus~ WHERE EstadoUnidad = ~no_sale~ AND NoSaleMotivoCodigo IS NULL'),
(N'tblViajeVehiculos: estados de la unidad', N'
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N~CK_tblViajeVehiculos_EstadoUnidad~ AND parent_object_id = OBJECT_ID(N~{S}.tblViajeVehiculos~) AND definition LIKE N~%bajado%~)
BEGIN
    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N~CK_tblViajeVehiculos_EstadoUnidad~ AND parent_object_id = OBJECT_ID(N~{S}.tblViajeVehiculos~))
        ALTER TABLE {S}.tblViajeVehiculos DROP CONSTRAINT CK_tblViajeVehiculos_EstadoUnidad;
    ALTER TABLE {S}.tblViajeVehiculos ADD CONSTRAINT CK_tblViajeVehiculos_EstadoUnidad CHECK (EstadoUnidad IN (~activa~, ~no_sale~, ~bajado~));
END'),
(N'tblViajeVehiculos: lo que lleva cada estado', N'
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N~CK_tblViajeVehiculos_NoSale~ AND parent_object_id = OBJECT_ID(N~{S}.tblViajeVehiculos~) AND definition LIKE N~%NoSaleMotivoCodigo%~)
BEGIN
    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N~CK_tblViajeVehiculos_NoSale~ AND parent_object_id = OBJECT_ID(N~{S}.tblViajeVehiculos~))
        ALTER TABLE {S}.tblViajeVehiculos DROP CONSTRAINT CK_tblViajeVehiculos_NoSale;
    ALTER TABLE {S}.tblViajeVehiculos ADD CONSTRAINT CK_tblViajeVehiculos_NoSale CHECK ((EstadoUnidad = ~activa~ AND NoSaleMotivo IS NULL AND NoSaleMotivoCodigo IS NULL AND NoSaleHallazgoId IS NULL AND NoSaleMarcadoPor IS NULL AND NoSaleAt IS NULL AND ReemplazoVehiculoId IS NULL AND BajadoAt IS NULL AND BajadoPor IS NULL AND BajadoMotivo IS NULL) OR (EstadoUnidad = ~no_sale~ AND NoSaleMotivoCodigo IS NOT NULL AND NoSaleMarcadoPor IS NOT NULL AND NoSaleAt IS NOT NULL AND BajadoAt IS NULL AND BajadoPor IS NULL AND BajadoMotivo IS NULL) OR (EstadoUnidad = ~bajado~ AND BajadoAt IS NOT NULL AND BajadoPor IS NOT NULL AND BajadoMotivo IS NOT NULL AND Posicion IS NULL));
END'),
(N'vwViajesCompletos sin las unidades bajadas', N'
CREATE OR ALTER VIEW {S}.vwViajesCompletos
AS
SELECT x.ViajeId, x.VehiculoId
FROM (
    SELECT e.ViajeId, e.VehiculoId,
           CASE WHEN EXISTS (SELECT 1 FROM {S}.tblInspecciones i
                              WHERE i.EtapaId = e.Id AND i.Estado = ~cerrada~)
                THEN 1 ELSE 0 END AS Cerrada
    FROM {S}.tblEtapas e
) AS x
WHERE NOT EXISTS (SELECT 1 FROM {S}.tblViajeVehiculos vv
                   WHERE vv.ViajeId = x.ViajeId AND vv.VehiculoId = x.VehiculoId AND vv.EstadoUnidad IN (~no_sale~, ~bajado~))
GROUP BY x.ViajeId, x.VehiculoId
HAVING COUNT(*) > 0 AND COUNT(*) = SUM(x.Cerrada)'),
(N'vwCobertura sin las unidades bajadas', N'
CREATE OR ALTER VIEW {S}.vwCobertura
AS
SELECT vv.ViajeId, vv.VehiculoId, v.Vin, v.ClienteId, v.Operacion, vj.OrigenId, vj.DestinoId, v.Marca, vj.Placa, vj.Trailer, vj.TipoUnidadId, vj.ConductorId,
       d.FechaDespacho,
       CAST(SWITCHOFFSET(d.FechaDespacho, N~-05:00~) AS date) AS Fecha,
       r.RegistrosCerrados,
       CAST(CASE WHEN r.RegistrosCerrados >= 6 THEN 1 ELSE 0 END AS bit) AS Completa
FROM {S}.tblViajeVehiculos vv
JOIN {S}.tblViajes vj ON vj.Id = vv.ViajeId
JOIN {S}.tblVehiculos v ON v.Id = vv.VehiculoId
CROSS APPLY (
    SELECT MIN(i.CerradaAt) AS FechaDespacho
    FROM {S}.tblInspecciones i
    JOIN {S}.tblEtapas e ON e.Id = i.EtapaId
    JOIN {S}.tblEtapasCatalogo ec ON ec.Id = e.EtapaCatalogoId
    WHERE e.ViajeId = vv.ViajeId AND e.VehiculoId = vv.VehiculoId AND ec.Codigo = ~cargue~ AND i.Momento = ~salida~ AND i.Estado = ~cerrada~
) d
CROSS APPLY (
    SELECT CASE WHEN COUNT(*) > 6 THEN 6 ELSE COUNT(*) END AS RegistrosCerrados
    FROM (
        SELECT DISTINCT i.EtapaId, i.Momento
        FROM {S}.tblInspecciones i
        JOIN {S}.tblEtapas e ON e.Id = i.EtapaId
        WHERE e.ViajeId = vv.ViajeId AND e.VehiculoId = vv.VehiculoId AND i.Estado = ~cerrada~
    ) AS registros
) r
WHERE vv.EstadoUnidad NOT IN (~no_sale~, ~bajado~) AND d.FechaDespacho IS NOT NULL');

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

-- Resultado (esperado: Motivos = 6, ColumnasNuevas = 4, RestriccionesConBajado = 2, NoSaleSinCodigo = 0, VistasConBajado = 2).
DECLARE @cuentas nvarchar(max) = REPLACE(N'
SELECT
    (SELECT COUNT(*) FROM {S}.tblMotivosNoSale WHERE Codigo IN (N~dano_v1_plus~, N~dano_reportado_conductor~, N~no_llego_al_patio~, N~faltan_documentos~, N~no_esta_listo~, N~otro~)) AS Motivos,
    (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N~{S}.tblViajeVehiculos~) AND name IN (N~NoSaleMotivoCodigo~, N~BajadoAt~, N~BajadoPor~, N~BajadoMotivo~)) AS ColumnasNuevas,
    (SELECT COUNT(*) FROM sys.check_constraints WHERE name IN (N~CK_tblViajeVehiculos_EstadoUnidad~, N~CK_tblViajeVehiculos_NoSale~) AND parent_object_id = OBJECT_ID(N~{S}.tblViajeVehiculos~) AND definition LIKE N~%bajado%~) AS RestriccionesConBajado,
    (SELECT COUNT(*) FROM {S}.tblViajeVehiculos WHERE EstadoUnidad = N~no_sale~ AND NoSaleMotivoCodigo IS NULL) AS NoSaleSinCodigo,
    (SELECT COUNT(*) FROM sys.sql_modules WHERE object_id IN (OBJECT_ID(N~{S}.vwViajesCompletos~), OBJECT_ID(N~{S}.vwCobertura~)) AND definition LIKE N~%bajado%~) AS VistasConBajado;', N'{S}', @q);
SET @cuentas = REPLACE(@cuentas, N'~', N'''');
EXEC (@cuentas);
