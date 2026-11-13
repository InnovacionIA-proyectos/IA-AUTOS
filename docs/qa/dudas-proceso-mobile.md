# Dudas del proceso en la app, con lo que hace el código hoy

Ocho puntos que salieron de probar la app en el celular el 3-nov-2026 contra el contrato 3.0.0. Cada uno
dice **qué hace el código hoy** (comprobado, con el archivo y la línea), **qué falta decidir** y **una
propuesta** lo bastante concreta para abrir la issue.

Lo que está escrito como «hoy» se verificó leyendo el código y, donde se dice, llamando a la API local.
Lo que está escrito como «propuesta» es nuestro, no de negocio: hay que confirmarlo con Calidad.

Resumen de dónde cae cada uno:

| # | Duda | Dónde se arregla |
|---|---|---|
| 1 | Un extra con daño no se puede clasificar | Contrato + backend + app |
| 2 | La acomodación se repite en cada unidad | App |
| 3 | Un informe en borrador no se puede cerrar ni descartar | App (y backend si se quiere descartar) |
| 4 | El descargue vuelve a pedir las 7 fotos | Negocio primero; luego backend + app |
| 5 | Los daños no se arrastran por el viaje | App (el dato ya está) |
| 6 | La transferencia del descargue la firman los mismos dos | Negocio primero; luego backend + app |
| 7 | Qué ve el conductor de los hallazgos | Negocio (respondido en parte) |
| 8 | Quién confirma la ubicación de un lugar | Negocio + app |

---

## Estado al 12-nov-2026

Tres de los ocho se resolvieron con el sprint 7, y conviene leerlos ya como historia y no como pendientes:

| # | Qué pasó |
|---|---|
| **1** | **Resuelto.** La S7-HU-04 lo recogió y se eligió el camino **a** ampliado: el extra lleva `danado`, `clase_dano`, `tamano_dano` y `nivel_severidad`, y su daño sale en la lista de daños del carro con el nombre del extra (sin punto en el dibujo, porque un extra no tiene zona). En la app es IA-AUTOS#346. Ojo con lo que no es obvio: el servidor exige el nivel **de todo el mundo**, también del conductor, al revés que en el daño de una zona. |
| **3c** | **Resuelto.** Ya no «desaparece»: un daño sin nivel se ve marcado como «Sin calificar» en la ficha y en el dibujo (IA-AUTOS#343), y el inspector del viaje los tiene todos juntos en una lista, el más viejo primero. |
| **5** | **Resuelto.** Los daños anteriores se pintan y se listan al inspeccionar, y también en la ficha (IA-AUTOS#367). La pregunta que dejaba abierta —«¿solo los del viaje o todo el historial?»— quedó respondida: **todo el historial del VIN**, por la ruta `GET /api/vehiculos/{vin}/hallazgos-previos`. |

El **2** quedó respondido a medias: la decisión se tomó —una acomodación por viaje, S7-HU-05— y la app ya dibuja
la disposición real de la niñera y conserva las posiciones, así que no hay que reacomodar. Lo que **no** se
pudo cerrar es la foto única: el servidor sigue contándolas por inspección. Está en
[dudas-del-celular-sprint7.md](dudas-del-celular-sprint7.md), punto 5, con el código y la sub-HU que hace falta.

El **3b** sigue abierto y es el CA-1 de IA-AUTOS#369: además de decidir desde cuándo responde el conductor por
una unidad, hace falta un campo nuevo en la respuesta del viaje. Está en el punto 4 del mismo documento.

Los demás —**3**, **4**, **6**, **7** y **8**— siguen como estaban.

---

## 1. Un extra con daño no se puede clasificar — RESUELTO (S7-HU-04)

> Lo de abajo es el estado del 3-nov-2026 y se conserva como registro. Se resolvió ampliando el extra con
> `danado`, `clase_dano`, `tamano_dano` y `nivel_severidad` (camino **a**), y el daño cuenta en la lista del
> carro. En la app, IA-AUTOS#346.

**Hoy (3-nov).** Al elegir «Barras de techo», «Volco» o «Platón» se pide una observación (opcional) y una foto, y
nada más. El modelo del extra no tiene dónde guardar un daño:

```dart
// mobile/lib/features/inspecciones/domain/extra_local.dart
final String tipo;          // barras_techo, volco, platon, otro
final String? textoOtro;
final String? observacion;  // máximo 500
final List<FotoLocal> fotos;
```

Y el contrato tampoco: `CrearExtraRequest` admite solo `tipo`, `texto` y `observacion`.

**Por qué no se puede resolver con un hallazgo normal.** Un hallazgo exige cuadrante, y los extras no
están en el dibujo:

```csharp
// backend/.../Inspecciones/Validators/ValidadoresInspecciones.cs:132
validador.RuleFor(x => cuadranteNumero(x)).NotNull().WithMessage("La zona indicada no existe");
```

Así que hoy un golpe en el platón se puede fotografiar y describir en texto, pero **no entra en las
estadísticas de daños**: no tiene clase, ni tamaño, ni nivel de severidad, y no cuenta para «no sale» ni
para los indicadores.

**Qué falta decidir.** Un daño sobre un extra, ¿es un hallazgo con todas las de la ley (cuenta para la
tasa de rechazo, puede ser V1+ y frenar la salida) o es otra cosa, informativa?

**Propuesta.** Que el extra pueda llevar un daño opcional con los mismos tres campos del catálogo —
`clase_dano`, `tamano_dano`, `nivel_severidad`— y que el nivel lo ponga quien inspecciona, igual que en
una zona. Dos caminos posibles, y la decisión es del backend:

- **a)** Ampliar `CrearExtraRequest` y `ExtraDto` con esos tres campos. Es lo más simple, pero deja los
  daños de extras fuera de `tblHallazgos` y habría que sumarlos aparte en los reportes.
- **b)** Permitir un hallazgo sin cuadrante que apunte al extra (`extra_id` en vez de `cuadrante_numero`),
  relajando la regla de arriba. Cuesta más, pero el daño queda en el mismo sitio que todos los demás y
  hereda seguimiento, «no sale» e indicadores sin trabajo extra.

