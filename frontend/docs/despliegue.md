# Despliegue de la web

La web es un sitio estático (Angular compilado) servido por nginx sin privilegios. No guarda estado ni secretos.

## Imagen Docker

```bash
docker build -t ia-autos-web ./frontend
docker run -p 8080:8080 \
  -e API_URL=https://<url-publica-de-la-api> \
  -e APK_URL=https://<url-del-apk> \
  ia-autos-web
```

- Escucha en el puerto 8080 y corre como usuario `nginx` (no root).
- Al arrancar genera `/config.json` desde las variables de entorno (ver README) y la política de seguridad de contenido
  (CSP) con `API_URL`. Cambiar la API no requiere recompilar.
- `GET /healthz` responde `200 ok` (usado por el HEALTHCHECK).

## Variables

| Variable | Obligatoria | Ejemplo |
|---|---|---|
| `API_URL` | sí | `https://api.botero.example` (sin barra final) |
| `APK_URL` | no | enlace público del APK |
| `INACTIVIDAD_MINUTOS` | no | `30` |
| `PROVEEDOR_LOGIN` | no | `propio` |

## Requisitos del backend

La API debe permitir el origen de la web: `Cors__Origins=https://<dominio-de-la-web>` (sin barra final). Si no, el
navegador bloqueará las llamadas.

## Cabeceras de seguridad que aplica nginx

`Content-Security-Policy` (solo este origen y la API; `frame-ancestors 'none'`), `X-Content-Type-Options`,
`X-Frame-Options`, `Referrer-Policy`, `Permissions-Policy` y `Strict-Transport-Security`. HSTS solo surte efecto bajo
HTTPS: el TLS lo termina el proxy o la plataforma de despliegue, no el contenedor.

## Caché

`config.json` e `index.html` no se cachean; el resto de archivos lleva hash en el nombre y se cachean un año.

## Entorno local completo

Desde la raíz del repositorio: `docker compose up --build` levanta SQL Server, el almacén de imágenes, la API y la web
(http://localhost:4200). Ver el encabezado de `docker-compose.yml`.

## Presupuesto de tamaño

El build de producción falla si el paquete inicial supera 1 MB (aviso a 500 kB). Hoy pesa unos 400 kB sin comprimir
(unos 89 kB transferidos). ExcelJS y la generación de QR se cargan solo al entrar a Cargar VINs y a Descargar.
