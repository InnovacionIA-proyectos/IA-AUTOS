# iOS: compilar sin Mac y probar en un iPhone

Mientras no haya cuenta de Apple Developer (99 USD/año), la app se prueba así:

```
Codemagic (Mac en la nube)          Windows                           iPhone
compila el .ipa SIN firmar    →     Sideloadly lo firma con un   →    se instala y se prueba
(codemagic.yaml)                    Apple ID gratuito                 (vale 7 días)
```

## Qué hay en el proyecto

- `ios/`: un solo esquema (`Runner`), sin sabores. Identificador `co.com.boterosoto.trazaautos.qa`, nombre
  «Traza Autos QA», solo iPhone, iOS 13 o superior.
- `ios/Runner/Info.plist`: textos de permiso de cámara y ubicación (sin ellos iOS cierra la app al pedirlos).
- Configuración: la de QA (`config/qa.example.json`), que llama a la API por la pasarela `/contenedores` de IIS.
- `../codemagic.yaml`: flujo `ios-qa-sin-firma`. Corre las pruebas, compila en release y deja
  `traza-autos-qa-sin-firma.ipa` como artefacto. Se lanza a mano (no en cada push) para cuidar los 500 minutos
  gratuitos del mes.

## 1. Compilar en Codemagic

1. En la app IA-AUTOS de Codemagic, pestaña `codemagic.yaml`, elegir la rama y pulsar **Check for configuration files**.
2. **Start new build** → flujo `iOS QA sin firma (Sideloadly)`.
3. Al terminar, descargar `traza-autos-qa-sin-firma.ipa` de los artefactos del build.

## 2. Preparar Windows (una sola vez)

1. Instalar **iTunes de escritorio** desde apple.com (no la versión de Microsoft Store): Sideloadly la necesita para ver
   el iPhone por USB.
2. Instalar **Sideloadly** desde sideloadly.io.
3. Usar un **Apple ID secundario** creado para esto, no el personal: se escribe en una herramienta de terceros.

## 3. Preparar el iPhone (una sola vez)

1. Conectarlo por USB y aceptar **Confiar en este computador**.
2. Tras la primera instalación: **Ajustes → Privacidad y seguridad → Modo desarrollador** → activar y reiniciar
   (obligatorio desde iOS 16).
3. **Ajustes → General → VPN y gestión de dispositivos** → confiar en el perfil del Apple ID.

## 4. Firmar e instalar

En Sideloadly: arrastrar el `.ipa`, elegir el iPhone, escribir el Apple ID y pulsar **Start**.

## Límites de la cuenta gratuita

- La app deja de abrir a los **7 días**: se vuelve a firmar el mismo `.ipa` con Sideloadly (no hace falta compilar).
- Máximo **3 apps** instaladas así por iPhone.
- Sin TestFlight ni App Store: no se puede repartir a otras personas.
- Solo builds **release**: en iOS un build debug no abre sin una Mac conectada.

## Cuando haya cuenta de Apple Developer

Se conecta la clave de App Store Connect en Codemagic, se agrega firma automática al flujo y el `.ipa` se sube a
TestFlight. Para publicar habrá que separar sabores en iOS (esquemas de Xcode) si se quiere convivir con QA en el mismo
teléfono.
