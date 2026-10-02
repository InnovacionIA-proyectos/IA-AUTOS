# Desarrollo y pruebas

## Con Flutter instalado

```bash
flutter pub get
flutter analyze
flutter test
```

Si cambia el esquema de Drift (`lib/core/db/app_database.dart`):

```bash
dart run build_runner build
```

El código generado (`app_database.g.dart`) se versiona.

## Sin Flutter instalado (Docker)

La imagen `ghcr.io/cirruslabs/flutter:stable` trae Flutter y el SDK de Android. Se usan volúmenes con nombre para no
descargar todo en cada ejecución:

```bash
docker volume create ia-autos-pub
docker volume create ia-autos-gradle

# Desde la raíz del repositorio (monta la raíz para que la prueba de contrato vea backend/docs/openapi.json):
docker run --rm \
  -v ia-autos-pub:/root/.pub-cache -v ia-autos-gradle:/root/.gradle \
  -v "$PWD:/repo" -w /repo/mobile \
  ghcr.io/cirruslabs/flutter:stable sh -c "flutter pub get && flutter analyze && flutter test"
```

En Git Bash de Windows anteponer `export MSYS_NO_PATHCONV=1` y usar rutas con `C:/...` para el montaje.

APK de depuración del sabor dev:

```bash
docker run --rm -v ia-autos-pub:/root/.pub-cache -v ia-autos-gradle:/root/.gradle \
  -v "$PWD:/repo" -w /repo/mobile ghcr.io/cirruslabs/flutter:stable \
  flutter build apk --debug --flavor dev --dart-define-from-file=config/dev.json
```

La primera compilación descarga Gradle y las dependencias de Android (varios minutos).

## Contrato con la API

`test/contrato/contrato_api_test.dart` lee `../backend/docs/openapi.json`. Si el archivo no está visible (por ejemplo, si se
monta solo `mobile/`), la prueba se **omite** con un aviso; para que cuente, ejecuta desde la raíz montada como arriba o
indica la ruta con la variable `OPENAPI_JSON`.

## Probar contra la API local

1. En la raíz: `docker compose up --build` (API en `http://localhost:8080`).
2. Crear un usuario de campo (ver `backend/README.md`, comando `crear-superadmin` y la API de usuarios).
3. En el emulador: `API_URL=http://10.0.2.2:8080` (sabor dev).
