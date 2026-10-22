/* =============================================================================
   IA-AUTOS - Verificacion del DDL (restricciones, triggers y vista)
   Ejecutar DESPUES de ddl.sql, sobre una base de DESARROLLO o QA (crea y borra
   datos de prueba marcados con el prefijo 'ZZ_TEST').

     sqlcmd -S <servidor> -d <base> -U <usuario> -P <password> -I -b -i verificar-ddl.sql

   Termina con error si alguna verificacion falla.
   ============================================================================= */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID('tempdb..#r') IS NOT NULL DROP TABLE #r;
CREATE TABLE #r (Prueba nvarchar(200) NOT NULL, Ok bit NOT NULL, Detalle nvarchar(400) NULL);
GO

-- Borra un vehiculo de prueba con todo lo que cuelga de el. La base no borra en cascada y protege la evidencia, asi que la limpieza
-- apaga los triggers solo mientras borra y los vuelve a encender, aunque falle.
IF OBJECT_ID('tempdb..#zz_borrar_vehiculo') IS NOT NULL DROP PROCEDURE #zz_borrar_vehiculo;
GO
CREATE PROCEDURE #zz_borrar_vehiculo @vin varchar(64)
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @v uniqueidentifier = (SELECT Id FROM dbo.tblVehiculos WHERE Vin = @vin);
    IF @v IS NULL RETURN;
    DECLARE @insp TABLE (Id uniqueidentifier PRIMARY KEY);
    INSERT @insp SELECT i.Id FROM dbo.tblInspecciones i JOIN dbo.tblEtapas e ON e.Id = i.EtapaId WHERE e.VehiculoId = @v;
    ALTER TABLE dbo.tblTransferenciasCustodia DISABLE TRIGGER ALL;
    ALTER TABLE dbo.tblEvidencias DISABLE TRIGGER ALL;
    ALTER TABLE dbo.tblHallazgos DISABLE TRIGGER ALL;
    ALTER TABLE dbo.tblInspecciones DISABLE TRIGGER ALL;
    ALTER TABLE dbo.tblHallazgoSeguimiento DISABLE TRIGGER ALL;
    ALTER TABLE dbo.tblNotificaciones DISABLE TRIGGER ALL;
    BEGIN TRY
        DELETE FROM dbo.tblNotificaciones WHERE VehiculoId = @v OR InspeccionId IN (SELECT Id FROM @insp);
        DELETE FROM dbo.tblHallazgoSeguimiento WHERE HallazgoId IN (SELECT h.Id FROM dbo.tblHallazgos h WHERE h.InspeccionId IN (SELECT Id FROM @insp));
        DELETE FROM dbo.tblTransferenciasCustodia WHERE InspeccionId IN (SELECT Id FROM @insp);
        DELETE FROM dbo.tblEvidencias WHERE InspeccionId IN (SELECT Id FROM @insp);
        -- Las unidades que "no salen" apuntan a un hallazgo, y un hallazgo preexistente apunta a su origen: se quitan primero.
        DELETE FROM dbo.tblViajeVehiculos WHERE VehiculoId = @v;
        DELETE FROM dbo.tblHallazgos WHERE EsPreexistente = 1 AND InspeccionId IN (SELECT Id FROM @insp);
        DELETE FROM dbo.tblHallazgos WHERE InspeccionId IN (SELECT Id FROM @insp);
        -- Las versiones se borran de la mas nueva a la mas antigua: una version no se puede quitar mientras otra la corrige.
        WHILE EXISTS (SELECT 1 FROM dbo.tblInspecciones WHERE Id IN (SELECT Id FROM @insp))
            DELETE FROM dbo.tblInspecciones
            WHERE Id IN (SELECT Id FROM @insp)
              AND Id NOT IN (SELECT CorrigeInspeccionId FROM dbo.tblInspecciones WHERE CorrigeInspeccionId IS NOT NULL);
        DELETE FROM dbo.tblEtapas WHERE VehiculoId = @v;
        DELETE FROM dbo.tblVehiculos WHERE Id = @v;
    END TRY
    BEGIN CATCH
        ALTER TABLE dbo.tblTransferenciasCustodia ENABLE TRIGGER ALL;
        ALTER TABLE dbo.tblEvidencias ENABLE TRIGGER ALL;
        ALTER TABLE dbo.tblHallazgos ENABLE TRIGGER ALL;
        ALTER TABLE dbo.tblInspecciones ENABLE TRIGGER ALL;
        ALTER TABLE dbo.tblHallazgoSeguimiento ENABLE TRIGGER ALL;
        ALTER TABLE dbo.tblNotificaciones ENABLE TRIGGER ALL;
        THROW;
    END CATCH
    ALTER TABLE dbo.tblTransferenciasCustodia ENABLE TRIGGER ALL;
    ALTER TABLE dbo.tblEvidencias ENABLE TRIGGER ALL;
    ALTER TABLE dbo.tblHallazgos ENABLE TRIGGER ALL;
    ALTER TABLE dbo.tblInspecciones ENABLE TRIGGER ALL;
    ALTER TABLE dbo.tblHallazgoSeguimiento ENABLE TRIGGER ALL;
    ALTER TABLE dbo.tblNotificaciones ENABLE TRIGGER ALL;
END
GO

DECLARE @u1 uniqueidentifier = NEWID(), @u2 uniqueidentifier = NEWID();
DECLARE @v uniqueidentifier = NEWID();
DECLARE @pc uniqueidentifier = (SELECT Id FROM dbo.tblEtapasCatalogo WHERE Codigo = 'cargue');
DECLARE @pt uniqueidentifier = (SELECT Id FROM dbo.tblEtapasCatalogo WHERE Codigo = 'transporte');
DECLARE @e1 uniqueidentifier = NEWID(), @e2 uniqueidentifier = NEWID();
DECLARE @i1 uniqueidentifier = NEWID(), @i2 uniqueidentifier = NEWID();
DECLARE @h1 uniqueidentifier = NEWID();
DECLARE @vj uniqueidentifier = NEWID();
DECLARE @tipoUnidad uniqueidentifier = (SELECT Id FROM dbo.tblTiposUnidadTransportadora WHERE Codigo = 'ninera');
DECLARE @cli uniqueidentifier = (SELECT TOP 1 Id FROM dbo.tblClientes WHERE Codigo = 'sofasa');

-- Datos base
INSERT dbo.tblUsuarios (Id, Nombre, Email, Rol) VALUES (@u1, N'ZZ_TEST Uno', N'zz_test_uno@ejemplo.com', 'inspector');
INSERT dbo.tblUsuarios (Id, Nombre, Email, Rol) VALUES (@u2, N'ZZ_TEST Dos', N'zz_test_dos@ejemplo.com', 'conductor');
INSERT dbo.tblVehiculos (Id, Vin, Marca, Operacion, ClienteId) VALUES (@v, 'ZZ_TEST_VIN_0001', N'Renault', 'nacional', @cli);
INSERT dbo.tblViajes (Id, CodigoMtrack, TipoUnidadId, Placa, ConductorId, InspectorId) VALUES (@vj, 'ZZ_TEST_MTK_1', @tipoUnidad, N'ZZT123', @u2, @u1);
INSERT dbo.tblViajeVehiculos (Id, ViajeId, VehiculoId) VALUES (NEWID(), @vj, @v);
INSERT dbo.tblEtapas (Id, VehiculoId, ViajeId, EtapaCatalogoId, Orden) VALUES (@e1, @v, @vj, @pc, 1);
INSERT dbo.tblEtapas (Id, VehiculoId, ViajeId, EtapaCatalogoId, Orden) VALUES (@e2, @v, @vj, @pt, 2);
INSERT dbo.tblInspecciones (Id, EtapaId, ViajeId, UsuarioId, Momento, Fecha) VALUES (@i1, @e1, @vj, @u1, 'salida', SYSDATETIMEOFFSET());
INSERT dbo.tblInspecciones (Id, EtapaId, ViajeId, UsuarioId, Momento, Fecha) VALUES (@i2, @e2, @vj, @u1, 'salida', SYSDATETIMEOFFSET());

-- 1) Vista de viajes completos (un VIN completa su ruta en un viaje cuando todas sus etapas tienen informe cerrado)
INSERT #r SELECT N'Vista: sin informes cerrados no esta movilizado', CASE WHEN NOT EXISTS (SELECT 1 FROM dbo.vwViajesCompletos WHERE ViajeId = @vj AND VehiculoId = @v) THEN 1 ELSE 0 END, NULL;
UPDATE dbo.tblInspecciones SET Estado = 'cerrada', CerradaAt = SYSDATETIMEOFFSET() WHERE Id = @i1;
INSERT #r SELECT N'Vista: con una de dos etapas cerrada sigue pendiente', CASE WHEN NOT EXISTS (SELECT 1 FROM dbo.vwViajesCompletos WHERE ViajeId = @vj AND VehiculoId = @v) THEN 1 ELSE 0 END, NULL;
UPDATE dbo.tblInspecciones SET Estado = 'cerrada', CerradaAt = SYSDATETIMEOFFSET() WHERE Id = @i2;
INSERT #r SELECT N'Vista: con todas las etapas cerradas esta movilizado', CASE WHEN EXISTS (SELECT 1 FROM dbo.vwViajesCompletos WHERE ViajeId = @vj AND VehiculoId = @v) THEN 1 ELSE 0 END, NULL;

-- 2) Triggers de inmutabilidad
BEGIN TRY
    UPDATE dbo.tblInspecciones SET Notas = N'cambio' WHERE Id = @i1;
    INSERT #r VALUES (N'Trigger: modificar inspeccion cerrada debe fallar', 0, N'no fallo');
END TRY
BEGIN CATCH
    INSERT #r VALUES (N'Trigger: modificar inspeccion cerrada debe fallar', CASE WHEN ERROR_NUMBER() = 51000 THEN 1 ELSE 0 END, ERROR_MESSAGE());
END CATCH

BEGIN TRY
    INSERT dbo.tblHallazgos (Id, InspeccionId, ParteVehiculo, ClaseDano, TamanoDano) VALUES (@h1, @i1, N'Capot', 'golpe', '1_5cm');
    INSERT #r VALUES (N'Trigger: hallazgo en inspeccion cerrada debe fallar', 0, N'no fallo');
END TRY
BEGIN CATCH
    INSERT #r VALUES (N'Trigger: hallazgo en inspeccion cerrada debe fallar', CASE WHEN ERROR_NUMBER() = 51001 THEN 1 ELSE 0 END, ERROR_MESSAGE());
END CATCH

BEGIN TRY
    INSERT dbo.tblEvidencias (Id, InspeccionId, StoragePath, SelloFecha, SelloLat, SelloLng) VALUES (NEWID(), @i1, N'zz.jpg', SYSDATETIMEOFFSET(), 4.6, -74.1);
    INSERT #r VALUES (N'Trigger: evidencia en inspeccion cerrada debe fallar', 0, N'no fallo');
END TRY
BEGIN CATCH
    INSERT #r VALUES (N'Trigger: evidencia en inspeccion cerrada debe fallar', CASE WHEN ERROR_NUMBER() = 51002 THEN 1 ELSE 0 END, ERROR_MESSAGE());
END CATCH

-- La foto del sello de quien valida un hallazgo (EsSello) si se admite en una inspeccion cerrada, pero no es de una vista (CHECK)
BEGIN TRY
    EXEC sp_executesql N'INSERT dbo.tblEvidencias (Id, InspeccionId, StoragePath, SelloFecha, SelloLat, SelloLng, EsSello) VALUES (NEWID(), @i, N''sello-base.jpg'', SYSDATETIMEOFFSET(), 4.6, -74.1, 1)', N'@i uniqueidentifier', @i1;
    INSERT #r VALUES (N'Trigger: la foto del sello se admite en una inspeccion cerrada', 1, NULL);
END TRY
BEGIN CATCH
    INSERT #r VALUES (N'Trigger: la foto del sello se admite en una inspeccion cerrada', 0, ERROR_MESSAGE());
END CATCH

BEGIN TRY
    EXEC sp_executesql N'INSERT dbo.tblEvidencias (Id, InspeccionId, VistaId, StoragePath, SelloFecha, SelloLat, SelloLng, EsSello) SELECT NEWID(), @i, Id, N''sello-base2.jpg'', SYSDATETIMEOFFSET(), 4.6, -74.1, 1 FROM dbo.tblVistasFoto WHERE Codigo = ''frontal''', N'@i uniqueidentifier', @i1;
    INSERT #r VALUES (N'CHECK el sello no es de una vista', 0, N'no fallo');
END TRY
BEGIN CATCH
    INSERT #r VALUES (N'CHECK el sello no es de una vista', CASE WHEN ERROR_NUMBER() = 547 THEN 1 ELSE 0 END, ERROR_MESSAGE());
END CATCH

INSERT #r VALUES (N'Vista vwViajesCompletos: una unidad que no sale no cuenta como completa',
    CASE WHEN OBJECT_DEFINITION(OBJECT_ID(N'dbo.vwViajesCompletos')) LIKE N'%no_sale%' THEN 1 ELSE 0 END, NULL);

-- La transferencia de custodia SI puede registrarse sobre una inspeccion cerrada (firma posterior al cierre)
BEGIN TRY
    INSERT dbo.tblTransferenciasCustodia (Id, InspeccionId, EntregaUsuarioId, RecibeUsuarioId) VALUES (NEWID(), @i1, @u1, @u2);
    INSERT #r VALUES (N'Transferencia sobre inspeccion cerrada se permite', 1, NULL);
END TRY
BEGIN CATCH
    INSERT #r VALUES (N'Transferencia sobre inspeccion cerrada se permite', 0, ERROR_MESSAGE());
END CATCH

