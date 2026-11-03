# Configuración de la app móvil

La app no lleva URLs ni secretos en el código. Todo se fija **al compilar**, con variables de compilación de Dart
(`--dart-define`). Ninguna es un secreto: lo que se compila dentro de un APK se puede extraer, así que nunca se pone allí
una clave.

## Variables

| Variable | Obligatoria | Valores | Descripción |
|---|---|---|---|
| `API_URL` | sí | URL sin barra final | dirección pública de la API (`https://...`); en dev puede ser `http://10.0.2.2:8080` (el emulador de Android ve el `localhost` del equipo como `10.0.2.2`) |
| `FLAVOR` | no | `dev`, `qa`, `prod` | entorno; debe coincidir con el sabor de Android (`--flavor`). Por defecto `dev` |
| `API_HOST_DESTINO` | no | host interno | servicio del cluster al que debe reenviar la pasarela `/contenedores` de IIS; la app lo manda en `X-Forwarded-Host` en toda petición (ver abajo). Vacío = se llama a `API_URL` directo |
| `EXTRAS_HABILITADOS` | no | `true`, `false` | muestra la sección de extras en la captura. Los extras se envían con el informe (contrato 2.14.0 del backend); prenderlo solo en el entorno que ya tiene esa versión desplegada |
| `GPS_PRECISION_MINIMA_M` | no | metros, entero | desde cuántos metros de margen del GPS se avisa de precisión baja. Por defecto 30, el mismo valor que el backend trae en `gps_precision_minima_m`. Se fija aquí porque `GET /api/parametros` es de administradores y del coordinador: quien está en el patio no puede leerlo |
| `ACOMODACION_FOTOS_MINIMAS` | no | entero | cuántas fotos de la acomodación pide la app antes de cerrar la segunda inspección del cargue. Por defecto 1, el mismo valor que el backend trae en `acomodacion_fotos_minimas`. Se fija aquí por la misma razón que `GPS_PRECISION_MINIMA_M`; si queda corto, el servidor rechaza el cierre y dice cuántas faltan |

Sin `API_URL` la app arranca pero muestra un aviso en el login y no deja ingresar.

## Cómo pasarlas

Cada entorno tiene un archivo JSON en `config/` (solo existen los `*.example.json` en el repositorio; los reales están en
`.gitignore`):

```bash
cp config/dev.example.json config/dev.json   # editar API_URL
flutter run --flavor dev --dart-define-from-file=config/dev.json
```

```json
{ "FLAVOR": "qa", "API_URL": "https://<url-de-la-api-qa>" }
```

## Pasarela `/contenedores` (IIS)

El backend vive en el cluster, solo por HTTP y en una IP privada (`10.10.40.17`), así que el teléfono no puede llamarlo
directo. En QA se consume a través del proxy inverso de IIS en `servicios.boterosoto.com`, que reenvía al servicio
indicado en la cabecera `X-Forwarded-Host`:

```json
{
  "FLAVOR": "qa",
  "API_URL": "https://servicios.boterosoto.com/contenedores",
  "API_HOST_DESTINO": "iaautos-backend-7cfmwx-4e79a5-10-10-40-17.sslip.io"
}
```

- La cabecera va en **todas** las peticiones (login incluido), en el chequeo `/health` de la cola y en las fotos.
- Las fotos llegan con URL firmada armada con el host que ve la API (el interno). La app rearma toda URL
  `/api/uploads/...` sobre `API_URL` conservando `exp` y `firma` (la firma solo depende del archivo y del vencimiento).
- Cómo leer las respuestas: **404 con HTML** (`X-Powered-By: ASP.NET`) = falta la cabecera, no salió de IIS;
  **404/401 con JSON** = llegó al backend.
- Prueba de humo: `curl -i -H "X-Forwarded-Host: <host>" https://servicios.boterosoto.com/contenedores/health` → 200 JSON.

## Cómo conseguir los valores

- **`API_URL`**: es la URL pública del despliegue de la API ([../backend](../backend)). En local, con
  `docker compose up` desde la raíz del repositorio, la API queda en `http://localhost:8080` (desde un emulador:
  `http://10.0.2.2:8080`; desde un teléfono físico en la misma red: `http://<IP-del-equipo>:8080`, solo con el sabor dev).
- **Origen CORS**: la app móvil no necesita CORS (no es un navegador).
- **Llave de firma del APK de producción**: ver [release.md](release.md). Es un secreto: se genera una sola vez, se guarda en
  un gestor de secretos del equipo y nunca se sube al repositorio.

## Permisos de Android

Declarados en `android/app/src/main/AndroidManifest.xml`:

| Permiso | Para qué |
|---|---|
| `INTERNET` | llamar a la API |
| `CAMERA` | evidencia fotográfica |
| `ACCESS_FINE_LOCATION`, `ACCESS_COARSE_LOCATION` | sellar informes y fotos con coordenadas |

La cámara y la ubicación se piden en el momento de usarlas. Si el usuario las niega, la app lo explica y permite reintentar
(y abrir los ajustes si el bloqueo es permanente).
