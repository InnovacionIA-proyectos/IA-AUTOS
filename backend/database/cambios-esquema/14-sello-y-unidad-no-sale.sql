/* =============================================================================
   Foto del sello de quien valida y unidad que no sale, para una base de IA Autos YA EXISTENTE (con 10-zona-dano-y-hallazgo.sql y 11-seguimiento-hallazgo.sql
   aplicados). Se ejecuta UNA vez (si ya esta aplicado, no hace nada) y todo ocurre en UNA transaccion: si algo falla se revierte completo.

   Que hace:
     1. tblEvidencias: gana EsSello (0 en todas las existentes). Es la foto del sello de quien valida un hallazgo V2 (Sofasa): no es de una vista ni de un
        hallazgo, no cuenta para el cierre de la inspeccion y se registra aunque la inspeccion ya este cerrada, como la constancia de no firma. Una restriccion
        impide que una evidencia sea a la vez sello, constancia, de un hallazgo o de una vista.
     2. TR_tblEvidencias_NoEnCerrada: admite tambien la foto del sello en una inspeccion cerrada (las demas evidencias nuevas siguen sin admitirse).
     3. vwViajesCompletos: una unidad marcada "no sale" en un viaje ya no cuenta como completa en ese viaje (el viaje sigue sin ella).

   Seguridad de los datos: no borra ni cambia ningun dato; solo agrega la columna y su restriccion y reemplaza el trigger y la vista.

   Como ejecutarlo: abrir en SSMS, DBeaver o sqlcmd CONECTADO a la base de IA Autos, ajustar @esquema y ejecutar completo con un usuario con
   permiso para alterar tablas, triggers y vistas (no el usuario de la aplicacion). Ejecutarlo ANTES de desplegar la API.
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
IF OBJECT_ID(@q + N'.tblEvidencias', N'U') IS NULL OR OBJECT_ID(@q + N'.tblInspecciones', N'U') IS NULL OR OBJECT_ID(@q + N'.tblEtapas', N'U') IS NULL
   OR OBJECT_ID(@q + N'.tblViajeVehiculos', N'U') IS NULL
    THROW 50001, N'Faltan tablas de IA Autos en el esquema indicado: revise @esquema.', 1;
IF COL_LENGTH(@q + N'.tblViajeVehiculos', N'EstadoUnidad') IS NULL
    THROW 50002, N'Falta el estado de la unidad en el viaje: ejecute antes 10-zona-dano-y-hallazgo.sql.', 1;
IF COL_LENGTH(@q + N'.tblEvidencias', N'EsConstancia') IS NULL
    THROW 50003, N'Falta la marca de constancia de las evidencias: ejecute antes 08-transferencias-custodia.sql.', 1;

IF COL_LENGTH(@q + N'.tblEvidencias', N'EsSello') IS NOT NULL
   AND OBJECT_DEFINITION(OBJECT_ID(@q + N'.vwViajesCompletos')) LIKE N'%EstadoUnidad%'
   AND OBJECT_DEFINITION(OBJECT_ID(@q + N'.TR_tblEvidencias_NoEnCerrada')) LIKE N'%EsSello%'
BEGIN
    PRINT N'La foto del sello y la unidad que no sale ya estan aplicadas: no se hace nada.';
    RETURN;
END

-- Pasos en orden. {S} = esquema entre corchetes; ~ = comilla simple dentro del texto.
DECLARE @paso TABLE (n int IDENTITY(1, 1) PRIMARY KEY, descripcion nvarchar(100), s nvarchar(max));

INSERT @paso (descripcion, s) VALUES
(N'tblEvidencias: marca de la foto del sello', N'
IF COL_LENGTH(N~{S}.tblEvidencias~, N~EsSello~) IS NULL
    ALTER TABLE {S}.tblEvidencias ADD EsSello bit NOT NULL CONSTRAINT DF_tblEvidencias_EsSello DEFAULT (0)'),
(N'tblEvidencias: el sello no es de una vista, un hallazgo ni una constancia', N'
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N~CK_tblEvidencias_Sello~ AND parent_object_id = OBJECT_ID(N~{S}.tblEvidencias~))
    ALTER TABLE {S}.tblEvidencias ADD CONSTRAINT CK_tblEvidencias_Sello CHECK (EsSello = 0 OR (HallazgoId IS NULL AND VistaId IS NULL AND EsConstancia = 0))'),
(N'trigger de evidencias: admite la foto del sello en una inspeccion cerrada', N'
CREATE OR ALTER TRIGGER {S}.TR_tblEvidencias_NoEnCerrada
ON {S}.tblEvidencias
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted i
               JOIN {S}.tblInspecciones n ON n.Id = i.InspeccionId
               WHERE n.Estado = ~cerrada~ AND i.EsConstancia = 0 AND i.EsSello = 0)
        THROW 51002, ~La inspeccion esta cerrada y es inmutable; no admite evidencias nuevas~, 1;
END'),
(N'vwViajesCompletos: una unidad que no sale no cuenta como completa', N'
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
                   WHERE vv.ViajeId = x.ViajeId AND vv.VehiculoId = x.VehiculoId AND vv.EstadoUnidad = ~no_sale~)
GROUP BY x.ViajeId, x.VehiculoId
HAVING COUNT(*) > 0 AND COUNT(*) = SUM(x.Cerrada)');

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

-- Resultado (esperado: la columna, su restriccion, el trigger con EsSello y la vista con EstadoUnidad, y ninguna evidencia marcada como sello).
DECLARE @cuentas nvarchar(max) = REPLACE(N'
SELECT
    (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N~{S}.tblEvidencias~) AND name = N~EsSello~) AS ColumnaEsSello,
    (SELECT COUNT(*) FROM sys.check_constraints WHERE name = N~CK_tblEvidencias_Sello~ AND parent_object_id = OBJECT_ID(N~{S}.tblEvidencias~)) AS RestriccionSello,
    CASE WHEN OBJECT_DEFINITION(OBJECT_ID(N~{S}.TR_tblEvidencias_NoEnCerrada~)) LIKE N~%EsSello%~ THEN 1 ELSE 0 END AS TriggerAdmiteSello,
    CASE WHEN OBJECT_DEFINITION(OBJECT_ID(N~{S}.vwViajesCompletos~)) LIKE N~%EstadoUnidad%~ THEN 1 ELSE 0 END AS VistaSinUnidadesNoSale,
    (SELECT COUNT(*) FROM {S}.tblEvidencias WHERE EsSello = 1) AS EvidenciasSello', N'{S}', @q);
SET @cuentas = REPLACE(@cuentas, N'~', N'''');
EXEC (@cuentas);