-- 3) Restricciones (cada una debe fallar)
DECLARE @casos TABLE (Prueba nvarchar(200), Sentencia nvarchar(max), Esperado int);
INSERT @casos VALUES
 (N'CHECK rol invalido', N'INSERT dbo.tblUsuarios (Id,Nombre,Email,Rol) VALUES (NEWID(),N''ZZ_TEST'',N''zz_test_x@e.com'',''gerente'')', 547),
 (N'UNIQUE correo (sin distinguir mayusculas)', N'INSERT dbo.tblUsuarios (Id,Nombre,Email,Rol) VALUES (NEWID(),N''ZZ_TEST'',N''ZZ_TEST_UNO@EJEMPLO.COM'',''admin'')', 2601),
 (N'UNIQUE VIN (sin distinguir mayusculas)', N'INSERT dbo.tblVehiculos (Id,Vin,Marca,Operacion,ClienteId) SELECT NEWID(),''zz_test_vin_0001'',N''X'',''nacional'',Id FROM dbo.tblClientes WHERE Codigo=''sofasa''', 2627),
 (N'CHECK operacion invalida', N'INSERT dbo.tblVehiculos (Id,Vin,Marca,Operacion,ClienteId) SELECT NEWID(),''ZZ_TEST_VIN_0002'',N''X'',''otra'',Id FROM dbo.tblClientes WHERE Codigo=''sofasa''', 547),
 (N'UNIQUE (viaje, vehiculo, orden) en etapas', N'INSERT dbo.tblEtapas (Id,VehiculoId,ViajeId,EtapaCatalogoId,Orden) SELECT NEWID(),VehiculoId,ViajeId,EtapaCatalogoId,Orden FROM dbo.tblEtapas WHERE Orden=1 AND VehiculoId=(SELECT Id FROM dbo.tblVehiculos WHERE Vin=''ZZ_TEST_VIN_0001'')', 2627),
 (N'CHECK orden >= 1', N'INSERT dbo.tblEtapas (Id,VehiculoId,ViajeId,EtapaCatalogoId,Orden) SELECT NEWID(),(SELECT Id FROM dbo.tblVehiculos WHERE Vin=''ZZ_TEST_VIN_0001''),(SELECT Id FROM dbo.tblViajes WHERE CodigoMtrack=''ZZ_TEST_MTK_1''),(SELECT TOP 1 Id FROM dbo.tblEtapasCatalogo WHERE Codigo=''descargue''),0', 547),
 (N'UNIQUE (etapa, momento) original en inspecciones', N'INSERT dbo.tblInspecciones (Id,EtapaId,ViajeId,UsuarioId,Momento,Fecha) SELECT NEWID(),EtapaId,ViajeId,UsuarioId,Momento,Fecha FROM dbo.tblInspecciones WHERE Estado=''cerrada'' AND UsuarioId=(SELECT Id FROM dbo.tblUsuarios WHERE Email=N''zz_test_uno@ejemplo.com'')', 2601),
 (N'CHECK latitud fuera de rango', N'INSERT dbo.tblInspecciones (Id,EtapaId,ViajeId,UsuarioId,Momento,Fecha,Lat) SELECT NEWID(),(SELECT TOP 1 e.Id FROM dbo.tblEtapas e JOIN dbo.tblVehiculos v ON v.Id=e.VehiculoId WHERE v.Vin=''ZZ_TEST_VIN_0001'' AND e.Orden=1),(SELECT Id FROM dbo.tblViajes WHERE CodigoMtrack=''ZZ_TEST_MTK_1''),(SELECT Id FROM dbo.tblUsuarios WHERE Email=N''zz_test_uno@ejemplo.com''),''llegada'',SYSDATETIMEOFFSET(),95', 547),
 (N'CHECK estado de inspeccion', N'INSERT dbo.tblInspecciones (Id,EtapaId,ViajeId,UsuarioId,Momento,Fecha,Estado) SELECT NEWID(),(SELECT TOP 1 e.Id FROM dbo.tblEtapas e JOIN dbo.tblVehiculos v ON v.Id=e.VehiculoId WHERE v.Vin=''ZZ_TEST_VIN_0001'' AND e.Orden=1),(SELECT Id FROM dbo.tblViajes WHERE CodigoMtrack=''ZZ_TEST_MTK_1''),(SELECT Id FROM dbo.tblUsuarios WHERE Email=N''zz_test_uno@ejemplo.com''),''llegada'',SYSDATETIMEOFFSET(),''otro''', 547),
 (N'FK inspeccion inexistente en hallazgos', N'INSERT dbo.tblHallazgos (Id,InspeccionId,ParteVehiculo,ClaseDano,TamanoDano) VALUES (NEWID(),NEWID(),N''Capot'',''golpe'',''1_5cm'')', 547),
 (N'FK clase de dano invalida', N'DECLARE @ins uniqueidentifier=(SELECT TOP 1 i.Id FROM dbo.tblInspecciones i JOIN dbo.tblEtapas e ON e.Id=i.EtapaId JOIN dbo.tblVehiculos v ON v.Id=e.VehiculoId WHERE v.Vin=''ZZ_TEST_VIN_0001''); INSERT dbo.tblHallazgos (Id,InspeccionId,ParteVehiculo,ClaseDano,TamanoDano) VALUES (NEWID(),@ins,N''Capot'',''inventada'',''1_5cm'')', 547),
 (N'CHECK JSON de coordenadas', N'DECLARE @ins uniqueidentifier=(SELECT TOP 1 i.Id FROM dbo.tblInspecciones i JOIN dbo.tblEtapas e ON e.Id=i.EtapaId JOIN dbo.tblVehiculos v ON v.Id=e.VehiculoId WHERE v.Vin=''ZZ_TEST_VIN_0001''); INSERT dbo.tblHallazgos (Id,InspeccionId,ParteVehiculo,ClaseDano,TamanoDano,CoordsDiagrama) VALUES (NEWID(),@ins,N''Capot'',''golpe'',''1_5cm'',N''no es json'')', 547),
 (N'UNIQUE transferencia por inspeccion', N'INSERT dbo.tblTransferenciasCustodia (Id,InspeccionId,EntregaUsuarioId,RecibeUsuarioId) SELECT NEWID(),InspeccionId,EntregaUsuarioId,RecibeUsuarioId FROM dbo.tblTransferenciasCustodia WHERE EntregaUsuarioId=(SELECT Id FROM dbo.tblUsuarios WHERE Email=N''zz_test_uno@ejemplo.com'')', 2627),
 (N'UNIQUE orden Sofasa por SourceId', N'INSERT dbo.tblOrdenesSofasa (Id,SourceId) VALUES (NEWID(),-424242); INSERT dbo.tblOrdenesSofasa (Id,SourceId) VALUES (NEWID(),-424242)', 2627);

DECLARE @prueba nvarchar(200), @sql nvarchar(max), @esp int;
DECLARE c CURSOR LOCAL FAST_FORWARD FOR SELECT Prueba, Sentencia, Esperado FROM @casos;
OPEN c; FETCH NEXT FROM c INTO @prueba, @sql, @esp;
WHILE @@FETCH_STATUS = 0
BEGIN
    BEGIN TRY
        EXEC (@sql);
        INSERT #r VALUES (@prueba, 0, N'la sentencia no fallo');
    END TRY
    BEGIN CATCH
        INSERT #r VALUES (@prueba, CASE WHEN ERROR_NUMBER() = @esp THEN 1 ELSE 0 END, N'error ' + CAST(ERROR_NUMBER() AS nvarchar(10)) + N': ' + LEFT(ERROR_MESSAGE(), 150));
    END CATCH
    FETCH NEXT FROM c INTO @prueba, @sql, @esp;
END
CLOSE c; DEALLOCATE c;

-- 4) Catalogos maestros: semillas, FK de uso y reglas de los cuadrantes
INSERT #r SELECT N'Catalogo: 3 etapas (cargue, transporte, descargue)',
    CASE WHEN (SELECT COUNT(*) FROM dbo.tblEtapasCatalogo WHERE Codigo IN ('cargue','transporte','descargue')) = 3 THEN 1 ELSE 0 END, NULL;
INSERT #r SELECT N'Catalogo: 6 puntos de custodia (lugares)',
    CASE WHEN (SELECT COUNT(*) FROM dbo.tblPuntosCustodia WHERE Codigo IN ('cedi_1','origen','certificadora','destino','cedi_2','concesionario')) = 6 THEN 1 ELSE 0 END, NULL;
INSERT #r SELECT N'Catalogo: 4 tipos de unidad transportadora',
    CASE WHEN (SELECT COUNT(*) FROM dbo.tblTiposUnidadTransportadora WHERE Codigo IN ('ninera','nodriza','quintinaria','planchon_grua')) = 4 THEN 1 ELSE 0 END, NULL;
INSERT #r SELECT N'Catalogo: 10 clases de dano', CASE WHEN (SELECT COUNT(*) FROM dbo.tblClasesDano) >= 10 THEN 1 ELSE 0 END, NULL;
INSERT #r SELECT N'Catalogo: 6 tamanos de dano', CASE WHEN (SELECT COUNT(*) FROM dbo.tblTamanosDano) >= 6 THEN 1 ELSE 0 END, NULL;
INSERT #r SELECT N'Catalogo: 4 niveles de severidad (V1+, V1, V2, V3)',
    CASE WHEN (SELECT COUNT(*) FROM dbo.tblNivelesSeveridad WHERE Codigo IN ('v1_plus','v1','v2','v3')) = 4 THEN 1 ELSE 0 END, NULL;
INSERT #r SELECT N'Catalogo: cada nivel de severidad trae su accion',
    CASE WHEN (SELECT COUNT(*) FROM dbo.tblNivelesSeveridad WHERE Codigo IN ('v1_plus','v1','v2','v3') AND Accion IS NOT NULL) = 4 THEN 1 ELSE 0 END, NULL;
INSERT #r SELECT N'Catalogo: 9 vistas de foto (7 obligatorias y 2 adicionales)',
    CASE WHEN (SELECT COUNT(*) FROM dbo.tblVistasFoto) >= 9
          AND (SELECT COUNT(*) FROM dbo.tblVistasFoto WHERE Obligatoria = 1) >= 7
          AND (SELECT COUNT(*) FROM dbo.tblVistasFoto WHERE Obligatoria = 0 AND Codigo IN ('piezas_baul','cojineria')) = 2
         THEN 1 ELSE 0 END, NULL;

-- Valores en uso: una FK impide borrarlos (la API responde 409 y solo permite inactivarlos).
DECLARE @v3 uniqueidentifier = NEWID(), @e3 uniqueidentifier = NEWID(), @i3 uniqueidentifier = NEWID(), @vj3 uniqueidentifier = NEWID();
DECLARE @lugar uniqueidentifier = (SELECT Id FROM dbo.tblPuntosCustodia WHERE Codigo = 'cedi_1');
DECLARE @etapaDescargue uniqueidentifier = (SELECT Id FROM dbo.tblEtapasCatalogo WHERE Codigo = 'descargue');
INSERT dbo.tblVehiculos (Id, Vin, Marca, Operacion, ClienteId) VALUES (@v3, 'ZZ_TEST_VIN_0003', N'Renault', 'nacional', @cli);
INSERT dbo.tblViajes (Id, CodigoMtrack, TipoUnidadId, Placa, OrigenId) VALUES (@vj3, 'ZZ_TEST_MTK_3', @tipoUnidad, N'ZZT333', @lugar);
INSERT dbo.tblViajeVehiculos (Id, ViajeId, VehiculoId) VALUES (NEWID(), @vj3, @v3);
INSERT dbo.tblEtapas (Id, VehiculoId, ViajeId, EtapaCatalogoId, PuntoCustodiaId, Orden) VALUES (@e3, @v3, @vj3, @etapaDescargue, @lugar, 1);
INSERT dbo.tblInspecciones (Id, EtapaId, ViajeId, UsuarioId, Momento, Fecha) VALUES (@i3, @e3, @vj3, @u1, 'salida', SYSDATETIMEOFFSET());
INSERT dbo.tblHallazgos (Id, InspeccionId, ParteVehiculo, ClaseDano, TamanoDano) VALUES (NEWID(), @i3, N'Capot', 'golpe', '1_5cm');

DECLARE @vistaFrontal uniqueidentifier = (SELECT Id FROM dbo.tblVistasFoto WHERE Codigo = 'frontal');
DECLARE @cuadranteTest uniqueidentifier = NEWID();
INSERT dbo.tblCuadrantes (Id, TipoVehiculo, Numero, Nombre) VALUES (@cuadranteTest, 'zz_test', 1, N'ZZ_TEST cuadrante');
INSERT dbo.tblCuadranteVistas (Id, CuadranteId, VistaFotoId, Geometria) VALUES (NEWID(), @cuadranteTest, @vistaFrontal, N'{"puntos":[[0,0],[1,0],[1,1]]}');
INSERT #r SELECT N'Cuadrante: se guarda su zona y su poligono en una vista',
    CASE WHEN EXISTS (SELECT 1 FROM dbo.tblCuadranteVistas WHERE CuadranteId = @cuadranteTest AND VistaFotoId = @vistaFrontal) THEN 1 ELSE 0 END, NULL;

DECLARE @casosCat TABLE (Prueba nvarchar(200), Sentencia nvarchar(max), Esperado int);
INSERT @casosCat VALUES
 (N'FK tamano de dano invalido en hallazgos', N'INSERT dbo.tblHallazgos (Id,InspeccionId,ParteVehiculo,ClaseDano,TamanoDano) SELECT NEWID(), InspeccionId, N''Capot'', ''golpe'', ''inventado'' FROM dbo.tblHallazgos WHERE ParteVehiculo = N''Capot''', 547),
 (N'No se borra una clase de dano en uso', N'DELETE FROM dbo.tblClasesDano WHERE Codigo = ''golpe''', 547),
 (N'No se borra un tamano de dano en uso', N'DELETE FROM dbo.tblTamanosDano WHERE Codigo = ''1_5cm''', 547),
 (N'No se borra una etapa del catalogo en uso', N'DELETE FROM dbo.tblEtapasCatalogo WHERE Codigo = ''descargue''', 547),
 (N'No se borra un punto de custodia en uso', N'DELETE FROM dbo.tblPuntosCustodia WHERE Codigo = ''cedi_1''', 547),
 (N'No se borra una vista con poligonos de cuadrante', N'DELETE FROM dbo.tblVistasFoto WHERE Codigo = ''frontal''', 547),
 (N'UNIQUE (tipo de vehiculo, numero) en cuadrantes', N'INSERT dbo.tblCuadrantes (Id,TipoVehiculo,Numero,Nombre) SELECT NEWID(),TipoVehiculo,Numero,Nombre FROM dbo.tblCuadrantes WHERE TipoVehiculo = ''zz_test''', 2627),
 (N'UNIQUE (cuadrante, vista) en poligonos', N'INSERT dbo.tblCuadranteVistas (Id,CuadranteId,VistaFotoId,Geometria) SELECT NEWID(),CuadranteId,VistaFotoId,Geometria FROM dbo.tblCuadranteVistas WHERE CuadranteId IN (SELECT Id FROM dbo.tblCuadrantes WHERE TipoVehiculo = ''zz_test'')', 2627),
 (N'CHECK geometria JSON en poligonos', N'INSERT dbo.tblCuadranteVistas (Id,CuadranteId,VistaFotoId,Geometria) SELECT NEWID(),Id,(SELECT Id FROM dbo.tblVistasFoto WHERE Codigo = ''superior''),N''no es json'' FROM dbo.tblCuadrantes WHERE TipoVehiculo = ''zz_test''', 547),
 (N'UNIQUE codigo en catalogos', N'INSERT dbo.tblClasesDano (Id,Codigo,Nombre) VALUES (NEWID(),''GOLPE'',N''Repetido'')', 2627);

DECLARE @pruebaCat nvarchar(200), @sqlCat nvarchar(max), @espCat int;
DECLARE cCat CURSOR LOCAL FAST_FORWARD FOR SELECT Prueba, Sentencia, Esperado FROM @casosCat;
OPEN cCat; FETCH NEXT FROM cCat INTO @pruebaCat, @sqlCat, @espCat;
WHILE @@FETCH_STATUS = 0
BEGIN
    BEGIN TRY
        EXEC (@sqlCat);
        INSERT #r VALUES (@pruebaCat, 0, N'la sentencia no fallo');
    END TRY
    BEGIN CATCH
        INSERT #r VALUES (@pruebaCat, CASE WHEN ERROR_NUMBER() = @espCat THEN 1 ELSE 0 END, N'error ' + CAST(ERROR_NUMBER() AS nvarchar(10)) + N': ' + LEFT(ERROR_MESSAGE(), 150));
    END CATCH
    FETCH NEXT FROM cCat INTO @pruebaCat, @sqlCat, @espCat;
END
CLOSE cCat; DEALLOCATE cCat;

-- Un valor inactivo sigue siendo valido para lo ya guardado: inactivar no rompe ninguna FK.
UPDATE dbo.tblClasesDano SET Activo = 0 WHERE Codigo = 'golpe';
INSERT #r SELECT N'Inactivar una clase en uso es valido y conserva los hallazgos', CASE WHEN EXISTS (SELECT 1 FROM dbo.tblHallazgos WHERE ClaseDano = 'golpe') THEN 1 ELSE 0 END, NULL;
UPDATE dbo.tblClasesDano SET Activo = 1 WHERE Codigo = 'golpe';
-- Borrar el cuadrante borra sus poligonos (cascada).
DELETE FROM dbo.tblCuadrantes WHERE TipoVehiculo = 'zz_test';
INSERT #r SELECT N'Borrar un cuadrante borra sus poligonos', CASE WHEN NOT EXISTS (SELECT 1 FROM dbo.tblCuadranteVistas WHERE CuadranteId = @cuadranteTest) THEN 1 ELSE 0 END, NULL;
EXEC #zz_borrar_vehiculo 'ZZ_TEST_VIN_0003';
DELETE FROM dbo.tblViajes WHERE CodigoMtrack = 'ZZ_TEST_MTK_3';

-- 5) Viajes: codigo MTrack unico, estados, referencias, cascada hacia sus VIN y proteccion de lo que cuelga de ellos
INSERT #r SELECT N'Viaje: se guarda con su unidad, conductor e inspector',
    CASE WHEN EXISTS (SELECT 1 FROM dbo.tblViajes WHERE Id = @vj AND ConductorId = @u2 AND InspectorId = @u1 AND Estado = 'programado') THEN 1 ELSE 0 END, NULL;
INSERT dbo.tblViajes (Id, Placa) VALUES (NEWID(), N'ZZT_SIN_CODIGO_1');
INSERT dbo.tblViajes (Id, Placa) VALUES (NEWID(), N'ZZT_SIN_CODIGO_2');
INSERT #r SELECT N'Viaje: varios viajes sin codigo MTrack son validos (los migrados no lo tienen)',
    CASE WHEN (SELECT COUNT(*) FROM dbo.tblViajes WHERE Placa LIKE N'ZZT_SIN_CODIGO%') = 2 THEN 1 ELSE 0 END, NULL;

