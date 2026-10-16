/* =============================================================================
   El viaje como eje del modelo, para una base de IA Autos YA EXISTENTE (con 02, 03 y 04 aplicados).
   Se ejecuta UNA vez (si ya esta aplicado, no hace nada) y todo ocurre en UNA transaccion: si algo falla se revierte completo.

   Que hace:
     1. Crea tblViajes y tblViajeVehiculos, y agrega tblUsuarios.Cedula (el conductor se identifica por cedula en la carga por Excel).
     2. Agrega a tblEtapas el ViajeId y la marca ConPersonalApoyo, a tblInspecciones el ViajeId, y a tblOrdenesSofasa el viaje y el codigo MTrack.
     3. Migra lo existente a viajes SINTETICOS:
          - Cada vehiculo con asignaciones queda en un viaje: se toma su ultima asignacion y los vehiculos con la misma placa (antes
            "ninera"), el mismo conductor y el mismo dia comparten viaje. El viaje queda con tipo de unidad Ninera.
          - Cada vehiculo con inspecciones y sin asignacion queda en un viaje propio, con la placa de su primera inspeccion.
          - Del historial se conserva el acceso: el inspector (o el conductor) que hizo informes de un viaje queda como su inspector
            (o conductor) si el viaje no tenia uno.
          - Estado: finalizado si todas las etapas de sus vehiculos tienen informe cerrado; en_curso si hay algun informe; programado si no.
        Las inspecciones y etapas existentes pasan a colgar de su viaje. El trigger de inmutabilidad se desactiva SOLO mientras se llena
        tblInspecciones.ViajeId (ningun otro dato de un informe cerrado cambia) y se vuelve a activar.
     4. Las etapas de vehiculos sin asignacion ni inspecciones eran un esqueleto sin informacion: se borran (las etapas nacen ahora al
        agregar el VIN a un viaje). Aborta sin cambiar nada si una etapa con informes se quedara sin viaje.
     5. Cambia la unicidad de las etapas a (viaje, vehiculo, orden), vincula las ordenes Sofasa a su viaje por VIN y reemplaza la vista
        vwVehiculosMovilizados por vwViajesCompletos.
     6. Retira tblAsignaciones, despues de comprobar que todos sus vehiculos quedaron en un viaje.

   Seguridad de los datos: aborta (sin cambiar nada) si falta el tipo de unidad Ninera, si alguna placa de asignacion tiene mas de
   50 caracteres o si algun vehiculo con asignacion no quedo en un viaje.

   Como ejecutarlo: abrir en SSMS, DBeaver o sqlcmd CONECTADO a la base de IA Autos, ajustar @esquema y ejecutar completo con un
   usuario con permiso para crear tablas y vistas y desactivar triggers (no el usuario de la aplicacion). Luego dar permisos a la
   aplicacion si no los tiene por esquema:
     GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::<esquema> TO <usuario_de_la_app>;
   Ejecutarlo ANTES de desplegar la API que usa viajes.
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
IF OBJECT_ID(@q + N'.tblEtapas', N'U') IS NULL OR OBJECT_ID(@q + N'.tblInspecciones', N'U') IS NULL OR OBJECT_ID(@q + N'.tblOrdenesSofasa', N'U') IS NULL OR OBJECT_ID(@q + N'.tblTiposUnidadTransportadora', N'U') IS NULL
    THROW 50001, N'Faltan tablas de IA Autos en el esquema indicado: ejecute antes 02-catalogos-maestros.sql y revise @esquema.', 1;

IF OBJECT_ID(@q + N'.tblViajes', N'U') IS NOT NULL
BEGIN
    PRINT N'Los viajes ya estan aplicados: no se hace nada.';
    RETURN;
END

IF OBJECT_ID(@q + N'.tblAsignaciones', N'U') IS NULL
    THROW 50002, N'No existe tblAsignaciones: la base no esta en el estado previo esperado para esta migracion.', 1;

-- Precondiciones: el tipo de unidad Ninera existe y ninguna placa de asignacion se truncaria al pasar a 50 caracteres.
DECLARE @hayNinera int, @placasLargas int, @sqlPre nvarchar(max) = REPLACE(N'
    SELECT @a = COUNT(*) FROM {S}.tblTiposUnidadTransportadora WHERE Codigo = ~ninera~;
    SELECT @b = COUNT(*) FROM {S}.tblAsignaciones WHERE LEN(Ninera) > 50', N'{S}', @q);
SET @sqlPre = REPLACE(@sqlPre, N'~', N'''');
EXEC sp_executesql @sqlPre, N'@a int OUTPUT, @b int OUTPUT', @a = @hayNinera OUTPUT, @b = @placasLargas OUTPUT;
IF @hayNinera = 0
    THROW 50010, N'Falta el tipo de unidad Ninera en tblTiposUnidadTransportadora: cree el valor antes de ejecutar este script.', 1;
IF @placasLargas > 0
    THROW 50011, N'Hay placas de asignacion de mas de 50 caracteres: corrijalas antes de ejecutar este script.', 1;

-- Tablas de trabajo (de sesion): se descartan primero por si un intento anterior quedo a medias en la misma conexion.
DROP TABLE IF EXISTS #mig_ultima;
DROP TABLE IF EXISTS #mig_suelto;
DROP TABLE IF EXISTS #mig_grupo;
DROP TABLE IF EXISTS #mig_vehiculo;
CREATE TABLE #mig_ultima (VehiculoId uniqueidentifier NOT NULL PRIMARY KEY, Ninera nvarchar(100) NOT NULL, ConductorId uniqueidentifier NULL, AsignadoPor uniqueidentifier NOT NULL, CreatedAt datetimeoffset(3) NOT NULL, Dia date NOT NULL);
CREATE TABLE #mig_suelto (VehiculoId uniqueidentifier NOT NULL PRIMARY KEY, ViajeId uniqueidentifier NOT NULL DEFAULT NEWID(), Placa nvarchar(100) NULL, CreatedAt datetimeoffset(3) NOT NULL);
CREATE TABLE #mig_grupo (ViajeId uniqueidentifier NOT NULL DEFAULT NEWID(), Origen varchar(20) NOT NULL, Placa nvarchar(100) NULL, ConductorId uniqueidentifier NULL, Dia date NULL, CreadoPor uniqueidentifier NULL, CreatedAt datetimeoffset(3) NOT NULL);
CREATE TABLE #mig_vehiculo (VehiculoId uniqueidentifier NOT NULL PRIMARY KEY, ViajeId uniqueidentifier NOT NULL);

-- Pasos en orden. {S} = esquema entre corchetes; ~ = comilla simple dentro del texto.
DECLARE @paso TABLE (n int IDENTITY(1, 1) PRIMARY KEY, descripcion nvarchar(100), s nvarchar(max));

INSERT @paso (descripcion, s) VALUES
(N'tblViajes', N'
CREATE TABLE {S}.tblViajes
(
    Id             uniqueidentifier  NOT NULL CONSTRAINT PK_tblViajes PRIMARY KEY,
    CodigoMtrack   varchar(50)       NULL,
    TipoUnidadId   uniqueidentifier  NULL,
    Placa          nvarchar(50)      NULL,
    Trailer        nvarchar(50)      NULL,
    ConductorId    uniqueidentifier  NULL,
    InspectorId    uniqueidentifier  NULL,
    OrigenId       uniqueidentifier  NULL,
    DestinoId      uniqueidentifier  NULL,
    Estado         varchar(20)       NOT NULL CONSTRAINT DF_tblViajes_Estado DEFAULT (~programado~),
    CreadoPor      uniqueidentifier  NULL,
    CreatedAt      datetimeoffset(3) NOT NULL CONSTRAINT DF_tblViajes_CreatedAt DEFAULT (SYSDATETIMEOFFSET()),
    UpdatedAt      datetimeoffset(3) NOT NULL CONSTRAINT DF_tblViajes_UpdatedAt DEFAULT (SYSDATETIMEOFFSET()),
    CONSTRAINT FK_tblViajes_TipoUnidad FOREIGN KEY (TipoUnidadId) REFERENCES {S}.tblTiposUnidadTransportadora (Id),
    CONSTRAINT FK_tblViajes_Conductor FOREIGN KEY (ConductorId) REFERENCES {S}.tblUsuarios (Id),
    CONSTRAINT FK_tblViajes_Inspector FOREIGN KEY (InspectorId) REFERENCES {S}.tblUsuarios (Id),
    CONSTRAINT FK_tblViajes_Origen FOREIGN KEY (OrigenId) REFERENCES {S}.tblPuntosCustodia (Id),
    CONSTRAINT FK_tblViajes_Destino FOREIGN KEY (DestinoId) REFERENCES {S}.tblPuntosCustodia (Id),
    CONSTRAINT FK_tblViajes_CreadoPor FOREIGN KEY (CreadoPor) REFERENCES {S}.tblUsuarios (Id),
    CONSTRAINT CK_tblViajes_Estado CHECK (Estado IN (~programado~, ~en_curso~, ~finalizado~, ~cancelado~))
)'),
(N'tblViajes: indices', N'
CREATE UNIQUE INDEX UX_tblViajes_CodigoMtrack ON {S}.tblViajes (CodigoMtrack) WHERE CodigoMtrack IS NOT NULL;
CREATE INDEX IX_tblViajes_Conductor ON {S}.tblViajes (ConductorId);
CREATE INDEX IX_tblViajes_Inspector ON {S}.tblViajes (InspectorId);
CREATE INDEX IX_tblViajes_Estado ON {S}.tblViajes (Estado, CreatedAt DESC);
CREATE INDEX IX_tblViajes_TipoUnidad ON {S}.tblViajes (TipoUnidadId);
CREATE INDEX IX_tblViajes_Origen ON {S}.tblViajes (OrigenId);
CREATE INDEX IX_tblViajes_Destino ON {S}.tblViajes (DestinoId);
CREATE INDEX IX_tblViajes_CreadoPor ON {S}.tblViajes (CreadoPor)'),
(N'tblViajeVehiculos', N'
CREATE TABLE {S}.tblViajeVehiculos
(
    Id          uniqueidentifier  NOT NULL CONSTRAINT PK_tblViajeVehiculos PRIMARY KEY,
    ViajeId     uniqueidentifier  NOT NULL,
    VehiculoId  uniqueidentifier  NOT NULL,
    CreatedAt   datetimeoffset(3) NOT NULL CONSTRAINT DF_tblViajeVehiculos_CreatedAt DEFAULT (SYSDATETIMEOFFSET()),
    UpdatedAt   datetimeoffset(3) NOT NULL CONSTRAINT DF_tblViajeVehiculos_UpdatedAt DEFAULT (SYSDATETIMEOFFSET()),
    CONSTRAINT FK_tblViajeVehiculos_Viaje FOREIGN KEY (ViajeId) REFERENCES {S}.tblViajes (Id) ON DELETE CASCADE,
    CONSTRAINT FK_tblViajeVehiculos_Vehiculo FOREIGN KEY (VehiculoId) REFERENCES {S}.tblVehiculos (Id) ON DELETE CASCADE,
    CONSTRAINT UQ_tblViajeVehiculos_Viaje_Vehiculo UNIQUE (ViajeId, VehiculoId)
)'),
(N'tblViajeVehiculos: indice por vehiculo', N'CREATE INDEX IX_tblViajeVehiculos_Vehiculo ON {S}.tblViajeVehiculos (VehiculoId)'),
(N'tblUsuarios.Cedula', N'ALTER TABLE {S}.tblUsuarios ADD Cedula varchar(20) NULL'),
(N'tblUsuarios.Cedula: indice unico', N'CREATE UNIQUE INDEX UX_tblUsuarios_Cedula ON {S}.tblUsuarios (Cedula) WHERE Cedula IS NOT NULL'),
(N'tblEtapas: ViajeId y ConPersonalApoyo', N'ALTER TABLE {S}.tblEtapas ADD ViajeId uniqueidentifier NULL, ConPersonalApoyo bit NOT NULL CONSTRAINT DF_tblEtapas_ConPersonalApoyo DEFAULT (0)'),
(N'tblInspecciones: ViajeId', N'ALTER TABLE {S}.tblInspecciones ADD ViajeId uniqueidentifier NULL'),
(N'tblOrdenesSofasa: viaje y codigo MTrack', N'ALTER TABLE {S}.tblOrdenesSofasa ADD CodigoMtrack varchar(50) NULL, ViajeId uniqueidentifier NULL'),
(N'viajes sinteticos: ultima asignacion de cada vehiculo', N'
INSERT #mig_ultima (VehiculoId, Ninera, ConductorId, AsignadoPor, CreatedAt, Dia)
SELECT x.VehiculoId, x.Ninera, x.ConductorId, x.AsignadoPor, x.CreatedAt, CAST(x.CreatedAt AS date)
FROM (SELECT a.VehiculoId, a.Ninera, a.ConductorId, a.AsignadoPor, a.CreatedAt, ROW_NUMBER() OVER (PARTITION BY a.VehiculoId ORDER BY a.CreatedAt DESC, a.Id) AS rn FROM {S}.tblAsignaciones a) x
WHERE x.rn = 1'),
(N'viajes sinteticos: un viaje por placa, conductor y dia', N'
INSERT #mig_grupo (Origen, Placa, ConductorId, Dia, CreadoPor, CreatedAt)
SELECT ~asignacion~, u.Ninera, u.ConductorId, u.Dia, CAST(MAX(CAST(u.AsignadoPor AS binary(16))) AS uniqueidentifier), MIN(u.CreatedAt)
FROM #mig_ultima u GROUP BY u.Ninera, u.ConductorId, u.Dia'),
(N'viajes sinteticos: vehiculo de cada asignacion en su viaje', N'
INSERT #mig_vehiculo (VehiculoId, ViajeId)
SELECT u.VehiculoId, g.ViajeId
FROM #mig_ultima u
JOIN #mig_grupo g ON g.Origen = ~asignacion~ AND g.Placa = u.Ninera AND g.Dia = u.Dia AND (g.ConductorId = u.ConductorId OR (g.ConductorId IS NULL AND u.ConductorId IS NULL))'),
(N'viajes sinteticos: vehiculos con informes y sin asignacion', N'
INSERT #mig_suelto (VehiculoId, Placa, CreatedAt)
SELECT v.Id,
       (SELECT TOP 1 i.Placa FROM {S}.tblEtapas e JOIN {S}.tblInspecciones i ON i.EtapaId = e.Id WHERE e.VehiculoId = v.Id AND i.Placa IS NOT NULL ORDER BY i.Fecha),
       (SELECT MIN(i.CreatedAt) FROM {S}.tblEtapas e JOIN {S}.tblInspecciones i ON i.EtapaId = e.Id WHERE e.VehiculoId = v.Id)
FROM {S}.tblVehiculos v
WHERE EXISTS (SELECT 1 FROM {S}.tblEtapas e JOIN {S}.tblInspecciones i ON i.EtapaId = e.Id WHERE e.VehiculoId = v.Id)
  AND NOT EXISTS (SELECT 1 FROM #mig_vehiculo m WHERE m.VehiculoId = v.Id)'),
(N'viajes sinteticos: un viaje por vehiculo sin asignacion', N'
INSERT #mig_grupo (ViajeId, Origen, Placa, ConductorId, Dia, CreadoPor, CreatedAt)
SELECT s.ViajeId, ~inspeccion~, s.Placa, NULL, NULL, NULL, s.CreatedAt FROM #mig_suelto s;
INSERT #mig_vehiculo (VehiculoId, ViajeId) SELECT s.VehiculoId, s.ViajeId FROM #mig_suelto s'),
(N'viajes sinteticos: crear los viajes', N'
INSERT INTO {S}.tblViajes (Id, TipoUnidadId, Placa, ConductorId, CreadoPor, CreatedAt, UpdatedAt)
SELECT g.ViajeId,
       CASE WHEN g.Origen = ~asignacion~ THEN (SELECT TOP 1 t.Id FROM {S}.tblTiposUnidadTransportadora t WHERE t.Codigo = ~ninera~) END,
       g.Placa, g.ConductorId, g.CreadoPor, g.CreatedAt, g.CreatedAt
FROM #mig_grupo g'),
(N'viajes sinteticos: vehiculos de cada viaje', N'
INSERT INTO {S}.tblViajeVehiculos (Id, ViajeId, VehiculoId)
SELECT NEWID(), m.ViajeId, m.VehiculoId FROM #mig_vehiculo m'),
(N'viajes sinteticos: conserva el acceso de quien hizo informes', N'
UPDATE v SET InspectorId = x.UsuarioId
FROM {S}.tblViajes v
CROSS APPLY (SELECT TOP 1 i.UsuarioId FROM {S}.tblViajeVehiculos vv JOIN {S}.tblEtapas e ON e.VehiculoId = vv.VehiculoId JOIN {S}.tblInspecciones i ON i.EtapaId = e.Id JOIN {S}.tblUsuarios u ON u.Id = i.UsuarioId AND u.Rol = ~inspector~ WHERE vv.ViajeId = v.Id ORDER BY i.Fecha) x
WHERE v.InspectorId IS NULL;
UPDATE v SET ConductorId = x.UsuarioId
FROM {S}.tblViajes v
CROSS APPLY (SELECT TOP 1 i.UsuarioId FROM {S}.tblViajeVehiculos vv JOIN {S}.tblEtapas e ON e.VehiculoId = vv.VehiculoId JOIN {S}.tblInspecciones i ON i.EtapaId = e.Id JOIN {S}.tblUsuarios u ON u.Id = i.UsuarioId AND u.Rol = ~conductor~ WHERE vv.ViajeId = v.Id ORDER BY i.Fecha) x
WHERE v.ConductorId IS NULL'),
(N'etapas: cada una pasa al viaje de su vehiculo', N'
UPDATE e SET ViajeId = vv.ViajeId
FROM {S}.tblEtapas e JOIN {S}.tblViajeVehiculos vv ON vv.VehiculoId = e.VehiculoId'),
(N'etapas: se borra el esqueleto de vehiculos sin viaje ni informes', N'
DELETE e FROM {S}.tblEtapas e
WHERE e.ViajeId IS NULL AND NOT EXISTS (SELECT 1 FROM {S}.tblInspecciones i WHERE i.EtapaId = e.Id)'),
(N'etapas: ninguna con informes queda sin viaje', N'
IF EXISTS (SELECT 1 FROM {S}.tblEtapas WHERE ViajeId IS NULL) THROW 50020, N~Hay etapas con informes que no quedaron en ningun viaje: no se puede continuar.~, 1'),
(N'inspecciones: cada una pasa al viaje de su etapa', N'
IF EXISTS (SELECT 1 FROM sys.triggers WHERE name = ~TR_tblInspecciones_Inmutable~ AND parent_id = OBJECT_ID(~{S}.tblInspecciones~)) DISABLE TRIGGER {S}.TR_tblInspecciones_Inmutable ON {S}.tblInspecciones;
UPDATE i SET ViajeId = e.ViajeId FROM {S}.tblInspecciones i JOIN {S}.tblEtapas e ON e.Id = i.EtapaId;
IF EXISTS (SELECT 1 FROM sys.triggers WHERE name = ~TR_tblInspecciones_Inmutable~ AND parent_id = OBJECT_ID(~{S}.tblInspecciones~)) ENABLE TRIGGER {S}.TR_tblInspecciones_Inmutable ON {S}.tblInspecciones'),
(N'viajes sinteticos: estado segun sus informes', N'
UPDATE v SET Estado = ~en_curso~
FROM {S}.tblViajes v
WHERE EXISTS (SELECT 1 FROM {S}.tblInspecciones i WHERE i.ViajeId = v.Id);
UPDATE v SET Estado = ~finalizado~
FROM {S}.tblViajes v
WHERE EXISTS (SELECT 1 FROM {S}.tblEtapas e WHERE e.ViajeId = v.Id)
  AND NOT EXISTS (SELECT 1 FROM {S}.tblEtapas e WHERE e.ViajeId = v.Id AND NOT EXISTS (SELECT 1 FROM {S}.tblInspecciones i WHERE i.EtapaId = e.Id AND i.Estado = ~cerrada~))'),
(N'etapas: ViajeId obligatorio', N'ALTER TABLE {S}.tblEtapas ALTER COLUMN ViajeId uniqueidentifier NOT NULL'),
(N'etapas: llave foranea al viaje', N'ALTER TABLE {S}.tblEtapas ADD CONSTRAINT FK_tblEtapas_Viaje FOREIGN KEY (ViajeId) REFERENCES {S}.tblViajes (Id)'),
(N'etapas: unicidad por viaje, vehiculo y orden', N'
ALTER TABLE {S}.tblEtapas DROP CONSTRAINT UQ_tblEtapas_Vehiculo_Orden;
ALTER TABLE {S}.tblEtapas ADD CONSTRAINT UQ_tblEtapas_Viaje_Vehiculo_Orden UNIQUE (ViajeId, VehiculoId, Orden);
CREATE INDEX IX_tblEtapas_Vehiculo ON {S}.tblEtapas (VehiculoId)'),
(N'inspecciones: ViajeId obligatorio, llave foranea e indice', N'
ALTER TABLE {S}.tblInspecciones ALTER COLUMN ViajeId uniqueidentifier NOT NULL;
ALTER TABLE {S}.tblInspecciones ADD CONSTRAINT FK_tblInspecciones_Viaje FOREIGN KEY (ViajeId) REFERENCES {S}.tblViajes (Id);
CREATE INDEX IX_tblInspecciones_Viaje ON {S}.tblInspecciones (ViajeId)'),
(N'ordenes Sofasa: viaje por VIN', N'
UPDATE o SET ViajeId = vv.ViajeId
FROM {S}.tblOrdenesSofasa o
JOIN {S}.tblVehiculos v ON v.Vin = o.Vin
JOIN {S}.tblViajeVehiculos vv ON vv.VehiculoId = v.Id'),
(N'ordenes Sofasa: llave foranea e indice', N'
ALTER TABLE {S}.tblOrdenesSofasa ADD CONSTRAINT FK_tblOrdenesSofasa_Viaje FOREIGN KEY (ViajeId) REFERENCES {S}.tblViajes (Id) ON DELETE SET NULL;
CREATE INDEX IX_tblOrdenesSofasa_Viaje ON {S}.tblOrdenesSofasa (ViajeId)'),
(N'vista vwVehiculosMovilizados fuera', N'IF OBJECT_ID(~{S}.vwVehiculosMovilizados~, ~V~) IS NOT NULL DROP VIEW {S}.vwVehiculosMovilizados'),
(N'vista vwViajesCompletos', N'
CREATE VIEW {S}.vwViajesCompletos
AS
SELECT x.ViajeId, x.VehiculoId
FROM (
    SELECT e.ViajeId, e.VehiculoId,
           CASE WHEN EXISTS (SELECT 1 FROM {S}.tblInspecciones i WHERE i.EtapaId = e.Id AND i.Estado = ~cerrada~) THEN 1 ELSE 0 END AS Cerrada
    FROM {S}.tblEtapas e
) AS x
GROUP BY x.ViajeId, x.VehiculoId
HAVING COUNT(*) > 0 AND COUNT(*) = SUM(x.Cerrada)'),
(N'asignaciones: todas quedaron en un viaje', N'
IF EXISTS (SELECT 1 FROM {S}.tblAsignaciones a WHERE NOT EXISTS (SELECT 1 FROM {S}.tblViajeVehiculos vv WHERE vv.VehiculoId = a.VehiculoId)) THROW 50021, N~Hay asignaciones cuyo vehiculo no quedo en ningun viaje: no se retira tblAsignaciones.~, 1'),
(N'tblAsignaciones fuera', N'DROP TABLE {S}.tblAsignaciones');

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

-- Resultado: viajes migrados y lo que quedo sin viaje (esperado: 0 etapas e inspecciones sin viaje).
DECLARE @cuentas nvarchar(max) = REPLACE(N'
SELECT N~viajes~ AS Dato, COUNT(*) AS Total FROM {S}.tblViajes UNION ALL
SELECT N~vehiculos en viajes~, COUNT(*) FROM {S}.tblViajeVehiculos UNION ALL
SELECT N~viajes finalizados~, COUNT(*) FROM {S}.tblViajes WHERE Estado = ~finalizado~ UNION ALL
SELECT N~viajes en curso~, COUNT(*) FROM {S}.tblViajes WHERE Estado = ~en_curso~ UNION ALL
SELECT N~viajes programados~, COUNT(*) FROM {S}.tblViajes WHERE Estado = ~programado~ UNION ALL
SELECT N~etapas~, COUNT(*) FROM {S}.tblEtapas UNION ALL
SELECT N~etapas sin viaje~, COUNT(*) FROM {S}.tblEtapas WHERE ViajeId IS NULL UNION ALL
SELECT N~inspecciones~, COUNT(*) FROM {S}.tblInspecciones UNION ALL
SELECT N~inspecciones sin viaje~, COUNT(*) FROM {S}.tblInspecciones WHERE ViajeId IS NULL', N'{S}', @q);
SET @cuentas = REPLACE(@cuentas, N'~', N'''');
EXEC (@cuentas);

DROP TABLE IF EXISTS #mig_ultima;
DROP TABLE IF EXISTS #mig_suelto;
DROP TABLE IF EXISTS #mig_grupo;
DROP TABLE IF EXISTS #mig_vehiculo;
