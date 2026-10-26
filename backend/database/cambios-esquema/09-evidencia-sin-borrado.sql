/* =============================================================================
   La evidencia no se borra, para una base de IA Autos YA EXISTENTE (con 08-transferencias-custodia.sql aplicado). Se ejecuta UNA
   vez (si ya esta aplicado, no hace nada) y todo ocurre en UNA transaccion: si algo falla se revierte completo.

   Que hace:
     1. Quita el borrado en cascada de las relaciones vehiculo -> etapa, etapa -> inspeccion, inspeccion -> hallazgo, evidencia y
        transferencia, y vehiculo -> vehiculos de un viaje. Se conservan las cascadas viaje -> sus vehiculos y cuadrante -> sus
        regiones. Desde ahora, borrar un vehiculo, una etapa o una inspeccion que tenga registros debajo falla (error 547): hay que
        quitar lo de abajo de forma explicita, y lo que es evidencia no se puede quitar.
     2. Triggers INSTEAD OF DELETE que lanzan el error 51003 en tblEvidencias, tblHallazgos, tblTransferenciasCustodia y tblAuditLog, y
        en tblInspecciones cuando esta cerrada o tiene fotos, hallazgos o transferencia (un borrador vacio si se borra).
     3. tblVehiculos.Activo (bit, 1 por defecto) con su indice: la baja de un vehiculo pasa a ser logica.

   Seguridad de los datos: no borra ni cambia ningun dato; solo cambia relaciones y agrega la columna Activo (todos los vehiculos
   quedan activos). Aborta (sin cambiar nada) si falta alguna relacion o tabla esperada.

   Como ejecutarlo: abrir en SSMS, DBeaver o sqlcmd CONECTADO a la base de IA Autos, ajustar @esquema y ejecutar completo con un
   usuario con permiso para alterar tablas y crear triggers (no el usuario de la aplicacion). Ejecutarlo ANTES de desplegar la API
   que ya no depende de las cascadas.
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
IF OBJECT_ID(@q + N'.tblVehiculos', N'U') IS NULL OR OBJECT_ID(@q + N'.tblEtapas', N'U') IS NULL OR OBJECT_ID(@q + N'.tblInspecciones', N'U') IS NULL
   OR OBJECT_ID(@q + N'.tblHallazgos', N'U') IS NULL OR OBJECT_ID(@q + N'.tblEvidencias', N'U') IS NULL
   OR OBJECT_ID(@q + N'.tblTransferenciasCustodia', N'U') IS NULL OR OBJECT_ID(@q + N'.tblViajeVehiculos', N'U') IS NULL
   OR OBJECT_ID(@q + N'.tblAuditLog', N'U') IS NULL
    THROW 50001, N'Faltan tablas de IA Autos en el esquema indicado: revise @esquema.', 1;

IF COL_LENGTH(@q + N'.tblVehiculos', N'Activo') IS NOT NULL
BEGIN
    PRINT N'La evidencia sin borrado ya esta aplicada: no se hace nada.';
    RETURN;
END

IF COL_LENGTH(@q + N'.tblTransferenciasCustodia', N'TipoCierre') IS NULL
    THROW 50002, N'Falta tblTransferenciasCustodia.TipoCierre: ejecute antes 08-transferencias-custodia.sql.', 1;

-- Cada relacion que pierde la cascada debe existir con su nombre habitual: si no, el script no sabe que quitar y se detiene.
DECLARE @faltantes nvarchar(max) = (
    SELECT STRING_AGG(v.nombre, N', ')
    FROM (VALUES (N'tblViajeVehiculos', N'FK_tblViajeVehiculos_Vehiculo'), (N'tblEtapas', N'FK_tblEtapas_Vehiculo'),
                 (N'tblInspecciones', N'FK_tblInspecciones_Etapa'), (N'tblHallazgos', N'FK_tblHallazgos_Inspeccion'),
                 (N'tblEvidencias', N'FK_tblEvidencias_Inspeccion'), (N'tblTransferenciasCustodia', N'FK_tblTransferencias_Inspeccion'))
         AS v (tabla, nombre)
    WHERE NOT EXISTS (SELECT 1 FROM sys.foreign_keys f WHERE f.name = v.nombre AND f.parent_object_id = OBJECT_ID(@q + N'.' + v.tabla)));
IF @faltantes IS NOT NULL
BEGIN
    DECLARE @mensaje nvarchar(2000) = CONCAT(N'No existen las relaciones esperadas: ', @faltantes, N'. Revise que la base tenga el esquema anterior.');
    THROW 50003, @mensaje, 1;
END

-- Pasos en orden. {S} = esquema entre corchetes; ~ = comilla simple dentro del texto.
DECLARE @paso TABLE (n int IDENTITY(1, 1) PRIMARY KEY, descripcion nvarchar(100), s nvarchar(max));

INSERT @paso (descripcion, s) VALUES
(N'tblViajeVehiculos -> tblVehiculos sin cascada', N'
ALTER TABLE {S}.tblViajeVehiculos DROP CONSTRAINT FK_tblViajeVehiculos_Vehiculo;
ALTER TABLE {S}.tblViajeVehiculos WITH CHECK ADD CONSTRAINT FK_tblViajeVehiculos_Vehiculo FOREIGN KEY (VehiculoId) REFERENCES {S}.tblVehiculos (Id)'),
(N'tblEtapas -> tblVehiculos sin cascada', N'
ALTER TABLE {S}.tblEtapas DROP CONSTRAINT FK_tblEtapas_Vehiculo;
ALTER TABLE {S}.tblEtapas WITH CHECK ADD CONSTRAINT FK_tblEtapas_Vehiculo FOREIGN KEY (VehiculoId) REFERENCES {S}.tblVehiculos (Id)'),
(N'tblInspecciones -> tblEtapas sin cascada', N'
ALTER TABLE {S}.tblInspecciones DROP CONSTRAINT FK_tblInspecciones_Etapa;
ALTER TABLE {S}.tblInspecciones WITH CHECK ADD CONSTRAINT FK_tblInspecciones_Etapa FOREIGN KEY (EtapaId) REFERENCES {S}.tblEtapas (Id)'),
(N'tblHallazgos -> tblInspecciones sin cascada', N'
ALTER TABLE {S}.tblHallazgos DROP CONSTRAINT FK_tblHallazgos_Inspeccion;
ALTER TABLE {S}.tblHallazgos WITH CHECK ADD CONSTRAINT FK_tblHallazgos_Inspeccion FOREIGN KEY (InspeccionId) REFERENCES {S}.tblInspecciones (Id)'),
(N'tblEvidencias -> tblInspecciones sin cascada', N'
ALTER TABLE {S}.tblEvidencias DROP CONSTRAINT FK_tblEvidencias_Inspeccion;
ALTER TABLE {S}.tblEvidencias WITH CHECK ADD CONSTRAINT FK_tblEvidencias_Inspeccion FOREIGN KEY (InspeccionId) REFERENCES {S}.tblInspecciones (Id)'),
(N'tblTransferenciasCustodia -> tblInspecciones sin cascada', N'
ALTER TABLE {S}.tblTransferenciasCustodia DROP CONSTRAINT FK_tblTransferencias_Inspeccion;
ALTER TABLE {S}.tblTransferenciasCustodia WITH CHECK ADD CONSTRAINT FK_tblTransferencias_Inspeccion FOREIGN KEY (InspeccionId) REFERENCES {S}.tblInspecciones (Id)'),
(N'tblVehiculos: Activo e indice', N'
ALTER TABLE {S}.tblVehiculos ADD Activo bit NOT NULL CONSTRAINT DF_tblVehiculos_Activo DEFAULT (1);
CREATE INDEX IX_tblVehiculos_Activo ON {S}.tblVehiculos (Activo)'),
(N'trigger: tblEvidencias no se borra', N'
CREATE OR ALTER TRIGGER {S}.TR_tblEvidencias_NoBorrar ON {S}.tblEvidencias INSTEAD OF DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 51003, ~Este registro es evidencia y no se puede borrar~, 1;
END'),
(N'trigger: tblHallazgos no se borra', N'
CREATE OR ALTER TRIGGER {S}.TR_tblHallazgos_NoBorrar ON {S}.tblHallazgos INSTEAD OF DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 51003, ~Este registro es evidencia y no se puede borrar~, 1;
END'),
(N'trigger: tblTransferenciasCustodia no se borra', N'
CREATE OR ALTER TRIGGER {S}.TR_tblTransferenciasCustodia_NoBorrar ON {S}.tblTransferenciasCustodia INSTEAD OF DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 51003, ~Este registro es evidencia y no se puede borrar~, 1;
END'),
(N'trigger: tblAuditLog no se borra', N'
CREATE OR ALTER TRIGGER {S}.TR_tblAuditLog_NoBorrar ON {S}.tblAuditLog INSTEAD OF DELETE
AS
BEGIN
    SET NOCOUNT ON;
    THROW 51003, ~Este registro es evidencia y no se puede borrar~, 1;
END'),
(N'trigger: tblInspecciones solo se borra un borrador vacio', N'
CREATE OR ALTER TRIGGER {S}.TR_tblInspecciones_NoBorrar ON {S}.tblInspecciones INSTEAD OF DELETE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM deleted d
               WHERE d.Estado = ~cerrada~
                  OR EXISTS (SELECT 1 FROM {S}.tblEvidencias e WHERE e.InspeccionId = d.Id)
                  OR EXISTS (SELECT 1 FROM {S}.tblHallazgos h WHERE h.InspeccionId = d.Id)
                  OR EXISTS (SELECT 1 FROM {S}.tblTransferenciasCustodia t WHERE t.InspeccionId = d.Id))
        THROW 51003, ~Una inspeccion cerrada, o con fotos, hallazgos o transferencia, es evidencia y no se puede borrar~, 1;
    DELETE i FROM {S}.tblInspecciones i JOIN deleted d ON d.Id = i.Id;
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

-- Resultado (esperado: 5 triggers, 2 cascadas que se conservan y todos los vehiculos activos).
DECLARE @cuentas nvarchar(max) = REPLACE(N'
SELECT N~triggers de no borrado~ AS Dato, COUNT(*) AS Total FROM sys.triggers WHERE name LIKE N~TR[_]%[_]NoBorrar~ AND parent_id IN
    (OBJECT_ID(N~{S}.tblEvidencias~), OBJECT_ID(N~{S}.tblHallazgos~), OBJECT_ID(N~{S}.tblTransferenciasCustodia~), OBJECT_ID(N~{S}.tblAuditLog~), OBJECT_ID(N~{S}.tblInspecciones~)) UNION ALL
SELECT N~relaciones que aun borran en cascada (esperado 2: viaje -> sus vehiculos y cuadrante -> sus regiones)~, COUNT(*) FROM sys.foreign_keys WHERE delete_referential_action = 1
    AND parent_object_id IN (SELECT object_id FROM sys.tables WHERE schema_id = SCHEMA_ID(N~{E}~)) UNION ALL
SELECT N~vehiculos~, COUNT(*) FROM {S}.tblVehiculos UNION ALL
SELECT N~vehiculos activos~, COUNT(*) FROM {S}.tblVehiculos WHERE Activo = 1', N'{S}', @q);
SET @cuentas = REPLACE(REPLACE(@cuentas, N'{E}', @esquema), N'~', N'''');
EXEC (@cuentas);