DECLARE @casosViaje TABLE (Prueba nvarchar(200), Sentencia nvarchar(max), Esperado int);
INSERT @casosViaje VALUES
 (N'UNIQUE codigo MTrack (sin distinguir mayusculas)', N'INSERT dbo.tblViajes (Id, CodigoMtrack) VALUES (NEWID(), ''zz_test_mtk_1'')', 2601),
 (N'CHECK estado del viaje', N'INSERT dbo.tblViajes (Id, CodigoMtrack, Estado) VALUES (NEWID(), ''ZZ_TEST_MTK_E'', ''volando'')', 547),
 (N'FK conductor inexistente en viajes', N'INSERT dbo.tblViajes (Id, CodigoMtrack, ConductorId) VALUES (NEWID(), ''ZZ_TEST_MTK_C'', NEWID())', 547),
 (N'FK unidad inexistente en viajes', N'INSERT dbo.tblViajes (Id, CodigoMtrack, TipoUnidadId) VALUES (NEWID(), ''ZZ_TEST_MTK_U'', NEWID())', 547),
 (N'UNIQUE (viaje, vehiculo) en tblViajeVehiculos', N'INSERT dbo.tblViajeVehiculos (Id, ViajeId, VehiculoId) SELECT NEWID(), ViajeId, VehiculoId FROM dbo.tblViajeVehiculos WHERE VehiculoId = (SELECT Id FROM dbo.tblVehiculos WHERE Vin = ''ZZ_TEST_VIN_0001'')', 2627),
 (N'Una etapa exige un viaje existente', N'INSERT dbo.tblEtapas (Id,VehiculoId,ViajeId,EtapaCatalogoId,Orden) SELECT NEWID(),(SELECT Id FROM dbo.tblVehiculos WHERE Vin=''ZZ_TEST_VIN_0001''),NEWID(),(SELECT TOP 1 Id FROM dbo.tblEtapasCatalogo WHERE Codigo=''descargue''),7', 547),
 (N'Una inspeccion exige un viaje existente', N'INSERT dbo.tblInspecciones (Id,EtapaId,ViajeId,UsuarioId,Momento,Fecha) SELECT NEWID(),(SELECT TOP 1 e.Id FROM dbo.tblEtapas e JOIN dbo.tblVehiculos v ON v.Id=e.VehiculoId WHERE v.Vin=''ZZ_TEST_VIN_0001'' AND e.Orden=1),NEWID(),(SELECT Id FROM dbo.tblUsuarios WHERE Email=N''zz_test_uno@ejemplo.com''),''llegada'',SYSDATETIMEOFFSET()', 547),
 (N'No se borra un viaje con etapas', N'DELETE FROM dbo.tblViajes WHERE CodigoMtrack = ''ZZ_TEST_MTK_1''', 547),
 (N'No se borra un usuario que es conductor de un viaje', N'DELETE FROM dbo.tblUsuarios WHERE Email = N''zz_test_dos@ejemplo.com''', 547),
 (N'UNIQUE cedula (entre quienes la tienen)', N'INSERT dbo.tblUsuarios (Id,Nombre,Email,Rol,Cedula) VALUES (NEWID(),N''ZZ_TEST'',N''zz_test_c1@e.com'',''conductor'',''ZZ99999''); INSERT dbo.tblUsuarios (Id,Nombre,Email,Rol,Cedula) VALUES (NEWID(),N''ZZ_TEST'',N''zz_test_c2@e.com'',''conductor'',''ZZ99999'')', 2601);

DECLARE @pruebaViaje nvarchar(200), @sqlViaje nvarchar(max), @espViaje int;
DECLARE cViaje CURSOR LOCAL FAST_FORWARD FOR SELECT Prueba, Sentencia, Esperado FROM @casosViaje;
OPEN cViaje; FETCH NEXT FROM cViaje INTO @pruebaViaje, @sqlViaje, @espViaje;
WHILE @@FETCH_STATUS = 0
BEGIN
    BEGIN TRY
        EXEC (@sqlViaje);
        INSERT #r VALUES (@pruebaViaje, 0, N'la sentencia no fallo');
    END TRY
    BEGIN CATCH
        INSERT #r VALUES (@pruebaViaje, CASE WHEN ERROR_NUMBER() = @espViaje THEN 1 ELSE 0 END, N'error ' + CAST(ERROR_NUMBER() AS nvarchar(10)) + N': ' + LEFT(ERROR_MESSAGE(), 150));
    END CATCH
    FETCH NEXT FROM cViaje INTO @pruebaViaje, @sqlViaje, @espViaje;
END
CLOSE cViaje; DEALLOCATE cViaje;
DELETE FROM dbo.tblUsuarios WHERE Email LIKE N'zz_test_c_@e.com';

-- Un viaje sin etapas se borra y arrastra a sus VIN; la orden Sofasa que lo referenciaba pierde el vinculo (SET NULL) y el vehiculo sigue existiendo.
DECLARE @vj4 uniqueidentifier = NEWID(), @v4 uniqueidentifier = NEWID();
INSERT dbo.tblVehiculos (Id, Vin, Marca, Operacion, ClienteId) VALUES (@v4, 'ZZ_TEST_VIN_0004', N'Renault', 'nacional', @cli);
INSERT dbo.tblViajes (Id, CodigoMtrack) VALUES (@vj4, 'ZZ_TEST_MTK_4');
INSERT dbo.tblViajeVehiculos (Id, ViajeId, VehiculoId) VALUES (NEWID(), @vj4, @v4);
INSERT dbo.tblOrdenesSofasa (Id, SourceId, ViajeId) VALUES (NEWID(), -424243, @vj4);
DELETE FROM dbo.tblViajes WHERE Id = @vj4;
INSERT #r SELECT N'Borrar un viaje sin etapas borra sus VIN del viaje y suelta las ordenes Sofasa',
    CASE WHEN NOT EXISTS (SELECT 1 FROM dbo.tblViajeVehiculos WHERE ViajeId = @vj4)
          AND EXISTS (SELECT 1 FROM dbo.tblOrdenesSofasa WHERE SourceId = -424243 AND ViajeId IS NULL)
          AND EXISTS (SELECT 1 FROM dbo.tblVehiculos WHERE Id = @v4) THEN 1 ELSE 0 END, NULL;
DELETE FROM dbo.tblOrdenesSofasa WHERE SourceId = -424243;
DELETE FROM dbo.tblVehiculos WHERE Id = @v4;

-- 6) Identidad y clientes: credencial por cedula y PIN, correo opcional, bloqueo y cliente
INSERT #r SELECT N'Cliente: el cliente actual (sofasa) esta sembrado', CASE WHEN @cli IS NOT NULL THEN 1 ELSE 0 END, NULL;
DECLARE @uPin uniqueidentifier = NEWID(), @uSin1 uniqueidentifier = NEWID();
INSERT dbo.tblUsuarios (Id, Nombre, Email, Rol, Cedula, TipoCredencial, PinHash, DebeCambiarPin, ClienteId) VALUES (@uPin, N'ZZ_TEST Pin', NULL, 'conductor', 'ZZ77001', 'pin', N'pbkdf2-sha256$1$a$b', 1, @cli);
INSERT dbo.tblUsuarios (Id, Nombre, Email, Rol, Cedula, TipoCredencial, ClienteId) VALUES (@uSin1, N'ZZ_TEST Pin2', NULL, 'inspector', 'ZZ77002', 'pin', @cli);
INSERT #r SELECT N'Usuario: varios usuarios sin correo (con cedula y PIN) son validos',
    CASE WHEN (SELECT COUNT(*) FROM dbo.tblUsuarios WHERE Nombre LIKE N'ZZ_TEST Pin%' AND Email IS NULL AND TipoCredencial = 'pin') = 2 THEN 1 ELSE 0 END, NULL;
INSERT #r SELECT N'Usuario: nace sin intentos fallidos ni bloqueo',
    CASE WHEN EXISTS (SELECT 1 FROM dbo.tblUsuarios WHERE Id = @uSin1 AND IntentosFallidos = 0 AND BloqueadoHasta IS NULL AND UltimoAccesoAt IS NULL) THEN 1 ELSE 0 END, NULL;
INSERT #r SELECT N'Usuario: un usuario creado con correo toma el tipo de credencial contrasena por defecto',
    CASE WHEN EXISTS (SELECT 1 FROM dbo.tblUsuarios WHERE Id = @u1 AND TipoCredencial = 'password' AND DebeCambiarPin = 0) THEN 1 ELSE 0 END, NULL;

DECLARE @casosId TABLE (Prueba nvarchar(200), Sentencia nvarchar(max), Esperado int);
INSERT @casosId VALUES
 (N'CHECK tipo de credencial invalido', N'INSERT dbo.tblUsuarios (Id,Nombre,Email,Rol,TipoCredencial) VALUES (NEWID(),N''ZZ_TEST'',N''zz_test_t@e.com'',''admin'',''huella'')', 547),
 (N'CHECK contrasena sin correo', N'INSERT dbo.tblUsuarios (Id,Nombre,Email,Rol,Cedula,TipoCredencial) VALUES (NEWID(),N''ZZ_TEST'',NULL,''admin'',''ZZ77003'',''password'')', 547),
 (N'CHECK PIN sin cedula', N'INSERT dbo.tblUsuarios (Id,Nombre,Email,Rol,TipoCredencial) VALUES (NEWID(),N''ZZ_TEST'',N''zz_test_p@e.com'',''conductor'',''pin'')', 547),
 (N'UNIQUE cedula con PIN', N'INSERT dbo.tblUsuarios (Id,Nombre,Email,Rol,Cedula,TipoCredencial) VALUES (NEWID(),N''ZZ_TEST'',NULL,''conductor'',''ZZ77001'',''pin'')', 2601),
 (N'FK cliente inexistente en usuarios', N'INSERT dbo.tblUsuarios (Id,Nombre,Email,Rol,ClienteId) VALUES (NEWID(),N''ZZ_TEST'',N''zz_test_k@e.com'',''admin'',NEWID())', 547),
 (N'Un vehiculo exige cliente', N'INSERT dbo.tblVehiculos (Id,Vin,Marca,Operacion,ClienteId) VALUES (NEWID(),''ZZ_TEST_VIN_0005'',N''X'',''nacional'',NULL)', 515),
 (N'FK cliente inexistente en vehiculos', N'INSERT dbo.tblVehiculos (Id,Vin,Marca,Operacion,ClienteId) VALUES (NEWID(),''ZZ_TEST_VIN_0005'',N''X'',''nacional'',NEWID())', 547),
 (N'UNIQUE codigo de cliente', N'INSERT dbo.tblClientes (Id,Codigo,Nombre) VALUES (NEWID(),''SOFASA'',N''Repetido'')', 2627),
 (N'No se borra un cliente con usuarios o vehiculos', N'DELETE FROM dbo.tblClientes WHERE Codigo = ''sofasa''', 547);

DECLARE @pruebaId nvarchar(200), @sqlId nvarchar(max), @espId int;
DECLARE cId CURSOR LOCAL FAST_FORWARD FOR SELECT Prueba, Sentencia, Esperado FROM @casosId;
OPEN cId; FETCH NEXT FROM cId INTO @pruebaId, @sqlId, @espId;
WHILE @@FETCH_STATUS = 0
BEGIN
    BEGIN TRY
        EXEC (@sqlId);
        INSERT #r VALUES (@pruebaId, 0, N'la sentencia no fallo');
    END TRY
    BEGIN CATCH
        INSERT #r VALUES (@pruebaId, CASE WHEN ERROR_NUMBER() = @espId THEN 1 ELSE 0 END, N'error ' + CAST(ERROR_NUMBER() AS nvarchar(10)) + N': ' + LEFT(ERROR_MESSAGE(), 150));
    END CATCH
    FETCH NEXT FROM cId INTO @pruebaId, @sqlId, @espId;
END
CLOSE cId; DEALLOCATE cId;

-- Un cliente sin usuarios ni vehiculos si se puede borrar.
INSERT dbo.tblClientes (Id, Codigo, Nombre) VALUES (NEWID(), 'zz_test', N'ZZ_TEST cliente');
DELETE FROM dbo.tblClientes WHERE Codigo = 'zz_test';
INSERT #r SELECT N'Un cliente sin usuarios ni vehiculos se puede borrar', CASE WHEN NOT EXISTS (SELECT 1 FROM dbo.tblClientes WHERE Codigo = 'zz_test') THEN 1 ELSE 0 END, NULL;

-- 7) Inspeccion de salida y de llegada: versiones, hora de cierre y fotos selladas
DECLARE @vistaFrontal2 uniqueidentifier = (SELECT Id FROM dbo.tblVistasFoto WHERE Codigo = 'frontal');
DECLARE @v5 uniqueidentifier = NEWID(), @vj5 uniqueidentifier = NEWID(), @e5 uniqueidentifier = NEWID();
DECLARE @s5 uniqueidentifier = NEWID(), @l5 uniqueidentifier = NEWID(), @c5 uniqueidentifier = NEWID();
INSERT dbo.tblVehiculos (Id, Vin, Marca, Operacion, ClienteId) VALUES (@v5, 'ZZ_TEST_VIN_0006', N'Renault', 'nacional', @cli);
INSERT dbo.tblViajes (Id, CodigoMtrack) VALUES (@vj5, 'ZZ_TEST_MTK_6');
INSERT dbo.tblViajeVehiculos (Id, ViajeId, VehiculoId) VALUES (NEWID(), @vj5, @v5);
INSERT dbo.tblEtapas (Id, VehiculoId, ViajeId, EtapaCatalogoId, Orden) VALUES (@e5, @v5, @vj5, @pc, 1);
INSERT dbo.tblEtapas (Id, VehiculoId, ViajeId, EtapaCatalogoId, Orden) VALUES (NEWID(), @v5, @vj5, @pt, 2);
INSERT dbo.tblInspecciones (Id, EtapaId, ViajeId, UsuarioId, Momento, Fecha) VALUES (@s5, @e5, @vj5, @u1, 'salida', SYSDATETIMEOFFSET());
INSERT dbo.tblInspecciones (Id, EtapaId, ViajeId, UsuarioId, Momento, Fecha) VALUES (@l5, @e5, @vj5, @u1, 'llegada', SYSDATETIMEOFFSET());
INSERT #r SELECT N'Inspeccion: una etapa admite una salida y una llegada originales',
    CASE WHEN (SELECT COUNT(*) FROM dbo.tblInspecciones WHERE EtapaId = @e5) = 2 THEN 1 ELSE 0 END, NULL;
INSERT #r SELECT N'Inspeccion: nace en version 1, sin hora de cierre y con hora de inicio',
    CASE WHEN EXISTS (SELECT 1 FROM dbo.tblInspecciones WHERE Id = @s5 AND Version = 1 AND CorrigeInspeccionId IS NULL AND CerradaAt IS NULL AND IniciadaAt IS NOT NULL) THEN 1 ELSE 0 END, NULL;

-- Una foto de una vista con todos sus sellos (la del dispositivo y la del servidor) y su nota
INSERT dbo.tblEvidencias (Id, InspeccionId, VistaId, StoragePath, SelloFecha, SelloLat, SelloLng, Precision, DispositivoId, HashSha256, Nota)
VALUES (NEWID(), @s5, @vistaFrontal2, N'zz-vista.jpg', SYSDATETIMEOFFSET(), 4.6, -74.1, 5.5, N'zz-dispositivo', REPLICATE('a', 64), N'Falta una pieza');
INSERT #r SELECT N'Evidencia: una foto de una vista guarda su sello, precision, dispositivo, hash y nota',
    CASE WHEN EXISTS (SELECT 1 FROM dbo.tblEvidencias WHERE InspeccionId = @s5 AND VistaId = @vistaFrontal2 AND SelloServidorAt IS NOT NULL AND Precision = 5.5 AND Nota = N'Falta una pieza') THEN 1 ELSE 0 END, NULL;

