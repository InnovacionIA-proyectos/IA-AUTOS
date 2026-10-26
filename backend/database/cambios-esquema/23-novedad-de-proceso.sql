/* =============================================================================
   Novedad de proceso, catalogo de motivos y cierre "recibo sin entrega", para una base de IA Autos YA EXISTENTE (con 22-catalogo-estados-sofasa.sql aplicado). Se ejecuta UNA vez (si ya esta aplicado,
   no cambia nada) y todo ocurre en UNA transaccion: si algo falla se revierte completo.

   Que hace:
     1. tblMotivosSinInspector: el catalogo de motivos por los que el inspector previsto no esta (Codigo unico, Nombre, RequiereTexto, Orden, Activo) y sus tres valores iniciales:
        inspector_no_llego "El inspector no llego", inspector_no_disponible "El inspector no esta disponible" y otro "Otro" (RequiereTexto = 1). Calidad confirma los valores; se editan desde el
        portal como los demas catalogos. Si ya hay filas con esos codigos no se tocan.
     2. tblNovedadesProceso: lo que pasa en el proceso y no es un dano (no tiene zona, clase ni nivel y no entra en patrones ni en el mapa de calor). Tipo: cargue_sin_inspector o
        recibo_sin_entrega. Guarda el viaje, el vehiculo, la etapa, la inspeccion, el motivo (obligatorio en cargue_sin_inspector), el texto del motivo (hasta 300; obligatorio si el motivo
        requiere texto y nulo si no), quien la registro y la fecha, lat y lng del momento. Es append-only: un UPDATE falla con el error 51005, un DELETE con el 51003 y un texto que no
        corresponde al motivo, con el 51008. A lo sumo una novedad de cada tipo por inspeccion.
     3. tblInspecciones: MotivoSinInspector (codigo del catalogo) y MotivoSinInspectorTexto, nulos por defecto; solo una inspeccion de salida puede tener motivo y el texto exige motivo.
     4. tblTransferenciasCustodia: un cierre nuevo, recibo_sin_entrega. EntregaUsuarioId pasa a admitir nulo, pero solo con ese cierre, que exige la firma de quien recibe y no tiene
        usuario, firma, fecha ni IP de entrega. Los demas cierres (firmada, con_reservas, no_firma) no cambian.
     5. tblNotificaciones: el tipo novedad_proceso.

   Seguridad de los datos: no borra ni cambia ningun dato existente. Las restricciones nuevas se validan contra lo que ya hay y todo lo existente las cumple.

   Como ejecutarlo: abrir en SSMS, DBeaver o sqlcmd CONECTADO a la base de IA Autos, ajustar @esquema y ejecutar completo, en UNA sola ejecucion de script, con un usuario con permiso para
   crear tablas y triggers (no el usuario de la aplicacion). Ejecutarlo ANTES de desplegar la API.
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
IF OBJECT_ID(@q + N'.tblTransferenciasCustodia', N'U') IS NULL OR OBJECT_ID(@q + N'.tblNotificaciones', N'U') IS NULL OR OBJECT_ID(@q + N'.tblInspecciones', N'U') IS NULL
    THROW 50001, N'Faltan las tablas tblInspecciones, tblTransferenciasCustodia o tblNotificaciones de IA Autos en el esquema indicado: revise @esquema.', 1;

-- Pasos en orden. {S} = esquema entre corchetes; ~ = comilla simple dentro del texto.
DECLARE @paso TABLE (n int IDENTITY(1, 1) PRIMARY KEY, descripcion nvarchar(100), s nvarchar(max));

INSERT @paso (descripcion, s) VALUES
(N'tblMotivosSinInspector', N'
IF OBJECT_ID(N~{S}.tblMotivosSinInspector~, N~U~) IS NULL
BEGIN
    CREATE TABLE {S}.tblMotivosSinInspector
    (
        Id            uniqueidentifier  NOT NULL CONSTRAINT PK_tblMotivosSinInspector PRIMARY KEY,
        Codigo        varchar(30)       NOT NULL,
        Nombre        nvarchar(100)     NOT NULL,
        RequiereTexto bit               NOT NULL CONSTRAINT DF_tblMotivosSinInspector_RequiereTexto DEFAULT (0),
        Orden         int               NOT NULL CONSTRAINT DF_tblMotivosSinInspector_Orden DEFAULT (0),
        Activo        bit               NOT NULL CONSTRAINT DF_tblMotivosSinInspector_Activo DEFAULT (1),
        CreatedAt     datetimeoffset(3) NOT NULL CONSTRAINT DF_tblMotivosSinInspector_CreatedAt DEFAULT (SYSDATETIMEOFFSET()),
        UpdatedAt     datetimeoffset(3) NOT NULL CONSTRAINT DF_tblMotivosSinInspector_UpdatedAt DEFAULT (SYSDATETIMEOFFSET()),
        CONSTRAINT UQ_tblMotivosSinInspector_Codigo UNIQUE (Codigo),
        CONSTRAINT CK_tblMotivosSinInspector_Nombre CHECK (LEN(LTRIM(RTRIM(Nombre))) > 0)
    );
END'),
(N'motivos iniciales', N'
INSERT INTO {S}.tblMotivosSinInspector (Id, Codigo, Nombre, RequiereTexto, Orden)
SELECT NEWID(), v.Codigo, v.Nombre, v.RequiereTexto, v.Orden
FROM (VALUES
    (~inspector_no_llego~,      N~El inspector no lleg~ + NCHAR(243), 0, 1),
    (~inspector_no_disponible~, N~El inspector no est~ + NCHAR(225) + N~ disponible~, 0, 2),
    (~otro~,                    N~Otro~, 1, 3)) AS v (Codigo, Nombre, RequiereTexto, Orden)
WHERE NOT EXISTS (SELECT 1 FROM {S}.tblMotivosSinInspector m WHERE m.Codigo = v.Codigo)'),
(N'tblNovedadesProceso', N'
IF OBJECT_ID(N~{S}.tblNovedadesProceso~, N~U~) IS NULL
BEGIN
    CREATE TABLE {S}.tblNovedadesProceso
    (
        Id            uniqueidentifier  NOT NULL CONSTRAINT PK_tblNovedadesProceso PRIMARY KEY,
        Tipo          varchar(30)       NOT NULL,
        ViajeId       uniqueidentifier  NOT NULL,
        VehiculoId    uniqueidentifier  NOT NULL,
        EtapaId       uniqueidentifier  NOT NULL,
        InspeccionId  uniqueidentifier  NOT NULL,
        MotivoCodigo  varchar(30)       NULL,
        MotivoTexto   nvarchar(300)     NULL,
        UsuarioId     uniqueidentifier  NOT NULL,
        Fecha         datetimeoffset(3) NOT NULL,
        Lat           float             NULL,
        Lng           float             NULL,
        CreatedAt     datetimeoffset(3) NOT NULL CONSTRAINT DF_tblNovedadesProceso_CreatedAt DEFAULT (SYSDATETIMEOFFSET()),
        CONSTRAINT FK_tblNovedadesProceso_Viaje FOREIGN KEY (ViajeId) REFERENCES {S}.tblViajes (Id),
        CONSTRAINT FK_tblNovedadesProceso_Vehiculo FOREIGN KEY (VehiculoId) REFERENCES {S}.tblVehiculos (Id),
        CONSTRAINT FK_tblNovedadesProceso_Etapa FOREIGN KEY (EtapaId) REFERENCES {S}.tblEtapas (Id),
        CONSTRAINT FK_tblNovedadesProceso_Inspeccion FOREIGN KEY (InspeccionId) REFERENCES {S}.tblInspecciones (Id),
        CONSTRAINT FK_tblNovedadesProceso_Motivo FOREIGN KEY (MotivoCodigo) REFERENCES {S}.tblMotivosSinInspector (Codigo),
        CONSTRAINT FK_tblNovedadesProceso_Usuario FOREIGN KEY (UsuarioId) REFERENCES {S}.tblUsuarios (Id),
        CONSTRAINT CK_tblNovedadesProceso_Tipo CHECK (Tipo IN (~cargue_sin_inspector~, ~recibo_sin_entrega~)),
        -- El cargue sin inspector siempre dice por que; el texto solo acompana a un motivo y no puede quedar en blanco.
        CONSTRAINT CK_tblNovedadesProceso_Motivo CHECK (Tipo <> ~cargue_sin_inspector~ OR MotivoCodigo IS NOT NULL),
        CONSTRAINT CK_tblNovedadesProceso_Texto CHECK (MotivoTexto IS NULL OR (MotivoCodigo IS NOT NULL AND LEN(LTRIM(RTRIM(MotivoTexto))) > 0)),
        CONSTRAINT CK_tblNovedadesProceso_Lat CHECK (Lat IS NULL OR (Lat BETWEEN -90 AND 90)),
        CONSTRAINT CK_tblNovedadesProceso_Lng CHECK (Lng IS NULL OR (Lng BETWEEN -180 AND 180))
    );
    -- A lo sumo una novedad de cada tipo por inspeccion.
    CREATE UNIQUE INDEX UX_tblNovedadesProceso_Inspeccion_Tipo ON {S}.tblNovedadesProceso (InspeccionId, Tipo);
    CREATE INDEX IX_tblNovedadesProceso_Viaje ON {S}.tblNovedadesProceso (ViajeId);
    CREATE INDEX IX_tblNovedadesProceso_Vehiculo ON {S}.tblNovedadesProceso (VehiculoId);
    CREATE INDEX IX_tblNovedadesProceso_Tipo_Fecha ON {S}.tblNovedadesProceso (Tipo, Fecha DESC);
END'),
(N'trigger de las novedades de proceso: no se modifican', N'
CREATE OR ALTER TRIGGER {S}.TR_tblNovedadesProceso_NoModificar
ON {S}.tblNovedadesProceso
INSTEAD OF UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 51005, ~Una novedad de proceso no se modifica~, 1;
END'),
(N'trigger de las novedades de proceso: no se borran', N'
CREATE OR ALTER TRIGGER {S}.TR_tblNovedadesProceso_NoBorrar
ON {S}.tblNovedadesProceso
INSTEAD OF DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 51003, ~Este registro es evidencia y no se puede borrar~, 1;
END'),
(N'trigger de las novedades de proceso: el texto del motivo', N'
CREATE OR ALTER TRIGGER {S}.TR_tblNovedadesProceso_TextoDelMotivo
ON {S}.tblNovedadesProceso
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;
    -- El motivo "Otro" (RequiereTexto = 1) exige escribir el motivo; un motivo de la lista no admite texto.
    IF EXISTS (SELECT 1 FROM inserted i JOIN {S}.tblMotivosSinInspector m ON m.Codigo = i.MotivoCodigo
               WHERE (m.RequiereTexto = 1 AND i.MotivoTexto IS NULL) OR (m.RequiereTexto = 0 AND i.MotivoTexto IS NOT NULL))
        THROW 51008, ~El texto del motivo es obligatorio solo cuando el motivo lo requiere~, 1;
END'),
(N'tblInspecciones: columnas del motivo', N'
IF COL_LENGTH(N~{S}.tblInspecciones~, N~MotivoSinInspector~) IS NULL
    ALTER TABLE {S}.tblInspecciones ADD MotivoSinInspector varchar(30) NULL;
IF COL_LENGTH(N~{S}.tblInspecciones~, N~MotivoSinInspectorTexto~) IS NULL
    ALTER TABLE {S}.tblInspecciones ADD MotivoSinInspectorTexto nvarchar(300) NULL'),
(N'tblInspecciones: restricciones del motivo', N'
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N~FK_tblInspecciones_MotivoSinInspector~ AND parent_object_id = OBJECT_ID(N~{S}.tblInspecciones~))
    ALTER TABLE {S}.tblInspecciones ADD CONSTRAINT FK_tblInspecciones_MotivoSinInspector FOREIGN KEY (MotivoSinInspector) REFERENCES {S}.tblMotivosSinInspector (Codigo);
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N~CK_tblInspecciones_MotivoSinInspector~ AND parent_object_id = OBJECT_ID(N~{S}.tblInspecciones~))
    ALTER TABLE {S}.tblInspecciones ADD CONSTRAINT CK_tblInspecciones_MotivoSinInspector CHECK (MotivoSinInspector IS NULL OR Momento = ~salida~);
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N~CK_tblInspecciones_MotivoTexto~ AND parent_object_id = OBJECT_ID(N~{S}.tblInspecciones~))
    ALTER TABLE {S}.tblInspecciones ADD CONSTRAINT CK_tblInspecciones_MotivoTexto CHECK (MotivoSinInspectorTexto IS NULL OR (MotivoSinInspector IS NOT NULL AND LEN(LTRIM(RTRIM(MotivoSinInspectorTexto))) > 0))'),
(N'tblNotificaciones: tipo novedad_proceso', N'
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N~CK_tblNotificaciones_Tipo~ AND parent_object_id = OBJECT_ID(N~{S}.tblNotificaciones~) AND definition LIKE N~%novedad_proceso%~)
BEGIN
    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N~CK_tblNotificaciones_Tipo~ AND parent_object_id = OBJECT_ID(N~{S}.tblNotificaciones~))
        ALTER TABLE {S}.tblNotificaciones DROP CONSTRAINT CK_tblNotificaciones_Tipo;
    ALTER TABLE {S}.tblNotificaciones ADD CONSTRAINT CK_tblNotificaciones_Tipo CHECK (Tipo IN (~novedad_conductor~, ~respuesta_conductor~, ~hallazgo_critico~, ~umbral_patron~, ~captura_incompleta~, ~transferencia_pendiente~, ~reporte_diario~, ~novedad_proceso~));
END'),
(N'tblTransferenciasCustodia: suelta los cierres anteriores', N'
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N~CK_tblTransferencias_Cierre~ AND parent_object_id = OBJECT_ID(N~{S}.tblTransferenciasCustodia~) AND definition LIKE N~%recibo_sin_entrega%~)
BEGIN
    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N~CK_tblTransferencias_Cierre~ AND parent_object_id = OBJECT_ID(N~{S}.tblTransferenciasCustodia~))
        ALTER TABLE {S}.tblTransferenciasCustodia DROP CONSTRAINT CK_tblTransferencias_Cierre;
    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N~CK_tblTransferencias_TipoCierre~ AND parent_object_id = OBJECT_ID(N~{S}.tblTransferenciasCustodia~))
        ALTER TABLE {S}.tblTransferenciasCustodia DROP CONSTRAINT CK_tblTransferencias_TipoCierre;
    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N~CK_tblTransferencias_Testigo~ AND parent_object_id = OBJECT_ID(N~{S}.tblTransferenciasCustodia~))
        ALTER TABLE {S}.tblTransferenciasCustodia DROP CONSTRAINT CK_tblTransferencias_Testigo;
END'),
(N'tblTransferenciasCustodia: EntregaUsuarioId admite nulo', N'
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N~{S}.tblTransferenciasCustodia~) AND name = N~EntregaUsuarioId~ AND is_nullable = 0)
    ALTER TABLE {S}.tblTransferenciasCustodia ALTER COLUMN EntregaUsuarioId uniqueidentifier NULL'),
(N'tblTransferenciasCustodia: restricciones con recibo_sin_entrega', N'
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N~CK_tblTransferencias_TipoCierre~ AND parent_object_id = OBJECT_ID(N~{S}.tblTransferenciasCustodia~))
    ALTER TABLE {S}.tblTransferenciasCustodia ADD CONSTRAINT CK_tblTransferencias_TipoCierre CHECK (TipoCierre IS NULL OR TipoCierre IN (~firmada~, ~con_reservas~, ~no_firma~, ~recibo_sin_entrega~));
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N~CK_tblTransferencias_Testigo~ AND parent_object_id = OBJECT_ID(N~{S}.tblTransferenciasCustodia~))
    ALTER TABLE {S}.tblTransferenciasCustodia ADD CONSTRAINT CK_tblTransferencias_Testigo CHECK (TestigoUsuarioId IS NULL OR (TestigoUsuarioId <> EntregaUsuarioId AND TestigoUsuarioId <> RecibeUsuarioId));
-- Quien entrega solo puede faltar en un recibo sin entrega.
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N~CK_tblTransferencias_Entrega~ AND parent_object_id = OBJECT_ID(N~{S}.tblTransferenciasCustodia~))
    ALTER TABLE {S}.tblTransferenciasCustodia ADD CONSTRAINT CK_tblTransferencias_Entrega CHECK (EntregaUsuarioId IS NOT NULL OR ISNULL(TipoCierre, ~~) = ~recibo_sin_entrega~);
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N~CK_tblTransferencias_Cierre~ AND parent_object_id = OBJECT_ID(N~{S}.tblTransferenciasCustodia~))
    ALTER TABLE {S}.tblTransferenciasCustodia ADD CONSTRAINT CK_tblTransferencias_Cierre CHECK (
        TipoCierre IS NULL
        OR (TipoCierre = ~firmada~ AND FirmaEntrega IS NOT NULL AND FirmaEntrega <> ~~ AND FirmaRecibe IS NOT NULL AND FirmaRecibe <> ~~ AND ReservasTexto IS NULL)
        OR (TipoCierre = ~con_reservas~ AND FirmaEntrega IS NOT NULL AND FirmaEntrega <> ~~ AND FirmaRecibe IS NOT NULL AND FirmaRecibe <> ~~ AND ReservasTexto IS NOT NULL AND ReservasTexto <> ~~)
        OR (TipoCierre = ~no_firma~ AND NoFirmaParte IS NOT NULL AND MotivoNoFirma IS NOT NULL AND MotivoNoFirma <> ~~ AND TestigoUsuarioId IS NOT NULL AND EvidenciaConstanciaId IS NOT NULL)
        OR (TipoCierre = ~recibo_sin_entrega~ AND EntregaUsuarioId IS NULL AND FirmaEntrega IS NULL AND FirmaEntregaAt IS NULL AND FirmaEntregaIp IS NULL
            AND FirmaRecibe IS NOT NULL AND FirmaRecibe <> ~~ AND ReservasTexto IS NULL))');

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

-- Resultado (esperado: Tablas = 2; Motivos = 3 (o mas si ya habia otros); Triggers = 3; ColumnasInspeccion = 2; CierreNuevo = 1; EntregaNula = 1; TipoNotificacion = 1; NovedadesProceso = 0 en una base recien migrada).
DECLARE @cuentas nvarchar(max) = REPLACE(N'
SELECT
    (SELECT COUNT(*) FROM sys.tables WHERE name IN (N~tblMotivosSinInspector~, N~tblNovedadesProceso~) AND schema_id = SCHEMA_ID(N~{E}~)) AS Tablas,
    (SELECT COUNT(*) FROM {S}.tblMotivosSinInspector) AS Motivos,
    (SELECT COUNT(*) FROM sys.triggers WHERE name IN (N~TR_tblNovedadesProceso_NoModificar~, N~TR_tblNovedadesProceso_NoBorrar~, N~TR_tblNovedadesProceso_TextoDelMotivo~)) AS Triggers,
    (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N~{S}.tblInspecciones~) AND name IN (N~MotivoSinInspector~, N~MotivoSinInspectorTexto~)) AS ColumnasInspeccion,
    (SELECT COUNT(*) FROM sys.check_constraints WHERE name = N~CK_tblTransferencias_Cierre~ AND parent_object_id = OBJECT_ID(N~{S}.tblTransferenciasCustodia~) AND definition LIKE N~%recibo_sin_entrega%~) AS CierreNuevo,
    (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N~{S}.tblTransferenciasCustodia~) AND name = N~EntregaUsuarioId~ AND is_nullable = 1) AS EntregaNula,
    (SELECT COUNT(*) FROM sys.check_constraints WHERE name = N~CK_tblNotificaciones_Tipo~ AND parent_object_id = OBJECT_ID(N~{S}.tblNotificaciones~) AND definition LIKE N~%novedad_proceso%~) AS TipoNotificacion,
    (SELECT COUNT(*) FROM {S}.tblNovedadesProceso) AS NovedadesProceso', N'{S}', @q);
SET @cuentas = REPLACE(REPLACE(@cuentas, N'{E}', @esquema), N'~', N'''');
EXEC (@cuentas);
