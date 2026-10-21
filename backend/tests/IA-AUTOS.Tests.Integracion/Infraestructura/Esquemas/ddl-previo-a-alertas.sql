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
   tblClientes: el cliente (empresa) al que pertenecen usuarios y vehiculos. Es un catalogo administrable como los demas
   (Codigo estable, Nombre, Orden y Activo); el primero activo por Orden es el cliente por defecto.
   ----------------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.tblClientes', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblClientes
    (
        Id         uniqueidentifier  NOT NULL CONSTRAINT PK_tblClientes PRIMARY KEY,
        Codigo     varchar(30)       NOT NULL,
        Nombre     nvarchar(100)     NOT NULL,
        Orden      int               NOT NULL CONSTRAINT DF_tblClientes_Orden DEFAULT (0),
        Activo     bit               NOT NULL CONSTRAINT DF_tblClientes_Activo DEFAULT (1),
        CreatedAt  datetimeoffset(3) NOT NULL CONSTRAINT DF_tblClientes_CreatedAt DEFAULT (SYSDATETIMEOFFSET()),
        UpdatedAt  datetimeoffset(3) NOT NULL CONSTRAINT DF_tblClientes_UpdatedAt DEFAULT (SYSDATETIMEOFFSET()),
        CONSTRAINT UQ_tblClientes_Codigo UNIQUE (Codigo)
    );
END
GO

