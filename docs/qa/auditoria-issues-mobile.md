# Qué hay hecho de las 27 issues de mobile

Revisión del 28-oct-2026 sobre la rama `299-dibujo-v6-y-extras`, contrastando los criterios de
aceptación de cada issue contra el código. Cada veredicto dice **qué se comprobó**, para que quien
cierre la issue pueda verificarlo en un minuto.

Lo que no se comprobó aquí: el comportamiento en dispositivo de lo que ya se probó en sprints
anteriores. Donde el criterio pide una prueba de campo (instalar un APK, revisar con el negocio),
se dice.

| | Issues | Qué hacer |
|---|---|---|
| Listas | #163, #164, #166, #167, #168, #170, #199, #240, #296 | Cerrar |
| Lista salvo un criterio fuera de alcance | #208 | Cerrar; su CA-5 es de #212 |
| Hechas, bloqueadas por backend | #193, #299 | Esperar a #297 y #298 |
| A medias | #165, #216 | Completar |
| Sin empezar | #159, #187, #188, #201, #207, #212, #218, #273, #286, #291, #304, #313 | Pendientes |

---

## Listas para cerrar

### #163 — Llave de firma, sabores y configuración

Los cuatro criterios están.

- `android/app/build.gradle.kts` lee `android/key.properties` y, sin él, el release de `prod` falla
  con «Falta android/key.properties» (la excepción dice eso textualmente).
- Los tres sabores compilan con su `applicationId`: `.dev`, `.qa` y `co.com.boterosoto.trazaautos`,
  cada uno con su nombre en el teléfono.
- `docs/release.md` y `docs/configuracion.md` existen.
- `.gitignore` excluye `config/*.json` y conserva solo los `*.example.json`.

Queda por confirmar fuera del código: que la llave esté custodiada en el gestor de secretos.

### #164 — Selectores con solo valores activos

- `Catalogos.opciones()` filtra por `activo`, y `seleccionables()` además exige `codigo`: es lo que
  alimenta los selectores de clase y tamaño.
- `etiqueta()` busca entre **todos** los valores, activos e inactivos, que es lo que hace que un
  informe viejo con un valor ya retirado se siga pintando con su nombre.
- Lo que viaja a la API es el `codigo`; lo que se muestra es el `nombre`.

### #166 — Ingreso, cambio de PIN y cola por usuario

- Un 423 arma «Cuenta bloqueada hasta …» con `details.bloqueado_hasta`, y un 429 dice «Demasiados
  intentos».
- `PIN_CHANGE_REQUIRED` se detecta en **cualquier** llamada (`ClienteApi`), lleva al cambio de PIN y
  pausa la cola sin marcar el job como fallido.
- `Usuario.fromJson` admite correo nulo y lee `cedula` y `debe_cambiar_pin`.
- El menú y el router usan las capacidades de `GET /api/auth/me`; sin ellas se ve «Sin permisos».
- La cola separa lo de cada quien: los envíos ajenos se marcan «De otro usuario» y no se procesan.

### #168 — Hash, envíos fallidos y tarjetas por etapa

- El hash de `POST /api/uploads` se guarda en `FotoLocal` y se reenvía en la evidencia.
- `InformeJob` guarda por foto la precisión, el dispositivo, «sin ubicación» y el hash, y el usuario
  dueño.
- Un 4xx distinto de 409 deja el envío fallido y conserva los archivos.
- La tarjeta de cada etapa muestra el tipo de cierre de la transferencia y la fecha de cierre.
- `ETAPA_PREVIA_SIN_CIERRE` se traduce a «Falta cerrar la llegada de …» o «Falta cerrar la
  transferencia de …» según el motivo.

### #199 — Captura y confirmación

Los cinco criterios, **probados en dispositivo** el 28-oct:

