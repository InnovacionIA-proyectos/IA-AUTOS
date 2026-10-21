/* =============================================================================
   Notificaciones, dispositivos y la novedad del conductor, para una base de IA Autos YA EXISTENTE (con 10-zona-dano-y-hallazgo.sql aplicado). Se ejecuta UNA vez
   (si ya esta aplicado, no hace nada) y todo ocurre en UNA transaccion: si algo falla se revierte completo.

   Que hace:
     1. tblNotificaciones: las alertas y avisos del sistema y las respuestas del coordinador a una novedad del conductor, una fila por canal (bandeja, push o correo) y
        por destinatario. Es append-only salvo la lectura (LeidaAt) y la respuesta (texto, medio, quien y cuando): cambiar otra columna falla (error 51006) y borrar una
        fila falla (error 51003). ClaveDedupe es unica para que una alerta no se repita; el canal correo solo se admite en el reporte diario.
     2. tblDispositivos: los celulares donde un usuario recibe las notificaciones push (token FCM unico).
     3. tblHallazgos: la novedad del conductor (con TipoNovedad) no tiene zona, clase, nivel ni texto libre hasta que un inspector la revisa, y al revisarla se
        completan; el conductor nunca asigna el nivel V; y una novedad solo puede ser de la inspeccion de SALIDA de TRANSPORTE (error 51007).

   Seguridad de los datos: no borra ni cambia ningun dato; solo crea las tablas, las restricciones y los triggers. Las restricciones del hallazgo se validan contra lo ya
   guardado: si algun hallazgo no las cumpliera, el script aborta sin cambiar nada.

   Como ejecutarlo: abrir en SSMS, DBeaver o sqlcmd CONECTADO a la base de IA Autos, ajustar @esquema y ejecutar completo, en UNA sola ejecucion de script, con un usuario con
   permiso para crear tablas y triggers (no el usuario de la aplicacion). Ejecutarlo ANTES de desplegar la API.
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
IF OBJECT_ID(@q + N'.tblUsuarios', N'U') IS NULL OR OBJECT_ID(@q + N'.tblClientes', N'U') IS NULL OR OBJECT_ID(@q + N'.tblViajes', N'U') IS NULL
   OR OBJECT_ID(@q + N'.tblVehiculos', N'U') IS NULL OR OBJECT_ID(@q + N'.tblHallazgos', N'U') IS NULL OR OBJECT_ID(@q + N'.tblInspecciones', N'U') IS NULL
   OR OBJECT_ID(@q + N'.tblEtapas', N'U') IS NULL OR OBJECT_ID(@q + N'.tblEtapasCatalogo', N'U') IS NULL
    THROW 50001, N'Faltan tablas de IA Autos en el esquema indicado: revise @esquema.', 1;
IF COL_LENGTH(@q + N'.tblHallazgos', N'TipoNovedad') IS NULL OR COL_LENGTH(@q + N'.tblHallazgos', N'RevisadoAt') IS NULL
    THROW 50002, N'Falta el origen del hallazgo: ejecute antes 10-zona-dano-y-hallazgo.sql.', 1;

IF OBJECT_ID(@q + N'.tblNotificaciones', N'U') IS NOT NULL AND OBJECT_ID(@q + N'.tblDispositivos', N'U') IS NOT NULL
   AND EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_tblHallazgos_NovedadSinRevisar' AND parent_object_id = OBJECT_ID(@q + N'.tblHallazgos'))
   AND EXISTS (SELECT 1 FROM sys.triggers WHERE name = N'TR_tblHallazgos_NovedadDeTransporte' AND parent_id = OBJECT_ID(@q + N'.tblHallazgos'))
BEGIN
    PRINT N'Las notificaciones, los dispositivos y la novedad del conductor ya estan aplicados: no se hace nada.';
    RETURN;
END

-- Pasos en orden. {S} = esquema entre corchetes; ~ = comilla simple dentro del texto.
DECLARE @paso TABLE (n int IDENTITY(1, 1) PRIMARY KEY, descripcion nvarchar(100), s nvarchar(max));

INSERT @paso (descripcion, s) VALUES
(N'tblNotificaciones', N'
IF OBJECT_ID(N~{S}.tblNotificaciones~, N~U~) IS NULL
BEGIN
    CREATE TABLE {S}.tblNotificaciones
    (
        Id               uniqueidentifier  NOT NULL CONSTRAINT PK_tblNotificaciones PRIMARY KEY,
        Tipo             varchar(30)       NOT NULL,
        Urgencia         varchar(5)        NOT NULL,
        UsuarioId        uniqueidentifier  NULL,
        ClienteId        uniqueidentifier  NULL,
        ViajeId          uniqueidentifier  NULL,
        VehiculoId       uniqueidentifier  NULL,
        HallazgoId       uniqueidentifier  NULL,
        InspeccionId     uniqueidentifier  NULL,
        Titulo           nvarchar(200)     NOT NULL,
        Mensaje          nvarchar(1000)    NOT NULL,
        Canal            varchar(10)       NOT NULL,
        EnviadaAt        datetimeoffset(3) NULL,
        ErrorEnvio       nvarchar(500)     NULL,
        LeidaAt          datetimeoffset(3) NULL,
        RespuestaTexto   nvarchar(1000)    NULL,
        RespuestaMedio   varchar(10)       NULL,
        RespondidaPor    uniqueidentifier  NULL,
        RespondidaAt     datetimeoffset(3) NULL,
        ClaveDedupe      varchar(300)      NOT NULL,
        CreatedAt        datetimeoffset(3) NOT NULL CONSTRAINT DF_tblNotificaciones_CreatedAt DEFAULT (SYSDATETIMEOFFSET()),
        CONSTRAINT FK_tblNotificaciones_Usuario FOREIGN KEY (UsuarioId) REFERENCES {S}.tblUsuarios (Id),
        CONSTRAINT FK_tblNotificaciones_Cliente FOREIGN KEY (ClienteId) REFERENCES {S}.tblClientes (Id),
        CONSTRAINT FK_tblNotificaciones_Viaje FOREIGN KEY (ViajeId) REFERENCES {S}.tblViajes (Id),
        CONSTRAINT FK_tblNotificaciones_Vehiculo FOREIGN KEY (VehiculoId) REFERENCES {S}.tblVehiculos (Id),
        CONSTRAINT FK_tblNotificaciones_Hallazgo FOREIGN KEY (HallazgoId) REFERENCES {S}.tblHallazgos (Id),
        CONSTRAINT FK_tblNotificaciones_Inspeccion FOREIGN KEY (InspeccionId) REFERENCES {S}.tblInspecciones (Id),
        CONSTRAINT FK_tblNotificaciones_RespondidaPor FOREIGN KEY (RespondidaPor) REFERENCES {S}.tblUsuarios (Id),
        CONSTRAINT UQ_tblNotificaciones_ClaveDedupe UNIQUE (ClaveDedupe),
        CONSTRAINT CK_tblNotificaciones_Tipo CHECK (Tipo IN (~novedad_conductor~, ~respuesta_conductor~, ~hallazgo_critico~, ~umbral_patron~, ~captura_incompleta~, ~transferencia_pendiente~, ~reporte_diario~)),
        CONSTRAINT CK_tblNotificaciones_Urgencia CHECK (Urgencia IN (~alta~, ~media~)),
        CONSTRAINT CK_tblNotificaciones_Canal CHECK (Canal IN (~bandeja~, ~push~, ~correo~)),
        CONSTRAINT CK_tblNotificaciones_RespuestaMedio CHECK (RespuestaMedio IS NULL OR RespuestaMedio IN (~app~, ~llamada~)),
        -- Solo el reporte diario no tiene destinatario, y solo el reporte diario se envia por correo.
        CONSTRAINT CK_tblNotificaciones_Destinatario CHECK (UsuarioId IS NOT NULL OR Tipo = ~reporte_diario~),
        CONSTRAINT CK_tblNotificaciones_Correo CHECK (Canal <> ~correo~ OR Tipo = ~reporte_diario~),
        -- La respuesta del coordinador es completa (texto, medio, quien y cuando) y solo existe sobre una novedad del conductor.
        CONSTRAINT CK_tblNotificaciones_Respuesta CHECK (
            (RespuestaTexto IS NULL AND RespuestaMedio IS NULL AND RespondidaPor IS NULL AND RespondidaAt IS NULL)
            OR (RespuestaTexto IS NOT NULL AND RespuestaMedio IS NOT NULL AND RespondidaPor IS NOT NULL AND RespondidaAt IS NOT NULL AND Tipo = ~novedad_conductor~))
    );
    CREATE INDEX IX_tblNotificaciones_Usuario_Leida ON {S}.tblNotificaciones (UsuarioId, LeidaAt);
END'),
(N'trigger de las notificaciones: solo se lee y se responde', N'
CREATE OR ALTER TRIGGER {S}.TR_tblNotificaciones_SoloLecturaYRespuesta
ON {S}.tblNotificaciones
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF UPDATE(Id) OR UPDATE(Tipo) OR UPDATE(Urgencia) OR UPDATE(UsuarioId) OR UPDATE(ClienteId) OR UPDATE(ViajeId) OR UPDATE(VehiculoId) OR UPDATE(HallazgoId)
       OR UPDATE(InspeccionId) OR UPDATE(Titulo) OR UPDATE(Mensaje) OR UPDATE(Canal) OR UPDATE(EnviadaAt) OR UPDATE(ErrorEnvio) OR UPDATE(ClaveDedupe) OR UPDATE(CreatedAt)
        THROW 51006, ~Una notificacion solo admite cambiar su lectura y su respuesta~, 1;
END'),
(N'trigger de las notificaciones: no se borran', N'
CREATE OR ALTER TRIGGER {S}.TR_tblNotificaciones_NoBorrar
ON {S}.tblNotificaciones
INSTEAD OF DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 51003, ~Este registro es evidencia y no se puede borrar~, 1;
END'),
(N'tblDispositivos', N'
IF OBJECT_ID(N~{S}.tblDispositivos~, N~U~) IS NULL
BEGIN
    CREATE TABLE {S}.tblDispositivos
    (
        Id            uniqueidentifier  NOT NULL CONSTRAINT PK_tblDispositivos PRIMARY KEY,
        UsuarioId     uniqueidentifier  NOT NULL,
        TokenFcm      varchar(512)      NOT NULL,
        Plataforma    varchar(20)       NOT NULL,
        Modelo        nvarchar(100)     NULL,
        UltimoUsoAt   datetimeoffset(3) NOT NULL CONSTRAINT DF_tblDispositivos_UltimoUsoAt DEFAULT (SYSDATETIMEOFFSET()),
        Activo        bit               NOT NULL CONSTRAINT DF_tblDispositivos_Activo DEFAULT (1),
        CreatedAt     datetimeoffset(3) NOT NULL CONSTRAINT DF_tblDispositivos_CreatedAt DEFAULT (SYSDATETIMEOFFSET()),
        UpdatedAt     datetimeoffset(3) NOT NULL CONSTRAINT DF_tblDispositivos_UpdatedAt DEFAULT (SYSDATETIMEOFFSET()),
        CONSTRAINT FK_tblDispositivos_Usuario FOREIGN KEY (UsuarioId) REFERENCES {S}.tblUsuarios (Id),
        CONSTRAINT UQ_tblDispositivos_TokenFcm UNIQUE (TokenFcm),
        CONSTRAINT CK_tblDispositivos_Token CHECK (LEN(TokenFcm) > 0),
        CONSTRAINT CK_tblDispositivos_Plataforma CHECK (Plataforma IN (~android~, ~ios~, ~web~))
    );
    CREATE INDEX IX_tblDispositivos_Usuario ON {S}.tblDispositivos (UsuarioId, Activo);
END'),
(N'hallazgo: CK_tblHallazgos_NovedadSinRevisar', N'
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N~CK_tblHallazgos_NovedadSinRevisar~ AND parent_object_id = OBJECT_ID(N~{S}.tblHallazgos~))
    ALTER TABLE {S}.tblHallazgos WITH CHECK ADD CONSTRAINT CK_tblHallazgos_NovedadSinRevisar CHECK (
    TipoNovedad IS NULL OR RevisadoAt IS NOT NULL OR (ZonaId IS NULL AND ClaseDano IS NULL AND NivelSeveridad IS NULL AND Nota IS NULL))'),
(N'hallazgo: CK_tblHallazgos_NovedadRevisada', N'
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N~CK_tblHallazgos_NovedadRevisada~ AND parent_object_id = OBJECT_ID(N~{S}.tblHallazgos~))
    ALTER TABLE {S}.tblHallazgos WITH CHECK ADD CONSTRAINT CK_tblHallazgos_NovedadRevisada CHECK (
    TipoNovedad IS NULL OR RevisadoAt IS NULL OR (ZonaId IS NOT NULL AND ClaseDano IS NOT NULL AND NivelSeveridad IS NOT NULL))'),
(N'hallazgo: CK_tblHallazgos_NivelDelConductor', N'
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N~CK_tblHallazgos_NivelDelConductor~ AND parent_object_id = OBJECT_ID(N~{S}.tblHallazgos~))
    ALTER TABLE {S}.tblHallazgos WITH CHECK ADD CONSTRAINT CK_tblHallazgos_NivelDelConductor CHECK (Origen <> ~conductor~ OR RevisadoAt IS NOT NULL OR NivelSeveridad IS NULL)'),
(N'trigger del hallazgo: una novedad es de la salida de Transporte', N'
CREATE OR ALTER TRIGGER {S}.TR_tblHallazgos_NovedadDeTransporte
ON {S}.tblHallazgos
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted h
               JOIN {S}.tblInspecciones i ON i.Id = h.InspeccionId
               JOIN {S}.tblEtapas e ON e.Id = i.EtapaId
               JOIN {S}.tblEtapasCatalogo c ON c.Id = e.EtapaCatalogoId
               WHERE h.TipoNovedad IS NOT NULL AND (i.Momento <> ~salida~ OR c.Codigo <> ~transporte~))
        THROW 51007, ~Una novedad del conductor es de la inspeccion de salida de Transporte~, 1;
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

-- Resultado (esperado: 2 tablas, 3 restricciones del hallazgo, 3 triggers nuevos y ninguna notificacion ni dispositivo).
DECLARE @cuentas nvarchar(max) = REPLACE(N'
SELECT
    (SELECT COUNT(*) FROM sys.tables WHERE name IN (N~tblNotificaciones~, N~tblDispositivos~) AND schema_id = SCHEMA_ID(N~{E}~)) AS Tablas,
    (SELECT COUNT(*) FROM sys.check_constraints WHERE name IN (N~CK_tblHallazgos_NovedadSinRevisar~, N~CK_tblHallazgos_NovedadRevisada~, N~CK_tblHallazgos_NivelDelConductor~)
        AND parent_object_id = OBJECT_ID(N~{S}.tblHallazgos~)) AS RestriccionesDelHallazgo,
    (SELECT COUNT(*) FROM sys.triggers WHERE name IN (N~TR_tblNotificaciones_SoloLecturaYRespuesta~, N~TR_tblNotificaciones_NoBorrar~, N~TR_tblHallazgos_NovedadDeTransporte~)) AS Triggers,
    (SELECT COUNT(*) FROM {S}.tblNotificaciones) AS Notificaciones,
    (SELECT COUNT(*) FROM {S}.tblDispositivos) AS Dispositivos', N'{S}', @q);
SET @cuentas = REPLACE(REPLACE(@cuentas, N'{E}', @esquema), N'~', N'''');
EXEC (@cuentas);
