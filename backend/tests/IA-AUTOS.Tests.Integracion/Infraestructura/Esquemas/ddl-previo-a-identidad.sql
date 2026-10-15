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

-- Cedula: documento de la persona (el conductor se identifica por ella en la carga de viajes por Excel). Unica entre quienes la tienen.
IF COL_LENGTH(N'dbo.tblUsuarios', N'Cedula') IS NULL
    ALTER TABLE dbo.tblUsuarios ADD Cedula varchar(20) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblUsuarios_Cedula' AND object_id = OBJECT_ID(N'dbo.tblUsuarios'))
    CREATE UNIQUE INDEX UX_tblUsuarios_Cedula ON dbo.tblUsuarios (Cedula) WHERE Cedula IS NOT NULL;
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
   Catalogos maestros (administrables desde el portal; nunca se borran valores en uso, se inactivan).
   Todos comparten: Codigo (clave estable en minusculas que usa la API y no cambia), Nombre (etiqueta),
   Orden (posicion al mostrarlos) y Activo (un valor inactivo no se ofrece pero sigue siendo valido en lo ya guardado).
   ----------------------------------------------------------------------------- */

-- Etapas del proceso (cargue, transporte, descargue). Es el "proceso" de cada etapa de un vehiculo.
IF OBJECT_ID(N'dbo.tblEtapasCatalogo', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblEtapasCatalogo
    (
        Id         uniqueidentifier  NOT NULL CONSTRAINT PK_tblEtapasCatalogo PRIMARY KEY,
        Codigo     varchar(30)       NOT NULL,
        Nombre     nvarchar(100)     NOT NULL,
        Orden      int               NOT NULL CONSTRAINT DF_tblEtapasCatalogo_Orden DEFAULT (0),
        Activo     bit               NOT NULL CONSTRAINT DF_tblEtapasCatalogo_Activo DEFAULT (1),
        CreatedAt  datetimeoffset(3) NOT NULL CONSTRAINT DF_tblEtapasCatalogo_CreatedAt DEFAULT (SYSDATETIMEOFFSET()),
        UpdatedAt  datetimeoffset(3) NOT NULL CONSTRAINT DF_tblEtapasCatalogo_UpdatedAt DEFAULT (SYSDATETIMEOFFSET()),
        CONSTRAINT UQ_tblEtapasCatalogo_Codigo UNIQUE (Codigo)
    );
END
GO

-- Puntos de custodia = lugares fisicos donde se hace una etapa (CEDI 1, Origen, Certificadora, Destino, CEDI 2, Concesionario).
IF OBJECT_ID(N'dbo.tblPuntosCustodia', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblPuntosCustodia
    (
        Id         uniqueidentifier  NOT NULL CONSTRAINT PK_tblPuntosCustodia PRIMARY KEY,
        Codigo     varchar(30)       NOT NULL,
        Nombre     nvarchar(100)     NOT NULL,
        Orden      int               NOT NULL CONSTRAINT DF_tblPuntosCustodia_Orden DEFAULT (0),
        Activo     bit               NOT NULL CONSTRAINT DF_tblPuntosCustodia_Activo DEFAULT (1),
        CreatedAt  datetimeoffset(3) NOT NULL CONSTRAINT DF_tblPuntosCustodia_CreatedAt DEFAULT (SYSDATETIMEOFFSET()),
        UpdatedAt  datetimeoffset(3) NOT NULL CONSTRAINT DF_tblPuntosCustodia_UpdatedAt DEFAULT (SYSDATETIMEOFFSET()),
        CONSTRAINT UQ_tblPuntosCustodia_Codigo UNIQUE (Codigo)
    );
END
GO

IF OBJECT_ID(N'dbo.tblTiposUnidadTransportadora', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblTiposUnidadTransportadora
    (
        Id         uniqueidentifier  NOT NULL CONSTRAINT PK_tblTiposUnidadTransportadora PRIMARY KEY,
        Codigo     varchar(30)       NOT NULL,
        Nombre     nvarchar(100)     NOT NULL,
        Orden      int               NOT NULL CONSTRAINT DF_tblTiposUnidadTransportadora_Orden DEFAULT (0),
        Activo     bit               NOT NULL CONSTRAINT DF_tblTiposUnidadTransportadora_Activo DEFAULT (1),
        CreatedAt  datetimeoffset(3) NOT NULL CONSTRAINT DF_tblTiposUnidadTransportadora_CreatedAt DEFAULT (SYSDATETIMEOFFSET()),
        UpdatedAt  datetimeoffset(3) NOT NULL CONSTRAINT DF_tblTiposUnidadTransportadora_UpdatedAt DEFAULT (SYSDATETIMEOFFSET()),
        CONSTRAINT UQ_tblTiposUnidadTransportadora_Codigo UNIQUE (Codigo)
    );
END
GO

-- Clases y tamanos de dano: tblHallazgos guarda el Codigo y lo referencia con una FK (reemplaza los CHECK de antes).
IF OBJECT_ID(N'dbo.tblClasesDano', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblClasesDano
    (
        Id         uniqueidentifier  NOT NULL CONSTRAINT PK_tblClasesDano PRIMARY KEY,
        Codigo     varchar(30)       NOT NULL,
        Nombre     nvarchar(100)     NOT NULL,
        Orden      int               NOT NULL CONSTRAINT DF_tblClasesDano_Orden DEFAULT (0),
        Activo     bit               NOT NULL CONSTRAINT DF_tblClasesDano_Activo DEFAULT (1),
        CreatedAt  datetimeoffset(3) NOT NULL CONSTRAINT DF_tblClasesDano_CreatedAt DEFAULT (SYSDATETIMEOFFSET()),
        UpdatedAt  datetimeoffset(3) NOT NULL CONSTRAINT DF_tblClasesDano_UpdatedAt DEFAULT (SYSDATETIMEOFFSET()),
        CONSTRAINT UQ_tblClasesDano_Codigo UNIQUE (Codigo)
    );
END
GO

IF OBJECT_ID(N'dbo.tblTamanosDano', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblTamanosDano
    (
        Id         uniqueidentifier  NOT NULL CONSTRAINT PK_tblTamanosDano PRIMARY KEY,
        Codigo     varchar(30)       NOT NULL,
        Nombre     nvarchar(100)     NOT NULL,
        Orden      int               NOT NULL CONSTRAINT DF_tblTamanosDano_Orden DEFAULT (0),
        Activo     bit               NOT NULL CONSTRAINT DF_tblTamanosDano_Activo DEFAULT (1),
        CreatedAt  datetimeoffset(3) NOT NULL CONSTRAINT DF_tblTamanosDano_CreatedAt DEFAULT (SYSDATETIMEOFFSET()),
        UpdatedAt  datetimeoffset(3) NOT NULL CONSTRAINT DF_tblTamanosDano_UpdatedAt DEFAULT (SYSDATETIMEOFFSET()),
        CONSTRAINT UQ_tblTamanosDano_Codigo UNIQUE (Codigo)
    );
END
GO

-- Niveles de severidad (V1+, V1, V2, V3) con la accion que corresponde a cada uno.
IF OBJECT_ID(N'dbo.tblNivelesSeveridad', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblNivelesSeveridad
    (
        Id         uniqueidentifier  NOT NULL CONSTRAINT PK_tblNivelesSeveridad PRIMARY KEY,
        Codigo     varchar(30)       NOT NULL,
        Nombre     nvarchar(100)     NOT NULL,
        Accion     nvarchar(500)     NULL,
        Orden      int               NOT NULL CONSTRAINT DF_tblNivelesSeveridad_Orden DEFAULT (0),
        Activo     bit               NOT NULL CONSTRAINT DF_tblNivelesSeveridad_Activo DEFAULT (1),
        CreatedAt  datetimeoffset(3) NOT NULL CONSTRAINT DF_tblNivelesSeveridad_CreatedAt DEFAULT (SYSDATETIMEOFFSET()),
        UpdatedAt  datetimeoffset(3) NOT NULL CONSTRAINT DF_tblNivelesSeveridad_UpdatedAt DEFAULT (SYSDATETIMEOFFSET()),
        CONSTRAINT UQ_tblNivelesSeveridad_Codigo UNIQUE (Codigo)
    );
END
GO

-- Vistas de foto: Obligatoria = 1 se toman siempre; Obligatoria = 0 (piezas en el baul, cojineria) solo con faltante o dano.
-- TipoVehiculo NULL = aplica a cualquier tipo de vehiculo.
IF OBJECT_ID(N'dbo.tblVistasFoto', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblVistasFoto
    (
        Id            uniqueidentifier  NOT NULL CONSTRAINT PK_tblVistasFoto PRIMARY KEY,
        Codigo        varchar(30)       NOT NULL,
        Nombre        nvarchar(100)     NOT NULL,
        Obligatoria   bit               NOT NULL CONSTRAINT DF_tblVistasFoto_Obligatoria DEFAULT (1),
        TipoVehiculo  varchar(30)       NULL,
        Orden         int               NOT NULL CONSTRAINT DF_tblVistasFoto_Orden DEFAULT (0),
        Activo        bit               NOT NULL CONSTRAINT DF_tblVistasFoto_Activo DEFAULT (1),
        CreatedAt     datetimeoffset(3) NOT NULL CONSTRAINT DF_tblVistasFoto_CreatedAt DEFAULT (SYSDATETIMEOFFSET()),
        UpdatedAt     datetimeoffset(3) NOT NULL CONSTRAINT DF_tblVistasFoto_UpdatedAt DEFAULT (SYSDATETIMEOFFSET()),
        CONSTRAINT UQ_tblVistasFoto_Codigo UNIQUE (Codigo)
    );
END
GO

-- Cuadrantes: zonas numeradas de la carroceria (puerta delantera, pilar central...), por tipo de vehiculo. Un mismo cuadrante se ve
-- desde varias vistas, por eso donde se dibuja en cada vista va aparte, en tblCuadranteVistas.
IF OBJECT_ID(N'dbo.tblCuadrantes', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblCuadrantes
    (
        Id            uniqueidentifier  NOT NULL CONSTRAINT PK_tblCuadrantes PRIMARY KEY,
        TipoVehiculo  varchar(30)       NULL,
        Numero        int               NOT NULL,
        Nombre        nvarchar(100)     NOT NULL,
        Orden         int               NOT NULL CONSTRAINT DF_tblCuadrantes_Orden DEFAULT (0),
        Activo        bit               NOT NULL CONSTRAINT DF_tblCuadrantes_Activo DEFAULT (1),
        CreatedAt     datetimeoffset(3) NOT NULL CONSTRAINT DF_tblCuadrantes_CreatedAt DEFAULT (SYSDATETIMEOFFSET()),
        UpdatedAt     datetimeoffset(3) NOT NULL CONSTRAINT DF_tblCuadrantes_UpdatedAt DEFAULT (SYSDATETIMEOFFSET()),
        CONSTRAINT UQ_tblCuadrantes_Tipo_Numero UNIQUE (TipoVehiculo, Numero),
        CONSTRAINT CK_tblCuadrantes_Numero CHECK (Numero >= 1)
    );
END
GO

-- Poligono de un cuadrante en una vista (las vistas adicionales no tienen). Geometria = poligono normalizado en JSON
-- ({"puntos":[[x,y],...]} con x e y entre 0 y 1 sobre la imagen de la vista) para que la app y el portal dibujen lo mismo.
-- Al borrar el cuadrante se borran sus poligonos; una vista con poligonos no se puede borrar.
IF OBJECT_ID(N'dbo.tblCuadranteVistas', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblCuadranteVistas
    (
        Id            uniqueidentifier  NOT NULL CONSTRAINT PK_tblCuadranteVistas PRIMARY KEY,
        CuadranteId   uniqueidentifier  NOT NULL,
        VistaFotoId   uniqueidentifier  NOT NULL,
        Geometria     nvarchar(max)     NOT NULL,
        CreatedAt     datetimeoffset(3) NOT NULL CONSTRAINT DF_tblCuadranteVistas_CreatedAt DEFAULT (SYSDATETIMEOFFSET()),
        UpdatedAt     datetimeoffset(3) NOT NULL CONSTRAINT DF_tblCuadranteVistas_UpdatedAt DEFAULT (SYSDATETIMEOFFSET()),
        CONSTRAINT FK_tblCuadranteVistas_Cuadrante FOREIGN KEY (CuadranteId) REFERENCES dbo.tblCuadrantes (Id) ON DELETE CASCADE,
        CONSTRAINT FK_tblCuadranteVistas_Vista FOREIGN KEY (VistaFotoId) REFERENCES dbo.tblVistasFoto (Id),
        CONSTRAINT UQ_tblCuadranteVistas_Cuadrante_Vista UNIQUE (CuadranteId, VistaFotoId),
        CONSTRAINT CK_tblCuadranteVistas_Geometria CHECK (ISJSON(Geometria) = 1)
    );
    CREATE INDEX IX_tblCuadranteVistas_Vista ON dbo.tblCuadranteVistas (VistaFotoId);
END
GO

/* -----------------------------------------------------------------------------
   tblViajes: el viaje es el eje del modelo. Una unidad transportadora (tipo, placa, trailer) lleva uno o varios VIN con un
   conductor (y, si hay, un inspector) entre un origen y un destino. Cada etapa e inspeccion cuelga de un viaje.
   - CodigoMtrack: codigo del viaje en MTrack (se digita o llega en el Excel; aun no hay integracion). Unico entre los que lo tienen.
   - Estado: programado -> en_curso -> finalizado, o cancelado (un viaje cancelado deja de ser visible para inspectores y conductores).
   - Los viajes que vienen de las asignaciones e inspecciones anteriores a esta tabla pueden no tener tipo de unidad, placa ni codigo.
   ----------------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.tblViajes', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblViajes
    (
        Id             uniqueidentifier  NOT NULL CONSTRAINT PK_tblViajes PRIMARY KEY,
        CodigoMtrack   varchar(50)       NULL,
        TipoUnidadId   uniqueidentifier  NULL,
        Placa          nvarchar(50)      NULL,
        Trailer        nvarchar(50)      NULL,
        ConductorId    uniqueidentifier  NULL,
        InspectorId    uniqueidentifier  NULL,
        OrigenId       uniqueidentifier  NULL,
        DestinoId      uniqueidentifier  NULL,
        Estado         varchar(20)       NOT NULL CONSTRAINT DF_tblViajes_Estado DEFAULT ('programado'),
        CreadoPor      uniqueidentifier  NULL,
        CreatedAt      datetimeoffset(3) NOT NULL CONSTRAINT DF_tblViajes_CreatedAt DEFAULT (SYSDATETIMEOFFSET()),
        UpdatedAt      datetimeoffset(3) NOT NULL CONSTRAINT DF_tblViajes_UpdatedAt DEFAULT (SYSDATETIMEOFFSET()),
        CONSTRAINT FK_tblViajes_TipoUnidad FOREIGN KEY (TipoUnidadId) REFERENCES dbo.tblTiposUnidadTransportadora (Id),
        CONSTRAINT FK_tblViajes_Conductor FOREIGN KEY (ConductorId) REFERENCES dbo.tblUsuarios (Id),
        CONSTRAINT FK_tblViajes_Inspector FOREIGN KEY (InspectorId) REFERENCES dbo.tblUsuarios (Id),
        CONSTRAINT FK_tblViajes_Origen FOREIGN KEY (OrigenId) REFERENCES dbo.tblPuntosCustodia (Id),
        CONSTRAINT FK_tblViajes_Destino FOREIGN KEY (DestinoId) REFERENCES dbo.tblPuntosCustodia (Id),
        CONSTRAINT FK_tblViajes_CreadoPor FOREIGN KEY (CreadoPor) REFERENCES dbo.tblUsuarios (Id),
        CONSTRAINT CK_tblViajes_Estado CHECK (Estado IN ('programado', 'en_curso', 'finalizado', 'cancelado'))
    );
    CREATE UNIQUE INDEX UX_tblViajes_CodigoMtrack ON dbo.tblViajes (CodigoMtrack) WHERE CodigoMtrack IS NOT NULL;
    CREATE INDEX IX_tblViajes_Conductor ON dbo.tblViajes (ConductorId);
    CREATE INDEX IX_tblViajes_Inspector ON dbo.tblViajes (InspectorId);
    CREATE INDEX IX_tblViajes_Estado ON dbo.tblViajes (Estado, CreatedAt DESC);
    CREATE INDEX IX_tblViajes_TipoUnidad ON dbo.tblViajes (TipoUnidadId);
    CREATE INDEX IX_tblViajes_Origen ON dbo.tblViajes (OrigenId);
    CREATE INDEX IX_tblViajes_Destino ON dbo.tblViajes (DestinoId);
    CREATE INDEX IX_tblViajes_CreadoPor ON dbo.tblViajes (CreadoPor);
END
GO

/* -----------------------------------------------------------------------------
   tblViajeVehiculos: los VIN de un viaje. Un VIN puede estar en varios viajes a lo largo del tiempo (la aplicacion no admite
   dos viajes activos con el mismo VIN).
   ----------------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.tblViajeVehiculos', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblViajeVehiculos
    (
        Id          uniqueidentifier  NOT NULL CONSTRAINT PK_tblViajeVehiculos PRIMARY KEY,
        ViajeId     uniqueidentifier  NOT NULL,
        VehiculoId  uniqueidentifier  NOT NULL,
        CreatedAt   datetimeoffset(3) NOT NULL CONSTRAINT DF_tblViajeVehiculos_CreatedAt DEFAULT (SYSDATETIMEOFFSET()),
        UpdatedAt   datetimeoffset(3) NOT NULL CONSTRAINT DF_tblViajeVehiculos_UpdatedAt DEFAULT (SYSDATETIMEOFFSET()),
        CONSTRAINT FK_tblViajeVehiculos_Viaje FOREIGN KEY (ViajeId) REFERENCES dbo.tblViajes (Id) ON DELETE CASCADE,
        CONSTRAINT FK_tblViajeVehiculos_Vehiculo FOREIGN KEY (VehiculoId) REFERENCES dbo.tblVehiculos (Id) ON DELETE CASCADE,
        CONSTRAINT UQ_tblViajeVehiculos_Viaje_Vehiculo UNIQUE (ViajeId, VehiculoId)
    );
    CREATE INDEX IX_tblViajeVehiculos_Vehiculo ON dbo.tblViajeVehiculos (VehiculoId);
END
GO

/* -----------------------------------------------------------------------------
   tblEtapas (tramo de un VIN dentro de un viaje: etapa del catalogo y el lugar donde se hace)
   - Al agregar un VIN a un viaje se crean sus etapas: Cargue en el origen, Transporte y Descargue en el destino.
   - ConPersonalApoyo (solo en la etapa de Cargue): con personal de apoyo la inspeccion de salida la hace un inspector; sin el,
     la hace el conductor.
   ----------------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.tblEtapas', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblEtapas
    (
        Id                  uniqueidentifier NOT NULL CONSTRAINT PK_tblEtapas PRIMARY KEY,
        VehiculoId          uniqueidentifier NOT NULL,
        EtapaCatalogoId     uniqueidentifier NOT NULL,
        PuntoCustodiaId     uniqueidentifier NULL,
        ViajeId             uniqueidentifier NOT NULL,
        Orden               int              NOT NULL,
        RequiereInspeccion  bit              NOT NULL CONSTRAINT DF_tblEtapas_RequiereInspeccion DEFAULT (1),
        ConPersonalApoyo    bit              NOT NULL CONSTRAINT DF_tblEtapas_ConPersonalApoyo DEFAULT (0),
        CreatedAt           datetimeoffset(3) NOT NULL CONSTRAINT DF_tblEtapas_CreatedAt DEFAULT (SYSDATETIMEOFFSET()),
        UpdatedAt           datetimeoffset(3) NOT NULL CONSTRAINT DF_tblEtapas_UpdatedAt DEFAULT (SYSDATETIMEOFFSET()),
        CONSTRAINT FK_tblEtapas_Vehiculo FOREIGN KEY (VehiculoId) REFERENCES dbo.tblVehiculos (Id) ON DELETE CASCADE,
        CONSTRAINT FK_tblEtapas_EtapaCatalogo FOREIGN KEY (EtapaCatalogoId) REFERENCES dbo.tblEtapasCatalogo (Id),
        CONSTRAINT FK_tblEtapas_PuntoCustodia FOREIGN KEY (PuntoCustodiaId) REFERENCES dbo.tblPuntosCustodia (Id),
        CONSTRAINT FK_tblEtapas_Viaje FOREIGN KEY (ViajeId) REFERENCES dbo.tblViajes (Id),
        CONSTRAINT UQ_tblEtapas_Viaje_Vehiculo_Orden UNIQUE (ViajeId, VehiculoId, Orden),
        CONSTRAINT CK_tblEtapas_Orden CHECK (Orden >= 1)
    );
    CREATE INDEX IX_tblEtapas_EtapaCatalogo ON dbo.tblEtapas (EtapaCatalogoId);
    CREATE INDEX IX_tblEtapas_PuntoCustodia ON dbo.tblEtapas (PuntoCustodiaId);
    CREATE INDEX IX_tblEtapas_Vehiculo ON dbo.tblEtapas (VehiculoId);
END
GO

/* -----------------------------------------------------------------------------
   tblInspecciones (un informe por etapa; al cerrarse es inmutable). ViajeId es el viaje de la etapa: la inspeccion toma de el la placa,
   el trailer y el conductor. Las columnas ConductorNombre, Placa y Trailer solo conservan lo capturado antes de existir los viajes.
   Latitud y longitud en float (precision GPS completa).
   ----------------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.tblInspecciones', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblInspecciones
    (
        Id               uniqueidentifier NOT NULL CONSTRAINT PK_tblInspecciones PRIMARY KEY,
        EtapaId          uniqueidentifier NOT NULL,
        ViajeId          uniqueidentifier NOT NULL,
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
        CONSTRAINT FK_tblInspecciones_Viaje FOREIGN KEY (ViajeId) REFERENCES dbo.tblViajes (Id),
        -- A lo sumo un informe por etapa y momento (los valores check_in/check_out se conservan por historico).
        CONSTRAINT UQ_tblInspecciones_Etapa_Momento UNIQUE (EtapaId, Momento),
        CONSTRAINT CK_tblInspecciones_Momento CHECK (Momento IN ('check_in', 'check_out', 'informe')),
        CONSTRAINT CK_tblInspecciones_Estado CHECK (Estado IN ('borrador', 'cerrada')),
        CONSTRAINT CK_tblInspecciones_Lat CHECK (Lat IS NULL OR (Lat BETWEEN -90 AND 90)),
        CONSTRAINT CK_tblInspecciones_Lng CHECK (Lng IS NULL OR (Lng BETWEEN -180 AND 180))
    );
    CREATE INDEX IX_tblInspecciones_Usuario ON dbo.tblInspecciones (UsuarioId);
    CREATE INDEX IX_tblInspecciones_Viaje ON dbo.tblInspecciones (ViajeId);
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
        CONSTRAINT FK_tblHallazgos_ClaseDano FOREIGN KEY (ClaseDano) REFERENCES dbo.tblClasesDano (Codigo),
        CONSTRAINT FK_tblHallazgos_TamanoDano FOREIGN KEY (TamanoDano) REFERENCES dbo.tblTamanosDano (Codigo),
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
        CodigoMtrack        varchar(50)      NULL,
        ViajeId             uniqueidentifier NULL,
        SyncedAt            datetimeoffset(3) NOT NULL CONSTRAINT DF_tblOrdenesSofasa_SyncedAt DEFAULT (SYSDATETIMEOFFSET()),
        CreatedAt           datetimeoffset(3) NOT NULL CONSTRAINT DF_tblOrdenesSofasa_CreatedAt DEFAULT (SYSDATETIMEOFFSET()),
        UpdatedAt           datetimeoffset(3) NOT NULL CONSTRAINT DF_tblOrdenesSofasa_UpdatedAt DEFAULT (SYSDATETIMEOFFSET()),
        CONSTRAINT UQ_tblOrdenesSofasa_SourceId UNIQUE (SourceId),
        CONSTRAINT FK_tblOrdenesSofasa_Vehiculo FOREIGN KEY (VehiculoId) REFERENCES dbo.tblVehiculos (Id) ON DELETE SET NULL,
        CONSTRAINT FK_tblOrdenesSofasa_Viaje FOREIGN KEY (ViajeId) REFERENCES dbo.tblViajes (Id) ON DELETE SET NULL
    );
    CREATE INDEX IX_tblOrdenesSofasa_Vin ON dbo.tblOrdenesSofasa (Vin);
    CREATE INDEX IX_tblOrdenesSofasa_DatetimeEvent ON dbo.tblOrdenesSofasa (DatetimeEvent DESC);
    CREATE INDEX IX_tblOrdenesSofasa_Vehiculo ON dbo.tblOrdenesSofasa (VehiculoId);
    CREATE INDEX IX_tblOrdenesSofasa_Viaje ON dbo.tblOrdenesSofasa (ViajeId);
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
   Catalogos iniciales (idempotentes: solo agregan lo que falta, por Codigo).
   Los acentos se escriben con NCHAR para que el archivo siga siendo ASCII: 225=a, 233=e, 237=i, 243=o, 250=u, 241=enie.
   Las acciones de los niveles de severidad y los cuadrantes no tienen semilla: se cargan o se editan desde el portal.
   ----------------------------------------------------------------------------- */
INSERT INTO dbo.tblEtapasCatalogo (Id, Codigo, Nombre, Orden)
SELECT NEWID(), v.Codigo, v.Nombre, v.Orden
FROM (VALUES
    ('cargue',     N'Cargue',     1),
    ('transporte', N'Transporte', 2),
    ('descargue',  N'Descargue',  3)) AS v (Codigo, Nombre, Orden)
WHERE NOT EXISTS (SELECT 1 FROM dbo.tblEtapasCatalogo c WHERE c.Codigo = v.Codigo);
GO

INSERT INTO dbo.tblPuntosCustodia (Id, Codigo, Nombre, Orden)
SELECT NEWID(), v.Codigo, v.Nombre, v.Orden
FROM (VALUES
    ('cedi_1',        N'CEDI 1',        1),
    ('origen',        N'Origen',        2),
    ('certificadora', N'Certificadora', 3),
    ('destino',       N'Destino',       4),
    ('cedi_2',        N'CEDI 2',        5),
    ('concesionario', N'Concesionario', 6)) AS v (Codigo, Nombre, Orden)
WHERE NOT EXISTS (SELECT 1 FROM dbo.tblPuntosCustodia c WHERE c.Codigo = v.Codigo);
GO

INSERT INTO dbo.tblTiposUnidadTransportadora (Id, Codigo, Nombre, Orden)
SELECT NEWID(), v.Codigo, v.Nombre, v.Orden
FROM (VALUES
    ('ninera',        N'Ni' + NCHAR(241) + N'era',                      1),
    ('nodriza',       N'Nodriza',                                       2),
    ('quintinaria',   N'Quintinaria',                                   3),
    ('planchon_grua', N'Planch' + NCHAR(243) + N'n gr' + NCHAR(250) + N'a', 4)) AS v (Codigo, Nombre, Orden)
WHERE NOT EXISTS (SELECT 1 FROM dbo.tblTiposUnidadTransportadora c WHERE c.Codigo = v.Codigo);
GO

INSERT INTO dbo.tblClasesDano (Id, Codigo, Nombre, Orden)
SELECT NEWID(), v.Codigo, v.Nombre, v.Orden
FROM (VALUES
    ('deformacion', N'Deformaci' + NCHAR(243) + N'n', 1),
    ('rotura',      N'Rotura',                         2),
    ('cortado',     N'Cortado',                        3),
    ('perforacion', N'Perforaci' + NCHAR(243) + N'n', 4),
    ('golpe',       N'Golpe',                          5),
    ('mancha',      N'Mancha',                         6),
    ('raya_pintar', N'Raya para pintar',               7),
    ('desconchado', N'Desconchado',                    8),
    ('particulas',  N'Part' + NCHAR(237) + N'culas',  9),
    ('faltante',    N'Faltante',                       10)) AS v (Codigo, Nombre, Orden)
WHERE NOT EXISTS (SELECT 1 FROM dbo.tblClasesDano c WHERE c.Codigo = v.Codigo);
GO

INSERT INTO dbo.tblTamanosDano (Id, Codigo, Nombre, Orden)
SELECT NEWID(), v.Codigo, v.Nombre, v.Orden
FROM (VALUES
    ('1_5cm',             N'1 a 5 cm',                                        1),
    ('5_10cm',            N'5 a 10 cm',                                       2),
    ('10_20cm',           N'10 a 20 cm',                                      3),
    ('mas_20cm',          N'M' + NCHAR(225) + N's de 20 cm',                  4),
    ('requiere_reemplazo', N'Requiere reemplazo',                             5),
    ('mas_10_particulas', N'M' + NCHAR(225) + N's de 10 part' + NCHAR(237) + N'culas', 6)) AS v (Codigo, Nombre, Orden)
WHERE NOT EXISTS (SELECT 1 FROM dbo.tblTamanosDano c WHERE c.Codigo = v.Codigo);
GO

INSERT INTO dbo.tblNivelesSeveridad (Id, Codigo, Nombre, Orden)
SELECT NEWID(), v.Codigo, v.Nombre, v.Orden
FROM (VALUES
    ('v1_plus', N'V1+', 1),
    ('v1',      N'V1',  2),
    ('v2',      N'V2',  3),
    ('v3',      N'V3',  4)) AS v (Codigo, Nombre, Orden)
WHERE NOT EXISTS (SELECT 1 FROM dbo.tblNivelesSeveridad c WHERE c.Codigo = v.Codigo);
GO

INSERT INTO dbo.tblVistasFoto (Id, Codigo, Nombre, Obligatoria, Orden)
SELECT NEWID(), v.Codigo, v.Nombre, v.Obligatoria, v.Orden
FROM (VALUES
    ('frontal',                    N'Frontal',                       1, 1),
    ('trasera',                    N'Trasera',                       1, 2),
    ('lateral_derecha',            N'Lateral derecha',               1, 3),
    ('lateral_izquierda',          N'Lateral izquierda',             1, 4),
    ('diagonal_trasera_izquierda', N'Diagonal trasera izquierda',    1, 5),
    ('diagonal_delantera_derecha', N'Diagonal delantera derecha',    1, 6),
    ('superior',                   N'Superior',                      1, 7),
    ('piezas_baul',                N'Piezas en el ba' + NCHAR(250) + N'l', 0, 8),
    ('cojineria',                  N'Cojiner' + NCHAR(237) + N'a',   0, 9)) AS v (Codigo, Nombre, Obligatoria, Orden)
WHERE NOT EXISTS (SELECT 1 FROM dbo.tblVistasFoto c WHERE c.Codigo = v.Codigo);
GO

/* -----------------------------------------------------------------------------
   Vista: viajes completos (reemplaza a vwVehiculosMovilizados).
   Una fila por VIN y viaje en el que el VIN ya completo su ruta: TODAS sus etapas del viaje (y al menos una) tienen su informe
   cerrado. Un VIN recien asignado a un viaje, sin informes, NO cuenta: queda como "pendiente por movilizar".
   Un vehiculo esta movilizado si tiene al menos una fila; un viaje esta completo cuando todos sus VIN tienen fila.
   Es la unica definicion de "movilizado" usada por listados, metricas y patrones.
   ----------------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.vwVehiculosMovilizados', N'V') IS NOT NULL
    DROP VIEW dbo.vwVehiculosMovilizados;
GO

CREATE OR ALTER VIEW dbo.vwViajesCompletos
AS
SELECT x.ViajeId, x.VehiculoId
FROM (
    SELECT e.ViajeId, e.VehiculoId,
           CASE WHEN EXISTS (SELECT 1 FROM dbo.tblInspecciones i
                              WHERE i.EtapaId = e.Id AND i.Estado = 'cerrada')
                THEN 1 ELSE 0 END AS Cerrada
    FROM dbo.tblEtapas e
) AS x
GROUP BY x.ViajeId, x.VehiculoId
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