/* -----------------------------------------------------------------------------
   tblUsuarios
   Identidad: un usuario entra con correo y contrasena (TipoCredencial 'password') o con cedula y PIN (TipoCredencial 'pin',
   para conductores e inspectores, que pueden no tener correo). Ver las columnas agregadas mas abajo.
   ----------------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.tblUsuarios', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblUsuarios
    (
        Id                uniqueidentifier NOT NULL CONSTRAINT PK_tblUsuarios PRIMARY KEY,
        Nombre            nvarchar(200)    NOT NULL,
        Email             nvarchar(320)    NULL,
        Rol               varchar(30)      NOT NULL,
        SubtipoInspector  varchar(30)      NULL,
        Activo            bit              NOT NULL CONSTRAINT DF_tblUsuarios_Activo DEFAULT (1),
        PasswordHash      nvarchar(400)    NULL,
        CreatedAt         datetimeoffset(3) NOT NULL CONSTRAINT DF_tblUsuarios_CreatedAt DEFAULT (SYSDATETIMEOFFSET()),
        UpdatedAt         datetimeoffset(3) NOT NULL CONSTRAINT DF_tblUsuarios_UpdatedAt DEFAULT (SYSDATETIMEOFFSET()),
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

-- Credencial y bloqueo del usuario:
--   PinHash: hash del PIN (mismo algoritmo que la contrasena); nunca se guarda en claro.
--   TipoCredencial: 'password' (correo y contrasena) o 'pin' (cedula y PIN).
--   DebeCambiarPin: el PIN lo asigno o reinicio un administrador, asi que el usuario debe cambiarlo antes de continuar.
--   IntentosFallidos / BloqueadoHasta: PIN errado seguidos y hasta cuando esta bloqueada la cuenta.
--   UltimoAccesoAt: ultimo inicio de sesion exitoso.
--   ClienteId: cliente al que pertenece (nulo en el super administrador, que ve todos los clientes).
IF COL_LENGTH(N'dbo.tblUsuarios', N'TipoCredencial') IS NULL
    ALTER TABLE dbo.tblUsuarios ADD
        PinHash          nvarchar(400)     NULL,
        TipoCredencial   varchar(20)       NOT NULL CONSTRAINT DF_tblUsuarios_TipoCredencial DEFAULT ('password'),
        DebeCambiarPin   bit               NOT NULL CONSTRAINT DF_tblUsuarios_DebeCambiarPin DEFAULT (0),
        IntentosFallidos int               NOT NULL CONSTRAINT DF_tblUsuarios_IntentosFallidos DEFAULT (0),
        BloqueadoHasta   datetimeoffset(3) NULL,
        UltimoAccesoAt   datetimeoffset(3) NULL,
        ClienteId        uniqueidentifier  NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_tblUsuarios_TipoCredencial')
    ALTER TABLE dbo.tblUsuarios ADD CONSTRAINT CK_tblUsuarios_TipoCredencial CHECK (TipoCredencial IN ('password', 'pin'));
GO

-- Quien entra con contrasena necesita correo; quien entra con PIN necesita cedula.
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_tblUsuarios_Identidad')
    ALTER TABLE dbo.tblUsuarios ADD CONSTRAINT CK_tblUsuarios_Identidad CHECK
        ((TipoCredencial = 'password' AND Email IS NOT NULL) OR (TipoCredencial = 'pin' AND Cedula IS NOT NULL));
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblUsuarios_Cliente')
    ALTER TABLE dbo.tblUsuarios ADD CONSTRAINT FK_tblUsuarios_Cliente FOREIGN KEY (ClienteId) REFERENCES dbo.tblClientes (Id);
GO

-- El correo es unico entre los usuarios que lo tienen (los conductores e inspectores sin correo pueden ser varios).
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_tblUsuarios_Email' AND object_id = OBJECT_ID(N'dbo.tblUsuarios'))
    CREATE UNIQUE INDEX UX_tblUsuarios_Email ON dbo.tblUsuarios (Email) WHERE Email IS NOT NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblUsuarios_Cliente' AND object_id = OBJECT_ID(N'dbo.tblUsuarios'))
    CREATE INDEX IX_tblUsuarios_Cliente ON dbo.tblUsuarios (ClienteId);
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
        ClienteId     uniqueidentifier NOT NULL,
        Activo        bit              NOT NULL CONSTRAINT DF_tblVehiculos_Activo DEFAULT (1),
        CreatedAt     datetimeoffset(3) NOT NULL CONSTRAINT DF_tblVehiculos_CreatedAt DEFAULT (SYSDATETIMEOFFSET()),
        UpdatedAt     datetimeoffset(3) NOT NULL CONSTRAINT DF_tblVehiculos_UpdatedAt DEFAULT (SYSDATETIMEOFFSET()),
        CONSTRAINT UQ_tblVehiculos_Vin UNIQUE (Vin),
        CONSTRAINT FK_tblVehiculos_Cliente FOREIGN KEY (ClienteId) REFERENCES dbo.tblClientes (Id),
        CONSTRAINT CK_tblVehiculos_Operacion CHECK (Operacion IN ('importacion', 'exportacion', 'nacional'))
    );
    CREATE INDEX IX_tblVehiculos_Cliente ON dbo.tblVehiculos (ClienteId);
END
GO

-- Baja logica: un vehiculo dado de baja queda inactivo (no se busca ni se agrega a un viaje) y conserva sus etapas, inspecciones y evidencias.
IF COL_LENGTH(N'dbo.tblVehiculos', N'Activo') IS NULL
    ALTER TABLE dbo.tblVehiculos ADD Activo bit NOT NULL CONSTRAINT DF_tblVehiculos_Activo DEFAULT (1);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblVehiculos_Activo' AND object_id = OBJECT_ID(N'dbo.tblVehiculos'))
    CREATE INDEX IX_tblVehiculos_Activo ON dbo.tblVehiculos (Activo);
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

-- Una zona numerada se identifica por (cuadrante, numero): este indice unico permite que las zonas lo referencien junto con su numero.
IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'UQ_tblCuadrantes_Id_Numero' AND parent_object_id = OBJECT_ID(N'dbo.tblCuadrantes'))
    ALTER TABLE dbo.tblCuadrantes ADD CONSTRAINT UQ_tblCuadrantes_Id_Numero UNIQUE (Id, Numero);
GO

/* -----------------------------------------------------------------------------
   Zona del dano. Un hallazgo se atribuye a una zona del vehiculo, que es UNA de estas dos cosas:
     - una zona numerada: el cuadrante (numero) mas su lado. Los numeros 1 a 25 llevan lado (izquierdo o derecho); del 26 en adelante no.
       Hay una fila por lado + numero, creada automaticamente al crear el cuadrante (TR_tblCuadrantes_CrearZonas);
     - una zona provisional, sin numero ni lado y sin dibujo, que se elige de una lista (frente y cada llanta). Se identifica por su Codigo.
   La zona se guarda por lado + numero, no por el panel donde se toco.
   ----------------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.tblZonasDano', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblZonasDano
    (
        Id            uniqueidentifier  NOT NULL CONSTRAINT PK_tblZonasDano PRIMARY KEY,
        CuadranteId   uniqueidentifier  NULL,
        Numero        int               NULL,
        Lado          varchar(10)       NULL,
        Codigo        varchar(40)       NULL,
        Nombre        nvarchar(100)     NULL,
        Orden         int               NOT NULL CONSTRAINT DF_tblZonasDano_Orden DEFAULT (0),
        Activo        bit               NOT NULL CONSTRAINT DF_tblZonasDano_Activo DEFAULT (1),
        CreatedAt     datetimeoffset(3) NOT NULL CONSTRAINT DF_tblZonasDano_CreatedAt DEFAULT (SYSDATETIMEOFFSET()),
        UpdatedAt     datetimeoffset(3) NOT NULL CONSTRAINT DF_tblZonasDano_UpdatedAt DEFAULT (SYSDATETIMEOFFSET()),
        -- Si el cuadrante se borra o cambia de numero, sus zonas lo siguen (un cuadrante con hallazgos no se puede borrar: ver tblHallazgos).
        CONSTRAINT FK_tblZonasDano_Cuadrante FOREIGN KEY (CuadranteId, Numero) REFERENCES dbo.tblCuadrantes (Id, Numero) ON DELETE CASCADE ON UPDATE CASCADE,
        CONSTRAINT UQ_tblZonasDano_Id_Cuadrante UNIQUE (Id, CuadranteId),
        CONSTRAINT CK_tblZonasDano_Lado CHECK (Lado IS NULL OR Lado IN ('izquierdo', 'derecho')),
        -- Numerada: cuadrante y numero, con lado del 1 al 25 y sin lado del 26 en adelante. Provisional: sin cuadrante, numero ni lado, con codigo y nombre.
        CONSTRAINT CK_tblZonasDano_Forma CHECK (
            (CuadranteId IS NOT NULL AND Numero IS NOT NULL AND Codigo IS NULL
                AND ((Numero BETWEEN 1 AND 25 AND Lado IS NOT NULL) OR (Numero > 25 AND Lado IS NULL)))
            OR (CuadranteId IS NULL AND Numero IS NULL AND Lado IS NULL AND Codigo IS NOT NULL AND Nombre IS NOT NULL))
    );
    -- Una zona numerada es unica por cuadrante y lado; una provisional, por codigo.
    CREATE UNIQUE INDEX UX_tblZonasDano_Cuadrante_Lado ON dbo.tblZonasDano (CuadranteId, Lado) WHERE CuadranteId IS NOT NULL;
    CREATE UNIQUE INDEX UX_tblZonasDano_Codigo ON dbo.tblZonasDano (Codigo) WHERE Codigo IS NOT NULL;
END
GO

/* -----------------------------------------------------------------------------
   Paneles del dibujo de danos: los 5 paneles de la ficha del vehiculo, separados del catalogo de vistas de foto. Cada uno tiene su imagen de
   fondo (ImagenPath, el archivo en el almacen de imagenes; nulo hasta que se carga) y sus regiones tocables.
   ----------------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.tblPanelesDano', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblPanelesDano
    (
        Id            uniqueidentifier  NOT NULL CONSTRAINT PK_tblPanelesDano PRIMARY KEY,
        Codigo        varchar(40)       NOT NULL,
        Nombre        nvarchar(100)     NOT NULL,
        ImagenPath    nvarchar(260)     NULL,
        Orden         int               NOT NULL CONSTRAINT DF_tblPanelesDano_Orden DEFAULT (0),
        Activo        bit               NOT NULL CONSTRAINT DF_tblPanelesDano_Activo DEFAULT (1),
        CreatedAt     datetimeoffset(3) NOT NULL CONSTRAINT DF_tblPanelesDano_CreatedAt DEFAULT (SYSDATETIMEOFFSET()),
        UpdatedAt     datetimeoffset(3) NOT NULL CONSTRAINT DF_tblPanelesDano_UpdatedAt DEFAULT (SYSDATETIMEOFFSET()),
        CONSTRAINT UQ_tblPanelesDano_Codigo UNIQUE (Codigo)
    );
END
GO

/* -----------------------------------------------------------------------------
   Regiones tocables de un panel: el poligono de una zona numerada en un panel (las zonas provisionales no tienen region). Geometria =
   poligono normalizado en JSON ({"puntos":[[x,y],...]} con x e y entre 0 y 1 sobre la imagen del panel). Una zona se dibuja a lo sumo una
   vez por panel. Al borrar la zona (o su cuadrante) se borran sus regiones.
   ----------------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.tblRegionesDano', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblRegionesDano
    (
        Id            uniqueidentifier  NOT NULL CONSTRAINT PK_tblRegionesDano PRIMARY KEY,
        PanelId       uniqueidentifier  NOT NULL,
        ZonaId        uniqueidentifier  NOT NULL,
        CuadranteId   uniqueidentifier  NOT NULL,
        Geometria     nvarchar(max)     NOT NULL,
        CreatedAt     datetimeoffset(3) NOT NULL CONSTRAINT DF_tblRegionesDano_CreatedAt DEFAULT (SYSDATETIMEOFFSET()),
        UpdatedAt     datetimeoffset(3) NOT NULL CONSTRAINT DF_tblRegionesDano_UpdatedAt DEFAULT (SYSDATETIMEOFFSET()),
        CONSTRAINT FK_tblRegionesDano_Panel FOREIGN KEY (PanelId) REFERENCES dbo.tblPanelesDano (Id),
        -- CuadranteId no es nulo: asi la zona de una region es siempre una zona numerada (una provisional no tiene cuadrante).
        CONSTRAINT FK_tblRegionesDano_Zona FOREIGN KEY (ZonaId, CuadranteId) REFERENCES dbo.tblZonasDano (Id, CuadranteId) ON DELETE CASCADE,
        CONSTRAINT UQ_tblRegionesDano_Panel_Zona UNIQUE (PanelId, ZonaId),
        CONSTRAINT CK_tblRegionesDano_Geometria CHECK (ISJSON(Geometria) = 1)
    );
    CREATE INDEX IX_tblRegionesDano_Zona ON dbo.tblRegionesDano (ZonaId);
END
GO

-- Cada cuadrante nuevo trae sus zonas: dos (izquierdo y derecho) del 1 al 25 y una del 26 en adelante.
CREATE OR ALTER TRIGGER dbo.TR_tblCuadrantes_CrearZonas
ON dbo.tblCuadrantes
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.tblZonasDano (Id, CuadranteId, Numero, Lado, Orden)
    SELECT NEWID(), i.Id, i.Numero, l.Lado, i.Numero
    FROM inserted i
    CROSS APPLY (SELECT Lado FROM (VALUES ('izquierdo'), ('derecho')) AS x (Lado) WHERE i.Numero <= 25
                 UNION ALL SELECT NULL WHERE i.Numero > 25) AS l;
END
GO

-- Los 5 paneles de la ficha y las zonas provisionales (la lista que se ofrece para el frente y las llantas). Solo agregan lo que falta.
INSERT INTO dbo.tblPanelesDano (Id, Codigo, Nombre, Orden)
SELECT NEWID(), v.Codigo, v.Nombre, v.Orden
FROM (VALUES ('superior', N'Superior', 1), ('perfil_posterior_izquierdo', N'Perfil posterior izquierdo', 2),
             ('perfil_posterior_derecho', N'Perfil posterior derecho', 3), ('costado_izquierdo', N'Costado izquierdo', 4),
             ('costado_derecho', N'Costado derecho', 5)) AS v (Codigo, Nombre, Orden)
WHERE NOT EXISTS (SELECT 1 FROM dbo.tblPanelesDano p WHERE p.Codigo = v.Codigo);

INSERT INTO dbo.tblZonasDano (Id, Codigo, Nombre, Orden)
SELECT NEWID(), v.Codigo, v.Nombre, v.Orden
FROM (VALUES ('frente', N'Frente', 101), ('llanta_delantera_izquierda', N'Llanta delantera izquierda', 102),
             ('llanta_delantera_derecha', N'Llanta delantera derecha', 103), ('llanta_trasera_izquierda', N'Llanta trasera izquierda', 104),
             ('llanta_trasera_derecha', N'Llanta trasera derecha', 105)) AS v (Codigo, Nombre, Orden)
WHERE NOT EXISTS (SELECT 1 FROM dbo.tblZonasDano z WHERE z.Codigo = v.Codigo);
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
        -- Una unidad con un hallazgo V1+ puede "no salir" del viaje sin bloquearlo: el vinculo no se borra para conservar su trazabilidad.
        EstadoUnidad          varchar(10)       NOT NULL CONSTRAINT DF_tblViajeVehiculos_EstadoUnidad DEFAULT ('activa'),
        NoSaleMotivo          nvarchar(500)     NULL,
        NoSaleHallazgoId      uniqueidentifier  NULL,
        NoSaleMarcadoPor      uniqueidentifier  NULL,
        NoSaleAt              datetimeoffset(3) NULL,
        ReemplazoVehiculoId   uniqueidentifier  NULL,
        CreatedAt   datetimeoffset(3) NOT NULL CONSTRAINT DF_tblViajeVehiculos_CreatedAt DEFAULT (SYSDATETIMEOFFSET()),
        UpdatedAt   datetimeoffset(3) NOT NULL CONSTRAINT DF_tblViajeVehiculos_UpdatedAt DEFAULT (SYSDATETIMEOFFSET()),
        CONSTRAINT FK_tblViajeVehiculos_Viaje FOREIGN KEY (ViajeId) REFERENCES dbo.tblViajes (Id) ON DELETE CASCADE,
        CONSTRAINT FK_tblViajeVehiculos_Vehiculo FOREIGN KEY (VehiculoId) REFERENCES dbo.tblVehiculos (Id),
        CONSTRAINT FK_tblViajeVehiculos_NoSaleMarcadoPor FOREIGN KEY (NoSaleMarcadoPor) REFERENCES dbo.tblUsuarios (Id),
        CONSTRAINT FK_tblViajeVehiculos_Reemplazo FOREIGN KEY (ReemplazoVehiculoId) REFERENCES dbo.tblVehiculos (Id),
        CONSTRAINT UQ_tblViajeVehiculos_Viaje_Vehiculo UNIQUE (ViajeId, VehiculoId),
        CONSTRAINT CK_tblViajeVehiculos_EstadoUnidad CHECK (EstadoUnidad IN ('activa', 'no_sale')),
        -- "No sale" exige su motivo, el hallazgo que lo causa, quien lo marco y cuando; una unidad activa no lleva nada de eso.
        CONSTRAINT CK_tblViajeVehiculos_NoSale CHECK (
            (EstadoUnidad = 'activa' AND NoSaleMotivo IS NULL AND NoSaleHallazgoId IS NULL AND NoSaleMarcadoPor IS NULL AND NoSaleAt IS NULL AND ReemplazoVehiculoId IS NULL)
            OR (EstadoUnidad = 'no_sale' AND NoSaleMotivo IS NOT NULL AND NoSaleHallazgoId IS NOT NULL AND NoSaleMarcadoPor IS NOT NULL AND NoSaleAt IS NOT NULL))
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
        CONSTRAINT FK_tblEtapas_Vehiculo FOREIGN KEY (VehiculoId) REFERENCES dbo.tblVehiculos (Id),
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
   tblInspecciones: por etapa hay una inspeccion de salida y una de llegada; al cerrarse es inmutable. ViajeId es el viaje de la etapa: la
   inspeccion toma de el la placa, el trailer y el conductor. Las columnas ConductorNombre, Placa y Trailer solo conservan lo capturado
   antes de existir los viajes. Latitud y longitud en float (precision GPS completa).
   Una correccion NO modifica la inspeccion cerrada: crea una version nueva (Version = 2, 3...) que referencia a la anterior en
   CorrigeInspeccionId. Por etapa y momento hay una sola inspeccion original (indice filtrado) y cada version se corrige una sola vez.
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
        IniciadaAt       datetimeoffset(3) NOT NULL CONSTRAINT DF_tblInspecciones_IniciadaAt DEFAULT (SYSDATETIMEOFFSET()),
        CerradaAt        datetimeoffset(3) NULL,
        Version          int              NOT NULL CONSTRAINT DF_tblInspecciones_Version DEFAULT (1),
        CorrigeInspeccionId uniqueidentifier NULL,
        CreatedAt        datetimeoffset(3) NOT NULL CONSTRAINT DF_tblInspecciones_CreatedAt DEFAULT (SYSDATETIMEOFFSET()),
        UpdatedAt        datetimeoffset(3) NOT NULL CONSTRAINT DF_tblInspecciones_UpdatedAt DEFAULT (SYSDATETIMEOFFSET()),
        CONSTRAINT FK_tblInspecciones_Etapa FOREIGN KEY (EtapaId) REFERENCES dbo.tblEtapas (Id),
        CONSTRAINT FK_tblInspecciones_Usuario FOREIGN KEY (UsuarioId) REFERENCES dbo.tblUsuarios (Id),
        CONSTRAINT FK_tblInspecciones_Viaje FOREIGN KEY (ViajeId) REFERENCES dbo.tblViajes (Id),
        CONSTRAINT FK_tblInspecciones_Corrige FOREIGN KEY (CorrigeInspeccionId) REFERENCES dbo.tblInspecciones (Id),
        CONSTRAINT CK_tblInspecciones_Momento CHECK (Momento IN ('salida', 'llegada')),
        CONSTRAINT CK_tblInspecciones_Estado CHECK (Estado IN ('borrador', 'cerrada')),
        -- Una inspeccion cerrada tiene hora de cierre y una en borrador no.
        CONSTRAINT CK_tblInspecciones_Cierre CHECK ((Estado = 'borrador' AND CerradaAt IS NULL) OR (Estado = 'cerrada' AND CerradaAt IS NOT NULL)),
        -- La version 1 es la original; las siguientes son correcciones y referencian a la version que corrigen.
        CONSTRAINT CK_tblInspecciones_Version CHECK ((Version = 1 AND CorrigeInspeccionId IS NULL) OR (Version > 1 AND CorrigeInspeccionId IS NOT NULL)),
        CONSTRAINT CK_tblInspecciones_Lat CHECK (Lat IS NULL OR (Lat BETWEEN -90 AND 90)),
        CONSTRAINT CK_tblInspecciones_Lng CHECK (Lng IS NULL OR (Lng BETWEEN -180 AND 180))
    );
    -- A lo sumo una inspeccion ORIGINAL por etapa y momento; las correcciones (con CorrigeInspeccionId) no cuentan.
    -- Los indices se crean con SQL dinamico para que este script se pueda ejecutar tambien sobre una base que aun tiene la tabla anterior.
    EXEC (N'CREATE UNIQUE INDEX UX_tblInspecciones_Etapa_Momento_Original ON dbo.tblInspecciones (EtapaId, Momento) WHERE CorrigeInspeccionId IS NULL');
    -- Cada version se corrige a lo sumo una vez: las versiones forman una cadena.
    EXEC (N'CREATE UNIQUE INDEX UX_tblInspecciones_Corrige ON dbo.tblInspecciones (CorrigeInspeccionId) WHERE CorrigeInspeccionId IS NOT NULL');
    CREATE INDEX IX_tblInspecciones_Usuario ON dbo.tblInspecciones (UsuarioId);
    CREATE INDEX IX_tblInspecciones_Viaje ON dbo.tblInspecciones (ViajeId);
    CREATE INDEX IX_tblInspecciones_Fecha ON dbo.tblInspecciones (Fecha DESC);
    -- Soporta la definicion de "movilizado" (existe informe cerrado por etapa).
    CREATE INDEX IX_tblInspecciones_Etapa_Estado ON dbo.tblInspecciones (EtapaId, Estado);
END
GO

/* -----------------------------------------------------------------------------
   tblHallazgos (averia atribuida a una zona del vehiculo, con su clase, tamano y nivel de severidad)
   ZonaId: la zona del dano (lado + numero, o una zona provisional). Es nula solo en una novedad del conductor aun sin revisar, o en un
   hallazgo historico que no se pudo ubicar (MigracionPendiente = 1). ClaseDano y NivelSeveridad: nulos en una novedad del conductor
   sin revisar (el conductor nunca asigna el nivel); NivelSeveridad tambien en los hallazgos historicos, que no lo tenian.
   ParteVehiculo y CoordsDiagrama (JSON {"x":0..1,"y":0..1}) son del modelo anterior: solo quedan en los hallazgos historicos.
   Si el hallazgo es preexistente, HallazgoOrigenId es el hallazgo previo del mismo VIN que ya lo tenia.
   EstadoSeguimiento: lo cambia el coordinador (validado, rechazado, recuperado o derogado). EvidenciaCompleta se recalcula al cambiar
   el seguimiento o la transferencia. SQL Server 2019 no tiene tipo JSON nativo.
   ----------------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.tblHallazgos', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblHallazgos
    (
        Id              uniqueidentifier NOT NULL CONSTRAINT PK_tblHallazgos PRIMARY KEY,
        InspeccionId    uniqueidentifier NOT NULL,
        ParteVehiculo   nvarchar(200)    NULL,
        ClaseDano       varchar(30)      NULL,
        TamanoDano      varchar(30)      NOT NULL,
        CoordsDiagrama  nvarchar(200)    NULL,
        ZonaId              uniqueidentifier NULL,
        TipoNovedad         varchar(20)      NULL,
        NivelSeveridad      varchar(30)      NULL,
        Origen              varchar(10)      NOT NULL CONSTRAINT DF_tblHallazgos_Origen DEFAULT ('inspector'),
        Nota                nvarchar(500)    NULL,
        EsPreexistente      bit              NOT NULL CONSTRAINT DF_tblHallazgos_EsPreexistente DEFAULT (0),
        HallazgoOrigenId    uniqueidentifier NULL,
        ConfirmadoPor       uniqueidentifier NULL,
        EstadoSeguimiento   varchar(15)      NOT NULL CONSTRAINT DF_tblHallazgos_EstadoSeguimiento DEFAULT ('pendiente'),
        EvidenciaCompleta   bit              NOT NULL CONSTRAINT DF_tblHallazgos_EvidenciaCompleta DEFAULT (0),
        RevisadoPor         uniqueidentifier NULL,
        RevisadoAt          datetimeoffset(3) NULL,
        MigracionPendiente  bit              NOT NULL CONSTRAINT DF_tblHallazgos_MigracionPendiente DEFAULT (0),
        CreatedAt       datetimeoffset(3) NOT NULL CONSTRAINT DF_tblHallazgos_CreatedAt DEFAULT (SYSDATETIMEOFFSET()),
        UpdatedAt       datetimeoffset(3) NOT NULL CONSTRAINT DF_tblHallazgos_UpdatedAt DEFAULT (SYSDATETIMEOFFSET()),
        CONSTRAINT FK_tblHallazgos_Inspeccion FOREIGN KEY (InspeccionId) REFERENCES dbo.tblInspecciones (Id),
        CONSTRAINT FK_tblHallazgos_ClaseDano FOREIGN KEY (ClaseDano) REFERENCES dbo.tblClasesDano (Codigo),
        CONSTRAINT FK_tblHallazgos_TamanoDano FOREIGN KEY (TamanoDano) REFERENCES dbo.tblTamanosDano (Codigo),
        CONSTRAINT FK_tblHallazgos_Zona FOREIGN KEY (ZonaId) REFERENCES dbo.tblZonasDano (Id),
        CONSTRAINT FK_tblHallazgos_Nivel FOREIGN KEY (NivelSeveridad) REFERENCES dbo.tblNivelesSeveridad (Codigo),
        CONSTRAINT FK_tblHallazgos_Origen FOREIGN KEY (HallazgoOrigenId) REFERENCES dbo.tblHallazgos (Id),
        CONSTRAINT FK_tblHallazgos_ConfirmadoPor FOREIGN KEY (ConfirmadoPor) REFERENCES dbo.tblUsuarios (Id),
        CONSTRAINT FK_tblHallazgos_RevisadoPor FOREIGN KEY (RevisadoPor) REFERENCES dbo.tblUsuarios (Id),
        CONSTRAINT CK_tblHallazgos_Coords CHECK (CoordsDiagrama IS NULL OR ISJSON(CoordsDiagrama) = 1),
        CONSTRAINT CK_tblHallazgos_Origen CHECK (Origen IN ('inspector', 'conductor')),
        -- La clase del dano solo puede faltar en una novedad del conductor (la clasifica despues el inspector); el tipo de novedad solo existe en ellas.
        CONSTRAINT CK_tblHallazgos_Clase CHECK (ClaseDano IS NOT NULL OR Origen = 'conductor'),
        CONSTRAINT CK_tblHallazgos_TipoNovedad CHECK (TipoNovedad IS NULL OR (Origen = 'conductor' AND TipoNovedad IN ('golpe', 'piedra', 'granizo', 'incidente_vial'))),
        CONSTRAINT CK_tblHallazgos_Seguimiento CHECK (EstadoSeguimiento IN ('pendiente', 'validado', 'rechazado', 'recuperado', 'derogado')),
        -- Un hallazgo preexistente siempre dice cual hallazgo previo lo origino.
        CONSTRAINT CK_tblHallazgos_Preexistente CHECK (EsPreexistente = 0 OR HallazgoOrigenId IS NOT NULL)
    );
    CREATE INDEX IX_tblHallazgos_Inspeccion ON dbo.tblHallazgos (InspeccionId);
END
GO

-- Indices de consulta de los hallazgos. Usan columnas nuevas del hallazgo, por eso se crean con SQL dinamico y solo si existen (este script tambien se ejecuta
-- sobre una base que aun tiene el hallazgo anterior):
--   por zona (lado + numero) y clase, con la inspeccion incluida: la propuesta de hallazgo preexistente y el mapa de calor;
--   por estado de seguimiento, origen y fecha de revision: la bandeja del coordinador y de las novedades por revisar.
IF COL_LENGTH(N'dbo.tblHallazgos', N'ZonaId') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblHallazgos_Zona_Clase' AND object_id = OBJECT_ID(N'dbo.tblHallazgos'))
    EXEC (N'CREATE INDEX IX_tblHallazgos_Zona_Clase ON dbo.tblHallazgos (ZonaId, ClaseDano) INCLUDE (InspeccionId)');
IF COL_LENGTH(N'dbo.tblHallazgos', N'EstadoSeguimiento') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblHallazgos_Seguimiento' AND object_id = OBJECT_ID(N'dbo.tblHallazgos'))
    EXEC (N'CREATE INDEX IX_tblHallazgos_Seguimiento ON dbo.tblHallazgos (EstadoSeguimiento, Origen, RevisadoAt)');
GO

-- La unidad que "no sale" apunta al hallazgo V1+ que lo causo. tblViajeVehiculos se crea antes que tblHallazgos, por eso la relacion se agrega aqui.
IF COL_LENGTH(N'dbo.tblViajeVehiculos', N'NoSaleHallazgoId') IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tblViajeVehiculos_NoSaleHallazgo')
    EXEC (N'ALTER TABLE dbo.tblViajeVehiculos ADD CONSTRAINT FK_tblViajeVehiculos_NoSaleHallazgo FOREIGN KEY (NoSaleHallazgoId) REFERENCES dbo.tblHallazgos (Id)');
GO

/* -----------------------------------------------------------------------------
   tblEvidencias (foto sellada; append-only por diseno)
   Una foto es de una vista (VistaId: una de las 7 obligatorias o de las 2 adicionales) o de un hallazgo (HallazgoId). Lleva el sello del
   dispositivo (SelloFecha, SelloLat, SelloLng y Precision en metros) y el del servidor (SelloServidorAt, al recibirla) y el hash SHA-256
   del archivo. SelloLat y SelloLng son obligatorios: una foto tomada sin senal de GPS se guarda en (0, 0) y se muestra como
   `sin ubicacion`. Nota = el faltante o dano de las fotos adicionales (piezas en el baul, cojineria).
   EsConstancia = 1 marca la foto de la constancia de no firma de una transferencia: no es de una vista ni de un hallazgo, no cuenta para
   el cierre de la inspeccion y se puede registrar aunque la inspeccion ya este cerrada (la transferencia se firma despues del cierre).
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
        SelloLat      float            NOT NULL,
        SelloLng      float            NOT NULL,
        VistaId       uniqueidentifier NULL,
        HashSha256    char(64)         NULL,
        SelloServidorAt datetimeoffset(3) NOT NULL CONSTRAINT DF_tblEvidencias_SelloServidorAt DEFAULT (SYSDATETIMEOFFSET()),
        Precision     float            NULL,
        DispositivoId nvarchar(100)    NULL,
        Nota          nvarchar(500)    NULL,
        EsConstancia  bit              NOT NULL CONSTRAINT DF_tblEvidencias_EsConstancia DEFAULT (0),
        EsSello       bit              NOT NULL CONSTRAINT DF_tblEvidencias_EsSello DEFAULT (0),
        CreatedAt     datetimeoffset(3) NOT NULL CONSTRAINT DF_tblEvidencias_CreatedAt DEFAULT (SYSDATETIMEOFFSET()),
        UpdatedAt     datetimeoffset(3) NOT NULL CONSTRAINT DF_tblEvidencias_UpdatedAt DEFAULT (SYSDATETIMEOFFSET()),
        CONSTRAINT FK_tblEvidencias_Inspeccion FOREIGN KEY (InspeccionId) REFERENCES dbo.tblInspecciones (Id),
        CONSTRAINT FK_tblEvidencias_Hallazgo FOREIGN KEY (HallazgoId) REFERENCES dbo.tblHallazgos (Id),
        CONSTRAINT FK_tblEvidencias_Vista FOREIGN KEY (VistaId) REFERENCES dbo.tblVistasFoto (Id),
        CONSTRAINT CK_tblEvidencias_Lat CHECK (SelloLat BETWEEN -90 AND 90),
        CONSTRAINT CK_tblEvidencias_Lng CHECK (SelloLng BETWEEN -180 AND 180),
        CONSTRAINT CK_tblEvidencias_Precision CHECK (Precision IS NULL OR Precision >= 0),
        CONSTRAINT CK_tblEvidencias_Hash CHECK (HashSha256 IS NULL OR HashSha256 NOT LIKE '%[^0-9a-f]%'),
        -- La foto del sello de quien valida (EsSello) no es de una vista, de un hallazgo ni una constancia.
        CONSTRAINT CK_tblEvidencias_Sello CHECK (EsSello = 0 OR (HallazgoId IS NULL AND VistaId IS NULL AND EsConstancia = 0))
    );
    EXEC (N'CREATE INDEX IX_tblEvidencias_Vista ON dbo.tblEvidencias (VistaId)');
    CREATE INDEX IX_tblEvidencias_Inspeccion ON dbo.tblEvidencias (InspeccionId);
    CREATE INDEX IX_tblEvidencias_Hallazgo ON dbo.tblEvidencias (HallazgoId);
END
GO

/* -----------------------------------------------------------------------------
   tblHallazgoSeguimiento: historial del seguimiento de un hallazgo (append-only: una fila nunca se modifica ni se borra).
   Accion: seguimiento (el coordinador cambia el estado del hallazgo), revision (un inspector revisa una novedad del conductor) o no_sale
   (la unidad no sale del viaje por ese hallazgo). EvidenciaSelloId es la foto del sello de quien valida (Sofasa); Detalle es un JSON con
   lo asignado en una revision. Al insertar una fila de seguimiento, el estado del hallazgo (tblHallazgos.EstadoSeguimiento) pasa a EstadoNuevo.
   ----------------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.tblHallazgoSeguimiento', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblHallazgoSeguimiento
    (
        Id                uniqueidentifier  NOT NULL CONSTRAINT PK_tblHallazgoSeguimiento PRIMARY KEY,
        HallazgoId        uniqueidentifier  NOT NULL,
        Accion            varchar(15)       NOT NULL,
        EstadoAnterior    varchar(15)       NULL,
        EstadoNuevo       varchar(15)       NULL,
        UsuarioId         uniqueidentifier  NOT NULL,
        Nota              nvarchar(1000)    NULL,
        EvidenciaSelloId  uniqueidentifier  NULL,
        Detalle           nvarchar(max)     NULL,
        CreatedAt         datetimeoffset(3) NOT NULL CONSTRAINT DF_tblHallazgoSeguimiento_CreatedAt DEFAULT (SYSDATETIMEOFFSET()),
        UpdatedAt         datetimeoffset(3) NOT NULL CONSTRAINT DF_tblHallazgoSeguimiento_UpdatedAt DEFAULT (SYSDATETIMEOFFSET()),
        CONSTRAINT FK_tblHallazgoSeguimiento_Hallazgo FOREIGN KEY (HallazgoId) REFERENCES dbo.tblHallazgos (Id),
        CONSTRAINT FK_tblHallazgoSeguimiento_Usuario FOREIGN KEY (UsuarioId) REFERENCES dbo.tblUsuarios (Id),
        CONSTRAINT FK_tblHallazgoSeguimiento_Sello FOREIGN KEY (EvidenciaSelloId) REFERENCES dbo.tblEvidencias (Id),
        CONSTRAINT CK_tblHallazgoSeguimiento_Accion CHECK (Accion IN ('seguimiento', 'revision', 'no_sale')),
        CONSTRAINT CK_tblHallazgoSeguimiento_Estados CHECK (
            (EstadoAnterior IS NULL OR EstadoAnterior IN ('pendiente', 'validado', 'rechazado', 'recuperado', 'derogado'))
            AND (EstadoNuevo IS NULL OR EstadoNuevo IN ('pendiente', 'validado', 'rechazado', 'recuperado', 'derogado'))),
        -- Un cambio de seguimiento siempre dice a que estado pasa el hallazgo.
        CONSTRAINT CK_tblHallazgoSeguimiento_Cambio CHECK (Accion <> 'seguimiento' OR EstadoNuevo IS NOT NULL),
        CONSTRAINT CK_tblHallazgoSeguimiento_Detalle CHECK (Detalle IS NULL OR ISJSON(Detalle) = 1)
    );
    CREATE INDEX IX_tblHallazgoSeguimiento_Hallazgo ON dbo.tblHallazgoSeguimiento (HallazgoId, CreatedAt);
END
GO

-- El historial no se modifica (error 51005); su borrado lo impide el trigger de no borrado de las tablas de evidencia (error 51003).
CREATE OR ALTER TRIGGER dbo.TR_tblHallazgoSeguimiento_NoModificar
ON dbo.tblHallazgoSeguimiento
INSTEAD OF UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 51005, 'El historial de seguimiento no se modifica', 1;
END
GO

-- Se crean con SQL dinamico y solo si las columnas existen, para poder ejecutar este script tambien sobre una base que aun tiene el hallazgo anterior.
IF COL_LENGTH(N'dbo.tblHallazgos', N'EstadoSeguimiento') IS NOT NULL
    EXEC (N'CREATE OR ALTER TRIGGER dbo.TR_tblHallazgoSeguimiento_Sincronizar
ON dbo.tblHallazgoSeguimiento
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;
    -- Si en una misma sentencia llegan varios cambios del mismo hallazgo, queda el ultimo.
    UPDATE h SET h.EstadoSeguimiento = u.EstadoNuevo, h.UpdatedAt = SYSDATETIMEOFFSET()
    FROM dbo.tblHallazgos h
    JOIN (SELECT HallazgoId, EstadoNuevo, ROW_NUMBER() OVER (PARTITION BY HallazgoId ORDER BY CreatedAt DESC, Id DESC) AS Orden
          FROM inserted WHERE Accion = ''seguimiento'') u ON u.HallazgoId = h.Id AND u.Orden = 1;
END');
GO

/* -----------------------------------------------------------------------------
   tblNotificaciones: las notificaciones del sistema (alertas y avisos operativos) y las respuestas del coordinador a una novedad del conductor. Una fila por canal
   (bandeja, push o correo) y por destinatario. Es append-only salvo la lectura (LeidaAt) y la respuesta (RespuestaTexto, RespuestaMedio, RespondidaPor, RespondidaAt):
   cambiar otra columna falla (error 51006) y borrar una fila falla (error 51003). ClaveDedupe es unica por alerta, canal y destinatario para que no se repita.
   El canal correo solo se admite en el reporte diario (que no tiene destinatario: UsuarioId nulo).
   tblDispositivos: los celulares donde un usuario recibe las notificaciones push (token FCM unico).
   ----------------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.tblNotificaciones', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblNotificaciones
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
        CONSTRAINT FK_tblNotificaciones_Usuario FOREIGN KEY (UsuarioId) REFERENCES dbo.tblUsuarios (Id),
        CONSTRAINT FK_tblNotificaciones_Cliente FOREIGN KEY (ClienteId) REFERENCES dbo.tblClientes (Id),
        CONSTRAINT FK_tblNotificaciones_Viaje FOREIGN KEY (ViajeId) REFERENCES dbo.tblViajes (Id),
        CONSTRAINT FK_tblNotificaciones_Vehiculo FOREIGN KEY (VehiculoId) REFERENCES dbo.tblVehiculos (Id),
        CONSTRAINT FK_tblNotificaciones_Hallazgo FOREIGN KEY (HallazgoId) REFERENCES dbo.tblHallazgos (Id),
        CONSTRAINT FK_tblNotificaciones_Inspeccion FOREIGN KEY (InspeccionId) REFERENCES dbo.tblInspecciones (Id),
        CONSTRAINT FK_tblNotificaciones_RespondidaPor FOREIGN KEY (RespondidaPor) REFERENCES dbo.tblUsuarios (Id),
        CONSTRAINT UQ_tblNotificaciones_ClaveDedupe UNIQUE (ClaveDedupe),
        CONSTRAINT CK_tblNotificaciones_Tipo CHECK (Tipo IN ('novedad_conductor', 'respuesta_conductor', 'hallazgo_critico', 'umbral_patron', 'captura_incompleta', 'transferencia_pendiente', 'reporte_diario')),
        CONSTRAINT CK_tblNotificaciones_Urgencia CHECK (Urgencia IN ('alta', 'media')),
        CONSTRAINT CK_tblNotificaciones_Canal CHECK (Canal IN ('bandeja', 'push', 'correo')),
        CONSTRAINT CK_tblNotificaciones_RespuestaMedio CHECK (RespuestaMedio IS NULL OR RespuestaMedio IN ('app', 'llamada')),
        -- Solo el reporte diario no tiene destinatario, y solo el reporte diario se envia por correo.
        CONSTRAINT CK_tblNotificaciones_Destinatario CHECK (UsuarioId IS NOT NULL OR Tipo = 'reporte_diario'),
        CONSTRAINT CK_tblNotificaciones_Correo CHECK (Canal <> 'correo' OR Tipo = 'reporte_diario'),
        -- La respuesta del coordinador es completa (texto, medio, quien y cuando) y solo existe sobre una novedad del conductor.
        CONSTRAINT CK_tblNotificaciones_Respuesta CHECK (
            (RespuestaTexto IS NULL AND RespuestaMedio IS NULL AND RespondidaPor IS NULL AND RespondidaAt IS NULL)
            OR (RespuestaTexto IS NOT NULL AND RespuestaMedio IS NOT NULL AND RespondidaPor IS NOT NULL AND RespondidaAt IS NOT NULL AND Tipo = 'novedad_conductor'))
    );
    CREATE INDEX IX_tblNotificaciones_Usuario_Leida ON dbo.tblNotificaciones (UsuarioId, LeidaAt);
END
GO

-- Solo se lee y se responde una notificacion: cualquier otra columna que se intente cambiar falla (51006). El borrado lo impide el trigger de no borrado de las tablas de evidencia (51003).
CREATE OR ALTER TRIGGER dbo.TR_tblNotificaciones_SoloLecturaYRespuesta
ON dbo.tblNotificaciones
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF UPDATE(Id) OR UPDATE(Tipo) OR UPDATE(Urgencia) OR UPDATE(UsuarioId) OR UPDATE(ClienteId) OR UPDATE(ViajeId) OR UPDATE(VehiculoId) OR UPDATE(HallazgoId)
       OR UPDATE(InspeccionId) OR UPDATE(Titulo) OR UPDATE(Mensaje) OR UPDATE(Canal) OR UPDATE(EnviadaAt) OR UPDATE(ErrorEnvio) OR UPDATE(ClaveDedupe) OR UPDATE(CreatedAt)
        THROW 51006, 'Una notificacion solo admite cambiar su lectura y su respuesta', 1;
END
GO

IF OBJECT_ID(N'dbo.tblDispositivos', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblDispositivos
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
        CONSTRAINT FK_tblDispositivos_Usuario FOREIGN KEY (UsuarioId) REFERENCES dbo.tblUsuarios (Id),
        CONSTRAINT UQ_tblDispositivos_TokenFcm UNIQUE (TokenFcm),
        CONSTRAINT CK_tblDispositivos_Token CHECK (LEN(TokenFcm) > 0),
        CONSTRAINT CK_tblDispositivos_Plataforma CHECK (Plataforma IN ('android', 'ios', 'web'))
    );
    CREATE INDEX IX_tblDispositivos_Usuario ON dbo.tblDispositivos (UsuarioId, Activo);
END
GO

/* -----------------------------------------------------------------------------
   La novedad del conductor es un hallazgo con TipoNovedad (golpe, piedra, granizo o incidente_vial) sobre la inspeccion de SALIDA de TRANSPORTE del VIN. Mientras ningun
   inspector la revise (RevisadoAt nulo) no tiene zona, clase, nivel ni texto libre; al revisarla se completan la zona, la clase y el nivel. El conductor nunca asigna el
   nivel V: un hallazgo de origen conductor no lo tiene hasta que se revisa. Se crean con SQL dinamico y solo si las columnas existen, para poder ejecutar este script
   tambien sobre una base que aun tiene el hallazgo anterior.
   ----------------------------------------------------------------------------- */
