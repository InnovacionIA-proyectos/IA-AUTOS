# Plan de migración móvil: React Native (Expo) a Flutter

Proyecto: Traza Autos 360° / Inventario Visual por VIN. Documento de planeación; no modifica el proyecto original. Fecha: 2026-10-01.

Planes hermanos: PLAN-MIGRACION-BACKEND.md (.NET 10 + SQL Server 2019) y PLAN-MIGRACION-ANGULAR.md (web).

Decisiones confirmadas que aplican: estado con Riverpod; login móvil con usuario y contraseña contra la API .NET; auditoría de acciones como requisito (la registra el backend); contratos generados desde el OpenAPI del backend.

---

La skill de .NET no define estándares para Flutter. Se aplica Clean Architecture por analogía y las convenciones propias de Dart (clases en UpperCamelCase, variables en lowerCamelCase, constantes en lowerCamelCase o `SCREAMING_CAPS` según la guía del equipo).

## F-1. Alcance

Se porta la app Expo: Login, AbrirVin, Vin, Captura, Firma, Sofasa. Alertas es un stub y se difiere. No se porta el código muerto (cliente Supabase y `LargeSecureStore`).

## F-2. Arquitectura

Feature-first, con capas `data / domain / presentation` por funcionalidad.

```
lib/
  core/        red, errores, tema, constantes, cliente HTTP
  features/
    auth/          data/ domain/ presentation/
    vehiculos/     (abrir VIN, inventario)
    inspecciones/  (captura, hallazgos, evidencias, cierre)
    custodia/      (firma)
    sofasa/
    sync/          (cola offline)
  main.dart
```

| Aspecto | Elección |
|---|---|
| Estado | Riverpod |
| HTTP | Dio con interceptor de autenticación y reintento controlado |
| Offline | Drift (SQLite): cola de jobs transaccional y persistente |
| Seguridad | flutter_secure_storage para el token |
| Dispositivo | camera, geolocator, connectivity_plus, signature; `CustomPainter` para el diagrama del vehículo |
| Contratos | Clientes Dart generados desde el OpenAPI de la API .NET |
| Entornos | Flavors de desarrollo, QA y producción; sin URLs de producción hardcodeadas |
| Navegación | go_router |

## F-3. Fases

- **M0. Contrato.** No iniciar hasta que la API .NET tenga OpenAPI estable en Auth, Inspecciones y Custodia (F4, pasos 2 y 4 del backend).
- **M1. Cimientos.** Proyecto, flavors, tema, cliente HTTP, login con usuario y contraseña y almacenamiento seguro del token.
- **M2. Lectura.** Abrir VIN, inventario y Sofasa.
- **M3. Cola offline.** Pieza más crítica. Debe reproducir la semántica actual:
  - Jobs reanudables, orden FIFO, un job a la vez.
  - Hasta 8 intentos.
  - 401, 408, 429 y 5xx transitorios.
  - 409 al cerrar tratado como éxito.
  - Red estable validada contra `/health`.
  - Pasos de un informe: crear inspección, subir cada foto y crear su evidencia, crear hallazgos, cerrar.
  Escribir los tests antes de portarla.
- **M4. Captura.** Diagrama de daños, fotos con GPS y cierre. Mejoras sobre el comportamiento actual:
  - Cada foto lleva su propio sello de fecha y ubicación (hoy todas llevan el del cierre).
  - Un 4xx no transitorio no descarta evidencia en silencio: el job queda en estado "fallido" visible, con reintento manual.
- **M5. Firma y custodia.** Firma de quien entrega y de quien recibe, con firma parcial acumulada. El servidor valida que quien firma sea el usuario autenticado.
- **M6. Release.** Firma del APK con clave propia (hoy usa la de debug), distribución controlada, piloto en campo.

## F-4. Estrategia de corte

Conviven el APK Expo y el de Flutter contra la misma API durante la ventana de transición. Se despliega a un grupo piloto y se retira Expo al terminar. Los usuarios deberán iniciar sesión de nuevo.

El corte móvil debe ocurrir **después** del corte del backend (fase F5), porque la app Flutter se desarrolla contra la API .NET con SQL Server.

## F-5. Pruebas

- Unitarias con `flutter_test` (reglas de cola y dominio).
- Widget tests de pantallas críticas.
- Integración con `integration_test` o Patrol: flujo completo con red simulada (corte y recuperación).
- Pruebas de dispositivo en gama baja y media (cámara, GPS, memoria).

## F-6. Riesgos y mitigaciones (móvil)

| Riesgo | Mitigación |
|---|---|
| Diferencias de comportamiento de la cola offline | Tests de caracterización de la cola actual antes de portarla |
| Permisos de cámara y GPS en Android | Pruebas en dispositivos reales y manejo explícito de denegación |
| Pérdida de evidencia en reintentos | Estado de job "fallido" visible y reintento manual |
| Pérdida de sesión al cambiar de app | Comunicar con anticipación; login simple |
| Desarrollo contra contrato inestable | M0: no iniciar antes del OpenAPI estable |


---

## Dependencias con los otros planes

1. M0 depende del OpenAPI estable de Auth, Inspecciones y Custodia (fase F4 del plan de backend).
2. El corte móvil ocurre después del corte del backend (fase F5), porque la app Flutter se desarrolla contra la API .NET con SQL Server.
3. El login web (plan Angular) no bloquea al móvil.
