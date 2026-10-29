# Piloto y corte del APK actual

Para S6-HU-07.2: instalar el APK firmado en el grupo piloto y retirar el APK actual (Expo). No es código: es un
procedimiento que se sigue **celular por celular** y deja un registro.

## Por qué hay corte

La API ya no acepta lo que envía el APK actual: responde `400` a un informe con momento `informe`
(`ValidadoresInspecciones`), y a los campos de conductor, placa y tráiler que antes se tecleaban. Se comprobó contra
el backend local con un `POST /api/inspecciones` de momento `informe`: `400 Datos inválidos`. Por eso las dos apps no
conviven: lo que quede pendiente en la actual **no se puede enviar solo** y hay que pasarlo a mano.

Los dos APK pueden quedar instalados a la vez (`applicationId` distintos), así que desinstalar el actual es un paso
explícito del procedimiento, no algo que ocurra solo.

## Antes de empezar (una sola vez)

- [ ] El APK de producción está firmado con la llave propia de la empresa (`android/key.properties`), no con la de
      depuración. `flutter build apk --release --flavor prod` se niega a compilar sin ella (ver `release.md`).
- [ ] La llave y sus contraseñas están en el gestor de secretos. **Pendiente de confirmar.**
- [ ] El APK está alojado en el enlace de distribución y se probó la descarga desde un celular del grupo.
- [ ] El `versionCode` es mayor que 11, que es donde terminó la app actual.
- [ ] El líder y mobile fijaron la **fecha de corte** (decisión PAB-063): `____ / ____ / ______`.
- [ ] El backend con el contrato nuevo ya está desplegado en el entorno del piloto.

## Checklist de instalación por celular

Un celular por hoja. Marcar cada paso al hacerlo.

**Celular:** ____________  **Persona:** ____________  **Fecha:** ____________  **Hizo:** ____________

1. [ ] Conectar a una red con internet estable y con el celular cargado.
2. [ ] Abrir la app **actual** y revisar «Envíos pendientes». Anotar cuántos hay: ______
3. [ ] Si hay pendientes: seguir «Pasar lo pendiente» (abajo) **antes** de instalar la nueva.
4. [ ] Descargar el APK firmado desde el enlace del piloto e instalarlo (aceptar «instalar apps desconocidas» solo
      para el navegador o gestor de archivos que se usó, y retirarlo después).
5. [ ] Abrir la app nueva. Iniciar sesión con **cédula y PIN** (los usuarios deben ingresar de nuevo).
6. [ ] Si pide cambiar el PIN (PIN asignado por un administrador), cambiarlo.
7. [ ] Aceptar los permisos de **cámara** y **ubicación**. Con el GPS encendido, esperar «Ubicación» en una captura.
8. [ ] Abrir «Mis viajes»: deben aparecer los viajes de la persona. Si no aparece ninguno, avisar a quien los asigna.
9. [ ] Prueba corta: abrir una unidad, tomar una foto y comprobar que queda sellada con fecha y ubicación.
10. [ ] Prueba de red: activar modo avión, guardar un informe, desactivarlo y comprobar que sale solo
      («Todo sincronizado»).
11. [ ] Desinstalar la app **actual** (Expo). Anotar que quedó desinstalada: [ ]
12. [ ] Registrar el celular en la tabla del final.

## Pasar lo pendiente de la app actual

La app nueva **no importa** la cola de la actual (la fuente no lo pide), así que lo pendiente se registra de nuevo:

1. Con señal, abrir «Envíos pendientes» de la app actual y esperar a que envíe lo que la API todavía acepte.
2. De lo que quede: anotar VIN, etapa, momento y hora, y guardar sus fotos (galería o carpeta de la app) como
   **paquete de evidencia** en la carpeta del piloto, con el nombre `celular-VIN-etapa`.
3. Con la app nueva, abrir la unidad en «Mis viajes» y capturar de nuevo lo que falte, tomando las fotos otra vez. Si
   la foto original es la única prueba de un daño que ya no se puede repetir, adjuntarla al paquete y avisar al
   coordinador de calidad para que lo deje como nota aclaratoria del informe.
4. Solo cuando todo lo pendiente esté registrado en la nueva, desinstalar la actual (paso 11).

> **A confirmar con mobile:** dónde guarda la app Expo las fotos pendientes y cómo se exportan. El código de la app
> actual no está en este repositorio, así que ese paso no se pudo comprobar aquí.

## Cómo se comprueba que el corte quedó bien

- En un celular que tenía envíos pendientes: después del corte, esos envíos aparecen registrados con la app nueva y la
  actual ya no está instalada.
- Un envío de la app actual con momento `informe` recibe `400` de la API (comprobado contra el backend local).
- Solo opera la app nueva: la API ya no acepta lo que envía la actual.

## Registro del piloto

| Celular | Persona | Pendientes en la actual | Pasados a la nueva | App actual desinstalada | Fecha | Observaciones |
|---|---|---|---|---|---|---|
| | | | | | | |

## Qué no se verificó

- La firma con la llave de la empresa y el enlace de distribución real: dependen de quien custodia la llave.
- La instalación en cada celular del grupo (CA-1 y CA-3): es trabajo de campo y se marca en la hoja de cada uno.
- El paso de exportar la cola de la app Expo (ver la nota de arriba).
