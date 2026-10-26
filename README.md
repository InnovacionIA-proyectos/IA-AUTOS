# IA-AUTOS: Traza Autos 360 / Inventario Visual por VIN

Custodia e inspección de vehículos nuevos por VIN: carga masiva de unidades, informes de campo con evidencia fotográfica
geolocalizada, transferencia de custodia firmada y reportes de patrones de daño.

Este repositorio es la migración del sistema original (Next.js, Expo y PostgreSQL) a una arquitectura con tres piezas
claramente separadas, más la documentación que las guía.

```
IA-AUTOS/
  backend/    API .NET 10 + SQL Server 2019 (Clean Architecture, monolito modular)
  frontend/   Web de informes y administración, Angular 22
  mobile/     App de campo, Flutter (Android), con cola de envío offline
  docs/       planes de migración y diagrama de arquitectura
  docker-compose.yml   entorno local completo (base de datos, almacén de imágenes, API y web)
```

El ETL de Sofasa **no** forma parte de esta solución: ya está desplegado y sigue funcionando contra la API sin cambios de
contrato (solo cambia la URL de destino; ver [backend/docs/migracion-backend.md](backend/docs/migracion-backend.md)).

## Cómo encajan

```
 App móvil (Flutter) ──┐
                       ├── HTTPS / JSON ──>  API .NET 10  ──>  SQL Server 2019
 Web (Angular 22) ─────┘   (OpenAPI)             │
                                                 └──>  S3 / MinIO (fotos)
 ETL Sofasa ── POST /api/etl/sofasa/ordenes ─────┘
```

El contrato de la API vive en [backend/docs/openapi.json](backend/docs/openapi.json). La API lo genera, una prueba
detecta si se desvía, y de él salen los tipos de la web (`npm run api:generar`); la app móvil lo verifica con una prueba
de contrato.

## Cada pieza

| Pieza | Qué es | Documentación |
|---|---|---|
| [backend](backend) | API REST, JWT, SQL Server, subida de fotos a S3, reportes y PDF | [README](backend/README.md), [configuración](backend/docs/configuracion.md), [seguridad](backend/docs/seguridad.md), [API](backend/docs/api.md) |
| [frontend](frontend) | Tablero, listados, inventario con PDF, informes, Sofasa, carga de VINs por Excel y usuarios | [README](frontend/README.md), [autenticación y SSO](frontend/docs/autenticacion.md), [despliegue](frontend/docs/despliegue.md) |
| [mobile](mobile) | Login, VIN, captura de informes con fotos selladas, firma y cola offline | [README](mobile/README.md), [cola offline](mobile/docs/cola-offline.md), [configuración](mobile/docs/configuracion.md), [release](mobile/docs/release.md) |

Los planes que originaron la migración están en [docs/planes](docs/planes) y el diagrama en
[docs/arquitectura](docs/arquitectura). La especificación de negocio, las historias de usuario (Sprint 5 y 6), dónde vive cada pieza (la web es un módulo del portal de administración) y el índice de la documentación del backend están en [docs/README.md](docs/README.md).

## Puesta en marcha local

Requisitos: Docker. (Para trabajar sin instalar nada más, cada pieza documenta cómo usar contenedores.)

```bash
cp backend/.env.example backend/.env     # completar (ver backend/docs/configuracion.md)
docker compose up --build
```

- Web: http://localhost:4200 · API: http://localhost:8080 · almacén de imágenes de desarrollo: puerto 9090
- Para que el navegador pueda llamar a la API, en `backend/.env`: `Cors__Origins=http://localhost:4200`.
- Crear el primer usuario administrador: ver [backend/README.md](backend/README.md) (comando `crear-superadmin`, por ejemplo
  `docker compose run --rm -e SUPERADMIN_PASSWORD='<contraseña robusta>' api crear-superadmin admin@empresa.com "Nombre Apellido"`).
- La app móvil se compila aparte: [mobile/README.md](mobile/README.md).

## Pruebas

| Pieza | Comando | Qué cubre |
|---|---|---|
| backend | `dotnet test backend/IA-AUTOS.slnx` | unitarias, integración contra SQL Server real (Testcontainers) y contrato OpenAPI |
| frontend | `npm run test:ci` (en `frontend/`) | Vitest + Testing Library |
| mobile | `flutter test` (en `mobile/`) | dominio, cola offline, controladores, widgets y contrato |

## Dónde se configura cada cosa

Ningún secreto se versiona. Cada pieza documenta sus variables y cómo obtener sus valores:

- Backend (cadena de conexión, secreto JWT, token del ETL, S3, CORS): [backend/docs/configuracion.md](backend/docs/configuracion.md)
- Web (`API_URL`, `APK_URL`, inactividad, proveedor de login): [frontend/README.md](frontend/README.md#configuración-de-ejecución)
- App móvil (`API_URL`, `FLAVOR`, llave de firma del APK): [mobile/docs/configuracion.md](mobile/docs/configuracion.md)
