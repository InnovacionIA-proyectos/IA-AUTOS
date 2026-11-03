/* =============================================================================
   Reclamo del camion por el inspector, para una base de IA Autos YA EXISTENTE (con 25-destino-por-unidad-y-cierre-con-una-firma.sql aplicado). Se ejecuta UNA vez (si ya esta aplicado, no cambia nada)
   y todo ocurre en UNA transaccion: si algo falla se revierte completo.

   Que hace:
     1. tblInspectorLugares: los puntos de cada inspector (usuario y lugar del catalogo de lugares reales, tblLugares; unico por pareja). Un inspector puede tener varios puntos.
     2. tblReclamos: quien reclamo un cargue o una entrega, cuando, y al liberarse quien y cuando lo libero. Proceso: cargue o entrega (la etapa Descargue). Un indice unico filtrado impide dos reclamos
        ACTIVOS (sin liberar) del mismo viaje y proceso; los reclamos liberados se conservan como historial y no se borran (error 51003). Liberado y quien lo libero van juntos o ninguno.
        tblViajes.InspectorId se conserva: lo llena el reclamo del cargue.
     3. tblParametros: la fila reclamo_sin_actividad_minutos (numero, 30): minutos que un camion reclamado puede seguir sin actividad antes de avisar al coordinador. El valor definitivo lo confirma Calidad y
        se ajusta sin desplegar.

   Seguridad de los datos: no cambia ningun dato existente; solo crea las dos tablas, su indice y su trigger y agrega la fila del parametro si no existe (una fila que ya existe nunca se sobrescribe).

   Como ejecutarlo: abrir en SSMS, DBeaver o sqlcmd CONECTADO a la base de IA Autos, ajustar @esquema y ejecutar completo, en UNA sola ejecucion de script, con un usuario con permiso para
   crear tablas y triggers (no el usuario de la aplicacion). Ejecutarlo ANTES de desplegar la API.
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
IF OBJECT_ID(@q + N'.tblUsuarios', N'U') IS NULL OR OBJECT_ID(@q + N'.tblViajes', N'U') IS NULL OR OBJECT_ID(@q + N'.tblParametros', N'U') IS NULL
    THROW 50001, N'Faltan las tablas tblUsuarios, tblViajes o tblParametros de IA Autos en el esquema indicado: revise @esquema.', 1;
IF OBJECT_ID(@q + N'.tblLugares', N'U') IS NULL
    THROW 50002, N'Falta tblLugares: ejecute antes 24-lugares-y-programacion.sql.', 1;

-- Pasos en orden. {S} = esquema entre corchetes; ~ = comilla simple dentro del texto.
DECLARE @paso TABLE (n int IDENTITY(1, 1) PRIMARY KEY, descripcion nvarchar(100), s nvarchar(max));

INSERT @paso (descripcion, s) VALUES
(N'tblInspectorLugares', N'
IF OBJECT_ID(N~{S}.tblInspectorLugares~, N~U~) IS NULL
BEGIN
    CREATE TABLE {S}.tblInspectorLugares
    (
        Id         uniqueidentifier  NOT NULL CONSTRAINT PK_tblInspectorLugares PRIMARY KEY,
        UsuarioId  uniqueidentifier  NOT NULL,
        LugarId    uniqueidentifier  NOT NULL,
        CreatedAt  datetimeoffset(3) NOT NULL CONSTRAINT DF_tblInspectorLugares_CreatedAt DEFAULT (SYSDATETIMEOFFSET()),
        CONSTRAINT UQ_tblInspectorLugares_Par UNIQUE (UsuarioId, LugarId),
        CONSTRAINT FK_tblInspectorLugares_Usuario FOREIGN KEY (UsuarioId) REFERENCES {S}.tblUsuarios (Id),
        CONSTRAINT FK_tblInspectorLugares_Lugar FOREIGN KEY (LugarId) REFERENCES {S}.tblLugares (Id)
    );
    CREATE INDEX IX_tblInspectorLugares_Lugar ON {S}.tblInspectorLugares (LugarId);
END'),
(N'tblReclamos', N'
IF OBJECT_ID(N~{S}.tblReclamos~, N~U~) IS NULL
BEGIN
    CREATE TABLE {S}.tblReclamos
    (
        Id           uniqueidentifier  NOT NULL CONSTRAINT PK_tblReclamos PRIMARY KEY,
        ViajeId      uniqueidentifier  NOT NULL,
        InspectorId  uniqueidentifier  NOT NULL,
        Proceso      varchar(10)       NOT NULL,
        ReclamadoAt  datetimeoffset(3) NOT NULL CONSTRAINT DF_tblReclamos_ReclamadoAt DEFAULT (SYSDATETIMEOFFSET()),
        LiberadoAt   datetimeoffset(3) NULL,
        LiberadoPor  uniqueidentifier  NULL,
        CONSTRAINT FK_tblReclamos_Viaje FOREIGN KEY (ViajeId) REFERENCES {S}.tblViajes (Id),
        CONSTRAINT FK_tblReclamos_Inspector FOREIGN KEY (InspectorId) REFERENCES {S}.tblUsuarios (Id),
        CONSTRAINT FK_tblReclamos_LiberadoPor FOREIGN KEY (LiberadoPor) REFERENCES {S}.tblUsuarios (Id),
        CONSTRAINT CK_tblReclamos_Proceso CHECK (Proceso IN (~cargue~, ~entrega~)),
        -- Liberado y quien lo libero van juntos o ninguno, y no se libera antes de reclamarse.
        CONSTRAINT CK_tblReclamos_Liberacion CHECK ((LiberadoAt IS NULL AND LiberadoPor IS NULL) OR (LiberadoAt IS NOT NULL AND LiberadoPor IS NOT NULL AND LiberadoAt >= ReclamadoAt))
    );
    CREATE INDEX IX_tblReclamos_Viaje ON {S}.tblReclamos (ViajeId);
    CREATE INDEX IX_tblReclamos_Inspector ON {S}.tblReclamos (InspectorId);
END'),
(N'tblReclamos: un solo reclamo activo por viaje y proceso', N'
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N~UX_tblReclamos_Activo~ AND object_id = OBJECT_ID(N~{S}.tblReclamos~))
    CREATE UNIQUE INDEX UX_tblReclamos_Activo ON {S}.tblReclamos (ViajeId, Proceso) WHERE LiberadoAt IS NULL'),
(N'tblReclamos: no se borra', N'
CREATE OR ALTER TRIGGER {S}.TR_tblReclamos_NoBorrar
ON {S}.tblReclamos
INSTEAD OF DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 51003, ~Este registro es evidencia y no se puede borrar~, 1;
END'),
(N'parametro del aviso de inactividad', N'
INSERT INTO {S}.tblParametros (Clave, Valor, Tipo, Descripcion)
SELECT v.Clave, v.Valor, v.Tipo, v.Descripcion
FROM (VALUES
    (~reclamo_sin_actividad_minutos~, N~30~, ~numero~, N~Minutos que un camion reclamado puede seguir sin ninguna inspeccion iniciada o cerrada antes de avisar al coordinador; el reclamo no se libera solo~)
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

-- Resultado 1. Esperado justo despues de migrar: TablaPuntos = 1, TablaReclamos = 1, IndiceActivo = 1, Trigger = 1, Parametro = 1 (valor 30, numero), PuntosDeInspectores = 0 y Reclamos = 0 (nada se migra).
DECLARE @cuentas nvarchar(max) = REPLACE(N'
SELECT
    (SELECT COUNT(*) FROM sys.tables WHERE name = N~tblInspectorLugares~ AND schema_id = SCHEMA_ID(N~{E}~)) AS TablaPuntos,
    (SELECT COUNT(*) FROM sys.tables WHERE name = N~tblReclamos~ AND schema_id = SCHEMA_ID(N~{E}~)) AS TablaReclamos,
    (SELECT COUNT(*) FROM sys.indexes WHERE name = N~UX_tblReclamos_Activo~ AND object_id = OBJECT_ID(N~{S}.tblReclamos~) AND is_unique = 1 AND has_filter = 1) AS IndiceActivo,
    (SELECT COUNT(*) FROM sys.triggers WHERE name = N~TR_tblReclamos_NoBorrar~ AND parent_id = OBJECT_ID(N~{S}.tblReclamos~)) AS [Trigger],
    (SELECT COUNT(*) FROM {S}.tblParametros WHERE Clave = N~reclamo_sin_actividad_minutos~ AND Tipo = N~numero~) AS Parametro,
    (SELECT Valor FROM {S}.tblParametros WHERE Clave = N~reclamo_sin_actividad_minutos~) AS ValorDelParametro,
    (SELECT COUNT(*) FROM {S}.tblInspectorLugares) AS PuntosDeInspectores,
    (SELECT COUNT(*) FROM {S}.tblReclamos) AS Reclamos', N'{S}', @q);
SET @cuentas = REPLACE(REPLACE(@cuentas, N'{E}', @esquema), N'~', N'''');
EXEC (@cuentas);