- El selector ofrece clases, tamaños y niveles activos; el conductor no ve el nivel (DEC-003).
- `HallazgoLocal` y la cola guardan cuadrante, lado, clase, tamaño, nivel, nota, `es_preexistente` y
  `hallazgo_origen_id`, y el cuerpo va sin `parte_vehiculo` ni `coords_diagrama`.
- Al marcar un daño en una zona que ya traía uno de la misma clase, propone «Ya estaba registrado en
  {proceso} ({zona}, {clase})» con la foto previa. Funciona sin señal: los previos se guardan en
  Drift.
- Un 400 con `fieldErrors` llega a la cola con una línea por campo y no se reintenta.
- «No sale» se le ofrece solo al inspector del viaje y solo con un V1+ de una inspección cerrada;
  pide motivo y VIN de reemplazo; el 409 se muestra sin reintentar y la unidad queda «No sale» en
  Mis viajes.

Nota: los hallazgos previos no salen de `GET /api/inspecciones/{id}/hallazgos-previos`, porque esa
ruta pide una inspección ya creada y la captura arma el informe en el equipo. Salen del inventario
del VIN, que es la misma historia, y se guardan en Drift.

### #240 — Creación de hallazgo con zona y nivel

El criterio único está y se probó en dispositivo: el hallazgo llega con `cuadrante_numero`, `lado` y
`nivel_severidad`, y la página del VIN lo muestra sin romperse.

---

## Listas (con una nota)

### #167 — Checklist, guía de cámara y cierre

Cinco de seis criterios:

- El checklist filtra las vistas por el tipo de vehículo del VIN.
- La cámara usa la imagen guía de las cinco vistas que la tienen.
- El cargue con apoyo bloquea al conductor con «Este cargue tiene personal de apoyo: solo firmas el
  recibo».
- Un 400 al cerrar muestra el detalle línea por línea; un 409 se toma como éxito.
- Repetir una foto agrega otra evidencia de la misma vista sin borrar la anterior.

CA-6 cerrado el 28-oct: el umbral sale de `GPS_PRECISION_MINIMA_M` al compilar, con 30 metros de
valor inicial. No se lee de `GET /api/parametros`, donde el backend ya tiene `gps_precision_minima_m`,
porque esa ruta es de administradores y del coordinador: a quien está en el patio le responde 403.

### #170 — Firma, reservas y no firma

Los cinco criterios están implementados: firma por parte con su usuario dueño, «Pendiente de …», los
403/400/409 con su mensaje, el recibo con reservas solo para quien recibe y obligatorio no vacío, la
constancia de no firma con motivo, testigo de `GET /api/usuarios` (nunca una de las dos partes) y
foto, y el tipo de cierre al final.

CA-3 cerrado el 28-oct: el campo de observaciones se corta en los 1000 caracteres que admite el
servidor, en vez de dejar escribir de más y que lo rechace al enviar.

### #208 — Revisión de novedades en la app

CA-1 a CA-4 y CA-6, probados en dispositivo el 28-oct: la lista de novedades del conductor con su
clase, tamaño, hora, ubicación y conductor; «Revisar» con la foto, el dibujo y la hoja con el tamaño
cargado y el nivel obligatorio; la propuesta de «ya estaba registrado»; el 409 que se muestra y
recarga; y «Origen: conductor» en la ficha.

**CA-5 queda fuera:** pide que el coordinador las revise desde su bandeja, y la bandeja es #212, que
la propia issue declara fuera de alcance.

### #296 — Archivo v6 del dibujo

CA-1 a CA-5: el JSON `version: 6` con la rueda separada en cuatro cuadrantes (llanta en 6 y 26, rin
en 34 y 35), 7 paneles, 35 números laterales y 10 centrales, y las 7 imágenes PNG versionadas junto
al archivo.

CA-6 cerrado el 28-oct: `assets/dibujo_dano/catalogo-cuadrantes.csv`, 80 filas con número, lado,
nombre, nombre completo, área AIAG y en qué paneles se dibuja cada zona. No entra en el APK (el
pubspec solo empaqueta el JSON y la carpeta de imágenes). CA-7 a CA-9 son de proceso.

