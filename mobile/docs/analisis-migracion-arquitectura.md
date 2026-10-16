# Análisis: migración a Feature-First + Clean pragmática

Documento de análisis previo a la migración. **No modifica código.** Fecha: 2026-10-16.

Arquitectura destino: *Arquitectura Flutter Mobile — Feature-First + Clean pragmática* (Presentation · Domain · Data).
Su idea en una frase: organizar por negocio y separar responsabilidades sin sobrearquitecturar. Regla pragmática: una
feature pequeña no crea `presentation/domain/data` vacíos; la estructura crece cuando hace falta. Se evita la cadena
`Page → Controller → UseCase → Repository → RepositoryImpl → Gateway → Adapter → API` para tareas simples.

Alcance: solo `mobile/`. Nada fuera de esta carpeta se toca.

---

## 1. Dónde está la app hoy

52 archivos Dart en `lib/` (4.377 líneas sin el generado), 129 pruebas en `test/`. Un solo commit en el repo
(`dfa331b`, "MIGRACIÓN TOTAL"), así que no hay historia que preservar más allá del estado actual.

```
lib/
  main.dart
  app/        app.dart, proveedores.dart (TODO el cableado Riverpod), router.dart
  core/       config, db (Drift: tablas jobs + cache_lecturas), dispositivo, errores, http (Dio),
              red (connectivity), seguridad (secure storage), tema, ui, util
  features/
    auth/          data/ domain/ presentation/
    vehiculos/     data/ domain/ presentation/
    inspecciones/  data/ domain/ presentation/
    custodia/      data/ domain/ presentation/
    sofasa/        data/ domain/ presentation/
    sync/          data/ domain/ presentation/
```

**Conclusión principal: la app ya es Feature-First con capas Presentation/Domain/Data.** La migración no es una
reescritura. Es un ajuste para que cumpla la parte *pragmática* de la guía y para corregir cuatro desvíos concretos.

Lo que ya cumple y no hay que tocar:

| Principio de la guía | Estado |
|---|---|
| División por funcionalidades del negocio | Cumple: 6 features alineadas con las pantallas |
| Domain sin Flutter ni HTTP | Cumple: ningún `features/*/domain` importa flutter, dio, drift ni riverpod |
| Presentation no habla con la API ni la BD directamente | Cumple: ninguna página usa `ClienteApi`, `AppDatabase` ni `CacheLecturas` |
| Reglas de negocio fuera de los widgets | Cumple: `reglas_informe.dart`, `inventario.dart` (`accionDeEtapa`), `trazo_firma.dart` |
| Puertos solo donde hay más de una implementación o se necesita doble para probar | Cumple: `sync/domain/puertos.dart`, `AuthRepositorio`, `AlmacenSesion`, `ServicioUbicacion/Fotos` |

---

## 2. Brechas respecto a la guía

### B1. Cableado centralizado en `app/proveedores.dart` (la brecha de fondo)

Todos los providers viven en un archivo único de 70 líneas que importa `data/` de las seis features. Consecuencias:

- Cada feature **depende de `app/`** (`import '../../../app/proveedores.dart'`) en 9 archivos de presentation. Es una
  dependencia hacia arriba: la feature no se entiende sola y mover una feature arrastra el archivo global.
- Contradice "cuando cambia Embarques, su código está cerca": añadir un repositorio a `sofasa` obliga a editar `app/`.
- El arnés de pruebas (`test/support/arnes.dart`) también cuelga de ese archivo global.

**Destino:** cada feature declara sus propios providers (en su `data/` o en un `providers.dart` de la feature).
`app/` queda solo con lo transversal: entorno, base de datos, cliente HTTP, red, almacén seguro, y el router.

### B2. `core/` depende de `features/` (dependencia invertida)

```
lib/core/red/estado_red.dart          → import features/sync/domain/puertos.dart   (implementa EstadoRed)
lib/core/seguridad/almacen_sesion.dart → import features/auth/domain/sesion.dart    (guarda Sesion)
```

`core` debería ser lo transversal del que todos dependen, no al revés. Dos piezas de `core` son en realidad **Data de
una feature**:

| Archivo hoy | Qué es realmente | Destino |
|---|---|---|
| `core/red/estado_red.dart` | implementación del puerto `EstadoRed` de sync | `features/sync/data/estado_red_conectividad.dart` |
| `core/seguridad/almacen_sesion.dart` | persistencia de la sesión (puerto + impl) | puerto en `features/auth/domain/`, impl en `features/auth/data/` |
| `core/db/cache_lecturas.dart` | caché que solo usa `inspecciones` | `features/inspecciones/data/` |

