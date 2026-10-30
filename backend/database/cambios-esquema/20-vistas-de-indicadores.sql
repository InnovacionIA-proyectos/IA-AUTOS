/* =============================================================================
   Vistas de los indicadores del tablero, para una base de IA Autos YA EXISTENTE (con 10-zona-dano-y-hallazgo.sql, 13-acciones-niveles-severidad.sql y 14-sello-y-unidad-no-sale.sql aplicados).
   Se ejecuta UNA vez (si ya esta aplicado, no cambia nada) y todo ocurre en UNA transaccion: si algo falla se revierte completo.

   Que hace (solo crea vistas; no cambia ningun dato ni ninguna tabla):
     1. vwCobertura: una fila por UNIDAD DESPACHADA = un VIN de un viaje con la salida de su Cargue cerrada, sin las unidades marcadas "no sale". RegistrosCerrados (0 a 6) cuenta los
        registros cerrados de la unidad en el viaje (salida y llegada de cada etapa); Completa = 1 solo con los 6. FechaDespacho es el cierre de la salida del Cargue; Fecha, su dia en hora de Colombia.
     2. vwAdopcion (formula PROPUESTA, pendiente de confirmar por Calidad): por dia, cliente y configuracion, Programados = los VIN de los viajes programados ese dia (cualquier viaje no cancelado,
        no solo los que ya tienen la salida del Cargue cerrada: con ese denominador la adopcion daria siempre 100 %) y ConSalidaDigital = de esos, los que tienen una inspeccion de salida
        registrada en el sistema (el que no la tiene se hizo en papel o no se hizo). El dia de un viaje es el de su creacion, porque tblViajes aun no guarda la fecha programada.
     3. vwTiempoCaptura: una fila por inspeccion CERRADA de Cargue y de Descargue (ninguna de Transporte), con Minutos = cierre - inicio.
     4. vwAveriasPorConfiguracion: una fila por hallazgo revisado de una inspeccion cerrada (sin las novedades del conductor aun sin revisar), con la configuracion del viaje (placa, trailer,
        tipo de unidad, conductor con su cedula), el VIN, la marca, la operacion, el cliente, la clase, el tamano, el nivel, el proceso y la zona (lado + numero).
     5. vwIndicadorDiario: por dia, cliente y configuracion, Despachados (las unidades de vwCobertura despachadas ese dia) y ConNovedad (de esas mismas unidades, las que tienen al menos un
        hallazgo de nivel V1 ese dia; los de nivel V1+ tambien cuentan, como propuesta pendiente de confirmar por el negocio).
     Todas calculan el cliente por el VEHICULO (no por el viaje) y los dias en hora de Colombia (UTC-5, sin horario de verano). Las vistas de adopcion y del indicador diario guardan una fila
     por dia, cliente, operacion, origen, destino, marca y trailer: para el total de un dia se suman sus filas y el porcentaje se recalcula.

   Seguridad de los datos: no cambia ningun dato; solo crea (o reemplaza) las vistas.

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
IF OBJECT_ID(@q + N'.tblViajes', N'U') IS NULL OR OBJECT_ID(@q + N'.tblViajeVehiculos', N'U') IS NULL OR OBJECT_ID(@q + N'.tblVehiculos', N'U') IS NULL
   OR OBJECT_ID(@q + N'.tblEtapas', N'U') IS NULL OR OBJECT_ID(@q + N'.tblEtapasCatalogo', N'U') IS NULL OR OBJECT_ID(@q + N'.tblInspecciones', N'U') IS NULL
   OR OBJECT_ID(@q + N'.tblHallazgos', N'U') IS NULL OR OBJECT_ID(@q + N'.tblUsuarios', N'U') IS NULL OR OBJECT_ID(@q + N'.tblZonasDano', N'U') IS NULL
   OR OBJECT_ID(@q + N'.tblCuadrantes', N'U') IS NULL OR OBJECT_ID(@q + N'.tblTiposUnidadTransportadora', N'U') IS NULL
    THROW 50001, N'Faltan tablas de IA Autos en el esquema indicado: revise @esquema.', 1;
IF COL_LENGTH(@q + N'.tblHallazgos', N'ZonaId') IS NULL OR COL_LENGTH(@q + N'.tblHallazgos', N'RevisadoAt') IS NULL
    THROW 50002, N'Falta la zona del hallazgo: ejecute antes 10-zona-dano-y-hallazgo.sql.', 1;
IF COL_LENGTH(@q + N'.tblHallazgos', N'NivelSeveridad') IS NULL
    THROW 50003, N'Falta el nivel de severidad del hallazgo: ejecute antes 13-acciones-niveles-severidad.sql.', 1;
IF COL_LENGTH(@q + N'.tblViajeVehiculos', N'EstadoUnidad') IS NULL
    THROW 50004, N'Falta el estado de la unidad en el viaje: ejecute antes 14-sello-y-unidad-no-sale.sql.', 1;

-- Pasos en orden. {S} = esquema entre corchetes; ~ = comilla simple dentro del texto.
DECLARE @paso TABLE (n int IDENTITY(1, 1) PRIMARY KEY, descripcion nvarchar(100), s nvarchar(max));

INSERT @paso (descripcion, s) VALUES
(N'vwCobertura', N'
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
WHERE vv.EstadoUnidad <> ~no_sale~ AND d.FechaDespacho IS NOT NULL'),
(N'vwAdopcion', N'
CREATE OR ALTER VIEW {S}.vwAdopcion
AS
SELECT x.Fecha, x.ClienteId, x.Operacion, x.OrigenId, x.DestinoId, x.Marca, x.Trailer,
       COUNT(*) AS Programados,
       SUM(x.ConSalidaDigital) AS ConSalidaDigital,
       CAST(ROUND(SUM(x.ConSalidaDigital) * 100.0 / COUNT(*), 2) AS decimal(5, 2)) AS Porcentaje
FROM (
    SELECT CAST(SWITCHOFFSET(vj.CreatedAt, N~-05:00~) AS date) AS Fecha, v.ClienteId, v.Operacion, vj.OrigenId, vj.DestinoId, v.Marca, vj.Trailer,
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
GROUP BY x.Fecha, x.ClienteId, x.Operacion, x.OrigenId, x.DestinoId, x.Marca, x.Trailer'),
(N'vwTiempoCaptura', N'
CREATE OR ALTER VIEW {S}.vwTiempoCaptura
AS
SELECT i.Id AS InspeccionId, i.ViajeId, e.VehiculoId, v.ClienteId, v.Operacion, vj.OrigenId, vj.DestinoId, v.Marca, vj.Trailer,
       ec.Codigo AS Proceso, i.Momento, i.UsuarioId,
       CAST(SWITCHOFFSET(i.CerradaAt, N~-05:00~) AS date) AS Fecha,
       CAST(DATEDIFF(SECOND, i.IniciadaAt, i.CerradaAt) / 60.0 AS decimal(10, 2)) AS Minutos
FROM {S}.tblInspecciones i
JOIN {S}.tblEtapas e ON e.Id = i.EtapaId
JOIN {S}.tblEtapasCatalogo ec ON ec.Id = e.EtapaCatalogoId
JOIN {S}.tblVehiculos v ON v.Id = e.VehiculoId
JOIN {S}.tblViajes vj ON vj.Id = i.ViajeId
WHERE i.Estado = ~cerrada~ AND i.CerradaAt IS NOT NULL AND i.CerradaAt >= i.IniciadaAt AND ec.Codigo IN (~cargue~, ~descargue~)'),
(N'vwAveriasPorConfiguracion', N'
CREATE OR ALTER VIEW {S}.vwAveriasPorConfiguracion
AS
SELECT h.Id AS HallazgoId, h.InspeccionId, i.ViajeId, e.VehiculoId, v.Vin, v.ClienteId, v.Operacion, vj.OrigenId, vj.DestinoId, v.Marca, vj.Placa, vj.Trailer,
       vj.TipoUnidadId, tu.Codigo AS UnidadCodigo, tu.Nombre AS Unidad, vj.ConductorId, cu.Nombre AS ConductorNombre, cu.Cedula AS ConductorCedula,
       ec.Codigo AS Proceso, i.Momento, h.ClaseDano, h.TamanoDano, h.NivelSeveridad,
       z.Id AS ZonaId, z.Numero, z.Lado, z.Codigo AS ZonaCodigo, c.Nombre AS CuadranteNombre,
       CASE WHEN z.Lado IS NOT NULL THEN CONCAT(UPPER(LEFT(z.Lado, 1)), SUBSTRING(z.Lado, 2, 20), N~ ~, z.Numero, N~ ~, NCHAR(183), N~ ~, c.Nombre)
            WHEN z.Numero IS NOT NULL THEN CONCAT(z.Numero, N~ ~, NCHAR(183), N~ ~, c.Nombre)
            ELSE z.Nombre END AS ZonaNombre,
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
LEFT JOIN {S}.tblCuadrantes c ON c.Id = z.CuadranteId
WHERE NOT (h.Origen = ~conductor~ AND h.RevisadoAt IS NULL)'),
(N'vwIndicadorDiario', N'
CREATE OR ALTER VIEW {S}.vwIndicadorDiario
AS
SELECT x.Fecha, x.ClienteId, x.Operacion, x.OrigenId, x.DestinoId, x.Marca, x.Trailer,
       COUNT(*) AS Despachados,
       SUM(x.ConNovedad) AS ConNovedad,
       CAST(ROUND(SUM(x.ConNovedad) * 100.0 / COUNT(*), 2) AS decimal(5, 2)) AS Porcentaje
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
                THEN 1 ELSE 0 END AS ConNovedad
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
        -- Con el dibujo version 6 ya sembrado esa vista es la de 34-resembrar-dibujo-v6.sql (sin la zona provisional): no se vuelve a la vieja.
        IF @descripcion = N'vwAveriasPorConfiguracion' AND COL_LENGTH(@q + N'.tblZonasDano', N'LlevaLado') IS NOT NULL
            PRINT N'    (omitida: la vista ya es la del dibujo version 6)';
        ELSE
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

-- Resultado (esperado: 5 vistas).
DECLARE @cuentas nvarchar(max) = N'
SELECT COUNT(*) AS Vistas
FROM sys.views
WHERE schema_id = SCHEMA_ID(N~{E}~)
  AND name IN (N~vwCobertura~, N~vwAdopcion~, N~vwTiempoCaptura~, N~vwAveriasPorConfiguracion~, N~vwIndicadorDiario~)';
SET @cuentas = REPLACE(REPLACE(@cuentas, N'{E}', @esquema), N'~', N'''');
EXEC (@cuentas);
