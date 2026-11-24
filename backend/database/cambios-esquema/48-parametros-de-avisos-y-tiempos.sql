/* =============================================================================
   Los parametros de los tiempos y los avisos, el plazo guardado en cada permiso "No hay inspector", la precision del GPS con que se fijo la ubicacion de un lugar y el aviso de la ubicacion fijada, para una base de IA Autos YA
   EXISTENTE. Se ejecuta UNA vez (si ya esta aplicado, no cambia nada) y todo ocurre en UNA transaccion: si algo falla se revierte completo.

   Que hace:
     1. tblParametros: las seis filas permiso_sin_inspector_minutos (10), espera_inspector_aviso_minutos (15), espera_inspector_plan_b_minutos (30), viaje_sin_movimiento_horas (24), firma_cargue_pendiente_minutos (30) y
        propuesta_ubicacion_visitas (3). Una fila que ya existe NUNCA se sobrescribe.
     2. tblPermisosSinInspector.VenceAt: el plazo de cada solicitud, guardado al nacer (la fecha de la solicitud mas la espera vigente ese dia). Los permisos que ya existian reciben su fecha mas 10 minutos, que era su plazo.
        CK_tblPermisosSinInspector_Reloj deja de fijar 10 minutos: el reloj nunca concede antes del plazo guardado en la solicitud (VenceAt); una restriccion nueva exige que el plazo no sea anterior a la solicitud.
     3. tblLugares.PrecisionM: la precision, en metros, del GPS con que se fijo la ubicacion "aqui" (nula en los lugares fijados antes, o cargados a mano) y su restriccion de no negativa.
     4. tblNotificaciones: CK_tblNotificaciones_Tipo admite el tipo ubicacion_fijada, sin quitar ninguno de los diecisiete existentes.

   Seguridad de los datos: solo agrega filas, columnas, restricciones y un tipo de aviso; la unica modificacion de datos es dar a cada permiso que ya existia su plazo (la fecha de la solicitud mas 10 minutos). Se ejecuta ANTES de
   desplegar la API.

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
IF OBJECT_ID(@q + N'.tblParametros', N'U') IS NULL OR OBJECT_ID(@q + N'.tblPermisosSinInspector', N'U') IS NULL OR OBJECT_ID(@q + N'.tblLugares', N'U') IS NULL OR OBJECT_ID(@q + N'.tblNotificaciones', N'U') IS NULL
    THROW 50001, N'Faltan tblParametros, tblPermisosSinInspector, tblLugares o tblNotificaciones de IA Autos en el esquema indicado: revise @esquema.', 1;

-- Pasos en orden. {S} = esquema entre corchetes; ~ = comilla simple dentro del texto.
DECLARE @paso TABLE (n int IDENTITY(1, 1) PRIMARY KEY, descripcion nvarchar(100), s nvarchar(max));

INSERT @paso (descripcion, s) VALUES
(N'tblParametros: los parametros de los tiempos y los avisos', N'
INSERT INTO {S}.tblParametros (Clave, Valor, Tipo, Descripcion)
SELECT v.Clave, v.Valor, v.Tipo, v.Descripcion
FROM (VALUES
    (~permiso_sin_inspector_minutos~,   N~10~, ~numero~, N~Minutos que espera el permiso de la excepcion No hay inspector del cargue, sin respuesta de nadie, antes de que el sistema lo conceda por tiempo (cada solicitud guarda el plazo con que nacio)~),
    (~espera_inspector_aviso_minutos~,  N~15~, ~numero~, N~Minutos desde que el conductor activa la excepcion No hay inspector en un destino, sin que nadie tome la entrega, antes de avisar al coordinador~),
    (~espera_inspector_plan_b_minutos~, N~30~, ~numero~, N~Minutos adicionales, despues de la espera del aviso, para habilitar la entrega sin inspector del punto~),
    (~viaje_sin_movimiento_horas~,      N~24~, ~numero~, N~Horas que un viaje en curso puede seguir sin movimiento antes de avisar al coordinador~),
    (~firma_cargue_pendiente_minutos~,  N~30~, ~numero~, N~Minutos que la salida del cargue de un carro puede seguir cerrada sin la firma de una parte de la ninera antes de avisarle a esa parte~),
    (~propuesta_ubicacion_visitas~,     N~3~,  ~numero~, N~Visitas distintas con fotos selladas a un lugar sin ubicacion confirmada que hacen falta para proponer su ubicacion~)
) AS v (Clave, Valor, Tipo, Descripcion)
WHERE NOT EXISTS (SELECT 1 FROM {S}.tblParametros p WHERE p.Clave = v.Clave)'),
(N'tblPermisosSinInspector: VenceAt (el plazo de cada solicitud)', N'
IF COL_LENGTH(N~{S}.tblPermisosSinInspector~, N~VenceAt~) IS NULL
    ALTER TABLE {S}.tblPermisosSinInspector ADD VenceAt datetimeoffset(3) NULL'),
(N'tblPermisosSinInspector: el plazo de los permisos que ya existian', N'
UPDATE {S}.tblPermisosSinInspector SET VenceAt = DATEADD(MINUTE, 10, CreatedAt) WHERE VenceAt IS NULL'),
(N'tblPermisosSinInspector: VenceAt obligatorio', N'
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N~{S}.tblPermisosSinInspector~) AND name = N~VenceAt~ AND is_nullable = 1)
    ALTER TABLE {S}.tblPermisosSinInspector ALTER COLUMN VenceAt datetimeoffset(3) NOT NULL'),
(N'tblPermisosSinInspector: el reloj no concede antes del plazo guardado', N'
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N~CK_tblPermisosSinInspector_Reloj~ AND parent_object_id = OBJECT_ID(N~{S}.tblPermisosSinInspector~) AND definition LIKE N~%VenceAt%~)
BEGIN
    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N~CK_tblPermisosSinInspector_Reloj~ AND parent_object_id = OBJECT_ID(N~{S}.tblPermisosSinInspector~))
        ALTER TABLE {S}.tblPermisosSinInspector DROP CONSTRAINT CK_tblPermisosSinInspector_Reloj;
    ALTER TABLE {S}.tblPermisosSinInspector ADD CONSTRAINT CK_tblPermisosSinInspector_Reloj CHECK (PorTiempo = 0 OR ConcedidoAt >= VenceAt);
END'),
(N'tblPermisosSinInspector: el plazo no es anterior a la solicitud', N'
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N~CK_tblPermisosSinInspector_Plazo~ AND parent_object_id = OBJECT_ID(N~{S}.tblPermisosSinInspector~))
    ALTER TABLE {S}.tblPermisosSinInspector ADD CONSTRAINT CK_tblPermisosSinInspector_Plazo CHECK (VenceAt >= CreatedAt)'),
(N'tblLugares: PrecisionM', N'
IF COL_LENGTH(N~{S}.tblLugares~, N~PrecisionM~) IS NULL
    ALTER TABLE {S}.tblLugares ADD PrecisionM float NULL'),
(N'tblLugares: la precision no es negativa', N'
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N~CK_tblLugares_Precision~ AND parent_object_id = OBJECT_ID(N~{S}.tblLugares~))
    ALTER TABLE {S}.tblLugares ADD CONSTRAINT CK_tblLugares_Precision CHECK (PrecisionM IS NULL OR PrecisionM >= 0)'),
(N'tblNotificaciones: tipo ubicacion_fijada', N'
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N~CK_tblNotificaciones_Tipo~ AND parent_object_id = OBJECT_ID(N~{S}.tblNotificaciones~) AND definition LIKE N~%ubicacion_fijada%~)
BEGIN
    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N~CK_tblNotificaciones_Tipo~ AND parent_object_id = OBJECT_ID(N~{S}.tblNotificaciones~))
        ALTER TABLE {S}.tblNotificaciones DROP CONSTRAINT CK_tblNotificaciones_Tipo;
    ALTER TABLE {S}.tblNotificaciones ADD CONSTRAINT CK_tblNotificaciones_Tipo CHECK (Tipo IN (~novedad_conductor~, ~respuesta_conductor~, ~hallazgo_critico~, ~umbral_patron~, ~captura_incompleta~, ~transferencia_pendiente~, ~reporte_diario~, ~novedad_proceso~, ~reclamo_sin_actividad~, ~fuera_de_rango~, ~cargue_sin_reclamar~, ~propuesta_ubicacion~, ~permiso_sin_inspector~, ~firma_cargue_pendiente~, ~cambio_cargue_inspector~, ~cambio_destino_unidad~, ~carro_rechazado_en_destino~, ~ubicacion_fijada~));
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

-- Resultado (esperado: Parametros = 6, PermisosSinPlazo = 0, PlazoObligatorio = 1, RelojPorPlazo = 1, PlazoNoAnterior = 1, PrecisionDelLugar = 1, TipoDeAviso = 1).
DECLARE @cuentas nvarchar(max) = REPLACE(N'
SELECT
    (SELECT COUNT(*) FROM {S}.tblParametros WHERE Clave IN (N~permiso_sin_inspector_minutos~, N~espera_inspector_aviso_minutos~, N~espera_inspector_plan_b_minutos~, N~viaje_sin_movimiento_horas~, N~firma_cargue_pendiente_minutos~, N~propuesta_ubicacion_visitas~)) AS Parametros,
    (SELECT COUNT(*) FROM {S}.tblPermisosSinInspector WHERE VenceAt IS NULL) AS PermisosSinPlazo,
    (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N~{S}.tblPermisosSinInspector~) AND name = N~VenceAt~ AND is_nullable = 0) AS PlazoObligatorio,
    (SELECT COUNT(*) FROM sys.check_constraints WHERE name = N~CK_tblPermisosSinInspector_Reloj~ AND parent_object_id = OBJECT_ID(N~{S}.tblPermisosSinInspector~) AND definition LIKE N~%VenceAt%~) AS RelojPorPlazo,
    (SELECT COUNT(*) FROM sys.check_constraints WHERE name = N~CK_tblPermisosSinInspector_Plazo~ AND parent_object_id = OBJECT_ID(N~{S}.tblPermisosSinInspector~)) AS PlazoNoAnterior,
    (SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N~{S}.tblLugares~) AND name = N~PrecisionM~) AS PrecisionDelLugar,
    (SELECT COUNT(*) FROM sys.check_constraints WHERE name = N~CK_tblNotificaciones_Tipo~ AND parent_object_id = OBJECT_ID(N~{S}.tblNotificaciones~) AND definition LIKE N~%ubicacion_fijada%~) AS TipoDeAviso;', N'{S}', @q);
SET @cuentas = REPLACE(@cuentas, N'~', N'''');
EXEC (@cuentas);
