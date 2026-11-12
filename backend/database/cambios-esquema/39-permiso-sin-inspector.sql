/* =============================================================================
   Permiso de la excepcion "No hay inspector", para una base de IA Autos YA EXISTENTE (con 38-capacidad-de-la-ninera.sql aplicado). Se ejecuta UNA vez (si ya esta aplicado, no cambia nada) y todo ocurre en UNA
   transaccion: si algo falla se revierte completo.

   Que hace:
     1. Crea tblPermisosSinInspector: la solicitud del conductor cuando el punto de cargue tiene inspectores (una por viaje), con su motivo, su texto, la fecha y el GPS de cuando la pidio; la concede un
        coordinador o administrador (ConcedidoPor) o, si nadie responde en 10 minutos, el reloj (PorTiempo = 1; CK_tblPermisosSinInspector_Reloj impide concederla antes de los 10 minutos).
     2. tblNovedadesProceso: columna PermisoId (la solicitud con la que se activo la excepcion; nula cuando el punto no tenia inspectores) y su llave foranea.
     3. tblNotificaciones: CK_tblNotificaciones_Tipo admite el tipo permiso_sin_inspector, sin quitar ninguno de los doce existentes.

   Seguridad de los datos: solo crea una tabla, agrega una columna nula y reemplaza una restriccion por una que admite un tipo mas; no cambia ningun dato.

   Como ejecutarlo: abrir en SSMS, DBeaver o sqlcmd CONECTADO a la base de IA Autos, ajustar @esquema y ejecutar completo, en UNA sola ejecucion de script, con un usuario con permiso para
   crear tablas y modificarlas (no el usuario de la aplicacion). Ejecutarlo ANTES de desplegar la API.
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
IF OBJECT_ID(@q + N'.tblNovedadesProceso', N'U') IS NULL OR OBJECT_ID(@q + N'.tblNotificaciones', N'U') IS NULL OR OBJECT_ID(@q + N'.tblMotivosSinInspector', N'U') IS NULL
    THROW 50001, N'Faltan tablas de IA Autos (novedades de proceso, notificaciones o motivos) en el esquema indicado: revise @esquema.', 1;

-- Pasos en orden. {S} = esquema entre corchetes; ~ = comilla simple dentro del texto.
DECLARE @paso TABLE (n int IDENTITY(1, 1) PRIMARY KEY, descripcion nvarchar(100), s nvarchar(max));

INSERT @paso (descripcion, s) VALUES
(N'tabla tblPermisosSinInspector', N'
IF OBJECT_ID(N~{S}.tblPermisosSinInspector~, N~U~) IS NULL
BEGIN
    CREATE TABLE {S}.tblPermisosSinInspector
    (
        Id            uniqueidentifier  NOT NULL CONSTRAINT PK_tblPermisosSinInspector PRIMARY KEY,
        ViajeId       uniqueidentifier  NOT NULL,
        ConductorId   uniqueidentifier  NOT NULL,
        LugarId       uniqueidentifier  NOT NULL,
        MotivoCodigo  varchar(30)       NOT NULL,
        MotivoTexto   nvarchar(300)     NULL,
        Fecha         datetimeoffset(3) NOT NULL,
        Lat           float             NULL,
        Lng           float             NULL,
        Estado        varchar(10)       NOT NULL CONSTRAINT DF_tblPermisosSinInspector_Estado DEFAULT (~pendiente~),
        ConcedidoAt   datetimeoffset(3) NULL,
        ConcedidoPor  uniqueidentifier  NULL,
        PorTiempo     bit               NOT NULL CONSTRAINT DF_tblPermisosSinInspector_PorTiempo DEFAULT (0),
        CreatedAt     datetimeoffset(3) NOT NULL CONSTRAINT DF_tblPermisosSinInspector_CreatedAt DEFAULT (SYSDATETIMEOFFSET()),
        UpdatedAt     datetimeoffset(3) NOT NULL CONSTRAINT DF_tblPermisosSinInspector_UpdatedAt DEFAULT (SYSDATETIMEOFFSET()),
        CONSTRAINT FK_tblPermisosSinInspector_Viaje FOREIGN KEY (ViajeId) REFERENCES {S}.tblViajes (Id),
        CONSTRAINT FK_tblPermisosSinInspector_Conductor FOREIGN KEY (ConductorId) REFERENCES {S}.tblUsuarios (Id),
        CONSTRAINT FK_tblPermisosSinInspector_Lugar FOREIGN KEY (LugarId) REFERENCES {S}.tblLugares (Id),
        CONSTRAINT FK_tblPermisosSinInspector_Motivo FOREIGN KEY (MotivoCodigo) REFERENCES {S}.tblMotivosSinInspector (Codigo),
        CONSTRAINT FK_tblPermisosSinInspector_ConcedidoPor FOREIGN KEY (ConcedidoPor) REFERENCES {S}.tblUsuarios (Id),
        CONSTRAINT UQ_tblPermisosSinInspector_Viaje UNIQUE (ViajeId),
        CONSTRAINT CK_tblPermisosSinInspector_Estado CHECK (Estado IN (~pendiente~, ~concedido~)),
        CONSTRAINT CK_tblPermisosSinInspector_Texto CHECK (MotivoTexto IS NULL OR LEN(LTRIM(RTRIM(MotivoTexto))) > 0),
        CONSTRAINT CK_tblPermisosSinInspector_Lat CHECK (Lat IS NULL OR (Lat BETWEEN -90 AND 90)),
        CONSTRAINT CK_tblPermisosSinInspector_Lng CHECK (Lng IS NULL OR (Lng BETWEEN -180 AND 180)),
        -- Pendiente no trae concesion; concedido dice cuando y quien (un coordinador o administrador) o, si fue el reloj, ninguno.
        CONSTRAINT CK_tblPermisosSinInspector_Concesion CHECK (
            (Estado = ~pendiente~ AND ConcedidoAt IS NULL AND ConcedidoPor IS NULL AND PorTiempo = 0)
            OR (Estado = ~concedido~ AND ConcedidoAt IS NOT NULL AND ((PorTiempo = 0 AND ConcedidoPor IS NOT NULL) OR (PorTiempo = 1 AND ConcedidoPor IS NULL)))),
        -- El reloj nunca concede antes de los 10 minutos de la solicitud.
        CONSTRAINT CK_tblPermisosSinInspector_Reloj CHECK (PorTiempo = 0 OR ConcedidoAt >= DATEADD(MINUTE, 10, CreatedAt))
    );
    CREATE INDEX IX_tblPermisosSinInspector_Estado ON {S}.tblPermisosSinInspector (Estado, CreatedAt);
END'),
(N'tblNovedadesProceso: permiso con el que se activo la excepcion', N'
IF COL_LENGTH(N~{S}.tblNovedadesProceso~, N~PermisoId~) IS NULL
    ALTER TABLE {S}.tblNovedadesProceso ADD PermisoId uniqueidentifier NULL'),
(N'tblNovedadesProceso: llave foranea hacia el permiso', N'
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N~FK_tblNovedadesProceso_Permiso~ AND parent_object_id = OBJECT_ID(N~{S}.tblNovedadesProceso~))
    ALTER TABLE {S}.tblNovedadesProceso ADD CONSTRAINT FK_tblNovedadesProceso_Permiso FOREIGN KEY (PermisoId) REFERENCES {S}.tblPermisosSinInspector (Id)'),
(N'tblNotificaciones: tipo permiso_sin_inspector', N'
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N~CK_tblNotificaciones_Tipo~ AND parent_object_id = OBJECT_ID(N~{S}.tblNotificaciones~) AND definition LIKE N~%permiso_sin_inspector%~)
BEGIN
    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N~CK_tblNotificaciones_Tipo~ AND parent_object_id = OBJECT_ID(N~{S}.tblNotificaciones~))
        ALTER TABLE {S}.tblNotificaciones DROP CONSTRAINT CK_tblNotificaciones_Tipo;
    ALTER TABLE {S}.tblNotificaciones ADD CONSTRAINT CK_tblNotificaciones_Tipo CHECK (Tipo IN (~novedad_conductor~, ~respuesta_conductor~, ~hallazgo_critico~, ~umbral_patron~, ~captura_incompleta~, ~transferencia_pendiente~, ~reporte_diario~, ~novedad_proceso~, ~reclamo_sin_actividad~, ~fuera_de_rango~, ~cargue_sin_reclamar~, ~propuesta_ubicacion~, ~permiso_sin_inspector~));
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

-- Resultado (esperado: todo en 1).
DECLARE @cuentas nvarchar(max) = REPLACE(N'
SELECT
    (SELECT COUNT(*) FROM sys.tables WHERE name = N~tblPermisosSinInspector~ AND schema_id = SCHEMA_ID(N~{E}~)) AS TablaDePermisos,
    (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N~{S}.tblNovedadesProceso~) AND name = N~PermisoId~) AS PermisoEnLaNovedad,
    (SELECT COUNT(*) FROM sys.check_constraints WHERE name = N~CK_tblNotificaciones_Tipo~ AND parent_object_id = OBJECT_ID(N~{S}.tblNotificaciones~) AND definition LIKE N~%permiso_sin_inspector%~) AS TipoDeAviso;', N'{S}', @q);
SET @cuentas = REPLACE(REPLACE(@cuentas, N'{E}', @esquema), N'~', N'''');
EXEC (@cuentas);
