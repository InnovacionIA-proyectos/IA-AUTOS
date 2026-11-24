/* =============================================================================
   La espera del inspector del punto en la entrega, para una base de IA Autos YA EXISTENTE. Se ejecuta UNA vez (si ya esta aplicado, no cambia nada) y todo ocurre en UNA transaccion: si algo falla se revierte completo.

   Que hace:
     1. tblEsperasInspectorPunto: la espera que el conductor activa en una parada con inspectores donde nadie tomo la entrega: viaje, parada, conductor, motivo (de tblMotivosSinInspector), la hora y el GPS de su celular
        (ActivadaAt, nunca posterior a la hora en que llego al servidor, CreatedAt), cuando se aviso al coordinador y como termino: esperando, inspector_llego (con el inspector y la hora), cerrada_sin_inspector_del_punto (con la
        hora y quien cerro) o anulada. Un indice unico deja a lo sumo UNA espera abierta por viaje y parada; las que terminaron quedan como historial. Las restricciones de la base exigen que cada estado traiga sus datos.
     2. tblTransferenciasCustodia.SinInspectorDelPunto: la marca "entregada sin inspector del punto" del carro que el conductor entrego con el plan B (0 por defecto; los carros que ya existian quedan sin marca).
     3. tblNotificaciones: CK_tblNotificaciones_Tipo admite el tipo espera_inspector_punto, sin quitar ninguno de los dieciocho existentes.

   Seguridad de los datos: solo agrega una tabla, una columna con valor por defecto, restricciones y un tipo de aviso; no modifica ni borra ningun dato existente. Se ejecuta ANTES de desplegar la API.

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
IF OBJECT_ID(@q + N'.tblViajes', N'U') IS NULL OR OBJECT_ID(@q + N'.tblLugares', N'U') IS NULL OR OBJECT_ID(@q + N'.tblUsuarios', N'U') IS NULL OR OBJECT_ID(@q + N'.tblMotivosSinInspector', N'U') IS NULL
   OR OBJECT_ID(@q + N'.tblTransferenciasCustodia', N'U') IS NULL OR OBJECT_ID(@q + N'.tblNotificaciones', N'U') IS NULL
    THROW 50001, N'Faltan tblViajes, tblLugares, tblUsuarios, tblMotivosSinInspector, tblTransferenciasCustodia o tblNotificaciones de IA Autos en el esquema indicado: revise @esquema.', 1;

-- Pasos en orden. {S} = esquema entre corchetes; ~ = comilla simple dentro del texto.
DECLARE @paso TABLE (n int IDENTITY(1, 1) PRIMARY KEY, descripcion nvarchar(100), s nvarchar(max));

INSERT @paso (descripcion, s) VALUES
(N'tblEsperasInspectorPunto: la tabla de la espera', N'
IF OBJECT_ID(N~{S}.tblEsperasInspectorPunto~, N~U~) IS NULL
BEGIN
    CREATE TABLE {S}.tblEsperasInspectorPunto
    (
        Id               uniqueidentifier  NOT NULL CONSTRAINT PK_tblEsperasInspectorPunto PRIMARY KEY,
        ViajeId          uniqueidentifier  NOT NULL,
        LugarId          uniqueidentifier  NOT NULL,
        ConductorId      uniqueidentifier  NOT NULL,
        MotivoCodigo     varchar(30)       NOT NULL,
        MotivoTexto      nvarchar(300)     NULL,
        ActivadaAt       datetimeoffset(3) NOT NULL,
        Lat              float             NULL,
        Lng              float             NULL,
        PrecisionM       float             NULL,
        Estado           varchar(40)       NOT NULL CONSTRAINT DF_tblEsperasInspectorPunto_Estado DEFAULT (~esperando~),
        AvisoAt          datetimeoffset(3) NULL,
        InspectorId      uniqueidentifier  NULL,
        InspectorLlegoAt datetimeoffset(3) NULL,
        CerradaAt        datetimeoffset(3) NULL,
        CerradaPor       uniqueidentifier  NULL,
        CreatedAt        datetimeoffset(3) NOT NULL CONSTRAINT DF_tblEsperasInspectorPunto_CreatedAt DEFAULT (SYSDATETIMEOFFSET()),
        UpdatedAt        datetimeoffset(3) NOT NULL CONSTRAINT DF_tblEsperasInspectorPunto_UpdatedAt DEFAULT (SYSDATETIMEOFFSET()),
        CONSTRAINT FK_tblEsperasInspectorPunto_Viaje FOREIGN KEY (ViajeId) REFERENCES {S}.tblViajes (Id),
        CONSTRAINT FK_tblEsperasInspectorPunto_Lugar FOREIGN KEY (LugarId) REFERENCES {S}.tblLugares (Id),
        CONSTRAINT FK_tblEsperasInspectorPunto_Conductor FOREIGN KEY (ConductorId) REFERENCES {S}.tblUsuarios (Id),
        CONSTRAINT FK_tblEsperasInspectorPunto_Motivo FOREIGN KEY (MotivoCodigo) REFERENCES {S}.tblMotivosSinInspector (Codigo),
        CONSTRAINT FK_tblEsperasInspectorPunto_Inspector FOREIGN KEY (InspectorId) REFERENCES {S}.tblUsuarios (Id),
        CONSTRAINT FK_tblEsperasInspectorPunto_CerradaPor FOREIGN KEY (CerradaPor) REFERENCES {S}.tblUsuarios (Id),
        CONSTRAINT CK_tblEsperasInspectorPunto_Estado CHECK (Estado IN (~esperando~, ~inspector_llego~, ~cerrada_sin_inspector_del_punto~, ~anulada~)),
        CONSTRAINT CK_tblEsperasInspectorPunto_Texto CHECK (MotivoTexto IS NULL OR LEN(LTRIM(RTRIM(MotivoTexto))) > 0),
        CONSTRAINT CK_tblEsperasInspectorPunto_Lat CHECK (Lat IS NULL OR (Lat BETWEEN -90 AND 90)),
        CONSTRAINT CK_tblEsperasInspectorPunto_Lng CHECK (Lng IS NULL OR (Lng BETWEEN -180 AND 180)),
        CONSTRAINT CK_tblEsperasInspectorPunto_Precision CHECK (PrecisionM IS NULL OR PrecisionM >= 0),
        CONSTRAINT CK_tblEsperasInspectorPunto_Activacion CHECK (ActivadaAt <= CreatedAt),
        CONSTRAINT CK_tblEsperasInspectorPunto_Llegada CHECK ((Estado = ~inspector_llego~ AND InspectorId IS NOT NULL AND InspectorLlegoAt IS NOT NULL) OR (Estado <> ~inspector_llego~ AND InspectorId IS NULL AND InspectorLlegoAt IS NULL)),
        CONSTRAINT CK_tblEsperasInspectorPunto_Cierre CHECK ((Estado = ~cerrada_sin_inspector_del_punto~ AND CerradaAt IS NOT NULL AND CerradaPor IS NOT NULL) OR (Estado <> ~cerrada_sin_inspector_del_punto~ AND CerradaAt IS NULL AND CerradaPor IS NULL))
    );
END'),
(N'tblEsperasInspectorPunto: una sola espera abierta por viaje y parada', N'
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N~UX_tblEsperasInspectorPunto_Abierta~ AND object_id = OBJECT_ID(N~{S}.tblEsperasInspectorPunto~))
    CREATE UNIQUE INDEX UX_tblEsperasInspectorPunto_Abierta ON {S}.tblEsperasInspectorPunto (ViajeId, LugarId) WHERE Estado = ~esperando~'),
(N'tblEsperasInspectorPunto: indices de consulta', N'
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N~IX_tblEsperasInspectorPunto_Parada~ AND object_id = OBJECT_ID(N~{S}.tblEsperasInspectorPunto~))
    CREATE INDEX IX_tblEsperasInspectorPunto_Parada ON {S}.tblEsperasInspectorPunto (ViajeId, LugarId, ActivadaAt);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N~IX_tblEsperasInspectorPunto_Estado~ AND object_id = OBJECT_ID(N~{S}.tblEsperasInspectorPunto~))
    CREATE INDEX IX_tblEsperasInspectorPunto_Estado ON {S}.tblEsperasInspectorPunto (Estado, ActivadaAt)'),
(N'tblTransferenciasCustodia: SinInspectorDelPunto', N'
IF COL_LENGTH(N~{S}.tblTransferenciasCustodia~, N~SinInspectorDelPunto~) IS NULL
    ALTER TABLE {S}.tblTransferenciasCustodia ADD SinInspectorDelPunto bit NOT NULL CONSTRAINT DF_tblTransferencias_SinInspectorDelPunto DEFAULT (0)'),
(N'tblNotificaciones: tipo espera_inspector_punto', N'
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N~CK_tblNotificaciones_Tipo~ AND parent_object_id = OBJECT_ID(N~{S}.tblNotificaciones~) AND definition LIKE N~%espera_inspector_punto%~)
BEGIN
    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N~CK_tblNotificaciones_Tipo~ AND parent_object_id = OBJECT_ID(N~{S}.tblNotificaciones~))
        ALTER TABLE {S}.tblNotificaciones DROP CONSTRAINT CK_tblNotificaciones_Tipo;
    ALTER TABLE {S}.tblNotificaciones ADD CONSTRAINT CK_tblNotificaciones_Tipo CHECK (Tipo IN (~novedad_conductor~, ~respuesta_conductor~, ~hallazgo_critico~, ~umbral_patron~, ~captura_incompleta~, ~transferencia_pendiente~, ~reporte_diario~, ~novedad_proceso~, ~reclamo_sin_actividad~, ~fuera_de_rango~, ~cargue_sin_reclamar~, ~propuesta_ubicacion~, ~permiso_sin_inspector~, ~firma_cargue_pendiente~, ~cambio_cargue_inspector~, ~cambio_destino_unidad~, ~carro_rechazado_en_destino~, ~ubicacion_fijada~, ~espera_inspector_punto~));
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

-- Resultado (esperado: Tabla = 1, UnicaEsperaAbierta = 1, RestriccionesDeLaEspera = 8, MarcaDeLaEntrega = 1, TipoDeAviso = 1).
DECLARE @cuentas nvarchar(max) = REPLACE(N'
SELECT
    (SELECT COUNT(*) FROM sys.tables WHERE object_id = OBJECT_ID(N~{S}.tblEsperasInspectorPunto~)) AS Tabla,
    (SELECT COUNT(*) FROM sys.indexes WHERE name = N~UX_tblEsperasInspectorPunto_Abierta~ AND object_id = OBJECT_ID(N~{S}.tblEsperasInspectorPunto~) AND is_unique = 1 AND has_filter = 1) AS UnicaEsperaAbierta,
    (SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N~{S}.tblEsperasInspectorPunto~) AND name LIKE N~CK[_]tblEsperasInspectorPunto[_]%~) AS RestriccionesDeLaEspera,
    (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N~{S}.tblTransferenciasCustodia~) AND name = N~SinInspectorDelPunto~ AND is_nullable = 0) AS MarcaDeLaEntrega,
    (SELECT COUNT(*) FROM sys.check_constraints WHERE name = N~CK_tblNotificaciones_Tipo~ AND parent_object_id = OBJECT_ID(N~{S}.tblNotificaciones~) AND definition LIKE N~%espera_inspector_punto%~) AS TipoDeAviso;', N'{S}', @q);
SET @cuentas = REPLACE(@cuentas, N'~', N'''');
EXEC (@cuentas);
