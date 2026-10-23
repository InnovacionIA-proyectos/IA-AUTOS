# Plan de migración web: Next.js a Angular

> **Estado: retirado.** La web de IA Autos **no vive en este repositorio**: es un **módulo del portal de administración**, en el repositorio [`administracion`](https://github.com/InnovacionIA-proyectos/administracion), carpeta
> [`src/app/ia-autos`](https://github.com/InnovacionIA-proyectos/administracion/tree/QA/src/app/ia-autos). Entra por la sesión del portal (inicio de sesión único) y consume el mismo contrato OpenAPI v2 que la app
> ([guía v1 → v2](../../backend/docs/migracion-v1-v2.md)). El front Angular 22 de `frontend/` queda **retirado** (HU S6-HU-08); este documento se conserva como registro de la planeación original y **no describe el estado actual**.
> Dónde está cada pieza y las historias de usuario: [docs/README.md](../README.md).

Proyecto: Traza Autos 360° / Inventario Visual por VIN. Documento de planeación; no modifica el proyecto original. Fecha: 2026-10-01.

Planes hermanos en esta carpeta: PLAN-MIGRACION-BACKEND.md (.NET 10 + SQL Server 2019) y PLAN-MIGRACION-FLUTTER.md (móvil).

---

## 0. Resumen ejecutivo

Hoy la web es la parte de interfaz de un proyecto Next.js 14 que también aloja la API. Al migrar el backend a .NET 10, la web queda como cliente puro de la API. Este plan reemplaza esa interfaz por una SPA Angular.

### Decisiones confirmadas

| # | Decisión |
|---|---|
| 1 | La API pasa a .NET 10 con SQL Server 2019 (plan de backend). La web solo consume contratos OpenAPI. |
| 2 | Tablas `tbl*`, auditoría de acciones como requisito: son del backend; la web no accede a la base de datos. |
| 3 | Login web: **pendiente** de los dos artefactos de requerimientos (aún no he podido leerlos; el usuario compartirá los accesos). El plan lo deja detrás de una abstracción intercambiable. |
| 4 | Logs estructurados con correlación: opcionales, no se implementan por defecto. |

### Supuestos a confirmar

1. La versión de Angular será la última estable en el momento de iniciar (v20 o superior), con componentes standalone y signals.
2. Las convenciones `cls`/`tbl` de la skill de .NET aplican a C# y a la base de datos, no a TypeScript. En Angular se usa la guía de estilo oficial (archivos en kebab-case, clases en PascalCase con sufijo: `Component`, `Service`, `Guard`). Confirmar si el equipo quiere otra convención.
3. La paridad funcional se toma del código actual. Falta un inventario visual de pantallas (capturas) para validar diseño y comportamiento; ver sección 2.

---

## 1. Análisis del sistema web actual

### 1.1 Stack actual (parte web)

- Next.js 14.2 (App Router), React 18, Tailwind 3.4, lucide-react.
- `xlsx` 0.18.5 para leer Excel en el navegador.
- Sesión en `localStorage` (clave `ivvin.sesion.v1`); control de acceso por rol solo del lado cliente (`RoleGuard` en `AppShell.tsx`).
- Cliente Supabase creado pero sin uso real.
- Variables de build: URL de la API, URL de Supabase, clave anónima, bandera de autenticación de desarrollo, DSN de Sentry (este último sin uso real).

### 1.2 Páginas y funciones identificadas en el código

| Ruta actual | Función | Consume |
|---|---|---|
| `/` | Tablero: patrones de daño y tasa de rechazo | GET `/api/patrones`, `/api/metricas/rechazo` |
| Listados (componente `ListadoVehiculos`) | Movilizados, pendientes y novedades, paginados | GET `/api/vehiculos/listado` |
| `/cargar-vins` | Carga masiva desde Excel o CSV: hoja, fila de encabezados, detección de columnas, operación, marca | POST `/api/vehiculos/cargar` |
| `/inventario` | Búsqueda por VIN con autocompletado, inventario y PDF | GET `/api/vehiculos`, `/:vin/inventario`, `/inventario/pdf` |
| `/inspecciones/[id]` | Informe detallado de la inspección | GET `/api/inspecciones/:id` |
| `/sofasa` | Órdenes Sofasa | GET `/api/sofasa/ordenes` |
| `/usuarios` | Gestión de usuarios (solo super_admin) | GET/POST/PATCH/DELETE `/api/usuarios` |
| `/descargar` | Descarga del APK (URL hardcodeada) | Enlace externo |
| Login | Inicio de sesión | POST `/api/auth/login`, GET `/api/auth/me` |

La web admite únicamente los roles admin y super_admin. Los roles coordinador_calidad y comercial existen solo vía API.

### 1.3 Riesgos del sistema web actual

- Sesión y JWT (30 días) en `localStorage`: expuestos ante XSS; sin revocación.
- Autorización de páginas solo en el cliente; la seguridad real depende de la API (que hoy tampoco comprueba propiedad del objeto).
- `xlsx` 0.18.5 con vulnerabilidades conocidas, usado sobre archivos subidos por el usuario.
- URLs de producción hardcodeadas (por ejemplo, la del APK en `app/descargar/page.tsx`).
- Sin cabeceras de seguridad (CSP, HSTS) en `next.config.mjs`.
- Modo de autenticación de desarrollo (`NEXT_PUBLIC_DEV_AUTH`) en variables de build.
- Cero pruebas de interfaz.

---

## 2. Alcance y paridad

- Se migran todas las pantallas de la sección 1.2 con paridad funcional, más las correcciones de seguridad del plan.
- No se migran el cliente Supabase ni los componentes sin uso.
- Antes de iniciar: levantar un inventario visual (capturas de cada pantalla y estados: vacío, carga, error, sin permiso) y confirmar con el negocio qué cambia y qué se mantiene. Este inventario no existe todavía.

---

## 3. Diseño de arquitectura

### 3.1 Principios

SPA Angular con componentes standalone, signals para estado local y de aplicación, y organización por funcionalidades (feature-first). KISS: no se introduce NgRx completo salvo que una funcionalidad lo justifique.

### 3.2 Estructura de capas

| Capa | Contenido |
|---|---|
| `core/` | Configuración en tiempo de ejecución, interceptores HTTP, autenticación, guards, manejo global de errores, cliente API generado |
| `shared/` | Componentes de interfaz reutilizables, pipes, directivas, modelos de presentación |
| `features/` | Una carpeta por funcionalidad: `dashboard`, `vehiculos`, `inspecciones`, `sofasa`, `usuarios`, `carga-vins`, `descargas`, `auth` |
| `layout/` | Shell de la aplicación (barra lateral, cabecera, menú por rol) |

### 3.3 Estructura de carpetas

```
web-angular/
  src/
    app/
      core/
        auth/          auth.service, auth-provider (interfaz), guards/, interceptors/
        api/           cliente generado desde OpenAPI (no editar a mano)
        config/        runtime-config.service (carga config.json al arrancar)
        errors/        manejo global de errores
      shared/          ui/, pipes/, directives/
      layout/          app-shell, sidebar, header
      features/
        auth/          login
        dashboard/     patrones, metricas
        vehiculos/     listados (movilizados, pendientes, novedad), inventario
        inspecciones/  informe-detalle
        sofasa/        ordenes
        usuarios/      lista, formulario
        carga-vins/    carga masiva
        descargas/     pagina del APK
      app.routes.ts
    assets/ , environments/ (sin secretos)
  public/config.json   (configuración de tiempo de ejecución, sin secretos)
  tests/               e2e/
  Dockerfile  docker-compose.yml  nginx.conf  .gitignore  docs/
```

### 3.4 Decisiones técnicas

| Tema | Decisión | Justificación |
|---|---|---|
| Componentes | Standalone, `OnPush`, control flow nativo | Estándar actual de Angular |
| Estado | Signals y servicios por funcionalidad | Suficiente para una app administrativa; evita complejidad |
| Enrutado | Carga diferida por funcionalidad; guards funcionales | Menor bundle inicial; control de acceso explícito |
| HTTP | `HttpClient` con interceptores (token, errores, reintentos acotados) | Centraliza manejo del 401/403 |
| Contratos | Cliente TypeScript generado desde el OpenAPI de .NET (por ejemplo con OpenAPI Generator o ng-openapi-gen) | Una sola fuente de verdad, sin tipos duplicados |
| Formularios | Reactive Forms tipados | Validación explícita; reemplaza el parseo suelto |
| Estilos / UI | Tailwind 3.4 (continuidad del diseño actual) con Angular CDK; iconos con lucide-angular. Alternativa: Angular Material | Mantiene identidad visual y reduce el rediseño; confirmar con el equipo de diseño |
| Gráficos | Librería de gráficos a elegir (por ejemplo ECharts o Chart.js) para tablero y mapa de calor | Hoy el tablero usa patrones y un diagrama del vehículo; validar en el inventario visual |
| i18n | Español como idioma base; preparar textos para i18n solo si el negocio lo pide | YAGNI |
| Configuración | `config.json` cargado al arrancar (URL de API, URL del APK, proveedor de login); nunca secretos en el bundle | Un mismo build para todos los entornos |

---

## 4. Autenticación y autorización web

**Estado: pendiente de los artefactos de requerimientos.** Se diseña para soportar ambos escenarios sin reescribir el resto.

### 4.1 Abstracción

Un `InjectionToken<AuthProvider>` con la interfaz: `iniciarSesion()`, `cerrarSesion()`, `obtenerToken()`, `usuarioActual()`, `rolesActuales()`. El resto de la aplicación depende solo de esa interfaz y de los guards.

### 4.2 Escenario A. Login propio (usuario y contraseña)

- Formulario de login contra `POST /auth/login` de la API .NET.
- Almacenamiento del token: preferir memoria más un refresh token en cookie `HttpOnly; Secure; SameSite` (requiere soporte del backend). Evitar `localStorage` para el token, que es el riesgo actual. Si el backend no puede dar cookie, aceptar `sessionStorage` y JWT de vida corta con refresh.
- Cierre de sesión por inactividad y al recibir 401.

### 4.3 Escenario B. SSO (OIDC, por ejemplo Microsoft Entra ID)

- Flujo Authorization Code con PKCE (por ejemplo con `angular-oauth2-oidc` o MSAL Angular). Sin secretos en el cliente.
- El backend valida el token del proveedor y mapea roles. Definir con los artefactos: proveedor, claims de rol, restricción de dominio de correo y flujo de usuarios no registrados.

### 4.4 Autorización

- Guards funcionales de rutas: `authGuard` (hay sesión) y `roleGuard` (rol permitido). La web admite admin y super_admin; `/usuarios` solo super_admin.
- Menú y acciones visibles según rol (solo experiencia de usuario). **La seguridad real la impone la API.** El plan de backend corrige los huecos de propiedad del objeto.
- Eliminar el modo de autenticación de desarrollo de los builds de producción.

---

## 5. Plan por fases

### A0. Prerrequisitos

- OpenAPI estable de la API .NET (al menos Auth, Vehiculos, Inspecciones, Reportes, Usuarios, Sofasa). Depende de las fases F2 a F4 del plan de backend.
- Acceso a los artefactos de login web y decisión del escenario (A o B).
- Inventario visual de pantallas y acuerdo de paridad con el negocio.
- Decisión de la librería de interfaz y de gráficos.

### A1. Cimientos

- Proyecto Angular, linting, formato, configuración de entornos por `config.json`, cliente API generado, interceptores, manejo global de errores, shell de la aplicación, Dockerfile, docker-compose, `.gitignore` y CI.

### A2. Autenticación

- Implementación del escenario elegido detrás de `AuthProvider`, guards, manejo de sesión expirada, pantalla de login y de "sin permiso".

### A3. Pantallas de solo lectura

- Tablero (patrones y rechazo), listados de movilizados, pendientes y novedades, inventario por VIN con descarga de PDF, informe de inspección, órdenes Sofasa.

### A4. Pantallas de administración

- Gestión de usuarios (solo super_admin) y carga masiva de VINs. Esta última exige cuidado:
  - Hoy el Excel se parsea en el navegador con `xlsx` 0.18.5 (vulnerable).
  - **Decisión a tomar:** (a) mantener parseo en el navegador con una librería mantenida (por ejemplo ExcelJS, o la versión vigente de SheetJS) dentro de un Web Worker y con límites de tamaño; o (b) subir el archivo al backend y parsear allí (ClosedXML), lo que exige un endpoint nuevo. Se recomienda (a) para conservar el contrato actual y la detección de columnas en el cliente, con tope de 5000 filas.
  - Conservar el flujo: selección de hoja, fila de encabezados, detección de columnas por patrones (VIN, chasis, marca, modelo, color), operación y marca fija opcionales, reporte de errores por VIN.

### A5. Descargas y detalles finales

- Página del APK con la URL tomada de la configuración (no hardcodeada), páginas de error, estados vacíos y de carga.

### A6. Pruebas, endurecimiento y corte

- Pruebas completas (sección 7), cabeceras de seguridad, auditoría de dependencias.
- Despliegue paralelo de la SPA contra la API .NET. Validación con usuarios administradores.
- Corte: el dominio de la web pasa a la SPA. La web antigua se mantiene disponible durante una ventana de observación con rollback inmediato.

### A7. Cierre

- Retirar la interfaz de Next.js, eliminar variables de Supabase y de autenticación de desarrollo, archivar el código antiguo.

---

## 6. Dependencias con los otros planes

1. Angular depende del OpenAPI del backend (A0). Se puede iniciar A1 en paralelo con un contrato provisional.
2. El Next.js actual ya puede apuntar a otra API mediante su variable de URL, por lo que la interfaz actual puede seguir operando contra la API .NET durante la transición. Orden recomendado: primero el corte del backend, después el de la web.
3. El login web (A2) está bloqueado por los artefactos; no bloquea el backend ni el móvil.
4. Si se elige almacenamiento de refresh token en cookie, el backend debe soportarlo (CORS con credenciales y cookies seguras); esto debe entrar en el diseño de Auth del plan de backend.

---

## 7. Testing

| Nivel | Herramienta | Alcance |
|---|---|---|
| Unitarios | Vitest o Jest (según lo que soporte la versión elegida) | servicios, guards, pipes, lógica de detección de columnas del Excel |
| Componentes | Angular Testing Library | formularios, tablas, estados de carga y error |
| Contrato | Verificación del cliente generado contra el OpenAPI | detectar cambios incompatibles en CI |
| E2E | Playwright | login, tablero, listados, inventario con PDF, carga masiva, gestión de usuarios con permisos por rol |
| Accesibilidad | axe en CI | navegación por teclado y contraste |

Hoy no existen pruebas de interfaz; esta es una mejora neta. Todo debe ejecutarse automáticamente en CI.

---

## 8. Docker y despliegue

- `Dockerfile` multi-stage: Node para compilar la SPA y nginx sin privilegios (no-root) para servirla.
- `nginx.conf`: fallback a `index.html` para el enrutado de la SPA, compresión, caché de activos con hash, y cabeceras de seguridad (CSP, HSTS, X-Content-Type-Options, Referrer-Policy).
- `docker-compose.yml` con perfiles de desarrollo y QA; la API .NET y SQL Server se levantan desde el compose del backend.
- La configuración de tiempo de ejecución (`config.json`) se monta por entorno; sin secretos.
- Alternativa de despliegue: servir la SPA como archivos estáticos desde la propia API ASP.NET (mismo origen, sin CORS). Más simple; menos independencia de despliegue. Decisión a tomar junto con la infraestructura del plan de backend.
- `.gitignore` excluye `.env`, `node_modules/`, `dist/`, `.angular/` y archivos temporales.

## 9. DevOps

- CI: instalación con lockfile, lint, pruebas unitarias, generación y verificación del cliente OpenAPI, build de producción, E2E contra la API en contenedor, escaneo de dependencias y construcción de imagen.
- Entornos: local, desarrollo, QA, producción, con aprobación manual a producción.
- Auditoría de acciones: la registra el backend; la web solo envía las llamadas autenticadas.

---

## 10. Documentación a entregar

Sin emojis: descripción del proyecto, arquitectura, estructura de carpetas, configuración por entorno, flujo de autenticación elegido, catálogo de pantallas con su correspondencia con las rutas antiguas, guía de ejecución local, guía de pruebas, guía de despliegue y runbook de corte y rollback.

---

## 11. Recomendaciones de producción

- No almacenar el token en `localStorage`; ver sección 4.
- Aplicar CSP estricta y reducir superficie de XSS (sin `innerHTML` sin sanitizar, sin scripts de terceros innecesarios).
- Sustituir `xlsx` 0.18.5 y auditar dependencias en CI.
- Retirar el modo de autenticación de desarrollo y las URLs hardcodeadas.
- Cierre de sesión por inactividad, y manejo uniforme de 401/403.
- Monitoreo de errores en el cliente solo si el negocio lo solicita (Sentry hoy no se usa realmente).

---

## 12. Riesgos y mitigaciones

| Riesgo | Mitigación |
|---|---|
| Login web sin definir | Abstracción `AuthProvider`; cierre al leer los artefactos |
| Paridad funcional incompleta (no hay inventario visual) | Inventario de pantallas y validación con el negocio antes de A3 |
| Contrato de la API inestable | Iniciar tras OpenAPI estable; cliente generado y verificación en CI |
| Token expuesto por XSS | Memoria más cookie HttpOnly o sesión corta; CSP |
| Vulnerabilidades en el parseo de Excel | Librería mantenida, Web Worker, límites de tamaño y de filas |
| Cookies entre dominios (CORS con credenciales) | Preferir mismo origen o subdominios del mismo sitio; definir con el backend |
| Cambio de experiencia para administradores | Piloto con usuarios clave y ventana de coexistencia con rollback |
| Gráficos y mapa de calor distintos a los actuales | Validar con el inventario visual; elegir librería antes de A3 |
| Sobreingeniería (NgRx, microfrontends) | Signals y servicios; sin microfrontends |

## 13. Pendientes de confirmación

1. Acceso a los dos artefactos de requerimientos del login web.
2. Escenario de autenticación (A: login propio, B: SSO) y, si es B, proveedor.
3. Librería de interfaz (Tailwind con CDK o Angular Material) y de gráficos.
4. Parseo del Excel: navegador (recomendado) o servidor.
5. Servir la SPA con nginx separado o desde la API ASP.NET.
6. Inventario visual de pantallas (capturas) para validar paridad.
7. Convención de nombres para TypeScript (guía oficial de Angular, salvo otra indicación).

---

## Anexo. Prompt para generar el diagrama de la web Angular (ChatGPT)

```
Actúa como arquitecto de software front-end. Genera, en Mermaid (y una imagen limpia si puedes), (1) un diagrama C4 nivel 2 que muestre una SPA Angular consumiendo una API ASP.NET Core (.NET 10) con SQL Server 2019, y (2) un diagrama C4 nivel 3 de la SPA con sus módulos. Sin emojis.

SPA ANGULAR (componentes standalone, signals):
- core: configuración en tiempo de ejecución (config.json), interceptores HTTP (token, errores), AuthProvider intercambiable (login propio o SSO OIDC con PKCE), guards funcionales (auth y rol), cliente API generado desde OpenAPI, manejo global de errores.
- layout: shell con barra lateral y cabecera con menú por rol.
- features (carga diferida): auth/login, dashboard (patrones y tasa de rechazo), vehiculos (listados movilizados, pendientes y novedad; inventario por VIN con PDF), inspecciones (informe detalle), sofasa (órdenes), usuarios (solo super_admin), carga-vins (Excel/CSV parseado en el navegador con Web Worker, tope de 5000 filas), descargas (APK).
- shared: componentes de interfaz, pipes, directivas.

ACTORES: Admin y Super Admin (web).
SISTEMAS: API ASP.NET Core (Clean Architecture, 4 capas), SQL Server 2019, MinIO/S3 (fotos servidas por la API), proveedor de identidad (si hay SSO), servidor nginx no-root que sirve la SPA con cabeceras de seguridad.
RELACIONES: navegador -> nginx (HTTPS, archivos estáticos); SPA -> API (HTTPS, JWT Bearer o cookie segura); API -> SQL Server; API -> MinIO; SPA -> proveedor de identidad (OIDC, solo escenario SSO).
Incluye fronteras de confianza (navegador / servidor web / API / datos), leyenda por colores y una breve explicación de cada relación. No incluyas credenciales ni valores reales.
```