IF COL_LENGTH(N'dbo.tblHallazgos', N'TipoNovedad') IS NOT NULL AND COL_LENGTH(N'dbo.tblHallazgos', N'RevisadoAt') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_tblHallazgos_NovedadSinRevisar' AND parent_object_id = OBJECT_ID(N'dbo.tblHallazgos'))
        EXEC (N'ALTER TABLE dbo.tblHallazgos ADD CONSTRAINT CK_tblHallazgos_NovedadSinRevisar CHECK (
            TipoNovedad IS NULL OR RevisadoAt IS NOT NULL OR (ZonaId IS NULL AND ClaseDano IS NULL AND NivelSeveridad IS NULL AND Nota IS NULL))');
    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_tblHallazgos_NovedadRevisada' AND parent_object_id = OBJECT_ID(N'dbo.tblHallazgos'))
        EXEC (N'ALTER TABLE dbo.tblHallazgos ADD CONSTRAINT CK_tblHallazgos_NovedadRevisada CHECK (
            TipoNovedad IS NULL OR RevisadoAt IS NULL OR (ZonaId IS NOT NULL AND ClaseDano IS NOT NULL AND NivelSeveridad IS NOT NULL))');
    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_tblHallazgos_NivelDelConductor' AND parent_object_id = OBJECT_ID(N'dbo.tblHallazgos'))
        EXEC (N'ALTER TABLE dbo.tblHallazgos ADD CONSTRAINT CK_tblHallazgos_NivelDelConductor CHECK (Origen <> ''conductor'' OR RevisadoAt IS NOT NULL OR NivelSeveridad IS NULL)');
    EXEC (N'CREATE OR ALTER TRIGGER dbo.TR_tblHallazgos_NovedadDeTransporte
ON dbo.tblHallazgos
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted h
               JOIN dbo.tblInspecciones i ON i.Id = h.InspeccionId
               JOIN dbo.tblEtapas e ON e.Id = i.EtapaId
               JOIN dbo.tblEtapasCatalogo c ON c.Id = e.EtapaCatalogoId
               WHERE h.TipoNovedad IS NOT NULL AND (i.Momento <> ''salida'' OR c.Codigo <> ''transporte''))
        THROW 51007, ''Una novedad del conductor es de la inspeccion de salida de Transporte'', 1;
