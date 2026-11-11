/* =============================================================================
   IA Autos - SEMILLA DE DESARROLLO, parte 1: la base (S5-HU-31.5, IA-AUTOS#123)

   Para que frontend vea y pruebe inventario, inspecciones y transferencias SIN la app movil. Siembra, para el cliente sofasa: sus 5 usuarios (admin, coordinador, comercial, inspector y conductor), 4 lugares, 10
   vehiculos y UN viaje FINALIZADO (SEED-MTK-0001) de 3 unidades con sus 3 etapas (Cargue, Transporte y Descargue), cada una con su inspeccion de salida y de llegada CERRADAS (18), las fotos de las vistas y 9
   transferencias: 7 firmadas, 1 con reservas y 1 con constancia de no firma (con su testigo y la foto de la constancia).

   Todo lleva un prefijo para poder borrarlo con 00-limpiar-semilla.sql: VIN SEED..., correo @seed.iaautos.test, codigo de viaje SEED-..., lugares SEED ... Nada de esto son datos reales (ni nombres, ni cedulas,
   ni placas) y NO llega a QA ni a produccion.

   Acceso de prueba (solo desarrollo): admin, coordinador y comercial entran con su correo y la clave  Seed-Dev-2026!  ; inspector y conductor, con su cedula y el PIN  135790  .
   Cedulas (inventadas): inspector 90000001, conductor 90000002. Correos: seed.sof_admin@seed.iaautos.test, seed.sof_coord@..., seed.sof_comercial@..., seed.sof_inspector@..., seed.sof_conductor@...
   Las fotos apuntan a rutas seed/... del almacen de archivos: si no existen alli, la foto se ve como no disponible (la fila y su sello si estan).

   Como ejecutarlo: DESPUES de ddl.sql y de los cambios de esquema (hasta el 34 como minimo: necesita las zonas del dibujo), abrir en SSMS o DBeaver CONECTADO a la base de pruebas con un usuario
   propietario y ejecutar completo en UNA sola ejecucion de script. El esquema es db_IA_AUTOS (base compartida de pruebas); en una base creada con ddl.sql es dbo: reemplace db_IA_AUTOS. por dbo.
   en todo el archivo. Una sola transaccion: si algo falla no queda nada. Si ya esta sembrado, se detiene: ejecute antes 00-limpiar-semilla.sql.
   ============================================================================= */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;

IF EXISTS (SELECT 1 FROM db_IA_AUTOS.tblVehiculos WHERE Vin LIKE 'SEED%')
    THROW 50010, N'La semilla ya esta cargada: ejecute primero 00-limpiar-semilla.sql.', 1;
IF NOT EXISTS (SELECT 1 FROM db_IA_AUTOS.tblClientes WHERE Codigo = 'sofasa')
    THROW 50011, N'Falta el cliente sofasa: ejecute antes ddl.sql.', 1;
IF NOT EXISTS (SELECT 1 FROM db_IA_AUTOS.tblZonasDano)
    THROW 50012, N'Faltan las zonas del dibujo de danos: ejecute antes el script de cambios 34-resembrar-dibujo-v6.sql.', 1;

DECLARE @ahora datetimeoffset = SYSDATETIMEOFFSET();
DECLARE @inicio datetimeoffset = DATEADD(DAY, -3, @ahora);
DECLARE @hClave nvarchar(800) = N'pbkdf2-sha256$600000$Xu1e7V7tXu1e7V7tXu1e7Q==$t+ODrhkhUzX4kt+b7Ax6RrUUdsGNzXbs601pZrZEt8k=';
DECLARE @hPin nvarchar(800) = N'pbkdf2-sha256$600000$Xu1e7V7tXu1e7V7tXu1e7Q==$dWhspOHqQtGGI981TQoUIGTGw4pCIbbQC+aZtp6fQYw=';
DECLARE @firma nvarchar(max) = N'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==';
DECLARE @sofasa uniqueidentifier = (SELECT Id FROM db_IA_AUTOS.tblClientes WHERE Codigo = 'sofasa');
DECLARE @ninera uniqueidentifier = (SELECT Id FROM db_IA_AUTOS.tblTiposUnidadTransportadora WHERE Codigo = 'ninera');

