-- =============================================================================
-- IA-AUTOS - Migracion PostgreSQL -> SQL Server 2019
-- Paso 0: verificacion previa (SOLO LECTURA). Ejecutar contra la base ORIGEN:
--
--   psql "<cadena_de_conexion_origen>" -v ON_ERROR_STOP=1 -f 00-preflight-postgres.sql
--
-- Cada consulta devuelve las filas que impedirian o ensuciarian la carga.
-- Si todas devuelven 0 filas, se puede continuar con 01-generar-inserts-postgres.sql.
-- =============================================================================
\pset format aligned
\echo '== 1. Correos duplicados sin distinguir mayusculas (SQL Server compara sin distinguir mayusculas)'
select lower(email) as email, count(*) from usuarios group by lower(email) having count(*) > 1;

\echo '== 2. VINs duplicados sin distinguir mayusculas'
select upper(vin) as vin, count(*) from vehiculos group by upper(vin) having count(*) > 1;

\echo '== 3. Inspecciones con mas de una transferencia de custodia (el destino permite una por inspeccion)'
select inspeccion_id, count(*) from transferencias_custodia group by inspeccion_id having count(*) > 1;

\echo '== 4. Textos que exceden el ancho de columna del destino'
select 'vehiculos.vin' as campo, id::text from vehiculos where length(vin) > 64
union all select 'vehiculos.marca', id::text from vehiculos where length(marca) > 100
union all select 'vehiculos.modelo', id::text from vehiculos where length(modelo) > 300
union all select 'usuarios.nombre', id::text from usuarios where length(nombre) > 200
union all select 'usuarios.email', id::text from usuarios where length(email) > 320
union all select 'inspecciones.notas', id::text from inspecciones where length(notas) > 1000
union all select 'inspecciones.placa/trailer', id::text from inspecciones where length(placa) > 50 or length(trailer) > 50
union all select 'inspecciones.conductor_nombre', id::text from inspecciones where length(conductor_nombre) > 200
union all select 'hallazgos.parte_vehiculo', id::text from hallazgos where length(parte_vehiculo) > 200
union all select 'hallazgos.coords_diagrama', id::text from hallazgos where length(coords_diagrama::text) > 200
union all select 'evidencias.storage_path', id::text from evidencias where length(storage_path) > 500
union all select 'asignaciones.ninera', id::text from asignaciones where length(ninera) > 100;

\echo '== 5. Valores fuera de los catalogos CHECK del destino'
select 'usuarios.rol' as campo, rol::text as valor from usuarios where rol::text not in ('inspector','conductor','coordinador_calidad','comercial','admin','super_admin')
union all select 'inspecciones.momento', momento::text from inspecciones where momento::text not in ('check_in','check_out','informe')
union all select 'hallazgos.clase_dano', clase_dano::text from hallazgos where clase_dano::text not in ('deformacion','rotura','cortado','perforacion','golpe','mancha','raya_pintar','desconchado','particulas','faltante')
union all select 'hallazgos.tamano_dano', tamano_dano::text from hallazgos where tamano_dano::text not in ('1_5cm','5_10cm','10_20cm','mas_20cm','requiere_reemplazo','mas_10_particulas')
union all select 'inspecciones.lat/lng', id::text from inspecciones where lat not between -90 and 90 or lng not between -180 and 180
union all select 'evidencias.sello_lat/lng', id::text from evidencias where sello_lat not between -90 and 90 or sello_lng not between -180 and 180;

\echo '== 6. Contrasenas: usuarios sin hash (no podran ingresar hasta que se les asigne una) y hashes repetidos'
select count(*) filter (where password_hash is null) as sin_hash,
       count(*) as total_usuarios
from usuarios;
select count(*) as usuarios_con_el_mismo_hash, left(md5(password_hash), 8) as huella_del_hash
from usuarios where password_hash is not null
group by password_hash having count(*) > 1;

\echo 'Si hay usuarios que comparten hash, es probable que conserven la contrasena de desarrollo inicial'
\echo '(migracion 20260626130000_auth_password). En ese caso genere los INSERT con: -v reset_passwords=1'
