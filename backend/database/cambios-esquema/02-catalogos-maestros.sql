/* =============================================================================
   Catalogos maestros para una base de IA Autos YA EXISTENTE.
   Se ejecuta UNA vez (si ya esta migrada, no hace nada) y todo ocurre en UNA transaccion: si algo falla se revierte completo.

   Que hace:
     1. Crea tblEtapasCatalogo (cargue, transporte, descargue) y mueve a ella el "proceso" que hoy tiene tblEtapas.
     2. Rehace tblPuntosCustodia como LUGARES (CEDI 1, Origen, Certificadora, Destino, CEDI 2, Concesionario). Los 7 valores
        anteriores se separan asi: cargue, transporte y descargue pasan a ser ETAPAS; punto_origen, certificadora,
        punto_destino y concesionario pasan a ser LUGARES (Origen, Certificadora, Destino, Concesionario); CEDI 1 y CEDI 2 son nuevos.
        Las etapas existentes conservan su proceso (etapa) y quedan sin lugar (NULL) hasta que se asigne desde el portal.
     3. Crea tblTiposUnidadTransportadora, tblClasesDano, tblTamanosDano, tblNivelesSeveridad, tblVistasFoto y tblCuadrantes
        con sus valores iniciales. Los cuadrantes y las acciones de los niveles no tienen semilla: se cargan desde el portal.
     4. Reemplaza los CHECK de tblHallazgos (clase y tamano de dano) por llaves foraneas a los catalogos. Los hallazgos
        existentes no cambian: ya guardan el codigo.

   Seguridad de los datos: aborta (sin cambiar nada) si alguna etapa usa uno de los 4 puntos que no son etapa, porque no
   tendria una etapa a la cual migrar. Hoy ninguna lo hace.

   Como ejecutarlo: abrir en SSMS (o sqlcmd) CONECTADO a la base de IA Autos, ajustar @esquema y ejecutar con un usuario con
   permiso para crear tablas (no el usuario de la aplicacion). Luego dar permisos a la aplicacion si no los tiene por esquema:
     GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::<esquema> TO <usuario_de_la_app>;
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
IF OBJECT_ID(@q + N'.tblPuntosCustodia', N'U') IS NULL OR OBJECT_ID(@q + N'.tblEtapas', N'U') IS NULL OR OBJECT_ID(@q + N'.tblHallazgos', N'U') IS NULL
    THROW 50001, N'Faltan tablas de IA Autos en el esquema indicado: conectese a la base correcta y revise @esquema.', 1;

IF COL_LENGTH(@q + N'.tblPuntosCustodia', N'Codigo') IS NOT NULL
BEGIN
    PRINT N'Los catalogos maestros ya estan migrados: no se hace nada.';
    RETURN;
END

-- Precondicion: ninguna etapa debe apuntar a un punto que no sea cargue, transporte o descargue.
DECLARE @fueraDeEtapa int, @sqlChk nvarchar(max) = REPLACE(N'
    SELECT @n = COUNT(*)
      FROM {S}.tblEtapas e JOIN {S}.tblPuntosCustodia p ON p.Id = e.PuntoCustodiaId
     WHERE p.Nombre NOT IN (~cargue~, ~transporte~, ~descargue~)', N'{S}', @q);
SET @sqlChk = REPLACE(@sqlChk, N'~', N'''');
EXEC sp_executesql @sqlChk, N'@n int OUTPUT', @n = @fueraDeEtapa OUTPUT;
IF @fueraDeEtapa > 0
    THROW 50010, N'Hay etapas en puntos que no son cargue, transporte ni descargue: defina a que etapa migrarlas antes de ejecutar este script.', 1;

-- Pasos en orden. {S} = esquema entre corchetes; ~ = comilla simple dentro del texto.
DECLARE @paso TABLE (n int IDENTITY(1, 1) PRIMARY KEY, descripcion nvarchar(100), s nvarchar(max));

INSERT @paso (descripcion, s) VALUES
(N'tblEtapasCatalogo', N'
CREATE TABLE {S}.tblEtapasCatalogo
(
    Id         uniqueidentifier  NOT NULL CONSTRAINT PK_tblEtapasCatalogo PRIMARY KEY,
    Codigo     varchar(30)       NOT NULL,
    Nombre     nvarchar(100)     NOT NULL,
    Orden      int               NOT NULL CONSTRAINT DF_tblEtapasCatalogo_Orden DEFAULT (0),
    Activo     bit               NOT NULL CONSTRAINT DF_tblEtapasCatalogo_Activo DEFAULT (1),
    CreatedAt  datetimeoffset(3) NOT NULL CONSTRAINT DF_tblEtapasCatalogo_CreatedAt DEFAULT (SYSDATETIMEOFFSET()),
    UpdatedAt  datetimeoffset(3) NOT NULL CONSTRAINT DF_tblEtapasCatalogo_UpdatedAt DEFAULT (SYSDATETIMEOFFSET()),
    CONSTRAINT UQ_tblEtapasCatalogo_Codigo UNIQUE (Codigo)
)'),
(N'semilla etapas', N'
INSERT INTO {S}.tblEtapasCatalogo (Id, Codigo, Nombre, Orden)
SELECT NEWID(), v.Codigo, v.Nombre, v.Orden
FROM (VALUES (~cargue~, N~Cargue~, 1), (~transporte~, N~Transporte~, 2), (~descargue~, N~Descargue~, 3)) AS v (Codigo, Nombre, Orden)'),
(N'tblEtapas: columna EtapaCatalogoId', N'ALTER TABLE {S}.tblEtapas ADD EtapaCatalogoId uniqueidentifier NULL'),
(N'tblEtapas: proceso actual -> etapa del catalogo', N'
UPDATE e SET e.EtapaCatalogoId = c.Id
  FROM {S}.tblEtapas e
  JOIN {S}.tblPuntosCustodia p ON p.Id = e.PuntoCustodiaId
  JOIN {S}.tblEtapasCatalogo c ON c.Codigo = p.Nombre'),
(N'tblEtapas: EtapaCatalogoId obligatoria', N'ALTER TABLE {S}.tblEtapas ALTER COLUMN EtapaCatalogoId uniqueidentifier NOT NULL'),
(N'tblEtapas: FK a la etapa', N'ALTER TABLE {S}.tblEtapas ADD CONSTRAINT FK_tblEtapas_EtapaCatalogo FOREIGN KEY (EtapaCatalogoId) REFERENCES {S}.tblEtapasCatalogo (Id)'),
(N'tblEtapas: indice por etapa', N'CREATE INDEX IX_tblEtapas_EtapaCatalogo ON {S}.tblEtapas (EtapaCatalogoId)'),
(N'tblEtapas: suelta el vinculo viejo', N'ALTER TABLE {S}.tblEtapas DROP CONSTRAINT FK_tblEtapas_PuntoCustodia'),
(N'tblEtapas: suelta el indice viejo', N'DROP INDEX IX_tblEtapas_PuntoCustodia ON {S}.tblEtapas'),
(N'tblEtapas: lugar opcional', N'ALTER TABLE {S}.tblEtapas ALTER COLUMN PuntoCustodiaId uniqueidentifier NULL'),
(N'tblEtapas: el lugar queda sin asignar', N'UPDATE {S}.tblEtapas SET PuntoCustodiaId = NULL'),
(N'tblPuntosCustodia: retira los 7 valores anteriores', N'DELETE FROM {S}.tblPuntosCustodia'),
(N'tblPuntosCustodia: suelta CHECK', N'ALTER TABLE {S}.tblPuntosCustodia DROP CONSTRAINT CK_tblPuntosCustodia_Nombre'),
(N'tblPuntosCustodia: suelta UNIQUE', N'ALTER TABLE {S}.tblPuntosCustodia DROP CONSTRAINT UQ_tblPuntosCustodia_Nombre'),
(N'tblPuntosCustodia: nombre', N'ALTER TABLE {S}.tblPuntosCustodia ALTER COLUMN Nombre nvarchar(100) NOT NULL'),
(N'tblPuntosCustodia: columnas nuevas', N'
ALTER TABLE {S}.tblPuntosCustodia ADD
    Codigo varchar(30) NULL,
    Orden  int NOT NULL CONSTRAINT DF_tblPuntosCustodia_Orden DEFAULT (0),
    Activo bit NOT NULL CONSTRAINT DF_tblPuntosCustodia_Activo DEFAULT (1)'),
(N'tblPuntosCustodia: codigo obligatorio', N'ALTER TABLE {S}.tblPuntosCustodia ALTER COLUMN Codigo varchar(30) NOT NULL'),
(N'tblPuntosCustodia: codigo unico', N'ALTER TABLE {S}.tblPuntosCustodia ADD CONSTRAINT UQ_tblPuntosCustodia_Codigo UNIQUE (Codigo)'),
(N'semilla lugares', N'
INSERT INTO {S}.tblPuntosCustodia (Id, Codigo, Nombre, Orden)
SELECT NEWID(), v.Codigo, v.Nombre, v.Orden
FROM (VALUES (~cedi_1~, N~CEDI 1~, 1), (~origen~, N~Origen~, 2), (~certificadora~, N~Certificadora~, 3),
             (~destino~, N~Destino~, 4), (~cedi_2~, N~CEDI 2~, 5), (~concesionario~, N~Concesionario~, 6)) AS v (Codigo, Nombre, Orden)'),
(N'tblEtapas: FK al lugar', N'ALTER TABLE {S}.tblEtapas ADD CONSTRAINT FK_tblEtapas_PuntoCustodia FOREIGN KEY (PuntoCustodiaId) REFERENCES {S}.tblPuntosCustodia (Id)'),
(N'tblEtapas: indice por lugar', N'CREATE INDEX IX_tblEtapas_PuntoCustodia ON {S}.tblEtapas (PuntoCustodiaId)'),

(N'tblTiposUnidadTransportadora', N'
CREATE TABLE {S}.tblTiposUnidadTransportadora
(
    Id         uniqueidentifier  NOT NULL CONSTRAINT PK_tblTiposUnidadTransportadora PRIMARY KEY,
    Codigo     varchar(30)       NOT NULL,
    Nombre     nvarchar(100)     NOT NULL,
    Orden      int               NOT NULL CONSTRAINT DF_tblTiposUnidadTransportadora_Orden DEFAULT (0),
    Activo     bit               NOT NULL CONSTRAINT DF_tblTiposUnidadTransportadora_Activo DEFAULT (1),
    CreatedAt  datetimeoffset(3) NOT NULL CONSTRAINT DF_tblTiposUnidadTransportadora_CreatedAt DEFAULT (SYSDATETIMEOFFSET()),
    UpdatedAt  datetimeoffset(3) NOT NULL CONSTRAINT DF_tblTiposUnidadTransportadora_UpdatedAt DEFAULT (SYSDATETIMEOFFSET()),
    CONSTRAINT UQ_tblTiposUnidadTransportadora_Codigo UNIQUE (Codigo)
)'),
(N'semilla tipos de unidad', N'
INSERT INTO {S}.tblTiposUnidadTransportadora (Id, Codigo, Nombre, Orden)
SELECT NEWID(), v.Codigo, v.Nombre, v.Orden
FROM (VALUES (~ninera~, N~Ni~ + NCHAR(241) + N~era~, 1), (~nodriza~, N~Nodriza~, 2), (~quintinaria~, N~Quintinaria~, 3),
             (~planchon_grua~, N~Planch~ + NCHAR(243) + N~n gr~ + NCHAR(250) + N~a~, 4)) AS v (Codigo, Nombre, Orden)'),

(N'tblClasesDano', N'
CREATE TABLE {S}.tblClasesDano
(
    Id         uniqueidentifier  NOT NULL CONSTRAINT PK_tblClasesDano PRIMARY KEY,
    Codigo     varchar(30)       NOT NULL,
    Nombre     nvarchar(100)     NOT NULL,
    Orden      int               NOT NULL CONSTRAINT DF_tblClasesDano_Orden DEFAULT (0),
    Activo     bit               NOT NULL CONSTRAINT DF_tblClasesDano_Activo DEFAULT (1),
    CreatedAt  datetimeoffset(3) NOT NULL CONSTRAINT DF_tblClasesDano_CreatedAt DEFAULT (SYSDATETIMEOFFSET()),
    UpdatedAt  datetimeoffset(3) NOT NULL CONSTRAINT DF_tblClasesDano_UpdatedAt DEFAULT (SYSDATETIMEOFFSET()),
    CONSTRAINT UQ_tblClasesDano_Codigo UNIQUE (Codigo)
)'),
(N'semilla clases de dano', N'
INSERT INTO {S}.tblClasesDano (Id, Codigo, Nombre, Orden)
SELECT NEWID(), v.Codigo, v.Nombre, v.Orden
FROM (VALUES (~deformacion~, N~Deformaci~ + NCHAR(243) + N~n~, 1), (~rotura~, N~Rotura~, 2), (~cortado~, N~Cortado~, 3),
             (~perforacion~, N~Perforaci~ + NCHAR(243) + N~n~, 4), (~golpe~, N~Golpe~, 5), (~mancha~, N~Mancha~, 6),
             (~raya_pintar~, N~Raya para pintar~, 7), (~desconchado~, N~Desconchado~, 8),
             (~particulas~, N~Part~ + NCHAR(237) + N~culas~, 9), (~faltante~, N~Faltante~, 10)) AS v (Codigo, Nombre, Orden)'),

(N'tblTamanosDano', N'
CREATE TABLE {S}.tblTamanosDano
(
    Id         uniqueidentifier  NOT NULL CONSTRAINT PK_tblTamanosDano PRIMARY KEY,
    Codigo     varchar(30)       NOT NULL,
    Nombre     nvarchar(100)     NOT NULL,
    Orden      int               NOT NULL CONSTRAINT DF_tblTamanosDano_Orden DEFAULT (0),
    Activo     bit               NOT NULL CONSTRAINT DF_tblTamanosDano_Activo DEFAULT (1),
    CreatedAt  datetimeoffset(3) NOT NULL CONSTRAINT DF_tblTamanosDano_CreatedAt DEFAULT (SYSDATETIMEOFFSET()),
    UpdatedAt  datetimeoffset(3) NOT NULL CONSTRAINT DF_tblTamanosDano_UpdatedAt DEFAULT (SYSDATETIMEOFFSET()),
    CONSTRAINT UQ_tblTamanosDano_Codigo UNIQUE (Codigo)
)'),
(N'semilla tamanos de dano', N'
INSERT INTO {S}.tblTamanosDano (Id, Codigo, Nombre, Orden)
SELECT NEWID(), v.Codigo, v.Nombre, v.Orden
FROM (VALUES (~1_5cm~, N~1 a 5 cm~, 1), (~5_10cm~, N~5 a 10 cm~, 2), (~10_20cm~, N~10 a 20 cm~, 3),
             (~mas_20cm~, N~M~ + NCHAR(225) + N~s de 20 cm~, 4), (~requiere_reemplazo~, N~Requiere reemplazo~, 5),
             (~mas_10_particulas~, N~M~ + NCHAR(225) + N~s de 10 part~ + NCHAR(237) + N~culas~, 6)) AS v (Codigo, Nombre, Orden)'),

(N'hallazgos: suelta CHECK de clase', N'ALTER TABLE {S}.tblHallazgos DROP CONSTRAINT CK_tblHallazgos_ClaseDano'),
(N'hallazgos: suelta CHECK de tamano', N'ALTER TABLE {S}.tblHallazgos DROP CONSTRAINT CK_tblHallazgos_TamanoDano'),
(N'hallazgos: FK a la clase', N'ALTER TABLE {S}.tblHallazgos ADD CONSTRAINT FK_tblHallazgos_ClaseDano FOREIGN KEY (ClaseDano) REFERENCES {S}.tblClasesDano (Codigo)'),
(N'hallazgos: FK al tamano', N'ALTER TABLE {S}.tblHallazgos ADD CONSTRAINT FK_tblHallazgos_TamanoDano FOREIGN KEY (TamanoDano) REFERENCES {S}.tblTamanosDano (Codigo)'),

(N'tblNivelesSeveridad', N'
CREATE TABLE {S}.tblNivelesSeveridad
(
    Id         uniqueidentifier  NOT NULL CONSTRAINT PK_tblNivelesSeveridad PRIMARY KEY,
    Codigo     varchar(30)       NOT NULL,
    Nombre     nvarchar(100)     NOT NULL,
    Accion     nvarchar(500)     NULL,
    Orden      int               NOT NULL CONSTRAINT DF_tblNivelesSeveridad_Orden DEFAULT (0),
    Activo     bit               NOT NULL CONSTRAINT DF_tblNivelesSeveridad_Activo DEFAULT (1),
    CreatedAt  datetimeoffset(3) NOT NULL CONSTRAINT DF_tblNivelesSeveridad_CreatedAt DEFAULT (SYSDATETIMEOFFSET()),
    UpdatedAt  datetimeoffset(3) NOT NULL CONSTRAINT DF_tblNivelesSeveridad_UpdatedAt DEFAULT (SYSDATETIMEOFFSET()),
    CONSTRAINT UQ_tblNivelesSeveridad_Codigo UNIQUE (Codigo)
)'),
(N'semilla niveles', N'
INSERT INTO {S}.tblNivelesSeveridad (Id, Codigo, Nombre, Orden)
SELECT NEWID(), v.Codigo, v.Nombre, v.Orden
FROM (VALUES (~v1_plus~, N~V1+~, 1), (~v1~, N~V1~, 2), (~v2~, N~V2~, 3), (~v3~, N~V3~, 4)) AS v (Codigo, Nombre, Orden)'),

(N'tblVistasFoto', N'
CREATE TABLE {S}.tblVistasFoto
(
    Id            uniqueidentifier  NOT NULL CONSTRAINT PK_tblVistasFoto PRIMARY KEY,
    Codigo        varchar(30)       NOT NULL,
    Nombre        nvarchar(100)     NOT NULL,
    Obligatoria   bit               NOT NULL CONSTRAINT DF_tblVistasFoto_Obligatoria DEFAULT (1),
    TipoVehiculo  varchar(30)       NULL,
    Orden         int               NOT NULL CONSTRAINT DF_tblVistasFoto_Orden DEFAULT (0),
    Activo        bit               NOT NULL CONSTRAINT DF_tblVistasFoto_Activo DEFAULT (1),
    CreatedAt     datetimeoffset(3) NOT NULL CONSTRAINT DF_tblVistasFoto_CreatedAt DEFAULT (SYSDATETIMEOFFSET()),
    UpdatedAt     datetimeoffset(3) NOT NULL CONSTRAINT DF_tblVistasFoto_UpdatedAt DEFAULT (SYSDATETIMEOFFSET()),
    CONSTRAINT UQ_tblVistasFoto_Codigo UNIQUE (Codigo)
)'),
(N'semilla vistas de foto', N'
INSERT INTO {S}.tblVistasFoto (Id, Codigo, Nombre, Obligatoria, Orden)
SELECT NEWID(), v.Codigo, v.Nombre, v.Obligatoria, v.Orden
FROM (VALUES (~frontal~, N~Frontal~, 1, 1), (~trasera~, N~Trasera~, 1, 2), (~lateral_derecha~, N~Lateral derecha~, 1, 3),
             (~lateral_izquierda~, N~Lateral izquierda~, 1, 4), (~diagonal_trasera_izquierda~, N~Diagonal trasera izquierda~, 1, 5),
             (~diagonal_delantera_derecha~, N~Diagonal delantera derecha~, 1, 6), (~superior~, N~Superior~, 1, 7),
             (~piezas_baul~, N~Piezas en el ba~ + NCHAR(250) + N~l~, 0, 8), (~cojineria~, N~Cojiner~ + NCHAR(237) + N~a~, 0, 9))
      AS v (Codigo, Nombre, Obligatoria, Orden)'),

(N'tblCuadrantes', N'
CREATE TABLE {S}.tblCuadrantes
(
    Id            uniqueidentifier  NOT NULL CONSTRAINT PK_tblCuadrantes PRIMARY KEY,
    VistaFotoId   uniqueidentifier  NOT NULL,
    TipoVehiculo  varchar(30)       NULL,
    Numero        int               NOT NULL,
    Nombre        nvarchar(100)     NOT NULL,
    Geometria     nvarchar(max)     NULL,
    Orden         int               NOT NULL CONSTRAINT DF_tblCuadrantes_Orden DEFAULT (0),
    Activo        bit               NOT NULL CONSTRAINT DF_tblCuadrantes_Activo DEFAULT (1),
    CreatedAt     datetimeoffset(3) NOT NULL CONSTRAINT DF_tblCuadrantes_CreatedAt DEFAULT (SYSDATETIMEOFFSET()),
    UpdatedAt     datetimeoffset(3) NOT NULL CONSTRAINT DF_tblCuadrantes_UpdatedAt DEFAULT (SYSDATETIMEOFFSET()),
    CONSTRAINT FK_tblCuadrantes_Vista FOREIGN KEY (VistaFotoId) REFERENCES {S}.tblVistasFoto (Id),
    CONSTRAINT UQ_tblCuadrantes_Tipo_Numero UNIQUE (TipoVehiculo, Numero),
    CONSTRAINT CK_tblCuadrantes_Numero CHECK (Numero >= 1),
    CONSTRAINT CK_tblCuadrantes_Geometria CHECK (Geometria IS NULL OR ISJSON(Geometria) = 1)
)'),
(N'tblCuadrantes: indice por vista', N'CREATE INDEX IX_tblCuadrantes_Vista ON {S}.tblCuadrantes (VistaFotoId)');

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

-- Resultado: cantidad de valores por catalogo (esperado: 3, 6, 4, 10, 6, 4, 9 y 0 cuadrantes).
DECLARE @cuentas nvarchar(max) = REPLACE(N'
SELECT N~etapas~ AS Catalogo, COUNT(*) AS Valores FROM {S}.tblEtapasCatalogo UNION ALL
SELECT N~puntos de custodia~, COUNT(*) FROM {S}.tblPuntosCustodia UNION ALL
SELECT N~tipos de unidad~, COUNT(*) FROM {S}.tblTiposUnidadTransportadora UNION ALL
SELECT N~clases de dano~, COUNT(*) FROM {S}.tblClasesDano UNION ALL
SELECT N~tamanos de dano~, COUNT(*) FROM {S}.tblTamanosDano UNION ALL
SELECT N~niveles de severidad~, COUNT(*) FROM {S}.tblNivelesSeveridad UNION ALL
SELECT N~vistas de foto~, COUNT(*) FROM {S}.tblVistasFoto UNION ALL
SELECT N~cuadrantes~, COUNT(*) FROM {S}.tblCuadrantes UNION ALL
SELECT N~etapas de vehiculos con etapa asignada~, COUNT(*) FROM {S}.tblEtapas WHERE EtapaCatalogoId IS NOT NULL', N'{S}', @q);
SET @cuentas = REPLACE(@cuentas, N'~', N'''');
EXEC (@cuentas);
