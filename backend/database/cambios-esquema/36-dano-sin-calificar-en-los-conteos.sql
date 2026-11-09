/* =============================================================================
   El dano sin calificar entra en los conteos, para una base de IA Autos YA EXISTENTE (con 35-firmante-externo-y-datos-del-aviso.sql aplicado). Se ejecuta UNA vez (si ya esta aplicado, no
   cambia nada) y todo ocurre en UNA transaccion: si algo falla se revierte completo.

   Que hace (solo vistas; no cambia ninguna tabla ni ningun dato):
     1. vwHallazgosVentanaUmbral (la que cuenta los patrones de los avisos de umbral): ya no excluye las novedades del conductor aun sin revisar. Son dano sin calificar (sin zona, clase ni nivel) y cuentan
        por proceso, con la zona nula.
     2. vwAveriasPorConfiguracion (la del mapa de calor, los patrones y las averias del tablero): tampoco las excluye; el dano sin nivel cuenta por su zona y su clase y sale con el nivel nulo.
     3. vwIndicadorDiario (el indicador diario): gana la columna SinCalificar, los danos sin nivel de las unidades de ese dia, como una cifra aparte. ConNovedad (unidades con al menos un hallazgo V1 o V1+)
        no cambia ni se le suma nada, y la tasa de rechazo (que no usa estas vistas) tampoco.

   Seguridad de los datos: solo reemplaza vistas (CREATE OR ALTER); repetirlo deja lo mismo.

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
IF OBJECT_ID(@q + N'.tblHallazgos', N'U') IS NULL OR OBJECT_ID(@q + N'.vwCobertura', N'V') IS NULL
    THROW 50001, N'Faltan la tabla tblHallazgos o la vista vwCobertura de IA Autos en el esquema indicado: revise @esquema.', 1;
IF COL_LENGTH(@q + N'.tblZonasDano', N'AiagArea') IS NULL
    THROW 50002, N'Falta ejecutar antes 34-resembrar-dibujo-v6.sql (la zona del dibujo version 6 trae el codigo AIAG que usa la vista).', 1;

-- Pasos en orden. {S} = esquema entre corchetes; ~ = comilla simple dentro del texto.
DECLARE @paso TABLE (n int IDENTITY(1, 1) PRIMARY KEY, descripcion nvarchar(100), s nvarchar(max));

INSERT @paso (descripcion, s) VALUES
(N'vista vwHallazgosVentanaUmbral', N'
CREATE OR ALTER VIEW {S}.vwHallazgosVentanaUmbral
AS
SELECT h.Id AS HallazgoId, h.InspeccionId, i.ViajeId, e.VehiculoId, v.ClienteId,
       z.Id AS ZonaId, z.Numero, z.Lado,
       ec.Codigo AS Proceso, i.CerradaAt AS Fecha
FROM {S}.tblHallazgos h
JOIN {S}.tblInspecciones i ON i.Id = h.InspeccionId AND i.Estado = ~cerrada~
JOIN {S}.tblEtapas e ON e.Id = i.EtapaId
JOIN {S}.tblEtapasCatalogo ec ON ec.Id = e.EtapaCatalogoId
JOIN {S}.tblVehiculos v ON v.Id = e.VehiculoId
LEFT JOIN {S}.tblZonasDano z ON z.Id = h.ZonaId'),
(N'vista vwAveriasPorConfiguracion', N'
CREATE OR ALTER VIEW {S}.vwAveriasPorConfiguracion
AS
SELECT h.Id AS HallazgoId, h.InspeccionId, i.ViajeId, e.VehiculoId, v.Vin, v.ClienteId, v.Operacion, vj.OrigenId, vj.DestinoId, v.Marca, vj.Placa, vj.Trailer,
       vj.TipoUnidadId, tu.Codigo AS UnidadCodigo, tu.Nombre AS Unidad, vj.ConductorId, cu.Nombre AS ConductorNombre, cu.Cedula AS ConductorCedula,
       ec.Codigo AS Proceso, i.Momento, h.ClaseDano, h.TamanoDano, h.NivelSeveridad,
       z.Id AS ZonaId, z.Numero, z.Lado, z.AiagArea AS AiagArea, c.Nombre AS CuadranteNombre,
       CASE WHEN z.Lado IS NOT NULL THEN CONCAT(UPPER(LEFT(z.Lado, 1)), SUBSTRING(z.Lado, 2, 20), N~ ~, z.Numero, N~ ~, NCHAR(183), N~ ~, c.Nombre)
            WHEN z.Numero IS NOT NULL THEN CONCAT(z.Numero, N~ ~, NCHAR(183), N~ ~, c.Nombre)
            ELSE NULL END AS ZonaNombre,
       CAST(SWITCHOFFSET(i.CerradaAt, N~-05:00~) AS date) AS Fecha
FROM {S}.tblHallazgos h
JOIN {S}.tblInspecciones i ON i.Id = h.InspeccionId AND i.Estado = ~cerrada~
JOIN {S}.tblEtapas e ON e.Id = i.EtapaId
JOIN {S}.tblEtapasCatalogo ec ON ec.Id = e.EtapaCatalogoId
JOIN {S}.tblVehiculos v ON v.Id = e.VehiculoId
JOIN {S}.tblViajes vj ON vj.Id = i.ViajeId
LEFT JOIN {S}.tblTiposUnidadTransportadora tu ON tu.Id = vj.TipoUnidadId
LEFT JOIN {S}.tblUsuarios cu ON cu.Id = vj.ConductorId
LEFT JOIN {S}.tblZonasDano z ON z.Id = h.ZonaId
LEFT JOIN {S}.tblCuadrantes c ON c.Id = z.CuadranteId'),
(N'vista vwIndicadorDiario', N'
CREATE OR ALTER VIEW {S}.vwIndicadorDiario
AS
SELECT x.Fecha, x.ClienteId, x.Operacion, x.OrigenId, x.DestinoId, x.Marca, x.Trailer,
       COUNT(*) AS Despachados,
       SUM(x.ConNovedad) AS ConNovedad,
       CAST(ROUND(SUM(x.ConNovedad) * 100.0 / COUNT(*), 2) AS decimal(5, 2)) AS Porcentaje,
       SUM(x.SinCalificar) AS SinCalificar
FROM (
    SELECT c.Fecha, c.ClienteId, c.Operacion, c.OrigenId, c.DestinoId, c.Marca, c.Trailer,
           CASE WHEN EXISTS (SELECT 1
                             FROM {S}.tblHallazgos h
                             JOIN {S}.tblInspecciones i ON i.Id = h.InspeccionId AND i.Estado = ~cerrada~
                             JOIN {S}.tblEtapas e ON e.Id = i.EtapaId
                             WHERE e.ViajeId = c.ViajeId AND e.VehiculoId = c.VehiculoId
                               AND h.NivelSeveridad IN (~v1~, ~v1_plus~)
                               AND NOT (h.Origen = ~conductor~ AND h.RevisadoAt IS NULL)
                               AND CAST(SWITCHOFFSET(i.CerradaAt, N~-05:00~) AS date) = c.Fecha)
                THEN 1 ELSE 0 END AS ConNovedad,
           (SELECT COUNT(*)
            FROM {S}.tblHallazgos h
            JOIN {S}.tblInspecciones i ON i.Id = h.InspeccionId AND i.Estado = ~cerrada~
            JOIN {S}.tblEtapas e ON e.Id = i.EtapaId
            WHERE e.ViajeId = c.ViajeId AND e.VehiculoId = c.VehiculoId
              AND h.NivelSeveridad IS NULL
              AND CAST(SWITCHOFFSET(i.CerradaAt, N~-05:00~) AS date) = c.Fecha) AS SinCalificar
    FROM {S}.vwCobertura c
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

-- Resultado (esperado: VistaUmbralSinFiltro = 1, VistaAveriasSinFiltro = 1, IndicadorConSinCalificar = 1).
DECLARE @cuentas nvarchar(max) = REPLACE(N'
SELECT
    (SELECT COUNT(*) FROM sys.sql_modules m WHERE m.object_id = OBJECT_ID(N~{S}.vwHallazgosVentanaUmbral~) AND m.definition NOT LIKE N~%RevisadoAt IS NULL%~) AS VistaUmbralSinFiltro,
    (SELECT COUNT(*) FROM sys.sql_modules m WHERE m.object_id = OBJECT_ID(N~{S}.vwAveriasPorConfiguracion~) AND m.definition NOT LIKE N~%RevisadoAt IS NULL%~) AS VistaAveriasSinFiltro,
    (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N~{S}.vwIndicadorDiario~) AND name = N~SinCalificar~) AS IndicadorConSinCalificar;', N'{S}', @q);
SET @cuentas = REPLACE(@cuentas, N'~', N'''');
EXEC (@cuentas);