END');
END
GO

/* -----------------------------------------------------------------------------
   vwPatronesPorCuadrante: hallazgos por zona para el mapa de calor y los patrones. Cuenta por zona (lado + numero, o la zona provisional), no solo
   por numero: izquierdo 7 y derecho 7 son filas distintas. Incluye los hallazgos de toda inspeccion CERRADA aunque la unidad no haya terminado el
   viaje, y excluye las novedades del conductor sin revisar y los hallazgos aun sin zona. Total = hallazgos del grupo.
   ----------------------------------------------------------------------------- */
IF COL_LENGTH(N'dbo.tblHallazgos', N'ZonaId') IS NOT NULL
    EXEC (N'CREATE OR ALTER VIEW dbo.vwPatronesPorCuadrante
AS
SELECT x.ZonaId, x.CuadranteId, x.Numero, x.Lado, x.ZonaCodigo, x.ZonaNombre, x.Proceso, x.NivelSeveridad, x.ClaseDano, x.ClienteId, x.Operacion, x.Fecha,
       COUNT(*) AS Total
FROM (
    SELECT z.Id AS ZonaId, z.CuadranteId, z.Numero, z.Lado, z.Codigo AS ZonaCodigo,
           CASE WHEN z.Lado IS NOT NULL THEN CONCAT(UPPER(LEFT(z.Lado, 1)), SUBSTRING(z.Lado, 2, 20), N'' '', z.Numero, N'' '', NCHAR(183), N'' '', c.Nombre)
                WHEN z.Numero IS NOT NULL THEN CONCAT(z.Numero, N'' '', NCHAR(183), N'' '', c.Nombre)
                ELSE z.Nombre END AS ZonaNombre,
           ec.Codigo AS Proceso, h.NivelSeveridad, h.ClaseDano, v.ClienteId, v.Operacion, CAST(i.CerradaAt AS date) AS Fecha
    FROM dbo.tblHallazgos h
    JOIN dbo.tblZonasDano z ON z.Id = h.ZonaId
    LEFT JOIN dbo.tblCuadrantes c ON c.Id = z.CuadranteId
    JOIN dbo.tblInspecciones i ON i.Id = h.InspeccionId AND i.Estado = ''cerrada''
    JOIN dbo.tblEtapas e ON e.Id = i.EtapaId
    JOIN dbo.tblEtapasCatalogo ec ON ec.Id = e.EtapaCatalogoId
    JOIN dbo.tblVehiculos v ON v.Id = e.VehiculoId
    WHERE NOT (h.Origen = ''conductor'' AND h.RevisadoAt IS NULL)
) AS x
GROUP BY x.ZonaId, x.CuadranteId, x.Numero, x.Lado, x.ZonaCodigo, x.ZonaNombre, x.Proceso, x.NivelSeveridad, x.ClaseDano, x.ClienteId, x.Operacion, x.Fecha');
GO

/* -----------------------------------------------------------------------------
   tblTransferenciasCustodia (entrega y recibo; una por inspeccion)
   Cada firma la envia su titular (quien entrega o quien recibe) y guarda su fecha e IP. La transferencia se cierra con un TipoCierre:
     firmada      ambas partes firmaron;
     con_reservas ambas firmaron y quien recibe dejo sus observaciones (ReservasTexto);
     no_firma     una parte (NoFirmaParte) se nego a firmar: queda el motivo, un testigo con usuario y la foto de la constancia.
   Mientras TipoCierre es NULL la transferencia esta abierta; con un tipo de cierre es inmutable y la etapa siguiente puede iniciarse.
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
        FirmaEntregaAt    datetimeoffset(3) NULL,
        FirmaEntregaIp    varchar(45)      NULL,
        FirmaRecibeAt     datetimeoffset(3) NULL,
        FirmaRecibeIp     varchar(45)      NULL,
        TipoCierre        varchar(20)      NULL,
        ReservasTexto     nvarchar(1000)   NULL,
        NoFirmaParte      varchar(10)      NULL,
        MotivoNoFirma     nvarchar(1000)   NULL,
        TestigoUsuarioId  uniqueidentifier NULL,
        EvidenciaConstanciaId uniqueidentifier NULL,
        CreatedAt         datetimeoffset(3) NOT NULL CONSTRAINT DF_tblTransferenciasCustodia_CreatedAt DEFAULT (SYSDATETIMEOFFSET()),
        UpdatedAt         datetimeoffset(3) NOT NULL CONSTRAINT DF_tblTransferenciasCustodia_UpdatedAt DEFAULT (SYSDATETIMEOFFSET()),
        CONSTRAINT FK_tblTransferencias_Inspeccion FOREIGN KEY (InspeccionId) REFERENCES dbo.tblInspecciones (Id),
        CONSTRAINT FK_tblTransferencias_Entrega FOREIGN KEY (EntregaUsuarioId) REFERENCES dbo.tblUsuarios (Id),
        CONSTRAINT FK_tblTransferencias_Recibe FOREIGN KEY (RecibeUsuarioId) REFERENCES dbo.tblUsuarios (Id),
        CONSTRAINT FK_tblTransferencias_Testigo FOREIGN KEY (TestigoUsuarioId) REFERENCES dbo.tblUsuarios (Id),
        CONSTRAINT FK_tblTransferencias_Constancia FOREIGN KEY (EvidenciaConstanciaId) REFERENCES dbo.tblEvidencias (Id),
        CONSTRAINT UQ_tblTransferencias_Inspeccion UNIQUE (InspeccionId),
        CONSTRAINT CK_tblTransferencias_TipoCierre CHECK (TipoCierre IS NULL OR TipoCierre IN ('firmada', 'con_reservas', 'no_firma')),
        CONSTRAINT CK_tblTransferencias_Parte CHECK (NoFirmaParte IS NULL OR NoFirmaParte IN ('entrega', 'recibe')),
        -- El testigo es una tercera persona: ni quien entrega ni quien recibe.
        CONSTRAINT CK_tblTransferencias_Testigo CHECK (TestigoUsuarioId IS NULL OR (TestigoUsuarioId <> EntregaUsuarioId AND TestigoUsuarioId <> RecibeUsuarioId)),
        -- Cada tipo de cierre exige lo suyo: las dos firmas (con o sin reservas) o el motivo, el testigo y la foto de la constancia.
        CONSTRAINT CK_tblTransferencias_Cierre CHECK (
            TipoCierre IS NULL
            OR (TipoCierre = 'firmada' AND FirmaEntrega IS NOT NULL AND FirmaEntrega <> '' AND FirmaRecibe IS NOT NULL AND FirmaRecibe <> '' AND ReservasTexto IS NULL)
            OR (TipoCierre = 'con_reservas' AND FirmaEntrega IS NOT NULL AND FirmaEntrega <> '' AND FirmaRecibe IS NOT NULL AND FirmaRecibe <> '' AND ReservasTexto IS NOT NULL AND ReservasTexto <> '')
            OR (TipoCierre = 'no_firma' AND NoFirmaParte IS NOT NULL AND MotivoNoFirma IS NOT NULL AND MotivoNoFirma <> '' AND TestigoUsuarioId IS NOT NULL AND EvidenciaConstanciaId IS NOT NULL))
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
-- Cliente inicial: el actual. Los usuarios y vehiculos nuevos pertenecen al primer cliente activo si no se indica otro.
INSERT INTO dbo.tblClientes (Id, Codigo, Nombre, Orden)
SELECT NEWID(), 'sofasa', N'Sofasa', 1
WHERE NOT EXISTS (SELECT 1 FROM dbo.tblClientes c WHERE c.Codigo = 'sofasa');
GO

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

-- La accion de cada nivel: que se hace con una unidad que lo tiene. Solo se completa donde aun no hay una (no pisa lo que se haya editado).
INSERT INTO dbo.tblNivelesSeveridad (Id, Codigo, Nombre, Accion, Orden)
SELECT NEWID(), v.Codigo, v.Nombre, v.Accion, v.Orden
FROM (VALUES
    ('v1_plus', N'V1+', N'Se recupera antes del despacho', 1),
    ('v1',      N'V1',  N'Se rechaza el veh' + NCHAR(237) + N'culo', 2),
    ('v2',      N'V2',  N'Validaci' + NCHAR(243) + N'n por Sofasa', 3),
    ('v3',      N'V3',  N'Se deroga (se acepta el defecto)', 4)) AS v (Codigo, Nombre, Accion, Orden)
WHERE NOT EXISTS (SELECT 1 FROM dbo.tblNivelesSeveridad c WHERE c.Codigo = v.Codigo);
GO

UPDATE n SET Accion = v.Accion
FROM dbo.tblNivelesSeveridad n
JOIN (VALUES
    ('v1_plus', N'Se recupera antes del despacho'),
    ('v1',      N'Se rechaza el veh' + NCHAR(237) + N'culo'),
    ('v2',      N'Validaci' + NCHAR(243) + N'n por Sofasa'),
    ('v3',      N'Se deroga (se acepta el defecto)')) AS v (Codigo, Accion) ON v.Codigo = n.Codigo
WHERE n.Accion IS NULL;
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

-- Una unidad marcada "no sale" en un viaje no cuenta como completa en ese viaje (el viaje sigue sin ella). Con SQL dinamico, para poder ejecutar este
-- script tambien sobre una base que aun no tiene el estado de la unidad.
IF COL_LENGTH(N'dbo.tblViajeVehiculos', N'EstadoUnidad') IS NOT NULL
    EXEC (N'CREATE OR ALTER VIEW dbo.vwViajesCompletos
AS
SELECT x.ViajeId, x.VehiculoId
FROM (
    SELECT e.ViajeId, e.VehiculoId,
           CASE WHEN EXISTS (SELECT 1 FROM dbo.tblInspecciones i
                              WHERE i.EtapaId = e.Id AND i.Estado = ''cerrada'')
                THEN 1 ELSE 0 END AS Cerrada
    FROM dbo.tblEtapas e
) AS x
WHERE NOT EXISTS (SELECT 1 FROM dbo.tblViajeVehiculos vv
                   WHERE vv.ViajeId = x.ViajeId AND vv.VehiculoId = x.VehiculoId AND vv.EstadoUnidad = ''no_sale'')
GROUP BY x.ViajeId, x.VehiculoId
HAVING COUNT(*) > 0 AND COUNT(*) = SUM(x.Cerrada)');
ELSE
    EXEC (N'CREATE OR ALTER VIEW dbo.vwViajesCompletos
AS
SELECT x.ViajeId, x.VehiculoId
FROM (
    SELECT e.ViajeId, e.VehiculoId,
           CASE WHEN EXISTS (SELECT 1 FROM dbo.tblInspecciones i
                              WHERE i.EtapaId = e.Id AND i.Estado = ''cerrada'')
                THEN 1 ELSE 0 END AS Cerrada
    FROM dbo.tblEtapas e
) AS x
GROUP BY x.ViajeId, x.VehiculoId
HAVING COUNT(*) > 0 AND COUNT(*) = SUM(x.Cerrada)');
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

-- La foto de la constancia de no firma (EsConstancia = 1) y la del sello de quien valida un hallazgo (EsSello = 1) si se admiten: la transferencia se
-- firma y el hallazgo se valida despues del cierre. Se crea con SQL dinamico y solo si las columnas existen, para poder ejecutar este script tambien
-- sobre una base que aun no las tiene.
IF COL_LENGTH(N'dbo.tblEvidencias', N'EsSello') IS NOT NULL
    EXEC (N'CREATE OR ALTER TRIGGER dbo.TR_tblEvidencias_NoEnCerrada
ON dbo.tblEvidencias
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted i
               JOIN dbo.tblInspecciones n ON n.Id = i.InspeccionId
               WHERE n.Estado = ''cerrada'' AND i.EsConstancia = 0 AND i.EsSello = 0)
        THROW 51002, ''La inspeccion esta cerrada y es inmutable; no admite evidencias nuevas'', 1;
END');
GO

/* -----------------------------------------------------------------------------
   La evidencia no se borra (ultima defensa en la base, ademas de la aplicacion):
   ningun usuario ni proceso puede borrar evidencias, hallazgos, transferencias ni el registro de auditoria, y una inspeccion
   solo se borra si es un borrador vacio (sin fotos, hallazgos ni transferencia). El error es 51003. Por eso las relaciones
   entre vehiculo, etapa, inspeccion y sus registros no tienen borrado en cascada: para quitar algo hay que hacerlo de forma explicita.
   Un trigger INSTEAD OF DELETE no puede existir en una tabla con una FK en cascada: en una base que aun conserva las cascadas
   anteriores no se crea (se crea al aplicar el cambio de esquema que las quita).
   ----------------------------------------------------------------------------- */
DECLARE @tablaProtegida sysname, @sqlProtegida nvarchar(max);
DECLARE cProtegidas CURSOR LOCAL FAST_FORWARD FOR
    SELECT v FROM (VALUES (N'tblEvidencias'), (N'tblHallazgos'), (N'tblTransferenciasCustodia'), (N'tblAuditLog'), (N'tblHallazgoSeguimiento'), (N'tblNotificaciones')) AS t (v);
OPEN cProtegidas;
FETCH NEXT FROM cProtegidas INTO @tablaProtegida;
WHILE @@FETCH_STATUS = 0
BEGIN
    IF OBJECT_ID(N'dbo.' + @tablaProtegida, N'U') IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID(N'dbo.' + @tablaProtegida) AND delete_referential_action <> 0)
    BEGIN
        SET @sqlProtegida = N'CREATE OR ALTER TRIGGER dbo.TR_' + @tablaProtegida + N'_NoBorrar
