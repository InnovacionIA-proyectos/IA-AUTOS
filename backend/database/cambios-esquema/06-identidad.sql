/* =============================================================================
   Identidad (cedula y PIN) y clientes, para una base de IA Autos YA EXISTENTE (con 05-viajes.sql aplicado).
   Se ejecuta UNA vez (si ya esta aplicado, no hace nada) y todo ocurre en UNA transaccion: si algo falla se revierte completo.

   Que hace:
     1. Crea tblClientes (catalogo: Codigo, Nombre, Orden, Activo) con el cliente actual: Sofasa.
     2. tblUsuarios: el correo pasa a ser opcional (unico solo entre quienes lo tienen) y se agregan PinHash, TipoCredencial,
        DebeCambiarPin, IntentosFallidos, BloqueadoHasta, UltimoAccesoAt y ClienteId. Todos los usuarios existentes quedan con
        TipoCredencial 'password' (siguen entrando con su correo y contrasena) y con el cliente actual; el super administrador queda
        sin cliente porque ve todos.
     3. tblVehiculos: agrega ClienteId (obligatorio): todos los vehiculos existentes quedan con el cliente actual.
     4. Reglas en la base: quien entra con contrasena tiene correo; quien entra con PIN tiene cedula.

   Seguridad de los datos: no borra ni cambia correos, contrasenas ni vehiculos. Aborta (sin cambiar nada) si falta la columna
   tblUsuarios.Cedula (05-viajes.sql) o la restriccion UQ_tblUsuarios_Email que se reemplaza.

   Como ejecutarlo: abrir en SSMS, DBeaver o sqlcmd CONECTADO a la base de IA Autos, ajustar @esquema y ejecutar completo con un
   usuario con permiso para crear tablas e indices (no el usuario de la aplicacion). Luego dar permisos a la aplicacion si no los
   tiene por esquema:
     GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::<esquema> TO <usuario_de_la_app>;
   Ejecutarlo ANTES de desplegar la API que usa PIN y clientes.
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
IF OBJECT_ID(@q + N'.tblUsuarios', N'U') IS NULL OR OBJECT_ID(@q + N'.tblVehiculos', N'U') IS NULL
    THROW 50001, N'Faltan tablas de IA Autos en el esquema indicado: revise @esquema.', 1;

IF OBJECT_ID(@q + N'.tblClientes', N'U') IS NOT NULL
BEGIN
    PRINT N'La identidad y los clientes ya estan aplicados: no se hace nada.';
    RETURN;
END

IF COL_LENGTH(@q + N'.tblUsuarios', N'Cedula') IS NULL
    THROW 50002, N'Falta tblUsuarios.Cedula: ejecute antes 05-viajes.sql.', 1;
IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = N'UQ_tblUsuarios_Email' AND parent_object_id = OBJECT_ID(@q + N'.tblUsuarios'))
    THROW 50003, N'No existe la restriccion UQ_tblUsuarios_Email: la base no esta en el estado previo esperado para esta migracion.', 1;

-- Pasos en orden. {S} = esquema entre corchetes; ~ = comilla simple dentro del texto.
DECLARE @paso TABLE (n int IDENTITY(1, 1) PRIMARY KEY, descripcion nvarchar(100), s nvarchar(max));

INSERT @paso (descripcion, s) VALUES
(N'tblClientes', N'
CREATE TABLE {S}.tblClientes
(
    Id         uniqueidentifier  NOT NULL CONSTRAINT PK_tblClientes PRIMARY KEY,
    Codigo     varchar(30)       NOT NULL,
    Nombre     nvarchar(100)     NOT NULL,
    Orden      int               NOT NULL CONSTRAINT DF_tblClientes_Orden DEFAULT (0),
    Activo     bit               NOT NULL CONSTRAINT DF_tblClientes_Activo DEFAULT (1),
    CreatedAt  datetimeoffset(3) NOT NULL CONSTRAINT DF_tblClientes_CreatedAt DEFAULT (SYSDATETIMEOFFSET()),
    UpdatedAt  datetimeoffset(3) NOT NULL CONSTRAINT DF_tblClientes_UpdatedAt DEFAULT (SYSDATETIMEOFFSET()),
    CONSTRAINT UQ_tblClientes_Codigo UNIQUE (Codigo)
)'),
(N'tblClientes: cliente actual (Sofasa)', N'INSERT INTO {S}.tblClientes (Id, Codigo, Nombre, Orden) VALUES (NEWID(), ~sofasa~, N~Sofasa~, 1)'),
(N'tblUsuarios: la restriccion unica del correo se reemplaza', N'ALTER TABLE {S}.tblUsuarios DROP CONSTRAINT UQ_tblUsuarios_Email'),
(N'tblUsuarios: el correo es opcional', N'ALTER TABLE {S}.tblUsuarios ALTER COLUMN Email nvarchar(320) NULL'),
(N'tblUsuarios: correo unico entre quienes lo tienen', N'CREATE UNIQUE INDEX UX_tblUsuarios_Email ON {S}.tblUsuarios (Email) WHERE Email IS NOT NULL'),
(N'tblUsuarios: credencial, bloqueo y cliente', N'
ALTER TABLE {S}.tblUsuarios ADD
    PinHash          nvarchar(400)     NULL,
    TipoCredencial   varchar(20)       NOT NULL CONSTRAINT DF_tblUsuarios_TipoCredencial DEFAULT (~password~),
    DebeCambiarPin   bit               NOT NULL CONSTRAINT DF_tblUsuarios_DebeCambiarPin DEFAULT (0),
    IntentosFallidos int               NOT NULL CONSTRAINT DF_tblUsuarios_IntentosFallidos DEFAULT (0),
    BloqueadoHasta   datetimeoffset(3) NULL,
    UltimoAccesoAt   datetimeoffset(3) NULL,
    ClienteId        uniqueidentifier  NULL'),
(N'tblUsuarios: tipo de credencial valido', N'ALTER TABLE {S}.tblUsuarios ADD CONSTRAINT CK_tblUsuarios_TipoCredencial CHECK (TipoCredencial IN (~password~, ~pin~))'),
(N'tblUsuarios: contrasena exige correo y PIN exige cedula', N'
ALTER TABLE {S}.tblUsuarios ADD CONSTRAINT CK_tblUsuarios_Identidad CHECK
    ((TipoCredencial = ~password~ AND Email IS NOT NULL) OR (TipoCredencial = ~pin~ AND Cedula IS NOT NULL))'),
(N'tblUsuarios: cliente actual para todos menos el super administrador', N'
UPDATE {S}.tblUsuarios SET ClienteId = (SELECT TOP 1 Id FROM {S}.tblClientes WHERE Codigo = ~sofasa~) WHERE Rol <> ~super_admin~'),
(N'tblUsuarios: llave foranea e indice del cliente', N'
ALTER TABLE {S}.tblUsuarios ADD CONSTRAINT FK_tblUsuarios_Cliente FOREIGN KEY (ClienteId) REFERENCES {S}.tblClientes (Id);
CREATE INDEX IX_tblUsuarios_Cliente ON {S}.tblUsuarios (ClienteId)'),
(N'tblVehiculos: ClienteId', N'ALTER TABLE {S}.tblVehiculos ADD ClienteId uniqueidentifier NULL'),
(N'tblVehiculos: cliente actual para todos', N'UPDATE {S}.tblVehiculos SET ClienteId = (SELECT TOP 1 Id FROM {S}.tblClientes WHERE Codigo = ~sofasa~)'),
(N'tblVehiculos: ClienteId obligatorio', N'ALTER TABLE {S}.tblVehiculos ALTER COLUMN ClienteId uniqueidentifier NOT NULL'),
(N'tblVehiculos: llave foranea e indice del cliente', N'
ALTER TABLE {S}.tblVehiculos ADD CONSTRAINT FK_tblVehiculos_Cliente FOREIGN KEY (ClienteId) REFERENCES {S}.tblClientes (Id);
CREATE INDEX IX_tblVehiculos_Cliente ON {S}.tblVehiculos (ClienteId)');

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

-- Resultado (esperado: 0 vehiculos sin cliente; los usuarios sin cliente son solo super administradores).
DECLARE @cuentas nvarchar(max) = REPLACE(N'
SELECT N~clientes~ AS Dato, COUNT(*) AS Total FROM {S}.tblClientes UNION ALL
SELECT N~usuarios~, COUNT(*) FROM {S}.tblUsuarios UNION ALL
SELECT N~usuarios con cliente~, COUNT(*) FROM {S}.tblUsuarios WHERE ClienteId IS NOT NULL UNION ALL
SELECT N~usuarios sin cliente (super administradores)~, COUNT(*) FROM {S}.tblUsuarios WHERE ClienteId IS NULL UNION ALL
SELECT N~usuarios que entran con contrasena~, COUNT(*) FROM {S}.tblUsuarios WHERE TipoCredencial = ~password~ UNION ALL
SELECT N~vehiculos~, COUNT(*) FROM {S}.tblVehiculos UNION ALL
SELECT N~vehiculos sin cliente~, COUNT(*) FROM {S}.tblVehiculos WHERE ClienteId IS NULL', N'{S}', @q);
SET @cuentas = REPLACE(@cuentas, N'~', N'''');
EXEC (@cuentas);
