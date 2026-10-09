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

-- Datos base
INSERT dbo.tblUsuarios (Id, Nombre, Email, Rol) VALUES (@u1, N'ZZ_TEST Uno', N'zz_test_uno@ejemplo.com', 'inspector');
INSERT dbo.tblUsuarios (Id, Nombre, Email, Rol) VALUES (@u2, N'ZZ_TEST Dos', N'zz_test_dos@ejemplo.com', 'conductor');
INSERT dbo.tblVehiculos (Id, Vin, Marca, Operacion) VALUES (@v, 'ZZ_TEST_VIN_0001', N'Renault', 'nacional');
INSERT dbo.tblEtapas (Id, VehiculoId, EtapaCatalogoId, Orden) VALUES (@e1, @v, @pc, 1);
INSERT dbo.tblEtapas (Id, VehiculoId, EtapaCatalogoId, Orden) VALUES (@e2, @v, @pt, 2);
INSERT dbo.tblInspecciones (Id, EtapaId, UsuarioId, Momento, Fecha) VALUES (@i1, @e1, @u1, 'informe', SYSDATETIMEOFFSET());
INSERT dbo.tblInspecciones (Id, EtapaId, UsuarioId, Momento, Fecha) VALUES (@i2, @e2, @u1, 'informe', SYSDATETIMEOFFSET());

-- 1) Vista de movilizados
INSERT #r SELECT N'Vista: sin informes cerrados no esta movilizado', CASE WHEN NOT EXISTS (SELECT 1 FROM dbo.vwVehiculosMovilizados WHERE VehiculoId = @v) THEN 1 ELSE 0 END, NULL;
UPDATE dbo.tblInspecciones SET Estado = 'cerrada' WHERE Id = @i1;
INSERT #r SELECT N'Vista: con una de dos etapas cerrada sigue pendiente', CASE WHEN NOT EXISTS (SELECT 1 FROM dbo.vwVehiculosMovilizados WHERE VehiculoId = @v) THEN 1 ELSE 0 END, NULL;
UPDATE dbo.tblInspecciones SET Estado = 'cerrada' WHERE Id = @i2;
INSERT #r SELECT N'Vista: con todas las etapas cerradas esta movilizado', CASE WHEN EXISTS (SELECT 1 FROM dbo.vwVehiculosMovilizados WHERE VehiculoId = @v) THEN 1 ELSE 0 END, NULL;

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
    INSERT dbo.tblEvidencias (Id, InspeccionId, StoragePath, SelloFecha) VALUES (NEWID(), @i1, N'zz.jpg', SYSDATETIMEOFFSET());
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
 (N'UNIQUE correo (sin distinguir mayusculas)', N'INSERT dbo.tblUsuarios (Id,Nombre,Email,Rol) VALUES (NEWID(),N''ZZ_TEST'',N''ZZ_TEST_UNO@EJEMPLO.COM'',''admin'')', 2627),
 (N'UNIQUE VIN (sin distinguir mayusculas)', N'INSERT dbo.tblVehiculos (Id,Vin,Marca,Operacion) VALUES (NEWID(),''zz_test_vin_0001'',N''X'',''nacional'')', 2627),
 (N'CHECK operacion invalida', N'INSERT dbo.tblVehiculos (Id,Vin,Marca,Operacion) VALUES (NEWID(),''ZZ_TEST_VIN_0002'',N''X'',''otra'')', 547),
 (N'UNIQUE (vehiculo, orden) en etapas', N'INSERT dbo.tblEtapas (Id,VehiculoId,EtapaCatalogoId,Orden) SELECT NEWID(),VehiculoId,EtapaCatalogoId,Orden FROM dbo.tblEtapas WHERE Orden=1 AND VehiculoId=(SELECT Id FROM dbo.tblVehiculos WHERE Vin=''ZZ_TEST_VIN_0001'')', 2627),
 (N'CHECK orden >= 1', N'INSERT dbo.tblEtapas (Id,VehiculoId,EtapaCatalogoId,Orden) SELECT NEWID(),(SELECT Id FROM dbo.tblVehiculos WHERE Vin=''ZZ_TEST_VIN_0001''),(SELECT TOP 1 Id FROM dbo.tblEtapasCatalogo WHERE Codigo=''descargue''),0', 547),
 (N'UNIQUE (etapa, momento) en inspecciones', N'INSERT dbo.tblInspecciones (Id,EtapaId,UsuarioId,Momento,Fecha) SELECT NEWID(),EtapaId,UsuarioId,Momento,Fecha FROM dbo.tblInspecciones WHERE Estado=''cerrada'' AND UsuarioId=(SELECT Id FROM dbo.tblUsuarios WHERE Email=N''zz_test_uno@ejemplo.com'')', 2627),
 (N'CHECK latitud fuera de rango', N'INSERT dbo.tblInspecciones (Id,EtapaId,UsuarioId,Momento,Fecha,Lat) SELECT NEWID(),(SELECT TOP 1 e.Id FROM dbo.tblEtapas e JOIN dbo.tblVehiculos v ON v.Id=e.VehiculoId WHERE v.Vin=''ZZ_TEST_VIN_0001'' AND e.Orden=1),(SELECT Id FROM dbo.tblUsuarios WHERE Email=N''zz_test_uno@ejemplo.com''),''check_in'',SYSDATETIMEOFFSET(),95', 547),
 (N'CHECK estado de inspeccion', N'INSERT dbo.tblInspecciones (Id,EtapaId,UsuarioId,Momento,Fecha,Estado) SELECT NEWID(),(SELECT TOP 1 e.Id FROM dbo.tblEtapas e JOIN dbo.tblVehiculos v ON v.Id=e.VehiculoId WHERE v.Vin=''ZZ_TEST_VIN_0001'' AND e.Orden=1),(SELECT Id FROM dbo.tblUsuarios WHERE Email=N''zz_test_uno@ejemplo.com''),''check_out'',SYSDATETIMEOFFSET(),''otro''', 547),
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
DECLARE @v3 uniqueidentifier = NEWID(), @e3 uniqueidentifier = NEWID(), @i3 uniqueidentifier = NEWID();
DECLARE @lugar uniqueidentifier = (SELECT Id FROM dbo.tblPuntosCustodia WHERE Codigo = 'cedi_1');
DECLARE @etapaDescargue uniqueidentifier = (SELECT Id FROM dbo.tblEtapasCatalogo WHERE Codigo = 'descargue');
INSERT dbo.tblVehiculos (Id, Vin, Marca, Operacion) VALUES (@v3, 'ZZ_TEST_VIN_0003', N'Renault', 'nacional');
INSERT dbo.tblEtapas (Id, VehiculoId, EtapaCatalogoId, PuntoCustodiaId, Orden) VALUES (@e3, @v3, @etapaDescargue, @lugar, 1);
INSERT dbo.tblInspecciones (Id, EtapaId, UsuarioId, Momento, Fecha) VALUES (@i3, @e3, @u1, 'informe', SYSDATETIMEOFFSET());
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

