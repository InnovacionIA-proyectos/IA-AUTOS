/* =============================================================================
   Capacidad y disposicion de la ninera, para una base de IA Autos YA EXISTENTE (con 37-dano-en-los-extras.sql aplicado). Se ejecuta UNA vez (si ya esta aplicado, no cambia nada) y todo ocurre en UNA
   transaccion: si algo falla se revierte completo.

   Que hace:
     1. tblViajes: columna Capacidad (los puestos de la ninera de ese viaje, 1 a 20) y su restriccion. Nula en los viajes anteriores: se comportan como de 7 puestos (no se migra ninguno).
     2. tblTiposUnidadTransportadora: columna Puestos (los puestos por defecto del tipo, 1 a 20; nula = sin definir) y su restriccion. Un viaje toma este valor al crearse y no se mueve si despues cambia.
     3. tblViajeVehiculos: CK_tblViajeVehiculos_Posicion pasa de 1..7 a 1..20 (la posicion se valida contra la capacidad del viaje, que llega hasta 20).
     4. tblEvidencias: columna EsAcomodacion (bit, 0 por defecto) y su restriccion: la foto de la acomodacion no es de una vista, un hallazgo, un extra, una constancia ni un sello. Las fotos de antes quedan en 0.

   Seguridad de los datos: no cambia ningun dato ni borra filas (solo agrega columnas nulas o con 0 por defecto y relaja un rango).

   Como ejecutarlo: abrir en SSMS, DBeaver o sqlcmd CONECTADO a la base de IA Autos, ajustar @esquema y ejecutar completo, en UNA sola ejecucion de script, con un usuario con permiso para
   modificar tablas (no el usuario de la aplicacion). Ejecutarlo ANTES de desplegar la API.
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
IF OBJECT_ID(@q + N'.tblViajes', N'U') IS NULL OR OBJECT_ID(@q + N'.tblTiposUnidadTransportadora', N'U') IS NULL OR OBJECT_ID(@q + N'.tblViajeVehiculos', N'U') IS NULL OR OBJECT_ID(@q + N'.tblEvidencias', N'U') IS NULL
    THROW 50001, N'Faltan tablas de IA Autos (viajes, tipos de unidad, unidades del viaje o evidencias) en el esquema indicado: revise @esquema.', 1;

-- Pasos en orden. {S} = esquema entre corchetes; ~ = comilla simple dentro del texto.
DECLARE @paso TABLE (n int IDENTITY(1, 1) PRIMARY KEY, descripcion nvarchar(100), s nvarchar(max));

INSERT @paso (descripcion, s) VALUES
(N'tblViajes: capacidad de la ninera', N'
IF COL_LENGTH(N~{S}.tblViajes~, N~Capacidad~) IS NULL
    ALTER TABLE {S}.tblViajes ADD Capacidad tinyint NULL'),
(N'tblViajes: rango de la capacidad', N'
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N~CK_tblViajes_Capacidad~ AND parent_object_id = OBJECT_ID(N~{S}.tblViajes~))
    ALTER TABLE {S}.tblViajes ADD CONSTRAINT CK_tblViajes_Capacidad CHECK (Capacidad IS NULL OR Capacidad BETWEEN 1 AND 20)'),
(N'tblTiposUnidadTransportadora: puestos por defecto', N'
IF COL_LENGTH(N~{S}.tblTiposUnidadTransportadora~, N~Puestos~) IS NULL
    ALTER TABLE {S}.tblTiposUnidadTransportadora ADD Puestos tinyint NULL'),
(N'tblTiposUnidadTransportadora: rango de los puestos', N'
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N~CK_tblTiposUnidadTransportadora_Puestos~ AND parent_object_id = OBJECT_ID(N~{S}.tblTiposUnidadTransportadora~))
    ALTER TABLE {S}.tblTiposUnidadTransportadora ADD CONSTRAINT CK_tblTiposUnidadTransportadora_Puestos CHECK (Puestos IS NULL OR Puestos BETWEEN 1 AND 20)'),
(N'tblViajeVehiculos: la posicion llega hasta 20', N'
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N~CK_tblViajeVehiculos_Posicion~ AND parent_object_id = OBJECT_ID(N~{S}.tblViajeVehiculos~) AND definition NOT LIKE N~%(20)%~)
BEGIN
    ALTER TABLE {S}.tblViajeVehiculos DROP CONSTRAINT CK_tblViajeVehiculos_Posicion;
    ALTER TABLE {S}.tblViajeVehiculos ADD CONSTRAINT CK_tblViajeVehiculos_Posicion CHECK (Posicion IS NULL OR Posicion BETWEEN 1 AND 20);
END'),
(N'tblEvidencias: marca de acomodacion', N'
IF COL_LENGTH(N~{S}.tblEvidencias~, N~EsAcomodacion~) IS NULL
    ALTER TABLE {S}.tblEvidencias ADD EsAcomodacion bit NOT NULL CONSTRAINT DF_tblEvidencias_EsAcomodacion DEFAULT (0)'),
(N'tblEvidencias: la foto de la acomodacion no es de otra cosa', N'
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N~CK_tblEvidencias_Acomodacion~ AND parent_object_id = OBJECT_ID(N~{S}.tblEvidencias~))
    ALTER TABLE {S}.tblEvidencias ADD CONSTRAINT CK_tblEvidencias_Acomodacion CHECK (EsAcomodacion = 0 OR (HallazgoId IS NULL AND VistaId IS NULL AND ExtraId IS NULL AND EsConstancia = 0 AND EsSello = 0))');

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
    (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N~{S}.tblViajes~) AND name = N~Capacidad~) AS CapacidadDelViaje,
    (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N~{S}.tblTiposUnidadTransportadora~) AND name = N~Puestos~) AS PuestosDelTipo,
    (SELECT COUNT(*) FROM sys.check_constraints WHERE name = N~CK_tblViajeVehiculos_Posicion~ AND parent_object_id = OBJECT_ID(N~{S}.tblViajeVehiculos~) AND definition LIKE N~%(20)%~) AS PosicionHasta20,
    (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N~{S}.tblEvidencias~) AND name = N~EsAcomodacion~) AS MarcaDeAcomodacion;', N'{S}', @q);
SET @cuentas = REPLACE(@cuentas, N'~', N'''');
EXEC (@cuentas);
