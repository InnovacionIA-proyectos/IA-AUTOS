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
- `sync` depende de `inspecciones` (transporta sus fotos y hallazgos) y no al revés.
- El estado de la red (`estadoRedProvider`) se consume como transversal desde varias funcionalidades, aunque su
  puerto viva en `sync/domain`: `inspecciones` y `catalogos` lo leen para decidir si revalidan. Moverlo a `core`
  queda pendiente; hasta entonces esa es la única dependencia admitida hacia `sync`.
- `catalogos` no depende de ninguna funcionalidad salvo eso; el resto (`inspecciones`, `vehiculos`) depende de
  `catalogos` para traducir códigos a etiquetas.
- Una feature pequeña no crea capas vacías: `sofasa` y `custodia` tienen un repositorio y una pantalla, y basta.
  Por lo mismo, un valor de catálogo se modela con un único tipo genérico que conserva sus campos, en vez de diez
  clases para diez catálogos de los que hoy se usan tres.

La lógica con más riesgo (la cola) está en `domain` precisamente para probarla con dobles en memoria, sin dispositivo.

## Piezas principales

| Pieza | Tecnología | Nota |
|---|---|---|
| Estado | Riverpod 3 (`Notifier`, `FutureProvider`) | sin generación de código; reintento automático de providers desactivado |
| Navegación | go_router | un solo `redirect` por sesión; sin sesión solo existe `/login` |
| HTTP | Dio | `ClienteApi` adjunta el JWT y convierte todo fallo en `ErrorApi` |
| Persistencia | Drift (SQLite) | tablas `jobs` (cola) y `cache_lecturas` (listado de inspecciones y catálogos) |
| Secretos | flutter_secure_storage | el token vive en el Keystore; nunca en SQLite ni en preferencias |
| Dispositivo | camera, geolocator, connectivity_plus | detrás de interfaces (`ServicioUbicacion`, `ServicioFotos`, `EstadoRed`) |

## Catálogos maestros

Etapas, lugares, clases y tamaños de daño, niveles de severidad, vistas de foto y cuadrantes **no son listas fijas**:
los administra TI o el coordinador de calidad desde el portal y la app los descarga de `GET /api/catalogos`.

- La copia vive en `cache_lecturas` bajo una sola clave global, como un JSON `{version, etag, usuarioId, catalogos}`.
  No se creó una tabla propia a propósito: subir `schemaVersion` obliga a una migración de Drift sobre la misma base
  en la que vive la cola de envíos, que es el único dato de la app que no se puede recuperar.
- Se revalida con `If-None-Match`, devolviendo el `ETag` tal como lo mandó el servidor; un `304` conserva lo guardado.
  Se revalida al abrir la app, al iniciar sesión y al volver una señal estable, nunca sin sesión.
- Los selectores muestran solo los valores activos; para dibujar informes viejos se usan todos, porque un hallazgo
  antiguo puede tener una clase que hoy está inactiva.
- Sin copia no se puede cerrar un informe: uno cerrado sin hallazgos diría que el vehículo venía sano.
- La copia es global, no por usuario, porque la API sirve la misma a todo autenticado. Lleva anotado quién la
  descargó para que, si algún día un catálogo pasa a filtrarse por cliente, no se le muestre a otro.
- Lo que **sí** queda fijo en el código (`vehiculos/domain/etiquetas.dart`) son los valores permitidos del contrato:
  `operacion`, `estado` de inspección y `rol`. No están en `/api/catalogos` y la API no manda su nombre.

## Sesión

El token se restaura del almacén seguro al arrancar. Un 401 en una petición autenticada cierra la sesión con un aviso
(el login no cuenta). `android:allowBackup` está desactivado para que una restauración de copia no deje datos cifrados
sin su llave.

## Contrato con la API

Los modelos son manuales (snake_case, igual que la API). `test/contrato/contrato_api_test.dart` verifica contra
`backend/docs/openapi.json` que cada campo leído o enviado, y cada ruta usada, siga existiendo.

## Pruebas

- **Unitarias de dominio**: cola (33), reglas de cierre, trazo de firma, inventario, catálogo de partes y
  catálogos maestros (lecturas, orden y aislamiento por usuario).
- **Persistencia**: cola y caché sobre SQLite real (en memoria y en archivo, para comprobar que sobrevive a
  reabrir), incluida la descarga y revalidación de catálogos.
- **Controladores**: sesión, sincronización, listado y catálogos, con dobles de red, API y almacén.
- **Widgets**: login, lienzo de firma, diagrama del vehículo, barra de sincronización y arranque de la app.
- **Contrato**: campos y rutas contra el OpenAPI del backend.

Pendientes de ejecutar en dispositivo real (no se pueden validar en CI): cámara, GPS, permisos y memoria en gama baja.
