/* =============================================================================
   IA Autos - SEMILLA DE DESARROLLO, parte 3: hallazgos, seguimiento, avisos y auditoria (S6-HU-01.12, IA-AUTOS#194)

   Se ejecuta DESPUES de 01-semilla-base.sql y 02-semilla-ampliacion.sql. Agrega, sobre las inspecciones ya sembradas:
     - HALLAZGOS con lado + numero de zona (izquierdo y derecho, y las zonas centrales: frente, capo y techo; una llanta), de los cuatro niveles (v1, v1_plus, v2, v3), con la pareja izquierdo 7 / derecho 7 de la
       misma clase (para probar la propuesta de PREEXISTENTE), preexistentes confirmados y nuevos;
     - un hallazgo en cada estado de SEGUIMIENTO: validado (v2, con la foto del sello de quien valida), rechazado con evidencia completa (con foto y transferencia con_reservas), rechazado con evidencia incompleta
       (transferencia abierta), recuperado (v1_plus) y derogado (v3), cada cambio con su fila en el historial;
     - una unidad que NO SALE con reemplazo (SEED-MTK-0007: SEEDS000000000010 por un v1_plus ya recuperado, reemplazada por SEEDS000000000003) y ese mismo VIN en un VIAJE POSTERIOR (SEED-MTK-0008);
     - NOVEDADES DEL CONDUCTOR sin revisar (2) y revisada (1);
     - AVISOS de cada tipo y de las dos urgencias, leidos y sin leer, y una con respuesta del coordinador;
     - NOTAS ACLARATORIAS (de una inspeccion, un hallazgo y una transferencia) y registros de AUDITORIA con su detalle;
     - los PARAMETROS de las alertas ya vienen con sus valores del ddl (no se cambian aqui; se comprueba que existan).

   Mismas reglas que las partes 1 y 2: prefijos, se borra con 00-limpiar-semilla.sql, SOLO desarrollo y pruebas, no llega a QA ni a produccion. Los textos son inventados.
   Como ejecutarlo: igual que las anteriores (esquema db_IA_AUTOS; en una base con ddl.sql, reemplace db_IA_AUTOS. por dbo.), en UNA sola ejecucion de script. Si ya esta cargada, se detiene.
   ============================================================================= */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;

IF NOT EXISTS (SELECT 1 FROM db_IA_AUTOS.tblViajes WHERE CodigoMtrack = 'SEED-MTK-0006')
    THROW 50015, N'Falta la parte 2: ejecute antes 02-semilla-ampliacion.sql.', 1;
IF EXISTS (SELECT 1 FROM db_IA_AUTOS.tblViajes WHERE CodigoMtrack = 'SEED-MTK-0007')
    THROW 50016, N'La parte 3 ya esta cargada: ejecute primero 00-limpiar-semilla.sql y vuelva a empezar por la parte 1.', 1;
IF (SELECT COUNT(*) FROM db_IA_AUTOS.tblParametros WHERE Clave IN ('niveles_criticos', 'umbral_patron_cantidad', 'umbral_patron_ventana_dias', 'captura_incompleta_minutos', 'transferencia_pendiente_minutos')) < 5
    THROW 50017, N'Faltan los parametros de las alertas: ejecute antes el script de cambios 17-parametros-de-alertas.sql.', 1;

DECLARE @ahora datetimeoffset = SYSDATETIMEOFFSET();
DECLARE @firma nvarchar(max) = N'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==';
DECLARE @sofasa uniqueidentifier = (SELECT Id FROM db_IA_AUTOS.tblClientes WHERE Codigo = 'sofasa');
DECLARE @ninera uniqueidentifier = (SELECT Id FROM db_IA_AUTOS.tblTiposUnidadTransportadora WHERE Codigo = 'ninera');
DECLARE @cedi uniqueidentifier = (SELECT Id FROM db_IA_AUTOS.tblLugares WHERE Nombre = 'SEED CEDI Envigado');
DECLARE @norte uniqueidentifier = (SELECT Id FROM db_IA_AUTOS.tblLugares WHERE Nombre = 'SEED Concesionario Norte');