---

## Hechas, pero bloqueadas por el backend

Las dos tienen el mismo CA-9: «no sale a campo antes de que S6-HU-14.2 y S6-HU-14.3 estén cerradas»,
que son **#297** (resiembra del catálogo con la v6) y **#298** (dominio y contrato), las dos
abiertas.

Medido el 28-oct contra la API local: de las 80 zonas del dibujo v6, **34 no existen** en el catálogo
del servidor y las **46 restantes significan otra pieza** con el mismo número. Hasta que se siembre
la v6, lo que se marca se guarda mal o se rechaza con 400.

### #193 — Panel de daño en la app

CA-1, CA-2, CA-4, CA-5, CA-6, CA-7 y CA-8 están (CA-3 fue retirado). En la lectura, cada zona con
daño va del color de su nivel (V1+ y V1 rojo, V2 ámbar, V3 gris, azul «por revisar») y se toca para
ver su ficha con zona, clase, tamaño, nivel, preexistencia y foto.

Sobre «un punto numerado por hallazgo»: el dibujo ya pinta el número de cada zona y rige «un daño por
cuadrante», así que ese número **es** el del daño; lo que se agregó fue su color. Poner otro número
encima sería pintar el mismo dato dos veces.

### #299 — La app consume el catálogo del servidor

CA-1 a CA-8 están: la regla «el del servidor si está completo, si no la copia de los assets» sin
mezclar nunca polígonos de uno con imágenes del otro, las imágenes descargadas guardadas en Drift,
`evenOdd` para las piezas con hueco, el toque resuelto dentro del `InteractiveViewer` con la
tolerancia dividida por la escala, la pregunta cuando el toque cae en un borde, y ningún rango de
números escrito en la app.

---

## A medias

### #165 — Mis viajes y captura: mensajes y reglas nuevas

Está CA-5: `faltaTransporte` ya no existe.

Faltan:
- **CA-1:** un 403 al abrir un VIN o un informe no muestra «Esta unidad no está en tus viajes».
- **CA-4:** los viajes terminados no se consultan con `?estado=finalizado`.

CA-2 y CA-3 quedan por verificar en dispositivo.

### #216 — Alinear la app a v2

El grueso está: los modelos, el gateway, los repositorios y `contrato_api_test.dart` siguen el
contrato v2, y la prueba de contrato pasa contra `backend/docs/openapi.json`.

Falta CA-4 al pie de la letra:
- `selector_dano.dart` sigue existiendo (se usa, y #193 pide reutilizarlo: puede que el criterio haya
  quedado viejo).
- `etiquetas.dart` conserva más de los tres mapas que el criterio nombra: también momento, tipo de
  cierre, estado de viaje y tipo de novedad. Todos son valores fijos del contrato que la API no
  traduce, así que están donde deben; conviene corregir el criterio.

---

## Sin empezar

| Issue | Qué falta |
|---|---|
| #159 | La app no lee `version` ni `corrige_inspeccion_id`: no hay cadena de versiones |
| #187 | Las fotos siguen pidiéndose con `Image.network` y `Bearer` (CA-4 lo prohíbe explícitamente) |
| #188 | No existen las notas aclaratorias en la app |
| #201 | No existen `NovedadJob` ni `LlegadaJob` |
| #207 | No existe la pantalla del conductor |
| #212 | No existe la bandeja «Avisos» |
| #218 | Es despliegue, no código: APK firmado al piloto y corte del APK actual |
| #273 | No existen «No hay inspector» ni el recibo sin entrega |
| #286 | No existe la entrega agrupada por punto |
| #291 | No existe la lista de reclamo |
| #304 | No existe el flujo de acomodación |
| #313 | No existe «fijar ubicación aquí» |
