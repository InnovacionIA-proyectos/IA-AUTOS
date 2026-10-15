/* =============================================================================
   Inspeccion de salida y de llegada con fotos selladas, para una base de IA Autos YA EXISTENTE (con 06-identidad.sql aplicado).
   Se ejecuta UNA vez (si ya esta aplicado, no hace nada) y todo ocurre en UNA transaccion: si algo falla se revierte completo.

   Que hace:
     1. tblInspecciones: el momento pasa a ser 'salida' o 'llegada' (ya no check_in, check_out ni informe) y gana IniciadaAt, CerradaAt,
        Version y CorrigeInspeccionId. La restriccion UNIQUE (EtapaId, Momento) se reemplaza por dos indices unicos filtrados: una sola
        inspeccion ORIGINAL por etapa y momento, y cada version se corrige una sola vez.
     2. Datos historicos: 'informe' y 'check_in' pasan a 'salida' y 'check_out' pasa a 'llegada'. Si una etapa tuviera varias
        inspecciones para el mismo momento nuevo (por ejemplo un informe y un check_in), la mas antigua queda como la original y las
        demas como versiones siguientes que la corrigen, en orden de fecha: no se borra ni se pierde ninguna.
        IniciadaAt toma la fecha de creacion y CerradaAt (solo de las cerradas) la ultima modificacion.
     3. tblEvidencias: gana VistaId (la vista de foto), HashSha256, SelloServidorAt, Precision, DispositivoId y Nota, y SelloLat y SelloLng
        pasan a ser obligatorios. Las fotos historicas sin ubicacion quedan en (0, 0), que la API muestra como `sin ubicacion`;
        SelloServidorAt toma la fecha de creacion de cada foto.

   Seguridad de los datos: no borra inspecciones, hallazgos ni fotos. Las inspecciones cerradas siguen cerradas: el trigger de
   inmutabilidad se desactiva SOLO mientras se actualizan (momento, hora de cierre y version) y se vuelve a activar dentro de la misma
   transaccion. Aborta (sin cambiar nada) si falta la columna tblUsuarios.ClienteId (06-identidad.sql) o la restriccion
   UQ_tblInspecciones_Etapa_Momento que se reemplaza.

   Como ejecutarlo: abrir en SSMS, DBeaver o sqlcmd CONECTADO a la base de IA Autos, ajustar @esquema y ejecutar completo con un
   usuario con permiso para alterar tablas y desactivar triggers (no el usuario de la aplicacion). Ejecutarlo ANTES de desplegar la API
   que usa inspecciones de salida y de llegada.
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
IF OBJECT_ID(@q + N'.tblInspecciones', N'U') IS NULL OR OBJECT_ID(@q + N'.tblEvidencias', N'U') IS NULL OR OBJECT_ID(@q + N'.tblVistasFoto', N'U') IS NULL
    THROW 50001, N'Faltan tablas de IA Autos en el esquema indicado: revise @esquema.', 1;

IF COL_LENGTH(@q + N'.tblInspecciones', N'IniciadaAt') IS NOT NULL
BEGIN
    PRINT N'Las inspecciones de salida y de llegada ya estan aplicadas: no se hace nada.';
    RETURN;
END

IF COL_LENGTH(@q + N'.tblUsuarios', N'ClienteId') IS NULL
    THROW 50002, N'Falta tblUsuarios.ClienteId: ejecute antes 06-identidad.sql.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'UQ_tblInspecciones_Etapa_Momento' AND parent_object_id = OBJECT_ID(@q + N'.tblInspecciones'))
    THROW 50003, N'No existe la restriccion UQ_tblInspecciones_Etapa_Momento: la base no esta en el estado previo esperado para esta migracion.', 1;

-- Pasos en orden. {S} = esquema entre corchetes; ~ = comilla simple dentro del texto.
DECLARE @paso TABLE (n int IDENTITY(1, 1) PRIMARY KEY, descripcion nvarchar(100), s nvarchar(max));

INSERT @paso (descripcion, s) VALUES
(N'trigger de inmutabilidad: se desactiva mientras se migra', N'IF EXISTS (SELECT 1 FROM sys.triggers WHERE name = ~TR_tblInspecciones_Inmutable~ AND parent_id = OBJECT_ID(~{S}.tblInspecciones~)) DISABLE TRIGGER {S}.TR_tblInspecciones_Inmutable ON {S}.tblInspecciones'),
(N'tblInspecciones: columnas nuevas', N'
ALTER TABLE {S}.tblInspecciones ADD
    IniciadaAt          datetimeoffset(3) NOT NULL CONSTRAINT DF_tblInspecciones_IniciadaAt DEFAULT (SYSDATETIMEOFFSET()),
    CerradaAt           datetimeoffset(3) NULL,
    Version             int               NOT NULL CONSTRAINT DF_tblInspecciones_Version DEFAULT (1),
    CorrigeInspeccionId uniqueidentifier  NULL'),
(N'tblInspecciones: se quitan la restriccion unica y el check del momento anteriores', N'
ALTER TABLE {S}.tblInspecciones DROP CONSTRAINT UQ_tblInspecciones_Etapa_Momento;
ALTER TABLE {S}.tblInspecciones DROP CONSTRAINT CK_tblInspecciones_Momento'),
(N'tblInspecciones: hora de inicio y de cierre de las historicas', N'
UPDATE {S}.tblInspecciones SET IniciadaAt = CreatedAt, CerradaAt = CASE WHEN Estado = ~cerrada~ THEN UpdatedAt ELSE NULL END'),
(N'tblInspecciones: momento nuevo y versiones de las historicas', N'
WITH m AS (
    SELECT Id, EtapaId,
           NuevoMomento = CASE Momento WHEN ~check_out~ THEN ~llegada~ ELSE ~salida~ END,
           rn = ROW_NUMBER() OVER (PARTITION BY EtapaId, CASE Momento WHEN ~check_out~ THEN ~llegada~ ELSE ~salida~ END ORDER BY Fecha, CreatedAt, Id)
    FROM {S}.tblInspecciones
), c AS (
    SELECT Id, NuevoMomento, rn, Anterior = LAG(Id) OVER (PARTITION BY EtapaId, NuevoMomento ORDER BY rn)
    FROM m
)
UPDATE i SET Momento = c.NuevoMomento, Version = c.rn, CorrigeInspeccionId = c.Anterior
FROM {S}.tblInspecciones i JOIN c ON c.Id = i.Id'),
(N'tblInspecciones: restricciones e indices nuevos', N'
ALTER TABLE {S}.tblInspecciones ADD CONSTRAINT FK_tblInspecciones_Corrige FOREIGN KEY (CorrigeInspeccionId) REFERENCES {S}.tblInspecciones (Id);
ALTER TABLE {S}.tblInspecciones ADD CONSTRAINT CK_tblInspecciones_Momento CHECK (Momento IN (~salida~, ~llegada~));
ALTER TABLE {S}.tblInspecciones ADD CONSTRAINT CK_tblInspecciones_Cierre CHECK ((Estado = ~borrador~ AND CerradaAt IS NULL) OR (Estado = ~cerrada~ AND CerradaAt IS NOT NULL));
ALTER TABLE {S}.tblInspecciones ADD CONSTRAINT CK_tblInspecciones_Version CHECK ((Version = 1 AND CorrigeInspeccionId IS NULL) OR (Version > 1 AND CorrigeInspeccionId IS NOT NULL));
CREATE UNIQUE INDEX UX_tblInspecciones_Etapa_Momento_Original ON {S}.tblInspecciones (EtapaId, Momento) WHERE CorrigeInspeccionId IS NULL;
CREATE UNIQUE INDEX UX_tblInspecciones_Corrige ON {S}.tblInspecciones (CorrigeInspeccionId) WHERE CorrigeInspeccionId IS NOT NULL'),
(N'tblEvidencias: las fotos historicas sin ubicacion quedan en (0, 0)', N'UPDATE {S}.tblEvidencias SET SelloLat = ISNULL(SelloLat, 0), SelloLng = ISNULL(SelloLng, 0) WHERE SelloLat IS NULL OR SelloLng IS NULL'),
(N'tblEvidencias: ubicacion obligatoria (se rehacen sus checks)', N'
ALTER TABLE {S}.tblEvidencias DROP CONSTRAINT CK_tblEvidencias_Lat;
ALTER TABLE {S}.tblEvidencias DROP CONSTRAINT CK_tblEvidencias_Lng;
ALTER TABLE {S}.tblEvidencias ALTER COLUMN SelloLat float NOT NULL;
ALTER TABLE {S}.tblEvidencias ALTER COLUMN SelloLng float NOT NULL;
ALTER TABLE {S}.tblEvidencias ADD CONSTRAINT CK_tblEvidencias_Lat CHECK (SelloLat BETWEEN -90 AND 90);
ALTER TABLE {S}.tblEvidencias ADD CONSTRAINT CK_tblEvidencias_Lng CHECK (SelloLng BETWEEN -180 AND 180)'),
(N'tblEvidencias: columnas nuevas', N'
ALTER TABLE {S}.tblEvidencias ADD
    VistaId         uniqueidentifier  NULL,
    HashSha256      char(64)          NULL,
    SelloServidorAt datetimeoffset(3) NOT NULL CONSTRAINT DF_tblEvidencias_SelloServidorAt DEFAULT (SYSDATETIMEOFFSET()),
    Precision       float             NULL,
    DispositivoId   nvarchar(100)     NULL,
    Nota            nvarchar(500)     NULL'),
(N'tblEvidencias: sello del servidor de las historicas', N'UPDATE {S}.tblEvidencias SET SelloServidorAt = CreatedAt'),
(N'tblEvidencias: restricciones e indice nuevos', N'
ALTER TABLE {S}.tblEvidencias ADD CONSTRAINT FK_tblEvidencias_Vista FOREIGN KEY (VistaId) REFERENCES {S}.tblVistasFoto (Id);
ALTER TABLE {S}.tblEvidencias ADD CONSTRAINT CK_tblEvidencias_Precision CHECK (Precision IS NULL OR Precision >= 0);
ALTER TABLE {S}.tblEvidencias ADD CONSTRAINT CK_tblEvidencias_Hash CHECK (HashSha256 IS NULL OR HashSha256 NOT LIKE ~%[^0-9a-f]%~);
CREATE INDEX IX_tblEvidencias_Vista ON {S}.tblEvidencias (VistaId)'),
(N'trigger de inmutabilidad: se vuelve a activar', N'IF EXISTS (SELECT 1 FROM sys.triggers WHERE name = ~TR_tblInspecciones_Inmutable~ AND parent_id = OBJECT_ID(~{S}.tblInspecciones~)) ENABLE TRIGGER {S}.TR_tblInspecciones_Inmutable ON {S}.tblInspecciones');

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

-- Resultado (esperado: ninguna inspeccion con momento distinto de salida o llegada; las versiones mayores a 1 solo aparecen si una etapa tenia varias para el mismo momento).
DECLARE @cuentas nvarchar(max) = REPLACE(N'
SELECT N~inspecciones de salida~ AS Dato, COUNT(*) AS Total FROM {S}.tblInspecciones WHERE Momento = ~salida~ UNION ALL
SELECT N~inspecciones de llegada~, COUNT(*) FROM {S}.tblInspecciones WHERE Momento = ~llegada~ UNION ALL
SELECT N~inspecciones cerradas~, COUNT(*) FROM {S}.tblInspecciones WHERE Estado = ~cerrada~ UNION ALL
SELECT N~versiones de correccion (mayores a 1)~, COUNT(*) FROM {S}.tblInspecciones WHERE Version > 1 UNION ALL
SELECT N~fotos~, COUNT(*) FROM {S}.tblEvidencias UNION ALL
SELECT N~fotos historicas sin ubicacion (0, 0)~, COUNT(*) FROM {S}.tblEvidencias WHERE SelloLat = 0 AND SelloLng = 0', N'{S}', @q);
SET @cuentas = REPLACE(@cuentas, N'~', N'''');
EXEC (@cuentas);
