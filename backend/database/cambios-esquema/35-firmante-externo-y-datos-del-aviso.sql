/* =============================================================================
   Firmante externo de la entrega y datos propios del aviso, para una base de IA Autos YA EXISTENTE (con 34-resembrar-dibujo-v6.sql aplicado). Se ejecuta UNA vez (si ya esta aplicado, no cambia nada) y
   todo ocurre en UNA transaccion: si algo falla se revierte completo.

   Que hace:
     1. tblTransferenciasCustodia: dos columnas nuevas, RecibeExternoNombre y RecibeExternoCedula, con los datos de la persona que recibe la unidad cuando NO es un usuario (el punto de entrega no
        tiene inspector asignado: por ejemplo quien recibe en el concesionario).
     2. tblTransferenciasCustodia: RecibeUsuarioId pasa a admitir nulo (quien recibe puede ser esa persona externa). Para poder cambiar la columna se quitan y se vuelven a crear, iguales, su indice
        IX_tblTransferencias_Recibe y la restriccion CK_tblTransferencias_Testigo.
     3. tblTransferenciasCustodia: restriccion CK_tblTransferencias_Recibe: nombre y cedula van juntos, no puede haber a la vez un usuario y una persona externa que reciben, y solo falta quien
        recibe cuando la entrega se cerro con una sola firma (la de quien entrega) porque nadie recibio.
     4. tblNotificaciones: columna Datos (JSON, opcional) con los campos propios del aviso (lugar, parte que firmo, motivo...), junto al texto, que no cambia. Solo admite JSON valido.
     5. tblNotificaciones: el trigger de solo lectura y respuesta tambien protege la columna Datos (como el resto de columnas del aviso).

   Seguridad de los datos: no cambia ningun dato ni borra filas; las transferencias y los avisos que ya existen se conservan tal cual (siguen teniendo su usuario que recibe y no tienen datos).

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
IF OBJECT_ID(@q + N'.tblTransferenciasCustodia', N'U') IS NULL OR OBJECT_ID(@q + N'.tblNotificaciones', N'U') IS NULL
    THROW 50001, N'Faltan las tablas tblTransferenciasCustodia o tblNotificaciones de IA Autos en el esquema indicado: revise @esquema.', 1;

-- Pasos en orden. {S} = esquema entre corchetes; ~ = comilla simple dentro del texto.
DECLARE @paso TABLE (n int IDENTITY(1, 1) PRIMARY KEY, descripcion nvarchar(100), s nvarchar(max));

INSERT @paso (descripcion, s) VALUES
(N'tblTransferenciasCustodia: nombre del firmante externo', N'
IF COL_LENGTH(N~{S}.tblTransferenciasCustodia~, N~RecibeExternoNombre~) IS NULL
    ALTER TABLE {S}.tblTransferenciasCustodia ADD RecibeExternoNombre nvarchar(150) NULL'),
(N'tblTransferenciasCustodia: cedula del firmante externo', N'
IF COL_LENGTH(N~{S}.tblTransferenciasCustodia~, N~RecibeExternoCedula~) IS NULL
    ALTER TABLE {S}.tblTransferenciasCustodia ADD RecibeExternoCedula varchar(30) NULL'),
(N'tblTransferenciasCustodia: quien recibe admite nulo', N'
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N~{S}.tblTransferenciasCustodia~) AND name = N~RecibeUsuarioId~ AND is_nullable = 0)
BEGIN
    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N~IX_tblTransferencias_Recibe~ AND object_id = OBJECT_ID(N~{S}.tblTransferenciasCustodia~))
        DROP INDEX IX_tblTransferencias_Recibe ON {S}.tblTransferenciasCustodia;
    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N~CK_tblTransferencias_Testigo~ AND parent_object_id = OBJECT_ID(N~{S}.tblTransferenciasCustodia~))
        ALTER TABLE {S}.tblTransferenciasCustodia DROP CONSTRAINT CK_tblTransferencias_Testigo;
    ALTER TABLE {S}.tblTransferenciasCustodia ALTER COLUMN RecibeUsuarioId uniqueidentifier NULL;
END'),
(N'tblTransferenciasCustodia: indice de quien recibe', N'
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N~IX_tblTransferencias_Recibe~ AND object_id = OBJECT_ID(N~{S}.tblTransferenciasCustodia~))
    CREATE INDEX IX_tblTransferencias_Recibe ON {S}.tblTransferenciasCustodia (RecibeUsuarioId)'),
(N'tblTransferenciasCustodia: el testigo es una tercera persona', N'
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N~CK_tblTransferencias_Testigo~ AND parent_object_id = OBJECT_ID(N~{S}.tblTransferenciasCustodia~))
    ALTER TABLE {S}.tblTransferenciasCustodia ADD CONSTRAINT CK_tblTransferencias_Testigo CHECK (TestigoUsuarioId IS NULL OR (TestigoUsuarioId <> EntregaUsuarioId AND TestigoUsuarioId <> RecibeUsuarioId))'),
(N'tblTransferenciasCustodia: quien recibe, usuario o persona externa', N'
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N~CK_tblTransferencias_Recibe~ AND parent_object_id = OBJECT_ID(N~{S}.tblTransferenciasCustodia~))
    ALTER TABLE {S}.tblTransferenciasCustodia ADD CONSTRAINT CK_tblTransferencias_Recibe CHECK (
        ((RecibeExternoNombre IS NULL AND RecibeExternoCedula IS NULL)
            OR (RecibeExternoNombre IS NOT NULL AND RecibeExternoNombre <> ~~ AND RecibeExternoCedula IS NOT NULL AND RecibeExternoCedula <> ~~))
        AND (RecibeUsuarioId IS NULL OR RecibeExternoNombre IS NULL)
        AND (RecibeUsuarioId IS NOT NULL OR RecibeExternoNombre IS NOT NULL OR (ISNULL(TipoCierre, ~~) = ~recibo_sin_entrega~ AND ISNULL(NoFirmaParte, ~~) = ~recibe~)))'),
(N'tblNotificaciones: columna Datos', N'
IF COL_LENGTH(N~{S}.tblNotificaciones~, N~Datos~) IS NULL
    ALTER TABLE {S}.tblNotificaciones ADD Datos nvarchar(max) NULL'),
(N'tblNotificaciones: Datos solo admite JSON valido', N'
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N~CK_tblNotificaciones_Datos~ AND parent_object_id = OBJECT_ID(N~{S}.tblNotificaciones~))
    ALTER TABLE {S}.tblNotificaciones ADD CONSTRAINT CK_tblNotificaciones_Datos CHECK (Datos IS NULL OR ISJSON(Datos) = 1)'),
(N'trigger de notificaciones: protege tambien Datos', N'
CREATE OR ALTER TRIGGER {S}.TR_tblNotificaciones_SoloLecturaYRespuesta
ON {S}.tblNotificaciones
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF UPDATE(Id) OR UPDATE(Tipo) OR UPDATE(Urgencia) OR UPDATE(UsuarioId) OR UPDATE(ClienteId) OR UPDATE(ViajeId) OR UPDATE(VehiculoId) OR UPDATE(HallazgoId)
       OR UPDATE(InspeccionId) OR UPDATE(Titulo) OR UPDATE(Mensaje) OR UPDATE(Canal) OR UPDATE(EnviadaAt) OR UPDATE(ErrorEnvio) OR UPDATE(ClaveDedupe) OR UPDATE(CreatedAt) OR UPDATE(Datos)
        THROW 51006, ~Una notificacion solo admite cambiar su lectura y su respuesta~, 1;
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

-- Resultado (esperado: ColumnasExternas = 2, RecibeAdmiteNulo = 1, RestriccionRecibe = 1, DatosDelAviso = 1, RestriccionDatos = 1, TriggerProtegeDatos = 1).
DECLARE @cuentas nvarchar(max) = REPLACE(N'
SELECT
    (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N~{S}.tblTransferenciasCustodia~) AND name IN (N~RecibeExternoNombre~, N~RecibeExternoCedula~)) AS ColumnasExternas,
    (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N~{S}.tblTransferenciasCustodia~) AND name = N~RecibeUsuarioId~ AND is_nullable = 1) AS RecibeAdmiteNulo,
    (SELECT COUNT(*) FROM sys.check_constraints WHERE name = N~CK_tblTransferencias_Recibe~ AND parent_object_id = OBJECT_ID(N~{S}.tblTransferenciasCustodia~)) AS RestriccionRecibe,
    (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N~{S}.tblNotificaciones~) AND name = N~Datos~) AS DatosDelAviso,
    (SELECT COUNT(*) FROM sys.check_constraints WHERE name = N~CK_tblNotificaciones_Datos~ AND parent_object_id = OBJECT_ID(N~{S}.tblNotificaciones~)) AS RestriccionDatos,
    (SELECT COUNT(*) FROM sys.sql_modules m JOIN sys.triggers t ON t.object_id = m.object_id WHERE t.name = N~TR_tblNotificaciones_SoloLecturaYRespuesta~ AND t.parent_id = OBJECT_ID(N~{S}.tblNotificaciones~) AND m.definition LIKE N~%UPDATE(Datos)%~) AS TriggerProtegeDatos;', N'{S}', @q);
SET @cuentas = REPLACE(@cuentas, N'~', N'''');
EXEC (@cuentas);
