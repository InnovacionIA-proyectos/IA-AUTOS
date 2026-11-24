/* =============================================================================
   La entrega por parada, el rechazo por novedad y los motivos del cierre con una sola firma, para una base de IA Autos YA EXISTENTE. Se ejecuta UNA vez (si ya esta aplicado, no cambia nada) y todo ocurre en UNA transaccion: si algo falla
   se revierte completo.

   Que hace:
     1. tblReclamos.LugarId: el reclamo de la entrega pasa a ser por parada. Cada reclamo de entrega ACTIVO que existia (uno por viaje) queda con una parada: la de cada destino del viaje que sea punto de su inspector (si el viaje tiene
        varias, el reclamo se copia una vez por parada); si ninguna lo es, la primera parada del viaje; y si el viaje no tiene destinos se libera. El indice unico de reclamos activos pasa a ser por viaje, proceso y parada (el reclamo del
        cargue sigue sin parada) y una restriccion exige la parada en toda entrega activa.
     2. tblInspecciones.Ciclo (1 por defecto): cuando un carro rechazado recibe otro destino su revision de llegada anterior se conserva como evidencia y la nueva es otro ciclo de la misma etapa; el indice de una inspeccion
        original por etapa y momento pasa a incluir el ciclo.
     3. tblTransferenciasCustodia: MotivoCodigo (el motivo codificado del cierre con una sola firma), TestigoNombre y TestigoCedula (el testigo sin usuario, siempre juntos), RechazoHallazgoId y RechazoMarcadoPor (el dano que motivo el
        rechazo y quien lo marco) y el tipo de cierre rechazado con su restriccion.
     4. tblMotivosSinInspector: los tres motivos codificados del cierre con una sola firma (se_nego_a_firmar, no_habia_nadie, sin_inspector_del_punto), INACTIVOS: no se ofrecen en la excepcion "No hay inspector", solo
        permiten que la novedad de proceso guarde el codigo.
     5. tblNotificaciones: CK_tblNotificaciones_Tipo admite el tipo carro_rechazado_en_destino, sin quitar ninguno de los dieciseis existentes.

   Seguridad de los datos: solo agrega columnas nulas, restricciones, un tipo de aviso y reescribe el indice de reclamos activos; la unica modificacion de datos es dar parada a los reclamos de entrega activos (o liberarlos si su viaje no
   tiene destinos). Se ejecuta ANTES de desplegar la API.

   Como ejecutarlo: abrir en SSMS, DBeaver o sqlcmd CONECTADO a la base de IA Autos, ajustar @esquema y ejecutar completo, en UNA sola ejecucion de script, con un usuario con permiso para
   modificar tablas (no el usuario de la aplicacion).
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
IF OBJECT_ID(@q + N'.tblReclamos', N'U') IS NULL OR OBJECT_ID(@q + N'.tblInspecciones', N'U') IS NULL OR OBJECT_ID(@q + N'.tblTransferenciasCustodia', N'U') IS NULL OR OBJECT_ID(@q + N'.tblNotificaciones', N'U') IS NULL OR OBJECT_ID(@q + N'.tblInspectorLugares', N'U') IS NULL
    THROW 50001, N'Faltan tblReclamos, tblInspecciones, tblTransferenciasCustodia, tblNotificaciones o tblInspectorLugares de IA Autos en el esquema indicado: revise @esquema.', 1;

-- Pasos en orden. {S} = esquema entre corchetes; ~ = comilla simple dentro del texto.
DECLARE @paso TABLE (n int IDENTITY(1, 1) PRIMARY KEY, descripcion nvarchar(100), s nvarchar(max));

INSERT @paso (descripcion, s) VALUES
(N'tblReclamos: LugarId (la parada del reclamo)', N'
IF COL_LENGTH(N~{S}.tblReclamos~, N~LugarId~) IS NULL
    ALTER TABLE {S}.tblReclamos ADD LugarId uniqueidentifier NULL'),
(N'tblReclamos: llave foranea a la parada', N'
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N~FK_tblReclamos_Lugar~ AND parent_object_id = OBJECT_ID(N~{S}.tblReclamos~))
    ALTER TABLE {S}.tblReclamos ADD CONSTRAINT FK_tblReclamos_Lugar FOREIGN KEY (LugarId) REFERENCES {S}.tblLugares (Id)'),
(N'tblReclamos: se quita el indice unico anterior', N'
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N~UX_tblReclamos_Activo~ AND object_id = OBJECT_ID(N~{S}.tblReclamos~))
   AND NOT EXISTS (SELECT 1 FROM sys.index_columns ic JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id
                   WHERE i.name = N~UX_tblReclamos_Activo~ AND i.object_id = OBJECT_ID(N~{S}.tblReclamos~) AND COL_NAME(ic.object_id, ic.column_id) = N~LugarId~)
    DROP INDEX UX_tblReclamos_Activo ON {S}.tblReclamos'),
(N'tblReclamos: cada entrega activa queda con su parada', N'
DECLARE @m TABLE (ReclamoId uniqueidentifier, ViajeId uniqueidentifier, InspectorId uniqueidentifier, ReclamadoAt datetimeoffset(3), LugarId uniqueidentifier, rn int);
INSERT @m (ReclamoId, ViajeId, InspectorId, ReclamadoAt, LugarId, rn)
SELECT r.Id, r.ViajeId, r.InspectorId, r.ReclamadoAt, d.LugarId, ROW_NUMBER() OVER (PARTITION BY r.Id ORDER BY d.LugarId)
FROM {S}.tblReclamos r
JOIN (SELECT DISTINCT vv.ViajeId, vv.LugarDestinoId AS LugarId FROM {S}.tblViajeVehiculos vv WHERE vv.LugarDestinoId IS NOT NULL AND vv.EstadoUnidad = N~activa~) d ON d.ViajeId = r.ViajeId
WHERE r.Proceso = N~entrega~ AND r.LiberadoAt IS NULL AND r.LugarId IS NULL
  AND EXISTS (SELECT 1 FROM {S}.tblInspectorLugares il WHERE il.UsuarioId = r.InspectorId AND il.LugarId = d.LugarId);
INSERT {S}.tblReclamos (Id, ViajeId, InspectorId, Proceso, ReclamadoAt, LugarId)
SELECT NEWID(), ViajeId, InspectorId, N~entrega~, ReclamadoAt, LugarId FROM @m WHERE rn > 1;
UPDATE r SET LugarId = m.LugarId FROM {S}.tblReclamos r JOIN @m m ON m.ReclamoId = r.Id AND m.rn = 1;
UPDATE r SET LugarId = (SELECT TOP 1 vv.LugarDestinoId FROM {S}.tblViajeVehiculos vv WHERE vv.ViajeId = r.ViajeId AND vv.LugarDestinoId IS NOT NULL ORDER BY vv.LugarDestinoId)
FROM {S}.tblReclamos r WHERE r.Proceso = N~entrega~ AND r.LiberadoAt IS NULL AND r.LugarId IS NULL;
UPDATE {S}.tblReclamos SET LiberadoAt = SYSDATETIMEOFFSET(), LiberadoPor = InspectorId
WHERE Proceso = N~entrega~ AND LiberadoAt IS NULL AND LugarId IS NULL'),
(N'tblReclamos: un reclamo activo por viaje, proceso y parada', N'
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N~UX_tblReclamos_Activo~ AND object_id = OBJECT_ID(N~{S}.tblReclamos~))
    CREATE UNIQUE INDEX UX_tblReclamos_Activo ON {S}.tblReclamos (ViajeId, Proceso, LugarId) WHERE LiberadoAt IS NULL'),
(N'tblReclamos: la entrega activa siempre tiene parada', N'
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N~CK_tblReclamos_Parada~ AND parent_object_id = OBJECT_ID(N~{S}.tblReclamos~))
    ALTER TABLE {S}.tblReclamos ADD CONSTRAINT CK_tblReclamos_Parada CHECK ((Proceso = N~cargue~ AND LugarId IS NULL) OR (Proceso = N~entrega~ AND (LugarId IS NOT NULL OR LiberadoAt IS NOT NULL)))'),
(N'tblInspecciones: Ciclo', N'
IF COL_LENGTH(N~{S}.tblInspecciones~, N~Ciclo~) IS NULL
    ALTER TABLE {S}.tblInspecciones ADD Ciclo int NOT NULL CONSTRAINT DF_tblInspecciones_Ciclo DEFAULT (1)'),
(N'tblInspecciones: una original por etapa, momento y ciclo', N'
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N~UX_tblInspecciones_Etapa_Momento_Original~ AND object_id = OBJECT_ID(N~{S}.tblInspecciones~))
   AND NOT EXISTS (SELECT 1 FROM sys.index_columns ic JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id
                   WHERE i.name = N~UX_tblInspecciones_Etapa_Momento_Original~ AND i.object_id = OBJECT_ID(N~{S}.tblInspecciones~) AND COL_NAME(ic.object_id, ic.column_id) = N~Ciclo~)
    DROP INDEX UX_tblInspecciones_Etapa_Momento_Original ON {S}.tblInspecciones;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N~UX_tblInspecciones_Etapa_Momento_Original~ AND object_id = OBJECT_ID(N~{S}.tblInspecciones~))
    CREATE UNIQUE INDEX UX_tblInspecciones_Etapa_Momento_Original ON {S}.tblInspecciones (EtapaId, Momento, Ciclo) WHERE CorrigeInspeccionId IS NULL'),
(N'tblInspecciones: el ciclo empieza en 1', N'
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N~CK_tblInspecciones_Ciclo~ AND parent_object_id = OBJECT_ID(N~{S}.tblInspecciones~))
    ALTER TABLE {S}.tblInspecciones ADD CONSTRAINT CK_tblInspecciones_Ciclo CHECK (Ciclo >= 1)'),
(N'tblTransferenciasCustodia: MotivoCodigo', N'
IF COL_LENGTH(N~{S}.tblTransferenciasCustodia~, N~MotivoCodigo~) IS NULL
    ALTER TABLE {S}.tblTransferenciasCustodia ADD MotivoCodigo varchar(30) NULL'),
(N'tblTransferenciasCustodia: TestigoNombre', N'
IF COL_LENGTH(N~{S}.tblTransferenciasCustodia~, N~TestigoNombre~) IS NULL
    ALTER TABLE {S}.tblTransferenciasCustodia ADD TestigoNombre nvarchar(150) NULL'),
(N'tblTransferenciasCustodia: TestigoCedula', N'
IF COL_LENGTH(N~{S}.tblTransferenciasCustodia~, N~TestigoCedula~) IS NULL
    ALTER TABLE {S}.tblTransferenciasCustodia ADD TestigoCedula varchar(30) NULL'),
(N'tblTransferenciasCustodia: RechazoHallazgoId', N'
IF COL_LENGTH(N~{S}.tblTransferenciasCustodia~, N~RechazoHallazgoId~) IS NULL
    ALTER TABLE {S}.tblTransferenciasCustodia ADD RechazoHallazgoId uniqueidentifier NULL'),
(N'tblTransferenciasCustodia: RechazoMarcadoPor', N'
IF COL_LENGTH(N~{S}.tblTransferenciasCustodia~, N~RechazoMarcadoPor~) IS NULL
    ALTER TABLE {S}.tblTransferenciasCustodia ADD RechazoMarcadoPor uniqueidentifier NULL'),
(N'tblTransferenciasCustodia: llaves foraneas del rechazo', N'
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N~FK_tblTransferencias_RechazoHallazgo~ AND parent_object_id = OBJECT_ID(N~{S}.tblTransferenciasCustodia~))
    ALTER TABLE {S}.tblTransferenciasCustodia ADD CONSTRAINT FK_tblTransferencias_RechazoHallazgo FOREIGN KEY (RechazoHallazgoId) REFERENCES {S}.tblHallazgos (Id);
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N~FK_tblTransferencias_RechazoMarcadoPor~ AND parent_object_id = OBJECT_ID(N~{S}.tblTransferenciasCustodia~))
    ALTER TABLE {S}.tblTransferenciasCustodia ADD CONSTRAINT FK_tblTransferencias_RechazoMarcadoPor FOREIGN KEY (RechazoMarcadoPor) REFERENCES {S}.tblUsuarios (Id)'),
(N'tblTransferenciasCustodia: motivo codificado del cierre con una sola firma', N'
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N~CK_tblTransferencias_MotivoCodigo~ AND parent_object_id = OBJECT_ID(N~{S}.tblTransferenciasCustodia~))
    ALTER TABLE {S}.tblTransferenciasCustodia ADD CONSTRAINT CK_tblTransferencias_MotivoCodigo CHECK (MotivoCodigo IS NULL OR MotivoCodigo IN (~se_nego_a_firmar~, ~no_habia_nadie~, ~sin_inspector_del_punto~, ~otro~))'),
(N'tblTransferenciasCustodia: testigo externo con nombre y cedula juntos', N'
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N~CK_tblTransferencias_TestigoExterno~ AND parent_object_id = OBJECT_ID(N~{S}.tblTransferenciasCustodia~))
    ALTER TABLE {S}.tblTransferenciasCustodia ADD CONSTRAINT CK_tblTransferencias_TestigoExterno CHECK ((TestigoNombre IS NULL AND TestigoCedula IS NULL) OR (TestigoNombre IS NOT NULL AND TestigoNombre <> ~~ AND TestigoCedula IS NOT NULL AND TestigoCedula <> ~~))'),
(N'tblTransferenciasCustodia: el rechazo lleva su dano y quien lo marco', N'
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N~CK_tblTransferencias_Rechazo~ AND parent_object_id = OBJECT_ID(N~{S}.tblTransferenciasCustodia~))
    ALTER TABLE {S}.tblTransferenciasCustodia ADD CONSTRAINT CK_tblTransferencias_Rechazo CHECK ((RechazoHallazgoId IS NULL AND RechazoMarcadoPor IS NULL) OR (RechazoHallazgoId IS NOT NULL AND RechazoMarcadoPor IS NOT NULL AND TipoCierre = ~rechazado~))'),
(N'tblTransferenciasCustodia: tipo de cierre rechazado', N'
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N~CK_tblTransferencias_TipoCierre~ AND parent_object_id = OBJECT_ID(N~{S}.tblTransferenciasCustodia~) AND definition LIKE N~%rechazado%~)
BEGIN
    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N~CK_tblTransferencias_TipoCierre~ AND parent_object_id = OBJECT_ID(N~{S}.tblTransferenciasCustodia~))
        ALTER TABLE {S}.tblTransferenciasCustodia DROP CONSTRAINT CK_tblTransferencias_TipoCierre;
    ALTER TABLE {S}.tblTransferenciasCustodia ADD CONSTRAINT CK_tblTransferencias_TipoCierre CHECK (TipoCierre IS NULL OR TipoCierre IN (~firmada~, ~con_reservas~, ~no_firma~, ~recibo_sin_entrega~, ~rechazado~));
END'),
(N'tblTransferenciasCustodia: cierre del carro rechazado', N'
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N~CK_tblTransferencias_Cierre~ AND parent_object_id = OBJECT_ID(N~{S}.tblTransferenciasCustodia~) AND definition LIKE N~%rechazado%~)
BEGIN
    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N~CK_tblTransferencias_Cierre~ AND parent_object_id = OBJECT_ID(N~{S}.tblTransferenciasCustodia~))
        ALTER TABLE {S}.tblTransferenciasCustodia DROP CONSTRAINT CK_tblTransferencias_Cierre;
    ALTER TABLE {S}.tblTransferenciasCustodia ADD CONSTRAINT CK_tblTransferencias_Cierre CHECK (
        TipoCierre IS NULL
        OR (TipoCierre = ~firmada~ AND FirmaEntrega IS NOT NULL AND FirmaEntrega <> ~~ AND FirmaRecibe IS NOT NULL AND FirmaRecibe <> ~~ AND ReservasTexto IS NULL)
        OR (TipoCierre = ~con_reservas~ AND FirmaEntrega IS NOT NULL AND FirmaEntrega <> ~~ AND FirmaRecibe IS NOT NULL AND FirmaRecibe <> ~~ AND ReservasTexto IS NOT NULL AND ReservasTexto <> ~~)
        -- Un carro rechazado por una novedad: las dos partes firman la constancia del rechazo, que queda unida al dano que lo motivo.
        OR (TipoCierre = ~rechazado~ AND FirmaEntrega IS NOT NULL AND FirmaEntrega <> ~~ AND FirmaRecibe IS NOT NULL AND FirmaRecibe <> ~~ AND ReservasTexto IS NULL AND RechazoHallazgoId IS NOT NULL)
        OR (TipoCierre = ~no_firma~ AND NoFirmaParte IS NOT NULL AND MotivoNoFirma IS NOT NULL AND MotivoNoFirma <> ~~ AND TestigoUsuarioId IS NOT NULL AND EvidenciaConstanciaId IS NOT NULL)
        -- Una sola firma y su motivo: firma solo quien recibe (nadie entrego) o firma solo quien entrega (quien recibe no firmo).
        OR (TipoCierre = ~recibo_sin_entrega~ AND NoFirmaParte IS NOT NULL AND MotivoNoFirma IS NOT NULL AND MotivoNoFirma <> ~~ AND ReservasTexto IS NULL
            AND ((NoFirmaParte = ~entrega~ AND EntregaUsuarioId IS NULL AND FirmaEntrega IS NULL AND FirmaEntregaAt IS NULL AND FirmaEntregaIp IS NULL
                  AND FirmaRecibe IS NOT NULL AND FirmaRecibe <> ~~)
              OR (NoFirmaParte = ~recibe~ AND EntregaUsuarioId IS NOT NULL AND FirmaEntrega IS NOT NULL AND FirmaEntrega <> ~~
                  AND FirmaRecibe IS NULL AND FirmaRecibeAt IS NULL AND FirmaRecibeIp IS NULL))));
END'),
(N'tblMotivosSinInspector: motivos del cierre con una sola firma', N'
INSERT {S}.tblMotivosSinInspector (Id, Codigo, Nombre, RequiereTexto, Orden, Activo)
SELECT NEWID(), v.Codigo, v.Nombre, 0, v.Orden, 0
FROM (VALUES
    (~se_nego_a_firmar~, N~Quien recibe se neg~ + NCHAR(243) + N~ a firmar~, 100),
    (~no_habia_nadie~, N~No hab~ + NCHAR(237) + N~a nadie para recibir~, 101),
    (~sin_inspector_del_punto~, N~El punto tiene inspector y no lleg~ + NCHAR(243), 102)
) AS v (Codigo, Nombre, Orden)
WHERE NOT EXISTS (SELECT 1 FROM {S}.tblMotivosSinInspector m WHERE m.Codigo = v.Codigo)'),
(N'tblNotificaciones: tipo carro_rechazado_en_destino', N'
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N~CK_tblNotificaciones_Tipo~ AND parent_object_id = OBJECT_ID(N~{S}.tblNotificaciones~) AND definition LIKE N~%carro_rechazado_en_destino%~)
BEGIN
    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N~CK_tblNotificaciones_Tipo~ AND parent_object_id = OBJECT_ID(N~{S}.tblNotificaciones~))
        ALTER TABLE {S}.tblNotificaciones DROP CONSTRAINT CK_tblNotificaciones_Tipo;
    ALTER TABLE {S}.tblNotificaciones ADD CONSTRAINT CK_tblNotificaciones_Tipo CHECK (Tipo IN (~novedad_conductor~, ~respuesta_conductor~, ~hallazgo_critico~, ~umbral_patron~, ~captura_incompleta~, ~transferencia_pendiente~, ~reporte_diario~, ~novedad_proceso~, ~reclamo_sin_actividad~, ~fuera_de_rango~, ~cargue_sin_reclamar~, ~propuesta_ubicacion~, ~permiso_sin_inspector~, ~firma_cargue_pendiente~, ~cambio_cargue_inspector~, ~cambio_destino_unidad~, ~carro_rechazado_en_destino~));
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

-- Resultado (esperado: EntregasSinParada = 0, IndiceUnicoPorParada = 1, ParadaObligatoria = 1, CicloDeRevision = 1, ColumnasDelCierre = 5, CierreConRechazo = 2, RestriccionesNuevas = 3, MotivosDelCierre = 3, TipoDeAviso = 1).
DECLARE @cuentas nvarchar(max) = REPLACE(N'
SELECT
    (SELECT COUNT(*) FROM {S}.tblReclamos WHERE Proceso = N~entrega~ AND LiberadoAt IS NULL AND LugarId IS NULL) AS EntregasSinParada,
    (SELECT COUNT(*) FROM sys.index_columns ic JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id WHERE i.name = N~UX_tblReclamos_Activo~ AND i.object_id = OBJECT_ID(N~{S}.tblReclamos~) AND COL_NAME(ic.object_id, ic.column_id) = N~LugarId~) AS IndiceUnicoPorParada,
    (SELECT COUNT(*) FROM sys.check_constraints WHERE name = N~CK_tblReclamos_Parada~ AND parent_object_id = OBJECT_ID(N~{S}.tblReclamos~)) AS ParadaObligatoria,
    (SELECT COUNT(*) FROM sys.index_columns ic JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id WHERE i.name = N~UX_tblInspecciones_Etapa_Momento_Original~ AND i.object_id = OBJECT_ID(N~{S}.tblInspecciones~) AND COL_NAME(ic.object_id, ic.column_id) = N~Ciclo~) AS CicloDeRevision,
    (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N~{S}.tblTransferenciasCustodia~) AND name IN (N~MotivoCodigo~, N~TestigoNombre~, N~TestigoCedula~, N~RechazoHallazgoId~, N~RechazoMarcadoPor~)) AS ColumnasDelCierre,
    (SELECT COUNT(*) FROM sys.check_constraints WHERE name IN (N~CK_tblTransferencias_TipoCierre~, N~CK_tblTransferencias_Cierre~) AND parent_object_id = OBJECT_ID(N~{S}.tblTransferenciasCustodia~) AND definition LIKE N~%rechazado%~) AS CierreConRechazo,
    (SELECT COUNT(*) FROM sys.check_constraints WHERE name IN (N~CK_tblTransferencias_MotivoCodigo~, N~CK_tblTransferencias_TestigoExterno~, N~CK_tblTransferencias_Rechazo~) AND parent_object_id = OBJECT_ID(N~{S}.tblTransferenciasCustodia~)) AS RestriccionesNuevas,
    (SELECT COUNT(*) FROM {S}.tblMotivosSinInspector WHERE Codigo IN (N~se_nego_a_firmar~, N~no_habia_nadie~, N~sin_inspector_del_punto~)) AS MotivosDelCierre,
    (SELECT COUNT(*) FROM sys.check_constraints WHERE name = N~CK_tblNotificaciones_Tipo~ AND parent_object_id = OBJECT_ID(N~{S}.tblNotificaciones~) AND definition LIKE N~%carro_rechazado_en_destino%~) AS TipoDeAviso;', N'{S}', @q);
SET @cuentas = REPLACE(@cuentas, N'~', N'''');
EXEC (@cuentas);
