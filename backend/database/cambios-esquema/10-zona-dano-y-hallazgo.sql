/* =============================================================================
   Zona del dano y hallazgo con nivel, origen y seguimiento, para una base de IA Autos YA EXISTENTE (con 09-evidencia-sin-borrado.sql
   aplicado). Se ejecuta UNA vez (si ya esta aplicado, no hace nada) y todo ocurre en UNA transaccion: si algo falla se revierte completo.

   Que hace:
     1. Zonas del dano: tblZonasDano guarda cada zona que se puede atribuir a un hallazgo. Una zona numerada es un cuadrante mas su lado
        (izquierdo o derecho del 1 al 25; sin lado del 26 en adelante), y se crea una fila por cada cuadrante existente y lado. Las cinco zonas
        provisionales (frente y una por llanta) no tienen numero, lado ni dibujo. Un trigger crea las zonas de cada cuadrante nuevo.
     2. Paneles y regiones: tblPanelesDano (los 5 paneles de la ficha, con su imagen de fondo aun sin cargar) y tblRegionesDano (el poligono de
        cada zona numerada en cada panel). Los poligonos ya cargados en tblCuadranteVistas pasan al panel que corresponde:
          superior -> superior; lateral_izquierda -> costado_izquierdo; lateral_derecha -> costado_derecho;
          diagonal_trasera_izquierda -> perfil_posterior_izquierdo; diagonal_delantera_derecha -> perfil_posterior_derecho.
        El lado de cada region sale del panel (costados y perfiles); del 26 en adelante no lleva lado. Un poligono del panel superior con numero del
        1 al 25 no se puede asignar a un lado por si solo (hay que partirlo en dos segun la mitad de la imagen) y queda sin migrar, listado en el
        reporte final para revision manual. tblCuadranteVistas no se toca: queda como respaldo.
     3. tblHallazgos: gana ZonaId, TipoNovedad, NivelSeveridad, Origen, Nota, EsPreexistente, HallazgoOrigenId, ConfirmadoPor, EstadoSeguimiento,
        EvidenciaCompleta, RevisadoPor, RevisadoAt y MigracionPendiente; ParteVehiculo y ClaseDano pasan a admitir nulos (ParteVehiculo y
        CoordsDiagrama solo quedan en los hallazgos historicos).
     4. Hallazgos historicos: se ubican por el texto de ParteVehiculo cuando la equivalencia es segura (parachoque delantero y fachada
        delantera -> frente; cada llanta -> su zona provisional). Los demas quedan con zona nula y MigracionPendiente = 1, y salen en el
        reporte final. No se pierde ni cambia ningun dato: ParteVehiculo y CoordsDiagrama se conservan.
     5. tblViajeVehiculos: gana EstadoUnidad (activa o no_sale), NoSaleMotivo, NoSaleHallazgoId, NoSaleMarcadoPor, NoSaleAt y ReemplazoVehiculoId.
        Todas las unidades quedan activas.

   Como ejecutarlo: abrir en SSMS, DBeaver o sqlcmd CONECTADO a la base de IA Autos, ajustar @esquema y ejecutar completo con un usuario con
   permiso para alterar tablas y crear triggers (no el usuario de la aplicacion). Ejecutarlo ANTES de desplegar la API.
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
IF OBJECT_ID(@q + N'.tblHallazgos', N'U') IS NULL OR OBJECT_ID(@q + N'.tblCuadrantes', N'U') IS NULL OR OBJECT_ID(@q + N'.tblCuadranteVistas', N'U') IS NULL
   OR OBJECT_ID(@q + N'.tblVistasFoto', N'U') IS NULL OR OBJECT_ID(@q + N'.tblViajeVehiculos', N'U') IS NULL OR OBJECT_ID(@q + N'.tblNivelesSeveridad', N'U') IS NULL
   OR OBJECT_ID(@q + N'.tblUsuarios', N'U') IS NULL OR OBJECT_ID(@q + N'.tblVehiculos', N'U') IS NULL
    THROW 50001, N'Faltan tablas de IA Autos en el esquema indicado: revise @esquema.', 1;

IF COL_LENGTH(@q + N'.tblHallazgos', N'ZonaId') IS NOT NULL
BEGIN
    PRINT N'La zona del dano y el hallazgo con nivel ya estan aplicados: no se hace nada.';
    RETURN;
END

IF COL_LENGTH(@q + N'.tblVehiculos', N'Activo') IS NULL
    THROW 50002, N'Falta tblVehiculos.Activo: ejecute antes 09-evidencia-sin-borrado.sql.', 1;

-- Pasos en orden. {S} = esquema entre corchetes; ~ = comilla simple dentro del texto.
DECLARE @paso TABLE (n int IDENTITY(1, 1) PRIMARY KEY, descripcion nvarchar(100), s nvarchar(max));

INSERT @paso (descripcion, s) VALUES
(N'tblCuadrantes: indice unico (Id, Numero)', N'
IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N~UQ_tblCuadrantes_Id_Numero~ AND parent_object_id = OBJECT_ID(N~{S}.tblCuadrantes~))
    ALTER TABLE {S}.tblCuadrantes ADD CONSTRAINT UQ_tblCuadrantes_Id_Numero UNIQUE (Id, Numero)'),
(N'tblZonasDano', N'
IF OBJECT_ID(N~{S}.tblZonasDano~, N~U~) IS NULL
BEGIN
    CREATE TABLE {S}.tblZonasDano
    (
        Id            uniqueidentifier  NOT NULL CONSTRAINT PK_tblZonasDano PRIMARY KEY,
        CuadranteId   uniqueidentifier  NULL,
        Numero        int               NULL,
        Lado          varchar(10)       NULL,
        Codigo        varchar(40)       NULL,
        Nombre        nvarchar(100)     NULL,
        Orden         int               NOT NULL CONSTRAINT DF_tblZonasDano_Orden DEFAULT (0),
        Activo        bit               NOT NULL CONSTRAINT DF_tblZonasDano_Activo DEFAULT (1),
        CreatedAt     datetimeoffset(3) NOT NULL CONSTRAINT DF_tblZonasDano_CreatedAt DEFAULT (SYSDATETIMEOFFSET()),
        UpdatedAt     datetimeoffset(3) NOT NULL CONSTRAINT DF_tblZonasDano_UpdatedAt DEFAULT (SYSDATETIMEOFFSET()),
        CONSTRAINT FK_tblZonasDano_Cuadrante FOREIGN KEY (CuadranteId, Numero) REFERENCES {S}.tblCuadrantes (Id, Numero) ON DELETE CASCADE ON UPDATE CASCADE,
        CONSTRAINT UQ_tblZonasDano_Id_Cuadrante UNIQUE (Id, CuadranteId),
        CONSTRAINT CK_tblZonasDano_Lado CHECK (Lado IS NULL OR Lado IN (~izquierdo~, ~derecho~)),
        CONSTRAINT CK_tblZonasDano_Forma CHECK (
            (CuadranteId IS NOT NULL AND Numero IS NOT NULL AND Codigo IS NULL
                AND ((Numero BETWEEN 1 AND 25 AND Lado IS NOT NULL) OR (Numero > 25 AND Lado IS NULL)))
            OR (CuadranteId IS NULL AND Numero IS NULL AND Lado IS NULL AND Codigo IS NOT NULL AND Nombre IS NOT NULL))
    );
    CREATE UNIQUE INDEX UX_tblZonasDano_Cuadrante_Lado ON {S}.tblZonasDano (CuadranteId, Lado) WHERE CuadranteId IS NOT NULL;
    CREATE UNIQUE INDEX UX_tblZonasDano_Codigo ON {S}.tblZonasDano (Codigo) WHERE Codigo IS NOT NULL;
END'),
(N'tblPanelesDano', N'
IF OBJECT_ID(N~{S}.tblPanelesDano~, N~U~) IS NULL
BEGIN
    CREATE TABLE {S}.tblPanelesDano
    (
        Id            uniqueidentifier  NOT NULL CONSTRAINT PK_tblPanelesDano PRIMARY KEY,
        Codigo        varchar(40)       NOT NULL,
        Nombre        nvarchar(100)     NOT NULL,
        ImagenPath    nvarchar(260)     NULL,
        Orden         int               NOT NULL CONSTRAINT DF_tblPanelesDano_Orden DEFAULT (0),
        Activo        bit               NOT NULL CONSTRAINT DF_tblPanelesDano_Activo DEFAULT (1),
        CreatedAt     datetimeoffset(3) NOT NULL CONSTRAINT DF_tblPanelesDano_CreatedAt DEFAULT (SYSDATETIMEOFFSET()),
        UpdatedAt     datetimeoffset(3) NOT NULL CONSTRAINT DF_tblPanelesDano_UpdatedAt DEFAULT (SYSDATETIMEOFFSET()),
        CONSTRAINT UQ_tblPanelesDano_Codigo UNIQUE (Codigo)
    );
END'),
(N'tblRegionesDano', N'
IF OBJECT_ID(N~{S}.tblRegionesDano~, N~U~) IS NULL
BEGIN
    CREATE TABLE {S}.tblRegionesDano
    (
        Id            uniqueidentifier  NOT NULL CONSTRAINT PK_tblRegionesDano PRIMARY KEY,
        PanelId       uniqueidentifier  NOT NULL,
        ZonaId        uniqueidentifier  NOT NULL,
        CuadranteId   uniqueidentifier  NOT NULL,
        Geometria     nvarchar(max)     NOT NULL,
        CreatedAt     datetimeoffset(3) NOT NULL CONSTRAINT DF_tblRegionesDano_CreatedAt DEFAULT (SYSDATETIMEOFFSET()),
        UpdatedAt     datetimeoffset(3) NOT NULL CONSTRAINT DF_tblRegionesDano_UpdatedAt DEFAULT (SYSDATETIMEOFFSET()),
        CONSTRAINT FK_tblRegionesDano_Panel FOREIGN KEY (PanelId) REFERENCES {S}.tblPanelesDano (Id),
        CONSTRAINT FK_tblRegionesDano_Zona FOREIGN KEY (ZonaId, CuadranteId) REFERENCES {S}.tblZonasDano (Id, CuadranteId) ON DELETE CASCADE,
        CONSTRAINT UQ_tblRegionesDano_Panel_Zona UNIQUE (PanelId, ZonaId),
        CONSTRAINT CK_tblRegionesDano_Geometria CHECK (ISJSON(Geometria) = 1)
    );
    CREATE INDEX IX_tblRegionesDano_Zona ON {S}.tblRegionesDano (ZonaId);
END'),
(N'trigger: cada cuadrante nuevo trae sus zonas', N'
CREATE OR ALTER TRIGGER {S}.TR_tblCuadrantes_CrearZonas
ON {S}.tblCuadrantes
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO {S}.tblZonasDano (Id, CuadranteId, Numero, Lado, Orden)
    SELECT NEWID(), i.Id, i.Numero, l.Lado, i.Numero
    FROM inserted i
    CROSS APPLY (SELECT Lado FROM (VALUES (~izquierdo~), (~derecho~)) AS x (Lado) WHERE i.Numero <= 25
                 UNION ALL SELECT NULL WHERE i.Numero > 25) AS l;
END'),
(N'paneles de la ficha y zonas provisionales', N'
INSERT INTO {S}.tblPanelesDano (Id, Codigo, Nombre, Orden)
SELECT NEWID(), v.Codigo, v.Nombre, v.Orden
FROM (VALUES (~superior~, N~Superior~, 1), (~perfil_posterior_izquierdo~, N~Perfil posterior izquierdo~, 2),
             (~perfil_posterior_derecho~, N~Perfil posterior derecho~, 3), (~costado_izquierdo~, N~Costado izquierdo~, 4),
             (~costado_derecho~, N~Costado derecho~, 5)) AS v (Codigo, Nombre, Orden)
WHERE NOT EXISTS (SELECT 1 FROM {S}.tblPanelesDano p WHERE p.Codigo = v.Codigo);
INSERT INTO {S}.tblZonasDano (Id, Codigo, Nombre, Orden)
SELECT NEWID(), v.Codigo, v.Nombre, v.Orden
FROM (VALUES (~frente~, N~Frente~, 101), (~llanta_delantera_izquierda~, N~Llanta delantera izquierda~, 102),
             (~llanta_delantera_derecha~, N~Llanta delantera derecha~, 103), (~llanta_trasera_izquierda~, N~Llanta trasera izquierda~, 104),
             (~llanta_trasera_derecha~, N~Llanta trasera derecha~, 105)) AS v (Codigo, Nombre, Orden)
WHERE NOT EXISTS (SELECT 1 FROM {S}.tblZonasDano z WHERE z.Codigo = v.Codigo)'),
(N'zonas de los cuadrantes que ya existen', N'
INSERT INTO {S}.tblZonasDano (Id, CuadranteId, Numero, Lado, Orden)
SELECT NEWID(), c.Id, c.Numero, l.Lado, c.Numero
FROM {S}.tblCuadrantes c
CROSS APPLY (SELECT Lado FROM (VALUES (~izquierdo~), (~derecho~)) AS x (Lado) WHERE c.Numero <= 25
             UNION ALL SELECT NULL WHERE c.Numero > 25) AS l
WHERE NOT EXISTS (SELECT 1 FROM {S}.tblZonasDano z WHERE z.CuadranteId = c.Id AND ISNULL(z.Lado, ~~) = ISNULL(l.Lado, ~~))'),
(N'regiones: los poligonos de tblCuadranteVistas pasan a su panel', N'
INSERT INTO {S}.tblRegionesDano (Id, PanelId, ZonaId, CuadranteId, Geometria)
SELECT NEWID(), p.Id, z.Id, c.Id, cv.Geometria
FROM {S}.tblCuadranteVistas cv
JOIN {S}.tblVistasFoto v ON v.Id = cv.VistaFotoId
JOIN {S}.tblCuadrantes c ON c.Id = cv.CuadranteId
JOIN (VALUES (~superior~, ~superior~, NULL), (~lateral_izquierda~, ~costado_izquierdo~, ~izquierdo~), (~lateral_derecha~, ~costado_derecho~, ~derecho~),
             (~diagonal_trasera_izquierda~, ~perfil_posterior_izquierdo~, ~izquierdo~), (~diagonal_delantera_derecha~, ~perfil_posterior_derecho~, ~derecho~))
     AS m (Vista, Panel, Lado) ON m.Vista = v.Codigo
JOIN {S}.tblPanelesDano p ON p.Codigo = m.Panel
JOIN {S}.tblZonasDano z ON z.CuadranteId = c.Id AND ((c.Numero > 25 AND z.Lado IS NULL) OR (c.Numero <= 25 AND z.Lado = m.Lado))
WHERE NOT EXISTS (SELECT 1 FROM {S}.tblRegionesDano r WHERE r.PanelId = p.Id AND r.ZonaId = z.Id)'),
(N'tblHallazgos: columnas nuevas', N'
ALTER TABLE {S}.tblHallazgos ALTER COLUMN ParteVehiculo nvarchar(200) NULL;
ALTER TABLE {S}.tblHallazgos ALTER COLUMN ClaseDano varchar(30) NULL;
ALTER TABLE {S}.tblHallazgos ADD
    ZonaId              uniqueidentifier NULL,
    TipoNovedad         varchar(20)      NULL,
    NivelSeveridad      varchar(30)      NULL,
    Origen              varchar(10)      NOT NULL CONSTRAINT DF_tblHallazgos_Origen DEFAULT (~inspector~),
    Nota                nvarchar(500)    NULL,
    EsPreexistente      bit              NOT NULL CONSTRAINT DF_tblHallazgos_EsPreexistente DEFAULT (0),
    HallazgoOrigenId    uniqueidentifier NULL,
    ConfirmadoPor       uniqueidentifier NULL,
    EstadoSeguimiento   varchar(15)      NOT NULL CONSTRAINT DF_tblHallazgos_EstadoSeguimiento DEFAULT (~pendiente~),
    EvidenciaCompleta   bit              NOT NULL CONSTRAINT DF_tblHallazgos_EvidenciaCompleta DEFAULT (0),
    RevisadoPor         uniqueidentifier NULL,
    RevisadoAt          datetimeoffset(3) NULL,
    MigracionPendiente  bit              NOT NULL CONSTRAINT DF_tblHallazgos_MigracionPendiente DEFAULT (0)'),
(N'tblHallazgos: relaciones y restricciones', N'
ALTER TABLE {S}.tblHallazgos ADD CONSTRAINT FK_tblHallazgos_Zona FOREIGN KEY (ZonaId) REFERENCES {S}.tblZonasDano (Id);
ALTER TABLE {S}.tblHallazgos ADD CONSTRAINT FK_tblHallazgos_Nivel FOREIGN KEY (NivelSeveridad) REFERENCES {S}.tblNivelesSeveridad (Codigo);
ALTER TABLE {S}.tblHallazgos ADD CONSTRAINT FK_tblHallazgos_Origen FOREIGN KEY (HallazgoOrigenId) REFERENCES {S}.tblHallazgos (Id);
ALTER TABLE {S}.tblHallazgos ADD CONSTRAINT FK_tblHallazgos_ConfirmadoPor FOREIGN KEY (ConfirmadoPor) REFERENCES {S}.tblUsuarios (Id);
ALTER TABLE {S}.tblHallazgos ADD CONSTRAINT FK_tblHallazgos_RevisadoPor FOREIGN KEY (RevisadoPor) REFERENCES {S}.tblUsuarios (Id);
ALTER TABLE {S}.tblHallazgos ADD CONSTRAINT CK_tblHallazgos_Origen CHECK (Origen IN (~inspector~, ~conductor~));
ALTER TABLE {S}.tblHallazgos ADD CONSTRAINT CK_tblHallazgos_Clase CHECK (ClaseDano IS NOT NULL OR Origen = ~conductor~);
ALTER TABLE {S}.tblHallazgos ADD CONSTRAINT CK_tblHallazgos_TipoNovedad CHECK (TipoNovedad IS NULL OR (Origen = ~conductor~ AND TipoNovedad IN (~golpe~, ~piedra~, ~granizo~, ~incidente_vial~)));
ALTER TABLE {S}.tblHallazgos ADD CONSTRAINT CK_tblHallazgos_Seguimiento CHECK (EstadoSeguimiento IN (~pendiente~, ~validado~, ~rechazado~, ~recuperado~, ~derogado~));
ALTER TABLE {S}.tblHallazgos ADD CONSTRAINT CK_tblHallazgos_Preexistente CHECK (EsPreexistente = 0 OR HallazgoOrigenId IS NOT NULL)'),
(N'hallazgos historicos: zona por el texto de la parte', N'
UPDATE h SET h.ZonaId = z.Id
FROM {S}.tblHallazgos h
JOIN (VALUES (N~Fachada delantera~, ~frente~), (N~Parachoque delantero~, ~frente~),
             (N~Llantas/rines delantero izquierdo~, ~llanta_delantera_izquierda~), (N~Llantas/rines delantero derecho~, ~llanta_delantera_derecha~),
             (N~Llantas/rines trasero izquierdo~, ~llanta_trasera_izquierda~), (N~Llantas/rines trasero derecho~, ~llanta_trasera_derecha~))
     AS m (Parte, Zona) ON m.Parte = h.ParteVehiculo
JOIN {S}.tblZonasDano z ON z.Codigo = m.Zona
WHERE h.ZonaId IS NULL;
UPDATE {S}.tblHallazgos SET MigracionPendiente = 1 WHERE ZonaId IS NULL'),
(N'tblViajeVehiculos: estado de la unidad', N'
ALTER TABLE {S}.tblViajeVehiculos ADD
    EstadoUnidad          varchar(10)       NOT NULL CONSTRAINT DF_tblViajeVehiculos_EstadoUnidad DEFAULT (~activa~),
    NoSaleMotivo          nvarchar(500)     NULL,
    NoSaleHallazgoId      uniqueidentifier  NULL,
    NoSaleMarcadoPor      uniqueidentifier  NULL,
    NoSaleAt              datetimeoffset(3) NULL,
    ReemplazoVehiculoId   uniqueidentifier  NULL'),
(N'tblViajeVehiculos: relaciones y restricciones', N'
ALTER TABLE {S}.tblViajeVehiculos ADD CONSTRAINT FK_tblViajeVehiculos_NoSaleHallazgo FOREIGN KEY (NoSaleHallazgoId) REFERENCES {S}.tblHallazgos (Id);
ALTER TABLE {S}.tblViajeVehiculos ADD CONSTRAINT FK_tblViajeVehiculos_NoSaleMarcadoPor FOREIGN KEY (NoSaleMarcadoPor) REFERENCES {S}.tblUsuarios (Id);
ALTER TABLE {S}.tblViajeVehiculos ADD CONSTRAINT FK_tblViajeVehiculos_Reemplazo FOREIGN KEY (ReemplazoVehiculoId) REFERENCES {S}.tblVehiculos (Id);
ALTER TABLE {S}.tblViajeVehiculos ADD CONSTRAINT CK_tblViajeVehiculos_EstadoUnidad CHECK (EstadoUnidad IN (~activa~, ~no_sale~));
ALTER TABLE {S}.tblViajeVehiculos ADD CONSTRAINT CK_tblViajeVehiculos_NoSale CHECK (
    (EstadoUnidad = ~activa~ AND NoSaleMotivo IS NULL AND NoSaleHallazgoId IS NULL AND NoSaleMarcadoPor IS NULL AND NoSaleAt IS NULL AND ReemplazoVehiculoId IS NULL)
    OR (EstadoUnidad = ~no_sale~ AND NoSaleMotivo IS NOT NULL AND NoSaleHallazgoId IS NOT NULL AND NoSaleMarcadoPor IS NOT NULL AND NoSaleAt IS NOT NULL))');

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

-- Reporte de migracion. 1) Resumen (esperado: regiones migradas + poligonos sin migrar = poligonos de origen; hallazgos con zona + pendientes = hallazgos).
DECLARE @cuentas nvarchar(max) = REPLACE(N'
SELECT N~paneles~ AS Dato, COUNT(*) AS Total FROM {S}.tblPanelesDano UNION ALL
SELECT N~zonas numeradas (cuadrante + lado)~, COUNT(*) FROM {S}.tblZonasDano WHERE CuadranteId IS NOT NULL UNION ALL
SELECT N~zonas provisionales~, COUNT(*) FROM {S}.tblZonasDano WHERE CuadranteId IS NULL UNION ALL
SELECT N~poligonos de origen (tblCuadranteVistas)~, COUNT(*) FROM {S}.tblCuadranteVistas UNION ALL
SELECT N~regiones migradas~, COUNT(*) FROM {S}.tblRegionesDano UNION ALL
SELECT N~poligonos sin migrar (panel superior con numero del 1 al 25: revision manual)~, COUNT(*) FROM {S}.tblCuadranteVistas cv
    JOIN {S}.tblVistasFoto v ON v.Id = cv.VistaFotoId JOIN {S}.tblCuadrantes c ON c.Id = cv.CuadranteId
    WHERE NOT EXISTS (SELECT 1 FROM {S}.tblRegionesDano r JOIN {S}.tblZonasDano z ON z.Id = r.ZonaId WHERE z.CuadranteId = c.Id AND r.Geometria = cv.Geometria) UNION ALL
SELECT N~hallazgos~, COUNT(*) FROM {S}.tblHallazgos UNION ALL
SELECT N~hallazgos con zona~, COUNT(*) FROM {S}.tblHallazgos WHERE ZonaId IS NOT NULL UNION ALL
SELECT N~hallazgos pendientes de ubicar (MigracionPendiente = 1)~, COUNT(*) FROM {S}.tblHallazgos WHERE MigracionPendiente = 1 UNION ALL
SELECT N~unidades de viaje activas~, COUNT(*) FROM {S}.tblViajeVehiculos WHERE EstadoUnidad = ~activa~;
-- 2) Hallazgos que no se pudieron ubicar: conservan ParteVehiculo y CoordsDiagrama para ubicarlos a mano.
SELECT h.Id AS HallazgoId, h.InspeccionId, h.ParteVehiculo, h.CoordsDiagrama FROM {S}.tblHallazgos h WHERE h.MigracionPendiente = 1 ORDER BY h.ParteVehiculo;
-- 3) Poligonos que no se migraron.
SELECT v.Codigo AS Vista, c.Numero, c.Nombre FROM {S}.tblCuadranteVistas cv
    JOIN {S}.tblVistasFoto v ON v.Id = cv.VistaFotoId JOIN {S}.tblCuadrantes c ON c.Id = cv.CuadranteId
    WHERE NOT EXISTS (SELECT 1 FROM {S}.tblRegionesDano r JOIN {S}.tblZonasDano z ON z.Id = r.ZonaId WHERE z.CuadranteId = c.Id AND r.Geometria = cv.Geometria)
    ORDER BY v.Codigo, c.Numero', N'{S}', @q);
SET @cuentas = REPLACE(@cuentas, N'~', N'''');
EXEC (@cuentas);
