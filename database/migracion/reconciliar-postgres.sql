-- =============================================================================
-- IA-AUTOS - Reconciliacion (ORIGEN PostgreSQL). Misma salida que reconciliar-sqlserver.sql.
--   psql "<cadena_origen>" -X -q -A -t -F '|' -v ON_ERROR_STOP=1 -f reconciliar-postgres.sql > origen.txt
-- Despues: diff origen.txt destino.txt   (sin diferencias = migracion conciliada)
-- =============================================================================
select 'usuarios|' || count(*) from usuarios;
select 'usuarios_activos|' || count(*) from usuarios where activo;
select 'usuarios_con_hash|' || count(*) from usuarios where password_hash is not null;
select 'vehiculos|' || count(*) from vehiculos;
select 'vehiculos_por_operacion|' || operacion || '=' || count(*) from vehiculos group by operacion order by 1;
select 'puntos_custodia|' || count(*) from puntos_custodia;
select 'etapas|' || count(*) from etapas;
select 'etapas_requieren_inspeccion|' || count(*) from etapas where requiere_inspeccion;
select 'inspecciones|' || count(*) from inspecciones;
select 'inspecciones_cerradas|' || count(*) from inspecciones where estado = 'cerrada';
select 'inspecciones_por_momento|' || momento || '=' || count(*) from inspecciones group by momento order by 1;
select 'hallazgos|' || count(*) from hallazgos;
select 'hallazgos_por_clase|' || clase_dano || '=' || count(*) from hallazgos group by clase_dano order by 1;
select 'hallazgos_con_coordenadas|' || count(*) from hallazgos where coords_diagrama is not null;
select 'evidencias|' || count(*) from evidencias;
select 'evidencias_con_geo|' || count(*) from evidencias where sello_lat is not null and sello_lng is not null;
select 'transferencias|' || count(*) from transferencias_custodia;
select 'transferencias_completas|' || count(*) from transferencias_custodia where firma_entrega is not null and firma_recibe is not null;
select 'asignaciones|' || count(*) from asignaciones;
select 'ordenes_sofasa|' || count(*) from ordenes_sofasa;
select 'ordenes_sofasa_con_vehiculo|' || count(*) from ordenes_sofasa where vehiculo_id is not null;
select 'audit_log|' || count(*) from audit_log;
select 'suma_lat_inspecciones|' || coalesce(round(sum(lat)::numeric, 4), 0) from inspecciones;
select 'suma_peso_sofasa|' || coalesce(round(sum(weight), 2), 0) from ordenes_sofasa;
select 'vehiculos_movilizados|' || count(*) from (
  select e.vehiculo_id
    from etapas e
   group by e.vehiculo_id
  having count(*) > 0
     and count(*) = count(*) filter (where exists (select 1 from inspecciones i where i.etapa_id = e.id and i.estado = 'cerrada'))
) m;
select 'vehiculos_con_novedad|' || count(*) from vehiculos v where exists (
  select 1 from etapas e join inspecciones i on i.etapa_id = e.id join hallazgos h on h.inspeccion_id = i.id where e.vehiculo_id = v.id);