ON dbo.' + @tablaProtegida + N'
INSTEAD OF DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 51003, ''Este registro es evidencia y no se puede borrar'', 1;
END';
        EXEC (@sqlProtegida);
    END
    FETCH NEXT FROM cProtegidas INTO @tablaProtegida;
END
CLOSE cProtegidas;
DEALLOCATE cProtegidas;
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID(N'dbo.tblInspecciones') AND delete_referential_action <> 0)
    EXEC (N'CREATE OR ALTER TRIGGER dbo.TR_tblInspecciones_NoBorrar
ON dbo.tblInspecciones
INSTEAD OF DELETE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM deleted d
               WHERE d.Estado = ''cerrada''
                  OR EXISTS (SELECT 1 FROM dbo.tblEvidencias e WHERE e.InspeccionId = d.Id)
                  OR EXISTS (SELECT 1 FROM dbo.tblHallazgos h WHERE h.InspeccionId = d.Id)
                  OR EXISTS (SELECT 1 FROM dbo.tblTransferenciasCustodia t WHERE t.InspeccionId = d.Id))
        THROW 51003, ''Una inspeccion cerrada, o con fotos, hallazgos o transferencia, es evidencia y no se puede borrar'', 1;
    DELETE i FROM dbo.tblInspecciones i JOIN deleted d ON d.Id = i.Id;
