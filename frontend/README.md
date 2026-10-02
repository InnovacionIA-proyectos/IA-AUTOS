# Traza Autos 360 - Web (Angular 22)

Web de informes y administración de Traza Autos 360. Reemplaza al frontend Next.js original y consume la API .NET de
[../backend](../backend). La app móvil de campo vive en [../mobile](../mobile).

## Stack

- Angular 22 (standalone, signals, sin zone.js, `OnPush`), TypeScript estricto.
- Tailwind CSS 4 con los tokens del sistema de diseño (Botero Soto). Fuentes Inter e IBM Plex Mono autohospedadas.
- Pruebas con Vitest + jsdom + Testing Library.
- Excel/CSV de carga de VINs: ExcelJS dentro de un Web Worker (carga diferida); QR de descarga con `qrcode`.

## Estructura

```
src/app/
  core/        config (config.json), api (cliente tipado), auth (sesión, guards, interceptores, inactividad), errors
  shared/      ui (botón, tabla, modal, campos, estados), util (formato, etiquetas, firma)
  layout/      shells (público, informes, gestión)
  features/    auth, dashboard, vehiculos, inspecciones, sofasa, usuarios, cargar-vins, descargar
public/        config.json (configuración de ejecución, sin secretos)
nginx/         plantilla de nginx y scripts de arranque de la imagen
```

## Rutas

| Ruta | Acceso | Contenido |
|---|---|---|
| `/login` | invitado | inicio de sesión |
| `/descargar` | público | enlace y QR de la app Android |
| `/` | admin, super_admin | tablero de patrones y tasa de rechazo |
| `/movilizados`, `/pendientes`, `/novedades` | admin, super_admin | listados paginados con búsqueda por VIN |
| `/inventario` | admin, super_admin | inventario por VIN y descarga del PDF |
| `/inspecciones`, `/inspecciones/:id` | admin, super_admin | informes y detalle con evidencias y firmas |
| `/sofasa` | admin, super_admin | órdenes sincronizadas desde Sofasa |
| `/cargar-vins` | admin, super_admin | carga masiva desde Excel/CSV |
| `/usuarios` | solo super_admin | gestión de usuarios |

Los guards solo mejoran la experiencia: la autorización real la impone siempre la API.

## Desarrollo

Requiere Node 22 o superior.

```bash
npm ci
npm start            # http://localhost:4200  (usa public/config.json: apiUrl http://localhost:8080)
npm run test:ci      # pruebas unitarias
npm run build        # compilación de producción en dist/traza-autos-web
```

La API debe aceptar el origen de la web: en `backend/.env`, `Cors__Origins=http://localhost:4200`.

Sin Node instalado se puede trabajar con Docker (el volumen con nombre evita lentitud de `node_modules` en Windows):

```bash
docker run --rm -v ia-autos-fe-nm:/repo/frontend/node_modules -v "$PWD/..:/repo" -w /repo/frontend node:24 sh -c "npm ci && npx ng test --no-watch"
```

## Contrato con la API

Los tipos de `src/app/core/api/api.d.ts` se generan desde `../backend/docs/openapi.json` (el backend lo verifica con una
prueba de deriva). Tras cambiar el contrato:

```bash
npm run api:generar
```

`api-tipos.ts` les da nombres cómodos; `api.service.ts` es el único punto que llama a la API.

## Configuración de ejecución

La web lee `config.json` al arrancar (no se compila dentro del bundle, así que una sola imagen sirve para todos los
entornos). No debe contener secretos: todo lo que está ahí es visible para el navegador.

| Clave | Variable de la imagen Docker | Descripción |
|---|---|---|
| `apiUrl` | `API_URL` | URL pública de la API, sin barra final |
| `apkUrl` | `APK_URL` | enlace de descarga del APK (vacío: la página `/descargar` avisa que falta configurarlo) |
| `inactividadMinutos` | `INACTIVIDAD_MINUTOS` | minutos sin actividad antes de cerrar la sesión (por defecto 30) |
| `proveedorLogin` | `PROVEEDOR_LOGIN` | `propio` (correo y contraseña) o `sso` (pendiente de implementar) |

Detalle de autenticación y cómo conectar un SSO: [docs/autenticacion.md](docs/autenticacion.md). Despliegue:
[docs/despliegue.md](docs/despliegue.md).
