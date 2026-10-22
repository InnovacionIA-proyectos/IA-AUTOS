/* =============================================================================
   Auditoria con IP, dispositivo y detalle, e inmodificable, para una base de IA Autos YA EXISTENTE (con 09-evidencia-sin-borrado.sql aplicado). Se ejecuta UNA vez (si ya esta aplicado, no cambia
   nada) y todo ocurre en UNA transaccion: si algo falla se revierte completo.

   Que hace:
     1. tblAuditLog: gana Ip (45), Dispositivo (200), Detalle (JSON {"antes": {...}, "despues": {...}}, nulo en las lecturas), ViajeId y ClienteId (el viaje y el cliente de lo auditado, sin llave
        foranea: el registro sobrevive a lo que audita). Las filas existentes quedan con esas columnas nulas.
     2. Indices por (Entidad, EntidadId), ViajeId y ClienteId (el de CreatedAt ya existe).
     3. TR_tblAuditLog_NoModificar: un UPDATE sobre la auditoria falla con el error 51005 (el borrado ya falla con 51003).

   Seguridad de los datos: no borra ni cambia ningun dato; solo agrega columnas nulas, indices y un trigger.

   Como ejecutarlo: abrir en SSMS, DBeaver o sqlcmd CONECTADO a la base de IA Autos, ajustar @esquema y ejecutar completo, en UNA sola ejecucion de script, con un usuario con permiso para alterar
   tablas y crear triggers (no el usuario de la aplicacion). Ejecutarlo ANTES de desplegar la API.
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
IF OBJECT_ID(@q + N'.tblAuditLog', N'U') IS NULL
    THROW 50001, N'Falta la tabla tblAuditLog de IA Autos en el esquema indicado: revise @esquema.', 1;

-- Pasos en orden. {S} = esquema entre corchetes; ~ = comilla simple dentro del texto.
DECLARE @paso TABLE (n int IDENTITY(1, 1) PRIMARY KEY, descripcion nvarchar(100), s nvarchar(max));

INSERT @paso (descripcion, s) VALUES
(N'columnas de la auditoria', N'
IF COL_LENGTH(N~{S}.tblAuditLog~, N~Ip~) IS NULL ALTER TABLE {S}.tblAuditLog ADD Ip varchar(45) NULL;
IF COL_LENGTH(N~{S}.tblAuditLog~, N~Dispositivo~) IS NULL ALTER TABLE {S}.tblAuditLog ADD Dispositivo nvarchar(200) NULL;
IF COL_LENGTH(N~{S}.tblAuditLog~, N~Detalle~) IS NULL ALTER TABLE {S}.tblAuditLog ADD Detalle nvarchar(max) NULL;
IF COL_LENGTH(N~{S}.tblAuditLog~, N~ViajeId~) IS NULL ALTER TABLE {S}.tblAuditLog ADD ViajeId uniqueidentifier NULL;
IF COL_LENGTH(N~{S}.tblAuditLog~, N~ClienteId~) IS NULL ALTER TABLE {S}.tblAuditLog ADD ClienteId uniqueidentifier NULL'),
(N'CK_tblAuditLog_Detalle', N'
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N~CK_tblAuditLog_Detalle~ AND parent_object_id = OBJECT_ID(N~{S}.tblAuditLog~))
    EXEC (N~ALTER TABLE {S}.tblAuditLog WITH CHECK ADD CONSTRAINT CK_tblAuditLog_Detalle CHECK (Detalle IS NULL OR ISJSON(Detalle) = 1)~)'),
(N'indices de la auditoria', N'
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N~IX_tblAuditLog_Entidad~ AND object_id = OBJECT_ID(N~{S}.tblAuditLog~))
    EXEC (N~CREATE INDEX IX_tblAuditLog_Entidad ON {S}.tblAuditLog (Entidad, EntidadId)~);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N~IX_tblAuditLog_Viaje~ AND object_id = OBJECT_ID(N~{S}.tblAuditLog~))
    EXEC (N~CREATE INDEX IX_tblAuditLog_Viaje ON {S}.tblAuditLog (ViajeId)~);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N~IX_tblAuditLog_Cliente~ AND object_id = OBJECT_ID(N~{S}.tblAuditLog~))
    EXEC (N~CREATE INDEX IX_tblAuditLog_Cliente ON {S}.tblAuditLog (ClienteId)~)'),
(N'trigger de la auditoria: no se modifica', N'
CREATE OR ALTER TRIGGER {S}.TR_tblAuditLog_NoModificar
ON {S}.tblAuditLog
INSTEAD OF UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 51005, ~El registro de auditoria no se modifica~, 1;
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

-- Resultado (esperado: 5 columnas nuevas, 3 indices nuevos, 1 restriccion de JSON y 1 trigger).
DECLARE @cuentas nvarchar(max) = REPLACE(N'
SELECT
    (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N~{S}.tblAuditLog~) AND name IN (N~Ip~, N~Dispositivo~, N~Detalle~, N~ViajeId~, N~ClienteId~)) AS Columnas,
    (SELECT COUNT(*) FROM sys.indexes WHERE object_id = OBJECT_ID(N~{S}.tblAuditLog~) AND name IN (N~IX_tblAuditLog_Entidad~, N~IX_tblAuditLog_Viaje~, N~IX_tblAuditLog_Cliente~)) AS Indices,
    (SELECT COUNT(*) FROM sys.check_constraints WHERE name = N~CK_tblAuditLog_Detalle~ AND parent_object_id = OBJECT_ID(N~{S}.tblAuditLog~)) AS RestriccionJson,
    (SELECT COUNT(*) FROM sys.triggers WHERE name = N~TR_tblAuditLog_NoModificar~ AND parent_id = OBJECT_ID(N~{S}.tblAuditLog~)) AS Triggers', N'{S}', @q);
SET @cuentas = REPLACE(REPLACE(@cuentas, N'{E}', @esquema), N'~', N'''');
EXEC (@cuentas);
