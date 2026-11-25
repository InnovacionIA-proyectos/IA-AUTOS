/* =============================================================================
   Descartar un informe en borrador, para una base de IA Autos YA EXISTENTE. Se ejecuta UNA vez (si ya esta aplicado, no cambia nada) y todo ocurre en UNA transaccion: si algo falla se revierte completo.

   Que hace:
     1. tblInspecciones.Estado admite dos estados nuevos, descartada (el borrador que su autor o el coordinador de calidad descarto) y superada (el borrador de otra persona que dejo de ser el informe de su momento porque
        quien tomo el carro lo capturo de cero), ademas de borrador y cerrada. CK_tblInspecciones_Cierre acepta los dos estados nuevos sin fecha de cierre: una descartada o superada nunca se cerro.
     2. UX_tblInspecciones_Etapa_Momento_Original (una inspeccion original por etapa, momento y ciclo) y UX_tblInspecciones_Corrige (cada version se corrige una sola vez) ya no cuentan las inspecciones descartadas ni
        superadas: el momento queda libre para una inspeccion original nueva y la version corregida puede corregirse de nuevo.

   Seguridad de los datos: no borra ni cambia ningun dato; solo amplia dos restricciones y reescribe dos indices unicos para que dejen de contar los estados nuevos (las inspecciones que ya existen son borrador o cerrada y siguen
   contando igual). Se ejecuta ANTES de desplegar la API.

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
IF OBJECT_ID(@q + N'.tblInspecciones', N'U') IS NULL OR COL_LENGTH(@q + N'.tblInspecciones', N'Ciclo') IS NULL
    THROW 50001, N'Falta tblInspecciones (con su columna Ciclo) de IA Autos en el esquema indicado: revise @esquema.', 1;

-- Pasos en orden. {S} = esquema entre corchetes; ~ = comilla simple dentro del texto.
DECLARE @paso TABLE (n int IDENTITY(1, 1) PRIMARY KEY, descripcion nvarchar(100), s nvarchar(max));

INSERT @paso (descripcion, s) VALUES
(N'tblInspecciones: estados descartada y superada', N'
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N~CK_tblInspecciones_Estado~ AND parent_object_id = OBJECT_ID(N~{S}.tblInspecciones~) AND definition LIKE N~%descartada%~)
BEGIN
    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N~CK_tblInspecciones_Estado~ AND parent_object_id = OBJECT_ID(N~{S}.tblInspecciones~))
        ALTER TABLE {S}.tblInspecciones DROP CONSTRAINT CK_tblInspecciones_Estado;
    ALTER TABLE {S}.tblInspecciones ADD CONSTRAINT CK_tblInspecciones_Estado CHECK (Estado IN (~borrador~, ~cerrada~, ~descartada~, ~superada~));
END'),
(N'tblInspecciones: solo la cerrada tiene fecha de cierre', N'
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N~CK_tblInspecciones_Cierre~ AND parent_object_id = OBJECT_ID(N~{S}.tblInspecciones~) AND definition LIKE N~%descartada%~)
BEGIN
    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N~CK_tblInspecciones_Cierre~ AND parent_object_id = OBJECT_ID(N~{S}.tblInspecciones~))
        ALTER TABLE {S}.tblInspecciones DROP CONSTRAINT CK_tblInspecciones_Cierre;
    ALTER TABLE {S}.tblInspecciones ADD CONSTRAINT CK_tblInspecciones_Cierre CHECK ((Estado IN (~borrador~, ~descartada~, ~superada~) AND CerradaAt IS NULL) OR (Estado = ~cerrada~ AND CerradaAt IS NOT NULL));
END'),
(N'tblInspecciones: la original unica no cuenta las descartadas ni las superadas', N'
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N~UX_tblInspecciones_Etapa_Momento_Original~ AND object_id = OBJECT_ID(N~{S}.tblInspecciones~) AND filter_definition NOT LIKE N~%descartada%~)
    DROP INDEX UX_tblInspecciones_Etapa_Momento_Original ON {S}.tblInspecciones;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N~UX_tblInspecciones_Etapa_Momento_Original~ AND object_id = OBJECT_ID(N~{S}.tblInspecciones~))
    CREATE UNIQUE INDEX UX_tblInspecciones_Etapa_Momento_Original ON {S}.tblInspecciones (EtapaId, Momento, Ciclo) WHERE CorrigeInspeccionId IS NULL AND Estado <> ~descartada~ AND Estado <> ~superada~'),
(N'tblInspecciones: la correccion unica no cuenta las descartadas ni las superadas', N'
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N~UX_tblInspecciones_Corrige~ AND object_id = OBJECT_ID(N~{S}.tblInspecciones~) AND filter_definition NOT LIKE N~%descartada%~)
    DROP INDEX UX_tblInspecciones_Corrige ON {S}.tblInspecciones;
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N~UX_tblInspecciones_Corrige~ AND object_id = OBJECT_ID(N~{S}.tblInspecciones~))
    CREATE UNIQUE INDEX UX_tblInspecciones_Corrige ON {S}.tblInspecciones (CorrigeInspeccionId) WHERE CorrigeInspeccionId IS NOT NULL AND Estado <> ~descartada~ AND Estado <> ~superada~');

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

-- Resultado (esperado: EstadosNuevos = 1, CierreSoloDeLaCerrada = 1, OriginalSinDescartadas = 1, CorreccionSinDescartadas = 1).
DECLARE @cuentas nvarchar(max) = REPLACE(N'
SELECT
    (SELECT COUNT(*) FROM sys.check_constraints WHERE name = N~CK_tblInspecciones_Estado~ AND parent_object_id = OBJECT_ID(N~{S}.tblInspecciones~) AND definition LIKE N~%descartada%~ AND definition LIKE N~%superada%~) AS EstadosNuevos,
    (SELECT COUNT(*) FROM sys.check_constraints WHERE name = N~CK_tblInspecciones_Cierre~ AND parent_object_id = OBJECT_ID(N~{S}.tblInspecciones~) AND definition LIKE N~%descartada%~) AS CierreSoloDeLaCerrada,
    (SELECT COUNT(*) FROM sys.indexes WHERE name = N~UX_tblInspecciones_Etapa_Momento_Original~ AND object_id = OBJECT_ID(N~{S}.tblInspecciones~) AND is_unique = 1 AND filter_definition LIKE N~%descartada%~ AND filter_definition LIKE N~%superada%~) AS OriginalSinDescartadas,
    (SELECT COUNT(*) FROM sys.indexes WHERE name = N~UX_tblInspecciones_Corrige~ AND object_id = OBJECT_ID(N~{S}.tblInspecciones~) AND is_unique = 1 AND filter_definition LIKE N~%descartada%~ AND filter_definition LIKE N~%superada%~) AS CorreccionSinDescartadas;', N'{S}', @q);
SET @cuentas = REPLACE(@cuentas, N'~', N'''');
EXEC (@cuentas);
