/* =============================================================================
   Catalogo de estados de las ordenes Sofasa, para una base de IA Autos YA EXISTENTE. Se ejecuta UNA vez (si ya esta aplicado, no hace nada) y todo ocurre en UNA transaccion: si algo falla
   se revierte completo.

   Que hace:
     1. tblEstadosSofasa: Codigo (el StatusCode REAL que trae la orden, texto libre con sus mayusculas, unico sin distinguir mayusculas, sin espacios en los bordes), Nombre (la etiqueta que se
        muestra), Orden, Activo, CreatedAt y UpdatedAt. tblOrdenesSofasa.StatusCode sigue siendo texto libre y NO se relaciona con una llave foranea: la ingesta del ETL no debe fallar porque llegue
        un estado que aun no esta en el catalogo.
     2. Carga el catalogo con los StatusCode distintos que ya tienen las ordenes (sin distinguir mayusculas ni espacios de los bordes). La etiqueta provisional es el mismo codigo: la etiqueta de cada
        estado la define Calidad/Operaciones (PAB-032) y se cambia desde el portal (PATCH /api/catalogos/estados-sofasa/{id}). Si todavia no hay ordenes, el catalogo queda vacio.
        Se puede ejecutar de nuevo mas adelante: solo agrega los estados que falten, y nunca cambia una etiqueta que ya existe.

   Seguridad de los datos: no cambia ninguna orden ni ningun otro dato; solo crea la tabla y sus filas.

   Como ejecutarlo: abrir en SSMS, DBeaver o sqlcmd CONECTADO a la base de IA Autos, ajustar @esquema y ejecutar completo, en UNA sola ejecucion de script, con un usuario con permiso para
   crear tablas (no el usuario de la aplicacion). Ejecutarlo ANTES de desplegar la API.
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
IF OBJECT_ID(@q + N'.tblOrdenesSofasa', N'U') IS NULL
    THROW 50001, N'Falta la tabla tblOrdenesSofasa de IA Autos en el esquema indicado: revise @esquema.', 1;

-- Pasos en orden. {S} = esquema entre corchetes; ~ = comilla simple dentro del texto.
DECLARE @paso TABLE (n int IDENTITY(1, 1) PRIMARY KEY, descripcion nvarchar(100), s nvarchar(max));

INSERT @paso (descripcion, s) VALUES
(N'tblEstadosSofasa', N'
IF OBJECT_ID(N~{S}.tblEstadosSofasa~, N~U~) IS NULL
BEGIN
    CREATE TABLE {S}.tblEstadosSofasa
    (
        Id         uniqueidentifier  NOT NULL CONSTRAINT PK_tblEstadosSofasa PRIMARY KEY,
        Codigo     nvarchar(100)     NOT NULL,
        Nombre     nvarchar(100)     NOT NULL,
        Orden      int               NOT NULL CONSTRAINT DF_tblEstadosSofasa_Orden DEFAULT (0),
        Activo     bit               NOT NULL CONSTRAINT DF_tblEstadosSofasa_Activo DEFAULT (1),
        CreatedAt  datetimeoffset(3) NOT NULL CONSTRAINT DF_tblEstadosSofasa_CreatedAt DEFAULT (SYSDATETIMEOFFSET()),
        UpdatedAt  datetimeoffset(3) NOT NULL CONSTRAINT DF_tblEstadosSofasa_UpdatedAt DEFAULT (SYSDATETIMEOFFSET()),
        CONSTRAINT UQ_tblEstadosSofasa_Codigo UNIQUE (Codigo),
        CONSTRAINT CK_tblEstadosSofasa_Codigo CHECK (LEN(Codigo) > 0 AND Codigo = LTRIM(RTRIM(Codigo))),
        CONSTRAINT CK_tblEstadosSofasa_Nombre CHECK (LEN(LTRIM(RTRIM(Nombre))) > 0),
        CONSTRAINT CK_tblEstadosSofasa_Orden CHECK (Orden >= 0)
    );
END'),
(N'estados de las ordenes existentes', N'
INSERT INTO {S}.tblEstadosSofasa (Id, Codigo, Nombre, Orden)
SELECT NEWID(), x.Codigo, x.Codigo, ROW_NUMBER() OVER (ORDER BY x.Codigo) + (SELECT ISNULL(MAX(Orden), 0) FROM {S}.tblEstadosSofasa)
FROM (
    SELECT MIN(LTRIM(RTRIM(o.StatusCode))) AS Codigo
    FROM {S}.tblOrdenesSofasa o
    WHERE o.StatusCode IS NOT NULL AND LEN(LTRIM(RTRIM(o.StatusCode))) > 0
    GROUP BY LTRIM(RTRIM(o.StatusCode))
) x
WHERE NOT EXISTS (SELECT 1 FROM {S}.tblEstadosSofasa e WHERE e.Codigo = x.Codigo)');

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

-- Resultado (esperado: Tabla = 1; Estados = los StatusCode distintos de las ordenes; SinEstado = 0, es decir, ningun StatusCode de una orden queda fuera del catalogo).
DECLARE @cuentas nvarchar(max) = REPLACE(N'
SELECT
    (SELECT COUNT(*) FROM sys.tables WHERE name = N~tblEstadosSofasa~ AND schema_id = SCHEMA_ID(N~{E}~)) AS Tabla,
    (SELECT COUNT(*) FROM {S}.tblEstadosSofasa) AS Estados,
    (SELECT COUNT(*) FROM {S}.tblOrdenesSofasa) AS Ordenes,
    (SELECT COUNT(DISTINCT LTRIM(RTRIM(o.StatusCode))) FROM {S}.tblOrdenesSofasa o
      WHERE o.StatusCode IS NOT NULL AND LEN(LTRIM(RTRIM(o.StatusCode))) > 0
        AND NOT EXISTS (SELECT 1 FROM {S}.tblEstadosSofasa e WHERE e.Codigo = LTRIM(RTRIM(o.StatusCode)))) AS SinEstado', N'{S}', @q);
SET @cuentas = REPLACE(REPLACE(@cuentas, N'{E}', @esquema), N'~', N'''');
EXEC (@cuentas);