Nos inclinamos por **b** si negocio dice que un extra dañado sí cuenta como daño del vehículo.

---

## 2. La acomodación se repite en cada unidad, y deja reacomodar lo ya acomodado

**Hoy.** La segunda inspección del cargue (la acomodación) muestra **todas** las unidades del viaje, cada
una con su selector de lugar, y eso se repite igual al entrar a cada vehículo:

```dart
// mobile/lib/features/inspecciones/presentation/seccion_acomodacion.dart:65
for (final unidad in lista) ...[
  _FilaUnidad(unidad: unidad, posiciones: posiciones, alElegir: ...),
```

Lo que sí funciona: un lugar que ya tiene otra unidad **no se puede robar**, su chip queda deshabilitado
(el servidor lo rechazaría con 409). Lo que no: la unidad que se está inspeccionando no se distingue de
las demás, y una unidad ya ubicada se puede mover desde la captura de cualquier otra.

**La raíz.** La acomodación es un hecho **del viaje** (cómo quedó armada la niñera), pero se captura
dentro de una inspección **por unidad**. Por eso se pregunta tres veces lo mismo.

**Qué falta decidir.** ¿La acomodación se registra una sola vez por viaje, o cada unidad confirma su
propio lugar?

**Propuesta (la que pediste, y es la más barata).** Dejar el esquema de la niñera como está —es la foto
de cómo va quedando el camión, y ahí sí se ven todas— pero mostrar **el selector solo de la unidad que se
está inspeccionando**. Las demás aparecen en el esquema como lugares ocupados, sin poder tocarse. Hace
falta pasarle el VIN de la captura a `SeccionAcomodacion`, que hoy no lo recibe.

Con eso la tercera inspección del cargue ya encuentra 2 de 7 lugares ocupados y solo pregunta por el suyo.

---

## 3. Un informe que quedó en borrador no se puede cerrar ni descartar

**Este es el más serio de la lista**, porque no es un detalle de pantalla: es un estado del que no se sale.

**Hoy.** La app arma el informe entero en el equipo y lo envía como un solo trabajo: crea la inspección,
sube las evidencias y los hallazgos, y la cierra. Si el cierre falla —por ejemplo porque el servidor pide
algo que la app no pidió— la inspección **queda creada en estado `borrador` en el servidor**. Y entonces:

