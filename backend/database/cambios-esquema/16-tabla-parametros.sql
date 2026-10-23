/* =============================================================================
   Tabla de parametros, para una base de IA Autos YA EXISTENTE. Se ejecuta UNA vez (si ya esta aplicado, no hace nada) y todo ocurre en UNA transaccion: si algo falla se revierte completo.

   Que hace:
     1. tblParametros: los valores que el negocio ajusta sin desplegar codigo. Una fila por parametro: Clave, Valor (nulo = sin valor), Tipo (numero, porcentaje, texto, lista, hora o correos),
        Descripcion, ActualizadoPor y UpdatedAt. Las filas se crean aqui; la API solo cambia su valor.
     2. Las filas iniciales (los valores los confirma Calidad): objetivo_diario_novedad 1.2, reporte_diario_destinatarios sin valor, reporte_diario_hora 06:00, horario_operacion_inicio 08:00,
        horario_operacion_fin 21:00, retencion_dias sin valor, gps_precision_minima_m 30 y url_firmada_minutos 15. No hay filas de linea base de rechazo ni de fotos minimas.

   Seguridad de los datos: no cambia ningun dato existente; solo crea la tabla y sus filas. Una fila que ya existe nunca se sobrescribe.

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
IF OBJECT_ID(@q + N'.tblUsuarios', N'U') IS NULL
    THROW 50001, N'Falta la tabla tblUsuarios de IA Autos en el esquema indicado: revise @esquema.', 1;

-- Pasos en orden. {S} = esquema entre corchetes; ~ = comilla simple dentro del texto.
DECLARE @paso TABLE (n int IDENTITY(1, 1) PRIMARY KEY, descripcion nvarchar(100), s nvarchar(max));

INSERT @paso (descripcion, s) VALUES
(N'tblParametros', N'
IF OBJECT_ID(N~{S}.tblParametros~, N~U~) IS NULL
BEGIN
    CREATE TABLE {S}.tblParametros
    (
        Clave           varchar(100)      NOT NULL CONSTRAINT PK_tblParametros PRIMARY KEY,
        Valor           nvarchar(1000)    NULL,
        Tipo            varchar(15)       NOT NULL,
        Descripcion     nvarchar(300)     NOT NULL,
        ActualizadoPor  uniqueidentifier  NULL,
        UpdatedAt       datetimeoffset(3) NOT NULL CONSTRAINT DF_tblParametros_UpdatedAt DEFAULT (SYSDATETIMEOFFSET()),
        CONSTRAINT FK_tblParametros_ActualizadoPor FOREIGN KEY (ActualizadoPor) REFERENCES {S}.tblUsuarios (Id),
        CONSTRAINT CK_tblParametros_Tipo CHECK (Tipo IN (~numero~, ~porcentaje~, ~texto~, ~lista~, ~hora~, ~correos~)),
        CONSTRAINT CK_tblParametros_Valor CHECK (Valor IS NULL OR LEN(Valor) > 0)
    );
END'),
(N'filas iniciales', N'
INSERT INTO {S}.tblParametros (Clave, Valor, Tipo, Descripcion)
SELECT v.Clave, v.Valor, v.Tipo, v.Descripcion
FROM (VALUES
    (~objetivo_diario_novedad~,        N~1.2~,   ~porcentaje~, N~Objetivo diario de vehiculos con novedad (al menos un hallazgo V1), en porcentaje~),
    (~reporte_diario_destinatarios~,   NULL,     ~correos~,    N~Correos que reciben el reporte diario, separados por coma; vacio = no se envia~),
    (~reporte_diario_hora~,            N~06:00~, ~hora~,       N~Hora (HH:mm, hora de Colombia) a la que se envia el reporte diario~),
    (~horario_operacion_inicio~,       N~08:00~, ~hora~,       N~Hora (HH:mm) en que empieza el horario de operacion~),
    (~horario_operacion_fin~,          N~21:00~, ~hora~,       N~Hora (HH:mm) en que termina el horario de operacion~),
    (~retencion_dias~,                 NULL,     ~numero~,     N~Dias que se conserva la evidencia; sin valor hasta que se defina la retencion~),
    (~gps_precision_minima_m~,         N~30~,    ~numero~,     N~Precision minima del GPS, en metros, para no avisar de precision baja~),
    (~url_firmada_minutos~,            N~15~,    ~numero~,     N~Minutos de vigencia de las URL firmadas de las fotos~)
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

-- Resultado (esperado: 1 tabla y 8 parametros: 6 con valor y 2 sin valor).
DECLARE @cuentas nvarchar(max) = REPLACE(N'
SELECT
    (SELECT COUNT(*) FROM sys.tables WHERE name = N~tblParametros~ AND schema_id = SCHEMA_ID(N~{E}~)) AS Tabla,
    (SELECT COUNT(*) FROM {S}.tblParametros) AS Parametros,
    (SELECT COUNT(*) FROM {S}.tblParametros WHERE Valor IS NOT NULL) AS ConValor,
    (SELECT COUNT(*) FROM {S}.tblParametros WHERE Valor IS NULL) AS SinValor', N'{S}', @q);
SET @cuentas = REPLACE(REPLACE(@cuentas, N'{E}', @esquema), N'~', N'''');
EXEC (@cuentas);
