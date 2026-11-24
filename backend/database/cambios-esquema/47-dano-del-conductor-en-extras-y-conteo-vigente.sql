/* =============================================================================
   El dano de un extra que registra el conductor y el conteo de lo sin calificar con la version vigente, para una base de IA Autos YA EXISTENTE. Se ejecuta UNA vez (si ya esta aplicado, no cambia nada)
   y todo ocurre en UNA transaccion: si algo falla se revierte completo.

   Que hace (no cambia ningun dato):
     1. tblHallazgos: la restriccion CK_tblHallazgos_Extra pedia que el hallazgo de un extra no tuviera zona Y fuera de origen inspector. Ahora solo pide que no tenga zona: el conductor tambien registra el
        dano de un extra (con su clase y su tamano, sin nivel) y un inspector lo califica despues. Una vez calificado el hallazgo conserva su extra y sigue sin zona.
     2. vwIndicadorDiario (el indicador diario): la cifra SinCalificar cuenta solo los danos sin nivel de la version vigente de cada informe; el dano de una version que una correccion cerrada ya reemplazo
        deja de contarse, igual que en el listado de vehiculos y en el informe del carro. ConNovedad y Porcentaje no cambian.

   Seguridad de los datos: solo cambia una restriccion (a una que admite todo lo que admitia antes) y reemplaza una vista (CREATE OR ALTER); repetirlo deja lo mismo. Se ejecuta ANTES de desplegar la API.

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
IF OBJECT_ID(@q + N'.tblHallazgos', N'U') IS NULL OR OBJECT_ID(@q + N'.tblInspecciones', N'U') IS NULL OR OBJECT_ID(@q + N'.vwCobertura', N'V') IS NULL
    THROW 50001, N'Faltan tblHallazgos, tblInspecciones o vwCobertura de IA Autos en el esquema indicado: revise @esquema.', 1;
IF COL_LENGTH(@q + N'.tblHallazgos', N'ExtraId') IS NULL
    THROW 50002, N'tblHallazgos no tiene ExtraId: ejecute antes 37-dano-en-los-extras.sql.', 1;
IF COL_LENGTH(@q + N'.vwCobertura', N'Fecha') IS NULL
    THROW 50003, N'vwCobertura no tiene Fecha: ejecute antes los scripts de cambios anteriores (los de los indicadores del tablero).', 1;

-- Pasos en orden. {S} = esquema entre corchetes; ~ = comilla simple dentro del texto.
DECLARE @paso TABLE (n int IDENTITY(1, 1) PRIMARY KEY, descripcion nvarchar(100), s nvarchar(max));

INSERT @paso (descripcion, s) VALUES
(N'tblHallazgos: el dano de un extra no lleva zona', N'
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N~CK_tblHallazgos_Extra~ AND parent_object_id = OBJECT_ID(N~{S}.tblHallazgos~) AND definition LIKE N~%Origen%~)
    ALTER TABLE {S}.tblHallazgos DROP CONSTRAINT CK_tblHallazgos_Extra;
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N~CK_tblHallazgos_Extra~ AND parent_object_id = OBJECT_ID(N~{S}.tblHallazgos~))
    ALTER TABLE {S}.tblHallazgos ADD CONSTRAINT CK_tblHallazgos_Extra CHECK (ExtraId IS NULL OR ZonaId IS NULL)'),
(N'vista vwIndicadorDiario con la version vigente', N'
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
              AND NOT EXISTS (SELECT 1 FROM {S}.tblInspecciones co WHERE co.CorrigeInspeccionId = i.Id AND co.Estado = ~cerrada~)
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

-- Resultado (esperado: DanoDelExtraSinZona = 1, ExtraSinOrigenEnLaRestriccion = 1, IndicadorConVersionVigente = 1).
DECLARE @cuentas nvarchar(max) = REPLACE(N'
SELECT
    (SELECT COUNT(*) FROM sys.check_constraints WHERE name = N~CK_tblHallazgos_Extra~ AND parent_object_id = OBJECT_ID(N~{S}.tblHallazgos~)) AS DanoDelExtraSinZona,
    (SELECT COUNT(*) FROM sys.check_constraints WHERE name = N~CK_tblHallazgos_Extra~ AND parent_object_id = OBJECT_ID(N~{S}.tblHallazgos~) AND definition NOT LIKE N~%Origen%~) AS ExtraSinOrigenEnLaRestriccion,
    (SELECT COUNT(*) FROM sys.sql_modules WHERE object_id = OBJECT_ID(N~{S}.vwIndicadorDiario~) AND definition LIKE N~%CorrigeInspeccionId%~) AS IndicadorConVersionVigente;', N'{S}', @q);
SET @cuentas = REPLACE(@cuentas, N'~', N'''');
EXEC (@cuentas);
