/* =============================================================================
   IA-AUTOS - DDL (SQL Server 2019, nivel de compatibilidad 150)
   Esquema de la aplicacion "Traza Autos 360 / Inventario Visual por VIN".

   Convenciones del estandar: tablas con prefijo tbl, columnas en PascalCase.
   (El prefijo tblSap no aplica: no hay tablas de SAP.)

   Uso:
     sqlcmd -S <servidor> -d <base> -U <usuario_admin> -P <password> -I -b -i ddl.sql
   Importante: ejecutar con -I (QUOTED_IDENTIFIER ON), requerido por las vistas
   e indices filtrados.

   Es idempotente: puede ejecutarse varias veces sin perder datos.
   No contiene datos de usuarios ni contrasenas: los usuarios iniciales se
   crean con la herramienta de migracion o con la API (ver docs/).
   ============================================================================= */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

/* -----------------------------------------------------------------------------
   tblUsuarios
   ----------------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.tblUsuarios', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblUsuarios
    (
        Id                uniqueidentifier NOT NULL CONSTRAINT PK_tblUsuarios PRIMARY KEY,
        Nombre            nvarchar(200)    NOT NULL,
        Email             nvarchar(320)    NOT NULL,
        Rol               varchar(30)      NOT NULL,
        SubtipoInspector  varchar(30)      NULL,
        Activo            bit              NOT NULL CONSTRAINT DF_tblUsuarios_Activo DEFAULT (1),
        PasswordHash      nvarchar(400)    NULL,
        CreatedAt         datetimeoffset(3) NOT NULL CONSTRAINT DF_tblUsuarios_CreatedAt DEFAULT (SYSDATETIMEOFFSET()),
        UpdatedAt         datetimeoffset(3) NOT NULL CONSTRAINT DF_tblUsuarios_UpdatedAt DEFAULT (SYSDATETIMEOFFSET()),
        CONSTRAINT UQ_tblUsuarios_Email UNIQUE (Email),
        CONSTRAINT CK_tblUsuarios_Rol CHECK (Rol IN
            ('inspector', 'conductor', 'coordinador_calidad', 'comercial', 'admin', 'super_admin')),
        CONSTRAINT CK_tblUsuarios_Subtipo CHECK (SubtipoInspector IS NULL OR SubtipoInspector IN
            ('importacion', 'nacional', 'export_preembarque', 'export_recibido'))
    );
END
GO

/* -----------------------------------------------------------------------------
   Sesion del portal (inicio de sesion unico)
   - PortalUsuarioId: id de la persona en el portal; NULL para quien no entra por el portal (conductores).
     Unico entre los que lo tienen (indice filtrado).
   - tblRefreshTokens: refresh tokens como hash, con familia para detectar el reuso de un token robado.
   ----------------------------------------------------------------------------- */
IF COL_LENGTH(N'dbo.tblUsuarios', N'PortalUsuarioId') IS NULL
    ALTER TABLE dbo.tblUsuarios ADD PortalUsuarioId bigint NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblUsuarios_PortalUsuarioId' AND object_id = OBJECT_ID(N'dbo.tblUsuarios'))
    CREATE UNIQUE INDEX UX_tblUsuarios_PortalUsuarioId ON dbo.tblUsuarios (PortalUsuarioId) WHERE PortalUsuarioId IS NOT NULL;
GO