```dart
// mobile/lib/features/vehiculos/presentation/vin_page.dart:740
else if (momento.enBorrador)
  const Insignia('Borrador', tono: TonoInsignia.info)
```

La fila muestra la insignia «Borrador» y **ningún botón**. Ni «Continuar», ni «Cerrar», ni «Descartar».
Tampoco se puede volver a capturar ese momento, porque `sePuedeCapturar` exige que no exista inspección.
Le pasa igual al conductor y al inspector.

Lo vimos dos veces en el mismo día: en la acomodación de Ana y en la salida del transporte.

**La API sí tiene con qué.** `POST /api/inspecciones/{id}/cerrar` existe y es justo el último paso que la
app ya ejecuta. Lo que **no** existe es forma de borrar una inspección: no hay `DELETE /api/inspecciones/{id}`.

**Propuesta.** En la fila del momento en borrador, ofrecer **«Continuar»**: abre la captura contra esa
inspección ya creada, muestra lo que ya tiene y al cerrar llama a `cerrar` en vez de crearla de nuevo.
Si negocio además quiere poder **descartar** un borrador —que es lo natural cuando alguien lo abrió por
error—, el backend tiene que exponer la operación, y hay que definir quién puede: nos parece que solo
quien lo creó, y solo mientras siga en borrador.

**Pregunta aparte, de proceso:** ¿debería existir siquiera un informe en borrador en el servidor? Hoy la
app no los crea a propósito; aparecen solo cuando el cierre falla a mitad de camino.

### 3b. «Reportar novedad» y «Llegué» frente al informe de transporte

Relacionado, y es lo que causó la confusión. Son **dos cosas distintas** que viven en pantallas distintas:

| | Dónde está | Qué hace | A dónde va |
|---|---|---|---|
| «Reportar novedad» / «Llegué» | Mis viajes, en la ficha de cada unidad | El reporte del conductor en ruta: foto, tipo y tamaño; o la confirmación de llegada | `POST /api/viajes/{id}/vehiculos/{vin}/novedades` y `.../llegada`. **No son un informe** |
| Inspección de transporte (salida / llegada) | Pantalla del VIN | Un informe como los demás, solo que el transporte no pide las 7 fotos | `POST /api/inspecciones` y `/cerrar` |

Lo que pasó: la novedad se envió bien por la cola, pero el **informe** de transporte que se había abierto
quedó en borrador, y por el punto 3 no hay cómo cerrarlo.

**Qué falta decidir.** ¿El conductor en ruta necesita además un informe de transporte, o le basta con
«Reportar novedad» y «Llegué»? Si basta, el informe de transporte sobra para él y habría que quitárselo
de la pantalla, que es de donde sale la ambigüedad.

### 3c. La novedad del conductor desaparece al revisarla — RESUELTO (S7-HU-03 y S7-HU-14)

**Hoy (3-nov).** El inspector ve las novedades que reportó el conductor en una lista que pide
`GET /api/hallazgos?...&origen=conductor&revisado=false`. Al revisarla —ponerle zona, clase y nivel— deja
de cumplir el filtro y **desaparece de la lista**. Es el comportamiento buscado para la bandeja de
pendientes, pero deja la sensación de que se perdió. Se arregla solo con el punto 5.

---

## 4. El descargue vuelve a pedir las mismas 7 fotos, dos veces

**Hoy.** La regla del servidor es por etapa, no por momento:

```csharp
// backend/src/IA-AUTOS.Dominio/Catalogos/clsCatalogos.cs:21
public static bool ExigeVistas(string? codigo) => codigo is CARGUE or DESCARGUE;
```

La única excepción es la acomodación (cargue + salida). O sea que hoy, por unidad y por viaje, se piden
las 7 vistas **tres veces**:

| Etapa · momento | ¿Pide las 7? |
|---|---|
| Cargue · llegada | sí |
| Cargue · salida | no — es la acomodación |
| Transporte · salida y llegada | no |
| Descargue · salida | **sí** |
| Descargue · llegada | **sí** |

Son 21 fotos de vista por unidad.