-- Cerrar la salida y corregirla: la correccion es una version nueva que referencia a la anterior
UPDATE dbo.tblInspecciones SET Estado = 'cerrada', CerradaAt = SYSDATETIMEOFFSET() WHERE Id = @s5;
INSERT dbo.tblInspecciones (Id, EtapaId, ViajeId, UsuarioId, Momento, Fecha, Version, CorrigeInspeccionId) VALUES (@c5, @e5, @vj5, @u1, 'salida', SYSDATETIMEOFFSET(), 2, @s5);
INSERT #r SELECT N'Inspeccion: una correccion es una version nueva que referencia a la anterior y la anterior sigue cerrada',
    CASE WHEN EXISTS (SELECT 1 FROM dbo.tblInspecciones WHERE Id = @c5 AND Version = 2 AND CorrigeInspeccionId = @s5)
          AND EXISTS (SELECT 1 FROM dbo.tblInspecciones WHERE Id = @s5 AND Estado = 'cerrada') THEN 1 ELSE 0 END, NULL;

DECLARE @casosInsp TABLE (Prueba nvarchar(200), Sentencia nvarchar(max), Esperado int);
INSERT @casosInsp VALUES
 (N'CHECK momento invalido (informe ya no existe)', N'INSERT dbo.tblInspecciones (Id,EtapaId,ViajeId,UsuarioId,Momento,Fecha) SELECT NEWID(),EtapaId,ViajeId,UsuarioId,''informe'',Fecha FROM dbo.tblInspecciones WHERE Version = 2 AND Momento = ''salida'' AND Fecha > ''2000-01-01''', 547),
 (N'UNIQUE una sola inspeccion original por etapa y momento', N'INSERT dbo.tblInspecciones (Id,EtapaId,ViajeId,UsuarioId,Momento,Fecha) SELECT NEWID(),EtapaId,ViajeId,UsuarioId,Momento,Fecha FROM dbo.tblInspecciones WHERE Version = 1 AND Momento = ''llegada'' AND ViajeId = (SELECT Id FROM dbo.tblViajes WHERE CodigoMtrack = ''ZZ_TEST_MTK_6'')', 2601),
 (N'UNIQUE una version se corrige una sola vez', N'INSERT dbo.tblInspecciones (Id,EtapaId,ViajeId,UsuarioId,Momento,Fecha,Version,CorrigeInspeccionId) SELECT NEWID(),EtapaId,ViajeId,UsuarioId,Momento,Fecha,3,CorrigeInspeccionId FROM dbo.tblInspecciones WHERE Version = 2 AND ViajeId = (SELECT Id FROM dbo.tblViajes WHERE CodigoMtrack = ''ZZ_TEST_MTK_6'')', 2601),
 (N'CHECK version 1 no corrige a otra inspeccion', N'INSERT dbo.tblInspecciones (Id,EtapaId,ViajeId,UsuarioId,Momento,Fecha,Version,CorrigeInspeccionId) SELECT NEWID(),EtapaId,ViajeId,UsuarioId,''llegada'',Fecha,1,Id FROM dbo.tblInspecciones WHERE Version = 2 AND ViajeId = (SELECT Id FROM dbo.tblViajes WHERE CodigoMtrack = ''ZZ_TEST_MTK_6'')', 547),
 (N'CHECK una version mayor a 1 debe referenciar a la que corrige', N'INSERT dbo.tblInspecciones (Id,EtapaId,ViajeId,UsuarioId,Momento,Fecha,Version) SELECT NEWID(),Id,ViajeId,(SELECT Id FROM dbo.tblUsuarios WHERE Email=N''zz_test_uno@ejemplo.com''),''llegada'',SYSDATETIMEOFFSET(),2 FROM dbo.tblEtapas WHERE Orden = 2 AND ViajeId = (SELECT Id FROM dbo.tblViajes WHERE CodigoMtrack = ''ZZ_TEST_MTK_6'')', 547),
 (N'CHECK una inspeccion cerrada tiene hora de cierre', N'INSERT dbo.tblInspecciones (Id,EtapaId,ViajeId,UsuarioId,Momento,Fecha,Estado) SELECT NEWID(),Id,ViajeId,(SELECT Id FROM dbo.tblUsuarios WHERE Email=N''zz_test_uno@ejemplo.com''),''llegada'',SYSDATETIMEOFFSET(),''cerrada'' FROM dbo.tblEtapas WHERE Orden = 2 AND ViajeId = (SELECT Id FROM dbo.tblViajes WHERE CodigoMtrack = ''ZZ_TEST_MTK_6'')', 547),
 (N'FK la version corregida debe existir', N'INSERT dbo.tblInspecciones (Id,EtapaId,ViajeId,UsuarioId,Momento,Fecha,Version,CorrigeInspeccionId) SELECT NEWID(),EtapaId,ViajeId,UsuarioId,''llegada'',Fecha,2,NEWID() FROM dbo.tblInspecciones WHERE Version = 2 AND ViajeId = (SELECT Id FROM dbo.tblViajes WHERE CodigoMtrack = ''ZZ_TEST_MTK_6'')', 547),
 (N'Una evidencia exige ubicacion (NOT NULL)', N'INSERT dbo.tblEvidencias (Id,InspeccionId,StoragePath,SelloFecha,SelloLat,SelloLng) VALUES (NEWID(),(SELECT Id FROM dbo.tblInspecciones WHERE Version = 2 AND ViajeId = (SELECT Id FROM dbo.tblViajes WHERE CodigoMtrack = ''ZZ_TEST_MTK_6'')),N''zz-sin-gps.jpg'',SYSDATETIMEOFFSET(),NULL,NULL)', 515),
 (N'CHECK precision negativa', N'INSERT dbo.tblEvidencias (Id,InspeccionId,StoragePath,SelloFecha,SelloLat,SelloLng,Precision) VALUES (NEWID(),(SELECT Id FROM dbo.tblInspecciones WHERE Version = 2 AND ViajeId = (SELECT Id FROM dbo.tblViajes WHERE CodigoMtrack = ''ZZ_TEST_MTK_6'')),N''zz-p.jpg'',SYSDATETIMEOFFSET(),4.6,-74.1,-1)', 547),
 (N'CHECK hash invalido', N'INSERT dbo.tblEvidencias (Id,InspeccionId,StoragePath,SelloFecha,SelloLat,SelloLng,HashSha256) VALUES (NEWID(),(SELECT Id FROM dbo.tblInspecciones WHERE Version = 2 AND ViajeId = (SELECT Id FROM dbo.tblViajes WHERE CodigoMtrack = ''ZZ_TEST_MTK_6'')),N''zz-h.jpg'',SYSDATETIMEOFFSET(),4.6,-74.1,REPLICATE(''Z'', 64))', 547),
 (N'FK vista inexistente en evidencias', N'INSERT dbo.tblEvidencias (Id,InspeccionId,VistaId,StoragePath,SelloFecha,SelloLat,SelloLng) VALUES (NEWID(),(SELECT Id FROM dbo.tblInspecciones WHERE Version = 2 AND ViajeId = (SELECT Id FROM dbo.tblViajes WHERE CodigoMtrack = ''ZZ_TEST_MTK_6'')),NEWID(),N''zz-v.jpg'',SYSDATETIMEOFFSET(),4.6,-74.1)', 547),
 (N'No se borra una vista de foto con evidencias', N'DELETE FROM dbo.tblVistasFoto WHERE Codigo = ''frontal''', 547);

DECLARE @pruebaInsp nvarchar(200), @sqlInsp nvarchar(max), @espInsp int;
DECLARE cInsp CURSOR LOCAL FAST_FORWARD FOR SELECT Prueba, Sentencia, Esperado FROM @casosInsp;
OPEN cInsp; FETCH NEXT FROM cInsp INTO @pruebaInsp, @sqlInsp, @espInsp;
WHILE @@FETCH_STATUS = 0
BEGIN
    BEGIN TRY
        EXEC (@sqlInsp);
        INSERT #r VALUES (@pruebaInsp, 0, N'la sentencia no fallo');
    END TRY
    BEGIN CATCH
        INSERT #r VALUES (@pruebaInsp, CASE WHEN ERROR_NUMBER() = @espInsp THEN 1 ELSE 0 END, N'error ' + CAST(ERROR_NUMBER() AS nvarchar(10)) + N': ' + LEFT(ERROR_MESSAGE(), 150));
    END CATCH
    FETCH NEXT FROM cInsp INTO @pruebaInsp, @sqlInsp, @espInsp;
END
CLOSE cInsp; DEALLOCATE cInsp;

-- Transferencias: cada tipo de cierre exige lo suyo, el testigo es una tercera persona y la foto de la constancia se admite sobre una inspeccion cerrada
DECLARE @u3 uniqueidentifier = NEWID(), @evc uniqueidentifier = NEWID();
INSERT dbo.tblUsuarios (Id, Nombre, Email, Rol) VALUES (@u3, N'ZZ_TEST Testigo', N'zz_test_testigo@ejemplo.com', 'inspector');
BEGIN TRY
    INSERT dbo.tblEvidencias (Id, InspeccionId, StoragePath, SelloFecha, SelloLat, SelloLng, EsConstancia) VALUES (@evc, @s5, N'zz-constancia.jpg', SYSDATETIMEOFFSET(), 4.6, -74.1, 1);
    INSERT #r VALUES (N'Constancia: la foto de una constancia se admite sobre una inspeccion cerrada', 1, NULL);
END TRY
BEGIN CATCH
    INSERT #r VALUES (N'Constancia: la foto de una constancia se admite sobre una inspeccion cerrada', 0, ERROR_MESSAGE());
END CATCH

