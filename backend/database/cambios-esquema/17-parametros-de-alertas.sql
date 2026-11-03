/* =============================================================================
   Parametros de las alertas y vista de la ventana de umbral, para una base de IA Autos YA EXISTENTE (con 10-zona-dano-y-hallazgo.sql y 16-tabla-parametros.sql aplicados). Se ejecuta UNA vez
   (si ya esta aplicado, no cambia nada) y todo ocurre en UNA transaccion: si algo falla se revierte completo.

   Que hace:
     1. Agrega a tblParametros las filas de las alertas (los valores los confirma Calidad): niveles_criticos v1_plus,v1; umbral_patron_cantidad 10; umbral_patron_ventana_dias 30;
        captura_incompleta_minutos 30 (la inspeccion FLASH AVES dura maximo 5 minutos) y transferencia_pendiente_minutos 240.
     2. vwHallazgosVentanaUmbral: los hallazgos de inspecciones cerradas con su cliente, su zona (lado + numero, o la zona provisional), su proceso y la fecha de cierre, una fila por hallazgo.
        Excluye las novedades del conductor aun sin revisar. Es la que consultan los procesos de alertas para contar patrones dentro de una ventana de dias.

   Seguridad de los datos: no cambia ningun dato existente; solo agrega filas y crea la vista. Una fila que ya existe nunca se sobrescribe.

   Como ejecutarlo: abrir en SSMS, DBeaver o sqlcmd CONECTADO a la base de IA Autos, ajustar @esquema y ejecutar completo, en UNA sola ejecucion de script, con un usuario con permiso para
   crear vistas (no el usuario de la aplicacion). Ejecutarlo ANTES de desplegar la API.
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
    THROW 50001, N'Falta tblParametros: ejecute antes 16-tabla-parametros.sql.', 1;
IF OBJECT_ID(@q + N'.tblHallazgos', N'U') IS NULL OR OBJECT_ID(@q + N'.tblInspecciones', N'U') IS NULL OR OBJECT_ID(@q + N'.tblEtapas', N'U') IS NULL
   OR OBJECT_ID(@q + N'.tblEtapasCatalogo', N'U') IS NULL OR OBJECT_ID(@q + N'.tblVehiculos', N'U') IS NULL OR OBJECT_ID(@q + N'.tblZonasDano', N'U') IS NULL
    THROW 50002, N'Faltan tablas de IA Autos en el esquema indicado: revise @esquema.', 1;
IF COL_LENGTH(@q + N'.tblHallazgos', N'ZonaId') IS NULL OR COL_LENGTH(@q + N'.tblHallazgos', N'RevisadoAt') IS NULL
    THROW 50003, N'Falta la zona del hallazgo: ejecute antes 10-zona-dano-y-hallazgo.sql.', 1;

-- Pasos en orden. {S} = esquema entre corchetes; ~ = comilla simple dentro del texto.
DECLARE @paso TABLE (n int IDENTITY(1, 1) PRIMARY KEY, descripcion nvarchar(100), s nvarchar(max));

INSERT @paso (descripcion, s) VALUES
(N'parametros de las alertas', N'
INSERT INTO {S}.tblParametros (Clave, Valor, Tipo, Descripcion)
SELECT v.Clave, v.Valor, v.Tipo, v.Descripcion
FROM (VALUES
    (~niveles_criticos~,                   N~v1_plus,v1~, ~lista~,  N~Niveles de severidad criticos: al asignarlos a un hallazgo se alerta al coordinador y al administrador del cliente~),
    (~umbral_patron_cantidad~,             N~10~,         ~numero~, N~Hallazgos por zona o por proceso, dentro de la ventana, a partir de los cuales se alerta de un patron~),
    (~umbral_patron_ventana_dias~,         N~30~,         ~numero~, N~Dias de la ventana en la que se cuentan los hallazgos de un patron~),
    (~captura_incompleta_minutos~,         N~30~,         ~numero~, N~Minutos que una inspeccion de Cargue o Descargue puede seguir en borrador sin todas las fotos antes de avisar a quien la inicio~),
    (~transferencia_pendiente_minutos~,    N~240~,        ~numero~, N~Minutos que una llegada cerrada puede seguir con la transferencia sin cierre antes de avisar al firmante que falta~)
) AS v (Clave, Valor, Tipo, Descripcion)
WHERE NOT EXISTS (SELECT 1 FROM {S}.tblParametros p WHERE p.Clave = v.Clave)'),
(N'vwHallazgosVentanaUmbral', N'
CREATE OR ALTER VIEW {S}.vwHallazgosVentanaUmbral
AS
SELECT h.Id AS HallazgoId, h.InspeccionId, i.ViajeId, e.VehiculoId, v.ClienteId,
       z.Id AS ZonaId, z.Numero, z.Lado, z.Codigo AS ZonaCodigo,
       ec.Codigo AS Proceso, i.CerradaAt AS Fecha
FROM {S}.tblHallazgos h
JOIN {S}.tblInspecciones i ON i.Id = h.InspeccionId AND i.Estado = ~cerrada~
JOIN {S}.tblEtapas e ON e.Id = i.EtapaId
JOIN {S}.tblEtapasCatalogo ec ON ec.Id = e.EtapaCatalogoId
JOIN {S}.tblVehiculos v ON v.Id = e.VehiculoId
LEFT JOIN {S}.tblZonasDano z ON z.Id = h.ZonaId
WHERE NOT (h.Origen = ~conductor~ AND h.RevisadoAt IS NULL)');

DECLARE @n int, @descripcion nvarchar(100), @sql nvarchar(max);
BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE c CURSOR LOCAL FAST_FORWARD FOR SELECT n, descripcion, s FROM @paso ORDER BY n;
    OPEN c;
    FETCH NEXT FROM c INTO @n, @descripcion, @sql;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        SET @sql = REPLACE(REPLACE(@sql, N'{S}', @q), N'~', N'''');
        -- Con el dibujo version 6 ya sembrado esa vista es la de 34-resembrar-dibujo-v6.sql (sin la zona provisional): no se vuelve a la vieja.
        IF @descripcion = N'vwHallazgosVentanaUmbral' AND COL_LENGTH(@q + N'.tblZonasDano', N'LlevaLado') IS NOT NULL
            PRINT N'    (omitida: la vista ya es la del dibujo version 6)';
        ELSE
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

-- Resultado (esperado: 5 parametros de alertas y 1 vista).
DECLARE @cuentas nvarchar(max) = REPLACE(N'
SELECT
    (SELECT COUNT(*) FROM {S}.tblParametros WHERE Clave IN (N~niveles_criticos~, N~umbral_patron_cantidad~, N~umbral_patron_ventana_dias~, N~captura_incompleta_minutos~, N~transferencia_pendiente_minutos~)) AS ParametrosDeAlertas,
    (SELECT COUNT(*) FROM sys.views WHERE name = N~vwHallazgosVentanaUmbral~ AND schema_id = SCHEMA_ID(N~{E}~)) AS Vista', N'{S}', @q);
SET @cuentas = REPLACE(REPLACE(@cuentas, N'{E}', @esquema), N'~', N'''');
EXEC (@cuentas);