END');
GO

/* -----------------------------------------------------------------------------
   tblParametros: los valores que el negocio ajusta sin desplegar codigo (objetivo diario, destinatarios y hora del reporte, horario de operacion, retencion, precision minima del GPS y
   vigencia de las URL firmadas). Una fila por parametro; las filas se crean aqui y no por la API, que solo cambia su valor. Valor nulo = sin valor (por ejemplo, ningun destinatario del reporte
   o la retencion que todavia no se define). Tipo dice como se valida el valor: numero, porcentaje (0 a 100, con punto decimal), texto, lista (codigos separados por coma), hora (HH:mm) o
   correos (direcciones separadas por coma). Los valores iniciales los confirma Calidad; las filas ya existentes nunca se sobrescriben.
   ----------------------------------------------------------------------------- */
IF OBJECT_ID(N'dbo.tblParametros', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tblParametros
    (
        Clave           varchar(100)      NOT NULL CONSTRAINT PK_tblParametros PRIMARY KEY,
        Valor           nvarchar(1000)    NULL,
        Tipo            varchar(15)       NOT NULL,
        Descripcion     nvarchar(300)     NOT NULL,
        ActualizadoPor  uniqueidentifier  NULL,
        UpdatedAt       datetimeoffset(3) NOT NULL CONSTRAINT DF_tblParametros_UpdatedAt DEFAULT (SYSDATETIMEOFFSET()),
        CONSTRAINT FK_tblParametros_ActualizadoPor FOREIGN KEY (ActualizadoPor) REFERENCES dbo.tblUsuarios (Id),
        CONSTRAINT CK_tblParametros_Tipo CHECK (Tipo IN ('numero', 'porcentaje', 'texto', 'lista', 'hora', 'correos')),
        CONSTRAINT CK_tblParametros_Valor CHECK (Valor IS NULL OR LEN(Valor) > 0)
    );
END
GO

INSERT INTO dbo.tblParametros (Clave, Valor, Tipo, Descripcion)
SELECT v.Clave, v.Valor, v.Tipo, v.Descripcion
FROM (VALUES
    ('objetivo_diario_novedad',        N'1.2',   'porcentaje', N'Objetivo diario de vehiculos con novedad (al menos un hallazgo V1), en porcentaje'),
    ('reporte_diario_destinatarios',   NULL,     'correos',    N'Correos que reciben el reporte diario, separados por coma; vacio = no se envia'),
    ('reporte_diario_hora',            N'06:00', 'hora',       N'Hora (HH:mm, hora de Colombia) a la que se envia el reporte diario'),
    ('horario_operacion_inicio',       N'08:00', 'hora',       N'Hora (HH:mm) en que empieza el horario de operacion'),
    ('horario_operacion_fin',          N'21:00', 'hora',       N'Hora (HH:mm) en que termina el horario de operacion'),
    ('retencion_dias',                 NULL,     'numero',     N'Dias que se conserva la evidencia; sin valor hasta que se defina la retencion'),
    ('gps_precision_minima_m',         N'30',    'numero',     N'Precision minima del GPS, en metros, para no avisar de precision baja'),
    ('url_firmada_minutos',            N'15',    'numero',     N'Minutos de vigencia de las URL firmadas de las fotos')
) AS v (Clave, Valor, Tipo, Descripcion)
WHERE NOT EXISTS (SELECT 1 FROM dbo.tblParametros p WHERE p.Clave = v.Clave);
GO

PRINT 'IA-AUTOS: esquema aplicado correctamente.';
GO