La tabla Drift `CacheLecturas` sigue en `core/db/app_database.dart` (es el esquema único de la BD); lo que se mueve es
el repositorio que la consulta.

### B3. Modelos compartidos ubicados dentro de `sync`

`sync/domain/job.dart` mezcla dos cosas: la cola (`Job`, `InformeJob`, `FirmaJob`) y **modelos de negocio de la
inspección** (`CoordsDiagrama`, `FotoLocal`, `HallazgoLocal`). Por eso `vehiculos` e `inspecciones` importan `sync`:

```
vehiculos/domain/inventario.dart        → CoordsDiagrama
vehiculos/domain/partes.dart            → CoordsDiagrama
vehiculos/presentation/diagrama_vehiculo.dart → CoordsDiagrama
inspecciones/domain/reglas_informe.dart → FotoLocal, HallazgoLocal, InformeJob
inspecciones/presentation/captura_page.dart → CoordsDiagrama, FotoLocal, HallazgoLocal
```

Conceptualmente *una inspección tiene hallazgos y fotos*, y *la cola transporta inspecciones*. La dependencia correcta
es `sync → inspecciones`, no al revés.

**Destino:** `CoordsDiagrama` a `vehiculos/domain` (es una coordenada sobre el diagrama del vehículo); `FotoLocal` y
`HallazgoLocal` a `inspecciones/domain`. `job.dart` conserva solo `Job`, `InformeJob`, `FirmaJob` e importa los
modelos de las features. Los `toJson/fromJson` no cambian (snake_case de la API; el contrato no se toca).

### B4. Interfaces y capas sin más de una implementación (la parte "pragmática")

La guía pide no crear interfaces ni archivos que no aportan valor. Casos:

| Pieza | Situación | Decisión propuesta |
|---|---|---|
| `AuthRepositorio` (interfaz) + `AuthRepositorioApi` (14 líneas) | única impl real; la falsa está en tests | **Conservar**: el doble `AuthRepositorioFalso` justifica el puerto |
| `VehiculosRepositorio`, `SofasaRepositorio`, `UsuariosRepositorio` (13-15 líneas, una función cada uno) | clases concretas sin interfaz, sin doble en tests | **Conservar como están**: ya son pragmáticos (no hay interfaz de más). Solo se les da provider dentro de su feature |
| `InspeccionesRepositorio` | concreta, pero el test la implementa como interfaz implícita (`implements`) | Conservar; funciona por la interfaz implícita de Dart |
| `UsuariosRepositorio` en `custodia/data` | lista usuarios de `/api/usuarios` y devuelve `Usuario` de `auth/domain` | Conservar en custodia: solo la firma lo usa |
| `_usuariosProvider`, `inventarioProvider`, `_ordenesProvider` definidos en la página | `FutureProvider` dentro del archivo de presentation | **Correcto según la guía**: feature pequeña, no se crea controlador aparte |

Cadena actual de una lectura: `Page → FutureProvider → Repositorio → ClienteApi`. Cadena de una escritura:
`Page → reglas (domain) → ColaRepositorio → (luego) ProcesadorCola → Gateway → ClienteApi`. Ninguna de las dos es la
cadena excesiva que la guía rechaza; **no hay UseCases que eliminar ni que crear.**

### B5. Páginas con lógica de orquestación (riesgo menor)

`captura_page.dart` (363 líneas) y `firma_page.dart` (190) encolan el job, refrescan y sincronizan desde el widget
(`_cerrar`, `_registrar`). Las *reglas* ya están en domain (`bloqueoDeCierre`, `construirInformeJob`, `firmaValida`),
así que la página no esconde negocio; solo orquesta. La guía lo acepta en una feature de una pantalla. Se deja como
está en esta migración; si se quiere probar el flujo "cerrar informe" sin widget, un controlador Riverpod por
pantalla sería el siguiente paso, no este.

---

## 3. Lo que NO cambia

- Semántica de la cola offline (`procesador_cola.dart`, 33 pruebas) — es la pieza más crítica y no se toca por dentro.
- Contrato con la API: rutas, nombres de campos, `toJson/fromJson`, `test/contrato`.
- Esquema Drift, código generado `app_database.g.dart`, `schemaVersion`.
- Riverpod 3, go_router, Dio, Drift, flutter_secure_storage: el stack es el que la guía asume.
- Rutas de la app y el `redirect` de sesión.
- `core/tema`, `core/ui`, `core/util`, `core/errores`, `core/config`, `core/http`: son transversales de verdad.
- `core/dispositivo` (ubicación, fotos): lo usan `inspecciones` hoy y potencialmente otras; se queda en core.
- Nada fuera de `mobile/`.