BEGIN TRY
    BEGIN TRANSACTION;

    -- Los triggers de la evidencia inmutable impiden insertar una inspeccion ya cerrada: se desactivan SOLO durante esta transaccion (el rollback tambien los deja como estaban).
    ALTER TABLE db_IA_AUTOS.tblInspecciones DISABLE TRIGGER ALL;
    ALTER TABLE db_IA_AUTOS.tblEvidencias DISABLE TRIGGER ALL;
    ALTER TABLE db_IA_AUTOS.tblTransferenciasCustodia DISABLE TRIGGER ALL;

    -- ------------------------------------------------------------------------------------------ usuarios de sofasa
    CREATE TABLE #u (k varchar(20) COLLATE DATABASE_DEFAULT PRIMARY KEY, id uniqueidentifier NOT NULL DEFAULT NEWID(), nombre nvarchar(100) COLLATE DATABASE_DEFAULT NOT NULL, rol varchar(30) COLLATE DATABASE_DEFAULT NOT NULL, cedula varchar(20) COLLATE DATABASE_DEFAULT NULL);
    INSERT #u (k, nombre, rol, cedula) VALUES
        ('sof_admin', N'Administrador Semilla (Sofasa)', 'admin', NULL),
        ('sof_coord', N'Coordinadora Semilla (Sofasa)', 'coordinador_calidad', NULL),
        ('sof_comercial', N'Comercial Semilla (Sofasa)', 'comercial', NULL),
        ('sof_inspector', N'Inspector Semilla (Sofasa)', 'inspector', '90000001'),
        ('sof_conductor', N'Conductor Semilla (Sofasa)', 'conductor', '90000002');
    INSERT db_IA_AUTOS.tblUsuarios (Id, Nombre, Email, Rol, SubtipoInspector, Activo, PasswordHash, TipoCredencial, Cedula, PinHash, ClienteId)
    SELECT id, nombre, 'seed.' + k + '@seed.iaautos.test', rol, CASE WHEN rol = 'inspector' THEN 'nacional' END, 1,
           CASE WHEN cedula IS NULL THEN @hClave END, CASE WHEN cedula IS NULL THEN 'password' ELSE 'pin' END, cedula, CASE WHEN cedula IS NOT NULL THEN @hPin END, @sofasa
    FROM #u;

    -- ------------------------------------------------------------------------------------------ lugares
    CREATE TABLE #l (k varchar(20) COLLATE DATABASE_DEFAULT PRIMARY KEY, id uniqueidentifier NOT NULL DEFAULT NEWID(), nombre nvarchar(100) COLLATE DATABASE_DEFAULT NOT NULL, tipo varchar(30) COLLATE DATABASE_DEFAULT NOT NULL, ciudad nvarchar(100) COLLATE DATABASE_DEFAULT NOT NULL, lat float NOT NULL, lng float NOT NULL);
    INSERT #l (k, nombre, tipo, ciudad, lat, lng) VALUES
        ('cedi', N'SEED CEDI Envigado', 'cedi_1', N'Envigado', 6.17, -75.58),
        ('norte', N'SEED Concesionario Norte', 'concesionario', N'Bogota', 4.75, -74.05),
        ('sur', N'SEED Concesionario Sur', 'concesionario', N'Cali', 3.40, -76.52),
        ('cert', N'SEED Certificadora', 'certificadora', N'Medellin', 6.25, -75.57);
    INSERT db_IA_AUTOS.tblLugares (Id, Nombre, TipoId, Ciudad, Latitud, Longitud, RadioMetros, Orden, Activo, EstadoUbicacion, UbicacionFijadaPor, UbicacionFijadaAt)
    SELECT l.id, l.nombre, p.Id, l.ciudad, l.lat, l.lng, 300, 900, 1, 'confirmada', (SELECT id FROM #u WHERE k = 'sof_coord'), @ahora
    FROM #l l JOIN db_IA_AUTOS.tblPuntosCustodia p ON p.Codigo = l.tipo;

    -- ------------------------------------------------------------------------------------------ vehiculos (VIN de 17 caracteres, SEEDS + 12 digitos)
    CREATE TABLE #v (n int PRIMARY KEY, id uniqueidentifier NOT NULL DEFAULT NEWID(), vin varchar(17) COLLATE DATABASE_DEFAULT NOT NULL);
    INSERT #v (n, vin) SELECT n, 'SEEDS' + RIGHT('000000000000' + CAST(n AS varchar(12)), 12) FROM (VALUES (1), (2), (3), (4), (5), (6), (7), (8), (9), (10)) AS t (n);
    INSERT db_IA_AUTOS.tblVehiculos (Id, Vin, Marca, Modelo, Color, Operacion, ClienteId, Activo)
    SELECT id, vin, N'Renault', CASE WHEN n % 2 = 0 THEN N'Sandero' ELSE N'Duster' END, CASE n % 3 WHEN 0 THEN N'Blanco' WHEN 1 THEN N'Gris' ELSE N'Rojo' END, 'nacional', @sofasa, 1 FROM #v;

    -- ------------------------------------------------------------------------------------------ el viaje finalizado: SEED-MTK-0001 con las unidades 1, 2 y 3
    DECLARE @viaje uniqueidentifier = NEWID();
    INSERT db_IA_AUTOS.tblViajes (Id, CodigoMtrack, TipoUnidadId, Placa, Trailer, ConductorId, InspectorId, OrigenId, DestinoId, FechaProgramada, LugarCargueId, Capacidad, Estado, CreadoPor, CreatedAt, UpdatedAt)
    SELECT @viaje, 'SEED-MTK-0001', @ninera, N'SED001', N'SEEDT01', (SELECT id FROM #u WHERE k = 'sof_conductor'), (SELECT id FROM #u WHERE k = 'sof_inspector'),
           (SELECT TipoId FROM db_IA_AUTOS.tblLugares WHERE Id = (SELECT id FROM #l WHERE k = 'cedi')), (SELECT TipoId FROM db_IA_AUTOS.tblLugares WHERE Id = (SELECT id FROM #l WHERE k = 'norte')),
           @inicio, (SELECT id FROM #l WHERE k = 'cedi'), 7, 'finalizado', (SELECT id FROM #u WHERE k = 'sof_coord'), @inicio, @ahora;
    INSERT db_IA_AUTOS.tblViajeVehiculos (Id, ViajeId, VehiculoId, LugarDestinoId, Posicion, Completa)
    SELECT NEWID(), @viaje, v.id, (SELECT id FROM #l WHERE k = 'norte'), v.n, 1 FROM #v v WHERE v.n <= 3;

    -- Las 3 etapas de cada unidad (Cargue, Transporte, Descargue).
    CREATE TABLE #e (vin varchar(17) COLLATE DATABASE_DEFAULT NOT NULL, ec varchar(20) COLLATE DATABASE_DEFAULT NOT NULL, orden int NOT NULL, id uniqueidentifier NOT NULL DEFAULT NEWID(), vehiculoId uniqueidentifier NOT NULL, PRIMARY KEY (vin, ec));
    INSERT #e (vin, ec, orden, vehiculoId) SELECT v.vin, c.Codigo, c.Orden, v.id FROM #v v CROSS JOIN db_IA_AUTOS.tblEtapasCatalogo c WHERE v.n <= 3;
    INSERT db_IA_AUTOS.tblEtapas (Id, VehiculoId, EtapaCatalogoId, PuntoCustodiaId, ViajeId, Orden)
    SELECT e.id, e.vehiculoId, c.Id, CASE e.ec WHEN 'cargue' THEN (SELECT TipoId FROM db_IA_AUTOS.tblLugares WHERE Id = (SELECT id FROM #l WHERE k = 'cedi'))
                                                WHEN 'descargue' THEN (SELECT TipoId FROM db_IA_AUTOS.tblLugares WHERE Id = (SELECT id FROM #l WHERE k = 'norte')) END, @viaje, e.orden
    FROM #e e JOIN db_IA_AUTOS.tblEtapasCatalogo c ON c.Codigo = e.ec;

    -- Las 18 inspecciones (salida y llegada de cada etapa), todas CERRADAS y en orden de tiempo: el Cargue y el Descargue las hace el inspector; el Transporte, el conductor.
    CREATE TABLE #i (vin varchar(17) COLLATE DATABASE_DEFAULT NOT NULL, ec varchar(20) COLLATE DATABASE_DEFAULT NOT NULL, momento varchar(10) COLLATE DATABASE_DEFAULT NOT NULL, id uniqueidentifier NOT NULL DEFAULT NEWID(), etapaId uniqueidentifier NOT NULL, usuarioId uniqueidentifier NOT NULL,
                     inicio datetimeoffset NOT NULL, PRIMARY KEY (vin, ec, momento));
    INSERT #i (vin, ec, momento, etapaId, usuarioId, inicio)
    SELECT e.vin, e.ec, m.momento, e.id, (SELECT id FROM #u WHERE k = CASE WHEN e.ec = 'transporte' THEN 'sof_conductor' ELSE 'sof_inspector' END),
           DATEADD(MINUTE, ((e.orden - 1) * 2 + m.idx) * 90, @inicio)
    FROM #e e CROSS JOIN (VALUES ('salida', 0), ('llegada', 1)) AS m (momento, idx);
    INSERT db_IA_AUTOS.tblInspecciones (Id, EtapaId, ViajeId, UsuarioId, Momento, Fecha, Lat, Lng, Estado, ConductorNombre, Placa, Trailer, IniciadaAt, CerradaAt, Version, ConfirmacionSitio)
    SELECT id, etapaId, @viaje, usuarioId, momento, inicio, 6.17, -75.58, 'cerrada', N'Conductor Semilla (Sofasa)', N'SED001', N'SEEDT01', inicio, DATEADD(MINUTE, 25, inicio), 1, 'sin_confirmacion_posible' FROM #i;

    -- Las fotos: el Cargue y el Descargue llevan las 9 vistas (las 7 obligatorias y las dos con nota, baul y cojineria); el Transporte, sin vistas.
    INSERT db_IA_AUTOS.tblEvidencias (Id, InspeccionId, StoragePath, SelloFecha, SelloLat, SelloLng, VistaId, HashSha256, Precision, DispositivoId, Nota)
    SELECT NEWID(), i.id, CONCAT('seed/', i.vin, '/', i.ec, '-', i.momento, '-', f.Codigo, '.jpg'), DATEADD(MINUTE, 5 + f.Orden, i.inicio), 6.17, -75.58, f.Id,
           LOWER(CONVERT(char(64), HASHBYTES('SHA2_256', CONCAT(i.vin, i.ec, i.momento, f.Codigo)), 2)), 8.5, N'seed-celular-01',
           CASE f.Codigo WHEN 'piezas_baul' THEN N'Baul sin novedad: gato, llanta de repuesto y triangulo completos' WHEN 'cojineria' THEN N'Cojineria sin manchas ni rasgaduras' END
    FROM #i i CROSS JOIN db_IA_AUTOS.tblVistasFoto f
    WHERE i.ec <> 'transporte' AND f.Activo = 1;

    -- Las transferencias de custodia de cada llegada cerrada: 7 firmadas, la del Cargue de la unidad 2 con reservas y la del Cargue de la unidad 3 con constancia de no firma.
    DECLARE @constancia uniqueidentifier = NEWID();
    DECLARE @insNoFirma uniqueidentifier = (SELECT id FROM #i WHERE vin = 'SEEDS000000000003' AND ec = 'cargue' AND momento = 'llegada');
    INSERT db_IA_AUTOS.tblEvidencias (Id, InspeccionId, StoragePath, SelloFecha, SelloLat, SelloLng, HashSha256, DispositivoId, Nota, EsConstancia)
    SELECT @constancia, @insNoFirma, 'seed/SEEDS000000000003/constancia-no-firma.jpg', DATEADD(MINUTE, 20, i.inicio), 6.17, -75.58, LOWER(CONVERT(char(64), HASHBYTES('SHA2_256', 'constancia-no-firma'), 2)),
           N'seed-celular-01', N'Foto de la constancia: quien recibe se nego a firmar', 1
    FROM #i i WHERE i.id = @insNoFirma;
    INSERT db_IA_AUTOS.tblTransferenciasCustodia (Id, InspeccionId, EntregaUsuarioId, RecibeUsuarioId, FirmaEntrega, FirmaRecibe, FirmadaAt, FirmaEntregaAt, FirmaEntregaIp, FirmaRecibeAt, FirmaRecibeIp,
                                                  TipoCierre, ReservasTexto, NoFirmaParte, MotivoNoFirma, TestigoUsuarioId, EvidenciaConstanciaId)
    SELECT NEWID(), i.id,
           (SELECT id FROM #u WHERE k = CASE i.ec WHEN 'cargue' THEN 'sof_inspector' ELSE 'sof_conductor' END),
           (SELECT id FROM #u WHERE k = CASE i.ec WHEN 'cargue' THEN 'sof_conductor' ELSE 'sof_inspector' END),
           x.fe, x.fr, DATEADD(MINUTE, 30, i.inicio), CASE WHEN x.fe IS NOT NULL THEN DATEADD(MINUTE, 26, i.inicio) END, CASE WHEN x.fe IS NOT NULL THEN '10.20.30.40' END,
           CASE WHEN x.fr IS NOT NULL THEN DATEADD(MINUTE, 28, i.inicio) END, CASE WHEN x.fr IS NOT NULL THEN '10.20.30.41' END,
           x.tipo, x.reservas, x.parte, x.motivo, x.testigo, x.constancia
    FROM #i i
    CROSS APPLY (SELECT CASE WHEN i.vin = 'SEEDS000000000003' AND i.ec = 'cargue' THEN CAST(NULL AS nvarchar(max)) ELSE @firma END AS fe,
                        CASE WHEN i.vin = 'SEEDS000000000003' AND i.ec = 'cargue' THEN CAST(NULL AS nvarchar(max)) ELSE @firma END AS fr,
                        CASE WHEN i.vin = 'SEEDS000000000003' AND i.ec = 'cargue' THEN 'no_firma' WHEN i.vin = 'SEEDS000000000002' AND i.ec = 'cargue' THEN 'con_reservas' ELSE 'firmada' END AS tipo,
                        CASE WHEN i.vin = 'SEEDS000000000002' AND i.ec = 'cargue' THEN N'Un rayon leve en la puerta trasera derecha que no estaba al entregar' END AS reservas,
                        CASE WHEN i.vin = 'SEEDS000000000003' AND i.ec = 'cargue' THEN 'recibe' END AS parte,
                        CASE WHEN i.vin = 'SEEDS000000000003' AND i.ec = 'cargue' THEN N'La persona que recibe se nego a firmar' END AS motivo,
                        CASE WHEN i.vin = 'SEEDS000000000003' AND i.ec = 'cargue' THEN (SELECT id FROM #u WHERE k = 'sof_coord') END AS testigo,
                        CASE WHEN i.vin = 'SEEDS000000000003' AND i.ec = 'cargue' THEN @constancia END AS constancia) x
    WHERE i.momento = 'llegada';

    ALTER TABLE db_IA_AUTOS.tblInspecciones ENABLE TRIGGER ALL;
    ALTER TABLE db_IA_AUTOS.tblEvidencias ENABLE TRIGGER ALL;
    ALTER TABLE db_IA_AUTOS.tblTransferenciasCustodia ENABLE TRIGGER ALL;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH

-- Resultado (esperado: 5, 4, 10, 1, 3, 9, 18, 108, 9, 1, 1, 7).
SELECT
    (SELECT COUNT(*) FROM db_IA_AUTOS.tblUsuarios WHERE Email LIKE '%@seed.iaautos.test') AS Usuarios,
    (SELECT COUNT(*) FROM db_IA_AUTOS.tblLugares WHERE Nombre LIKE 'SEED %') AS Lugares,
    (SELECT COUNT(*) FROM db_IA_AUTOS.tblVehiculos WHERE Vin LIKE 'SEED%') AS Vehiculos,
    (SELECT COUNT(*) FROM db_IA_AUTOS.tblViajes WHERE CodigoMtrack = 'SEED-MTK-0001' AND Estado = 'finalizado') AS ViajesFinalizados,
    (SELECT COUNT(*) FROM db_IA_AUTOS.tblViajeVehiculos vv JOIN db_IA_AUTOS.tblViajes v ON v.Id = vv.ViajeId WHERE v.CodigoMtrack = 'SEED-MTK-0001') AS UnidadesDelViaje,
    (SELECT COUNT(*) FROM db_IA_AUTOS.tblEtapas e JOIN db_IA_AUTOS.tblViajes v ON v.Id = e.ViajeId WHERE v.CodigoMtrack = 'SEED-MTK-0001') AS Etapas,
    (SELECT COUNT(*) FROM db_IA_AUTOS.tblInspecciones i JOIN db_IA_AUTOS.tblViajes v ON v.Id = i.ViajeId WHERE v.CodigoMtrack = 'SEED-MTK-0001' AND i.Estado = 'cerrada') AS InspeccionesCerradas,
    (SELECT COUNT(*) FROM db_IA_AUTOS.tblEvidencias e JOIN db_IA_AUTOS.tblInspecciones i ON i.Id = e.InspeccionId JOIN db_IA_AUTOS.tblViajes v ON v.Id = i.ViajeId WHERE v.CodigoMtrack = 'SEED-MTK-0001' AND e.VistaId IS NOT NULL) AS FotosDeVistas,
    (SELECT COUNT(*) FROM db_IA_AUTOS.tblTransferenciasCustodia t JOIN db_IA_AUTOS.tblInspecciones i ON i.Id = t.InspeccionId JOIN db_IA_AUTOS.tblViajes v ON v.Id = i.ViajeId WHERE v.CodigoMtrack = 'SEED-MTK-0001') AS Transferencias,
    (SELECT COUNT(*) FROM db_IA_AUTOS.tblTransferenciasCustodia WHERE TipoCierre = 'con_reservas' AND InspeccionId IN (SELECT Id FROM db_IA_AUTOS.tblInspecciones WHERE ViajeId IN (SELECT Id FROM db_IA_AUTOS.tblViajes WHERE CodigoMtrack = 'SEED-MTK-0001'))) AS ConReservas,
    (SELECT COUNT(*) FROM db_IA_AUTOS.tblTransferenciasCustodia WHERE TipoCierre = 'no_firma' AND TestigoUsuarioId IS NOT NULL AND EvidenciaConstanciaId IS NOT NULL AND InspeccionId IN (SELECT Id FROM db_IA_AUTOS.tblInspecciones WHERE ViajeId IN (SELECT Id FROM db_IA_AUTOS.tblViajes WHERE CodigoMtrack = 'SEED-MTK-0001'))) AS NoFirmaConTestigo,
    (SELECT COUNT(*) FROM db_IA_AUTOS.tblTransferenciasCustodia WHERE TipoCierre = 'firmada' AND InspeccionId IN (SELECT Id FROM db_IA_AUTOS.tblInspecciones WHERE ViajeId IN (SELECT Id FROM db_IA_AUTOS.tblViajes WHERE CodigoMtrack = 'SEED-MTK-0001'))) AS Firmadas;
