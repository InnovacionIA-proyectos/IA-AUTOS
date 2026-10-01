# Migracion del backend: guia de corte

Pasos para pasar del sistema anterior (Next.js y PostgreSQL) a esta API sobre SQL Server 2019. Los datos se trasladan con los scripts de `database/migracion/` (ver su README); aqui se ordena el corte completo.

## Compatibilidad

La app movil actual y el ETL siguen funcionando sin recompilar: rutas, metodos, codigos HTTP y formas de respuesta se conservan, y los tokens emitidos por el sistema anterior se aceptan mientras se use el mismo secreto JWT (`SUPABASE_JWT_SECRET` pasa a `Jwt__Secret`). Se verifico en ambos sentidos con la libreria `jose` del sistema anterior. Las diferencias intencionales estan en `docs/seguridad.md`.

## ETL de Sofasa

No se modifica. Solo debe apuntar a la API nueva:

| Variable del ETL | Cambio |
|---|---|
| `INGEST_URL` | nueva direccion: `https://<host-de-la-api>/api/etl/sofasa/ordenes` |
| `INGEST_TOKEN` | igual al `Etl__IngestToken` configurado en la API |

La respuesta conserva los nombres que lee el ETL (`guardadas`, `vehiculosCreados`, `vehiculosActualizados`, `errores`). La limitacion existente del ETL (lee solo las 1000 ordenes mas recientes en cada ciclo) no cambia con esta migracion.

## Orden recomendado

1. Preparar la base: `00-crear-base.sql`, `ddl.sql` y `01-crear-usuario-app.sql`; verificar con `database/tests/verificar-ddl.sql` en un entorno que no sea produccion.
2. Ensayar el traslado de datos en QA: preflight, generacion de inserts, carga y reconciliacion sin diferencias (`database/migracion/README.md`).
3. Desplegar la API en QA con las variables de `docs/configuracion.md`; crear el super administrador si la base no trae usuarios (`crear-superadmin`) y recorrer el flujo de campo con la app.
4. Ventana de mantenimiento: congelar escrituras en el sistema anterior, repetir el traslado sobre una base destino vacia, reconciliar y desplegar en produccion.
5. Cambiar la URL de la API en la app movil (variable `EXPO_PUBLIC_API_URL`) y en `INGEST_URL` del ETL.
6. Mantener el sistema anterior y PostgreSQL apagados pero intactos durante un periodo de observacion.

## Antes del corte, decidir

- Contrasenas: si algun usuario conserva la contrasena de desarrollo de la migracion `20260626130000_auth_password`, generar los inserts con `reset_passwords=1` (el preflight lo advierte) y asignar contrasenas nuevas desde la gestion de usuarios.
- Datos sensibles: `services/etl-sofasa.7z` y `ejemplo documentos cargue/` del repositorio anterior pueden contener datos reales; revisarlos y purgarlos del historial si corresponde.
- Secretos expuestos: rotar cualquier credencial que haya circulado en archivos de permisos o en el historial del repositorio anterior.
- Infraestructura: SQL Server 2019 tiene soporte principal finalizado (soporte extendido hasta enero de 2030). Railway no ofrece SQL Server administrado: definir donde corre la base y la API (ver el plan de migracion).

## Reversa

Antes de reabrir escrituras en produccion, volver al sistema anterior es inmediato (cambiar de nuevo `EXPO_PUBLIC_API_URL` e `INGEST_URL`). Despues de operar sobre la API nueva, los datos creados en SQL Server no existen en PostgreSQL; revertir implicaria perderlos o trasladarlos de vuelta. Por eso se recomienda un periodo de observacion con ambos sistemas disponibles y el corte de escrituras en una ventana controlada.

## Limitaciones conocidas de la verificacion

- Las pruebas de integracion usan SQL Server 2019 real, pero el almacen de imagenes se probo contra un servidor S3 compatible de pruebas, no contra el MinIO desplegado. Haga una prueba de subida y lectura contra su MinIO al desplegar en QA.
- La app movil no se probo de extremo a extremo contra esta API (no hay dispositivo en este entorno); la compatibilidad se verifico a nivel de contrato HTTP con las pruebas y con los formatos reales que envian la app y el ETL.
