/* =============================================================================
   Historial de seguimiento del hallazgo y vista de patrones por zona, para una base de IA Autos YA EXISTENTE (con
   10-zona-dano-y-hallazgo.sql aplicado). Se ejecuta UNA vez (si ya esta aplicado, no hace nada) y todo ocurre en UNA transaccion: si algo falla
   se revierte completo.

   Que hace:
     1. tblHallazgoSeguimiento: historial append-only del seguimiento de un hallazgo (accion seguimiento, revision o no_sale, con el estado
        anterior y el nuevo, el usuario, la nota, la foto del sello de quien valida y el detalle en JSON). Una fila nunca se modifica (error 51005)
        ni se borra (error 51003).
     2. Un trigger sincroniza tblHallazgos.EstadoSeguimiento: al insertar una fila de accion seguimiento, el hallazgo pasa al estado nuevo.
        Las filas de revision y de no_sale no lo cambian.
     3. vwPatronesPorCuadrante: hallazgos por zona (lado + numero, o zona provisional) para el mapa de calor y los patrones, con el proceso, el
        nivel, la clase, el cliente, la operacion y la fecha. Incluye los hallazgos de toda inspeccion cerrada aunque la unidad no haya terminado
        el viaje y excluye las novedades del conductor sin revisar y los hallazgos aun sin zona.

   Seguridad de los datos: no borra ni cambia ningun dato; solo crea la tabla, los triggers y la vista.

   Como ejecutarlo: abrir en SSMS, DBeaver o sqlcmd CONECTADO a la base de IA Autos, ajustar @esquema y ejecutar completo con un usuario con
   permiso para crear tablas, triggers y vistas (no el usuario de la aplicacion). Ejecutarlo ANTES de desplegar la API.
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
IF OBJECT_ID(@q + N'.tblHallazgos', N'U') IS NULL OR OBJECT_ID(@q + N'.tblUsuarios', N'U') IS NULL OR OBJECT_ID(@q + N'.tblEvidencias', N'U') IS NULL
   OR OBJECT_ID(@q + N'.tblInspecciones', N'U') IS NULL OR OBJECT_ID(@q + N'.tblEtapas', N'U') IS NULL OR OBJECT_ID(@q + N'.tblEtapasCatalogo', N'U') IS NULL
   OR OBJECT_ID(@q + N'.tblVehiculos', N'U') IS NULL OR OBJECT_ID(@q + N'.tblCuadrantes', N'U') IS NULL
    THROW 50001, N'Faltan tablas de IA Autos en el esquema indicado: revise @esquema.', 1;

IF COL_LENGTH(@q + N'.tblHallazgos', N'ZonaId') IS NULL OR OBJECT_ID(@q + N'.tblZonasDano', N'U') IS NULL
    THROW 50002, N'Falta la zona del hallazgo: ejecute antes 10-zona-dano-y-hallazgo.sql.', 1;

IF OBJECT_ID(@q + N'.tblHallazgoSeguimiento', N'U') IS NOT NULL AND OBJECT_ID(@q + N'.vwPatronesPorCuadrante', N'V') IS NOT NULL
   AND EXISTS (SELECT 1 FROM sys.triggers WHERE name = N'TR_tblHallazgoSeguimiento_Sincronizar' AND parent_id = OBJECT_ID(@q + N'.tblHallazgoSeguimiento'))
BEGIN
    PRINT N'El historial de seguimiento y la vista de patrones ya estan aplicados: no se hace nada.';
    RETURN;
END

-- Pasos en orden. {S} = esquema entre corchetes; ~ = comilla simple dentro del texto.
DECLARE @paso TABLE (n int IDENTITY(1, 1) PRIMARY KEY, descripcion nvarchar(100), s nvarchar(max));

INSERT @paso (descripcion, s) VALUES
(N'tblHallazgoSeguimiento', N'
IF OBJECT_ID(N~{S}.tblHallazgoSeguimiento~, N~U~) IS NULL
BEGIN
    CREATE TABLE {S}.tblHallazgoSeguimiento
    (
        Id                uniqueidentifier  NOT NULL CONSTRAINT PK_tblHallazgoSeguimiento PRIMARY KEY,
        HallazgoId        uniqueidentifier  NOT NULL,
        Accion            varchar(15)       NOT NULL,
        EstadoAnterior    varchar(15)       NULL,
        EstadoNuevo       varchar(15)       NULL,
        UsuarioId         uniqueidentifier  NOT NULL,
        Nota              nvarchar(1000)    NULL,
        EvidenciaSelloId  uniqueidentifier  NULL,
        Detalle           nvarchar(max)     NULL,
        CreatedAt         datetimeoffset(3) NOT NULL CONSTRAINT DF_tblHallazgoSeguimiento_CreatedAt DEFAULT (SYSDATETIMEOFFSET()),
        UpdatedAt         datetimeoffset(3) NOT NULL CONSTRAINT DF_tblHallazgoSeguimiento_UpdatedAt DEFAULT (SYSDATETIMEOFFSET()),
        CONSTRAINT FK_tblHallazgoSeguimiento_Hallazgo FOREIGN KEY (HallazgoId) REFERENCES {S}.tblHallazgos (Id),
        CONSTRAINT FK_tblHallazgoSeguimiento_Usuario FOREIGN KEY (UsuarioId) REFERENCES {S}.tblUsuarios (Id),
        CONSTRAINT FK_tblHallazgoSeguimiento_Sello FOREIGN KEY (EvidenciaSelloId) REFERENCES {S}.tblEvidencias (Id),
        CONSTRAINT CK_tblHallazgoSeguimiento_Accion CHECK (Accion IN (~seguimiento~, ~revision~, ~no_sale~)),
        CONSTRAINT CK_tblHallazgoSeguimiento_Estados CHECK (
            (EstadoAnterior IS NULL OR EstadoAnterior IN (~pendiente~, ~validado~, ~rechazado~, ~recuperado~, ~derogado~))
            AND (EstadoNuevo IS NULL OR EstadoNuevo IN (~pendiente~, ~validado~, ~rechazado~, ~recuperado~, ~derogado~))),
        CONSTRAINT CK_tblHallazgoSeguimiento_Cambio CHECK (Accion <> ~seguimiento~ OR EstadoNuevo IS NOT NULL),
        CONSTRAINT CK_tblHallazgoSeguimiento_Detalle CHECK (Detalle IS NULL OR ISJSON(Detalle) = 1)
    );
    CREATE INDEX IX_tblHallazgoSeguimiento_Hallazgo ON {S}.tblHallazgoSeguimiento (HallazgoId, CreatedAt);
END'),
(N'trigger: el historial no se modifica (51005)', N'
CREATE OR ALTER TRIGGER {S}.TR_tblHallazgoSeguimiento_NoModificar
ON {S}.tblHallazgoSeguimiento
INSTEAD OF UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 51005, ~El historial de seguimiento no se modifica~, 1;
END'),
(N'trigger: el historial no se borra (51003)', N'
CREATE OR ALTER TRIGGER {S}.TR_tblHallazgoSeguimiento_NoBorrar
ON {S}.tblHallazgoSeguimiento
INSTEAD OF DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 51003, ~Este registro es evidencia y no se puede borrar~, 1;
END'),
(N'trigger: el seguimiento actualiza el estado del hallazgo', N'
CREATE OR ALTER TRIGGER {S}.TR_tblHallazgoSeguimiento_Sincronizar
ON {S}.tblHallazgoSeguimiento
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE h SET h.EstadoSeguimiento = u.EstadoNuevo, h.UpdatedAt = SYSDATETIMEOFFSET()
    FROM {S}.tblHallazgos h
    JOIN (SELECT HallazgoId, EstadoNuevo, ROW_NUMBER() OVER (PARTITION BY HallazgoId ORDER BY CreatedAt DESC, Id DESC) AS Orden
          FROM inserted WHERE Accion = ~seguimiento~) u ON u.HallazgoId = h.Id AND u.Orden = 1;
END'),
(N'vista: patrones por zona', N'
CREATE OR ALTER VIEW {S}.vwPatronesPorCuadrante
AS
SELECT x.ZonaId, x.CuadranteId, x.Numero, x.Lado, x.ZonaCodigo, x.ZonaNombre, x.Proceso, x.NivelSeveridad, x.ClaseDano, x.ClienteId, x.Operacion, x.Fecha,
       COUNT(*) AS Total
FROM (
    SELECT z.Id AS ZonaId, z.CuadranteId, z.Numero, z.Lado, z.Codigo AS ZonaCodigo,
           CASE WHEN z.Lado IS NOT NULL THEN CONCAT(UPPER(LEFT(z.Lado, 1)), SUBSTRING(z.Lado, 2, 20), N~ ~, z.Numero, N~ ~, NCHAR(183), N~ ~, c.Nombre)
                WHEN z.Numero IS NOT NULL THEN CONCAT(z.Numero, N~ ~, NCHAR(183), N~ ~, c.Nombre)
                ELSE z.Nombre END AS ZonaNombre,
           ec.Codigo AS Proceso, h.NivelSeveridad, h.ClaseDano, v.ClienteId, v.Operacion, CAST(i.CerradaAt AS date) AS Fecha
    FROM {S}.tblHallazgos h
    JOIN {S}.tblZonasDano z ON z.Id = h.ZonaId
    LEFT JOIN {S}.tblCuadrantes c ON c.Id = z.CuadranteId
    JOIN {S}.tblInspecciones i ON i.Id = h.InspeccionId AND i.Estado = ~cerrada~
    JOIN {S}.tblEtapas e ON e.Id = i.EtapaId
    JOIN {S}.tblEtapasCatalogo ec ON ec.Id = e.EtapaCatalogoId
    JOIN {S}.tblVehiculos v ON v.Id = e.VehiculoId
    WHERE NOT (h.Origen = ~conductor~ AND h.RevisadoAt IS NULL)
) AS x
GROUP BY x.ZonaId, x.CuadranteId, x.Numero, x.Lado, x.ZonaCodigo, x.ZonaNombre, x.Proceso, x.NivelSeveridad, x.ClaseDano, x.ClienteId, x.Operacion, x.Fecha');

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

-- Resultado (esperado: 3 triggers del historial, la vista creada y su conteo de filas segun los hallazgos de inspecciones cerradas con zona).
DECLARE @cuentas nvarchar(max) = REPLACE(N'
SELECT N~triggers del historial~ AS Dato, COUNT(*) AS Total FROM sys.triggers WHERE parent_id = OBJECT_ID(N~{S}.tblHallazgoSeguimiento~) UNION ALL
SELECT N~filas del historial~, COUNT(*) FROM {S}.tblHallazgoSeguimiento UNION ALL
SELECT N~filas de la vista de patrones~, COUNT(*) FROM {S}.vwPatronesPorCuadrante UNION ALL
SELECT N~hallazgos contados por la vista~, ISNULL(SUM(Total), 0) FROM {S}.vwPatronesPorCuadrante', N'{S}', @q);
SET @cuentas = REPLACE(@cuentas, N'~', N'''');
EXEC (@cuentas);
