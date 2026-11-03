/* =============================================================================
   Ubicacion con rango y confirmacion en sitio, para una base de IA Autos YA EXISTENTE (con 31-extras-del-vehiculo.sql aplicado). Se ejecuta UNA vez (si ya esta aplicado, no cambia nada) y todo ocurre
   en UNA transaccion: si algo falla se revierte completo.

   Que hace:
     1. tblParametros: agrega radio_lugar_cargue_m (1000), radio_destino_m (500), valvula_precision_m (500) y cargue_sin_reclamar_minutos (30), todos de tipo numero. Son valores ilustrativos que confirma
        el negocio y se ajustan sin desplegar (PATCH /api/parametros/{clave}). Una fila que ya existe nunca se sobrescribe. No toca gps_precision_minima_m (30 m): ese es el aviso de precision baja, distinto
        de la valvula de 500 m.
     2. tblLugares: agrega EstadoUbicacion (sin_ubicacion, pendiente_confirmar o confirmada; los lugares que ya tienen coordenadas quedan confirmada y los demas sin_ubicacion), UbicacionFijadaPor y
        UbicacionFijadaAt (quien fijo la ubicacion y cuando; nulos en los que ya existian).
     3. tblInspecciones: agrega ConfirmacionSitio (confirmado_en_sitio, fuera_de_rango o sin_confirmacion_posible; nulo mientras no se determina), ConfirmacionDistanciaM (la distancia calculada, en metros) y
        ConfirmacionEvidenciaId (la foto que lo determino). Se escriben mientras la inspeccion esta en borrador: una cerrada es inmutable (trigger existente).
     4. tblNotificaciones: CK_tblNotificaciones_Tipo admite los tipos fuera_de_rango y cargue_sin_reclamar, sin quitar ninguno de los nueve existentes.
     5. tblPropuestasUbicacion: las propuestas de autocalibracion de un lugar: centro y radio propuestos, cuantas fotos y visitas los sustentan, el estado (pendiente, confirmada o descartada) y la decision con
        su autor y su fecha. Un lugar solo tiene una propuesta pendiente a la vez y una propuesta decidida no se modifica ni se borra (error 51010).

   Seguridad de los datos: no cambia ningun dato existente salvo marcar como confirmada la ubicacion de los lugares que ya tenian coordenadas; todo lo demas nace vacio o con su valor inicial.

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
IF OBJECT_ID(@q + N'.tblParametros', N'U') IS NULL
    THROW 50001, N'Falta la tabla tblParametros de IA Autos en el esquema indicado: revise @esquema.', 1;
IF OBJECT_ID(@q + N'.tblLugares', N'U') IS NULL
    THROW 50002, N'Falta la tabla tblLugares de IA Autos (la crea 24-lugares-y-programacion.sql) en el esquema indicado: revise @esquema.', 1;
IF OBJECT_ID(@q + N'.tblInspecciones', N'U') IS NULL
    THROW 50003, N'Falta la tabla tblInspecciones de IA Autos en el esquema indicado: revise @esquema.', 1;
IF OBJECT_ID(@q + N'.tblNotificaciones', N'U') IS NULL
    THROW 50004, N'Falta la tabla tblNotificaciones de IA Autos en el esquema indicado: revise @esquema.', 1;

-- Pasos en orden. {S} = esquema entre corchetes; ~ = comilla simple dentro del texto (~~ dentro de un EXEC anidado).
DECLARE @paso TABLE (n int IDENTITY(1, 1) PRIMARY KEY, descripcion nvarchar(100), s nvarchar(max));

INSERT @paso (descripcion, s) VALUES
(N'parametros del rango y del cargue sin reclamar', N'
INSERT INTO {S}.tblParametros (Clave, Valor, Tipo, Descripcion)
SELECT v.Clave, v.Valor, v.Tipo, v.Descripcion
FROM (VALUES
    (~radio_lugar_cargue_m~,       N~1000~, ~numero~, N~Radio, en metros, alrededor de un lugar de cargue dentro del cual la primera foto sellada confirma que el inspector esta en sitio (el valor definitivo lo confirma el negocio)~),
    (~radio_destino_m~,            N~500~,  ~numero~, N~Radio, en metros, alrededor de un destino dentro del cual la primera foto sellada confirma que se esta en sitio (el valor definitivo lo confirma el negocio)~),
    (~valvula_precision_m~,        N~500~,  ~numero~, N~Precision del GPS, en metros, a partir de la cual una foto no confirma ni acusa la ubicacion (valvula; distinta de gps_precision_minima_m, que solo avisa de precision baja)~),
    (~cargue_sin_reclamar_minutos~, N~30~,  ~numero~, N~Minutos desde la hora programada de un cargue sin ningun reclamo antes de avisar al coordinador; no se repite (el valor definitivo lo confirma el negocio)~)
) AS v (Clave, Valor, Tipo, Descripcion)
WHERE NOT EXISTS (SELECT 1 FROM {S}.tblParametros p WHERE p.Clave = v.Clave)'),
(N'tblLugares: estado de la ubicacion y quien la fijo', N'
IF COL_LENGTH(N~{S}.tblLugares~, N~EstadoUbicacion~) IS NULL
BEGIN
    ALTER TABLE {S}.tblLugares ADD EstadoUbicacion varchar(25) NOT NULL CONSTRAINT DF_tblLugares_EstadoUbicacion DEFAULT (~sin_ubicacion~);
    ALTER TABLE {S}.tblLugares ADD UbicacionFijadaPor uniqueidentifier NULL;
    ALTER TABLE {S}.tblLugares ADD UbicacionFijadaAt datetimeoffset(3) NULL;
    EXEC (N~UPDATE {S}.tblLugares SET EstadoUbicacion = ~~confirmada~~ WHERE Latitud IS NOT NULL~);
END'),
(N'tblLugares: los tres estados de la ubicacion', N'
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N~CK_tblLugares_EstadoUbicacion~ AND parent_object_id = OBJECT_ID(N~{S}.tblLugares~))
    ALTER TABLE {S}.tblLugares ADD CONSTRAINT CK_tblLugares_EstadoUbicacion CHECK (EstadoUbicacion IN (~sin_ubicacion~, ~pendiente_confirmar~, ~confirmada~))'),
(N'tblLugares: quien fijo la ubicacion', N'
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N~FK_tblLugares_UbicacionFijadaPor~ AND parent_object_id = OBJECT_ID(N~{S}.tblLugares~))
    ALTER TABLE {S}.tblLugares ADD CONSTRAINT FK_tblLugares_UbicacionFijadaPor FOREIGN KEY (UbicacionFijadaPor) REFERENCES {S}.tblUsuarios (Id)'),
(N'tblInspecciones: resultado de la confirmacion en sitio', N'
IF COL_LENGTH(N~{S}.tblInspecciones~, N~ConfirmacionSitio~) IS NULL
BEGIN
    ALTER TABLE {S}.tblInspecciones ADD ConfirmacionSitio varchar(25) NULL;
    ALTER TABLE {S}.tblInspecciones ADD ConfirmacionDistanciaM float NULL;
    ALTER TABLE {S}.tblInspecciones ADD ConfirmacionEvidenciaId uniqueidentifier NULL;
END'),
(N'tblInspecciones: los tres resultados y su distancia', N'
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N~CK_tblInspecciones_ConfirmacionSitio~ AND parent_object_id = OBJECT_ID(N~{S}.tblInspecciones~))
    ALTER TABLE {S}.tblInspecciones ADD CONSTRAINT CK_tblInspecciones_ConfirmacionSitio CHECK (ConfirmacionSitio IS NULL OR ConfirmacionSitio IN (~confirmado_en_sitio~, ~fuera_de_rango~, ~sin_confirmacion_posible~))'),
(N'tblInspecciones: la distancia y la foto solo con un resultado', N'
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N~CK_tblInspecciones_ConfirmacionDatos~ AND parent_object_id = OBJECT_ID(N~{S}.tblInspecciones~))
    ALTER TABLE {S}.tblInspecciones ADD CONSTRAINT CK_tblInspecciones_ConfirmacionDatos CHECK (ConfirmacionSitio IS NOT NULL OR (ConfirmacionDistanciaM IS NULL AND ConfirmacionEvidenciaId IS NULL))'),
(N'tblInspecciones: la distancia no es negativa', N'
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N~CK_tblInspecciones_ConfirmacionDistancia~ AND parent_object_id = OBJECT_ID(N~{S}.tblInspecciones~))
    ALTER TABLE {S}.tblInspecciones ADD CONSTRAINT CK_tblInspecciones_ConfirmacionDistancia CHECK (ConfirmacionDistanciaM IS NULL OR ConfirmacionDistanciaM >= 0)'),
(N'tblInspecciones: la foto que determino la confirmacion', N'
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N~FK_tblInspecciones_ConfirmacionEvidencia~ AND parent_object_id = OBJECT_ID(N~{S}.tblInspecciones~))
    ALTER TABLE {S}.tblInspecciones ADD CONSTRAINT FK_tblInspecciones_ConfirmacionEvidencia FOREIGN KEY (ConfirmacionEvidenciaId) REFERENCES {S}.tblEvidencias (Id)'),
(N'tblNotificaciones: tipos fuera_de_rango y cargue_sin_reclamar', N'
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N~CK_tblNotificaciones_Tipo~ AND parent_object_id = OBJECT_ID(N~{S}.tblNotificaciones~) AND definition LIKE N~%cargue_sin_reclamar%~)
BEGIN
    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N~CK_tblNotificaciones_Tipo~ AND parent_object_id = OBJECT_ID(N~{S}.tblNotificaciones~))
        ALTER TABLE {S}.tblNotificaciones DROP CONSTRAINT CK_tblNotificaciones_Tipo;
    ALTER TABLE {S}.tblNotificaciones ADD CONSTRAINT CK_tblNotificaciones_Tipo CHECK (Tipo IN (~novedad_conductor~, ~respuesta_conductor~, ~hallazgo_critico~, ~umbral_patron~, ~captura_incompleta~, ~transferencia_pendiente~, ~reporte_diario~, ~novedad_proceso~, ~reclamo_sin_actividad~, ~fuera_de_rango~, ~cargue_sin_reclamar~));
END'),
(N'tblPropuestasUbicacion', N'
IF OBJECT_ID(N~{S}.tblPropuestasUbicacion~, N~U~) IS NULL
BEGIN
    CREATE TABLE {S}.tblPropuestasUbicacion
    (
        Id           uniqueidentifier  NOT NULL CONSTRAINT PK_tblPropuestasUbicacion PRIMARY KEY,
        LugarId      uniqueidentifier  NOT NULL,
        CentroLat    float             NOT NULL,
        CentroLng    float             NOT NULL,
        RadioMetros  int               NOT NULL,
        Fotos        int               NOT NULL,
        Visitas      int               NOT NULL,
        Estado       varchar(15)       NOT NULL CONSTRAINT DF_tblPropuestasUbicacion_Estado DEFAULT (~pendiente~),
        DecididaPor  uniqueidentifier  NULL,
        DecididaAt   datetimeoffset(3) NULL,
        CreatedAt    datetimeoffset(3) NOT NULL CONSTRAINT DF_tblPropuestasUbicacion_CreatedAt DEFAULT (SYSDATETIMEOFFSET()),
        UpdatedAt    datetimeoffset(3) NOT NULL CONSTRAINT DF_tblPropuestasUbicacion_UpdatedAt DEFAULT (SYSDATETIMEOFFSET()),
        CONSTRAINT FK_tblPropuestasUbicacion_Lugar FOREIGN KEY (LugarId) REFERENCES {S}.tblLugares (Id),
        CONSTRAINT FK_tblPropuestasUbicacion_DecididaPor FOREIGN KEY (DecididaPor) REFERENCES {S}.tblUsuarios (Id),
        CONSTRAINT CK_tblPropuestasUbicacion_Estado CHECK (Estado IN (~pendiente~, ~confirmada~, ~descartada~)),
        CONSTRAINT CK_tblPropuestasUbicacion_Lat CHECK (CentroLat BETWEEN -90 AND 90),
        CONSTRAINT CK_tblPropuestasUbicacion_Lng CHECK (CentroLng BETWEEN -180 AND 180),
        CONSTRAINT CK_tblPropuestasUbicacion_Radio CHECK (RadioMetros > 0),
        CONSTRAINT CK_tblPropuestasUbicacion_Conteos CHECK (Fotos >= 0 AND Visitas >= 0),
        -- Una propuesta pendiente no tiene decision; una decidida siempre trae su autor y su fecha.
        CONSTRAINT CK_tblPropuestasUbicacion_Decision CHECK ((Estado = ~pendiente~ AND DecididaPor IS NULL AND DecididaAt IS NULL) OR (Estado <> ~pendiente~ AND DecididaPor IS NOT NULL AND DecididaAt IS NOT NULL))
    );
    CREATE INDEX IX_tblPropuestasUbicacion_Lugar ON {S}.tblPropuestasUbicacion (LugarId);
    CREATE UNIQUE INDEX UX_tblPropuestasUbicacion_Pendiente ON {S}.tblPropuestasUbicacion (LugarId) WHERE Estado = ~pendiente~;
END'),
(N'tblPropuestasUbicacion: una decidida no se modifica', N'
CREATE OR ALTER TRIGGER {S}.TR_tblPropuestasUbicacion_NoModificarDecidida
ON {S}.tblPropuestasUbicacion
AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM deleted WHERE Estado <> ~pendiente~)
        THROW 51010, ~Una propuesta de ubicacion ya decidida no se modifica ni se borra~, 1;
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

-- Resultado (esperado en una base recien migrada: Parametros = 4, ColumnasLugar = 3, ColumnasInspeccion = 3, TipoNotificacion = 1, Propuestas = 1 y Trigger = 1).
DECLARE @cuentas nvarchar(max) = REPLACE(N'
SELECT
    (SELECT COUNT(*) FROM {S}.tblParametros WHERE Clave IN (N~radio_lugar_cargue_m~, N~radio_destino_m~, N~valvula_precision_m~, N~cargue_sin_reclamar_minutos~) AND Tipo = N~numero~) AS Parametros,
    (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N~{S}.tblLugares~) AND name IN (N~EstadoUbicacion~, N~UbicacionFijadaPor~, N~UbicacionFijadaAt~)) AS ColumnasLugar,
    (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N~{S}.tblInspecciones~) AND name IN (N~ConfirmacionSitio~, N~ConfirmacionDistanciaM~, N~ConfirmacionEvidenciaId~)) AS ColumnasInspeccion,
    (SELECT COUNT(*) FROM sys.check_constraints WHERE name = N~CK_tblNotificaciones_Tipo~ AND parent_object_id = OBJECT_ID(N~{S}.tblNotificaciones~) AND definition LIKE N~%cargue_sin_reclamar%~ AND definition LIKE N~%fuera_de_rango%~) AS TipoNotificacion,
    (SELECT COUNT(*) FROM sys.tables WHERE name = N~tblPropuestasUbicacion~ AND schema_id = SCHEMA_ID(N~{E}~)) AS Propuestas,
    (SELECT COUNT(*) FROM sys.triggers WHERE name = N~TR_tblPropuestasUbicacion_NoModificarDecidida~) AS Trigger_', N'{S}', @q);
SET @cuentas = REPLACE(REPLACE(@cuentas, N'{E}', @esquema), N'~', N'''');
EXEC (@cuentas);
