# Compilar y publicar un release

## Reglas

- El APK de **producción** (`--flavor prod --release`) debe firmarse con una llave propia. Gradle se niega a compilarlo si
  falta `android/key.properties`. La app Expo firmaba con la llave de depuración; eso no se repite.
- Los sabores `dev` y `qa` en release usan la llave de depuración (son para pruebas internas).
- La llave y `key.properties` **nunca** se versionan (están en `.gitignore`).
- El `versionCode` (el número después de `+` en `version:` del `pubspec.yaml`) debe crecer en cada APK que se distribuya.
  La app Expo terminó en 11; esta empieza en 12.

## Crear la llave de firma (una sola vez)

```bash
keytool -genkeypair -v -keystore traza-autos-release.jks -alias traza -keyalg RSA -keysize 2048 -validity 10000
```

Guárdala, junto con sus contraseñas, en el gestor de secretos del equipo. **Si se pierde, no se podrán publicar
actualizaciones** que se instalen sobre la versión anterior.

Crea `android/key.properties` (no versionado):

```properties
storeFile=../traza-autos-release.jks
storePassword=<contraseña del almacén>
keyAlias=traza
keyPassword=<contraseña de la llave>
```

## Compilar

```bash
flutter build apk --release --flavor prod --dart-define-from-file=config/prod.json
# resultado: build/app/outputs/flutter-apk/app-prod-release.apk
```

Para QA: `--flavor qa --dart-define-from-file=config/qa.json`.

## Distribución

El APK se aloja fuera de este repositorio (hoy en el almacén S3/MinIO de la empresa). El enlace público se configura en la
web con la variable `APK_URL` de la imagen del frontend: la página `/descargar` muestra el botón y el QR.

## Lista de verificación antes de distribuir

1. `flutter analyze` y `flutter test` en verde.
2. Prueba en un teléfono real de gama baja o media: cámara, GPS, permisos y memoria.
3. Flujo completo con red cortada y recuperada: capturar un informe sin señal, comprobar que queda en la cola y que se
   envía al volver la conexión; revisar que un envío con error aparece en "Envíos pendientes".
4. Piloto con un grupo reducido antes de retirar la app Expo. Los usuarios deben iniciar sesión de nuevo.
5. El corte móvil ocurre **después** del corte del backend: esta app solo funciona contra la API .NET.
