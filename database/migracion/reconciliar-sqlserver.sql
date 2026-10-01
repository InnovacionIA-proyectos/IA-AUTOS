-- =============================================================================
-- IA-AUTOS - Reconciliacion (DESTINO SQL Server). Misma salida que reconciliar-postgres.sql.
--   sqlcmd -S <servidor> -d <base> -U <usuario> -P <password> -I -b -h -1 -W -i reconciliar-sqlserver.sql -o destino.txt
-- Despues: diff origen.txt destino.txt   (sin diferencias = migracion conciliada)
-- =============================================================================
SET NOCOUNT ON;
SELECT 'usuarios|' + CAST(COUNT(*) AS varchar(20)) FROM dbo.tblUsuarios;
SELECT 'usuarios_activos|' + CAST(COUNT(*) AS varchar(20)) FROM dbo.tblUsuarios WHERE Activo = 1;
SELECT 'usuarios_con_hash|' + CAST(COUNT(*) AS varchar(20)) FROM dbo.tblUsuarios WHERE PasswordHash IS NOT NULL;
SELECT 'vehiculos|' + CAST(COUNT(*) AS varchar(20)) FROM dbo.tblVehiculos;
SELECT 'vehiculos_por_operacion|' + Operacion + '=' + CAST(COUNT(*) AS varchar(20)) FROM dbo.tblVehiculos GROUP BY Operacion ORDER BY 1;
SELECT 'puntos_custodia|' + CAST(COUNT(*) AS varchar(20)) FROM dbo.tblPuntosCustodia;
SELECT 'etapas|' + CAST(COUNT(*) AS varchar(20)) FROM dbo.tblEtapas;
SELECT 'etapas_requieren_inspeccion|' + CAST(COUNT(*) AS varchar(20)) FROM dbo.tblEtapas WHERE RequiereInspeccion = 1;
SELECT 'inspecciones|' + CAST(COUNT(*) AS varchar(20)) FROM dbo.tblInspecciones;
SELECT 'inspecciones_cerradas|' + CAST(COUNT(*) AS varchar(20)) FROM dbo.tblInspecciones WHERE Estado = 'cerrada';
SELECT 'inspecciones_por_momento|' + Momento + '=' + CAST(COUNT(*) AS varchar(20)) FROM dbo.tblInspecciones GROUP BY Momento ORDER BY 1;
SELECT 'hallazgos|' + CAST(COUNT(*) AS varchar(20)) FROM dbo.tblHallazgos;
SELECT 'hallazgos_por_clase|' + ClaseDano + '=' + CAST(COUNT(*) AS varchar(20)) FROM dbo.tblHallazgos GROUP BY ClaseDano ORDER BY 1;
SELECT 'hallazgos_con_coordenadas|' + CAST(COUNT(*) AS varchar(20)) FROM dbo.tblHallazgos WHERE CoordsDiagrama IS NOT NULL;
SELECT 'evidencias|' + CAST(COUNT(*) AS varchar(20)) FROM dbo.tblEvidencias;
SELECT 'evidencias_con_geo|' + CAST(COUNT(*) AS varchar(20)) FROM dbo.tblEvidencias WHERE SelloLat IS NOT NULL AND SelloLng IS NOT NULL;
SELECT 'transferencias|' + CAST(COUNT(*) AS varchar(20)) FROM dbo.tblTransferenciasCustodia;
SELECT 'transferencias_completas|' + CAST(COUNT(*) AS varchar(20)) FROM dbo.tblTransferenciasCustodia WHERE FirmaEntrega IS NOT NULL AND FirmaRecibe IS NOT NULL;
SELECT 'asignaciones|' + CAST(COUNT(*) AS varchar(20)) FROM dbo.tblAsignaciones;
SELECT 'ordenes_sofasa|' + CAST(COUNT(*) AS varchar(20)) FROM dbo.tblOrdenesSofasa;
SELECT 'ordenes_sofasa_con_vehiculo|' + CAST(COUNT(*) AS varchar(20)) FROM dbo.tblOrdenesSofasa WHERE VehiculoId IS NOT NULL;
SELECT 'audit_log|' + CAST(COUNT(*) AS varchar(20)) FROM dbo.tblAuditLog;
SELECT 'suma_lat_inspecciones|' + CAST(COALESCE(CAST(ROUND(SUM(CAST(Lat AS decimal(18,6))), 4) AS decimal(18,4)), 0) AS varchar(40)) FROM dbo.tblInspecciones;
SELECT 'suma_peso_sofasa|' + CAST(COALESCE(CAST(ROUND(SUM(Weight), 2) AS decimal(18,2)), 0) AS varchar(40)) FROM dbo.tblOrdenesSofasa;
SELECT 'vehiculos_movilizados|' + CAST(COUNT(*) AS varchar(20)) FROM dbo.vwVehiculosMovilizados;
SELECT 'vehiculos_con_novedad|' + CAST(COUNT(*) AS varchar(20)) FROM dbo.tblVehiculos v
 WHERE EXISTS (SELECT 1 FROM dbo.tblEtapas e JOIN dbo.tblInspecciones i ON i.EtapaId = e.Id
               JOIN dbo.tblHallazgos h ON h.InspeccionId = i.Id WHERE e.VehiculoId = v.Id);
