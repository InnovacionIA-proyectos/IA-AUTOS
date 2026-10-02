/* =============================================================================
   IA-AUTOS - Creacion de la base de datos (SQL Server 2019)

   Uso (desarrollo / QA):
     sqlcmd -S <servidor> -U <usuario_admin> -P <password> -I -b -i 00-crear-base.sql

   En produccion la base normalmente la crea el DBA; en ese caso omita este
   archivo y ejecute ddl.sql directamente sobre la base destino (-d <base>).

   Es idempotente: si la base ya existe no la modifica, salvo los ajustes de
   aislamiento (READ_COMMITTED_SNAPSHOT) que se reaplican.
   ============================================================================= */
SET NOCOUNT ON;
GO

IF DB_ID(N'IAAutos') IS NULL
BEGIN
    -- Colacion insensible a mayusculas y sensible a acentos (VIN y correo se comparan sin distinguir mayusculas).
    CREATE DATABASE [IAAutos] COLLATE Modern_Spanish_CI_AS;
END
GO

-- Nivel de compatibilidad de SQL Server 2019.
ALTER DATABASE [IAAutos] SET COMPATIBILITY_LEVEL = 150;
GO

-- Las lecturas no bloquean a las escrituras (versionado de filas en lugar de bloqueos compartidos).
ALTER DATABASE [IAAutos] SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;
GO

PRINT 'Base IAAutos lista.';
GO