DECLARE @cabTr nvarchar(max) = N'INSERT dbo.tblTransferenciasCustodia (Id,InspeccionId,EntregaUsuarioId,RecibeUsuarioId,FirmaEntrega,FirmaRecibe,TipoCierre,ReservasTexto,NoFirmaParte,MotivoNoFirma,TestigoUsuarioId,EvidenciaConstanciaId) VALUES (NEWID(),'''
    + CAST(@l5 AS nvarchar(36)) + N''',''' + CAST(@u1 AS nvarchar(36)) + N''',''' + CAST(@u2 AS nvarchar(36)) + N''',';
DECLARE @casosTr TABLE (Prueba nvarchar(200), Sentencia nvarchar(max), Esperado int);
INSERT @casosTr VALUES
 (N'CHECK tipo de cierre desconocido', @cabTr + N'N''A'',N''B'',''otro'',NULL,NULL,NULL,NULL,NULL)', 547),
 (N'CHECK firmada exige las dos firmas', @cabTr + N'N''A'',NULL,''firmada'',NULL,NULL,NULL,NULL,NULL)', 547),
 (N'CHECK firmada no lleva reservas', @cabTr + N'N''A'',N''B'',''firmada'',N''algo'',NULL,NULL,NULL,NULL)', 547),
 (N'CHECK con reservas exige las observaciones', @cabTr + N'N''A'',N''B'',''con_reservas'',NULL,NULL,NULL,NULL,NULL)', 547),
 (N'CHECK no firma exige el testigo', @cabTr + N'NULL,NULL,''no_firma'',NULL,''recibe'',N''Se niega'',NULL,''' + CAST(@evc AS nvarchar(36)) + N''')', 547),
 (N'CHECK no firma exige la foto de la constancia', @cabTr + N'NULL,NULL,''no_firma'',NULL,''recibe'',N''Se niega'',''' + CAST(@u3 AS nvarchar(36)) + N''',NULL)', 547),
 (N'CHECK no firma exige el motivo', @cabTr + N'NULL,NULL,''no_firma'',NULL,''recibe'',N'''',''' + CAST(@u3 AS nvarchar(36)) + N''',''' + CAST(@evc AS nvarchar(36)) + N''')', 547),
 (N'CHECK el testigo no es quien entrega', @cabTr + N'NULL,NULL,''no_firma'',NULL,''recibe'',N''Se niega'',''' + CAST(@u1 AS nvarchar(36)) + N''',''' + CAST(@evc AS nvarchar(36)) + N''')', 547),
 (N'CHECK el testigo no es quien recibe', @cabTr + N'NULL,NULL,''no_firma'',NULL,''recibe'',N''Se niega'',''' + CAST(@u2 AS nvarchar(36)) + N''',''' + CAST(@evc AS nvarchar(36)) + N''')', 547),
 (N'CHECK la parte que no firma es entrega o recibe', @cabTr + N'NULL,NULL,NULL,NULL,''otro'',NULL,NULL,NULL)', 547),
 (N'FK el testigo debe existir', @cabTr + N'NULL,NULL,''no_firma'',NULL,''recibe'',N''Se niega'',NEWID(),''' + CAST(@evc AS nvarchar(36)) + N''')', 547),
 (N'FK la foto de la constancia debe existir', @cabTr + N'NULL,NULL,''no_firma'',NULL,''recibe'',N''Se niega'',''' + CAST(@u3 AS nvarchar(36)) + N''',NEWID())', 547);

DECLARE @pruebaTr nvarchar(200), @sqlTr nvarchar(max), @espTr int;
DECLARE cTr CURSOR LOCAL FAST_FORWARD FOR SELECT Prueba, Sentencia, Esperado FROM @casosTr;
OPEN cTr; FETCH NEXT FROM cTr INTO @pruebaTr, @sqlTr, @espTr;
WHILE @@FETCH_STATUS = 0
BEGIN
    BEGIN TRY
        EXEC (@sqlTr);
        INSERT #r VALUES (@pruebaTr, 0, N'la sentencia no fallo');
    END TRY
    BEGIN CATCH
        INSERT #r VALUES (@pruebaTr, CASE WHEN ERROR_NUMBER() = @espTr THEN 1 ELSE 0 END, N'error ' + CAST(ERROR_NUMBER() AS nvarchar(10)) + N': ' + LEFT(ERROR_MESSAGE(), 150));
    END CATCH
    FETCH NEXT FROM cTr INTO @pruebaTr, @sqlTr, @espTr;
END
CLOSE cTr; DEALLOCATE cTr;

-- Los tres cierres validos: firmada, con reservas y no firma (con su testigo y su foto)
BEGIN TRY
    INSERT dbo.tblTransferenciasCustodia (Id, InspeccionId, EntregaUsuarioId, RecibeUsuarioId, FirmaEntrega, FirmaRecibe, FirmaEntregaAt, FirmaEntregaIp, FirmaRecibeAt, FirmaRecibeIp, FirmadaAt, TipoCierre, ReservasTexto)
    VALUES (NEWID(), @c5, @u1, @u2, N'A', N'B', SYSDATETIMEOFFSET(), '10.0.0.1', SYSDATETIMEOFFSET(), '10.0.0.2', SYSDATETIMEOFFSET(), 'con_reservas', N'El espejo esta rayado');
    INSERT dbo.tblTransferenciasCustodia (Id, InspeccionId, EntregaUsuarioId, RecibeUsuarioId, FirmaEntrega, NoFirmaParte, MotivoNoFirma, TestigoUsuarioId, EvidenciaConstanciaId, TipoCierre)
    VALUES (NEWID(), @l5, @u1, @u2, N'A', 'recibe', N'Se niega a firmar', @u3, @evc, 'no_firma');
    INSERT #r SELECT N'Transferencia: cierre con reservas y cierre por no firma se guardan con sus datos',
        CASE WHEN EXISTS (SELECT 1 FROM dbo.tblTransferenciasCustodia WHERE InspeccionId = @c5 AND TipoCierre = 'con_reservas' AND ReservasTexto IS NOT NULL AND FirmaEntregaIp = '10.0.0.1' AND FirmaRecibeIp = '10.0.0.2')
              AND EXISTS (SELECT 1 FROM dbo.tblTransferenciasCustodia WHERE InspeccionId = @l5 AND TipoCierre = 'no_firma' AND TestigoUsuarioId = @u3 AND EvidenciaConstanciaId = @evc) THEN 1 ELSE 0 END, NULL;
END TRY
BEGIN CATCH
    INSERT #r VALUES (N'Transferencia: cierre con reservas y cierre por no firma se guardan con sus datos', 0, ERROR_MESSAGE());
END CATCH
-- 7b) La evidencia no se borra (51003) y las relaciones ya no borran en cascada
INSERT #r SELECT N'Vehiculo: nace activo y tiene su indice',
    CASE WHEN EXISTS (SELECT 1 FROM dbo.tblVehiculos WHERE Vin = 'ZZ_TEST_VIN_0006' AND Activo = 1)
          AND EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblVehiculos_Activo' AND object_id = OBJECT_ID(N'dbo.tblVehiculos')) THEN 1 ELSE 0 END, NULL;
DECLARE @bV uniqueidentifier = NEWID(), @bF uniqueidentifier = NEWID(), @bH uniqueidentifier = NEWID(), @bT uniqueidentifier = NEWID();
INSERT dbo.tblEtapas (Id, VehiculoId, ViajeId, EtapaCatalogoId, Orden) VALUES (NEWID(), @v5, @vj5, @pc, 3), (NEWID(), @v5, @vj5, @pt, 4), (NEWID(), @v5, @vj5, @pc, 5), (NEWID(), @v5, @vj5, @pc, 6);
INSERT dbo.tblInspecciones (Id, EtapaId, ViajeId, UsuarioId, Momento, Fecha) VALUES
 (@bV, (SELECT Id FROM dbo.tblEtapas WHERE VehiculoId = @v5 AND Orden = 3), @vj5, @u1, 'salida', SYSDATETIMEOFFSET()),
 (@bF, (SELECT Id FROM dbo.tblEtapas WHERE VehiculoId = @v5 AND Orden = 4), @vj5, @u1, 'salida', SYSDATETIMEOFFSET()),
 (@bH, (SELECT Id FROM dbo.tblEtapas WHERE VehiculoId = @v5 AND Orden = 5), @vj5, @u1, 'salida', SYSDATETIMEOFFSET()),
 (@bT, (SELECT Id FROM dbo.tblEtapas WHERE VehiculoId = @v5 AND Orden = 6), @vj5, @u1, 'salida', SYSDATETIMEOFFSET());
INSERT dbo.tblEvidencias (Id, InspeccionId, StoragePath, SelloFecha, SelloLat, SelloLng) VALUES (NEWID(), @bF, N'zz-borrador.jpg', SYSDATETIMEOFFSET(), 4.6, -74.1);
INSERT dbo.tblHallazgos (Id, InspeccionId, ParteVehiculo, ClaseDano, TamanoDano) VALUES (NEWID(), @bH, N'Capot', 'golpe', '1_5cm');
INSERT dbo.tblTransferenciasCustodia (Id, InspeccionId, EntregaUsuarioId, RecibeUsuarioId) VALUES (NEWID(), @bT, @u1, @u2);
INSERT dbo.tblAuditLog (Id, Accion) VALUES (NEWID(), N'zz_test.borrar');

DECLARE @casosBorrado TABLE (Prueba nvarchar(200), Sentencia nvarchar(max), Esperado int);
INSERT @casosBorrado VALUES
 (N'No se borra una evidencia', N'DELETE FROM dbo.tblEvidencias WHERE StoragePath = N''zz-borrador.jpg''', 51003),
 (N'No se borra un hallazgo', N'DELETE FROM dbo.tblHallazgos WHERE InspeccionId = ''' + CAST(@bH AS nvarchar(36)) + N'''', 51003),
 (N'No se borra una transferencia', N'DELETE FROM dbo.tblTransferenciasCustodia WHERE InspeccionId = ''' + CAST(@bT AS nvarchar(36)) + N'''', 51003),
 (N'No se borra un registro de auditoria', N'DELETE FROM dbo.tblAuditLog WHERE Accion = N''zz_test.borrar''', 51003),
 (N'No se borra una inspeccion cerrada', N'DELETE FROM dbo.tblInspecciones WHERE Id = ''' + CAST(@s5 AS nvarchar(36)) + N'''', 51003),
 (N'No se borra un borrador con fotos', N'DELETE FROM dbo.tblInspecciones WHERE Id = ''' + CAST(@bF AS nvarchar(36)) + N'''', 51003),
 (N'No se borra un borrador con hallazgos', N'DELETE FROM dbo.tblInspecciones WHERE Id = ''' + CAST(@bH AS nvarchar(36)) + N'''', 51003),
 (N'No se borra un borrador con transferencia', N'DELETE FROM dbo.tblInspecciones WHERE Id = ''' + CAST(@bT AS nvarchar(36)) + N'''', 51003),
 (N'No se borra un vehiculo con etapas (sin cascada)', N'DELETE FROM dbo.tblVehiculos WHERE Vin = ''ZZ_TEST_VIN_0006''', 547),
 (N'No se borra una etapa con inspecciones (sin cascada)', N'DELETE FROM dbo.tblEtapas WHERE Id = ''' + CAST(@e5 AS nvarchar(36)) + N'''', 547);

DECLARE @pruebaBorrado nvarchar(200), @sqlBorrado nvarchar(max), @espBorrado int;
DECLARE cBorrado CURSOR LOCAL FAST_FORWARD FOR SELECT Prueba, Sentencia, Esperado FROM @casosBorrado;
OPEN cBorrado; FETCH NEXT FROM cBorrado INTO @pruebaBorrado, @sqlBorrado, @espBorrado;
WHILE @@FETCH_STATUS = 0
BEGIN
    BEGIN TRY
        EXEC (@sqlBorrado);
        INSERT #r VALUES (@pruebaBorrado, 0, N'la sentencia no fallo');
    END TRY
    BEGIN CATCH
        INSERT #r VALUES (@pruebaBorrado, CASE WHEN ERROR_NUMBER() = @espBorrado THEN 1 ELSE 0 END, N'error ' + CAST(ERROR_NUMBER() AS nvarchar(10)) + N': ' + LEFT(ERROR_MESSAGE(), 150));
    END CATCH
    FETCH NEXT FROM cBorrado INTO @pruebaBorrado, @sqlBorrado, @espBorrado;
END
CLOSE cBorrado; DEALLOCATE cBorrado;

-- 7c) Zonas del dano, paneles, regiones, hallazgo con nivel y estado de la unidad del viaje
INSERT #r SELECT N'Paneles: existen los 5 paneles de la ficha, separados de las vistas de foto',
    CASE WHEN (SELECT COUNT(*) FROM dbo.tblPanelesDano WHERE Codigo IN ('superior','perfil_posterior_izquierdo','perfil_posterior_derecho','costado_izquierdo','costado_derecho')) = 5 THEN 1 ELSE 0 END, NULL;
INSERT #r SELECT N'Zonas provisionales: frente y las cuatro llantas, sin cuadrante, numero ni lado',
    CASE WHEN (SELECT COUNT(*) FROM dbo.tblZonasDano WHERE CuadranteId IS NULL AND Numero IS NULL AND Lado IS NULL
                 AND Codigo IN ('frente','llanta_delantera_izquierda','llanta_delantera_derecha','llanta_trasera_izquierda','llanta_trasera_derecha')) = 5 THEN 1 ELSE 0 END, NULL;

DECLARE @cuad7 uniqueidentifier = NEWID(), @cuad30 uniqueidentifier = NEWID();
INSERT dbo.tblCuadrantes (Id, TipoVehiculo, Numero, Nombre) VALUES (@cuad7, 'zz_zona', 7, N'ZZ_TEST zona 7'), (@cuad30, 'zz_zona', 30, N'ZZ_TEST zona 30');
INSERT #r SELECT N'Cuadrante nuevo: el trigger crea dos zonas (izquierdo y derecho) del 1 al 25 y una sin lado del 26 en adelante',
    CASE WHEN (SELECT COUNT(*) FROM dbo.tblZonasDano WHERE CuadranteId = @cuad7 AND Lado IN ('izquierdo','derecho')) = 2
          AND (SELECT COUNT(*) FROM dbo.tblZonasDano WHERE CuadranteId = @cuad30 AND Lado IS NULL) = 1 THEN 1 ELSE 0 END, NULL;

DECLARE @zonaIzq7 uniqueidentifier = (SELECT Id FROM dbo.tblZonasDano WHERE CuadranteId = @cuad7 AND Lado = 'izquierdo');
DECLARE @zonaDer7 uniqueidentifier = (SELECT Id FROM dbo.tblZonasDano WHERE CuadranteId = @cuad7 AND Lado = 'derecho');
DECLARE @zona30 uniqueidentifier = (SELECT Id FROM dbo.tblZonasDano WHERE CuadranteId = @cuad30);
DECLARE @zonaFrente uniqueidentifier = (SELECT Id FROM dbo.tblZonasDano WHERE Codigo = 'frente');
DECLARE @panelSup uniqueidentifier = (SELECT Id FROM dbo.tblPanelesDano WHERE Codigo = 'superior');
DECLARE @panelCostIzq uniqueidentifier = (SELECT Id FROM dbo.tblPanelesDano WHERE Codigo = 'costado_izquierdo');

-- Una region valida: la zona izquierdo 7 dibujada en el costado izquierdo.
INSERT dbo.tblRegionesDano (Id, PanelId, ZonaId, CuadranteId, Geometria) VALUES (NEWID(), @panelCostIzq, @zonaIzq7, @cuad7, N'{"puntos":[[0.1,0.1],[0.2,0.1],[0.2,0.2]]}');
INSERT #r SELECT N'Region: la zona izquierdo 7 se dibuja en el costado izquierdo', CASE WHEN EXISTS (SELECT 1 FROM dbo.tblRegionesDano WHERE ZonaId = @zonaIzq7 AND PanelId = @panelCostIzq) THEN 1 ELSE 0 END, NULL;

DECLARE @bConductor uniqueidentifier = NEWID();
DECLARE @casosZona TABLE (Prueba nvarchar(200), Sentencia nvarchar(max), Esperado int);
INSERT @casosZona VALUES
 (N'CHECK una zona sobre el numero 25 no lleva lado', N'INSERT dbo.tblZonasDano (Id,CuadranteId,Numero,Lado) VALUES (NEWID(),''' + CAST(@cuad30 AS nvarchar(36)) + N''',30,''izquierdo'')', 547),
 (N'CHECK una zona del 1 al 25 exige lado', N'INSERT dbo.tblZonasDano (Id,CuadranteId,Numero,Lado) VALUES (NEWID(),''' + CAST(@cuad7 AS nvarchar(36)) + N''',7,NULL)', 547),
 (N'CHECK lado distinto de izquierdo o derecho', N'INSERT dbo.tblZonasDano (Id,CuadranteId,Numero,Lado) VALUES (NEWID(),''' + CAST(@cuad7 AS nvarchar(36)) + N''',7,''centro'')', 547),
 (N'UNIQUE una zona por cuadrante y lado', N'INSERT dbo.tblZonasDano (Id,CuadranteId,Numero,Lado) VALUES (NEWID(),''' + CAST(@cuad7 AS nvarchar(36)) + N''',7,''izquierdo'')', 2601),
 (N'FK el numero de la zona es el de su cuadrante', N'INSERT dbo.tblZonasDano (Id,CuadranteId,Numero,Lado) VALUES (NEWID(),''' + CAST(@cuad7 AS nvarchar(36)) + N''',30,NULL)', 547),
 (N'CHECK una zona provisional exige codigo y nombre', N'INSERT dbo.tblZonasDano (Id,Codigo) VALUES (NEWID(),''zz_provisional'')', 547),
 (N'UNIQUE codigo de la zona provisional', N'INSERT dbo.tblZonasDano (Id,Codigo,Nombre) VALUES (NEWID(),''frente'',N''Otra'')', 2601),
 (N'FK una region no puede ser de una zona provisional', N'INSERT dbo.tblRegionesDano (Id,PanelId,ZonaId,CuadranteId,Geometria) VALUES (NEWID(),''' + CAST(@panelSup AS nvarchar(36)) + N''',''' + CAST(@zonaFrente AS nvarchar(36)) + N''',''' + CAST(@cuad7 AS nvarchar(36)) + N''',N''{"puntos":[]}'')', 547),
 (N'FK la zona de una region es de su cuadrante', N'INSERT dbo.tblRegionesDano (Id,PanelId,ZonaId,CuadranteId,Geometria) VALUES (NEWID(),''' + CAST(@panelSup AS nvarchar(36)) + N''',''' + CAST(@zonaDer7 AS nvarchar(36)) + N''',''' + CAST(@cuad30 AS nvarchar(36)) + N''',N''{"puntos":[]}'')', 547),
 (N'UNIQUE una zona se dibuja una vez por panel', N'INSERT dbo.tblRegionesDano (Id,PanelId,ZonaId,CuadranteId,Geometria) VALUES (NEWID(),''' + CAST(@panelCostIzq AS nvarchar(36)) + N''',''' + CAST(@zonaIzq7 AS nvarchar(36)) + N''',''' + CAST(@cuad7 AS nvarchar(36)) + N''',N''{"puntos":[]}'')', 2627),
 (N'CHECK la geometria de la region es JSON', N'INSERT dbo.tblRegionesDano (Id,PanelId,ZonaId,CuadranteId,Geometria) VALUES (NEWID(),''' + CAST(@panelSup AS nvarchar(36)) + N''',''' + CAST(@zonaDer7 AS nvarchar(36)) + N''',''' + CAST(@cuad7 AS nvarchar(36)) + N''',N''no es json'')', 547),
 (N'CHECK origen del hallazgo', N'INSERT dbo.tblHallazgos (Id,InspeccionId,ClaseDano,TamanoDano,Origen) VALUES (NEWID(),''' + CAST(@bF AS nvarchar(36)) + N''',''golpe'',''1_5cm'',''robot'')', 547),
 (N'CHECK el tipo de novedad es solo del conductor', N'INSERT dbo.tblHallazgos (Id,InspeccionId,ClaseDano,TamanoDano,TipoNovedad) VALUES (NEWID(),''' + CAST(@bF AS nvarchar(36)) + N''',''golpe'',''1_5cm'',''piedra'')', 547),
 (N'CHECK tipo de novedad desconocido', N'INSERT dbo.tblHallazgos (Id,InspeccionId,TamanoDano,Origen,TipoNovedad) VALUES (NEWID(),''' + CAST(@bF AS nvarchar(36)) + N''',''1_5cm'',''conductor'',''lluvia'')', 547),
 (N'CHECK la clase solo falta en una novedad del conductor', N'INSERT dbo.tblHallazgos (Id,InspeccionId,TamanoDano) VALUES (NEWID(),''' + CAST(@bF AS nvarchar(36)) + N''',''1_5cm'')', 547),
 (N'CHECK estado de seguimiento', N'INSERT dbo.tblHallazgos (Id,InspeccionId,ClaseDano,TamanoDano,EstadoSeguimiento) VALUES (NEWID(),''' + CAST(@bF AS nvarchar(36)) + N''',''golpe'',''1_5cm'',''archivado'')', 547),
 (N'CHECK un hallazgo preexistente exige su hallazgo de origen', N'INSERT dbo.tblHallazgos (Id,InspeccionId,ClaseDano,TamanoDano,EsPreexistente) VALUES (NEWID(),''' + CAST(@bF AS nvarchar(36)) + N''',''golpe'',''1_5cm'',1)', 547),
 (N'FK nivel de severidad inexistente', N'INSERT dbo.tblHallazgos (Id,InspeccionId,ClaseDano,TamanoDano,NivelSeveridad) VALUES (NEWID(),''' + CAST(@bF AS nvarchar(36)) + N''',''golpe'',''1_5cm'',''v9'')', 547),
 (N'FK zona inexistente en el hallazgo', N'INSERT dbo.tblHallazgos (Id,InspeccionId,ClaseDano,TamanoDano,ZonaId) VALUES (NEWID(),''' + CAST(@bF AS nvarchar(36)) + N''',''golpe'',''1_5cm'',NEWID())', 547),
 (N'CHECK una unidad que no sale exige su motivo, hallazgo y responsable', N'UPDATE dbo.tblViajeVehiculos SET EstadoUnidad = ''no_sale'' WHERE VehiculoId = (SELECT Id FROM dbo.tblVehiculos WHERE Vin = ''ZZ_TEST_VIN_0006'')', 547),
 (N'CHECK una unidad activa no lleva datos de no sale', N'UPDATE dbo.tblViajeVehiculos SET NoSaleMotivo = N''x'' WHERE VehiculoId = (SELECT Id FROM dbo.tblVehiculos WHERE Vin = ''ZZ_TEST_VIN_0006'')', 547),
 (N'CHECK estado de la unidad del viaje', N'UPDATE dbo.tblViajeVehiculos SET EstadoUnidad = ''perdida'' WHERE VehiculoId = (SELECT Id FROM dbo.tblVehiculos WHERE Vin = ''ZZ_TEST_VIN_0006'')', 547);

DECLARE @pruebaZona nvarchar(200), @sqlZona nvarchar(max), @espZona int;
DECLARE cZona CURSOR LOCAL FAST_FORWARD FOR SELECT Prueba, Sentencia, Esperado FROM @casosZona;
OPEN cZona; FETCH NEXT FROM cZona INTO @pruebaZona, @sqlZona, @espZona;
WHILE @@FETCH_STATUS = 0
BEGIN
    BEGIN TRY
        EXEC (@sqlZona);
        INSERT #r VALUES (@pruebaZona, 0, N'la sentencia no fallo');
    END TRY
    BEGIN CATCH
        INSERT #r VALUES (@pruebaZona, CASE WHEN ERROR_NUMBER() = @espZona THEN 1 ELSE 0 END, N'error ' + CAST(ERROR_NUMBER() AS nvarchar(10)) + N': ' + LEFT(ERROR_MESSAGE(), 150));
    END CATCH
    FETCH NEXT FROM cZona INTO @pruebaZona, @sqlZona, @espZona;
END
CLOSE cZona; DEALLOCATE cZona;

-- Un hallazgo completo (zona lado + numero, clase, tamano, nivel, nota), uno preexistente que apunta al primero y una novedad del conductor sin zona, clase ni nivel.
DECLARE @hNuevo uniqueidentifier = NEWID(), @hPre uniqueidentifier = NEWID();
INSERT dbo.tblHallazgos (Id, InspeccionId, ZonaId, ClaseDano, TamanoDano, NivelSeveridad, Nota) VALUES (@hNuevo, @bF, @zonaIzq7, 'golpe', '1_5cm', 'v1_plus', N'Rayon en la puerta');
INSERT dbo.tblHallazgos (Id, InspeccionId, ZonaId, ClaseDano, TamanoDano, NivelSeveridad, EsPreexistente, HallazgoOrigenId, ConfirmadoPor)
VALUES (@hPre, @bF, @zonaIzq7, 'golpe', '1_5cm', 'v1_plus', 1, @hNuevo, @u1);
INSERT dbo.tblHallazgos (Id, InspeccionId, TamanoDano, Origen, TipoNovedad) VALUES (@bConductor, @bF, '1_5cm', 'conductor', 'granizo');
INSERT #r SELECT N'Hallazgo: guarda zona, nivel y nota; el preexistente apunta a su origen; la novedad del conductor no lleva zona, clase ni nivel',
    CASE WHEN EXISTS (SELECT 1 FROM dbo.tblHallazgos WHERE Id = @hNuevo AND ZonaId = @zonaIzq7 AND NivelSeveridad = 'v1_plus' AND Origen = 'inspector' AND EstadoSeguimiento = 'pendiente' AND EvidenciaCompleta = 0 AND MigracionPendiente = 0)
          AND EXISTS (SELECT 1 FROM dbo.tblHallazgos WHERE Id = @hPre AND EsPreexistente = 1 AND HallazgoOrigenId = @hNuevo)
          AND EXISTS (SELECT 1 FROM dbo.tblHallazgos WHERE Id = @bConductor AND ZonaId IS NULL AND ClaseDano IS NULL AND NivelSeveridad IS NULL AND TipoNovedad = 'granizo') THEN 1 ELSE 0 END, NULL;

-- 7d) Historial de seguimiento (append-only), estado del hallazgo y vista de patrones por zona
DECLARE @hDer uniqueidentifier = NEWID(), @selloEv uniqueidentifier = (SELECT TOP 1 Id FROM dbo.tblEvidencias WHERE InspeccionId = @bF);
INSERT dbo.tblHallazgos (Id, InspeccionId, ZonaId, ClaseDano, TamanoDano, NivelSeveridad) VALUES (@hDer, @bF, @zonaDer7, 'golpe', '1_5cm', 'v1_plus');

INSERT dbo.tblHallazgoSeguimiento (Id, HallazgoId, Accion, EstadoAnterior, EstadoNuevo, UsuarioId, Nota, EvidenciaSelloId, Detalle)
VALUES (NEWID(), @hNuevo, 'seguimiento', 'pendiente', 'validado', @u1, N'Validado por Sofasa', @selloEv, N'{"motivo":"v2"}');
INSERT #r SELECT N'Seguimiento: una fila de seguimiento cambia el estado del hallazgo',
    CASE WHEN EXISTS (SELECT 1 FROM dbo.tblHallazgos WHERE Id = @hNuevo AND EstadoSeguimiento = 'validado') THEN 1 ELSE 0 END, NULL;
INSERT dbo.tblHallazgoSeguimiento (Id, HallazgoId, Accion, EstadoAnterior, EstadoNuevo, UsuarioId) VALUES (NEWID(), @hNuevo, 'revision', 'validado', 'rechazado', @u1);
INSERT dbo.tblHallazgoSeguimiento (Id, HallazgoId, Accion, UsuarioId, Nota) VALUES (NEWID(), @hNuevo, 'no_sale', @u1, N'La unidad no sale');
INSERT #r SELECT N'Seguimiento: una revision o un no sale no cambian el estado del hallazgo',
    CASE WHEN EXISTS (SELECT 1 FROM dbo.tblHallazgos WHERE Id = @hNuevo AND EstadoSeguimiento = 'validado') AND (SELECT COUNT(*) FROM dbo.tblHallazgoSeguimiento WHERE HallazgoId = @hNuevo) = 3 THEN 1 ELSE 0 END, NULL;
INSERT dbo.tblHallazgoSeguimiento (Id, HallazgoId, Accion, EstadoAnterior, EstadoNuevo, UsuarioId) VALUES (NEWID(), @hNuevo, 'seguimiento', 'validado', 'rechazado', @u1);
INSERT #r SELECT N'Seguimiento: el ultimo cambio es el estado vigente del hallazgo',
    CASE WHEN EXISTS (SELECT 1 FROM dbo.tblHallazgos WHERE Id = @hNuevo AND EstadoSeguimiento = 'rechazado') AND EXISTS (SELECT 1 FROM dbo.tblHallazgos WHERE Id = @hPre AND EstadoSeguimiento = 'pendiente') THEN 1 ELSE 0 END, NULL;

DECLARE @casosHistorial TABLE (Prueba nvarchar(200), Sentencia nvarchar(max), Esperado int);
INSERT @casosHistorial VALUES
 (N'El historial no se modifica', N'UPDATE dbo.tblHallazgoSeguimiento SET Nota = N''otra'' WHERE HallazgoId = ''' + CAST(@hNuevo AS nvarchar(36)) + N'''', 51005),
 (N'El historial no se borra', N'DELETE FROM dbo.tblHallazgoSeguimiento WHERE HallazgoId = ''' + CAST(@hNuevo AS nvarchar(36)) + N'''', 51003),
 (N'CHECK accion del historial', N'INSERT dbo.tblHallazgoSeguimiento (Id,HallazgoId,Accion,UsuarioId) VALUES (NEWID(),''' + CAST(@hNuevo AS nvarchar(36)) + N''',''borrar'',''' + CAST(@u1 AS nvarchar(36)) + N''')', 547),
 (N'CHECK un cambio de seguimiento dice el estado nuevo', N'INSERT dbo.tblHallazgoSeguimiento (Id,HallazgoId,Accion,UsuarioId) VALUES (NEWID(),''' + CAST(@hNuevo AS nvarchar(36)) + N''',''seguimiento'',''' + CAST(@u1 AS nvarchar(36)) + N''')', 547),
 (N'CHECK estado del historial', N'INSERT dbo.tblHallazgoSeguimiento (Id,HallazgoId,Accion,EstadoNuevo,UsuarioId) VALUES (NEWID(),''' + CAST(@hNuevo AS nvarchar(36)) + N''',''seguimiento'',''archivado'',''' + CAST(@u1 AS nvarchar(36)) + N''')', 547),
 (N'CHECK el detalle del historial es JSON', N'INSERT dbo.tblHallazgoSeguimiento (Id,HallazgoId,Accion,UsuarioId,Detalle) VALUES (NEWID(),''' + CAST(@hNuevo AS nvarchar(36)) + N''',''revision'',''' + CAST(@u1 AS nvarchar(36)) + N''',N''no es json'')', 547),
 (N'FK hallazgo inexistente en el historial', N'INSERT dbo.tblHallazgoSeguimiento (Id,HallazgoId,Accion,UsuarioId) VALUES (NEWID(),NEWID(),''revision'',''' + CAST(@u1 AS nvarchar(36)) + N''')', 547),
 (N'FK usuario inexistente en el historial', N'INSERT dbo.tblHallazgoSeguimiento (Id,HallazgoId,Accion,UsuarioId) VALUES (NEWID(),''' + CAST(@hNuevo AS nvarchar(36)) + N''',''revision'',NEWID())', 547),
 (N'FK sello inexistente en el historial', N'INSERT dbo.tblHallazgoSeguimiento (Id,HallazgoId,Accion,UsuarioId,EvidenciaSelloId) VALUES (NEWID(),''' + CAST(@hNuevo AS nvarchar(36)) + N''',''revision'',''' + CAST(@u1 AS nvarchar(36)) + N''',NEWID())', 547);

DECLARE @pruebaHist nvarchar(200), @sqlHist nvarchar(max), @espHist int;
DECLARE cHist CURSOR LOCAL FAST_FORWARD FOR SELECT Prueba, Sentencia, Esperado FROM @casosHistorial;
OPEN cHist; FETCH NEXT FROM cHist INTO @pruebaHist, @sqlHist, @espHist;
WHILE @@FETCH_STATUS = 0
BEGIN
    BEGIN TRY
        EXEC (@sqlHist);
        INSERT #r VALUES (@pruebaHist, 0, N'la sentencia no fallo');
    END TRY
    BEGIN CATCH
        INSERT #r VALUES (@pruebaHist, CASE WHEN ERROR_NUMBER() = @espHist THEN 1 ELSE 0 END, N'error ' + CAST(ERROR_NUMBER() AS nvarchar(10)) + N': ' + LEFT(ERROR_MESSAGE(), 150));
    END CATCH
    FETCH NEXT FROM cHist INTO @pruebaHist, @sqlHist, @espHist;
END
CLOSE cHist; DEALLOCATE cHist;

-- Vista de patrones: solo cuenta hallazgos de inspecciones CERRADAS. Con la inspeccion en borrador no hay filas; al cerrarla, izquierdo 7 y derecho 7 son filas distintas.
INSERT #r SELECT N'Patrones: una inspeccion en borrador no cuenta',
    CASE WHEN NOT EXISTS (SELECT 1 FROM dbo.vwPatronesPorCuadrante WHERE CuadranteId = @cuad7) THEN 1 ELSE 0 END, NULL;
UPDATE dbo.tblInspecciones SET Estado = 'cerrada', CerradaAt = SYSDATETIMEOFFSET() WHERE Id = @bF;
INSERT #r SELECT N'Patrones: izquierdo 7 y derecho 7 son filas distintas, con su nombre, proceso, nivel, clase y total',
    CASE WHEN (SELECT COUNT(*) FROM dbo.vwPatronesPorCuadrante WHERE CuadranteId = @cuad7) = 2
          AND EXISTS (SELECT 1 FROM dbo.vwPatronesPorCuadrante WHERE CuadranteId = @cuad7 AND Lado = 'izquierdo' AND Numero = 7 AND Total = 2 AND Proceso = 'transporte'
                        AND NivelSeveridad = 'v1_plus' AND ClaseDano = 'golpe' AND ClienteId = @cli AND Operacion = 'nacional'
                        AND ZonaNombre = N'Izquierdo 7 ' + NCHAR(183) + N' ZZ_TEST zona 7')
          AND EXISTS (SELECT 1 FROM dbo.vwPatronesPorCuadrante WHERE CuadranteId = @cuad7 AND Lado = 'derecho' AND Total = 1) THEN 1 ELSE 0 END, NULL;
INSERT #r SELECT N'Patrones: la novedad del conductor sin revisar y el hallazgo sin zona no cuentan',
    CASE WHEN NOT EXISTS (SELECT 1 FROM dbo.vwPatronesPorCuadrante WHERE ZonaId IS NULL) AND (SELECT ISNULL(SUM(Total), 0) FROM dbo.vwPatronesPorCuadrante WHERE CuadranteId = @cuad7) = 3 THEN 1 ELSE 0 END, NULL;
-- Al revisar la novedad del conductor y asignarle zona, clase y nivel, pasa a contar.
UPDATE dbo.tblHallazgos SET ZonaId = @zonaDer7, ClaseDano = 'golpe', NivelSeveridad = 'v1', RevisadoAt = SYSDATETIMEOFFSET(), RevisadoPor = @u1 WHERE Id = @bConductor;
INSERT #r SELECT N'Patrones: la novedad del conductor revisada cuenta en su zona',
    CASE WHEN EXISTS (SELECT 1 FROM dbo.vwPatronesPorCuadrante WHERE CuadranteId = @cuad7 AND Lado = 'derecho' AND NivelSeveridad = 'v1' AND Total = 1) THEN 1 ELSE 0 END, NULL;

-- Una unidad puede no salir del viaje: queda con su motivo, el hallazgo que lo causa y quien lo marco, y conserva su vinculo con el viaje.
UPDATE dbo.tblViajeVehiculos SET EstadoUnidad = 'no_sale', NoSaleMotivo = N'Hallazgo V1+', NoSaleHallazgoId = @hNuevo, NoSaleMarcadoPor = @u1, NoSaleAt = SYSDATETIMEOFFSET()
WHERE VehiculoId = @v5;
INSERT #r SELECT N'Unidad del viaje: no sale con su motivo, hallazgo y responsable, sin borrar el vinculo',
    CASE WHEN EXISTS (SELECT 1 FROM dbo.tblViajeVehiculos WHERE VehiculoId = @v5 AND EstadoUnidad = 'no_sale' AND NoSaleHallazgoId = @hNuevo) THEN 1 ELSE 0 END, NULL;

-- Una zona con hallazgos no se borra; un cuadrante sin hallazgos se lleva sus zonas y regiones.
BEGIN TRY
    DELETE FROM dbo.tblZonasDano WHERE Id = @zonaIzq7;
    INSERT #r VALUES (N'No se borra una zona que tiene hallazgos', 0, N'no fallo');
END TRY
BEGIN CATCH
    INSERT #r VALUES (N'No se borra una zona que tiene hallazgos', CASE WHEN ERROR_NUMBER() = 547 THEN 1 ELSE 0 END, LEFT(ERROR_MESSAGE(), 150));
END CATCH
DELETE FROM dbo.tblCuadrantes WHERE Id = @cuad30;
INSERT #r SELECT N'Borrar un cuadrante borra sus zonas',
    CASE WHEN NOT EXISTS (SELECT 1 FROM dbo.tblZonasDano WHERE CuadranteId = @cuad30) AND EXISTS (SELECT 1 FROM dbo.tblZonasDano WHERE CuadranteId = @cuad7) THEN 1 ELSE 0 END, NULL;

-- Un borrador vacio si se borra.
DELETE FROM dbo.tblInspecciones WHERE Id = @bV;
INSERT #r SELECT N'Un borrador sin fotos, hallazgos ni transferencia si se borra',
    CASE WHEN NOT EXISTS (SELECT 1 FROM dbo.tblInspecciones WHERE Id = @bV) AND EXISTS (SELECT 1 FROM dbo.tblInspecciones WHERE Id = @bF) THEN 1 ELSE 0 END, NULL;
INSERT #r SELECT N'Los registros protegidos siguen ahi despues de los intentos de borrado',
    CASE WHEN EXISTS (SELECT 1 FROM dbo.tblEvidencias WHERE StoragePath = N'zz-borrador.jpg') AND EXISTS (SELECT 1 FROM dbo.tblHallazgos WHERE InspeccionId = @bH)
          AND EXISTS (SELECT 1 FROM dbo.tblTransferenciasCustodia WHERE InspeccionId = @bT) AND EXISTS (SELECT 1 FROM dbo.tblAuditLog WHERE Accion = N'zz_test.borrar')
          AND EXISTS (SELECT 1 FROM dbo.tblInspecciones WHERE Id = @s5) THEN 1 ELSE 0 END, NULL;
-- La auditoria de prueba se limpia con su trigger apagado solo mientras se borra.
ALTER TABLE dbo.tblAuditLog DISABLE TRIGGER ALL;
DELETE FROM dbo.tblAuditLog WHERE Accion = N'zz_test.borrar';
ALTER TABLE dbo.tblAuditLog ENABLE TRIGGER ALL;

-- 7f) Notificaciones, dispositivos y la novedad del conductor
DECLARE @st5 uniqueidentifier = NEWID(), @eT5 uniqueidentifier = (SELECT Id FROM dbo.tblEtapas WHERE ViajeId = @vj5 AND Orden = 2);
INSERT dbo.tblInspecciones (Id, EtapaId, ViajeId, UsuarioId, Momento, Fecha) VALUES (@st5, @eT5, @vj5, @u1, 'salida', SYSDATETIMEOFFSET());
DECLARE @casosNotif TABLE (Prueba nvarchar(200), Sentencia nvarchar(max), Esperado int);
INSERT @casosNotif VALUES
 (N'Notificacion: una valida se guarda', N'INSERT dbo.tblNotificaciones (Id,Tipo,Urgencia,UsuarioId,VehiculoId,Titulo,Mensaje,Canal,ClaveDedupe) VALUES (NEWID(),''novedad_conductor'',''alta'',''' + CAST(@u1 AS nvarchar(36)) + N''',''' + CAST(@v5 AS nvarchar(36)) + N''',N''T'',N''M'',''bandeja'',''zz_dedupe_1'')', 0),
 (N'UNIQUE clave de dedupe de la notificacion', N'INSERT dbo.tblNotificaciones (Id,Tipo,Urgencia,UsuarioId,Titulo,Mensaje,Canal,ClaveDedupe) VALUES (NEWID(),''novedad_conductor'',''alta'',''' + CAST(@u1 AS nvarchar(36)) + N''',N''T'',N''M'',''push'',''zz_dedupe_1'')', 2627),
 (N'CHECK tipo de la notificacion', N'INSERT dbo.tblNotificaciones (Id,Tipo,Urgencia,UsuarioId,Titulo,Mensaje,Canal,ClaveDedupe) VALUES (NEWID(),''inventado'',''alta'',''' + CAST(@u1 AS nvarchar(36)) + N''',N''T'',N''M'',''bandeja'',''zz_dedupe_2'')', 547),
 (N'CHECK el correo solo es del reporte diario', N'INSERT dbo.tblNotificaciones (Id,Tipo,Urgencia,UsuarioId,Titulo,Mensaje,Canal,ClaveDedupe) VALUES (NEWID(),''novedad_conductor'',''alta'',''' + CAST(@u1 AS nvarchar(36)) + N''',N''T'',N''M'',''correo'',''zz_dedupe_3'')', 547),
 (N'CHECK solo el reporte diario no tiene destinatario', N'INSERT dbo.tblNotificaciones (Id,Tipo,Urgencia,Titulo,Mensaje,Canal,ClaveDedupe) VALUES (NEWID(),''umbral_patron'',''media'',N''T'',N''M'',''bandeja'',''zz_dedupe_4'')', 547),
 (N'Notificacion: el reporte diario va por correo y sin destinatario', N'INSERT dbo.tblNotificaciones (Id,Tipo,Urgencia,Titulo,Mensaje,Canal,ClaveDedupe) VALUES (NEWID(),''reporte_diario'',''media'',N''T'',N''M'',''correo'',''zz_dedupe_5'')', 0),
 (N'CHECK la respuesta es completa', N'UPDATE dbo.tblNotificaciones SET RespuestaTexto = N''solo texto'' WHERE ClaveDedupe = ''zz_dedupe_1''', 547),
 (N'Notificacion: se lee', N'UPDATE dbo.tblNotificaciones SET LeidaAt = SYSDATETIMEOFFSET() WHERE ClaveDedupe = ''zz_dedupe_1''', 0),
 (N'Notificacion: se responde', N'UPDATE dbo.tblNotificaciones SET RespuestaTexto = N''En camino'', RespuestaMedio = ''llamada'', RespondidaPor = ''' + CAST(@u1 AS nvarchar(36)) + N''', RespondidaAt = SYSDATETIMEOFFSET() WHERE ClaveDedupe = ''zz_dedupe_1''', 0),
 (N'Trigger: una notificacion solo cambia su lectura y su respuesta', N'UPDATE dbo.tblNotificaciones SET Titulo = N''Otro'' WHERE ClaveDedupe = ''zz_dedupe_1''', 51006),
 (N'Trigger: una notificacion no se borra', N'DELETE FROM dbo.tblNotificaciones WHERE ClaveDedupe = ''zz_dedupe_1''', 51003),
 (N'Dispositivo: uno valido se guarda', N'INSERT dbo.tblDispositivos (Id,UsuarioId,TokenFcm,Plataforma) VALUES (NEWID(),''' + CAST(@u1 AS nvarchar(36)) + N''',''zz-token-1'',''android'')', 0),
 (N'UNIQUE token FCM del dispositivo', N'INSERT dbo.tblDispositivos (Id,UsuarioId,TokenFcm,Plataforma) VALUES (NEWID(),''' + CAST(@u1 AS nvarchar(36)) + N''',''zz-token-1'',''ios'')', 2627),
 (N'CHECK plataforma del dispositivo', N'INSERT dbo.tblDispositivos (Id,UsuarioId,TokenFcm,Plataforma) VALUES (NEWID(),''' + CAST(@u1 AS nvarchar(36)) + N''',''zz-token-2'',''symbian'')', 547),
 (N'Novedad: la del conductor en la salida de Transporte se guarda sin zona, clase ni nivel', N'INSERT dbo.tblHallazgos (Id,InspeccionId,TamanoDano,Origen,TipoNovedad) VALUES (NEWID(),''' + CAST(@st5 AS nvarchar(36)) + N''',''5_10cm'',''conductor'',''golpe'')', 0),
 (N'CHECK la novedad sin revisar no tiene nivel', N'INSERT dbo.tblHallazgos (Id,InspeccionId,TamanoDano,Origen,TipoNovedad,NivelSeveridad) VALUES (NEWID(),''' + CAST(@st5 AS nvarchar(36)) + N''',''5_10cm'',''conductor'',''golpe'',''v2'')', 547),
 (N'CHECK la novedad sin revisar no tiene texto libre', N'INSERT dbo.tblHallazgos (Id,InspeccionId,TamanoDano,Origen,TipoNovedad,Nota) VALUES (NEWID(),''' + CAST(@st5 AS nvarchar(36)) + N''',''5_10cm'',''conductor'',''golpe'',N''texto'')', 547),
 (N'CHECK la novedad revisada se completa', N'UPDATE dbo.tblHallazgos SET RevisadoAt = SYSDATETIMEOFFSET() WHERE InspeccionId = ''' + CAST(@st5 AS nvarchar(36)) + N''' AND TipoNovedad IS NOT NULL', 547),
 (N'CHECK el conductor no asigna el nivel', N'INSERT dbo.tblHallazgos (Id,InspeccionId,TamanoDano,Origen,ClaseDano,NivelSeveridad) VALUES (NEWID(),''' + CAST(@s5 AS nvarchar(36)) + N''',''5_10cm'',''conductor'',''golpe'',''v1'')', 547),
 (N'Trigger: una novedad solo es de la salida de Transporte', N'INSERT dbo.tblHallazgos (Id,InspeccionId,TamanoDano,Origen,TipoNovedad) VALUES (NEWID(),''' + CAST(@s5 AS nvarchar(36)) + N''',''5_10cm'',''conductor'',''golpe'')', 51007);
DECLARE @pruebaN nvarchar(200), @sentenciaN nvarchar(max), @esperadoN int, @obtenidoN int, @mensajeN nvarchar(4000);
DECLARE cNotif CURSOR LOCAL FAST_FORWARD FOR SELECT Prueba, Sentencia, Esperado FROM @casosNotif;
OPEN cNotif;
FETCH NEXT FROM cNotif INTO @pruebaN, @sentenciaN, @esperadoN;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @obtenidoN = 0; SET @mensajeN = NULL;
    BEGIN TRY
        EXEC (@sentenciaN);
    END TRY
    BEGIN CATCH
        SET @obtenidoN = ERROR_NUMBER(); SET @mensajeN = ERROR_MESSAGE();
    END CATCH
    INSERT #r VALUES (@pruebaN, CASE WHEN @obtenidoN = @esperadoN THEN 1 ELSE 0 END, CASE WHEN @obtenidoN = @esperadoN THEN NULL ELSE CONCAT(N'esperaba ', @esperadoN, N' y fue ', @obtenidoN, N': ', @mensajeN) END);
    FETCH NEXT FROM cNotif INTO @pruebaN, @sentenciaN, @esperadoN;
END
CLOSE cNotif;
DEALLOCATE cNotif;
-- Limpieza de lo de esta seccion (las notificaciones solo se borran con su trigger apagado).
ALTER TABLE dbo.tblNotificaciones DISABLE TRIGGER ALL;
DELETE FROM dbo.tblNotificaciones WHERE ClaveDedupe LIKE N'zz_dedupe_%';
ALTER TABLE dbo.tblNotificaciones ENABLE TRIGGER ALL;
DELETE FROM dbo.tblDispositivos WHERE TokenFcm LIKE N'zz-token-%';

-- 7h) Parametros de las alertas y vista de la ventana de umbral
INSERT #r SELECT N'Parametros de alertas: niveles criticos v1_plus,v1 (lista), umbral 10, ventana 30 dias, captura 30 min y transferencia 240 min',
    CASE WHEN (SELECT COUNT(*) FROM dbo.tblParametros WHERE Clave = 'niveles_criticos' AND Valor = N'v1_plus,v1' AND Tipo = 'lista') = 1
          AND (SELECT COUNT(*) FROM dbo.tblParametros WHERE Clave = 'umbral_patron_cantidad' AND Valor = N'10' AND Tipo = 'numero') = 1
          AND (SELECT COUNT(*) FROM dbo.tblParametros WHERE Clave = 'umbral_patron_ventana_dias' AND Valor = N'30' AND Tipo = 'numero') = 1
          AND (SELECT COUNT(*) FROM dbo.tblParametros WHERE Clave = 'captura_incompleta_minutos' AND Valor = N'30' AND Tipo = 'numero') = 1
          AND (SELECT COUNT(*) FROM dbo.tblParametros WHERE Clave = 'transferencia_pendiente_minutos' AND Valor = N'240' AND Tipo = 'numero') = 1 THEN 1 ELSE 0 END, NULL;
-- Un hallazgo del inspector en la llegada del Cargue de la unidad 5 y una novedad del conductor sin revisar en la salida de su Transporte (ya creada arriba): mientras las inspecciones estan en
-- borrador la vista no trae ninguno; ya cerradas, trae solo el hallazgo del inspector hasta que la novedad se revisa.
DECLARE @hvA uniqueidentifier = NEWID(), @hvB uniqueidentifier = NEWID();
INSERT dbo.tblHallazgos (Id, InspeccionId, ZonaId, ClaseDano, TamanoDano, NivelSeveridad) VALUES (@hvA, @l5, (SELECT Id FROM dbo.tblZonasDano WHERE Codigo = 'frente'), 'golpe', '5_10cm', 'v2');
INSERT dbo.tblHallazgos (Id, InspeccionId, TamanoDano, Origen, TipoNovedad) VALUES (@hvB, @st5, '5_10cm', 'conductor', 'golpe');
INSERT #r SELECT N'vwHallazgosVentanaUmbral: una inspeccion en borrador no trae hallazgos',
    CASE WHEN NOT EXISTS (SELECT 1 FROM dbo.vwHallazgosVentanaUmbral WHERE HallazgoId IN (@hvA, @hvB)) THEN 1 ELSE 0 END, NULL;
UPDATE dbo.tblInspecciones SET Estado = 'cerrada', CerradaAt = SYSDATETIMEOFFSET() WHERE Id IN (@l5, @st5);
INSERT #r SELECT N'vwHallazgosVentanaUmbral: trae el hallazgo con su cliente, zona, proceso y fecha de cierre y excluye la novedad del conductor sin revisar',
    CASE WHEN EXISTS (SELECT 1 FROM dbo.vwHallazgosVentanaUmbral w WHERE w.HallazgoId = @hvA AND w.InspeccionId = @l5 AND w.ViajeId = @vj5 AND w.VehiculoId = @v5
                       AND w.ClienteId = (SELECT ClienteId FROM dbo.tblVehiculos WHERE Id = @v5) AND w.ZonaCodigo = 'frente' AND w.Numero IS NULL AND w.Lado IS NULL
                       AND w.Proceso = 'cargue' AND w.Fecha IS NOT NULL)
          AND NOT EXISTS (SELECT 1 FROM dbo.vwHallazgosVentanaUmbral WHERE HallazgoId = @hvB) THEN 1 ELSE 0 END, NULL;
UPDATE dbo.tblHallazgos SET ClaseDano = 'golpe', NivelSeveridad = 'v1', ZonaId = (SELECT Id FROM dbo.tblZonasDano WHERE Codigo = 'frente'), RevisadoAt = SYSDATETIMEOFFSET(), RevisadoPor = @u1 WHERE Id = @hvB;
INSERT #r SELECT N'vwHallazgosVentanaUmbral: la novedad revisada entra a la vista con su proceso',
    CASE WHEN EXISTS (SELECT 1 FROM dbo.vwHallazgosVentanaUmbral w WHERE w.HallazgoId = @hvB AND w.Proceso = 'transporte') THEN 1 ELSE 0 END, NULL;
ALTER TABLE dbo.tblHallazgos DISABLE TRIGGER ALL;
DELETE FROM dbo.tblHallazgos WHERE Id IN (@hvA, @hvB);
ALTER TABLE dbo.tblHallazgos ENABLE TRIGGER ALL;

-- 7i) Auditoria con IP, dispositivo y detalle (inmodificable) y notas aclaratorias (append-only)
DECLARE @casosAud TABLE (Prueba nvarchar(200), Sentencia nvarchar(max), Esperado int);
INSERT @casosAud VALUES
 (N'Auditoria: un registro con IP, dispositivo, detalle JSON, viaje y cliente se guarda', N'INSERT dbo.tblAuditLog (Id, UsuarioId, Accion, Entidad, EntidadId, Ip, Dispositivo, Detalle, ViajeId, ClienteId) VALUES (NEWID(), ''' + CAST(@u1 AS nvarchar(36)) + N''', N''zz.accion'', N''zz'', NEWID(), ''10.0.0.1'', N''zz-celular'', N''{"antes":{"estado":"borrador"},"despues":{"estado":"cerrada"}}'', NEWID(), NEWID())', 0),
 (N'CHECK el detalle de la auditoria es JSON', N'INSERT dbo.tblAuditLog (Id, Accion, Detalle) VALUES (NEWID(), N''zz.accion'', N''esto no es json'')', 547),
 (N'Auditoria: el detalle nulo es una lectura', N'INSERT dbo.tblAuditLog (Id, Accion) VALUES (NEWID(), N''zz.lectura'')', 0),
 (N'Trigger: la auditoria no se modifica', N'UPDATE dbo.tblAuditLog SET Accion = N''otra'' WHERE Accion = N''zz.accion''', 51005),
 (N'Trigger: la auditoria no se borra', N'DELETE FROM dbo.tblAuditLog WHERE Accion = N''zz.accion''', 51003),
 (N'Nota aclaratoria: una valida se guarda', N'INSERT dbo.tblNotasAclaratorias (Id, Entidad, EntidadId, AutorId, Texto) VALUES (NEWID(), ''hallazgo'', NEWID(), ''' + CAST(@u1 AS nvarchar(36)) + N''', N''zz aclaracion'')', 0),
 (N'CHECK entidad de la nota aclaratoria', N'INSERT dbo.tblNotasAclaratorias (Id, Entidad, EntidadId, AutorId, Texto) VALUES (NEWID(), ''vehiculo'', NEWID(), ''' + CAST(@u1 AS nvarchar(36)) + N''', N''zz aclaracion'')', 547),
 (N'CHECK texto de la nota aclaratoria no vacio', N'INSERT dbo.tblNotasAclaratorias (Id, Entidad, EntidadId, AutorId, Texto) VALUES (NEWID(), ''inspeccion'', NEWID(), ''' + CAST(@u1 AS nvarchar(36)) + N''', N'''')', 547),
 (N'FK autor de la nota aclaratoria', N'INSERT dbo.tblNotasAclaratorias (Id, Entidad, EntidadId, AutorId, Texto) VALUES (NEWID(), ''transferencia'', NEWID(), NEWID(), N''zz aclaracion'')', 547),
 (N'Trigger: la nota aclaratoria no se modifica', N'UPDATE dbo.tblNotasAclaratorias SET Texto = N''otro'' WHERE Texto = N''zz aclaracion''', 51005),
 (N'Trigger: la nota aclaratoria no se borra', N'DELETE FROM dbo.tblNotasAclaratorias WHERE Texto = N''zz aclaracion''', 51003);
