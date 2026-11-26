/* =============================================================================
   Muelles, destino cambiado y avisos del patio, para una base de IA Autos YA EXISTENTE. Se ejecuta UNA vez (si ya esta aplicado, no cambia nada) y todo ocurre en UNA transaccion: si algo falla se revierte completo.

   Que hace:
     1. tblMuelles: el catalogo de muelles de cada lugar (LugarId, Codigo unico dentro del lugar, Nombre, Orden, Activo). No siembra ninguno: los muelles de cada lugar los carga el negocio desde Catalogos.
     2. tblViajes.MuelleId (con su llave foranea): el muelle del viaje, nulo mientras nadie lo asigna.
     3. tblViajeVehiculos.LugarDestinoCambiadoAt: cuando se cambio por ultima vez el destino de la unidad (nulo si nunca).
     4. tblNotificaciones: CK_tblNotificaciones_Tipo admite los tipos cambio_cargue_inspector y cambio_destino_unidad, sin quitar ninguno de los catorce existentes.

   Seguridad de los datos: solo agrega una tabla, dos columnas nulas, una llave foranea y dos tipos de aviso; no cambia ningun dato. Se ejecuta ANTES de desplegar la API.

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
IF OBJECT_ID(@q + N'.tblViajes', N'U') IS NULL OR OBJECT_ID(@q + N'.tblViajeVehiculos', N'U') IS NULL OR OBJECT_ID(@q + N'.tblLugares', N'U') IS NULL OR OBJECT_ID(@q + N'.tblNotificaciones', N'U') IS NULL
    THROW 50001, N'Faltan tblViajes, tblViajeVehiculos, tblLugares o tblNotificaciones de IA Autos en el esquema indicado: revise @esquema.', 1;

-- Pasos en orden. {S} = esquema entre corchetes; ~ = comilla simple dentro del texto.
DECLARE @paso TABLE (n int IDENTITY(1, 1) PRIMARY KEY, descripcion nvarchar(100), s nvarchar(max));

INSERT @paso (descripcion, s) VALUES
(N'tblMuelles: la tabla', N'
IF OBJECT_ID(N~{S}.tblMuelles~, N~U~) IS NULL
    CREATE TABLE {S}.tblMuelles
    (
        Id        uniqueidentifier  NOT NULL CONSTRAINT PK_tblMuelles PRIMARY KEY,
        LugarId   uniqueidentifier  NOT NULL,
        Codigo    varchar(30)       NOT NULL,
        Nombre    nvarchar(100)     NOT NULL,
        Orden     int               NOT NULL CONSTRAINT DF_tblMuelles_Orden DEFAULT (0),
        Activo    bit               NOT NULL CONSTRAINT DF_tblMuelles_Activo DEFAULT (1),
        CreatedAt datetimeoffset(3) NOT NULL CONSTRAINT DF_tblMuelles_CreatedAt DEFAULT (SYSDATETIMEOFFSET()),
        UpdatedAt datetimeoffset(3) NOT NULL CONSTRAINT DF_tblMuelles_UpdatedAt DEFAULT (SYSDATETIMEOFFSET()),
        CONSTRAINT FK_tblMuelles_Lugar FOREIGN KEY (LugarId) REFERENCES {S}.tblLugares (Id),
        CONSTRAINT UQ_tblMuelles_Lugar_Codigo UNIQUE (LugarId, Codigo),
        CONSTRAINT CK_tblMuelles_Nombre CHECK (LEN(LTRIM(RTRIM(Nombre))) > 0)
    )'),
(N'tblViajes: MuelleId', N'
IF COL_LENGTH(N~{S}.tblViajes~, N~MuelleId~) IS NULL
    ALTER TABLE {S}.tblViajes ADD MuelleId uniqueidentifier NULL'),
(N'tblViajes: llave foranea del muelle', N'
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N~FK_tblViajes_Muelle~ AND parent_object_id = OBJECT_ID(N~{S}.tblViajes~))
    ALTER TABLE {S}.tblViajes ADD CONSTRAINT FK_tblViajes_Muelle FOREIGN KEY (MuelleId) REFERENCES {S}.tblMuelles (Id)'),
(N'tblViajeVehiculos: LugarDestinoCambiadoAt', N'
IF COL_LENGTH(N~{S}.tblViajeVehiculos~, N~LugarDestinoCambiadoAt~) IS NULL
    ALTER TABLE {S}.tblViajeVehiculos ADD LugarDestinoCambiadoAt datetimeoffset(3) NULL'),
(N'tblNotificaciones: tipos cambio_cargue_inspector y cambio_destino_unidad', N'
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N~CK_tblNotificaciones_Tipo~ AND parent_object_id = OBJECT_ID(N~{S}.tblNotificaciones~) AND definition LIKE N~%cambio_destino_unidad%~)
BEGIN
    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N~CK_tblNotificaciones_Tipo~ AND parent_object_id = OBJECT_ID(N~{S}.tblNotificaciones~))
        ALTER TABLE {S}.tblNotificaciones DROP CONSTRAINT CK_tblNotificaciones_Tipo;
    ALTER TABLE {S}.tblNotificaciones ADD CONSTRAINT CK_tblNotificaciones_Tipo CHECK (Tipo IN (~novedad_conductor~, ~respuesta_conductor~, ~hallazgo_critico~, ~umbral_patron~, ~captura_incompleta~, ~transferencia_pendiente~, ~reporte_diario~, ~novedad_proceso~, ~reclamo_sin_actividad~, ~fuera_de_rango~, ~cargue_sin_reclamar~, ~propuesta_ubicacion~, ~permiso_sin_inspector~, ~firma_cargue_pendiente~, ~cambio_cargue_inspector~, ~cambio_destino_unidad~));
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

-- Resultado (esperado: Tabla = 1, MuelleDelViaje = 1, LlaveDelMuelle = 1, DestinoCambiado = 1, TiposDeAviso = 1).
DECLARE @cuentas nvarchar(max) = REPLACE(N'
SELECT
    (SELECT COUNT(*) FROM sys.tables WHERE object_id = OBJECT_ID(N~{S}.tblMuelles~)) AS Tabla,
    (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N~{S}.tblViajes~) AND name = N~MuelleId~) AS MuelleDelViaje,
    (SELECT COUNT(*) FROM sys.foreign_keys WHERE name = N~FK_tblViajes_Muelle~) AS LlaveDelMuelle,
    (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N~{S}.tblViajeVehiculos~) AND name = N~LugarDestinoCambiadoAt~) AS DestinoCambiado,
    (SELECT COUNT(*) FROM sys.check_constraints WHERE name = N~CK_tblNotificaciones_Tipo~ AND parent_object_id = OBJECT_ID(N~{S}.tblNotificaciones~) AND definition LIKE N~%cambio_cargue_inspector%~ AND definition LIKE N~%cambio_destino_unidad%~) AS TiposDeAviso;', N'{S}', @q);
SET @cuentas = REPLACE(@cuentas, N'~', N'''');
EXEC (@cuentas);
