# Configuración de la app móvil

La app no lleva URLs ni secretos en el código. Todo se fija **al compilar**, con variables de compilación de Dart
(`--dart-define`). Ninguna es un secreto: lo que se compila dentro de un APK se puede extraer, así que nunca se pone allí
una clave.

## Variables

| Variable | Obligatoria | Valores | Descripción |
|---|---|---|---|
| `API_URL` | sí | URL sin barra final | dirección pública de la API (`https://...`); en dev puede ser `http://10.0.2.2:8080` (el emulador de Android ve el `localhost` del equipo como `10.0.2.2`) |
| `FLAVOR` | no | `dev`, `qa`, `prod` | entorno; debe coincidir con el sabor de Android (`--flavor`). Por defecto `dev` |
| `EXTRAS_HABILITADOS` | no | `true`, `false` | muestra la sección de extras en la captura. Apagado mientras el servidor no los reciba |
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