DECLARE @pruebaA nvarchar(200), @sentenciaA nvarchar(max), @esperadoA int, @obtenidoA int, @mensajeA nvarchar(4000);
DECLARE cAud CURSOR LOCAL FAST_FORWARD FOR SELECT Prueba, Sentencia, Esperado FROM @casosAud;
OPEN cAud;
FETCH NEXT FROM cAud INTO @pruebaA, @sentenciaA, @esperadoA;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @obtenidoA = 0; SET @mensajeA = NULL;
    BEGIN TRY
        EXEC (@sentenciaA);
    END TRY
    BEGIN CATCH
        SET @obtenidoA = ERROR_NUMBER(); SET @mensajeA = ERROR_MESSAGE();
    END CATCH
    INSERT #r VALUES (@pruebaA, CASE WHEN @obtenidoA = @esperadoA THEN 1 ELSE 0 END, CASE WHEN @obtenidoA = @esperadoA THEN NULL ELSE CONCAT(N'esperaba ', @esperadoA, N' y fue ', @obtenidoA, N': ', @mensajeA) END);
    FETCH NEXT FROM cAud INTO @pruebaA, @sentenciaA, @esperadoA;
END
CLOSE cAud;
DEALLOCATE cAud;
INSERT #r SELECT N'Auditoria: tiene sus indices por entidad, viaje, cliente y fecha',
    CASE WHEN (SELECT COUNT(*) FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.tblAuditLog') AND name IN (N'IX_tblAuditLog_Entidad', N'IX_tblAuditLog_Viaje', N'IX_tblAuditLog_Cliente', N'IX_tblAuditLog_CreatedAt')) = 4 THEN 1 ELSE 0 END, NULL;
-- La limpieza de lo de esta seccion (la auditoria y las notas solo se borran con sus triggers apagados).
ALTER TABLE dbo.tblAuditLog DISABLE TRIGGER ALL;
DELETE FROM dbo.tblAuditLog WHERE Accion IN (N'zz.accion', N'zz.lectura');
ALTER TABLE dbo.tblAuditLog ENABLE TRIGGER ALL;
ALTER TABLE dbo.tblNotasAclaratorias DISABLE TRIGGER ALL;
DELETE FROM dbo.tblNotasAclaratorias WHERE Texto = N'zz aclaracion';
ALTER TABLE dbo.tblNotasAclaratorias ENABLE TRIGGER ALL;

-- Limpieza: el vehiculo de prueba se borra quitando antes, de forma explicita, todo lo que cuelga de el.
EXEC #zz_borrar_vehiculo 'ZZ_TEST_VIN_0006';
DELETE FROM dbo.tblViajes WHERE CodigoMtrack = 'ZZ_TEST_MTK_6';
DELETE FROM dbo.tblCuadrantes WHERE TipoVehiculo = 'zz_zona';
INSERT #r SELECT N'Limpieza: no quedan cuadrantes, zonas ni regiones de prueba',
    CASE WHEN NOT EXISTS (SELECT 1 FROM dbo.tblCuadrantes WHERE TipoVehiculo = 'zz_zona') AND NOT EXISTS (SELECT 1 FROM dbo.tblZonasDano WHERE CuadranteId IN (@cuad7, @cuad30))
          AND NOT EXISTS (SELECT 1 FROM dbo.tblRegionesDano WHERE CuadranteId IN (@cuad7, @cuad30)) THEN 1 ELSE 0 END, NULL;
INSERT #r SELECT N'Limpieza: se borra un vehiculo quitando antes lo que cuelga de el',
    CASE WHEN NOT EXISTS (SELECT 1 FROM dbo.tblVehiculos WHERE Vin = 'ZZ_TEST_VIN_0006') AND NOT EXISTS (SELECT 1 FROM dbo.tblInspecciones WHERE Id IN (@s5, @l5, @c5, @bF, @bH, @bT))
          AND NOT EXISTS (SELECT 1 FROM dbo.tblEvidencias WHERE StoragePath LIKE N'zz-%') THEN 1 ELSE 0 END, NULL;

-- 7e) Indices de consulta de los hallazgos: por zona y clase (con la inspeccion incluida) y por estado de seguimiento, origen y fecha de revision
INSERT #r SELECT N'Indices del hallazgo: zona + clase con la inspeccion incluida, y estado de seguimiento + origen + fecha de revision; ya no el de parte + clase',
    CASE WHEN (SELECT STRING_AGG(c.name, N',') WITHIN GROUP (ORDER BY ic.key_ordinal) FROM sys.index_columns ic JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
               WHERE ic.object_id = OBJECT_ID(N'dbo.tblHallazgos') AND ic.index_id = (SELECT index_id FROM sys.indexes WHERE name = N'IX_tblHallazgos_Zona_Clase' AND object_id = OBJECT_ID(N'dbo.tblHallazgos')) AND ic.is_included_column = 0) = N'ZonaId,ClaseDano'
          AND (SELECT STRING_AGG(c.name, N',') FROM sys.index_columns ic JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
               WHERE ic.object_id = OBJECT_ID(N'dbo.tblHallazgos') AND ic.index_id = (SELECT index_id FROM sys.indexes WHERE name = N'IX_tblHallazgos_Zona_Clase' AND object_id = OBJECT_ID(N'dbo.tblHallazgos')) AND ic.is_included_column = 1) = N'InspeccionId'
          AND (SELECT STRING_AGG(c.name, N',') WITHIN GROUP (ORDER BY ic.key_ordinal) FROM sys.index_columns ic JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
               WHERE ic.object_id = OBJECT_ID(N'dbo.tblHallazgos') AND ic.index_id = (SELECT index_id FROM sys.indexes WHERE name = N'IX_tblHallazgos_Seguimiento' AND object_id = OBJECT_ID(N'dbo.tblHallazgos'))) = N'EstadoSeguimiento,Origen,RevisadoAt'
          AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_tblHallazgos_Parte_Clase' AND object_id = OBJECT_ID(N'dbo.tblHallazgos')) THEN 1 ELSE 0 END, NULL;

