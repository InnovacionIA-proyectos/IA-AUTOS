/* =============================================================================
   La novedad del viaje y el reenvio de las novedades sin duplicar, para una base de IA Autos YA EXISTENTE. Se ejecuta UNA vez (si ya esta aplicado, no cambia nada) y todo ocurre en UNA transaccion: si algo falla se revierte
   completo.

   Que hace:
     1. tblTiposNovedadViaje: el catalogo de tipos de novedad del viaje (codigo, nombre, rol de quien lo reporta: conductor, inspector o todos, urgencia del aviso: alta o media, orden y activo) con sus diez valores iniciales.
        Una fila que ya existe NUNCA se sobrescribe.
     2. tblNovedadesViaje: lo que el conductor o un inspector reporta sobre el VIAJE y no sobre un carro: viaje, tipo, quien la reporto y su rol, el proceso y la parada (solo si la reporto un inspector), el texto (hasta 300),
        la fecha y el GPS del celular (opcional, siempre juntos), la foto sellada (opcional) y el identificador del envio (unico por viaje). Es append-only: UPDATE falla con 51005 y DELETE con 51003.
     3. tblHallazgos.IdEnvio: el identificador del envio de la novedad de un carro (nulo en todo lo que ya existia), unico por inspeccion cuando existe: un reenvio no la duplica.
     4. tblNotificaciones: CK_tblNotificaciones_Tipo admite el tipo novedad_viaje, sin quitar ninguno de los existentes.

   Seguridad de los datos: solo agrega tablas, una columna nula, restricciones, indices, triggers y un tipo de aviso; no modifica ni borra ningun dato existente. Se ejecuta ANTES de desplegar la API.

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
IF OBJECT_ID(@q + N'.tblViajes', N'U') IS NULL OR OBJECT_ID(@q + N'.tblLugares', N'U') IS NULL OR OBJECT_ID(@q + N'.tblUsuarios', N'U') IS NULL
   OR OBJECT_ID(@q + N'.tblHallazgos', N'U') IS NULL OR OBJECT_ID(@q + N'.tblNotificaciones', N'U') IS NULL
    THROW 50001, N'Faltan tblViajes, tblLugares, tblUsuarios, tblHallazgos o tblNotificaciones de IA Autos en el esquema indicado: revise @esquema.', 1;

-- Pasos en orden. {S} = esquema entre corchetes; ~ = comilla simple dentro del texto.
DECLARE @paso TABLE (n int IDENTITY(1, 1) PRIMARY KEY, descripcion nvarchar(100), s nvarchar(max));

INSERT @paso (descripcion, s) VALUES
(N'tblTiposNovedadViaje: el catalogo de tipos', N'
IF OBJECT_ID(N~{S}.tblTiposNovedadViaje~, N~U~) IS NULL
BEGIN
    CREATE TABLE {S}.tblTiposNovedadViaje
    (
        Id        uniqueidentifier  NOT NULL CONSTRAINT PK_tblTiposNovedadViaje PRIMARY KEY,
        Codigo    varchar(30)       NOT NULL,
        Nombre    nvarchar(100)     NOT NULL,
        Rol       varchar(10)       NOT NULL CONSTRAINT DF_tblTiposNovedadViaje_Rol DEFAULT (~todos~),
        Urgencia  varchar(10)       NOT NULL CONSTRAINT DF_tblTiposNovedadViaje_Urgencia DEFAULT (~media~),
        Orden     int               NOT NULL CONSTRAINT DF_tblTiposNovedadViaje_Orden DEFAULT (0),
        Activo    bit               NOT NULL CONSTRAINT DF_tblTiposNovedadViaje_Activo DEFAULT (1),
        CreatedAt datetimeoffset(3) NOT NULL CONSTRAINT DF_tblTiposNovedadViaje_CreatedAt DEFAULT (SYSDATETIMEOFFSET()),
        UpdatedAt datetimeoffset(3) NOT NULL CONSTRAINT DF_tblTiposNovedadViaje_UpdatedAt DEFAULT (SYSDATETIMEOFFSET()),
        CONSTRAINT UQ_tblTiposNovedadViaje_Codigo UNIQUE (Codigo),
        CONSTRAINT CK_tblTiposNovedadViaje_Nombre CHECK (LEN(LTRIM(RTRIM(Nombre))) > 0),
        CONSTRAINT CK_tblTiposNovedadViaje_Rol CHECK (Rol IN (~conductor~, ~inspector~, ~todos~)),
        CONSTRAINT CK_tblTiposNovedadViaje_Urgencia CHECK (Urgencia IN (~alta~, ~media~))
    );
END'),
(N'tblTiposNovedadViaje: los valores iniciales', N'
INSERT INTO {S}.tblTiposNovedadViaje (Id, Codigo, Nombre, Rol, Urgencia, Orden)
SELECT NEWID(), v.Codigo, v.Nombre, v.Rol, v.Urgencia, v.Orden
FROM (VALUES
    (~falla_camion~,          N~Falla del cami~ + NCHAR(243) + N~n~,                 ~conductor~, ~alta~,  1),
    (~dano_ninera~,           N~Da~ + NCHAR(241) + N~o en la ni~ + NCHAR(241) + N~era~, ~conductor~, ~media~, 2),
    (~bloqueo_via~,           N~Bloqueo de v~ + NCHAR(237) + N~a~,                   ~conductor~, ~media~, 3),
    (~intento_robo~,          N~Intento de robo~,                                    ~conductor~, ~alta~,  4),
    (~accidente~,             N~Accidente~,                                          ~conductor~, ~alta~,  5),
    (~carro_no_llego_patio~,  N~Carro no lleg~ + NCHAR(243) + N~ al patio~,          ~inspector~, ~media~, 6),
    (~puesto_ninera_danado~,  N~Puesto de la ni~ + NCHAR(241) + N~era da~ + NCHAR(241) + N~ado~, ~inspector~, ~media~, 7),
    (~faltan_documentos~,     N~Faltan documentos~,                                  ~inspector~, ~media~, 8),
    (~camion_llego_tarde~,    N~El cami~ + NCHAR(243) + N~n lleg~ + NCHAR(243) + N~ tarde~, ~inspector~, ~media~, 9),
    (~otro~,                  N~Otro~,                                               ~todos~,     ~media~, 10)
) AS v (Codigo, Nombre, Rol, Urgencia, Orden)
WHERE NOT EXISTS (SELECT 1 FROM {S}.tblTiposNovedadViaje t WHERE t.Codigo = v.Codigo)'),
(N'tblNovedadesViaje: la tabla de la novedad del viaje', N'
IF OBJECT_ID(N~{S}.tblNovedadesViaje~, N~U~) IS NULL
BEGIN
    CREATE TABLE {S}.tblNovedadesViaje
    (
        Id                uniqueidentifier  NOT NULL CONSTRAINT PK_tblNovedadesViaje PRIMARY KEY,
        ViajeId           uniqueidentifier  NOT NULL,
        TipoCodigo        varchar(30)       NOT NULL,
        UsuarioId         uniqueidentifier  NOT NULL,
        Rol               varchar(10)       NOT NULL,
        Proceso           varchar(10)       NULL,
        LugarId           uniqueidentifier  NULL,
        Texto             nvarchar(300)     NOT NULL,
        Fecha             datetimeoffset(3) NOT NULL,
        Lat               float             NULL,
        Lng               float             NULL,
        IdEnvio           uniqueidentifier  NULL,
        FotoStoragePath   nvarchar(500)     NULL,
        FotoSelloFecha    datetimeoffset(3) NULL,
        FotoSelloLat      float             NULL,
        FotoSelloLng      float             NULL,
        FotoPrecision     float             NULL,
        FotoDispositivoId nvarchar(100)     NULL,
        FotoHashSha256    char(64)          NULL,
        CreatedAt         datetimeoffset(3) NOT NULL CONSTRAINT DF_tblNovedadesViaje_CreatedAt DEFAULT (SYSDATETIMEOFFSET()),
        CONSTRAINT FK_tblNovedadesViaje_Viaje FOREIGN KEY (ViajeId) REFERENCES {S}.tblViajes (Id),
        CONSTRAINT FK_tblNovedadesViaje_Tipo FOREIGN KEY (TipoCodigo) REFERENCES {S}.tblTiposNovedadViaje (Codigo),
        CONSTRAINT FK_tblNovedadesViaje_Usuario FOREIGN KEY (UsuarioId) REFERENCES {S}.tblUsuarios (Id),
        CONSTRAINT FK_tblNovedadesViaje_Lugar FOREIGN KEY (LugarId) REFERENCES {S}.tblLugares (Id),
        CONSTRAINT CK_tblNovedadesViaje_Rol CHECK (Rol IN (~conductor~, ~inspector~)),
        CONSTRAINT CK_tblNovedadesViaje_Proceso CHECK (Proceso IS NULL OR Proceso IN (~cargue~, ~entrega~)),
        CONSTRAINT CK_tblNovedadesViaje_RolProceso CHECK (
            (Rol = ~conductor~ AND Proceso IS NULL AND LugarId IS NULL)
            OR (Rol = ~inspector~ AND Proceso IS NOT NULL AND ((Proceso = ~entrega~ AND LugarId IS NOT NULL) OR (Proceso = ~cargue~ AND LugarId IS NULL)))),
        CONSTRAINT CK_tblNovedadesViaje_Texto CHECK (LEN(LTRIM(RTRIM(Texto))) > 0),
        CONSTRAINT CK_tblNovedadesViaje_Ubicacion CHECK ((Lat IS NULL AND Lng IS NULL) OR (Lat IS NOT NULL AND Lng IS NOT NULL AND Lat BETWEEN -90 AND 90 AND Lng BETWEEN -180 AND 180)),
        CONSTRAINT CK_tblNovedadesViaje_Foto CHECK (
            (FotoStoragePath IS NULL AND FotoSelloFecha IS NULL AND FotoSelloLat IS NULL AND FotoSelloLng IS NULL AND FotoPrecision IS NULL AND FotoDispositivoId IS NULL AND FotoHashSha256 IS NULL)
            OR (FotoStoragePath IS NOT NULL AND FotoSelloFecha IS NOT NULL AND FotoSelloLat IS NOT NULL AND FotoSelloLng IS NOT NULL AND FotoHashSha256 IS NOT NULL)),
        CONSTRAINT CK_tblNovedadesViaje_FotoSello CHECK (
            FotoStoragePath IS NULL
            OR (FotoSelloLat BETWEEN -90 AND 90 AND FotoSelloLng BETWEEN -180 AND 180 AND (FotoPrecision IS NULL OR FotoPrecision >= 0) AND FotoHashSha256 NOT LIKE ~%[^0-9a-f]%~))
    );
END'),
(N'tblNovedadesViaje: un envio por viaje y la lectura por viaje', N'
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N~UX_tblNovedadesViaje_Envio~ AND object_id = OBJECT_ID(N~{S}.tblNovedadesViaje~))
    CREATE UNIQUE INDEX UX_tblNovedadesViaje_Envio ON {S}.tblNovedadesViaje (ViajeId, IdEnvio) WHERE IdEnvio IS NOT NULL;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N~IX_tblNovedadesViaje_Viaje~ AND object_id = OBJECT_ID(N~{S}.tblNovedadesViaje~))
    CREATE INDEX IX_tblNovedadesViaje_Viaje ON {S}.tblNovedadesViaje (ViajeId, Fecha)'),
(N'tblNovedadesViaje: no se modifica', N'
CREATE OR ALTER TRIGGER {S}.TR_tblNovedadesViaje_NoModificar
ON {S}.tblNovedadesViaje
INSTEAD OF UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 51005, ~Una novedad del viaje no se modifica~, 1;
END'),
(N'tblNovedadesViaje: no se borra', N'
CREATE OR ALTER TRIGGER {S}.TR_tblNovedadesViaje_NoBorrar
ON {S}.tblNovedadesViaje
INSTEAD OF DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 51003, ~Una novedad del viaje es evidencia y no se puede borrar~, 1;
END'),
(N'tblHallazgos: IdEnvio (el envio de la novedad de un carro)', N'
IF COL_LENGTH(N~{S}.tblHallazgos~, N~IdEnvio~) IS NULL
    ALTER TABLE {S}.tblHallazgos ADD IdEnvio uniqueidentifier NULL'),
(N'tblHallazgos: un envio por inspeccion', N'
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N~UX_tblHallazgos_Envio~ AND object_id = OBJECT_ID(N~{S}.tblHallazgos~))
    CREATE UNIQUE INDEX UX_tblHallazgos_Envio ON {S}.tblHallazgos (InspeccionId, IdEnvio) WHERE IdEnvio IS NOT NULL'),
(N'tblNotificaciones: tipo novedad_viaje', N'
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N~CK_tblNotificaciones_Tipo~ AND parent_object_id = OBJECT_ID(N~{S}.tblNotificaciones~) AND definition LIKE N~%novedad_viaje%~)
BEGIN
    -- El tipo nuevo se suma a los que la restriccion ya admite, sean los que sean: no se reescribe la lista y ningun aviso existente queda sin tipo valido.
    DECLARE @actual nvarchar(max) = (SELECT definition FROM sys.check_constraints WHERE name = N~CK_tblNotificaciones_Tipo~ AND parent_object_id = OBJECT_ID(N~{S}.tblNotificaciones~));
    IF @actual IS NULL
        THROW 50002, N~Falta CK_tblNotificaciones_Tipo en tblNotificaciones: revise que los scripts anteriores esten aplicados.~, 1;
    DECLARE @nueva nvarchar(max) = N~ALTER TABLE {S}.tblNotificaciones DROP CONSTRAINT CK_tblNotificaciones_Tipo; ALTER TABLE {S}.tblNotificaciones ADD CONSTRAINT CK_tblNotificaciones_Tipo CHECK ~ + LEFT(@actual, LEN(@actual) - 1) + N~ OR [Tipo] = ~~novedad_viaje~~)~;
    EXEC (@nueva);
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

-- Resultado (esperado: TiposDeNovedad = 10, TablaDeNovedades = 1, RestriccionesDeLaNovedad = 7, LlavesDeLaNovedad = 4, EnvioUnicoPorViaje = 1, EnvioDeLaNovedadDelCarro = 1, ProtegidaContraCambios = 2, TipoDeAviso = 1).
DECLARE @cuentas nvarchar(max) = REPLACE(N'
SELECT
    (SELECT COUNT(*) FROM {S}.tblTiposNovedadViaje WHERE Codigo IN (N~falla_camion~, N~dano_ninera~, N~bloqueo_via~, N~intento_robo~, N~accidente~, N~carro_no_llego_patio~, N~puesto_ninera_danado~, N~faltan_documentos~, N~camion_llego_tarde~, N~otro~)) AS TiposDeNovedad,
    (SELECT COUNT(*) FROM sys.tables WHERE object_id = OBJECT_ID(N~{S}.tblNovedadesViaje~)) AS TablaDeNovedades,
    (SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N~{S}.tblNovedadesViaje~) AND name LIKE N~CK[_]tblNovedadesViaje[_]%~) AS RestriccionesDeLaNovedad,
    (SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID(N~{S}.tblNovedadesViaje~)) AS LlavesDeLaNovedad,
    (SELECT COUNT(*) FROM sys.indexes WHERE name = N~UX_tblNovedadesViaje_Envio~ AND object_id = OBJECT_ID(N~{S}.tblNovedadesViaje~) AND is_unique = 1 AND has_filter = 1) AS EnvioUnicoPorViaje,
    (SELECT COUNT(*) FROM sys.indexes WHERE name = N~UX_tblHallazgos_Envio~ AND object_id = OBJECT_ID(N~{S}.tblHallazgos~) AND is_unique = 1 AND has_filter = 1) AS EnvioDeLaNovedadDelCarro,
    (SELECT COUNT(*) FROM sys.triggers WHERE name IN (N~TR_tblNovedadesViaje_NoModificar~, N~TR_tblNovedadesViaje_NoBorrar~)) AS ProtegidaContraCambios,
    (SELECT COUNT(*) FROM sys.check_constraints WHERE name = N~CK_tblNotificaciones_Tipo~ AND parent_object_id = OBJECT_ID(N~{S}.tblNotificaciones~) AND definition LIKE N~%novedad_viaje%~) AS TipoDeAviso;', N'{S}', @q);
SET @cuentas = REPLACE(@cuentas, N'~', N'''');
EXEC (@cuentas);
