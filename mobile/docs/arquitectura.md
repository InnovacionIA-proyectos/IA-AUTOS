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
- `sync` se consume como transversal, aunque viva en una funcionalidad. Son dos cosas distintas y ambas van en un
  solo sentido: el estado de la red (`estadoRedProvider`, cuyo puerto está en `sync/domain`) lo leen `inspecciones`,
  `catalogos` y `viajes` para decidir si revalidan; y el estado de la cola (`syncControllerProvider` y `BarraSync`,
  en `sync/presentation`) lo usan las pantallas de listado para mostrar lo pendiente y avisar antes de salir.
  Moverlos a `core` queda pendiente; hasta entonces son las únicas dependencias admitidas hacia `sync`, y nada en
  `sync` puede importar de vuelta.
- `catalogos` solo depende de `auth` (necesita saber si hay sesión antes de pedir nada) y del estado de la red,
  igual que `sync`. `inspecciones` y `vehiculos` dependen de `catalogos` para traducir códigos a etiquetas.
- `viajes` depende de `auth` y de `sync` como transversal (red y estado de la cola), igual que `inspecciones`.
  `inspecciones` depende de `viajes`: la captura lee de ahí la placa, el tráiler, la unidad y el conductor, que ya
  no se escriben.
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
| Persistencia | Drift (SQLite) | tablas `jobs` (cola) y `cache_lecturas` (listado de inspecciones, catálogos y viajes) |
| Secretos | flutter_secure_storage | el token vive en el Keystore; nunca en SQLite ni en preferencias |
| Dispositivo | camera, geolocator, connectivity_plus | detrás de interfaces (`ServicioUbicacion`, `ServicioFotos`, `EstadoRed`) |

## El viaje como eje

«Mis viajes» (`GET /api/viajes/mios`) es la entrada de la app: un inspector o un conductor solo ven los viajes donde
están asignados, y de ahí salen las unidades que pueden abrir.

- La copia vive en `cache_lecturas` con clave **por usuario**, porque un viaje solo lo ven su conductor y su
  inspector: en un equipo compartido nadie debe leer los de otra persona.
- Buscar un VIN es filtrar dentro de esa copia, no digitar 17 caracteres cualesquiera. La API responde `403` ante un
  VIN ajeno, así que ofrecer la búsqueda libre solo llevaba a un error.
- La captura ya no pide placa, tráiler ni conductor: salen del viaje de la etapa (`viaje_id`) y el servidor los toma
  de ahí. Si el viaje no está en la copia se pide suelto; si tampoco se puede, la captura sigue siendo posible.

## Catálogos maestros

Etapas, lugares, clases y tamaños de daño, niveles de severidad, vistas de foto y cuadrantes **no son listas fijas**:
los administra TI o el coordinador de calidad desde el portal y la app los descarga de `GET /api/catalogos`.
Se guarda el sobre completo, pero hoy la app solo consume etapas, clases y tamaños de daño; las vistas de foto
las usará S5-HU-24 y los cuadrantes, S6-HU-01.

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
- Lo que **sí** queda fijo a propósito (`vehiculos/domain/etiquetas.dart`) son los valores permitidos del contrato:
  `operacion`, `estado` de inspección y `rol`. No están en `/api/catalogos` y la API no manda su nombre.
- Queda fijo lo que **todavía** no se migró: `vehiculos/domain/partes.dart` dibuja el diagrama con una lista de
  zonas y coordenadas propia. Lo reemplazan los catálogos `cuadrantes` y `cuadrante-vistas` en S6-HU-01.

## Sesión

Se entra de dos formas: **cédula y PIN** (conductor e inspector, que pueden no tener correo) o **correo y
contraseña**. El modo de campo es el que ofrece primero la pantalla, porque es quien más usa la app.

Si el PIN lo asignó o reinició un administrador, `usuario.debe_cambiar_pin` llega en `true` y el resto de la API
responde `403 PIN_CHANGE_REQUIRED`: el router lleva a cambiarlo y no deja salir de ahí salvo cerrando sesión. El
token no cambia, solo deja de exigirse el cambio. Tras varios PIN errados la cuenta se bloquea y el servidor dice
hasta cuándo en `details.bloqueado_hasta`.

Por eso «hay sesión» y «se puede trabajar» no son lo mismo: `EstadoAuth.puedeOperar` distingue las dos cosas y es la
guarda de todo lo que arranca solo. Con un PIN temporal la cola de envíos y los catálogos **esperan**, porque el 403
no se arregla reintentando y marcaría como fallido lo que solo estaba a destiempo; al cambiar el PIN los dos
arrancan por la misma señal, sin reiniciar la app. Si el PIN se reinicia con la sesión ya abierta, el
`403 PIN_CHANGE_REQUIRED` de cualquier petición marca la sesión y el router lleva a cambiarlo; ese 403 es el único
que la cola trata como transitorio, para que el envío en curso se reanude después en lugar de perderse.

**Qué ve cada quien.** El menú no sale de una tabla de roles escrita aquí, sino de las `capacidades` que devuelve
`GET /api/auth/me`: salen de las mismas listas con que el backend autoriza los endpoints, así que no se desalinean. Se
piden al entrar —`/me` responde incluso con un PIN temporal— y se guardan con la sesión, para que el menú exista también
sin señal; si `/me` no responde se cae a `capacidadesDeRol`, una red de seguridad para no dejar a nadie sin menú.

Las pestañas y la capacidad que exige cada una viven en una sola lista (`app/pestanas.dart`), de donde salen las tres
cosas que deben concordar: la barra de abajo, el destino al entrar y qué rutas se dejan abrir. Esconder una pestaña no
basta —una ruta se abre por enlace, o queda abierta cuando a alguien le cambian el rol con la sesión puesta—, así que
cada pantalla va envuelta en `Permitido` y muestra «sin permisos» dentro del shell, con la barra a la vista. Capturar y
firmar exigen `registrar_inspecciones`, no solo poder mirar. La autorización de verdad sigue siendo la de la API; esto
solo evita ofrecer puertas que el servidor cierra.

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
- **Widgets**: login, cambio de PIN y su redirect, menú por rol y rutas sin permiso, captura (los datos del viaje y el desenlace que se anuncia al
  cerrar), lienzo de firma, diagrama del vehículo, barra de sincronización y arranque de la app.
- **Contrato**: campos y rutas contra el OpenAPI del backend.

Pendientes de ejecutar en dispositivo real (no se pueden validar en CI): cámara, GPS, permisos y memoria en gama baja.
Lo que sí se validó en dispositivo con la API local: ingreso con cédula y PIN con cambio forzado, «Mis viajes» con su
búsqueda, y la captura tomando los datos del viaje. El envío del informe todavía no se puede validar de punta a punta:
la app manda `momento: 'informe'` y la API solo acepta salida o llegada (S5-HU-24).