IF OBJECT_ID(N'dbo.tblRefreshTokens', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblRefreshTokens
    (
        Id               uniqueidentifier  NOT NULL CONSTRAINT PK_tblRefreshTokens PRIMARY KEY,
        UsuarioId        uniqueidentifier  NOT NULL,
        TokenHash        char(64)          NOT NULL,
        FamiliaId        uniqueidentifier  NOT NULL,
        ExpiraAt         datetimeoffset(3) NOT NULL,
        RevocadoAt       datetimeoffset(3) NULL,
        ReemplazadoPorId uniqueidentifier  NULL,
        Ip               varchar(45)       NULL,
        Dispositivo      nvarchar(300)     NULL,
        CreatedAt        datetimeoffset(3) NOT NULL CONSTRAINT DF_tblRefreshTokens_CreatedAt DEFAULT (SYSDATETIMEOFFSET()),
        UpdatedAt        datetimeoffset(3) NOT NULL CONSTRAINT DF_tblRefreshTokens_UpdatedAt DEFAULT (SYSDATETIMEOFFSET()),
        CONSTRAINT UQ_tblRefreshTokens_TokenHash UNIQUE (TokenHash),
        CONSTRAINT FK_tblRefreshTokens_Usuario FOREIGN KEY (UsuarioId) REFERENCES dbo.tblUsuarios (Id)
    );
    CREATE INDEX IX_tblRefreshTokens_Familia ON dbo.tblRefreshTokens (FamiliaId);
    CREATE INDEX IX_tblRefreshTokens_Usuario ON dbo.tblRefreshTokens (UsuarioId);
END
GO

/* -----------------------------------------------------------------------------
   tblVehiculos (unidad transportada, identificada por VIN; el VIN admite
   numeros de chasis de longitud variable)
   ----------------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.tblVehiculos', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblVehiculos
    (
        Id            uniqueidentifier NOT NULL CONSTRAINT PK_tblVehiculos PRIMARY KEY,
        Vin           varchar(64)      NOT NULL,
        Marca         nvarchar(100)    NOT NULL,
        TipoVehiculo  nvarchar(100)    NULL,
        Modelo        nvarchar(300)    NULL,
        Chasis        nvarchar(100)    NULL,
        Color         nvarchar(100)    NULL,
        Operacion     varchar(20)      NOT NULL,
        CreatedAt     datetimeoffset(3) NOT NULL CONSTRAINT DF_tblVehiculos_CreatedAt DEFAULT (SYSDATETIMEOFFSET()),
        UpdatedAt     datetimeoffset(3) NOT NULL CONSTRAINT DF_tblVehiculos_UpdatedAt DEFAULT (SYSDATETIMEOFFSET()),
        CONSTRAINT UQ_tblVehiculos_Vin UNIQUE (Vin),
        CONSTRAINT CK_tblVehiculos_Operacion CHECK (Operacion IN ('importacion', 'exportacion', 'nacional'))
    );
END
GO

/* -----------------------------------------------------------------------------
   tblPuntosCustodia (catalogo de los 7 puntos de custodia)
   ----------------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.tblPuntosCustodia', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblPuntosCustodia
    (
        Id         uniqueidentifier NOT NULL CONSTRAINT PK_tblPuntosCustodia PRIMARY KEY,
        Nombre     varchar(30)      NOT NULL,
        CreatedAt  datetimeoffset(3) NOT NULL CONSTRAINT DF_tblPuntosCustodia_CreatedAt DEFAULT (SYSDATETIMEOFFSET()),
        UpdatedAt  datetimeoffset(3) NOT NULL CONSTRAINT DF_tblPuntosCustodia_UpdatedAt DEFAULT (SYSDATETIMEOFFSET()),
        CONSTRAINT UQ_tblPuntosCustodia_Nombre UNIQUE (Nombre),
        CONSTRAINT CK_tblPuntosCustodia_Nombre CHECK (Nombre IN
            ('cargue', 'punto_origen', 'certificadora', 'punto_destino', 'transporte', 'descargue', 'concesionario'))
    );
END
GO

/* -----------------------------------------------------------------------------
   tblEtapas (fase del proceso por vehiculo y punto de custodia)
   ----------------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.tblEtapas', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblEtapas
    (
        Id                  uniqueidentifier NOT NULL CONSTRAINT PK_tblEtapas PRIMARY KEY,
        VehiculoId          uniqueidentifier NOT NULL,
        PuntoCustodiaId     uniqueidentifier NOT NULL,
        Orden               int              NOT NULL,
        RequiereInspeccion  bit              NOT NULL CONSTRAINT DF_tblEtapas_RequiereInspeccion DEFAULT (1),
        CreatedAt           datetimeoffset(3) NOT NULL CONSTRAINT DF_tblEtapas_CreatedAt DEFAULT (SYSDATETIMEOFFSET()),
        UpdatedAt           datetimeoffset(3) NOT NULL CONSTRAINT DF_tblEtapas_UpdatedAt DEFAULT (SYSDATETIMEOFFSET()),
        CONSTRAINT FK_tblEtapas_Vehiculo FOREIGN KEY (VehiculoId) REFERENCES dbo.tblVehiculos (Id) ON DELETE CASCADE,
        CONSTRAINT FK_tblEtapas_PuntoCustodia FOREIGN KEY (PuntoCustodiaId) REFERENCES dbo.tblPuntosCustodia (Id),
        CONSTRAINT UQ_tblEtapas_Vehiculo_Orden UNIQUE (VehiculoId, Orden),
        CONSTRAINT CK_tblEtapas_Orden CHECK (Orden >= 1)
    );
    CREATE INDEX IX_tblEtapas_PuntoCustodia ON dbo.tblEtapas (PuntoCustodiaId);
END
GO

/* -----------------------------------------------------------------------------
   tblInspecciones (un informe por etapa; al cerrarse es inmutable)
   Latitud y longitud en float (precision GPS completa).
   ----------------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.tblInspecciones', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblInspecciones
    (
        Id               uniqueidentifier NOT NULL CONSTRAINT PK_tblInspecciones PRIMARY KEY,
        EtapaId          uniqueidentifier NOT NULL,
        UsuarioId        uniqueidentifier NOT NULL,
        Momento          varchar(20)      NOT NULL,
        Fecha            datetimeoffset(3) NOT NULL,
        Lat              float            NULL,
        Lng              float            NULL,
        Estado           varchar(20)      NOT NULL CONSTRAINT DF_tblInspecciones_Estado DEFAULT ('borrador'),
        ConductorNombre  nvarchar(200)    NULL,
        Placa            nvarchar(50)     NULL,
        Trailer          nvarchar(50)     NULL,
        Notas            nvarchar(1000)   NULL,
        CreatedAt        datetimeoffset(3) NOT NULL CONSTRAINT DF_tblInspecciones_CreatedAt DEFAULT (SYSDATETIMEOFFSET()),
        UpdatedAt        datetimeoffset(3) NOT NULL CONSTRAINT DF_tblInspecciones_UpdatedAt DEFAULT (SYSDATETIMEOFFSET()),
        CONSTRAINT FK_tblInspecciones_Etapa FOREIGN KEY (EtapaId) REFERENCES dbo.tblEtapas (Id) ON DELETE CASCADE,
        CONSTRAINT FK_tblInspecciones_Usuario FOREIGN KEY (UsuarioId) REFERENCES dbo.tblUsuarios (Id),
        -- A lo sumo un informe por etapa y momento (los valores check_in/check_out se conservan por historico).
        CONSTRAINT UQ_tblInspecciones_Etapa_Momento UNIQUE (EtapaId, Momento),
        CONSTRAINT CK_tblInspecciones_Momento CHECK (Momento IN ('check_in', 'check_out', 'informe')),
        CONSTRAINT CK_tblInspecciones_Estado CHECK (Estado IN ('borrador', 'cerrada')),
        CONSTRAINT CK_tblInspecciones_Lat CHECK (Lat IS NULL OR (Lat BETWEEN -90 AND 90)),
        CONSTRAINT CK_tblInspecciones_Lng CHECK (Lng IS NULL OR (Lng BETWEEN -180 AND 180))
    );
    CREATE INDEX IX_tblInspecciones_Usuario ON dbo.tblInspecciones (UsuarioId);
    CREATE INDEX IX_tblInspecciones_Fecha ON dbo.tblInspecciones (Fecha DESC);
    -- Soporta la definicion de "movilizado" (existe informe cerrado por etapa).
    CREATE INDEX IX_tblInspecciones_Etapa_Estado ON dbo.tblInspecciones (EtapaId, Estado);
END
GO

/* -----------------------------------------------------------------------------
   tblHallazgos (averia marcada sobre el diagrama)
   CoordsDiagrama: JSON {"x":0..1,"y":0..1}. SQL Server 2019 no tiene tipo JSON nativo.
   ----------------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.tblHallazgos', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblHallazgos
    (
        Id              uniqueidentifier NOT NULL CONSTRAINT PK_tblHallazgos PRIMARY KEY,
        InspeccionId    uniqueidentifier NOT NULL,
        ParteVehiculo   nvarchar(200)    NOT NULL,
        ClaseDano       varchar(30)      NOT NULL,
        TamanoDano      varchar(30)      NOT NULL,
        CoordsDiagrama  nvarchar(200)    NULL,
        CreatedAt       datetimeoffset(3) NOT NULL CONSTRAINT DF_tblHallazgos_CreatedAt DEFAULT (SYSDATETIMEOFFSET()),
        UpdatedAt       datetimeoffset(3) NOT NULL CONSTRAINT DF_tblHallazgos_UpdatedAt DEFAULT (SYSDATETIMEOFFSET()),
        CONSTRAINT FK_tblHallazgos_Inspeccion FOREIGN KEY (InspeccionId) REFERENCES dbo.tblInspecciones (Id) ON DELETE CASCADE,
        CONSTRAINT CK_tblHallazgos_ClaseDano CHECK (ClaseDano IN
            ('deformacion', 'rotura', 'cortado', 'perforacion', 'golpe',
             'mancha', 'raya_pintar', 'desconchado', 'particulas', 'faltante')),
        CONSTRAINT CK_tblHallazgos_TamanoDano CHECK (TamanoDano IN
            ('1_5cm', '5_10cm', '10_20cm', 'mas_20cm', 'requiere_reemplazo', 'mas_10_particulas')),
        CONSTRAINT CK_tblHallazgos_Coords CHECK (CoordsDiagrama IS NULL OR ISJSON(CoordsDiagrama) = 1)
    );
    CREATE INDEX IX_tblHallazgos_Inspeccion ON dbo.tblHallazgos (InspeccionId);
    -- Mapa de calor / patrones por zona y clase de dano.
    CREATE INDEX IX_tblHallazgos_Parte_Clase ON dbo.tblHallazgos (ParteVehiculo, ClaseDano);
END
GO

/* -----------------------------------------------------------------------------
   tblEvidencias (foto sellada; append-only por diseno)
   HallazgoId usa NO ACTION: SQL Server no permite SET NULL junto a la cascada
   desde tblInspecciones (varias rutas de cascada). La aplicacion no elimina hallazgos.
   ----------------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.tblEvidencias', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblEvidencias
    (
        Id            uniqueidentifier NOT NULL CONSTRAINT PK_tblEvidencias PRIMARY KEY,
        InspeccionId  uniqueidentifier NOT NULL,
        HallazgoId    uniqueidentifier NULL,
        StoragePath   nvarchar(500)    NOT NULL,
        SelloFecha    datetimeoffset(3) NOT NULL,
        SelloLat      float            NULL,
        SelloLng      float            NULL,
        CreatedAt     datetimeoffset(3) NOT NULL CONSTRAINT DF_tblEvidencias_CreatedAt DEFAULT (SYSDATETIMEOFFSET()),
        UpdatedAt     datetimeoffset(3) NOT NULL CONSTRAINT DF_tblEvidencias_UpdatedAt DEFAULT (SYSDATETIMEOFFSET()),
        CONSTRAINT FK_tblEvidencias_Inspeccion FOREIGN KEY (InspeccionId) REFERENCES dbo.tblInspecciones (Id) ON DELETE CASCADE,
        CONSTRAINT FK_tblEvidencias_Hallazgo FOREIGN KEY (HallazgoId) REFERENCES dbo.tblHallazgos (Id),
        CONSTRAINT CK_tblEvidencias_Lat CHECK (SelloLat IS NULL OR (SelloLat BETWEEN -90 AND 90)),
        CONSTRAINT CK_tblEvidencias_Lng CHECK (SelloLng IS NULL OR (SelloLng BETWEEN -180 AND 180))
    );
    CREATE INDEX IX_tblEvidencias_Inspeccion ON dbo.tblEvidencias (InspeccionId);
    CREATE INDEX IX_tblEvidencias_Hallazgo ON dbo.tblEvidencias (HallazgoId);
END
GO

/* -----------------------------------------------------------------------------
   tblTransferenciasCustodia (entrega y recibo firmados; una por inspeccion)
   FirmaEntrega / FirmaRecibe: ruta de la imagen de la firma o trazo de la firma dibujada.
   ----------------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.tblTransferenciasCustodia', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblTransferenciasCustodia
    (
        Id                uniqueidentifier NOT NULL CONSTRAINT PK_tblTransferenciasCustodia PRIMARY KEY,
        InspeccionId      uniqueidentifier NOT NULL,
        EntregaUsuarioId  uniqueidentifier NOT NULL,
        RecibeUsuarioId   uniqueidentifier NOT NULL,
        FirmaEntrega      nvarchar(max)    NULL,
        FirmaRecibe       nvarchar(max)    NULL,
        FirmadaAt         datetimeoffset(3) NULL,
        CreatedAt         datetimeoffset(3) NOT NULL CONSTRAINT DF_tblTransferenciasCustodia_CreatedAt DEFAULT (SYSDATETIMEOFFSET()),
        UpdatedAt         datetimeoffset(3) NOT NULL CONSTRAINT DF_tblTransferenciasCustodia_UpdatedAt DEFAULT (SYSDATETIMEOFFSET()),
        CONSTRAINT FK_tblTransferencias_Inspeccion FOREIGN KEY (InspeccionId) REFERENCES dbo.tblInspecciones (Id) ON DELETE CASCADE,
        CONSTRAINT FK_tblTransferencias_Entrega FOREIGN KEY (EntregaUsuarioId) REFERENCES dbo.tblUsuarios (Id),
        CONSTRAINT FK_tblTransferencias_Recibe FOREIGN KEY (RecibeUsuarioId) REFERENCES dbo.tblUsuarios (Id),
        CONSTRAINT UQ_tblTransferencias_Inspeccion UNIQUE (InspeccionId)
    );
    CREATE INDEX IX_tblTransferencias_Entrega ON dbo.tblTransferenciasCustodia (EntregaUsuarioId);
    CREATE INDEX IX_tblTransferencias_Recibe ON dbo.tblTransferenciasCustodia (RecibeUsuarioId);
END
GO

/* -----------------------------------------------------------------------------
   tblAsignaciones (el admin asigna que VIN se transporta en cual ninera)
   ----------------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.tblAsignaciones', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblAsignaciones
    (
        Id            uniqueidentifier NOT NULL CONSTRAINT PK_tblAsignaciones PRIMARY KEY,
        VehiculoId    uniqueidentifier NOT NULL,
        Ninera        nvarchar(100)    NOT NULL,
        ConductorId   uniqueidentifier NULL,
        AsignadoPor   uniqueidentifier NOT NULL,
        CreatedAt     datetimeoffset(3) NOT NULL CONSTRAINT DF_tblAsignaciones_CreatedAt DEFAULT (SYSDATETIMEOFFSET()),
        UpdatedAt     datetimeoffset(3) NOT NULL CONSTRAINT DF_tblAsignaciones_UpdatedAt DEFAULT (SYSDATETIMEOFFSET()),
        CONSTRAINT FK_tblAsignaciones_Vehiculo FOREIGN KEY (VehiculoId) REFERENCES dbo.tblVehiculos (Id) ON DELETE CASCADE,
        CONSTRAINT FK_tblAsignaciones_Conductor FOREIGN KEY (ConductorId) REFERENCES dbo.tblUsuarios (Id) ON DELETE SET NULL,
        CONSTRAINT FK_tblAsignaciones_AsignadoPor FOREIGN KEY (AsignadoPor) REFERENCES dbo.tblUsuarios (Id)
    );
    CREATE INDEX IX_tblAsignaciones_Vehiculo ON dbo.tblAsignaciones (VehiculoId, CreatedAt DESC);
    CREATE INDEX IX_tblAsignaciones_Conductor ON dbo.tblAsignaciones (ConductorId);
    CREATE INDEX IX_tblAsignaciones_AsignadoPor ON dbo.tblAsignaciones (AsignadoPor);
END
GO

/* -----------------------------------------------------------------------------
   tblOrdenesSofasa (ordenes de carga ingeridas por el ETL; idempotente por SourceId)
   ----------------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.tblOrdenesSofasa', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblOrdenesSofasa
    (
        Id                  uniqueidentifier NOT NULL CONSTRAINT PK_tblOrdenesSofasa PRIMARY KEY,
        SourceId            bigint           NOT NULL,
        StatusCode          nvarchar(100)    NULL,
        DatetimeEvent       datetimeoffset(3) NULL,
        OrdenNumber         nvarchar(255)    NULL,
        OrdenSap            nvarchar(255)    NULL,
        OriginCedi          nvarchar(255)    NULL,
        Vin                 varchar(64)      NULL,
        Make                nvarchar(255)    NULL,
        ModelDescription    nvarchar(500)    NULL,
        DealerCode          nvarchar(255)    NULL,
        DestinationCity     nvarchar(255)    NULL,
        DestinationAddress  nvarchar(1000)   NULL,
        LoadingDate         datetimeoffset(3) NULL,
        Appointment         datetimeoffset(3) NULL,
        LoadingDock         nvarchar(255)    NULL,
        Weight              decimal(18, 4)   NULL,
        VehiculoId          uniqueidentifier NULL,
        SyncedAt            datetimeoffset(3) NOT NULL CONSTRAINT DF_tblOrdenesSofasa_SyncedAt DEFAULT (SYSDATETIMEOFFSET()),
        CreatedAt           datetimeoffset(3) NOT NULL CONSTRAINT DF_tblOrdenesSofasa_CreatedAt DEFAULT (SYSDATETIMEOFFSET()),
        UpdatedAt           datetimeoffset(3) NOT NULL CONSTRAINT DF_tblOrdenesSofasa_UpdatedAt DEFAULT (SYSDATETIMEOFFSET()),
        CONSTRAINT UQ_tblOrdenesSofasa_SourceId UNIQUE (SourceId),
        CONSTRAINT FK_tblOrdenesSofasa_Vehiculo FOREIGN KEY (VehiculoId) REFERENCES dbo.tblVehiculos (Id) ON DELETE SET NULL
    );
    CREATE INDEX IX_tblOrdenesSofasa_Vin ON dbo.tblOrdenesSofasa (Vin);
    CREATE INDEX IX_tblOrdenesSofasa_DatetimeEvent ON dbo.tblOrdenesSofasa (DatetimeEvent DESC);
    CREATE INDEX IX_tblOrdenesSofasa_Vehiculo ON dbo.tblOrdenesSofasa (VehiculoId);
END
GO

/* -----------------------------------------------------------------------------
   tblAuditLog (registro append-only de toda accion relevante).
   UsuarioId nulo = accion del sistema (por ejemplo, ingesta del ETL).
   ----------------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.tblAuditLog', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblAuditLog
    (
        Id         uniqueidentifier NOT NULL CONSTRAINT PK_tblAuditLog PRIMARY KEY,
        UsuarioId  uniqueidentifier NULL,
        Accion     nvarchar(100)    NOT NULL,
        Entidad    nvarchar(100)    NULL,
        EntidadId  uniqueidentifier NULL,
        CreatedAt  datetimeoffset(3) NOT NULL CONSTRAINT DF_tblAuditLog_CreatedAt DEFAULT (SYSDATETIMEOFFSET()),
        UpdatedAt  datetimeoffset(3) NOT NULL CONSTRAINT DF_tblAuditLog_UpdatedAt DEFAULT (SYSDATETIMEOFFSET()),
        CONSTRAINT FK_tblAuditLog_Usuario FOREIGN KEY (UsuarioId) REFERENCES dbo.tblUsuarios (Id)
    );
    CREATE INDEX IX_tblAuditLog_Usuario ON dbo.tblAuditLog (UsuarioId);
    CREATE INDEX IX_tblAuditLog_CreatedAt ON dbo.tblAuditLog (CreatedAt DESC);
END
GO

/* -----------------------------------------------------------------------------
   Catalogo: los 7 puntos de custodia (idempotente)
   ----------------------------------------------------------------------------- */
INSERT INTO dbo.tblPuntosCustodia (Id, Nombre)
SELECT NEWID(), v.Nombre
FROM (VALUES ('cargue'), ('punto_origen'), ('certificadora'), ('punto_destino'),
             ('transporte'), ('descargue'), ('concesionario')) AS v (Nombre)
WHERE NOT EXISTS (SELECT 1 FROM dbo.tblPuntosCustodia p WHERE p.Nombre = v.Nombre);
GO

/* -----------------------------------------------------------------------------
   Vista: vehiculos movilizados.
   Un vehiculo esta movilizado cuando TODAS sus etapas (y al menos una) tienen su
   informe cerrado. Los VINs recien cargados, con la ruta creada pero sin informes,
   NO cuentan: quedan como "pendientes por movilizar".
   Es la unica definicion de "movilizado" usada por listados, metricas y patrones.
   ----------------------------------------------------------------------------- */
CREATE OR ALTER VIEW dbo.vwVehiculosMovilizados
AS
SELECT x.VehiculoId
FROM (
    SELECT e.VehiculoId,
           CASE WHEN EXISTS (SELECT 1 FROM dbo.tblInspecciones i
                              WHERE i.EtapaId = e.Id AND i.Estado = 'cerrada')
                THEN 1 ELSE 0 END AS Cerrada
    FROM dbo.tblEtapas e
) AS x
GROUP BY x.VehiculoId
HAVING COUNT(*) > 0 AND COUNT(*) = SUM(x.Cerrada);
GO

/* -----------------------------------------------------------------------------
   Inmutabilidad a nivel de base de datos (ultima defensa; la aplicacion ya la
   aplica en el dominio):
   - una inspeccion cerrada no puede modificarse,
   - no se pueden agregar hallazgos ni evidencias a una inspeccion cerrada.
   La migracion de datos historicos deshabilita estos triggers durante la carga.
   ----------------------------------------------------------------------------- */
CREATE OR ALTER TRIGGER dbo.TR_tblInspecciones_Inmutable
ON dbo.tblInspecciones
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM deleted WHERE Estado = 'cerrada')
        THROW 51000, 'La inspeccion esta cerrada y es inmutable; no admite cambios', 1;
END
GO

CREATE OR ALTER TRIGGER dbo.TR_tblHallazgos_NoEnCerrada
ON dbo.tblHallazgos
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted i
               JOIN dbo.tblInspecciones n ON n.Id = i.InspeccionId
               WHERE n.Estado = 'cerrada')
        THROW 51001, 'La inspeccion esta cerrada y es inmutable; no admite hallazgos nuevos', 1;
END
GO

CREATE OR ALTER TRIGGER dbo.TR_tblEvidencias_NoEnCerrada
ON dbo.tblEvidencias
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted i
               JOIN dbo.tblInspecciones n ON n.Id = i.InspeccionId
               WHERE n.Estado = 'cerrada')
        THROW 51002, 'La inspeccion esta cerrada y es inmutable; no admite evidencias nuevas', 1;
END
GO

PRINT 'IA-AUTOS: esquema aplicado correctamente.';
GO
