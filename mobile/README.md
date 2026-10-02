# Traza Autos 360 - App móvil (Flutter)

App de campo para inspectores y conductores: abre un VIN, revisa su último informe, captura informes con novedades sobre
el diagrama del vehículo, fotos selladas con fecha y ubicación, y firma el cambio de responsabilidad. Reemplaza a la app
Expo (React Native) y consume la API .NET de [../backend](../backend).

Funciona **sin señal**: los informes y las firmas se guardan en una cola persistente (SQLite) y se envían solos cuando
hay una conexión estable.

## Alcance

| Pantalla | Qué hace |
|---|---|
| Login | correo y contraseña contra la API; el token se guarda en el Keystore de Android |
| Mis inspecciones | listado con caché local (se ve sin señal), barra de sincronización y botón de nueva inspección |
| Abrir VIN / VIN | inventario, último informe con fotos y diagrama, y el estado de cada etapa de la ruta de custodia |
| Captura | datos del transporte, novedades sobre el diagrama, fotos selladas, notas y cierre del informe |
| Firma | firma de quien entrega y de quien recibe (parcial o completa) |
| Envíos pendientes | lo que espera señal; los envíos con error se ven aquí con su motivo, y se reintentan o descartan a mano |
| Sofasa | órdenes de carga sincronizadas por el ETL (solo lectura) |

No se porta: la pestaña Alertas (era un marcador sin funcionalidad), el cliente Supabase ni `LargeSecureStore` (código
muerto de la app original).

## Estructura

```
lib/
  app/        proveedores (Riverpod), router (go_router), TrazaApp
  core/       config (entornos), http (Dio), red, db (Drift), seguridad, dispositivo, tema, ui
  features/
    auth/         sesión y login
    vehiculos/    inventario, catálogo de partes del diagrama, etiquetas
    inspecciones/ listado, captura, reglas de cierre
    custodia/     firma
    sofasa/
    sync/         cola offline (dominio, Drift, API) y barra de sincronización
test/             unitarias, de widgets y de contrato con la API
```

Cada funcionalidad se organiza en `domain` (reglas y modelos, sin Flutter), `data` (API y persistencia) y `presentation`.
Más detalle en [docs/arquitectura.md](docs/arquitectura.md); la cola en [docs/cola-offline.md](docs/cola-offline.md).

## Puesta en marcha

Requiere Flutter 3.44 o superior (Dart 3.12). Sin Flutter instalado se puede usar la imagen de Docker
`ghcr.io/cirruslabs/flutter:stable` (ver [docs/desarrollo.md](docs/desarrollo.md)).

```bash
cp config/dev.example.json config/dev.json        # ajustar API_URL
flutter pub get
flutter run --flavor dev --dart-define-from-file=config/dev.json
flutter test
flutter analyze
```

La dirección de la API y el entorno se fijan **al compilar** con `--dart-define-from-file` (no hay URLs de producción en
el código). Detalle y valores en [docs/configuracion.md](docs/configuracion.md).

## Entornos (sabores)

| Sabor | applicationId | Nombre en el teléfono | HTTP |
|---|---|---|---|
| dev | `co.com.boterosoto.trazaautos.dev` | Traza Autos DEV | admite `http` (emulador) |
| qa | `co.com.boterosoto.trazaautos.qa` | Traza Autos QA | solo `https` |
| prod | `co.com.boterosoto.trazaautos` | Traza Autos 360 | solo `https` |

Los tres pueden convivir en el mismo teléfono, y con la app Expo anterior (`co.com.boterosoto.inventariovin`) durante la
transición. Compilar y firmar un release: [docs/release.md](docs/release.md).

## Decisiones y diferencias con el plan

- **Firma**: se implementó un lienzo propio con `CustomPainter` en lugar del paquete `signature`, porque la web dibuja
  la firma desde un *path SVG* (`M x y L x y ...`) y el paquete no entrega ese formato.
- **Contratos**: no hay un generador de clientes Dart para este OpenAPI; los modelos son manuales y una prueba
  (`test/contrato`) los ata a `backend/docs/openapi.json` para detectar cambios de contrato.
- **Tipografía**: usa la fuente del sistema. Inter e IBM Plex Mono (las del sistema de diseño) quedan pendientes de
  empaquetar como activos; el VIN y la placa usan fuente monoespaciada del sistema.
- **Enlaces profundos** (`ivvin://`) y **escaneo de VIN**: no se portaron (la app original los declaraba pero no los
  usaba para nada visible).
