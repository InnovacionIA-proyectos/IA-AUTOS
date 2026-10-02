# Arquitectura de la app móvil

Feature-first con Clean Architecture por analogía (el plan de migración lo fija así): cada funcionalidad separa `domain`,
`data` y `presentation`, y `core` aloja lo transversal.

```
presentation  ->  domain  <-  data
(widgets, controladores)   (reglas, modelos, puertos)   (API, SQLite, archivos)
```

- **domain**: Dart puro (sin Flutter ni HTTP). Reglas de cierre, modelos de inventario, catálogo de partes, la cola de
  sincronización y sus *puertos* (`ColaRepositorio`, `GatewayInformes`, `EstadoRed`, `ArchivosLocales`).
- **data**: implementaciones de los puertos (`ColaDrift`, `GatewayInformesApi`, `ArchivosLocalesIo`) y repositorios de
  lectura que usan `ClienteApi`.
- **presentation**: pantallas y controladores Riverpod.

La lógica con más riesgo (la cola) está en `domain` precisamente para probarla con dobles en memoria, sin dispositivo.

## Piezas principales

| Pieza | Tecnología | Nota |
|---|---|---|
| Estado | Riverpod 3 (`Notifier`, `FutureProvider`) | sin generación de código; reintento automático de providers desactivado |
| Navegación | go_router | un solo `redirect` por sesión; sin sesión solo existe `/login` |
| HTTP | Dio | `ClienteApi` adjunta el JWT y convierte todo fallo en `ErrorApi` |
| Persistencia | Drift (SQLite) | tablas `jobs` (cola) y `cache_lecturas` |
| Secretos | flutter_secure_storage | el token vive en el Keystore; nunca en SQLite ni en preferencias |
| Dispositivo | camera, geolocator, connectivity_plus | detrás de interfaces (`ServicioUbicacion`, `ServicioFotos`) |

## Sesión

El token se restaura del almacén seguro al arrancar. Un 401 en una petición autenticada cierra la sesión con un aviso
(el login no cuenta). `android:allowBackup` está desactivado para que una restauración de copia no deje datos cifrados
sin su llave.

## Contrato con la API

Los modelos son manuales (snake_case, igual que la API). `test/contrato/contrato_api_test.dart` verifica contra
`backend/docs/openapi.json` que cada campo leído o enviado, y cada ruta usada, siga existiendo.

## Pruebas

- **Unitarias de dominio**: cola (33), reglas de cierre, trazo de firma, inventario y catálogo de partes.
- **Persistencia**: cola y caché sobre SQLite real (en memoria y en archivo, para comprobar que sobrevive a reabrir).
- **Controladores**: sesión, sincronización y listado, con dobles de red, API y almacén.
- **Widgets**: login, lienzo de firma, diagrama del vehículo y barra de sincronización.
- **Contrato**: campos y rutas contra el OpenAPI del backend.

Pendientes de ejecutar en dispositivo real (no se pueden validar en CI): cámara, GPS, permisos y memoria en gama baja.
