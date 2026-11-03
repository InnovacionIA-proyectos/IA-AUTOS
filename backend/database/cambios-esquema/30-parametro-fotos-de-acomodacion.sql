/* =============================================================================
   Parametro del minimo de fotos de la acomodacion, para una base de IA Autos YA EXISTENTE (con 29-posicion-de-la-unidad.sql aplicado). Se ejecuta UNA vez (si ya esta aplicado, no cambia nada) y todo
   ocurre en UNA transaccion: si algo falla se revierte completo.

   Que hace:
     1. tblParametros: agrega la fila acomodacion_fotos_minimas (numero, 1): cuantas fotos de la acomodacion de las unidades en la ninera exige el cierre de la segunda inspeccion del cargue. Es un valor
        ilustrativo que confirma el negocio y se ajusta sin desplegar (PATCH /api/parametros/acomodacion_fotos_minimas). Una fila que ya existe nunca se sobrescribe.

   Seguridad de los datos: no cambia ningun dato existente; solo agrega esa fila si no existe.

   Como ejecutarlo: abrir en SSMS, DBeaver o sqlcmd CONECTADO a la base de IA Autos, ajustar @esquema y ejecutar completo, en UNA sola ejecucion de script, con un usuario con permiso para
   modificar datos (no el usuario de la aplicacion). Ejecutarlo ANTES de desplegar la API.
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

-- Pasos en orden. {S} = esquema entre corchetes; ~ = comilla simple dentro del texto.
DECLARE @paso TABLE (n int IDENTITY(1, 1) PRIMARY KEY, descripcion nvarchar(100), s nvarchar(max));

INSERT @paso (descripcion, s) VALUES
(N'parametro del minimo de fotos de la acomodacion', N'
INSERT INTO {S}.tblParametros (Clave, Valor, Tipo, Descripcion)
SELECT v.Clave, v.Valor, v.Tipo, v.Descripcion
FROM (VALUES
    (~acomodacion_fotos_minimas~, N~1~, ~numero~, N~Fotos de la acomodacion de las unidades en la ninera que exige el cierre de la segunda inspeccion del cargue (el valor definitivo lo confirma el negocio)~)
) AS v (Clave, Valor, Tipo, Descripcion)
WHERE NOT EXISTS (SELECT 1 FROM {S}.tblParametros p WHERE p.Clave = v.Clave)');

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

-- Resultado 1 (esperado: Parametro = 1, tipo numero; Valor = 1 en una base recien migrada).
DECLARE @cuentas nvarchar(max) = REPLACE(N'
SELECT
    (SELECT COUNT(*) FROM {S}.tblParametros WHERE Clave = N~acomodacion_fotos_minimas~ AND Tipo = N~numero~) AS Parametro,
    (SELECT Valor FROM {S}.tblParametros WHERE Clave = N~acomodacion_fotos_minimas~) AS Valor', N'{S}', @q);
SET @cuentas = REPLACE(@cuentas, N'~', N'''');
EXEC (@cuentas);