---

## 4. Plan de migración propuesto (para ejecutar cuando se apruebe)

Orden pensado para que la app compile y las 129 pruebas pasen después de **cada** paso.

| Paso | Qué | Archivos | Riesgo |
|---|---|---|---|
| 0 | Línea base: `flutter analyze` + `flutter test` en verde antes de mover nada | — | — |
| 1 | Mover `CoordsDiagrama` → `vehiculos/domain/coords_diagrama.dart`; `FotoLocal`, `HallazgoLocal` → `inspecciones/domain/`. `job.dart` las importa | job.dart + 5 importadores + tests | bajo (solo imports) |
| 2 | `core/red/estado_red.dart` → `sync/data/` (y `Stream<bool> get cambios` entra al puerto `EstadoRed`); `core/db/cache_lecturas.dart` → `inspecciones/data/`; prueba de `esEstable` → `test/sync/` | 2 movimientos + puerto + proveedores + arnes + 1 test | bajo |
| 3 | `AlmacenSesion`: puerto a `auth/domain/`, `AlmacenSesionSeguro` a `auth/data/` | 1 split + proveedores + arnes | bajo |
| 4 | Providers por feature: `auth/`, `vehiculos/`, `inspecciones/`, `custodia/`, `sofasa/`, `sync/` declaran los suyos. `app/proveedores.dart` queda con entorno, BD, HTTP y nada más | 6 archivos nuevos + 9 páginas cambian import + arnes | medio (es el paso con más archivos, pero mecánico) |
| 5 | Actualizar `docs/arquitectura.md`, `README.md` (sección Estructura) y comentarios de encabezado | docs | — |
| 6 | Verificación final: `flutter analyze`, `flutter test`, `grep` de que ningún `core/` importa `features/` y ninguna feature importa `app/proveedores` | — | — |

Cada paso es un commit independiente dentro de `mobile/`.

### Criterio de "terminado"

```
grep -rn "features/" mobile/lib/core                        → 0 resultados
grep -rn "sync/" mobile/lib/features/vehiculos              → 0 resultados
grep -rln "app/proveedores" mobile/lib/features/*/presentation
    → solo login_page, captura_page y vin_page, y solo por entornoProvider o servicio*Provider
flutter analyze                                             → sin avisos
flutter test                                                → mismo resultado que la línea base
```

### Línea base medida (paso 0, 2026-10-16, Flutter 3.44.0 en Docker)

`flutter analyze`: sin avisos. `flutter test`: 168 pruebas, 167 en verde y 1 fallando **antes de tocar nada**:
`test/contrato/contrato_api_test.dart › campos que la app envía CrearInspeccionRequest`. El OpenAPI del backend en `QA`
ya no tiene `conductor_nombre`, `placa` ni `trailer` en `CrearInspeccionRequest` (cambio de la issue #92, pendiente en
mobile). No es de la migración y no se corrige aquí. El criterio de cada paso es reproducir exactamente ese resultado.

### Decisión tomada durante la ejecución

Los `providers.dart` de cada feature importan `app/proveedores.dart` para obtener `clienteApiProvider`,
`appDatabaseProvider` y `entornoProvider`. La alternativa (mover esos tres a `core`) no es posible sin romper la regla
"core no importa features", porque `clienteApiProvider` necesita el controlador de sesión para el token y el 401. `app`
es la raíz de composición y puede conocer a todos; lo que se evita es que `app` declare providers de una feature.

---

## 5. Lo que faltaba cubrir para que quede completo

Revisión hecha después del plan inicial. Cada punto entra al plan de la sección 4.

**Pruebas que cambian de import (9 referencias, 7 archivos).** Ningún test se reescribe; solo cambia la ruta:

| Archivo de prueba | Importa hoy |
|---|---|
| `test/support/arnes.dart` | `app/proveedores`, `core/red/estado_red`, `core/seguridad/almacen_sesion` |
| `test/support/dobles_sync.dart` | `features/sync/domain/job` |
| `test/sync/cola_drift_test.dart` | `core/db/cache_lecturas`, `features/sync/domain/job` |
| `test/sync/procesador_cola_test.dart` | `features/sync/domain/job` |
| `test/core/cliente_api_test.dart` | `core/red/estado_red` (prueba `esEstable`) |
| `test/inspecciones/reglas_informe_test.dart` | `features/sync/domain/job` |
| `test/widgets/lienzo_y_diagrama_test.dart` | `features/sync/domain/job` |

