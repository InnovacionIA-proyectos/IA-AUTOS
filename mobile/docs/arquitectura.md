# Arquitectura de la app móvil

Feature-First + Clean pragmática: la app se divide por funcionalidades del negocio y, dentro de cada una, el código se
separa en `presentation`, `domain` y `data` solo cuando esa separación aporta claridad. `core` aloja lo transversal y
`app` compone la aplicación.

```
presentation  ->  domain  <-  data
(widgets, controladores)   (reglas, modelos, puertos)   (API, SQLite, archivos)
```

- **domain**: Dart puro (sin Flutter ni HTTP). Reglas de cierre, modelos de inventario, catálogo de partes, fotos y
  hallazgos locales, la cola de sincronización y sus *puertos* (`ColaRepositorio`, `GatewayInformes`, `EstadoRed`,
  `ArchivosLocales`, `AlmacenSesion`).
- **data**: implementaciones de los puertos (`ColaDrift`, `GatewayInformesApi`, `ArchivosLocalesIo`,
  `EstadoRedConectividad`, `AlmacenSesionSeguro`) y repositorios de lectura que usan `ClienteApi`.
- **presentation**: pantallas y controladores Riverpod.
- **providers.dart** (en la raíz de cada feature): declara los providers de sus repositorios y puertos. Es el único
  punto de cableado de la feature.

Reglas de dependencia:

- `core` no importa nada de `features`.
- `app/proveedores.dart` solo declara lo transversal: entorno, base de datos, cliente HTTP y servicios del dispositivo.
  Los `providers.dart` de cada feature lo importan para componer sus repositorios.
- `presentation` importa los providers de su propia feature; acude a `app/proveedores.dart` únicamente por los
  transversales (`entornoProvider`, `servicioUbicacionProvider`, `servicioFotosProvider`).
- `sync` depende de `inspecciones` (transporta sus fotos y hallazgos) y no al revés. `vehiculos` no depende de `sync`.
- Una feature pequeña no crea capas vacías: `sofasa` y `custodia` tienen un repositorio y una pantalla, y basta.

La lógica con más riesgo (la cola) está en `domain` precisamente para probarla con dobles en memoria, sin dispositivo.

## Piezas principales

| Pieza | Tecnología | Nota |
|---|---|---|
| Estado | Riverpod 3 (`Notifier`, `FutureProvider`) | sin generación de código; reintento automático de providers desactivado |
| Navegación | go_router | un solo `redirect` por sesión; sin sesión solo existe `/login` |
| HTTP | Dio | `ClienteApi` adjunta el JWT y convierte todo fallo en `ErrorApi` |
| Persistencia | Drift (SQLite) | tablas `jobs` (cola) y `cache_lecturas` |
| Secretos | flutter_secure_storage | el token vive en el Keystore; nunca en SQLite ni en preferencias |
| Dispositivo | camera, geolocator, connectivity_plus | detrás de interfaces (`ServicioUbicacion`, `ServicioFotos`, `EstadoRed`) |

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
