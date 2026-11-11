/* =============================================================================
   IA Autos - SEMILLA DE DESARROLLO, parte 2: la ampliacion del sprint 5 (S6-HU-09.18, IA-AUTOS#174)

   Se ejecuta DESPUES de 01-semilla-base.sql. Agrega:
     - un SEGUNDO cliente de prueba (seed_prueba) con sus 5 usuarios (admin, coordinador, comercial, inspector y conductor) y 4 vehiculos (VIN SEEDP...);
     - dos conductores mas de sofasa: uno con el CAMBIO DE PIN PENDIENTE (ESC-12) y otro con la cuenta BLOQUEADA (ESC-11);
     - viajes en los cuatro estados: SEED-MTK-0002 EN CURSO con VIN de los DOS clientes, SEED-MTK-0003 PROGRAMADO, SEED-MTK-0004 CANCELADO (y SEED-MTK-0001 FINALIZADO de la parte 1), mas SEED-MTK-0005 (un Cargue SIN personal
       de apoyo: la salida la hizo el conductor, con su recibo sin entrega) y SEED-MTK-0006 (una inspeccion CORREGIDA: version 1 y version 2, ambas cerradas);
     - etapas con las 9 vistas (con nota en baul y cojineria) y fotos SIN ubicacion (GPS en 0), e inspecciones de Transporte sin vistas;
     - una transferencia ABIERTA que bloquea la salida siguiente (ESC-15: ETAPA_PREVIA_SIN_CIERRE con transferencia_sin_cierre);
     - ordenes Sofasa vinculadas por codigo MTrack.

   Mismas reglas que la parte 1: todo lleva prefijo (VIN SEED..., correo @seed.iaautos.test, codigo SEED-...), se borra con 00-limpiar-semilla.sql, es SOLO para desarrollo y pruebas y no llega a QA ni a produccion.
   Acceso de prueba: igual que la parte 1 (clave Seed-Dev-2026! con correo para admin, coordinador y comercial; PIN 135790 con cedula para inspector y conductor). Cedulas inventadas: inspector del cliente de prueba
   90000011, conductor 90000012, conductor con PIN pendiente 90000003, conductor bloqueado 90000004.

   Como ejecutarlo: igual que la parte 1 (esquema db_IA_AUTOS; en una base con ddl.sql, reemplace db_IA_AUTOS. por dbo.), en UNA sola ejecucion de script. Si ya esta cargada, se detiene.
   ============================================================================= */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;

IF NOT EXISTS (SELECT 1 FROM db_IA_AUTOS.tblVehiculos WHERE Vin = 'SEEDS000000000001')
    THROW 50013, N'Falta la parte 1: ejecute antes 01-semilla-base.sql.', 1;
IF EXISTS (SELECT 1 FROM db_IA_AUTOS.tblClientes WHERE Codigo = 'seed_prueba')
    THROW 50014, N'La ampliacion ya esta cargada: ejecute primero 00-limpiar-semilla.sql y vuelva a empezar por la parte 1.', 1;

DECLARE @ahora datetimeoffset = SYSDATETIMEOFFSET();
DECLARE @hClave nvarchar(800) = N'pbkdf2-sha256$600000$Xu1e7V7tXu1e7V7tXu1e7Q==$t+ODrhkhUzX4kt+b7Ax6RrUUdsGNzXbs601pZrZEt8k=';
DECLARE @hPin nvarchar(800) = N'pbkdf2-sha256$600000$Xu1e7V7tXu1e7V7tXu1e7Q==$dWhspOHqQtGGI981TQoUIGTGw4pCIbbQC+aZtp6fQYw=';
DECLARE @firma nvarchar(max) = N'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==';
DECLARE @sofasa uniqueidentifier = (SELECT Id FROM db_IA_AUTOS.tblClientes WHERE Codigo = 'sofasa');
DECLARE @ninera uniqueidentifier = (SELECT Id FROM db_IA_AUTOS.tblTiposUnidadTransportadora WHERE Codigo = 'ninera');
DECLARE @cedi uniqueidentifier = (SELECT Id FROM db_IA_AUTOS.tblLugares WHERE Nombre = 'SEED CEDI Envigado');
DECLARE @norte uniqueidentifier = (SELECT Id FROM db_IA_AUTOS.tblLugares WHERE Nombre = 'SEED Concesionario Norte');
DECLARE @sur uniqueidentifier = (SELECT Id FROM db_IA_AUTOS.tblLugares WHERE Nombre = 'SEED Concesionario Sur');
DECLARE @pruebaCli uniqueidentifier = NEWID();