**Qué falta decidir, y es lo primero.** ¿Qué significa cada momento del descargue? En el cargue está
claro: la llegada documenta el estado de la unidad y la salida documenta la acomodación. En el descargue
nadie nos ha dicho qué separa la salida de la llegada, y mientras no se sepa no se puede decir cuál de
las dos sobra.

**Propuesta.** Que el descargue pida las 7 vistas **una sola vez**, en el momento en que se compara
contra lo que venía del cargue —intuimos que es la llegada—, y que el otro momento sea un registro
liviano, como el transporte. Si negocio confirma, el cambio es de una línea en `ExigeVistas` más el
espejo en la app, igual que se hizo con la acomodación.

---

## 5. Los daños no se arrastran: en el descargue el dibujo sale en blanco — RESUELTO (S7-HU-14)

> Se hizo lo que decía la propuesta, y la pregunta del final quedó respondida: se arrastra **todo el historial
> del VIN**, no solo el viaje en curso. En la app, IA-AUTOS#367.

**Hoy (3-nov).** El dibujo solo pinta los daños **de la captura que se está haciendo**:

```dart
// mobile/lib/features/inspecciones/presentation/captura_page.dart:553
danos: {for (final h in _hallazgos) ?h.clave: h.nivel},
```

Los daños de inspecciones anteriores sí se bajan —`_cargarPrevios()` los trae del inventario del VIN—
pero se usan **solo** para una cosa: cuando el inspector marca un daño nuevo en la misma zona y de la
misma clase, le pregunta «¿ya estaba registrado?». No se pintan en el panel ni se listan en ninguna parte.
Y al conductor ni eso: la propuesta de preexistente se le oculta a propósito.

Resultado: el inspector del descargue abre la captura y ve el vehículo limpio. Para saber que la ventana
venía rota tiene que acordarse, o acertar a tocar justo esa zona con justo esa clase.

**Eso rompe la trazabilidad**, que es el punto del sistema: saber si el daño venía del cargue o apareció
en el transporte.

**Propuesta.** Pasarle los hallazgos previos al dibujo y pintarlos con un estilo propio —distinto de los
de esta captura, por ejemplo solo el contorno—, y listarlos debajo con su zona, su clase y su tamaño.
Al tocar una zona que ya traía daño, la app ofrece «ya estaba» o «es nuevo», que es lo que ya sabe hacer.

El dato ya está en el equipo: no hace falta nada del backend. Y el nivel no se filtra al conductor,
porque el inventario ya se lo quita antes de bajarlo.

**Lo que sí hay que decidir:** ¿hasta dónde se arrastra? ¿Solo los del viaje actual, o todo el historial
del VIN? Nos parece que lo del viaje actual, con los anteriores marcados aparte.

---

## 6. La transferencia del descargue la firman las mismas dos personas

**Hoy.** La app ofrece firmar transferencia en **toda** etapa que exija inspección, sin distinguir cuál:

```dart
// mobile/lib/features/vehiculos/domain/inventario.dart:534
if (etapa.requiereInspeccion) {
  if (referencia == null) return const SinInforme();
  return FirmarTransferencia(inspeccionId: referencia.id, ...);
}
```

Y los firmantes salen siempre del viaje:

```csharp
// backend/src/IA-AUTOS.API/Controllers/Viajes/clsViajesController.cs:149
/// Los usuarios del viaje que pueden firmar una transferencia (su conductor y su inspector activos)
```

Por eso en el descargue vuelven a salir el conductor y el inspector del viaje: **no hay nadie que
represente a quien recibe en el destino**.

**Qué falta decidir.** En el descargue, ¿quién recibe la custodia? Si es alguien del concesionario o del
CEDI de destino, hoy no tiene cómo firmar: no es usuario del viaje.

**Observación que puede cambiar la respuesta.** El contrato ya trae otro mecanismo para el destino, que
es **la entrega por punto** (`POST /api/viajes/{id}/entregas/{lugar}/firma`, con `entrega_usuario_id` y
`recibe_usuario_id`, y `/una-firma` con un motivo cuando solo firma uno). Puede ser que la transferencia
por etapa simplemente no aplique en el descargue y que lo correcto sea la entrega por punto — en cuyo
caso el arreglo es quitarla de ahí, no agregar firmantes.