-- 7g) Parametros: la tabla, sus filas iniciales y sus restricciones
INSERT #r SELECT N'Parametros: la tabla existe con sus 8 filas iniciales (6 con valor y 2 sin valor) y sin linea base de rechazo',
    CASE WHEN OBJECT_ID(N'dbo.tblParametros', N'U') IS NOT NULL
          AND (SELECT COUNT(*) FROM dbo.tblParametros WHERE Clave IN ('objetivo_diario_novedad', 'reporte_diario_destinatarios', 'reporte_diario_hora', 'horario_operacion_inicio', 'horario_operacion_fin', 'retencion_dias', 'gps_precision_minima_m', 'url_firmada_minutos')) = 8
          AND (SELECT COUNT(*) FROM dbo.tblParametros WHERE Clave IN ('reporte_diario_destinatarios', 'retencion_dias') AND Valor IS NULL) = 2
          AND (SELECT COUNT(*) FROM dbo.tblParametros WHERE Clave = 'objetivo_diario_novedad' AND Valor = N'1.2' AND Tipo = 'porcentaje') = 1
          AND (SELECT COUNT(*) FROM dbo.tblParametros WHERE Clave = 'reporte_diario_hora' AND Valor = N'06:00' AND Tipo = 'hora') = 1
          AND (SELECT COUNT(*) FROM dbo.tblParametros WHERE Clave = 'gps_precision_minima_m' AND Valor = N'30' AND Tipo = 'numero') = 1
          AND NOT EXISTS (SELECT 1 FROM dbo.tblParametros WHERE Clave LIKE 'linea_base_rechazo%' OR Clave LIKE '%fotos_minimas%') THEN 1 ELSE 0 END, NULL;
