-- =============================================================================
-- IA-AUTOS - Migracion PostgreSQL -> SQL Server 2019
-- Paso 1: genera un script T-SQL (INSERT) con los datos de la base ORIGEN.
--
--   psql "<cadena_origen>" -X -q -v ON_ERROR_STOP=1 [-v reset_passwords=1] \
--        -f 01-generar-inserts-postgres.sql -o ../export/datos.sql
--
--   reset_passwords=1  exporta PasswordHash en NULL: nadie podra ingresar hasta que un
--                      administrador asigne contrasenas nuevas (recomendado si la contrasena de
--                      desarrollo de la migracion 20260626130000 sigue vigente en algun usuario).
--
-- El archivo generado contiene datos de personas y hashes de contrasenas: tratelo como SECRETO
-- (carpeta database/export/ excluida de git), cargue y borrelo.
-- Cargarlo en el destino (ver 02-cargar-sqlserver.md):
--   sqlcmd -S <servidor> -d <base> -U <usuario> -P <password> -I -b -f 65001 -i datos.sql
-- =============================================================================
\set QUIET on
\pset tuples_only on
\pset format unaligned
\pset footer off
\if :{?reset_passwords}
\else
\set reset_passwords 0
\endif

create or replace function pg_temp.lit(t text) returns text language sql immutable as
$$ select case when t is null then 'NULL' else 'N''' || replace(t, '''', '''''') || '''' end $$;

create or replace function pg_temp.ts(t timestamptz) returns text language sql immutable as
$$ select case when t is null then 'NULL'
          else 'CAST(''' || to_char(t at time zone 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.MS"Z"') || ''' AS datetimeoffset(3))' end $$;

create or replace function pg_temp.num(n numeric) returns text language sql immutable as
$$ select case when n is null then 'NULL' else n::text end $$;

create or replace function pg_temp.guid(u uuid) returns text language sql immutable as
$$ select case when u is null then 'NULL' else 'CAST(''' || u::text || ''' AS uniqueidentifier)' end $$;

create or replace function pg_temp.bit(b boolean) returns text language sql immutable as
$$ select case when b is null then 'NULL' when b then '1' else '0' end $$;

select '/* IA-AUTOS: datos migrados desde PostgreSQL. Generado ' || to_char(now() at time zone 'UTC', 'YYYY-MM-DD HH24:MI') || ' UTC */';
select 'SET NOCOUNT ON; SET QUOTED_IDENTIFIER ON; SET XACT_ABORT ON;';
select 'GO';

-- Proteccion: la carga es inicial. Aborta si el destino ya tiene datos de negocio.
select $$IF EXISTS (SELECT 1 FROM dbo.tblUsuarios) OR EXISTS (SELECT 1 FROM dbo.tblVehiculos) OR EXISTS (SELECT 1 FROM dbo.tblEtapas)
    THROW 53000, 'El destino ya contiene datos: esta carga es solo para una base vacia', 1;$$;
select 'GO';

-- Los triggers de inmutabilidad se deshabilitan durante la carga historica y se rehabilitan al final.
select 'DISABLE TRIGGER dbo.TR_tblInspecciones_Inmutable ON dbo.tblInspecciones;';
select 'DISABLE TRIGGER dbo.TR_tblHallazgos_NoEnCerrada ON dbo.tblHallazgos;';
select 'DISABLE TRIGGER dbo.TR_tblEvidencias_NoEnCerrada ON dbo.tblEvidencias;';
select 'GO';

-- El catalogo de puntos de custodia conserva los ids originales (las etapas los referencian).
select 'DELETE FROM dbo.tblPuntosCustodia;';
select 'GO';

-- Cada tabla: un INSERT por fila y un GO cada 500 filas.
-- ---- tblPuntosCustodia
select stmt || case when n % 500 = 0 then E'\nGO' else '' end
from (select row_number() over (order by nombre) n,
             'INSERT dbo.tblPuntosCustodia (Id,Nombre,CreatedAt,UpdatedAt) VALUES (' ||
             pg_temp.guid(id) || ',' || pg_temp.lit(nombre::text) || ',' || pg_temp.ts(created_at) || ',' || pg_temp.ts(updated_at) || ');' as stmt
      from puntos_custodia) x order by n;
select 'GO';

-- ---- tblUsuarios
select stmt || case when n % 500 = 0 then E'\nGO' else '' end
from (select row_number() over (order by created_at, id) n,
             'INSERT dbo.tblUsuarios (Id,Nombre,Email,Rol,SubtipoInspector,Activo,PasswordHash,CreatedAt,UpdatedAt) VALUES (' ||
             pg_temp.guid(id) || ',' || pg_temp.lit(nombre) || ',' || pg_temp.lit(lower(email)) || ',' || pg_temp.lit(rol::text) || ',' ||
             pg_temp.lit(subtipo_inspector::text) || ',' || pg_temp.bit(activo) || ',' ||
             case when :reset_passwords = 1 then 'NULL' else pg_temp.lit(password_hash) end || ',' ||
             pg_temp.ts(created_at) || ',' || pg_temp.ts(updated_at) || ');' as stmt
      from usuarios) x order by n;
select 'GO';

-- ---- tblVehiculos
select stmt || case when n % 500 = 0 then E'\nGO' else '' end
from (select row_number() over (order by created_at, id) n,
             'INSERT dbo.tblVehiculos (Id,Vin,Marca,TipoVehiculo,Modelo,Chasis,Color,Operacion,CreatedAt,UpdatedAt) VALUES (' ||
             pg_temp.guid(id) || ',' || pg_temp.lit(vin) || ',' || pg_temp.lit(marca) || ',' || pg_temp.lit(tipo_vehiculo) || ',' ||
             pg_temp.lit(modelo) || ',' || pg_temp.lit(chasis) || ',' || pg_temp.lit(color) || ',' || pg_temp.lit(operacion::text) || ',' ||
             pg_temp.ts(created_at) || ',' || pg_temp.ts(updated_at) || ');' as stmt
      from vehiculos) x order by n;
select 'GO';

-- ---- tblEtapas
select stmt || case when n % 500 = 0 then E'\nGO' else '' end
from (select row_number() over (order by created_at, id) n,
             'INSERT dbo.tblEtapas (Id,VehiculoId,PuntoCustodiaId,Orden,RequiereInspeccion,CreatedAt,UpdatedAt) VALUES (' ||
             pg_temp.guid(id) || ',' || pg_temp.guid(vehiculo_id) || ',' || pg_temp.guid(punto_custodia_id) || ',' || orden || ',' ||
             pg_temp.bit(requiere_inspeccion) || ',' || pg_temp.ts(created_at) || ',' || pg_temp.ts(updated_at) || ');' as stmt
      from etapas) x order by n;
select 'GO';

-- ---- tblInspecciones
select stmt || case when n % 500 = 0 then E'\nGO' else '' end
from (select row_number() over (order by created_at, id) n,
             'INSERT dbo.tblInspecciones (Id,EtapaId,UsuarioId,Momento,Fecha,Lat,Lng,Estado,ConductorNombre,Placa,Trailer,Notas,CreatedAt,UpdatedAt) VALUES (' ||
             pg_temp.guid(id) || ',' || pg_temp.guid(etapa_id) || ',' || pg_temp.guid(usuario_id) || ',' || pg_temp.lit(momento::text) || ',' ||
             pg_temp.ts(fecha) || ',' || pg_temp.num(lat) || ',' || pg_temp.num(lng) || ',' || pg_temp.lit(estado::text) || ',' ||
             pg_temp.lit(conductor_nombre) || ',' || pg_temp.lit(placa) || ',' || pg_temp.lit(trailer) || ',' || pg_temp.lit(notas) || ',' ||
             pg_temp.ts(created_at) || ',' || pg_temp.ts(updated_at) || ');' as stmt
      from inspecciones) x order by n;
select 'GO';

-- ---- tblHallazgos
select stmt || case when n % 500 = 0 then E'\nGO' else '' end
from (select row_number() over (order by created_at, id) n,
             'INSERT dbo.tblHallazgos (Id,InspeccionId,ParteVehiculo,ClaseDano,TamanoDano,CoordsDiagrama,CreatedAt,UpdatedAt) VALUES (' ||
             pg_temp.guid(id) || ',' || pg_temp.guid(inspeccion_id) || ',' || pg_temp.lit(parte_vehiculo::text) || ',' ||
             pg_temp.lit(clase_dano::text) || ',' || pg_temp.lit(tamano_dano::text) || ',' || pg_temp.lit(coords_diagrama::text) || ',' ||
             pg_temp.ts(created_at) || ',' || pg_temp.ts(updated_at) || ');' as stmt
      from hallazgos) x order by n;
select 'GO';

-- ---- tblEvidencias
select stmt || case when n % 500 = 0 then E'\nGO' else '' end
from (select row_number() over (order by created_at, id) n,
             'INSERT dbo.tblEvidencias (Id,InspeccionId,HallazgoId,StoragePath,SelloFecha,SelloLat,SelloLng,CreatedAt,UpdatedAt) VALUES (' ||
             pg_temp.guid(id) || ',' || pg_temp.guid(inspeccion_id) || ',' || pg_temp.guid(hallazgo_id) || ',' || pg_temp.lit(storage_path) || ',' ||
             pg_temp.ts(sello_fecha) || ',' || pg_temp.num(sello_lat) || ',' || pg_temp.num(sello_lng) || ',' ||
             pg_temp.ts(created_at) || ',' || pg_temp.ts(updated_at) || ');' as stmt
      from evidencias) x order by n;
select 'GO';

-- ---- tblTransferenciasCustodia
select stmt || case when n % 500 = 0 then E'\nGO' else '' end
from (select row_number() over (order by created_at, id) n,
             'INSERT dbo.tblTransferenciasCustodia (Id,InspeccionId,EntregaUsuarioId,RecibeUsuarioId,FirmaEntrega,FirmaRecibe,FirmadaAt,CreatedAt,UpdatedAt) VALUES (' ||
             pg_temp.guid(id) || ',' || pg_temp.guid(inspeccion_id) || ',' || pg_temp.guid(entrega_usuario_id) || ',' || pg_temp.guid(recibe_usuario_id) || ',' ||
             pg_temp.lit(firma_entrega) || ',' || pg_temp.lit(firma_recibe) || ',' || pg_temp.ts(firmada_at) || ',' ||
             pg_temp.ts(created_at) || ',' || pg_temp.ts(updated_at) || ');' as stmt
      from transferencias_custodia) x order by n;
select 'GO';

-- ---- tblAsignaciones
select stmt || case when n % 500 = 0 then E'\nGO' else '' end
from (select row_number() over (order by created_at, id) n,
             'INSERT dbo.tblAsignaciones (Id,VehiculoId,Ninera,ConductorId,AsignadoPor,CreatedAt,UpdatedAt) VALUES (' ||
             pg_temp.guid(id) || ',' || pg_temp.guid(vehiculo_id) || ',' || pg_temp.lit(ninera) || ',' || pg_temp.guid(conductor_id) || ',' ||
             pg_temp.guid(asignado_por) || ',' || pg_temp.ts(created_at) || ',' || pg_temp.ts(updated_at) || ');' as stmt
      from asignaciones) x order by n;
select 'GO';

-- ---- tblOrdenesSofasa
select stmt || case when n % 500 = 0 then E'\nGO' else '' end
from (select row_number() over (order by created_at, id) n,
             'INSERT dbo.tblOrdenesSofasa (Id,SourceId,StatusCode,DatetimeEvent,OrdenNumber,OrdenSap,OriginCedi,Vin,Make,ModelDescription,DealerCode,DestinationCity,DestinationAddress,LoadingDate,Appointment,LoadingDock,Weight,VehiculoId,SyncedAt,CreatedAt,UpdatedAt) VALUES (' ||
             pg_temp.guid(id) || ',' || source_id || ',' || pg_temp.lit(status_code) || ',' || pg_temp.ts(datetime_event) || ',' ||
             pg_temp.lit(orden_number) || ',' || pg_temp.lit(orden_sap) || ',' || pg_temp.lit(origin_cedi) || ',' || pg_temp.lit(vin) || ',' ||
             pg_temp.lit(make) || ',' || pg_temp.lit(model_description) || ',' || pg_temp.lit(dealer_code) || ',' || pg_temp.lit(destination_city) || ',' ||
             pg_temp.lit(destination_address) || ',' || pg_temp.ts(loading_date) || ',' || pg_temp.ts(appointment) || ',' || pg_temp.lit(loading_dock) || ',' ||
             pg_temp.num(weight) || ',' || pg_temp.guid(vehiculo_id) || ',' || pg_temp.ts(synced_at) || ',' ||
             pg_temp.ts(created_at) || ',' || pg_temp.ts(updated_at) || ');' as stmt
      from ordenes_sofasa) x order by n;
select 'GO';

-- ---- tblAuditLog
select stmt || case when n % 500 = 0 then E'\nGO' else '' end
from (select row_number() over (order by created_at, id) n,
             'INSERT dbo.tblAuditLog (Id,UsuarioId,Accion,Entidad,EntidadId,CreatedAt,UpdatedAt) VALUES (' ||
             pg_temp.guid(id) || ',' || pg_temp.guid(usuario_id) || ',' || pg_temp.lit(accion) || ',' || pg_temp.lit(entidad) || ',' ||
             pg_temp.guid(entidad_id) || ',' || pg_temp.ts(created_at) || ',' || pg_temp.ts(updated_at) || ');' as stmt
      from audit_log) x order by n;
select 'GO';

select 'ENABLE TRIGGER dbo.TR_tblInspecciones_Inmutable ON dbo.tblInspecciones;';
select 'ENABLE TRIGGER dbo.TR_tblHallazgos_NoEnCerrada ON dbo.tblHallazgos;';
select 'ENABLE TRIGGER dbo.TR_tblEvidencias_NoEnCerrada ON dbo.tblEvidencias;';
select 'GO';
select 'PRINT ''IA-AUTOS: carga de datos finalizada. Ejecute reconciliar-sqlserver.sql y compare con reconciliar-postgres.sql'';';
select 'GO';
