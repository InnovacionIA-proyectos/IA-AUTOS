/* =============================================================================
   Transferencia de custodia con firmante real, recibo con reservas y constancia de no firma, para una base de IA Autos YA EXISTENTE
   (con 07-inspecciones-salida-llegada.sql aplicado). Se ejecuta UNA vez (si ya esta aplicado, no hace nada) y todo ocurre en UNA
   transaccion: si algo falla se revierte completo.

   Que hace:
     1. tblTransferenciasCustodia: gana TipoCierre (firmada, con_reservas o no_firma), ReservasTexto, NoFirmaParte, MotivoNoFirma,
        TestigoUsuarioId, EvidenciaConstanciaId y, por cada firma, su fecha y su IP (FirmaEntregaAt, FirmaEntregaIp, FirmaRecibeAt,
        FirmaRecibeIp). Restricciones: cada tipo de cierre exige lo suyo (las dos firmas, o el motivo, el testigo y la foto de la
        constancia) y el testigo no puede ser quien entrega ni quien recibe.
     2. Datos historicos: las transferencias que ya tienen las dos firmas quedan con TipoCierre = 'firmada'; las que tienen una sola firma
        quedan abiertas (sin tipo de cierre) y la aplicacion exigira cerrarlas antes de iniciar la etapa siguiente. La fecha de cada firma
        historica es la de la firma completa (o la de la ultima modificacion) y su IP queda vacia (no se registraba).
     3. tblEvidencias: gana EsConstancia (0 en todas las historicas) para la foto de la constancia de no firma, que se puede registrar aunque
        la inspeccion este cerrada; el trigger TR_tblEvidencias_NoEnCerrada se actualiza para admitirla.

   Seguridad de los datos: no borra ni cambia firmas, inspecciones ni fotos. Aborta (sin cambiar nada) si falta la columna
   tblInspecciones.IniciadaAt (07-inspecciones-salida-llegada.sql).

   Como ejecutarlo: abrir en SSMS, DBeaver o sqlcmd CONECTADO a la base de IA Autos, ajustar @esquema y ejecutar completo con un
   usuario con permiso para alterar tablas y triggers (no el usuario de la aplicacion). Ejecutarlo ANTES de desplegar la API que usa la
   firma por titular, las reservas y la constancia de no firma.
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
IF OBJECT_ID(@q + N'.tblTransferenciasCustodia', N'U') IS NULL OR OBJECT_ID(@q + N'.tblEvidencias', N'U') IS NULL OR OBJECT_ID(@q + N'.tblUsuarios', N'U') IS NULL
    THROW 50001, N'Faltan tablas de IA Autos en el esquema indicado: revise @esquema.', 1;

IF COL_LENGTH(@q + N'.tblTransferenciasCustodia', N'TipoCierre') IS NOT NULL
BEGIN
    PRINT N'Las transferencias con firmante real, reservas y constancia ya estan aplicadas: no se hace nada.';
    RETURN;
END

IF COL_LENGTH(@q + N'.tblInspecciones', N'IniciadaAt') IS NULL
    THROW 50002, N'Falta tblInspecciones.IniciadaAt: ejecute antes 07-inspecciones-salida-llegada.sql.', 1;

-- Pasos en orden. {S} = esquema entre corchetes; ~ = comilla simple dentro del texto.
DECLARE @paso TABLE (n int IDENTITY(1, 1) PRIMARY KEY, descripcion nvarchar(100), s nvarchar(max));

INSERT @paso (descripcion, s) VALUES
(N'tblTransferenciasCustodia: columnas nuevas', N'
ALTER TABLE {S}.tblTransferenciasCustodia ADD
    FirmaEntregaAt        datetimeoffset(3) NULL,
    FirmaEntregaIp        varchar(45)       NULL,
    FirmaRecibeAt         datetimeoffset(3) NULL,
    FirmaRecibeIp         varchar(45)       NULL,
    TipoCierre            varchar(20)       NULL,
    ReservasTexto         nvarchar(1000)    NULL,
    NoFirmaParte          varchar(10)       NULL,
    MotivoNoFirma         nvarchar(1000)    NULL,
    TestigoUsuarioId      uniqueidentifier  NULL,
    EvidenciaConstanciaId uniqueidentifier  NULL'),
(N'tblTransferenciasCustodia: fecha de cada firma historica', N'
UPDATE {S}.tblTransferenciasCustodia
SET FirmaEntregaAt = CASE WHEN FirmaEntrega IS NOT NULL AND FirmaEntrega <> ~~ THEN ISNULL(FirmadaAt, UpdatedAt) END,
    FirmaRecibeAt  = CASE WHEN FirmaRecibe  IS NOT NULL AND FirmaRecibe  <> ~~ THEN ISNULL(FirmadaAt, UpdatedAt) END'),
(N'tblTransferenciasCustodia: las completas quedan firmadas', N'
UPDATE {S}.tblTransferenciasCustodia SET TipoCierre = ~firmada~
WHERE FirmaEntrega IS NOT NULL AND FirmaEntrega <> ~~ AND FirmaRecibe IS NOT NULL AND FirmaRecibe <> ~~'),
(N'tblTransferenciasCustodia: restricciones', N'
ALTER TABLE {S}.tblTransferenciasCustodia ADD CONSTRAINT FK_tblTransferencias_Testigo FOREIGN KEY (TestigoUsuarioId) REFERENCES {S}.tblUsuarios (Id);
ALTER TABLE {S}.tblTransferenciasCustodia ADD CONSTRAINT FK_tblTransferencias_Constancia FOREIGN KEY (EvidenciaConstanciaId) REFERENCES {S}.tblEvidencias (Id);
ALTER TABLE {S}.tblTransferenciasCustodia ADD CONSTRAINT CK_tblTransferencias_TipoCierre CHECK (TipoCierre IS NULL OR TipoCierre IN (~firmada~, ~con_reservas~, ~no_firma~));
ALTER TABLE {S}.tblTransferenciasCustodia ADD CONSTRAINT CK_tblTransferencias_Parte CHECK (NoFirmaParte IS NULL OR NoFirmaParte IN (~entrega~, ~recibe~));
ALTER TABLE {S}.tblTransferenciasCustodia ADD CONSTRAINT CK_tblTransferencias_Testigo CHECK (TestigoUsuarioId IS NULL OR (TestigoUsuarioId <> EntregaUsuarioId AND TestigoUsuarioId <> RecibeUsuarioId));
ALTER TABLE {S}.tblTransferenciasCustodia ADD CONSTRAINT CK_tblTransferencias_Cierre CHECK (
    TipoCierre IS NULL
    OR (TipoCierre = ~firmada~ AND FirmaEntrega IS NOT NULL AND FirmaEntrega <> ~~ AND FirmaRecibe IS NOT NULL AND FirmaRecibe <> ~~ AND ReservasTexto IS NULL)
    OR (TipoCierre = ~con_reservas~ AND FirmaEntrega IS NOT NULL AND FirmaEntrega <> ~~ AND FirmaRecibe IS NOT NULL AND FirmaRecibe <> ~~ AND ReservasTexto IS NOT NULL AND ReservasTexto <> ~~)
    OR (TipoCierre = ~no_firma~ AND NoFirmaParte IS NOT NULL AND MotivoNoFirma IS NOT NULL AND MotivoNoFirma <> ~~ AND TestigoUsuarioId IS NOT NULL AND EvidenciaConstanciaId IS NOT NULL))'),
(N'tblEvidencias: marca de constancia', N'ALTER TABLE {S}.tblEvidencias ADD EsConstancia bit NOT NULL CONSTRAINT DF_tblEvidencias_EsConstancia DEFAULT (0)'),
(N'trigger de evidencias: admite la foto de la constancia en una inspeccion cerrada', N'
CREATE OR ALTER TRIGGER {S}.TR_tblEvidencias_NoEnCerrada
ON {S}.tblEvidencias
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted i
               JOIN {S}.tblInspecciones n ON n.Id = i.InspeccionId
               WHERE n.Estado = ~cerrada~ AND i.EsConstancia = 0)
        THROW 51002, ~La inspeccion esta cerrada y es inmutable; no admite evidencias nuevas~, 1;
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

-- Resultado (esperado: firmadas + abiertas = transferencias; las abiertas son las que tienen una sola firma y hay que cerrar).
DECLARE @cuentas nvarchar(max) = REPLACE(N'
SELECT N~transferencias~ AS Dato, COUNT(*) AS Total FROM {S}.tblTransferenciasCustodia UNION ALL
SELECT N~transferencias firmadas~, COUNT(*) FROM {S}.tblTransferenciasCustodia WHERE TipoCierre = ~firmada~ UNION ALL
SELECT N~transferencias abiertas (sin tipo de cierre)~, COUNT(*) FROM {S}.tblTransferenciasCustodia WHERE TipoCierre IS NULL UNION ALL
SELECT N~fotos~, COUNT(*) FROM {S}.tblEvidencias UNION ALL
SELECT N~fotos de constancia~, COUNT(*) FROM {S}.tblEvidencias WHERE EsConstancia = 1', N'{S}', @q);
SET @cuentas = REPLACE(@cuentas, N'~', N'''');
EXEC (@cuentas);