BEGIN TRY
    BEGIN TRANSACTION;

    ALTER TABLE db_IA_AUTOS.tblInspecciones DISABLE TRIGGER ALL;
    ALTER TABLE db_IA_AUTOS.tblEvidencias DISABLE TRIGGER ALL;
    ALTER TABLE db_IA_AUTOS.tblTransferenciasCustodia DISABLE TRIGGER ALL;
    ALTER TABLE db_IA_AUTOS.tblNovedadesProceso DISABLE TRIGGER ALL;

    -- ------------------------------------------------------------------------------------------ el segundo cliente y sus usuarios
    INSERT db_IA_AUTOS.tblClientes (Id, Codigo, Nombre, Orden, Activo) VALUES (@pruebaCli, 'seed_prueba', N'Cliente de prueba (semilla)', 90, 1);

    CREATE TABLE #u (k varchar(20) COLLATE DATABASE_DEFAULT PRIMARY KEY, id uniqueidentifier NOT NULL DEFAULT NEWID(), nombre nvarchar(100) COLLATE DATABASE_DEFAULT NOT NULL, rol varchar(30) COLLATE DATABASE_DEFAULT NOT NULL,
                     cedula varchar(20) COLLATE DATABASE_DEFAULT NULL, cliente uniqueidentifier NOT NULL, debeCambiarPin bit NOT NULL DEFAULT 0, intentos int NOT NULL DEFAULT 0, bloqueadoHasta datetimeoffset NULL);
    INSERT #u (k, nombre, rol, cedula, cliente, debeCambiarPin, intentos, bloqueadoHasta) VALUES
        ('pru_admin', N'Administrador Semilla (Prueba)', 'admin', NULL, @pruebaCli, 0, 0, NULL),
        ('pru_coord', N'Coordinadora Semilla (Prueba)', 'coordinador_calidad', NULL, @pruebaCli, 0, 0, NULL),
        ('pru_comercial', N'Comercial Semilla (Prueba)', 'comercial', NULL, @pruebaCli, 0, 0, NULL),
        ('pru_inspector', N'Inspector Semilla (Prueba)', 'inspector', '90000011', @pruebaCli, 0, 0, NULL),
        ('pru_conductor', N'Conductor Semilla (Prueba)', 'conductor', '90000012', @pruebaCli, 0, 0, NULL),
        ('sof_cond_pin', N'Conductor con cambio de PIN pendiente (Sofasa)', 'conductor', '90000003', @sofasa, 1, 0, NULL),
        ('sof_cond_bloq', N'Conductor con cuenta bloqueada (Sofasa)', 'conductor', '90000004', @sofasa, 0, 5, DATEADD(HOUR, 24, @ahora));
    INSERT db_IA_AUTOS.tblUsuarios (Id, Nombre, Email, Rol, SubtipoInspector, Activo, PasswordHash, TipoCredencial, Cedula, PinHash, DebeCambiarPin, IntentosFallidos, BloqueadoHasta, ClienteId)
    SELECT id, nombre, 'seed.' + k + '@seed.iaautos.test', rol, CASE WHEN rol = 'inspector' THEN 'nacional' END, 1, CASE WHEN cedula IS NULL THEN @hClave END,
           CASE WHEN cedula IS NULL THEN 'password' ELSE 'pin' END, cedula, CASE WHEN cedula IS NOT NULL THEN @hPin END, debeCambiarPin, intentos, bloqueadoHasta, cliente
    FROM #u;
    -- Los usuarios de la parte 1 que esta parte usa, por su clave.
    INSERT #u (k, nombre, rol, cliente, id)
    SELECT SUBSTRING(Email, 6, CHARINDEX('@', Email) - 6), Nombre, Rol, @sofasa, Id FROM db_IA_AUTOS.tblUsuarios WHERE Email IN
        ('seed.sof_inspector@seed.iaautos.test', 'seed.sof_conductor@seed.iaautos.test', 'seed.sof_coord@seed.iaautos.test');

    -- ------------------------------------------------------------------------------------------ vehiculos del cliente de prueba (SEEDP + 12 digitos)
    CREATE TABLE #v (vin varchar(17) COLLATE DATABASE_DEFAULT PRIMARY KEY, id uniqueidentifier NOT NULL DEFAULT NEWID());
    INSERT #v (vin) VALUES ('SEEDP000000000001'), ('SEEDP000000000002'), ('SEEDP000000000003'), ('SEEDP000000000004');
    INSERT db_IA_AUTOS.tblVehiculos (Id, Vin, Marca, Modelo, Color, Operacion, ClienteId, Activo)
    SELECT id, vin, N'Mazda', N'CX-30', N'Azul', 'nacional', @pruebaCli, 1 FROM #v;
    INSERT #v (vin, id) SELECT Vin, Id FROM db_IA_AUTOS.tblVehiculos WHERE Vin IN
        ('SEEDS000000000004', 'SEEDS000000000005', 'SEEDS000000000006', 'SEEDS000000000007', 'SEEDS000000000008', 'SEEDS000000000009');

    -- ------------------------------------------------------------------------------------------ viajes y sus unidades
    CREATE TABLE #vj (codigo varchar(20) COLLATE DATABASE_DEFAULT PRIMARY KEY, id uniqueidentifier NOT NULL DEFAULT NEWID(), estado varchar(20) COLLATE DATABASE_DEFAULT NOT NULL,
                      conductor varchar(20) COLLATE DATABASE_DEFAULT NOT NULL, inspector varchar(20) COLLATE DATABASE_DEFAULT NOT NULL, placa nvarchar(20) COLLATE DATABASE_DEFAULT NOT NULL, desdeHoras int NOT NULL);
    INSERT #vj (codigo, estado, conductor, inspector, placa, desdeHoras) VALUES
        ('SEED-MTK-0002', 'en_curso', 'sof_conductor', 'sof_inspector', N'SED002', -20),
        ('SEED-MTK-0003', 'programado', 'sof_conductor', 'sof_inspector', N'SED003', 30),
        ('SEED-MTK-0004', 'cancelado', 'sof_conductor', 'sof_inspector', N'SED004', -10),
        ('SEED-MTK-0005', 'en_curso', 'sof_conductor', 'sof_inspector', N'SED005', -8),
        ('SEED-MTK-0006', 'en_curso', 'sof_conductor', 'sof_inspector', N'SED006', -12);
    INSERT db_IA_AUTOS.tblViajes (Id, CodigoMtrack, TipoUnidadId, Placa, Trailer, ConductorId, InspectorId, OrigenId, DestinoId, FechaProgramada, LugarCargueId, Capacidad, Estado, CreadoPor, CreatedAt, UpdatedAt)
    SELECT j.id, j.codigo, @ninera, j.placa, N'SEEDT' + RIGHT(j.codigo, 2), c.id, i.id, (SELECT TipoId FROM db_IA_AUTOS.tblLugares WHERE Id = @cedi), (SELECT TipoId FROM db_IA_AUTOS.tblLugares WHERE Id = @norte),
           DATEADD(HOUR, j.desdeHoras, @ahora), @cedi, 7, j.estado, (SELECT id FROM #u WHERE k = 'sof_coord'), DATEADD(HOUR, j.desdeHoras - 6, @ahora), @ahora
    FROM #vj j JOIN #u c ON c.k = j.conductor JOIN #u i ON i.k = j.inspector;

    CREATE TABLE #vv (codigo varchar(20) COLLATE DATABASE_DEFAULT NOT NULL, vin varchar(17) COLLATE DATABASE_DEFAULT NOT NULL, destino uniqueidentifier NOT NULL, posicion tinyint NOT NULL, PRIMARY KEY (codigo, vin));
    INSERT #vv (codigo, vin, destino, posicion) VALUES
        ('SEED-MTK-0002', 'SEEDS000000000004', @norte, 1), ('SEED-MTK-0002', 'SEEDS000000000005', @sur, 2), ('SEED-MTK-0002', 'SEEDP000000000001', @norte, 3),
        ('SEED-MTK-0003', 'SEEDS000000000006', @norte, 1),
        ('SEED-MTK-0004', 'SEEDS000000000007', @sur, 1),
        ('SEED-MTK-0005', 'SEEDS000000000008', @norte, 1),
        ('SEED-MTK-0006', 'SEEDS000000000009', @sur, 1);
    INSERT db_IA_AUTOS.tblViajeVehiculos (Id, ViajeId, VehiculoId, LugarDestinoId, Posicion, Completa)
    SELECT NEWID(), j.id, v.id, x.destino, x.posicion, 0 FROM #vv x JOIN #vj j ON j.codigo = x.codigo JOIN #v v ON v.vin = x.vin;

    -- Las 3 etapas de cada unidad de esos viajes.
    CREATE TABLE #e (vin varchar(17) COLLATE DATABASE_DEFAULT NOT NULL, ec varchar(20) COLLATE DATABASE_DEFAULT NOT NULL, orden int NOT NULL, id uniqueidentifier NOT NULL DEFAULT NEWID(),
                     vehiculoId uniqueidentifier NOT NULL, viajeId uniqueidentifier NOT NULL, PRIMARY KEY (vin, ec));
    INSERT #e (vin, ec, orden, vehiculoId, viajeId)
    SELECT x.vin, c.Codigo, c.Orden, v.id, j.id FROM #vv x JOIN #vj j ON j.codigo = x.codigo JOIN #v v ON v.vin = x.vin CROSS JOIN db_IA_AUTOS.tblEtapasCatalogo c;
    INSERT db_IA_AUTOS.tblEtapas (Id, VehiculoId, EtapaCatalogoId, PuntoCustodiaId, ViajeId, Orden)
    SELECT e.id, e.vehiculoId, c.Id, CASE e.ec WHEN 'cargue' THEN (SELECT TipoId FROM db_IA_AUTOS.tblLugares WHERE Id = @cedi) WHEN 'descargue' THEN (SELECT TipoId FROM db_IA_AUTOS.tblLugares WHERE Id = @norte) END,
           e.viajeId, e.orden
    FROM #e e JOIN db_IA_AUTOS.tblEtapasCatalogo c ON c.Codigo = e.ec;

    -- ------------------------------------------------------------------------------------------ inspecciones (una fila por inspeccion; estado, version, fotos y GPS segun el caso)
    -- fotos: 1 = las 9 vistas; sinGps: 1 = las fotos de baul y cojineria quedaron sin ubicacion (GPS en 0). usuario: clave del autor.
    CREATE TABLE #i (vin varchar(17) COLLATE DATABASE_DEFAULT NOT NULL, ec varchar(20) COLLATE DATABASE_DEFAULT NOT NULL, momento varchar(10) COLLATE DATABASE_DEFAULT NOT NULL, version int NOT NULL,
                     estado varchar(10) COLLATE DATABASE_DEFAULT NOT NULL, usuario varchar(20) COLLATE DATABASE_DEFAULT NOT NULL, minutos int NOT NULL, fotos bit NOT NULL, sinGps bit NOT NULL,
                     id uniqueidentifier NOT NULL DEFAULT NEWID(), etapaId uniqueidentifier NULL, viajeId uniqueidentifier NULL, PRIMARY KEY (vin, ec, momento, version));
    INSERT #i (vin, ec, momento, version, estado, usuario, minutos, fotos, sinGps) VALUES
        -- SEED-MTK-0002 (en curso, con unidades de los dos clientes)
        ('SEEDS000000000004', 'cargue', 'salida', 1, 'cerrada', 'sof_inspector', 0, 1, 0),
        ('SEEDS000000000004', 'cargue', 'llegada', 1, 'cerrada', 'sof_inspector', 90, 1, 0),
        ('SEEDS000000000004', 'transporte', 'salida', 1, 'borrador', 'sof_conductor', 200, 0, 0),
        ('SEEDS000000000005', 'cargue', 'salida', 1, 'cerrada', 'sof_inspector', 20, 1, 0),
        ('SEEDS000000000005', 'cargue', 'llegada', 1, 'cerrada', 'sof_inspector', 110, 1, 0),
        ('SEEDP000000000001', 'cargue', 'salida', 1, 'cerrada', 'pru_inspector', 40, 1, 0),
        -- SEED-MTK-0005 (un Cargue sin personal de apoyo: la salida la hizo el conductor)
        ('SEEDS000000000008', 'cargue', 'salida', 1, 'cerrada', 'sof_conductor', 0, 0, 0),
        -- SEED-MTK-0006 (una inspeccion corregida: version 1 y version 2, ambas cerradas; la 2 con las fotos de baul y cojineria sin ubicacion)
        ('SEEDS000000000009', 'cargue', 'salida', 1, 'cerrada', 'sof_inspector', 0, 1, 0),
        ('SEEDS000000000009', 'cargue', 'salida', 2, 'cerrada', 'sof_inspector', 60, 1, 1);
    UPDATE i SET etapaId = e.id, viajeId = e.viajeId FROM #i i JOIN #e e ON e.vin = i.vin AND e.ec = i.ec;

    INSERT db_IA_AUTOS.tblInspecciones (Id, EtapaId, ViajeId, UsuarioId, Momento, Fecha, Lat, Lng, Estado, ConductorNombre, Placa, Trailer, IniciadaAt, CerradaAt, Version, CorrigeInspeccionId, MotivoSinInspector,
                                        MotivoSinInspectorTexto, ConfirmacionSitio)
    SELECT i.id, i.etapaId, i.viajeId, u.id, i.momento, DATEADD(MINUTE, i.minutos, DATEADD(HOUR, j.desdeHoras, @ahora)), 6.17, -75.58, i.estado, c.nombre, j.placa, N'SEEDT' + RIGHT(j.codigo, 2),
           DATEADD(MINUTE, i.minutos, DATEADD(HOUR, j.desdeHoras, @ahora)), CASE WHEN i.estado = 'cerrada' THEN DATEADD(MINUTE, i.minutos + 25, DATEADD(HOUR, j.desdeHoras, @ahora)) END, i.version,
           (SELECT p.id FROM #i p WHERE p.vin = i.vin AND p.ec = i.ec AND p.momento = i.momento AND p.version = i.version - 1),
           CASE WHEN i.vin = 'SEEDS000000000008' THEN 'inspector_no_llego' END, NULL, 'sin_confirmacion_posible'
    FROM #i i JOIN #vj j ON j.id = i.viajeId JOIN #u u ON u.k = i.usuario JOIN #u c ON c.k = j.conductor;

    -- Las 9 vistas (las 7 obligatorias y las dos con nota); las de baul y cojineria van sin ubicacion donde se pidio.
    INSERT db_IA_AUTOS.tblEvidencias (Id, InspeccionId, StoragePath, SelloFecha, SelloLat, SelloLng, VistaId, HashSha256, Precision, DispositivoId, Nota)
    SELECT NEWID(), i.id, CONCAT('seed/', i.vin, '/', i.ec, '-', i.momento, '-v', i.version, '-', f.Codigo, '.jpg'), DATEADD(MINUTE, 5 + f.Orden, DATEADD(MINUTE, i.minutos, DATEADD(HOUR, j.desdeHoras, @ahora))),
           CASE WHEN i.sinGps = 1 AND f.Codigo IN ('piezas_baul', 'cojineria') THEN 0 ELSE 6.17 END, CASE WHEN i.sinGps = 1 AND f.Codigo IN ('piezas_baul', 'cojineria') THEN 0 ELSE -75.58 END, f.Id,
           LOWER(CONVERT(char(64), HASHBYTES('SHA2_256', CONCAT(i.vin, i.ec, i.momento, i.version, f.Codigo)), 2)), CASE WHEN i.sinGps = 1 AND f.Codigo IN ('piezas_baul', 'cojineria') THEN NULL ELSE 8.5 END,
           N'seed-celular-02', CASE f.Codigo WHEN 'piezas_baul' THEN N'Baul sin novedad: gato, llanta de repuesto y triangulo completos' WHEN 'cojineria' THEN N'Cojineria sin manchas ni rasgaduras' END
    FROM #i i JOIN #vj j ON j.id = i.viajeId CROSS JOIN db_IA_AUTOS.tblVistasFoto f WHERE i.fotos = 1 AND f.Activo = 1;
    -- La salida del Cargue sin apoyo documenta la acomodacion en la ninera (no las vistas de estado): 2 fotos de acomodacion.
    INSERT db_IA_AUTOS.tblEvidencias (Id, InspeccionId, StoragePath, SelloFecha, SelloLat, SelloLng, HashSha256, Precision, DispositivoId, Nota, EsAcomodacion)
    SELECT NEWID(), i.id, CONCAT('seed/', i.vin, '/acomodacion-', n.n, '.jpg'), DATEADD(MINUTE, 6 + n.n, DATEADD(HOUR, j.desdeHoras, @ahora)), 6.17, -75.58,
           LOWER(CONVERT(char(64), HASHBYTES('SHA2_256', CONCAT(i.vin, 'acomodacion', n.n)), 2)), 9.0, N'seed-celular-02', N'Acomodacion de la unidad en la ninera', 1
    FROM #i i JOIN #vj j ON j.id = i.viajeId CROSS JOIN (VALUES (1), (2)) AS n (n) WHERE i.vin = 'SEEDS000000000008';

    -- El aviso de proceso del cargue sin inspector (la novedad que ve el coordinador).
    INSERT db_IA_AUTOS.tblNovedadesProceso (Id, Tipo, ViajeId, VehiculoId, EtapaId, InspeccionId, MotivoCodigo, MotivoTexto, UsuarioId, Fecha, Lat, Lng)
    SELECT NEWID(), 'cargue_sin_inspector', i.viajeId, e.vehiculoId, i.etapaId, i.id, 'inspector_no_llego', NULL, u.id, DATEADD(MINUTE, 5, DATEADD(HOUR, j.desdeHoras, @ahora)), 6.17, -75.58
    FROM #i i JOIN #e e ON e.vin = i.vin AND e.ec = i.ec JOIN #vj j ON j.id = i.viajeId JOIN #u u ON u.k = 'sof_conductor' WHERE i.vin = 'SEEDS000000000008';

    -- ------------------------------------------------------------------------------------------ transferencias
    -- tipo: firmada; abierta (solo firmo quien entrega: bloquea la salida siguiente, ESC-15); recibo_sin_entrega (el conductor hizo el Cargue y cierra solo con su firma).
    CREATE TABLE #t (vin varchar(17) COLLATE DATABASE_DEFAULT NOT NULL, ec varchar(20) COLLATE DATABASE_DEFAULT NOT NULL, momento varchar(10) COLLATE DATABASE_DEFAULT NOT NULL, tipo varchar(20) COLLATE DATABASE_DEFAULT NOT NULL,
                     entrega varchar(20) COLLATE DATABASE_DEFAULT NULL, recibe varchar(20) COLLATE DATABASE_DEFAULT NOT NULL);
    INSERT #t (vin, ec, momento, tipo, entrega, recibe) VALUES
        ('SEEDS000000000004', 'cargue', 'llegada', 'firmada', 'sof_inspector', 'sof_conductor'),
        ('SEEDS000000000005', 'cargue', 'llegada', 'abierta', 'sof_inspector', 'sof_conductor'),
        ('SEEDS000000000008', 'cargue', 'salida', 'recibo_sin_entrega', NULL, 'sof_conductor');
    INSERT db_IA_AUTOS.tblTransferenciasCustodia (Id, InspeccionId, EntregaUsuarioId, RecibeUsuarioId, FirmaEntrega, FirmaRecibe, FirmadaAt, FirmaEntregaAt, FirmaEntregaIp, FirmaRecibeAt, FirmaRecibeIp, TipoCierre, NoFirmaParte, MotivoNoFirma)
    SELECT NEWID(), i.id, ue.id, ur.id,
           CASE WHEN t.tipo IN ('firmada', 'abierta') THEN @firma END, CASE WHEN t.tipo IN ('firmada', 'recibo_sin_entrega') THEN @firma END,
           CASE WHEN t.tipo <> 'abierta' THEN DATEADD(MINUTE, i.minutos + 30, DATEADD(HOUR, j.desdeHoras, @ahora)) END,
           CASE WHEN t.tipo IN ('firmada', 'abierta') THEN DATEADD(MINUTE, i.minutos + 26, DATEADD(HOUR, j.desdeHoras, @ahora)) END, CASE WHEN t.tipo IN ('firmada', 'abierta') THEN '10.20.30.40' END,
           CASE WHEN t.tipo IN ('firmada', 'recibo_sin_entrega') THEN DATEADD(MINUTE, i.minutos + 28, DATEADD(HOUR, j.desdeHoras, @ahora)) END, CASE WHEN t.tipo IN ('firmada', 'recibo_sin_entrega') THEN '10.20.30.41' END,
           CASE t.tipo WHEN 'abierta' THEN NULL ELSE t.tipo END,
           CASE WHEN t.tipo = 'recibo_sin_entrega' THEN 'entrega' END, CASE WHEN t.tipo = 'recibo_sin_entrega' THEN N'El Cargue no tuvo personal de apoyo: la salida la hizo el conductor y nadie de la empresa firma la entrega' END
    FROM #t t JOIN #i i ON i.vin = t.vin AND i.ec = t.ec AND i.momento = t.momento AND i.version = 1 JOIN #vj j ON j.id = i.viajeId
    LEFT JOIN #u ue ON ue.k = t.entrega JOIN #u ur ON ur.k = t.recibe;

    -- ------------------------------------------------------------------------------------------ ordenes Sofasa vinculadas por codigo MTrack
    INSERT db_IA_AUTOS.tblOrdenesSofasa (Id, SourceId, StatusCode, DatetimeEvent, OrdenNumber, OrdenSap, OriginCedi, Vin, Make, ModelDescription, DealerCode, DestinationCity, DestinationAddress, LoadingDate, Appointment,
                                         LoadingDock, Weight, VehiculoId, CodigoMtrack, ViajeId, SyncedAt)
    SELECT NEWID(), 9000000000 + ROW_NUMBER() OVER (ORDER BY x.vin), CASE j.estado WHEN 'cancelado' THEN N'CANCELLED' WHEN 'programado' THEN N'SCHEDULED' ELSE N'LOADING' END,
           DATEADD(HOUR, j.desdeHoras, @ahora), CONCAT(N'SEED-ORD-', RIGHT(x.vin, 4)), CONCAT(N'SEED-SAP-', RIGHT(x.vin, 4)), N'SEED CEDI Envigado', x.vin, N'RENAULT', N'SANDERO',
           N'SEED-CONC-01', N'Bogota', N'Direccion de prueba 123', DATEADD(HOUR, j.desdeHoras, @ahora), DATEADD(HOUR, j.desdeHoras + 4, @ahora), N'Muelle 1', 1100.5, v.id, j.codigo, j.id, @ahora
    FROM #vv x JOIN #vj j ON j.codigo = x.codigo JOIN #v v ON v.vin = x.vin WHERE x.vin LIKE 'SEEDS%';

    ALTER TABLE db_IA_AUTOS.tblInspecciones ENABLE TRIGGER ALL;
    ALTER TABLE db_IA_AUTOS.tblEvidencias ENABLE TRIGGER ALL;
    ALTER TABLE db_IA_AUTOS.tblTransferenciasCustodia ENABLE TRIGGER ALL;
    ALTER TABLE db_IA_AUTOS.tblNovedadesProceso ENABLE TRIGGER ALL;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH

