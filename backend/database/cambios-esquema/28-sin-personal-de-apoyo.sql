/* =============================================================================
   Elimina la marca de personal de apoyo del cargue, para una base de IA Autos YA EXISTENTE (con 27-aviso-reclamo-sin-actividad.sql aplicado). Se ejecuta UNA vez (si ya esta aplicado, no cambia
   nada) y todo ocurre en UNA transaccion: si algo falla se revierte completo.

   Que hace:
     1. tblEtapas: elimina la columna ConPersonalApoyo y su restriccion por defecto (DF_tblEtapas_ConPersonalApoyo). Desde ahora el cargue nace sin ninguna marca: la salida del cargue la hace siempre
        un inspector y, si no hay inspector, el conductor activa en el momento la excepcion "No hay inspector" (su motivo sale del catalogo tblMotivosSinInspector y queda como novedad de proceso).
        El script avisa cuantas etapas tenian la marca encendida (PRINT) y no migra nada: no hay datos de piloto que conservar.
     2. Verifica, sin cambiar nada, el catalogo de motivos de la excepcion (tblMotivosSinInspector): su resultado muestra los motivos y si estan activos.

   Seguridad de los datos: solo elimina esa columna y su restriccion por defecto; ninguna vista ni indice la referencia (se comprueba antes y el script aborta si alguno lo hiciera).

   Como ejecutarlo: abrir en SSMS, DBeaver o sqlcmd CONECTADO a la base de IA Autos, ajustar @esquema y ejecutar completo, en UNA sola ejecucion de script, con un usuario con permiso para
   alterar tablas (no el usuario de la aplicacion). Ejecutarlo ANTES de desplegar la API.
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
IF OBJECT_ID(@q + N'.tblEtapas', N'U') IS NULL
    THROW 50001, N'Falta la tabla tblEtapas de IA Autos en el esquema indicado: revise @esquema.', 1;
IF OBJECT_ID(@q + N'.tblMotivosSinInspector', N'U') IS NULL
    THROW 50002, N'Falta tblMotivosSinInspector: ejecute antes 23-novedad-de-proceso.sql.', 1;
-- Ninguna vista, funcion ni procedimiento puede referenciar la columna: si alguno lo hace, el script aborta sin cambiar nada.
IF COL_LENGTH(@q + N'.tblEtapas', N'ConPersonalApoyo') IS NOT NULL
   AND EXISTS (SELECT 1 FROM sys.sql_modules m WHERE m.definition LIKE N'%ConPersonalApoyo%' AND OBJECT_SCHEMA_NAME(m.object_id) = @esquema)
    THROW 50003, N'Hay una vista, funcion o procedimiento que referencia tblEtapas.ConPersonalApoyo: revisela antes de eliminar la columna.', 1;

-- Pasos en orden. {S} = esquema entre corchetes; ~ = comilla simple dentro del texto.
DECLARE @paso TABLE (n int IDENTITY(1, 1) PRIMARY KEY, descripcion nvarchar(100), s nvarchar(max));

INSERT @paso (descripcion, s) VALUES
(N'tblEtapas: eliminar ConPersonalApoyo y su restriccion', N'
IF COL_LENGTH(N~{S}.tblEtapas~, N~ConPersonalApoyo~) IS NOT NULL
BEGIN
    DECLARE @conMarca int, @restriccion sysname;
    EXEC sp_executesql N~SELECT @n = COUNT(*) FROM {S}.tblEtapas WHERE ConPersonalApoyo = 1~, N~@n int OUTPUT~, @n = @conMarca OUTPUT;
    PRINT CONCAT(N~Etapas con la marca encendida antes de eliminarla: ~, @conMarca);
    SELECT @restriccion = dc.name
    FROM sys.default_constraints dc
    JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
    WHERE dc.parent_object_id = OBJECT_ID(N~{S}.tblEtapas~) AND c.name = N~ConPersonalApoyo~;
    IF @restriccion IS NOT NULL
        EXEC (N~ALTER TABLE {S}.tblEtapas DROP CONSTRAINT ~ + @restriccion);
    ALTER TABLE {S}.tblEtapas DROP COLUMN ConPersonalApoyo;
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

-- Resultado 1 (esperado: ColumnaEliminada = 1 y RestriccionEliminada = 1).
DECLARE @cuentas nvarchar(max) = REPLACE(N'
SELECT
    CASE WHEN COL_LENGTH(N~{S}.tblEtapas~, N~ConPersonalApoyo~) IS NULL THEN 1 ELSE 0 END AS ColumnaEliminada,
    CASE WHEN NOT EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = N~DF_tblEtapas_ConPersonalApoyo~) THEN 1 ELSE 0 END AS RestriccionEliminada', N'{S}', @q);
SET @cuentas = REPLACE(@cuentas, N'~', N'''');
EXEC (@cuentas);

-- Resultado 2: el catalogo de motivos de la excepcion "No hay inspector" (administrable desde el portal como los demas catalogos maestros). Compare con el listado de PAB-003: los valores se cambian sin
-- desplegar por PATCH /api/catalogos/motivos-sin-inspector/{id}. RequiereTexto = 1 en "otro": quien lo elige escribe cual es.
DECLARE @motivos nvarchar(max) = REPLACE(N'
SELECT Codigo, Nombre, RequiereTexto, Orden, Activo FROM {S}.tblMotivosSinInspector ORDER BY Orden, Codigo', N'{S}', @q);
EXEC (@motivos);
