/* =============================================================================
   La cobertura con cinco registros y el aviso de viaje sin movimiento, para una base de IA Autos YA EXISTENTE. Se ejecuta UNA vez (si ya esta aplicado, no cambia nada) y todo ocurre en UNA transaccion: si algo falla se
   revierte completo.

   Que hace:
     1. vwCobertura: el Descargue tiene una sola revision, la de llegada. RegistrosCerrados cuenta de 0 a 5 las parejas etapa + momento cerradas (llegada y salida del Cargue, llegada y salida de Transporte y llegada del
        Descargue; la salida del Descargue ya no cuenta, aunque exista una cerrada de antes) y Completa = 1 con los 5. Un carro que ya tenia sus 6 registros sigue completo. vwIndicadorDiario no cambia: lee vwCobertura.
     2. tblNotificaciones: CK_tblNotificaciones_Tipo admite el tipo viaje_sin_movimiento, sin quitar ninguno de los diecinueve existentes.

   Seguridad de los datos: no toca ningun dato; solo reemplaza una vista y la restriccion del tipo de aviso. Se ejecuta ANTES de desplegar la API.

   Como ejecutarlo: abrir en SSMS, DBeaver o sqlcmd CONECTADO a la base de IA Autos, ajustar @esquema y ejecutar completo, en UNA sola ejecucion de script, con un usuario con permiso para
   modificar tablas (no el usuario de la aplicacion).
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
IF OBJECT_ID(@q + N'.tblNotificaciones', N'U') IS NULL OR OBJECT_ID(@q + N'.tblInspecciones', N'U') IS NULL OR OBJECT_ID(@q + N'.tblViajeVehiculos', N'U') IS NULL OR OBJECT_ID(@q + N'.vwCobertura', N'V') IS NULL
    THROW 50001, N'Faltan tblNotificaciones, tblInspecciones, tblViajeVehiculos o vwCobertura de IA Autos en el esquema indicado: revise @esquema.', 1;

-- Pasos en orden. {S} = esquema entre corchetes; ~ = comilla simple dentro del texto.
DECLARE @paso TABLE (n int IDENTITY(1, 1) PRIMARY KEY, descripcion nvarchar(100), s nvarchar(max));

INSERT @paso (descripcion, s) VALUES
(N'vwCobertura con cinco registros', N'
CREATE OR ALTER VIEW {S}.vwCobertura
AS
SELECT vv.ViajeId, vv.VehiculoId, v.Vin, v.ClienteId, v.Operacion, vj.OrigenId, vj.DestinoId, v.Marca, vj.Placa, vj.Trailer, vj.TipoUnidadId, vj.ConductorId,
       d.FechaDespacho,
       CAST(SWITCHOFFSET(d.FechaDespacho, N~-05:00~) AS date) AS Fecha,
       r.RegistrosCerrados,
       CAST(CASE WHEN r.RegistrosCerrados >= 5 THEN 1 ELSE 0 END AS bit) AS Completa
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
    SELECT CASE WHEN COUNT(*) > 5 THEN 5 ELSE COUNT(*) END AS RegistrosCerrados
    FROM (
        SELECT DISTINCT i.EtapaId, i.Momento
        FROM {S}.tblInspecciones i
        JOIN {S}.tblEtapas e ON e.Id = i.EtapaId
        JOIN {S}.tblEtapasCatalogo ec ON ec.Id = e.EtapaCatalogoId
        WHERE e.ViajeId = vv.ViajeId AND e.VehiculoId = vv.VehiculoId AND i.Estado = ~cerrada~
          AND NOT (ec.Codigo = ~descargue~ AND i.Momento = ~salida~)
    ) AS registros
) r
WHERE vv.EstadoUnidad NOT IN (~no_sale~, ~bajado~) AND d.FechaDespacho IS NOT NULL'),
(N'tblNotificaciones: tipo viaje_sin_movimiento', N'
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N~CK_tblNotificaciones_Tipo~ AND parent_object_id = OBJECT_ID(N~{S}.tblNotificaciones~) AND definition LIKE N~%viaje_sin_movimiento%~)
BEGIN
    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N~CK_tblNotificaciones_Tipo~ AND parent_object_id = OBJECT_ID(N~{S}.tblNotificaciones~))
        ALTER TABLE {S}.tblNotificaciones DROP CONSTRAINT CK_tblNotificaciones_Tipo;
    ALTER TABLE {S}.tblNotificaciones ADD CONSTRAINT CK_tblNotificaciones_Tipo CHECK (Tipo IN (~novedad_conductor~, ~respuesta_conductor~, ~hallazgo_critico~, ~umbral_patron~, ~captura_incompleta~, ~transferencia_pendiente~, ~reporte_diario~, ~novedad_proceso~, ~reclamo_sin_actividad~, ~fuera_de_rango~, ~cargue_sin_reclamar~, ~propuesta_ubicacion~, ~permiso_sin_inspector~, ~firma_cargue_pendiente~, ~cambio_cargue_inspector~, ~cambio_destino_unidad~, ~carro_rechazado_en_destino~, ~ubicacion_fijada~, ~espera_inspector_punto~, ~viaje_sin_movimiento~));
END');

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

-- Resultado (esperado: CoberturaDeCinco = 1, SinSalidaDelDescargue = 1, TipoDeAviso = 1).
DECLARE @cuentas nvarchar(max) = REPLACE(N'
SELECT
    (SELECT COUNT(*) FROM sys.sql_modules WHERE object_id = OBJECT_ID(N~{S}.vwCobertura~) AND definition LIKE N~%RegistrosCerrados >= 5%~) AS CoberturaDeCinco,
    (SELECT COUNT(*) FROM sys.sql_modules WHERE object_id = OBJECT_ID(N~{S}.vwCobertura~) AND definition LIKE N~%NOT (ec.Codigo = ~~descargue~~ AND i.Momento = ~~salida~~)%~) AS SinSalidaDelDescargue,
    (SELECT COUNT(*) FROM sys.check_constraints WHERE name = N~CK_tblNotificaciones_Tipo~ AND parent_object_id = OBJECT_ID(N~{S}.tblNotificaciones~) AND definition LIKE N~%viaje_sin_movimiento%~) AS TipoDeAviso;', N'{S}', @q);
SET @cuentas = REPLACE(@cuentas, N'~', N'''');
EXEC (@cuentas);
