/* =============================================================================
   El mapeo de columnas de la carga de viajes, compartido por cliente, para una base de IA Autos YA EXISTENTE. Se ejecuta UNA vez (si ya esta aplicado, no cambia nada) y todo ocurre en UNA transaccion: si algo falla se
   revierte completo.

   Que hace:
     1. tblMapeosCargaViajes: un mapeo por cliente y por forma del archivo. La clave es el cliente mas la huella de la forma (los titulos normalizados con su numero de repeticion, ordenados, sin importar el orden de
        las columnas). Columnas y Campos son JSON con SOLO titulos y el campo elegido para cada uno: nunca valores de las filas. Guarda quien lo actualizo por ultima vez y cuando.

   Seguridad de los datos: solo agrega una tabla nueva y vacia; no cambia ningun dato. Se ejecuta ANTES de desplegar la API.

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
IF OBJECT_ID(@q + N'.tblClientes', N'U') IS NULL OR OBJECT_ID(@q + N'.tblUsuarios', N'U') IS NULL
    THROW 50001, N'Faltan tblClientes o tblUsuarios de IA Autos en el esquema indicado: revise @esquema.', 1;

-- Pasos en orden. {S} = esquema entre corchetes; ~ = comilla simple dentro del texto.
DECLARE @paso TABLE (n int IDENTITY(1, 1) PRIMARY KEY, descripcion nvarchar(100), s nvarchar(max));

INSERT @paso (descripcion, s) VALUES
(N'tblMapeosCargaViajes: la tabla', N'
IF OBJECT_ID(N~{S}.tblMapeosCargaViajes~, N~U~) IS NULL
    CREATE TABLE {S}.tblMapeosCargaViajes
    (
        Id             uniqueidentifier  NOT NULL CONSTRAINT PK_tblMapeosCargaViajes PRIMARY KEY,
        ClienteId      uniqueidentifier  NOT NULL,
        Huella         char(64)          NOT NULL,
        Columnas       nvarchar(max)     NOT NULL,
        Campos         nvarchar(max)     NOT NULL,
        ActualizadoPor uniqueidentifier NOT NULL,
        CreatedAt      datetimeoffset(3) NOT NULL CONSTRAINT DF_tblMapeosCargaViajes_CreatedAt DEFAULT (SYSDATETIMEOFFSET()),
        UpdatedAt      datetimeoffset(3) NOT NULL CONSTRAINT DF_tblMapeosCargaViajes_UpdatedAt DEFAULT (SYSDATETIMEOFFSET()),
        CONSTRAINT FK_tblMapeosCargaViajes_Cliente FOREIGN KEY (ClienteId) REFERENCES {S}.tblClientes (Id),
        CONSTRAINT FK_tblMapeosCargaViajes_ActualizadoPor FOREIGN KEY (ActualizadoPor) REFERENCES {S}.tblUsuarios (Id),
        CONSTRAINT UQ_tblMapeosCargaViajes_Cliente_Huella UNIQUE (ClienteId, Huella),
        CONSTRAINT CK_tblMapeosCargaViajes_Json CHECK (ISJSON(Columnas) = 1 AND ISJSON(Campos) = 1)
    )');

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

-- Resultado (esperado: Tabla = 1, UnicoPorClienteYForma = 1, SoloJson = 1).
DECLARE @cuentas nvarchar(max) = REPLACE(N'
SELECT
    (SELECT COUNT(*) FROM sys.tables WHERE object_id = OBJECT_ID(N~{S}.tblMapeosCargaViajes~)) AS Tabla,
    (SELECT COUNT(*) FROM sys.key_constraints WHERE name = N~UQ_tblMapeosCargaViajes_Cliente_Huella~) AS UnicoPorClienteYForma,
    (SELECT COUNT(*) FROM sys.check_constraints WHERE name = N~CK_tblMapeosCargaViajes_Json~) AS SoloJson;', N'{S}', @q);
SET @cuentas = REPLACE(@cuentas, N'~', N'''');
EXEC (@cuentas);