**Propuesta.** Primero que negocio diga cuál de las dos es la buena en el descargue. Si es la
transferencia, el backend tiene que admitir como firmante a quien recibe en el destino (un usuario del
lugar, o nombre y firma a mano como en la constancia de no firma). Si es la entrega por punto, la app
deja de ofrecer «Firmar transferencia» en el descargue.

---

## 7. Qué ve el conductor de los hallazgos

**Hoy, confirmado en el código** (`clsVisibilidadConductor.ParaConductor`). Al conductor se le entrega el
hallazgo con la zona, la clase, el tamaño y la foto, y se le quitan: `nivel_severidad`, `nivel_nombre`,
`nivel_accion`, la nota, el seguimiento, quién revisó y cuándo, y el estado de la evidencia.

Es lo que respondió Pablo: ve el daño, no ve la calificación, porque el nivel lo pone el inspector o el
coordinador.

**La pregunta que queda para mañana:** ¿debería ver el V1 y el V1+?

El argumento a favor: un V1+ es lo que **impide que la unidad salga**. Hoy el conductor puede estar
esperando sin saber por qué, porque el nivel que explica el bloqueo es justo el que no ve. El parámetro
`niveles_criticos` vale `v1_plus,v1`.

El argumento en contra: el nivel es una calificación con consecuencias comerciales, y quien la asigna es
quien inspecciona. Mostrarla invita a discutirla en el patio.

**Una salida intermedia**, por si ayuda: no mostrarle el nivel, pero sí el **efecto** — «esta unidad no
sale» con su motivo. El conductor sabe qué pasa sin ver la calificación. Para eso no hace falta tocar la
redacción del nivel.

---

## 8. Fijar la ubicación de un lugar no es confirmarla

Esto no salió de una prueba fallida sino de una duda al ver la insignia azul, y conviene dejarlo escrito
porque son dos actos distintos y el reparto de roles **es a propósito**.

**Fijar** es aportar un dato: «estoy aquí y mi teléfono dice estas coordenadas». Lo hace quien está en el
lugar. En la app es el botón «Fijar ubicación aquí», en la pantalla de entrega por punto.

**Confirmar** es convertir ese dato en una regla: fijar el centro y, sobre todo, el **radio**.

```csharp
// backend/src/IA-AUTOS.Dominio/Catalogos/clsLugar.cs:134
RadioMetros = null;
EstadoUbicacion = clsEstadoUbicacion.PENDIENTE_CONFIRMAR;
```

**Por qué no lo confirma el mismo que lo fijó.** El radio es la vara con la que después se mide a esa
misma persona: cuando toma una foto en el lugar, el servidor calcula su distancia al centro y, si se pasa,
marca «fuera del rango» y avisa al coordinador de calidad. Si el medido define su propia vara, el control
no vale nada. De ahí el reparto:

| Acto | Quién |
|---|---|
| Fijar | inspector, conductor, coordinador de calidad, admin, super_admin |
| Confirmar | **coordinador de calidad**, admin, super_admin |

Mientras está pendiente **no pasa nada**: no genera avisos de rango para nadie, y nadie puede volver a
fijarla (responde 409).

**Las dos formas de confirmarla**, las dos desde el portal:

1. **Carga manual**, `PATCH /api/catalogos/lugares/{id}`. Como lo que no se envía se conserva, basta
   mandar `radio_metros`: toma las coordenadas que dejó el conductor y el lugar queda confirmado.
2. **Autocalibración.** Un proceso de fondo mira las fotos de Cargue y Descargue selladas con ubicación y
   precisión mejor que `valvula_precision_m`, de lugares activos y no confirmados. Con al menos **3
   viajes distintos** propone centro (la mediana) y radio (el 90 % de las fotos, redondeado a 10 m, entre
   100 y 5000), y avisa a los coordinadores, que confirman o descartan.

### Lo que sí tiene huecos

**a) La app es más estricta que el servidor.** El backend deja fijar al inspector, pero la app solo
muestra el botón al conductor (`entrega_punto_page.dart:111`, `if (esConductor)`). La inspectora está en
el patio igual que el conductor. **Propuesta:** mostrarlo también al inspector, que es lo que el servidor
ya autoriza.