-- Resultado (esperado: 2, 12, 4, 6, 4, 1, 1, 1, 1, 1, 1, 6 y 1).
SELECT
    (SELECT COUNT(*) FROM db_IA_AUTOS.tblClientes WHERE Codigo IN ('sofasa', 'seed_prueba')) AS Clientes,
    (SELECT COUNT(*) FROM db_IA_AUTOS.tblUsuarios WHERE Email LIKE '%@seed.iaautos.test') AS Usuarios,
    (SELECT COUNT(*) FROM db_IA_AUTOS.tblVehiculos WHERE Vin LIKE 'SEEDP%') AS VehiculosDePrueba,
    (SELECT COUNT(*) FROM db_IA_AUTOS.tblViajes WHERE CodigoMtrack LIKE 'SEED-%') AS Viajes,
    (SELECT COUNT(DISTINCT Estado) FROM db_IA_AUTOS.tblViajes WHERE CodigoMtrack LIKE 'SEED-%') AS EstadosDeViaje,
    (SELECT COUNT(*) FROM db_IA_AUTOS.tblViajeVehiculos vv JOIN db_IA_AUTOS.tblViajes v ON v.Id = vv.ViajeId JOIN db_IA_AUTOS.tblVehiculos h ON h.Id = vv.VehiculoId
      WHERE v.CodigoMtrack = 'SEED-MTK-0002' AND h.ClienteId <> (SELECT Id FROM db_IA_AUTOS.tblClientes WHERE Codigo = 'sofasa')) AS UnidadDelOtroClienteEnElViajeMixto,
    (SELECT COUNT(*) FROM db_IA_AUTOS.tblInspecciones WHERE Version = 2 AND Estado = 'cerrada' AND ViajeId IN (SELECT Id FROM db_IA_AUTOS.tblViajes WHERE CodigoMtrack = 'SEED-MTK-0006')) AS InspeccionCorregida,
    (SELECT COUNT(*) FROM db_IA_AUTOS.tblTransferenciasCustodia WHERE TipoCierre IS NULL AND FirmaEntrega IS NOT NULL AND InspeccionId IN
      (SELECT Id FROM db_IA_AUTOS.tblInspecciones WHERE ViajeId IN (SELECT Id FROM db_IA_AUTOS.tblViajes WHERE CodigoMtrack = 'SEED-MTK-0002'))) AS TransferenciaAbierta,
    (SELECT COUNT(*) FROM db_IA_AUTOS.tblUsuarios WHERE Email LIKE '%@seed.iaautos.test' AND DebeCambiarPin = 1) AS ConductorConPinPendiente,
    (SELECT COUNT(*) FROM db_IA_AUTOS.tblUsuarios WHERE Email LIKE '%@seed.iaautos.test' AND BloqueadoHasta > SYSDATETIMEOFFSET()) AS ConductorBloqueado,
    (SELECT COUNT(*) FROM db_IA_AUTOS.tblEvidencias WHERE StoragePath LIKE 'seed/%' AND SelloLat = 0 AND SelloLng = 0) / 2 AS ParesDeFotosSinUbicacion,
    (SELECT COUNT(*) FROM db_IA_AUTOS.tblOrdenesSofasa WHERE CodigoMtrack LIKE 'SEED-MTK-%') AS OrdenesVinculadas,
    (SELECT COUNT(*) FROM db_IA_AUTOS.tblNovedadesProceso WHERE MotivoCodigo = 'inspector_no_llego' AND ViajeId IN (SELECT Id FROM db_IA_AUTOS.tblViajes WHERE CodigoMtrack = 'SEED-MTK-0005')) AS CargueSinApoyo;