DECLARE @casosParam TABLE (Prueba nvarchar(200), Sentencia nvarchar(max), Esperado int);
INSERT @casosParam VALUES
 (N'Parametro: uno valido se guarda', N'INSERT dbo.tblParametros (Clave, Valor, Tipo, Descripcion) VALUES (''zz_param_1'', N''5'', ''numero'', N''prueba'')', 0),
 (N'PK clave del parametro', N'INSERT dbo.tblParametros (Clave, Valor, Tipo, Descripcion) VALUES (''zz_param_1'', N''6'', ''numero'', N''prueba'')', 2627),
 (N'CHECK tipo del parametro', N'INSERT dbo.tblParametros (Clave, Valor, Tipo, Descripcion) VALUES (''zz_param_2'', N''6'', ''fecha'', N''prueba'')', 547),
 (N'CHECK el valor del parametro no es vacio (sin valor es nulo)', N'INSERT dbo.tblParametros (Clave, Valor, Tipo, Descripcion) VALUES (''zz_param_3'', N'''', ''texto'', N''prueba'')', 547),
 (N'Parametro: sin valor es nulo', N'INSERT dbo.tblParametros (Clave, Valor, Tipo, Descripcion) VALUES (''zz_param_4'', NULL, ''lista'', N''prueba'')', 0),
 (N'FK quien actualizo el parametro', N'UPDATE dbo.tblParametros SET ActualizadoPor = NEWID() WHERE Clave = ''zz_param_1''', 547),
 (N'Parametro: se actualiza su valor y quien lo hizo', N'UPDATE dbo.tblParametros SET Valor = N''7'', ActualizadoPor = ''' + CAST(@u1 AS nvarchar(36)) + N''' WHERE Clave = ''zz_param_1''', 0);
DECLARE @pruebaP nvarchar(200), @sentenciaP nvarchar(max), @esperadoP int, @obtenidoP int, @mensajeP nvarchar(4000);
DECLARE cParam CURSOR LOCAL FAST_FORWARD FOR SELECT Prueba, Sentencia, Esperado FROM @casosParam;
OPEN cParam;
FETCH NEXT FROM cParam INTO @pruebaP, @sentenciaP, @esperadoP;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @obtenidoP = 0; SET @mensajeP = NULL;
    BEGIN TRY
        EXEC (@sentenciaP);
    END TRY
    BEGIN CATCH
        SET @obtenidoP = ERROR_NUMBER(); SET @mensajeP = ERROR_MESSAGE();
    END CATCH
    INSERT #r VALUES (@pruebaP, CASE WHEN @obtenidoP = @esperadoP THEN 1 ELSE 0 END, CASE WHEN @obtenidoP = @esperadoP THEN NULL ELSE CONCAT(N'esperaba ', @esperadoP, N' y fue ', @obtenidoP, N': ', @mensajeP) END);
    FETCH NEXT FROM cParam INTO @pruebaP, @sentenciaP, @esperadoP;
END
CLOSE cParam;
DEALLOCATE cParam;
DELETE FROM dbo.tblParametros WHERE Clave LIKE 'zz_param_%';

-- 7i) Vistas de los indicadores del tablero: existen con sus columnas y se pueden consultar (sus formulas las prueban las pruebas de integracion con datos)
INSERT #r SELECT N'Vistas de indicadores: las cinco existen', CASE WHEN (SELECT COUNT(*) FROM sys.views WHERE name IN (N'vwCobertura', N'vwAdopcion', N'vwTiempoCaptura', N'vwAveriasPorConfiguracion', N'vwIndicadorDiario')) = 5 THEN 1 ELSE 0 END, NULL;
INSERT #r SELECT N'Vistas de indicadores: tienen sus columnas',
    CASE WHEN COL_LENGTH(N'dbo.vwCobertura', N'RegistrosCerrados') IS NOT NULL AND COL_LENGTH(N'dbo.vwCobertura', N'Completa') IS NOT NULL AND COL_LENGTH(N'dbo.vwCobertura', N'FechaDespacho') IS NOT NULL
          AND COL_LENGTH(N'dbo.vwAdopcion', N'Programados') IS NOT NULL AND COL_LENGTH(N'dbo.vwAdopcion', N'ConSalidaDigital') IS NOT NULL AND COL_LENGTH(N'dbo.vwAdopcion', N'Porcentaje') IS NOT NULL
          AND COL_LENGTH(N'dbo.vwTiempoCaptura', N'Minutos') IS NOT NULL AND COL_LENGTH(N'dbo.vwTiempoCaptura', N'Proceso') IS NOT NULL
          AND COL_LENGTH(N'dbo.vwAveriasPorConfiguracion', N'ConductorCedula') IS NOT NULL AND COL_LENGTH(N'dbo.vwAveriasPorConfiguracion', N'NivelSeveridad') IS NOT NULL AND COL_LENGTH(N'dbo.vwAveriasPorConfiguracion', N'ZonaNombre') IS NOT NULL
          AND COL_LENGTH(N'dbo.vwIndicadorDiario', N'Despachados') IS NOT NULL AND COL_LENGTH(N'dbo.vwIndicadorDiario', N'ConNovedad') IS NOT NULL AND COL_LENGTH(N'dbo.vwIndicadorDiario', N'Porcentaje') IS NOT NULL THEN 1 ELSE 0 END, NULL;
DECLARE @vistasOk int = 1;
BEGIN TRY
    EXEC (N'SELECT COUNT(*) FROM dbo.vwCobertura; SELECT COUNT(*) FROM dbo.vwAdopcion; SELECT COUNT(*) FROM dbo.vwTiempoCaptura; SELECT COUNT(*) FROM dbo.vwAveriasPorConfiguracion; SELECT COUNT(*) FROM dbo.vwIndicadorDiario') WITH RESULT SETS UNDEFINED;
END TRY
BEGIN CATCH
    SET @vistasOk = 0;
END CATCH
INSERT #r SELECT N'Vistas de indicadores: se consultan sin error', @vistasOk, NULL;

-- 8) Limpieza final: el vehiculo base y lo que cuelga de el
DELETE FROM dbo.tblOrdenesSofasa WHERE SourceId = -424242;
EXEC #zz_borrar_vehiculo 'ZZ_TEST_VIN_0001';
INSERT #r SELECT N'Limpieza: no quedan etapas ni inspecciones del vehiculo base', CASE WHEN NOT EXISTS (SELECT 1 FROM dbo.tblEtapas WHERE VehiculoId = @v) AND NOT EXISTS (SELECT 1 FROM dbo.tblInspecciones WHERE Id IN (@i1, @i2)) THEN 1 ELSE 0 END, NULL;
INSERT #r SELECT N'Limpieza: el vehiculo base ya no esta en sus viajes', CASE WHEN NOT EXISTS (SELECT 1 FROM dbo.tblViajeVehiculos WHERE VehiculoId = @v) THEN 1 ELSE 0 END, NULL;
DELETE FROM dbo.tblViajes WHERE CodigoMtrack LIKE 'ZZ_TEST%' OR Placa LIKE N'ZZT_SIN_CODIGO%';
DELETE FROM dbo.tblUsuarios WHERE Email LIKE N'zz_test_%' OR Nombre LIKE N'ZZ_TEST%';
INSERT #r SELECT N'Limpieza: no quedan datos de prueba', CASE WHEN NOT EXISTS (SELECT 1 FROM dbo.tblUsuarios WHERE Nombre LIKE N'ZZ_TEST%') AND NOT EXISTS (SELECT 1 FROM dbo.tblVehiculos WHERE Vin LIKE 'ZZ_TEST%') AND NOT EXISTS (SELECT 1 FROM dbo.tblViajes WHERE CodigoMtrack LIKE 'ZZ_TEST%' OR Placa LIKE N'ZZT%') THEN 1 ELSE 0 END, NULL;

SELECT Prueba, CASE Ok WHEN 1 THEN 'OK' ELSE 'FALLO' END AS Resultado, Detalle FROM #r ORDER BY Ok, Prueba;
DECLARE @fallos int = (SELECT COUNT(*) FROM #r WHERE Ok = 0);
DECLARE @total int = (SELECT COUNT(*) FROM #r);
PRINT CONCAT('Verificaciones: ', @total, ', fallidas: ', @fallos);
IF @fallos > 0 THROW 52000, 'Hay verificaciones del DDL fallidas', 1;
GO