**b) Calidad decide a ciegas.** Esto es lo que ve el portal de un lugar recién fijado:

```json
{"nombre": "Concesionario (por definir)", "latitud": 6.2261455, "longitud": -75.5936986,
 "radio_metros": null, "estado_ubicacion": "pendiente_confirmar",
 "ubicacion_fijada_por": "4df1ce96…", "ubicacion_fijada_at": "2026-11-03T16:37:47Z"}
```

Ve quién y cuándo, pero **no ve con qué precisión de GPS se tomó**. La app la envía (`precision` en el
cuerpo) y el servidor la recibe, pero solo la escribe en `tblAuditLog`: no se guarda en el lugar ni se
publica. Un punto tomado con ±200 m dentro de un galpón se ve idéntico a uno con ±5 m al aire libre.
**Propuesta:** guardar la precisión en el lugar y publicarla junto al estado, para que quien confirma
sepa qué tan bueno es el dato.

**c) Una ubicación pendiente se puede quedar así para siempre.** Fijada la coordenada, el botón
desaparece y nadie más puede tocarla. Si Calidad no la confirma, nada lo recuerda, y la autocalibración
necesita 3 viajes. Un lugar nuevo puede pasar semanas pendiente, sin radio y sin control.
**Propuesta:** que el lugar en `pendiente_confirmar` le aparezca a Calidad como pendiente —un aviso
cuando se fija, no solo cuando hay propuesta de autocalibración.

---

## Lo que hay que preguntarle a negocio, en una lista

1. ~~Un daño sobre un extra, ¿cuenta como daño del vehículo?~~ **Respondida:** sí, con sus tres datos (S7-HU-04).
2. ~~La acomodación, ¿se registra una vez por viaje o cada unidad confirma su lugar?~~ **Respondida:** una vez
   por viaje (S7-HU-05). Falta que el servidor cuente así también las fotos.
3. ¿Debe poder descartarse un informe en borrador? ¿Quién?
4. El conductor en ruta, ¿necesita el informe de transporte, o le bastan «Reportar novedad» y «Llegué»?
5. En el descargue, ¿qué documenta la salida y qué la llegada? ¿Cuál de las dos pide las 7 fotos?
6. ~~Los daños que se arrastran, ¿solo los del viaje actual o todo el historial del VIN?~~ **Respondida:** todo
   el historial del VIN (S7-HU-14).
7. En el descargue, ¿quién recibe la custodia y firma? ¿Es transferencia o es entrega por punto? **Respondida
   en parte:** para el destino es la entrega por punto, y ya admite a quien recibe sin ser usuario de la app
   (S7-HU-02). Sigue sin decidirse si «Firmar transferencia» debe desaparecer del descargue.
8. ¿El conductor debe ver el V1 y el V1+, o solo que la unidad no sale?
9. ¿El inspector también puede fijar la ubicación de un lugar?
10. Desde cuándo responde el conductor por una unidad: ¿cargue cerrado, transferencia firmada, o viaje
    arrancado? (bloquea el CA-1 de IA-AUTOS#369, y arrastra un campo nuevo en el contrato).
11. ¿Cada tamaño de daño pertenece a una clase, o hay tamaños que sirven para varias?
12. Cuando se firma el cambio de responsabilidad de una etapa, ¿quedan congelados todos sus informes o solo el
    firmado? (punto 1 de [dudas-del-celular-sprint7.md](dudas-del-celular-sprint7.md)).
13. ¿La transferencia de una etapa se puede firmar con un momento de esa etapa todavía sin capturar? Hoy sí, y
    así se firma el cargue sin que nadie haya acomodado las unidades en la niñera (punto 2 del mismo documento).
14. En un punto con inspectores asignados y un viaje sin inspector, ¿quién firma la entrega? Hoy no hay salida
    desde el teléfono: con los datos del externo responde 400 y sin ellos también. La IA-AUTOS#401 lo dejó
    fuera de alcance a propósito. Está entero en [duda-inspector-por-punto.md](duda-inspector-por-punto.md).

> **Esta lista es el consolidado.** Las dudas con su código, su archivo y su línea están en los otros tres
> documentos; aquí queda solo la pregunta, para llevarla a negocio sin arrastrar el detalle técnico.
