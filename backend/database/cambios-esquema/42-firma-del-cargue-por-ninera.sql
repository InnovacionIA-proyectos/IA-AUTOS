/* =============================================================================
   Firma del cargue por ninera, para una base de IA Autos YA EXISTENTE. Se ejecuta UNA vez (si ya esta aplicado, no cambia nada) y todo ocurre en UNA transaccion: si algo falla se revierte completo.

   Que hace:
     1. tblTransferenciasCustodia: la hora y el GPS del momento en que cada parte firmo, por parte (FirmaEntregaMomentoAt, FirmaEntregaLat, FirmaEntregaLng, FirmaEntregaPrecisionM y las de FirmaRecibe...),
        ademas de la hora en que la firma llego al servidor (FirmaEntregaAt, FirmaRecibeAt, que no cambian): una firma hecha sin senal se envia despues y conserva la hora y el lugar del momento. La latitud y la
        longitud van juntas y dentro de su rango (CK_tblTransferencias_GpsEntrega y CK_tblTransferencias_GpsRecibe).
     2. tblTransferenciasCustodia: FirmaPorNinera (0 por defecto): marca la transferencia del cargue de un carro que se firmo por ninera (una sola firma de cada parte para todos los carros del viaje).
     3. tblNotificaciones: CK_tblNotificaciones_Tipo admite el tipo firma_cargue_pendiente, sin quitar ninguno de los trece existentes.
     4. tblParametros: la fila firma_cargue_pendiente_minutos (30): minutos que la salida del cargue puede seguir cerrada sin la firma de una parte antes de avisarle (una fila que ya existe no se sobrescribe).

   Seguridad de los datos: solo agrega columnas nulas (o con valor por defecto), dos restricciones, un tipo de aviso y una fila de parametros; no cambia ningun dato. Las transferencias que ya existen se leen igual
   (sin hora ni GPS del momento y con FirmaPorNinera = 0).

   Como ejecutarlo: abrir en SSMS, DBeaver o sqlcmd CONECTADO a la base de IA Autos, ajustar @esquema y ejecutar completo, en UNA sola ejecucion de script, con un usuario con permiso para
   modificar tablas (no el usuario de la aplicacion). Ejecutarlo ANTES de desplegar la API.
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
IF OBJECT_ID(@q + N'.tblTransferenciasCustodia', N'U') IS NULL OR OBJECT_ID(@q + N'.tblNotificaciones', N'U') IS NULL OR OBJECT_ID(@q + N'.tblParametros', N'U') IS NULL
    THROW 50001, N'Faltan las tablas tblTransferenciasCustodia, tblNotificaciones o tblParametros de IA Autos en el esquema indicado: revise @esquema.', 1;

-- Pasos en orden. {S} = esquema entre corchetes; ~ = comilla simple dentro del texto.
DECLARE @paso TABLE (n int IDENTITY(1, 1) PRIMARY KEY, descripcion nvarchar(100), s nvarchar(max));

INSERT @paso (descripcion, s) VALUES
(N'tblTransferenciasCustodia: FirmaEntregaMomentoAt', N'
IF COL_LENGTH(N~{S}.tblTransferenciasCustodia~, N~FirmaEntregaMomentoAt~) IS NULL
    ALTER TABLE {S}.tblTransferenciasCustodia ADD FirmaEntregaMomentoAt datetimeoffset(3) NULL'),
(N'tblTransferenciasCustodia: FirmaEntregaLat', N'
IF COL_LENGTH(N~{S}.tblTransferenciasCustodia~, N~FirmaEntregaLat~) IS NULL
    ALTER TABLE {S}.tblTransferenciasCustodia ADD FirmaEntregaLat float NULL'),
(N'tblTransferenciasCustodia: FirmaEntregaLng', N'
IF COL_LENGTH(N~{S}.tblTransferenciasCustodia~, N~FirmaEntregaLng~) IS NULL
    ALTER TABLE {S}.tblTransferenciasCustodia ADD FirmaEntregaLng float NULL'),
(N'tblTransferenciasCustodia: FirmaEntregaPrecisionM', N'
IF COL_LENGTH(N~{S}.tblTransferenciasCustodia~, N~FirmaEntregaPrecisionM~) IS NULL
    ALTER TABLE {S}.tblTransferenciasCustodia ADD FirmaEntregaPrecisionM float NULL'),
(N'tblTransferenciasCustodia: FirmaRecibeMomentoAt', N'
IF COL_LENGTH(N~{S}.tblTransferenciasCustodia~, N~FirmaRecibeMomentoAt~) IS NULL
    ALTER TABLE {S}.tblTransferenciasCustodia ADD FirmaRecibeMomentoAt datetimeoffset(3) NULL'),
(N'tblTransferenciasCustodia: FirmaRecibeLat', N'
IF COL_LENGTH(N~{S}.tblTransferenciasCustodia~, N~FirmaRecibeLat~) IS NULL
    ALTER TABLE {S}.tblTransferenciasCustodia ADD FirmaRecibeLat float NULL'),
(N'tblTransferenciasCustodia: FirmaRecibeLng', N'
IF COL_LENGTH(N~{S}.tblTransferenciasCustodia~, N~FirmaRecibeLng~) IS NULL
    ALTER TABLE {S}.tblTransferenciasCustodia ADD FirmaRecibeLng float NULL'),
(N'tblTransferenciasCustodia: FirmaRecibePrecisionM', N'
IF COL_LENGTH(N~{S}.tblTransferenciasCustodia~, N~FirmaRecibePrecisionM~) IS NULL
    ALTER TABLE {S}.tblTransferenciasCustodia ADD FirmaRecibePrecisionM float NULL'),
(N'tblTransferenciasCustodia: FirmaPorNinera', N'
IF COL_LENGTH(N~{S}.tblTransferenciasCustodia~, N~FirmaPorNinera~) IS NULL
    ALTER TABLE {S}.tblTransferenciasCustodia ADD FirmaPorNinera bit NOT NULL CONSTRAINT DF_tblTransferenciasCustodia_FirmaPorNinera DEFAULT (0)'),
(N'tblTransferenciasCustodia: GPS de quien entrega', N'
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N~CK_tblTransferencias_GpsEntrega~ AND parent_object_id = OBJECT_ID(N~{S}.tblTransferenciasCustodia~))
    ALTER TABLE {S}.tblTransferenciasCustodia ADD CONSTRAINT CK_tblTransferencias_GpsEntrega CHECK ((FirmaEntregaLat IS NULL AND FirmaEntregaLng IS NULL AND FirmaEntregaPrecisionM IS NULL) OR (FirmaEntregaLat IS NOT NULL AND FirmaEntregaLng IS NOT NULL AND FirmaEntregaLat BETWEEN -90 AND 90 AND FirmaEntregaLng BETWEEN -180 AND 180 AND (FirmaEntregaPrecisionM IS NULL OR FirmaEntregaPrecisionM >= 0)))'),
(N'tblTransferenciasCustodia: GPS de quien recibe', N'
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N~CK_tblTransferencias_GpsRecibe~ AND parent_object_id = OBJECT_ID(N~{S}.tblTransferenciasCustodia~))
    ALTER TABLE {S}.tblTransferenciasCustodia ADD CONSTRAINT CK_tblTransferencias_GpsRecibe CHECK ((FirmaRecibeLat IS NULL AND FirmaRecibeLng IS NULL AND FirmaRecibePrecisionM IS NULL) OR (FirmaRecibeLat IS NOT NULL AND FirmaRecibeLng IS NOT NULL AND FirmaRecibeLat BETWEEN -90 AND 90 AND FirmaRecibeLng BETWEEN -180 AND 180 AND (FirmaRecibePrecisionM IS NULL OR FirmaRecibePrecisionM >= 0)))'),
(N'tblNotificaciones: tipo firma_cargue_pendiente', N'
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N~CK_tblNotificaciones_Tipo~ AND parent_object_id = OBJECT_ID(N~{S}.tblNotificaciones~) AND definition LIKE N~%firma_cargue_pendiente%~)
BEGIN
    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N~CK_tblNotificaciones_Tipo~ AND parent_object_id = OBJECT_ID(N~{S}.tblNotificaciones~))
        ALTER TABLE {S}.tblNotificaciones DROP CONSTRAINT CK_tblNotificaciones_Tipo;
    ALTER TABLE {S}.tblNotificaciones ADD CONSTRAINT CK_tblNotificaciones_Tipo CHECK (Tipo IN (~novedad_conductor~, ~respuesta_conductor~, ~hallazgo_critico~, ~umbral_patron~, ~captura_incompleta~, ~transferencia_pendiente~, ~reporte_diario~, ~novedad_proceso~, ~reclamo_sin_actividad~, ~fuera_de_rango~, ~cargue_sin_reclamar~, ~propuesta_ubicacion~, ~permiso_sin_inspector~, ~firma_cargue_pendiente~));
END'),
(N'parametro de la firma del cargue pendiente', N'
INSERT INTO {S}.tblParametros (Clave, Valor, Tipo, Descripcion)
SELECT v.Clave, v.Valor, v.Tipo, v.Descripcion
FROM (VALUES
    (~firma_cargue_pendiente_minutos~, N~30~, ~numero~, N~Minutos que la salida del cargue de un carro puede seguir cerrada sin la firma de una parte de la ninera antes de avisarle a esa parte (el valor definitivo lo confirma el negocio)~)
) AS v (Clave, Valor, Tipo, Descripcion)
WHERE NOT EXISTS (SELECT 1 FROM {S}.tblParametros p WHERE p.Clave = v.Clave)');

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

-- Resultado (esperado: ColumnasDeMomento = 8, FirmaPorNinera = 1, RestriccionesGps = 2, TipoDeAviso = 1, Parametro = 1).
DECLARE @cuentas nvarchar(max) = REPLACE(N'
SELECT
    (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N~{S}.tblTransferenciasCustodia~) AND name IN (N~FirmaEntregaMomentoAt~, N~FirmaEntregaLat~, N~FirmaEntregaLng~, N~FirmaEntregaPrecisionM~, N~FirmaRecibeMomentoAt~, N~FirmaRecibeLat~, N~FirmaRecibeLng~, N~FirmaRecibePrecisionM~)) AS ColumnasDeMomento,
    (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N~{S}.tblTransferenciasCustodia~) AND name = N~FirmaPorNinera~) AS FirmaPorNinera,
    (SELECT COUNT(*) FROM sys.check_constraints WHERE name IN (N~CK_tblTransferencias_GpsEntrega~, N~CK_tblTransferencias_GpsRecibe~) AND parent_object_id = OBJECT_ID(N~{S}.tblTransferenciasCustodia~)) AS RestriccionesGps,
    (SELECT COUNT(*) FROM sys.check_constraints WHERE name = N~CK_tblNotificaciones_Tipo~ AND parent_object_id = OBJECT_ID(N~{S}.tblNotificaciones~) AND definition LIKE N~%firma_cargue_pendiente%~) AS TipoDeAviso,
    (SELECT COUNT(*) FROM {S}.tblParametros WHERE Clave = N~firma_cargue_pendiente_minutos~) AS Parametro;', N'{S}', @q);
SET @cuentas = REPLACE(@cuentas, N'~', N'''');
EXEC (@cuentas);
