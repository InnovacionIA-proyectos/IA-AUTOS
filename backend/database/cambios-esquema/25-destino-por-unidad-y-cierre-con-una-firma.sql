/* =============================================================================
   Destino por unidad y cierre "recibo sin entrega" con una sola firma, para una base de IA Autos YA EXISTENTE (con 24-lugares-y-programacion.sql aplicado). Se ejecuta UNA vez (si ya esta aplicado,
   no cambia nada) y todo ocurre en UNA transaccion: si algo falla se revierte completo.

   Que hace:
     1. Destino por unidad. La columna tblViajeVehiculos.LugarDestinoId (hacia el catalogo de lugares reales) ya existe desde el script 24: no se crea otra. Los viajes anteriores tienen como destino
        un TIPO de lugar (tblViajes.DestinoId -> tblPuntosCustodia), que no es un lugar real; para no dejar unidades sin destino se crea UN lugar historico por cada tipo que algun viaje usa como
        destino ("<nombre del tipo> (por definir)", ciudad "Sin definir", INACTIVO: no se puede elegir en las cargas nuevas) y se copia a cada unidad del viaje. Quien administra el catalogo
        reasigna despues cada unidad a su lugar real. tblViajes.DestinoId se conserva como dato historico, sin uso nuevo.
     2. tblTransferenciasCustodia: el cierre recibo_sin_entrega pasa a admitir las DOS formas de una sola firma: firma solo quien recibe (nadie entrego: NoFirmaParte = entrega, sin usuario,
        firma, fecha ni IP de entrega; es lo que ya existia) o firma solo quien entrega (NoFirmaParte = recibe, sin firma, fecha ni IP de quien recibe). NoFirmaParte es la parte que NO firmo; la que
        firmo es la otra. En las dos formas el motivo es obligatorio (MotivoNoFirma). Los recibos sin entrega ya registrados quedan con NoFirmaParte = entrega y el motivo "El conductor hizo la salida
        del cargue y nadie de Botero Soto entrego la unidad", que es el unico caso que existia. Los cierres firmada, con_reservas y no_firma no cambian.

   Seguridad de los datos: no borra nada. Solo agrega lugares historicos, llena LugarDestinoId donde esta nulo y completa NoFirmaParte y MotivoNoFirma de los recibos sin entrega que no los tenian.

   Como ejecutarlo: abrir en SSMS, DBeaver o sqlcmd CONECTADO a la base de IA Autos, ajustar @esquema y ejecutar completo, en UNA sola ejecucion de script, con un usuario con permiso para
   alterar tablas (no el usuario de la aplicacion). Ejecutarlo ANTES de desplegar la API.
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
IF OBJECT_ID(@q + N'.tblLugares', N'U') IS NULL OR OBJECT_ID(@q + N'.tblViajeVehiculos', N'U') IS NULL OR OBJECT_ID(@q + N'.tblTransferenciasCustodia', N'U') IS NULL
    THROW 50001, N'Faltan las tablas tblLugares, tblViajeVehiculos o tblTransferenciasCustodia de IA Autos en el esquema indicado: revise @esquema.', 1;
IF COL_LENGTH(@q + N'.tblViajeVehiculos', N'LugarDestinoId') IS NULL
    THROW 50002, N'Falta tblViajeVehiculos.LugarDestinoId: ejecute antes 24-lugares-y-programacion.sql.', 1;

-- Pasos en orden. {S} = esquema entre corchetes; ~ = comilla simple dentro del texto.
DECLARE @paso TABLE (n int IDENTITY(1, 1) PRIMARY KEY, descripcion nvarchar(100), s nvarchar(max));

INSERT @paso (descripcion, s) VALUES
(N'Lugares historicos de los destinos anteriores', N'
INSERT {S}.tblLugares (Id, Nombre, TipoId, Ciudad, Orden, Activo)
SELECT NEWID(), LEFT(p.Nombre, 80) + N~ (por definir)~, p.Id, N~Sin definir~, 0, 0
FROM {S}.tblPuntosCustodia p
WHERE EXISTS (SELECT 1 FROM {S}.tblViajes vj WHERE vj.DestinoId = p.Id)
  AND NOT EXISTS (SELECT 1 FROM {S}.tblLugares l WHERE l.Nombre = LEFT(p.Nombre, 80) + N~ (por definir)~)'),
(N'Destino de cada unidad = destino de su viaje', N'
UPDATE vv SET LugarDestinoId = l.Id
FROM {S}.tblViajeVehiculos vv
JOIN {S}.tblViajes vj ON vj.Id = vv.ViajeId
JOIN {S}.tblPuntosCustodia p ON p.Id = vj.DestinoId
JOIN {S}.tblLugares l ON l.TipoId = p.Id AND l.Nombre = LEFT(p.Nombre, 80) + N~ (por definir)~
WHERE vv.LugarDestinoId IS NULL'),
(N'Recibos sin entrega anteriores: parte y motivo', N'
UPDATE {S}.tblTransferenciasCustodia
SET NoFirmaParte = N~entrega~,
    MotivoNoFirma = N~El conductor hizo la salida del cargue y nadie de Botero Soto entreg~ + NCHAR(243) + N~ la unidad~
WHERE TipoCierre = N~recibo_sin_entrega~ AND (NoFirmaParte IS NULL OR MotivoNoFirma IS NULL OR MotivoNoFirma = N~~)'),
(N'tblTransferenciasCustodia: recibo sin entrega con una sola firma', N'
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N~CK_tblTransferencias_Cierre~ AND parent_object_id = OBJECT_ID(N~{S}.tblTransferenciasCustodia~) AND definition LIKE N~%recibo_sin_entrega%NoFirmaParte%~)
BEGIN
    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N~CK_tblTransferencias_Cierre~ AND parent_object_id = OBJECT_ID(N~{S}.tblTransferenciasCustodia~))
        ALTER TABLE {S}.tblTransferenciasCustodia DROP CONSTRAINT CK_tblTransferencias_Cierre;
    ALTER TABLE {S}.tblTransferenciasCustodia ADD CONSTRAINT CK_tblTransferencias_Cierre CHECK (
        TipoCierre IS NULL
        OR (TipoCierre = ~firmada~ AND FirmaEntrega IS NOT NULL AND FirmaEntrega <> ~~ AND FirmaRecibe IS NOT NULL AND FirmaRecibe <> ~~ AND ReservasTexto IS NULL)
        OR (TipoCierre = ~con_reservas~ AND FirmaEntrega IS NOT NULL AND FirmaEntrega <> ~~ AND FirmaRecibe IS NOT NULL AND FirmaRecibe <> ~~ AND ReservasTexto IS NOT NULL AND ReservasTexto <> ~~)
        OR (TipoCierre = ~no_firma~ AND NoFirmaParte IS NOT NULL AND MotivoNoFirma IS NOT NULL AND MotivoNoFirma <> ~~ AND TestigoUsuarioId IS NOT NULL AND EvidenciaConstanciaId IS NOT NULL)
        -- Una sola firma y su motivo: firma solo quien recibe (nadie entrego) o firma solo quien entrega (quien recibe no firmo).
        OR (TipoCierre = ~recibo_sin_entrega~ AND NoFirmaParte IS NOT NULL AND MotivoNoFirma IS NOT NULL AND MotivoNoFirma <> ~~ AND ReservasTexto IS NULL
            AND ((NoFirmaParte = ~entrega~ AND EntregaUsuarioId IS NULL AND FirmaEntrega IS NULL AND FirmaEntregaAt IS NULL AND FirmaEntregaIp IS NULL
                  AND FirmaRecibe IS NOT NULL AND FirmaRecibe <> ~~)
              OR (NoFirmaParte = ~recibe~ AND EntregaUsuarioId IS NOT NULL AND FirmaEntrega IS NOT NULL AND FirmaEntrega <> ~~
                  AND FirmaRecibe IS NULL AND FirmaRecibeAt IS NULL AND FirmaRecibeIp IS NULL))));
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

-- Resultado 1. Esperado justo despues de migrar: UnidadesSinDestino = 0 y UnidadesConOtroDestino = 0 (CA-2); RecibosSinParteOMotivo = 0; CierreNuevo = 1.
-- LugaresHistoricos es cuantos tipos de lugar usaban los viajes como destino (0 si ningun viaje tenia destino). Si se repite mas adelante, UnidadesConOtroDestino puede ser mayor que 0
-- porque ya se reasignaron unidades a sus lugares reales: es lo esperado.
DECLARE @cuentas nvarchar(max) = REPLACE(N'
SELECT
    (SELECT COUNT(*) FROM {S}.tblViajeVehiculos vv JOIN {S}.tblViajes vj ON vj.Id = vv.ViajeId WHERE vj.DestinoId IS NOT NULL AND vv.LugarDestinoId IS NULL) AS UnidadesSinDestino,
    (SELECT COUNT(*) FROM {S}.tblViajeVehiculos vv JOIN {S}.tblViajes vj ON vj.Id = vv.ViajeId JOIN {S}.tblLugares l ON l.Id = vv.LugarDestinoId
      WHERE vj.DestinoId IS NOT NULL AND l.TipoId <> vj.DestinoId) AS UnidadesConOtroDestino,
    (SELECT COUNT(*) FROM {S}.tblLugares WHERE Nombre LIKE N~% (por definir)~ AND Activo = 0) AS LugaresHistoricos,
    (SELECT COUNT(*) FROM {S}.tblTransferenciasCustodia WHERE TipoCierre = N~recibo_sin_entrega~ AND (NoFirmaParte IS NULL OR MotivoNoFirma IS NULL OR MotivoNoFirma = N~~)) AS RecibosSinParteOMotivo,
    (SELECT COUNT(*) FROM sys.check_constraints WHERE name = N~CK_tblTransferencias_Cierre~ AND parent_object_id = OBJECT_ID(N~{S}.tblTransferenciasCustodia~) AND definition LIKE N~%recibo_sin_entrega%NoFirmaParte%~) AS CierreNuevo', N'{S}', @q);
SET @cuentas = REPLACE(@cuentas, N'~', N'''');
EXEC (@cuentas);

-- Resultado 2: el destino que quedo en cada viaje con destino anterior (un renglon por tipo de lugar): cuantas unidades tiene y el lugar historico que recibieron.
DECLARE @detalle nvarchar(max) = REPLACE(N'
SELECT p.Nombre AS DestinoDelViaje, l.Nombre AS LugarDeLasUnidades, l.Activo, COUNT(*) AS Unidades
FROM {S}.tblViajeVehiculos vv
JOIN {S}.tblViajes vj ON vj.Id = vv.ViajeId
JOIN {S}.tblPuntosCustodia p ON p.Id = vj.DestinoId
JOIN {S}.tblLugares l ON l.Id = vv.LugarDestinoId
GROUP BY p.Nombre, l.Nombre, l.Activo
ORDER BY p.Nombre', N'{S}', @q);
EXEC (@detalle);
