/* =============================================================================
   IA Autos - LIMPIEZA de la semilla de desarrollo (S5-HU-31.5, S6-HU-09.18, S6-HU-01.12)

   Borra TODO lo que sembraron 01-semilla-base.sql, 02-semilla-ampliacion.sql y 03-semilla-hallazgos.sql y NADA mas: parte de los vehiculos con VIN que empieza por SEED, de los usuarios con correo
   @seed.iaautos.test, de los viajes con codigo MTrack SEED-..., de los lugares SEED ... y del cliente seed_prueba, y de ahi borra lo que cuelga (etapas, inspecciones, fotos, hallazgos, seguimientos,
   transferencias, notas, avisos, auditoria, ordenes Sofasa vinculadas...). Los datos reales no se tocan: ninguno tiene esos prefijos.

   SOLO PARA DESARROLLO Y PRUEBAS. NO se ejecuta contra QA ni produccion (alli no hay semilla).

   Como ejecutarlo: abrir en SSMS o DBeaver CONECTADO a la base de pruebas, con un usuario propietario, y ejecutar completo en UNA sola ejecucion de script. El esquema es db_IA_AUTOS (la base compartida
   de pruebas); en una base creada con ddl.sql el esquema es dbo: reemplace db_IA_AUTOS. por dbo. en todo el archivo. Una sola transaccion: si algo falla no se borra nada. Es idempotente.
   ============================================================================= */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    -- Los triggers (evidencia inmutable, 51003) impiden borrar historial: se desactivan SOLO durante esta transaccion y se reactivan al final.
    ALTER TABLE db_IA_AUTOS.tblAuditLog DISABLE TRIGGER ALL;
    ALTER TABLE db_IA_AUTOS.tblEtapas DISABLE TRIGGER ALL;
    ALTER TABLE db_IA_AUTOS.tblEvidencias DISABLE TRIGGER ALL;
    ALTER TABLE db_IA_AUTOS.tblHallazgos DISABLE TRIGGER ALL;
    ALTER TABLE db_IA_AUTOS.tblHallazgoSeguimiento DISABLE TRIGGER ALL;
    ALTER TABLE db_IA_AUTOS.tblInspecciones DISABLE TRIGGER ALL;
    ALTER TABLE db_IA_AUTOS.tblInspeccionExtras DISABLE TRIGGER ALL;
    ALTER TABLE db_IA_AUTOS.tblNotasAclaratorias DISABLE TRIGGER ALL;
    ALTER TABLE db_IA_AUTOS.tblNotificaciones DISABLE TRIGGER ALL;
    ALTER TABLE db_IA_AUTOS.tblNovedadesProceso DISABLE TRIGGER ALL;
    ALTER TABLE db_IA_AUTOS.tblPropuestasUbicacion DISABLE TRIGGER ALL;
    ALTER TABLE db_IA_AUTOS.tblReclamos DISABLE TRIGGER ALL;
    ALTER TABLE db_IA_AUTOS.tblTransferenciasCustodia DISABLE TRIGGER ALL;
    ALTER TABLE db_IA_AUTOS.tblViajeVehiculos DISABLE TRIGGER ALL;

    -- Lo sembrado, por sus prefijos.
    SELECT Id INTO #veh FROM db_IA_AUTOS.tblVehiculos WHERE Vin LIKE 'SEED%';
    SELECT Id INTO #usr FROM db_IA_AUTOS.tblUsuarios WHERE Email LIKE '%@seed.iaautos.test';
    SELECT Id INTO #lug FROM db_IA_AUTOS.tblLugares WHERE Nombre LIKE 'SEED %';
    SELECT Id INTO #cli FROM db_IA_AUTOS.tblClientes WHERE Codigo = 'seed_prueba';
    SELECT Id INTO #vj FROM db_IA_AUTOS.tblViajes
     WHERE CodigoMtrack LIKE 'SEED-%' OR Id IN (SELECT ViajeId FROM db_IA_AUTOS.tblViajeVehiculos WHERE VehiculoId IN (SELECT Id FROM #veh));
    SELECT Id INTO #ins FROM db_IA_AUTOS.tblInspecciones
     WHERE ViajeId IN (SELECT Id FROM #vj) OR UsuarioId IN (SELECT Id FROM #usr) OR EtapaId IN (SELECT Id FROM db_IA_AUTOS.tblEtapas WHERE VehiculoId IN (SELECT Id FROM #veh));
    SELECT Id INTO #hal FROM db_IA_AUTOS.tblHallazgos WHERE InspeccionId IN (SELECT Id FROM #ins);
    SELECT Id INTO #evi FROM db_IA_AUTOS.tblEvidencias WHERE InspeccionId IN (SELECT Id FROM #ins);

    -- Primero lo que referencia a otras filas.
    DELETE FROM db_IA_AUTOS.tblHallazgoSeguimiento WHERE HallazgoId IN (SELECT Id FROM #hal) OR UsuarioId IN (SELECT Id FROM #usr);
    DELETE FROM db_IA_AUTOS.tblNotasAclaratorias WHERE AutorId IN (SELECT Id FROM #usr) OR EntidadId IN (SELECT Id FROM #ins) OR EntidadId IN (SELECT Id FROM #hal);
    DELETE FROM db_IA_AUTOS.tblNotificaciones
     WHERE UsuarioId IN (SELECT Id FROM #usr) OR ClienteId IN (SELECT Id FROM #cli) OR ViajeId IN (SELECT Id FROM #vj) OR VehiculoId IN (SELECT Id FROM #veh)
        OR HallazgoId IN (SELECT Id FROM #hal) OR InspeccionId IN (SELECT Id FROM #ins) OR RespondidaPor IN (SELECT Id FROM #usr);
    DELETE FROM db_IA_AUTOS.tblAuditLog
     WHERE UsuarioId IN (SELECT Id FROM #usr) OR ViajeId IN (SELECT Id FROM #vj) OR ClienteId IN (SELECT Id FROM #cli)
        OR EntidadId IN (SELECT Id FROM #ins) OR EntidadId IN (SELECT Id FROM #hal) OR EntidadId IN (SELECT Id FROM #veh) OR EntidadId IN (SELECT Id FROM #vj);
    DELETE FROM db_IA_AUTOS.tblNovedadesProceso WHERE ViajeId IN (SELECT Id FROM #vj) OR UsuarioId IN (SELECT Id FROM #usr);
    DELETE FROM db_IA_AUTOS.tblPermisosSinInspector WHERE ViajeId IN (SELECT Id FROM #vj);
    DELETE FROM db_IA_AUTOS.tblReclamos WHERE ViajeId IN (SELECT Id FROM #vj) OR InspectorId IN (SELECT Id FROM #usr);
    DELETE FROM db_IA_AUTOS.tblTransferenciasCustodia WHERE InspeccionId IN (SELECT Id FROM #ins);
    DELETE FROM db_IA_AUTOS.tblViajeVehiculos WHERE ViajeId IN (SELECT Id FROM #vj) OR VehiculoId IN (SELECT Id FROM #veh);
    UPDATE db_IA_AUTOS.tblInspecciones SET ConfirmacionEvidenciaId = NULL WHERE Id IN (SELECT Id FROM #ins);
    DELETE FROM db_IA_AUTOS.tblEvidencias WHERE Id IN (SELECT Id FROM #evi);
    DELETE FROM db_IA_AUTOS.tblInspeccionExtras WHERE InspeccionId IN (SELECT Id FROM #ins);
    DELETE FROM db_IA_AUTOS.tblHallazgos WHERE Id IN (SELECT Id FROM #hal);
    DELETE FROM db_IA_AUTOS.tblInspecciones WHERE Id IN (SELECT Id FROM #ins);   -- una sola sentencia: las versiones y la que corrigen se van juntas
    DELETE FROM db_IA_AUTOS.tblEtapas WHERE ViajeId IN (SELECT Id FROM #vj) OR VehiculoId IN (SELECT Id FROM #veh);
    DELETE FROM db_IA_AUTOS.tblOrdenesSofasa WHERE Vin LIKE 'SEED%' OR CodigoMtrack LIKE 'SEED-%' OR VehiculoId IN (SELECT Id FROM #veh) OR ViajeId IN (SELECT Id FROM #vj);
    DELETE FROM db_IA_AUTOS.tblViajes WHERE Id IN (SELECT Id FROM #vj);
    DELETE FROM db_IA_AUTOS.tblVehiculos WHERE Id IN (SELECT Id FROM #veh);
    DELETE FROM db_IA_AUTOS.tblInspectorLugares WHERE UsuarioId IN (SELECT Id FROM #usr) OR LugarId IN (SELECT Id FROM #lug);
    DELETE FROM db_IA_AUTOS.tblPropuestasUbicacion WHERE LugarId IN (SELECT Id FROM #lug);
    DELETE FROM db_IA_AUTOS.tblRefreshTokens WHERE UsuarioId IN (SELECT Id FROM #usr);
    DELETE FROM db_IA_AUTOS.tblDispositivos WHERE UsuarioId IN (SELECT Id FROM #usr);
    DELETE FROM db_IA_AUTOS.tblLugares WHERE Id IN (SELECT Id FROM #lug);
    DELETE FROM db_IA_AUTOS.tblUsuarios WHERE Id IN (SELECT Id FROM #usr);
    DELETE FROM db_IA_AUTOS.tblClientes WHERE Id IN (SELECT Id FROM #cli);

    ALTER TABLE db_IA_AUTOS.tblAuditLog ENABLE TRIGGER ALL;
    ALTER TABLE db_IA_AUTOS.tblEtapas ENABLE TRIGGER ALL;
    ALTER TABLE db_IA_AUTOS.tblEvidencias ENABLE TRIGGER ALL;
    ALTER TABLE db_IA_AUTOS.tblHallazgos ENABLE TRIGGER ALL;
    ALTER TABLE db_IA_AUTOS.tblHallazgoSeguimiento ENABLE TRIGGER ALL;
    ALTER TABLE db_IA_AUTOS.tblInspecciones ENABLE TRIGGER ALL;
    ALTER TABLE db_IA_AUTOS.tblInspeccionExtras ENABLE TRIGGER ALL;
    ALTER TABLE db_IA_AUTOS.tblNotasAclaratorias ENABLE TRIGGER ALL;
    ALTER TABLE db_IA_AUTOS.tblNotificaciones ENABLE TRIGGER ALL;
    ALTER TABLE db_IA_AUTOS.tblNovedadesProceso ENABLE TRIGGER ALL;
    ALTER TABLE db_IA_AUTOS.tblPropuestasUbicacion ENABLE TRIGGER ALL;
    ALTER TABLE db_IA_AUTOS.tblReclamos ENABLE TRIGGER ALL;
    ALTER TABLE db_IA_AUTOS.tblTransferenciasCustodia ENABLE TRIGGER ALL;
    ALTER TABLE db_IA_AUTOS.tblViajeVehiculos ENABLE TRIGGER ALL;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;   -- el rollback tambien deja los triggers como estaban
    THROW;
END CATCH

-- Resultado (esperado: todo en 0).
SELECT
    (SELECT COUNT(*) FROM db_IA_AUTOS.tblVehiculos WHERE Vin LIKE 'SEED%') AS VehiculosSEED,
    (SELECT COUNT(*) FROM db_IA_AUTOS.tblUsuarios WHERE Email LIKE '%@seed.iaautos.test') AS UsuariosSEED,
    (SELECT COUNT(*) FROM db_IA_AUTOS.tblViajes WHERE CodigoMtrack LIKE 'SEED-%') AS ViajesSEED,
    (SELECT COUNT(*) FROM db_IA_AUTOS.tblLugares WHERE Nombre LIKE 'SEED %') AS LugaresSEED,
    (SELECT COUNT(*) FROM db_IA_AUTOS.tblClientes WHERE Codigo = 'seed_prueba') AS ClientesSEED,
    (SELECT COUNT(*) FROM sys.triggers WHERE is_disabled = 1 AND parent_id IN (OBJECT_ID(N'db_IA_AUTOS.tblInspecciones'), OBJECT_ID(N'db_IA_AUTOS.tblEvidencias'), OBJECT_ID(N'db_IA_AUTOS.tblHallazgos'), OBJECT_ID(N'db_IA_AUTOS.tblNovedadesProceso'))) AS TriggersDesactivados;