BEGIN TRY
    BEGIN TRANSACTION;

    ALTER TABLE db_IA_AUTOS.tblInspecciones DISABLE TRIGGER ALL;
    ALTER TABLE db_IA_AUTOS.tblEvidencias DISABLE TRIGGER ALL;
    ALTER TABLE db_IA_AUTOS.tblTransferenciasCustodia DISABLE TRIGGER ALL;
    ALTER TABLE db_IA_AUTOS.tblHallazgos DISABLE TRIGGER ALL;
    ALTER TABLE db_IA_AUTOS.tblHallazgoSeguimiento DISABLE TRIGGER ALL;
    ALTER TABLE db_IA_AUTOS.tblNotasAclaratorias DISABLE TRIGGER ALL;
    ALTER TABLE db_IA_AUTOS.tblNotificaciones DISABLE TRIGGER ALL;
    ALTER TABLE db_IA_AUTOS.tblAuditLog DISABLE TRIGGER ALL;
    ALTER TABLE db_IA_AUTOS.tblEtapas DISABLE TRIGGER ALL;
    ALTER TABLE db_IA_AUTOS.tblViajeVehiculos DISABLE TRIGGER ALL;

    -- Lo ya sembrado, por sus claves naturales.
    CREATE TABLE #u (k varchar(20) COLLATE DATABASE_DEFAULT PRIMARY KEY, id uniqueidentifier NOT NULL);
    INSERT #u (k, id) SELECT SUBSTRING(Email, 6, CHARINDEX('@', Email) - 6), Id FROM db_IA_AUTOS.tblUsuarios WHERE Email LIKE '%@seed.iaautos.test';
    CREATE TABLE #i (id uniqueidentifier NOT NULL, vin varchar(17) COLLATE DATABASE_DEFAULT NOT NULL, ec varchar(20) COLLATE DATABASE_DEFAULT NOT NULL, momento varchar(10) COLLATE DATABASE_DEFAULT NOT NULL,
                     version int NOT NULL, viajeId uniqueidentifier NOT NULL, etapaId uniqueidentifier NOT NULL, vehiculoId uniqueidentifier NOT NULL, PRIMARY KEY (vin, ec, momento, version));
    INSERT #i SELECT i.Id, v.Vin, c.Codigo, i.Momento, i.Version, i.ViajeId, e.Id, v.Id
    FROM db_IA_AUTOS.tblInspecciones i JOIN db_IA_AUTOS.tblEtapas e ON e.Id = i.EtapaId JOIN db_IA_AUTOS.tblEtapasCatalogo c ON c.Id = e.EtapaCatalogoId JOIN db_IA_AUTOS.tblVehiculos v ON v.Id = e.VehiculoId
    WHERE v.Vin LIKE 'SEED%';

    -- ------------------------------------------------------------------------------------------ el viaje del «no sale» (0007) y el posterior (0008)
    CREATE TABLE #vj (codigo varchar(20) COLLATE DATABASE_DEFAULT PRIMARY KEY, id uniqueidentifier NOT NULL DEFAULT NEWID(), estado varchar(20) COLLATE DATABASE_DEFAULT NOT NULL, placa nvarchar(20) COLLATE DATABASE_DEFAULT NOT NULL, desdeHoras int NOT NULL);
    INSERT #vj (codigo, estado, placa, desdeHoras) VALUES ('SEED-MTK-0007', 'en_curso', N'SED007', -16), ('SEED-MTK-0008', 'programado', N'SED008', 48);
    INSERT db_IA_AUTOS.tblViajes (Id, CodigoMtrack, TipoUnidadId, Placa, Trailer, ConductorId, InspectorId, OrigenId, DestinoId, FechaProgramada, LugarCargueId, Capacidad, Estado, CreadoPor, CreatedAt, UpdatedAt)
    SELECT j.id, j.codigo, @ninera, j.placa, N'SEEDT' + RIGHT(j.codigo, 2), (SELECT id FROM #u WHERE k = 'sof_conductor'), (SELECT id FROM #u WHERE k = 'sof_inspector'),
           (SELECT TipoId FROM db_IA_AUTOS.tblLugares WHERE Id = @cedi), (SELECT TipoId FROM db_IA_AUTOS.tblLugares WHERE Id = @norte), DATEADD(HOUR, j.desdeHoras, @ahora), @cedi, 7, j.estado,
           (SELECT id FROM #u WHERE k = 'sof_coord'), DATEADD(HOUR, j.desdeHoras - 6, @ahora), @ahora
    FROM #vj j;
    DECLARE @v10 uniqueidentifier = (SELECT Id FROM db_IA_AUTOS.tblVehiculos WHERE Vin = 'SEEDS000000000010');
    DECLARE @v03 uniqueidentifier = (SELECT Id FROM db_IA_AUTOS.tblVehiculos WHERE Vin = 'SEEDS000000000003');
    DECLARE @j7 uniqueidentifier = (SELECT id FROM #vj WHERE codigo = 'SEED-MTK-0007'), @j8 uniqueidentifier = (SELECT id FROM #vj WHERE codigo = 'SEED-MTK-0008');
    -- (la fila de SEEDS000000000010 en el viaje 0007 se completa con los datos del «no sale» mas abajo, cuando existe su hallazgo)
    INSERT db_IA_AUTOS.tblViajeVehiculos (Id, ViajeId, VehiculoId, LugarDestinoId, Posicion, Completa)
    VALUES (NEWID(), @j7, @v10, @norte, 1, 0), (NEWID(), @j7, @v03, @norte, 2, 0), (NEWID(), @j8, @v10, @norte, 1, 0);
    CREATE TABLE #e (vehiculoId uniqueidentifier NOT NULL, viajeId uniqueidentifier NOT NULL, ec varchar(20) COLLATE DATABASE_DEFAULT NOT NULL, orden int NOT NULL, id uniqueidentifier NOT NULL DEFAULT NEWID());
    INSERT #e (vehiculoId, viajeId, ec, orden) SELECT x.v, x.j, c.Codigo, c.Orden FROM (VALUES (@v10, @j7), (@v03, @j7), (@v10, @j8)) AS x (v, j) CROSS JOIN db_IA_AUTOS.tblEtapasCatalogo c;
    INSERT db_IA_AUTOS.tblEtapas (Id, VehiculoId, EtapaCatalogoId, PuntoCustodiaId, ViajeId, Orden)
    SELECT e.id, e.vehiculoId, c.Id, CASE e.ec WHEN 'cargue' THEN (SELECT TipoId FROM db_IA_AUTOS.tblLugares WHERE Id = @cedi) WHEN 'descargue' THEN (SELECT TipoId FROM db_IA_AUTOS.tblLugares WHERE Id = @norte) END, e.viajeId, e.orden
    FROM #e e JOIN db_IA_AUTOS.tblEtapasCatalogo c ON c.Codigo = e.ec;
    -- La unidad que no sale tiene su Cargue cerrado (salida y llegada con sus 9 vistas y la transferencia firmada): de ahi sale su hallazgo v1_plus.
    DECLARE @insSalida10 uniqueidentifier = NEWID(), @insLlegada10 uniqueidentifier = NEWID();
    DECLARE @e10 uniqueidentifier = (SELECT id FROM #e WHERE vehiculoId = @v10 AND viajeId = @j7 AND ec = 'cargue');
    DECLARE @ini7 datetimeoffset = DATEADD(HOUR, -16, @ahora);
    INSERT db_IA_AUTOS.tblInspecciones (Id, EtapaId, ViajeId, UsuarioId, Momento, Fecha, Lat, Lng, Estado, ConductorNombre, Placa, Trailer, IniciadaAt, CerradaAt, Version, ConfirmacionSitio)
    SELECT x.id, @e10, @j7, (SELECT id FROM #u WHERE k = 'sof_inspector'), x.momento, DATEADD(MINUTE, x.min, @ini7), 6.17, -75.58, 'cerrada', N'Conductor Semilla (Sofasa)', N'SED007', N'SEEDT07',
           DATEADD(MINUTE, x.min, @ini7), DATEADD(MINUTE, x.min + 25, @ini7), 1, 'sin_confirmacion_posible'
    FROM (VALUES (@insSalida10, 'salida', 0), (@insLlegada10, 'llegada', 90)) AS x (id, momento, min);
    INSERT db_IA_AUTOS.tblEvidencias (Id, InspeccionId, StoragePath, SelloFecha, SelloLat, SelloLng, VistaId, HashSha256, Precision, DispositivoId, Nota)
    SELECT NEWID(), x.id, CONCAT('seed/SEEDS000000000010/cargue-', x.momento, '-', f.Codigo, '.jpg'), DATEADD(MINUTE, 5 + f.Orden, @ini7), 6.17, -75.58, f.Id,
           LOWER(CONVERT(char(64), HASHBYTES('SHA2_256', CONCAT('SEEDS000000000010', x.momento, f.Codigo)), 2)), 8.5, N'seed-celular-03',
           CASE f.Codigo WHEN 'piezas_baul' THEN N'Baul sin novedad' WHEN 'cojineria' THEN N'Cojineria sin novedad' END
    FROM (VALUES (@insSalida10, 'salida'), (@insLlegada10, 'llegada')) AS x (id, momento) CROSS JOIN db_IA_AUTOS.tblVistasFoto f WHERE f.Activo = 1;
    INSERT db_IA_AUTOS.tblTransferenciasCustodia (Id, InspeccionId, EntregaUsuarioId, RecibeUsuarioId, FirmaEntrega, FirmaRecibe, FirmadaAt, FirmaEntregaAt, FirmaEntregaIp, FirmaRecibeAt, FirmaRecibeIp, TipoCierre)
    VALUES (NEWID(), @insLlegada10, (SELECT id FROM #u WHERE k = 'sof_inspector'), (SELECT id FROM #u WHERE k = 'sof_conductor'), @firma, @firma, DATEADD(MINUTE, 120, @ini7),
            DATEADD(MINUTE, 116, @ini7), '10.20.30.40', DATEADD(MINUTE, 118, @ini7), '10.20.30.41', 'firmada');
    INSERT #i SELECT @insSalida10, 'SEEDS000000000010', 'cargue', 'salida', 1, @j7, @e10, @v10 UNION ALL SELECT @insLlegada10, 'SEEDS000000000010', 'cargue', 'llegada', 1, @j7, @e10, @v10;

    -- ------------------------------------------------------------------------------------------ hallazgos
    -- origen: inspector (con zona, clase, tamano y nivel) o conductor (novedad). revisada: la novedad ya la reviso un inspector. foto: el hallazgo tiene su foto propia. preex: clave del hallazgo del que es preexistente.
    CREATE TABLE #h (k varchar(30) COLLATE DATABASE_DEFAULT PRIMARY KEY, id uniqueidentifier NOT NULL DEFAULT NEWID(), vin varchar(17) COLLATE DATABASE_DEFAULT NOT NULL, ec varchar(20) COLLATE DATABASE_DEFAULT NOT NULL,
                     momento varchar(10) COLLATE DATABASE_DEFAULT NOT NULL, version int NOT NULL, zonaNum int NULL, zonaLado varchar(10) COLLATE DATABASE_DEFAULT NULL, clase varchar(30) COLLATE DATABASE_DEFAULT NULL,
                     tamano varchar(30) COLLATE DATABASE_DEFAULT NOT NULL, nivel varchar(30) COLLATE DATABASE_DEFAULT NULL, origen varchar(10) COLLATE DATABASE_DEFAULT NOT NULL, tipoNovedad varchar(20) COLLATE DATABASE_DEFAULT NULL,
                     revisada bit NOT NULL, preex varchar(30) COLLATE DATABASE_DEFAULT NULL, estado varchar(15) COLLATE DATABASE_DEFAULT NOT NULL, evidCompleta bit NOT NULL, foto bit NOT NULL, nota nvarchar(500) NULL);
    INSERT #h (k, vin, ec, momento, version, zonaNum, zonaLado, clase, tamano, nivel, origen, tipoNovedad, revisada, preex, estado, evidCompleta, foto, nota) VALUES
        -- La pareja izquierdo 7 / derecho 7 de la misma clase (raya de pintura): en la salida, un hallazgo nuevo; en la llegada, el mismo ya confirmado como preexistente y su par del otro lado, nuevo.
        ('izq7_salida', 'SEEDS000000000001', 'cargue', 'salida', 1, 7, 'izquierdo', 'raya_pintar', '1_5cm', 'v2', 'inspector', NULL, 0, NULL, 'pendiente', 0, 1, N'Raya de pintura en la puerta delantera izquierda'),
        ('izq7_llegada', 'SEEDS000000000001', 'cargue', 'llegada', 1, 7, 'izquierdo', 'raya_pintar', '1_5cm', 'v2', 'inspector', NULL, 0, 'izq7_salida', 'pendiente', 0, 1, N'La misma raya de la salida: preexistente confirmado'),
        ('der7_llegada', 'SEEDS000000000001', 'cargue', 'llegada', 1, 7, 'derecho', 'raya_pintar', '1_5cm', 'v2', 'inspector', NULL, 0, NULL, 'pendiente', 0, 1, N'Raya nueva del lado derecho (no es preexistente de la izquierda)'),
        -- Las zonas centrales y una llanta, de los cuatro niveles.
        ('frente_v1plus', 'SEEDS000000000001', 'descargue', 'salida', 1, 50, NULL, 'deformacion', '10_20cm', 'v1_plus', 'inspector', NULL, 0, NULL, 'pendiente', 0, 1, N'Deformacion en el centro del parachoques delantero'),
        ('llanta_v1', 'SEEDS000000000001', 'descargue', 'salida', 1, 6, 'izquierdo', 'golpe', '5_10cm', 'v1', 'inspector', NULL, 0, NULL, 'pendiente', 0, 1, N'Golpe en la llanta delantera izquierda'),
        ('techo_v3', 'SEEDS000000000001', 'descargue', 'llegada', 1, 55, NULL, 'mancha', '5_10cm', 'v3', 'inspector', NULL, 0, NULL, 'derogado', 0, 1, N'Mancha en el techo: se acepta el defecto'),
        -- Un hallazgo en cada estado de seguimiento.
        ('validado_v2', 'SEEDS000000000002', 'cargue', 'llegada', 1, 24, 'derecho', 'golpe', '5_10cm', 'v2', 'inspector', NULL, 0, NULL, 'validado', 0, 1, N'Golpe en el panel lateral trasero derecho'),
        ('rechazado_completo', 'SEEDS000000000002', 'cargue', 'llegada', 1, 4, 'izquierdo', 'golpe', '10_20cm', 'v1', 'inspector', NULL, 0, NULL, 'rechazado', 1, 1, N'Golpe en el guardafango delantero izquierdo'),
        ('recuperado_v1plus', 'SEEDS000000000004', 'cargue', 'llegada', 1, 30, 'izquierdo', 'deformacion', 'mas_20cm', 'v1_plus', 'inspector', NULL, 0, NULL, 'recuperado', 0, 1, N'Deformacion en el capo, ya reparada'),
        ('rechazado_incompleto', 'SEEDS000000000005', 'cargue', 'llegada', 1, 8, 'derecho', 'golpe', '5_10cm', 'v1', 'inspector', NULL, 0, NULL, 'rechazado', 0, 1, N'Golpe en el pilar A derecho: la transferencia sigue abierta'),
        -- El v1_plus de la unidad que no sale (ya recuperado: por eso vuelve a entrar al viaje posterior).
        ('no_sale_v1plus', 'SEEDS000000000010', 'cargue', 'llegada', 1, 53, NULL, 'deformacion', 'mas_20cm', 'v1_plus', 'inspector', NULL, 0, NULL, 'recuperado', 0, 1, N'Deformacion estructural en el capo'),
        -- Novedades del conductor: dos sin revisar (en la salida de Transporte que sigue en borrador) y una ya revisada por el inspector.
        ('novedad_sin_revisar_1', 'SEEDS000000000004', 'transporte', 'salida', 1, NULL, NULL, NULL, '5_10cm', NULL, 'conductor', 'golpe', 0, NULL, 'pendiente', 0, 0, NULL),
        ('novedad_sin_revisar_2', 'SEEDS000000000004', 'transporte', 'salida', 1, NULL, NULL, NULL, '1_5cm', NULL, 'conductor', 'granizo', 0, NULL, 'pendiente', 0, 0, NULL),
        ('novedad_revisada', 'SEEDS000000000001', 'transporte', 'salida', 1, 54, NULL, 'desconchado', '1_5cm', 'v2', 'conductor', 'piedra', 1, NULL, 'pendiente', 0, 0, N'Revisada: impacto de piedra en el parabrisas');

    INSERT db_IA_AUTOS.tblHallazgos (Id, InspeccionId, ParteVehiculo, ClaseDano, TamanoDano, ZonaId, TipoNovedad, NivelSeveridad, Origen, Nota, EsPreexistente, HallazgoOrigenId, ConfirmadoPor, EstadoSeguimiento,
                                     EvidenciaCompleta, RevisadoPor, RevisadoAt)
    SELECT h.id, i.id, NULL, h.clase, h.tamano, z.Id, h.tipoNovedad, h.nivel, h.origen, h.nota, CASE WHEN h.preex IS NOT NULL THEN 1 ELSE 0 END, (SELECT p.id FROM #h p WHERE p.k = h.preex),
           CASE WHEN h.preex IS NOT NULL THEN (SELECT id FROM #u WHERE k = 'sof_inspector') END, h.estado, h.evidCompleta,
           CASE WHEN h.revisada = 1 THEN (SELECT id FROM #u WHERE k = 'sof_inspector') END, CASE WHEN h.revisada = 1 THEN DATEADD(HOUR, -2, @ahora) END
    FROM #h h JOIN #i i ON i.vin = h.vin AND i.ec = h.ec AND i.momento = h.momento AND i.version = h.version
    LEFT JOIN db_IA_AUTOS.tblZonasDano z ON z.Numero = h.zonaNum AND ISNULL(z.Lado, '') = ISNULL(h.zonaLado, '');

    -- La foto propia de cada hallazgo (la novedad del conductor no la lleva).
    INSERT db_IA_AUTOS.tblEvidencias (Id, InspeccionId, HallazgoId, StoragePath, SelloFecha, SelloLat, SelloLng, HashSha256, Precision, DispositivoId, Nota)
    SELECT NEWID(), i.id, h.id, CONCAT('seed/', h.vin, '/hallazgo-', h.k, '.jpg'), DATEADD(MINUTE, -30, @ahora), 6.17, -75.58, LOWER(CONVERT(char(64), HASHBYTES('SHA2_256', CONCAT('hallazgo', h.k)), 2)), 7.5, N'seed-celular-02',
           N'Foto del hallazgo'
    FROM #h h JOIN #i i ON i.vin = h.vin AND i.ec = h.ec AND i.momento = h.momento AND i.version = h.version WHERE h.foto = 1;
    -- La foto del sello de quien valida (Sofasa) en el v2 validado.
    DECLARE @sello uniqueidentifier = NEWID();
    INSERT db_IA_AUTOS.tblEvidencias (Id, InspeccionId, StoragePath, SelloFecha, SelloLat, SelloLng, HashSha256, DispositivoId, Nota, EsSello)
    SELECT @sello, i.id, 'seed/SEEDS000000000002/sello-validacion-sofasa.jpg', DATEADD(HOUR, -3, @ahora), 6.17, -75.58, LOWER(CONVERT(char(64), HASHBYTES('SHA2_256', 'sello-validacion'), 2)), N'seed-celular-02',
           N'Sello de quien valida por Sofasa', 1
    FROM #i i WHERE i.vin = 'SEEDS000000000002' AND i.ec = 'cargue' AND i.momento = 'llegada';

    -- El historial de seguimiento: cada cambio con usuario, fecha y nota; el v2 validado con la foto del sello.
    CREATE TABLE #s (hk varchar(30) COLLATE DATABASE_DEFAULT NOT NULL, accion varchar(15) COLLATE DATABASE_DEFAULT NOT NULL, antes varchar(15) COLLATE DATABASE_DEFAULT NULL, nuevo varchar(15) COLLATE DATABASE_DEFAULT NULL,
                     usuario varchar(20) COLLATE DATABASE_DEFAULT NOT NULL, nota nvarchar(500) NULL, conSello bit NOT NULL, horas int NOT NULL);
    INSERT #s (hk, accion, antes, nuevo, usuario, nota, conSello, horas) VALUES
        ('validado_v2', 'seguimiento', 'pendiente', 'validado', 'sof_coord', N'Sofasa valido el v2 con la foto del sello', 1, -3),
        ('rechazado_completo', 'seguimiento', 'pendiente', 'rechazado', 'sof_coord', N'Se rechaza el vehiculo: v1 con evidencia completa (foto y transferencia con reservas)', 0, -5),
        ('rechazado_incompleto', 'seguimiento', 'pendiente', 'rechazado', 'sof_coord', N'Se rechaza el vehiculo: la evidencia esta incompleta, la transferencia sigue abierta', 0, -4),
        ('recuperado_v1plus', 'seguimiento', 'pendiente', 'recuperado', 'sof_coord', N'V1+ recuperado antes del despacho', 0, -6),
        ('techo_v3', 'seguimiento', 'pendiente', 'derogado', 'sof_coord', N'V3: se acepta el defecto', 0, -7),
        ('no_sale_v1plus', 'no_sale', NULL, NULL, 'sof_inspector', N'Deformacion estructural: la unidad no sale del viaje, la reemplaza SEEDS000000000003', 0, -14),
        ('no_sale_v1plus', 'seguimiento', 'pendiente', 'recuperado', 'sof_coord', N'Reparada en taller: V1+ recuperado, puede volver a un viaje', 0, -1),
        ('novedad_revisada', 'revision', NULL, NULL, 'sof_inspector', N'El inspector reviso la novedad del conductor: zona, clase y nivel asignados', 0, -2);
    INSERT db_IA_AUTOS.tblHallazgoSeguimiento (Id, HallazgoId, Accion, EstadoAnterior, EstadoNuevo, UsuarioId, Nota, EvidenciaSelloId, Detalle, CreatedAt, UpdatedAt)
    SELECT NEWID(), h.id, s.accion, s.antes, s.nuevo, u.id, s.nota, CASE WHEN s.conSello = 1 THEN @sello END,
           CONCAT('{"hecho":"', s.accion, '","hallazgo":"', h.k, '"}'), DATEADD(HOUR, s.horas, @ahora), DATEADD(HOUR, s.horas, @ahora)
    FROM #s s JOIN #h h ON h.k = s.hk JOIN #u u ON u.k = s.usuario;

    -- La unidad que no sale: datos del «no sale» en su fila del viaje 0007 (con el reemplazo).
    UPDATE db_IA_AUTOS.tblViajeVehiculos SET EstadoUnidad = 'no_sale', NoSaleMotivo = N'Deformacion estructural en el capo (v1_plus)', NoSaleHallazgoId = (SELECT id FROM #h WHERE k = 'no_sale_v1plus'),
           NoSaleMarcadoPor = (SELECT id FROM #u WHERE k = 'sof_inspector'), NoSaleAt = DATEADD(HOUR, -14, @ahora), ReemplazoVehiculoId = @v03
    WHERE ViajeId = @j7 AND VehiculoId = @v10;

    -- ------------------------------------------------------------------------------------------ avisos (bandeja) de cada tipo y urgencia
    CREATE TABLE #n (k varchar(40) COLLATE DATABASE_DEFAULT PRIMARY KEY, tipo varchar(30) COLLATE DATABASE_DEFAULT NOT NULL, urgencia varchar(5) COLLATE DATABASE_DEFAULT NOT NULL, usuario varchar(20) COLLATE DATABASE_DEFAULT NULL,
                     canal varchar(10) COLLATE DATABASE_DEFAULT NOT NULL, titulo nvarchar(200) NOT NULL, mensaje nvarchar(1000) NOT NULL, leida bit NOT NULL, vin varchar(17) COLLATE DATABASE_DEFAULT NULL,
                     hk varchar(30) COLLATE DATABASE_DEFAULT NULL, datos nvarchar(max) NULL);
    INSERT #n (k, tipo, urgencia, usuario, canal, titulo, mensaje, leida, vin, hk, datos) VALUES
        ('novedad_conductor', 'novedad_conductor', 'alta', 'sof_coord', 'bandeja', N'Novedad del conductor: golpe en SEEDS000000000004', N'El conductor reporto un golpe durante el recorrido.', 0, 'SEEDS000000000004', 'novedad_sin_revisar_1', NULL),
        ('respuesta_conductor', 'respuesta_conductor', 'alta', 'sof_conductor', 'bandeja', N'Respuesta a su novedad', N'El coordinador respondio su novedad por llamada.', 1, 'SEEDS000000000004', 'novedad_sin_revisar_1', NULL),
        ('hallazgo_critico', 'hallazgo_critico', 'alta', 'sof_coord', 'bandeja', N'Hallazgo critico (v1_plus) en SEEDS000000000001', N'Deformacion en el centro del parachoques delantero.', 0, 'SEEDS000000000001', 'frente_v1plus', NULL),
        ('hallazgo_critico_admin', 'hallazgo_critico', 'alta', 'sof_admin', 'bandeja', N'Hallazgo critico (v1_plus) en SEEDS000000000001', N'Deformacion en el centro del parachoques delantero.', 1, 'SEEDS000000000001', 'frente_v1plus', NULL),
        ('umbral_patron', 'umbral_patron', 'media', 'sof_coord', 'bandeja', N'Umbral de hallazgos superado: izquierdo 7', N'Se superaron 10 hallazgos en 30 dias en izquierdo 7.', 0, NULL, NULL, N'{"tipo_umbral":"zona","cuadrante_numero":7,"lado":"izquierdo","cantidad":10,"ventana_dias":30}'),
        ('captura_incompleta', 'captura_incompleta', 'media', 'sof_inspector', 'bandeja', N'Captura incompleta', N'Una inspeccion de Cargue lleva mas de 30 minutos sin cerrarse.', 1, 'SEEDS000000000004', NULL, NULL),
        ('transferencia_pendiente', 'transferencia_pendiente', 'media', 'sof_conductor', 'bandeja', N'Transferencia pendiente de firma', N'Falta su firma en la transferencia del Cargue de SEEDS000000000005.', 0, 'SEEDS000000000005', NULL, NULL),
        ('novedad_proceso', 'novedad_proceso', 'alta', 'sof_coord', 'bandeja', N'Cargue sin inspector: SEEDS000000000008', N'El conductor hizo el cargue sin inspector. Motivo: el inspector no llego.', 0, 'SEEDS000000000008', NULL, NULL),
        ('reclamo_sin_actividad', 'reclamo_sin_actividad', 'media', 'sof_inspector', 'bandeja', N'Reclamo sin actividad', N'Reclamo el cargue de un viaje y lleva tiempo sin actividad.', 1, NULL, NULL, NULL),
        ('fuera_de_rango', 'fuera_de_rango', 'media', 'sof_coord', 'bandeja', N'Inspeccion fuera de rango', N'La inspeccion se hizo lejos del lugar de cargue.', 0, 'SEEDS000000000009', NULL, NULL),
        ('cargue_sin_reclamar', 'cargue_sin_reclamar', 'media', 'sof_inspector', 'bandeja', N'Cargue sin reclamar', N'Hay un cargue programado de hoy que nadie ha reclamado.', 0, NULL, NULL, NULL),
        ('propuesta_ubicacion', 'propuesta_ubicacion', 'media', 'sof_coord', 'bandeja', N'Propuesta de ubicacion', N'Hay una propuesta de ubicacion pendiente para SEED Concesionario Sur.', 0, NULL, NULL, NULL),
        ('permiso_sin_inspector', 'permiso_sin_inspector', 'alta', 'sof_coord', 'bandeja', N'Permiso de la excepcion sin inspector', N'El conductor pidio el permiso para hacer el cargue sin inspector.', 1, 'SEEDS000000000008', NULL, N'{"hecho":"solicitud"}'),
        ('reporte_diario', 'reporte_diario', 'media', 'sof_coord', 'correo', N'Reporte diario', N'Resumen del dia de inspecciones.', 1, NULL, NULL, NULL),
        ('novedad_conductor_push', 'novedad_conductor', 'alta', 'sof_coord', 'push', N'Novedad del conductor: golpe en SEEDS000000000004', N'Aviso enviado al celular (registro del envio).', 0, 'SEEDS000000000004', 'novedad_sin_revisar_1', NULL);
    INSERT db_IA_AUTOS.tblNotificaciones (Id, Tipo, Urgencia, UsuarioId, ClienteId, ViajeId, VehiculoId, HallazgoId, InspeccionId, Titulo, Mensaje, Canal, EnviadaAt, LeidaAt, ClaveDedupe, CreatedAt, Datos)
    SELECT NEWID(), n.tipo, n.urgencia, u.id, @sofasa, i.viajeId, i.vehiculoId, h.id, NULL, n.titulo, n.mensaje, n.canal, DATEADD(MINUTE, -45, @ahora), CASE WHEN n.leida = 1 THEN DATEADD(MINUTE, -20, @ahora) END,
           CONCAT('seed:', n.k), DATEADD(MINUTE, -45, @ahora), n.datos
    FROM #n n LEFT JOIN #u u ON u.k = n.usuario
    LEFT JOIN (SELECT DISTINCT vin, viajeId, vehiculoId FROM #i) i ON i.vin = n.vin
    LEFT JOIN #h h ON h.k = n.hk;
    -- Una con respuesta del coordinador (la novedad del conductor): quien responde, el medio y cuando.
    UPDATE db_IA_AUTOS.tblNotificaciones SET RespuestaTexto = N'Recibido: llame al conductor, siga el recorrido y reporte al llegar.', RespuestaMedio = 'llamada',
           RespondidaPor = (SELECT id FROM #u WHERE k = 'sof_coord'), RespondidaAt = DATEADD(MINUTE, -30, @ahora), LeidaAt = DATEADD(MINUTE, -35, @ahora)
    WHERE ClaveDedupe = 'seed:novedad_conductor';

    -- ------------------------------------------------------------------------------------------ notas aclaratorias
    INSERT db_IA_AUTOS.tblNotasAclaratorias (Id, Entidad, EntidadId, AutorId, Texto, CreatedAt)
    SELECT NEWID(), x.entidad, x.entidadId, (SELECT id FROM #u WHERE k = x.autor), x.texto, DATEADD(HOUR, x.horas, @ahora)
    FROM (VALUES
        ('inspeccion', (SELECT id FROM #i WHERE vin = 'SEEDS000000000001' AND ec = 'cargue' AND momento = 'llegada' AND version = 1), 'sof_inspector', N'Se aclara que la raya de la izquierda ya estaba al recibir el vehiculo', -8),
        ('hallazgo', (SELECT id FROM #h WHERE k = 'rechazado_completo'), 'sof_coord', N'Se confirma con Sofasa que el golpe es de origen y no del traslado', -6),
        ('transferencia', (SELECT t.Id FROM db_IA_AUTOS.tblTransferenciasCustodia t WHERE t.InspeccionId = (SELECT id FROM #i WHERE vin = 'SEEDS000000000002' AND ec = 'cargue' AND momento = 'llegada' AND version = 1)), 'sof_conductor',
         N'Firmo con reservas por el rayon de la puerta trasera derecha', -9)) AS x (entidad, entidadId, autor, texto, horas);

    -- ------------------------------------------------------------------------------------------ auditoria con detalle
    INSERT db_IA_AUTOS.tblAuditLog (Id, UsuarioId, Accion, Entidad, EntidadId, CreatedAt, UpdatedAt, Ip, Dispositivo, Detalle, ViajeId, ClienteId)
    SELECT NEWID(), u.id, a.accion, a.entidad, a.entidadId, DATEADD(HOUR, a.horas, @ahora), DATEADD(HOUR, a.horas, @ahora), '10.20.30.40', N'seed-celular-02', a.detalle, a.viajeId, @sofasa
    FROM (VALUES
        ('sof_inspector', N'inspeccion.cerrar', N'inspecciones', (SELECT id FROM #i WHERE vin = 'SEEDS000000000001' AND ec = 'cargue' AND momento = 'llegada' AND version = 1), -10,
         N'{"antes":{"estado":"borrador"},"despues":{"estado":"cerrada","fotos":9}}', (SELECT viajeId FROM #i WHERE vin = 'SEEDS000000000001' AND ec = 'cargue' AND momento = 'llegada' AND version = 1)),
        ('sof_inspector', N'hallazgo.crear', N'hallazgos', (SELECT id FROM #h WHERE k = 'frente_v1plus'), -9,
         N'{"antes":null,"despues":{"zona":"50","clase":"deformacion","nivel":"v1_plus"}}', (SELECT viajeId FROM #i WHERE vin = 'SEEDS000000000001' AND ec = 'descargue' AND momento = 'salida')),
        ('sof_coord', N'hallazgo.seguimiento', N'hallazgos', (SELECT id FROM #h WHERE k = 'rechazado_completo'), -5,
         N'{"antes":{"estado":"pendiente"},"despues":{"estado":"rechazado","evidencia_completa":true}}', (SELECT viajeId FROM #i WHERE vin = 'SEEDS000000000002' AND ec = 'cargue' AND momento = 'llegada')),
        ('sof_coord', N'hallazgo.seguimiento', N'hallazgos', (SELECT id FROM #h WHERE k = 'validado_v2'), -3,
         N'{"antes":{"estado":"pendiente"},"despues":{"estado":"validado"},"foto_sello":true}', (SELECT viajeId FROM #i WHERE vin = 'SEEDS000000000002' AND ec = 'cargue' AND momento = 'llegada')),
        ('sof_inspector', N'viaje.no_sale', N'viajes', @j7, -14,
         N'{"antes":{"estado_unidad":"activa"},"despues":{"estado_unidad":"no_sale","reemplazo":"SEEDS000000000003"}}', @j7),
        ('sof_inspector', N'novedad.revisar', N'hallazgos', (SELECT id FROM #h WHERE k = 'novedad_revisada'), -2,
         N'{"antes":{"zona":null,"clase":null,"nivel":null},"despues":{"zona":"54","clase":"desconchado","nivel":"v2"}}', (SELECT viajeId FROM #i WHERE vin = 'SEEDS000000000001' AND ec = 'transporte' AND momento = 'salida')),
        ('sof_conductor', N'novedad.crear', N'hallazgos', (SELECT id FROM #h WHERE k = 'novedad_sin_revisar_1'), -1,
         N'{"antes":null,"despues":{"tipo_novedad":"golpe","tamano":"5_10cm"}}', (SELECT viajeId FROM #i WHERE vin = 'SEEDS000000000004' AND ec = 'transporte' AND momento = 'salida')),
        ('sof_coord', N'nota.crear', N'notas_aclaratorias', (SELECT id FROM #i WHERE vin = 'SEEDS000000000001' AND ec = 'cargue' AND momento = 'llegada' AND version = 1), -8,
         N'{"antes":null,"despues":{"entidad":"hallazgo"}}', NULL)) AS a (usuario, accion, entidad, entidadId, horas, detalle, viajeId)
    JOIN #u u ON u.k = a.usuario;

    ALTER TABLE db_IA_AUTOS.tblInspecciones ENABLE TRIGGER ALL;
    ALTER TABLE db_IA_AUTOS.tblEvidencias ENABLE TRIGGER ALL;
    ALTER TABLE db_IA_AUTOS.tblTransferenciasCustodia ENABLE TRIGGER ALL;
    ALTER TABLE db_IA_AUTOS.tblHallazgos ENABLE TRIGGER ALL;
    ALTER TABLE db_IA_AUTOS.tblHallazgoSeguimiento ENABLE TRIGGER ALL;
    ALTER TABLE db_IA_AUTOS.tblNotasAclaratorias ENABLE TRIGGER ALL;
    ALTER TABLE db_IA_AUTOS.tblNotificaciones ENABLE TRIGGER ALL;
    ALTER TABLE db_IA_AUTOS.tblAuditLog ENABLE TRIGGER ALL;
    ALTER TABLE db_IA_AUTOS.tblEtapas ENABLE TRIGGER ALL;
    ALTER TABLE db_IA_AUTOS.tblViajeVehiculos ENABLE TRIGGER ALL;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH

-- Resultado (esperado: 4, 5, 1, 1, 1, 1, 1, 1, 2, 15, 2, 7, 1, 3 y 8).
SELECT
    (SELECT COUNT(DISTINCT h.NivelSeveridad) FROM db_IA_AUTOS.tblHallazgos h JOIN db_IA_AUTOS.tblInspecciones i ON i.Id = h.InspeccionId WHERE i.ViajeId IN (SELECT Id FROM db_IA_AUTOS.tblViajes WHERE CodigoMtrack LIKE 'SEED-%')) AS Niveles,
    (SELECT COUNT(DISTINCT h.EstadoSeguimiento) FROM db_IA_AUTOS.tblHallazgos h JOIN db_IA_AUTOS.tblInspecciones i ON i.Id = h.InspeccionId WHERE i.ViajeId IN (SELECT Id FROM db_IA_AUTOS.tblViajes WHERE CodigoMtrack LIKE 'SEED-%')) AS EstadosDeSeguimiento,
    (SELECT COUNT(*) FROM db_IA_AUTOS.tblHallazgos WHERE EsPreexistente = 1 AND ConfirmadoPor IS NOT NULL AND HallazgoOrigenId IS NOT NULL AND InspeccionId IN (SELECT Id FROM db_IA_AUTOS.tblInspecciones WHERE ViajeId IN (SELECT Id FROM db_IA_AUTOS.tblViajes WHERE CodigoMtrack = 'SEED-MTK-0001'))) AS PreexistenteConfirmado,
    (SELECT COUNT(*) FROM db_IA_AUTOS.tblHallazgos WHERE EstadoSeguimiento = 'rechazado' AND EvidenciaCompleta = 1) AS RechazadoConEvidenciaCompleta,
    (SELECT COUNT(*) FROM db_IA_AUTOS.tblHallazgos WHERE EstadoSeguimiento = 'rechazado' AND EvidenciaCompleta = 0) AS RechazadoConEvidenciaIncompleta,
    (SELECT COUNT(*) FROM db_IA_AUTOS.tblHallazgoSeguimiento WHERE EvidenciaSelloId IS NOT NULL) AS ValidadoConSello,
    (SELECT COUNT(*) FROM db_IA_AUTOS.tblViajeVehiculos vv JOIN db_IA_AUTOS.tblVehiculos v ON v.Id = vv.VehiculoId WHERE v.Vin = 'SEEDS000000000010' AND vv.EstadoUnidad = 'no_sale' AND vv.ReemplazoVehiculoId IS NOT NULL) AS UnidadNoSaleConReemplazo,
    (SELECT COUNT(*) FROM db_IA_AUTOS.tblViajeVehiculos vv JOIN db_IA_AUTOS.tblViajes j ON j.Id = vv.ViajeId JOIN db_IA_AUTOS.tblVehiculos v ON v.Id = vv.VehiculoId WHERE v.Vin = 'SEEDS000000000010' AND j.CodigoMtrack = 'SEED-MTK-0008') AS MismoVinEnViajePosterior,
    (SELECT COUNT(*) FROM db_IA_AUTOS.tblHallazgos WHERE Origen = 'conductor' AND RevisadoAt IS NULL) AS NovedadesSinRevisar,
    (SELECT COUNT(*) FROM db_IA_AUTOS.tblNotificaciones WHERE ClaveDedupe LIKE 'seed:%') AS Avisos,
    (SELECT COUNT(DISTINCT Urgencia) FROM db_IA_AUTOS.tblNotificaciones WHERE ClaveDedupe LIKE 'seed:%') AS Urgencias,
    (SELECT COUNT(*) FROM db_IA_AUTOS.tblNotificaciones WHERE ClaveDedupe LIKE 'seed:%' AND LeidaAt IS NOT NULL) AS AvisosLeidos,
    (SELECT COUNT(*) FROM db_IA_AUTOS.tblNotificaciones WHERE ClaveDedupe LIKE 'seed:%' AND RespondidaAt IS NOT NULL) AS AvisosConRespuesta,
    (SELECT COUNT(*) FROM db_IA_AUTOS.tblNotasAclaratorias WHERE AutorId IN (SELECT Id FROM db_IA_AUTOS.tblUsuarios WHERE Email LIKE '%@seed.iaautos.test')) AS Notas,
    (SELECT COUNT(*) FROM db_IA_AUTOS.tblAuditLog WHERE Dispositivo = N'seed-celular-02') AS RegistrosDeAuditoria;