-- 5) Cascada: borrar el vehiculo elimina etapas, inspecciones y transferencias (no hay trigger de DELETE)
DELETE FROM dbo.tblOrdenesSofasa WHERE SourceId = -424242;
DELETE FROM dbo.tblVehiculos WHERE Vin = 'ZZ_TEST_VIN_0001';
INSERT #r SELECT N'Cascada: borrar vehiculo elimina etapas e inspecciones', CASE WHEN NOT EXISTS (SELECT 1 FROM dbo.tblEtapas WHERE VehiculoId = @v) AND NOT EXISTS (SELECT 1 FROM dbo.tblInspecciones WHERE Id IN (@i1, @i2)) THEN 1 ELSE 0 END, NULL;
DELETE FROM dbo.tblUsuarios WHERE Email LIKE N'zz_test_%';
INSERT #r SELECT N'Limpieza: no quedan datos de prueba', CASE WHEN NOT EXISTS (SELECT 1 FROM dbo.tblUsuarios WHERE Nombre LIKE N'ZZ_TEST%') AND NOT EXISTS (SELECT 1 FROM dbo.tblVehiculos WHERE Vin LIKE 'ZZ_TEST%') THEN 1 ELSE 0 END, NULL;

SELECT Prueba, CASE Ok WHEN 1 THEN 'OK' ELSE 'FALLO' END AS Resultado, Detalle FROM #r ORDER BY Ok, Prueba;
DECLARE @fallos int = (SELECT COUNT(*) FROM #r WHERE Ok = 0);
DECLARE @total int = (SELECT COUNT(*) FROM #r);
PRINT CONCAT('Verificaciones: ', @total, ', fallidas: ', @fallos);
IF @fallos > 0 THROW 52000, 'Hay verificaciones del DDL fallidas', 1;
GO