La prueba de `esEstable` que hoy vive en `test/core/cliente_api_test.dart` pasa a `test/sync/estado_red_test.dart`
cuando `estado_red` pase a `features/sync/data/`, para que la carpeta de pruebas siga reflejando la de código. El
adaptador falso de Dio que ambas pruebas necesitan se comparte desde `test/support/dio_falso.dart`.

**Puerto `EstadoRed` incompleto.** `sync_controller.dart` usa `red.cambios` (stream) que no está en la interfaz
`EstadoRed`; por eso `estadoRedProvider` está tipado con la clase concreta `EstadoRedConectividad` y el arnés hereda
de la concreta en vez de implementar el puerto. Al mover la clase a `sync/data` se agrega `Stream<bool> get cambios` al
puerto y el provider pasa a `Provider<EstadoRed>`. No cambia comportamiento: la implementación ya lo tenía.

**Docs con rutas.** `docs/desarrollo.md` menciona `lib/core/db/app_database.dart` (no se mueve, queda igual).
`docs/arquitectura.md` y `README.md` describen la estructura y el cableado: se actualizan en el paso 5. No hay otras
rutas de `lib/` citadas.

**`main.dart` y `app/app.dart`.** `main.dart` importa `app/proveedores.dart` para `entornoProvider`; sigue igual porque
el entorno se queda en `app/`. `app.dart` importa `sync_controller` directamente; no cambia.

**Esquema Drift.** `app_database.dart` y su `part 'app_database.g.dart'` no se mueven. No se corre `build_runner`.

**Android.** Nada en `android/` referencia rutas Dart. Sabores, `applicationId` y firma no cambian.

**Comentarios.** No se agregan comentarios nuevos. Los `///` existentes se conservan tal cual: son documentación del
código que ya está, no relleno; los que citan una ruta que cambia se corrigen en el mismo paso.

**Git.** El working tree del repo tiene `frontend/` borrado y `IA - Autos - Frontend/` sin trackear. Los commits de la
migración stagean solo rutas `mobile/...`; nunca `git add -A`. Se trabaja en una rama propia para no mezclar con `main`.

**Línea base ejecutable.** No hay `flutter` ni `dart` en esta máquina; hay Docker. El paso 0 corre
`flutter analyze && flutter test` con `ghcr.io/cirruslabs/flutter:stable` montando la raíz del repo en lectura, para
que `test/contrato` encuentre `backend/docs/openapi.json`. Se guarda el número de pruebas y el resultado de `analyze`
como referencia; el paso 6 debe dar exactamente lo mismo.

**Lo que no entra, a propósito.** Ningún cambio de comportamiento: no se toca la cola, las reglas de cierre, la firma,
los mensajes al usuario ni el contrato. Si durante la migración aparece algo que parece un bug, se anota y se
pregunta; no se corrige dentro de esta migración.

## 6. Prerrequisitos y dudas

**Herramientas.** En esta máquina no hay `flutter` ni `dart` en el PATH; sí hay Docker. Las pruebas se corren con la
imagen `ghcr.io/cirruslabs/flutter:stable` como describe `docs/desarrollo.md`. La primera ejecución descarga la imagen
(varios minutos). Hay que establecer la línea base (paso 0) **antes** de mover nada: si hoy algo ya falla, no se
puede atribuir a la migración.

**Prueba de contrato.** `test/contrato` lee `../backend/docs/openapi.json`. Solo lee; no se toca `backend/`. Si se
monta solo `mobile/` en Docker la prueba se omite con aviso: para que cuente hay que montar la raíz del repo (lectura).

**Decisiones a confirmar antes de ejecutar:**

1. ¿Providers por feature en un archivo `providers.dart` dentro de cada feature, o junto al repositorio en `data/`?
   Propuesta: `features/<f>/providers.dart` (un solo punto por feature, visible al abrir la carpeta).
2. ¿Se aprovecha para el controlador por pantalla en Captura y Firma (B5), o se deja para después?
   Propuesta: después. No es exigencia de la guía y mezcla refactor estructural con cambio de comportamiento probable.
3. `core/dispositivo`: ¿se queda en core (lo usa solo `inspecciones` hoy) o pasa a `inspecciones/data/`?
   Propuesta: se queda en core. Cámara y GPS son capacidades del dispositivo, no de una feature; custodia o vehículos
   podrían necesitarlas.
