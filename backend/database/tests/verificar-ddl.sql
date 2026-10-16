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
DELETE FROM dbo.tblVehiculos WHERE Vin = 'ZZ_TEST_VIN_0003';
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

-- Borrar el vehiculo de la prueba se lleva sus inspecciones, versiones y fotos (cascada); la cadena de versiones no la impide.
DELETE FROM dbo.tblVehiculos WHERE Vin = 'ZZ_TEST_VIN_0006';
DELETE FROM dbo.tblViajes WHERE CodigoMtrack = 'ZZ_TEST_MTK_6';
INSERT #r SELECT N'Cascada: borrar el vehiculo borra sus inspecciones con versiones y fotos',
    CASE WHEN NOT EXISTS (SELECT 1 FROM dbo.tblInspecciones WHERE Id IN (@s5, @l5, @c5)) AND NOT EXISTS (SELECT 1 FROM dbo.tblEvidencias WHERE StoragePath LIKE N'zz-%') THEN 1 ELSE 0 END, NULL;

-- 8) Cascada: borrar el vehiculo elimina etapas, inspecciones, transferencias y su lugar en los viajes (no hay trigger de DELETE)
DELETE FROM dbo.tblOrdenesSofasa WHERE SourceId = -424242;
DELETE FROM dbo.tblVehiculos WHERE Vin = 'ZZ_TEST_VIN_0001';
INSERT #r SELECT N'Cascada: borrar vehiculo elimina etapas e inspecciones', CASE WHEN NOT EXISTS (SELECT 1 FROM dbo.tblEtapas WHERE VehiculoId = @v) AND NOT EXISTS (SELECT 1 FROM dbo.tblInspecciones WHERE Id IN (@i1, @i2)) THEN 1 ELSE 0 END, NULL;
INSERT #r SELECT N'Cascada: borrar vehiculo lo quita de sus viajes', CASE WHEN NOT EXISTS (SELECT 1 FROM dbo.tblViajeVehiculos WHERE VehiculoId = @v) THEN 1 ELSE 0 END, NULL;
DELETE FROM dbo.tblViajes WHERE CodigoMtrack LIKE 'ZZ_TEST%' OR Placa LIKE N'ZZT_SIN_CODIGO%';
DELETE FROM dbo.tblUsuarios WHERE Email LIKE N'zz_test_%' OR Nombre LIKE N'ZZ_TEST%';
INSERT #r SELECT N'Limpieza: no quedan datos de prueba', CASE WHEN NOT EXISTS (SELECT 1 FROM dbo.tblUsuarios WHERE Nombre LIKE N'ZZ_TEST%') AND NOT EXISTS (SELECT 1 FROM dbo.tblVehiculos WHERE Vin LIKE 'ZZ_TEST%') AND NOT EXISTS (SELECT 1 FROM dbo.tblViajes WHERE CodigoMtrack LIKE 'ZZ_TEST%' OR Placa LIKE N'ZZT%') THEN 1 ELSE 0 END, NULL;

SELECT Prueba, CASE Ok WHEN 1 THEN 'OK' ELSE 'FALLO' END AS Resultado, Detalle FROM #r ORDER BY Ok, Prueba;
DECLARE @fallos int = (SELECT COUNT(*) FROM #r WHERE Ok = 0);
DECLARE @total int = (SELECT COUNT(*) FROM #r);
PRINT CONCAT('Verificaciones: ', @total, ', fallidas: ', @fallos);
IF @fallos > 0 THROW 52000, 'Hay verificaciones del DDL fallidas', 1;
GO
