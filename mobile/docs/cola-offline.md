# Cola de sincronización offline

Es la pieza más crítica de la app. Reproduce la semántica de la app Expo original (que se caracterizó antes de portarla,
ver `test/sync/procesador_cola_test.dart`) y añade dos mejoras.

## Qué se encola

Al cerrar un informe o registrar una firma NO se llama a la API directamente: se guarda un *job* en SQLite (Drift,
tabla `jobs`) y, si hay señal estable, se sincroniza de inmediato. Las fotos se guardan como archivos en el almacenamiento
privado de la app (no en base64 dentro de la base de datos).

| Job | Pasos al enviarlo |
|---|---|
| Informe | crear inspección, subir cada foto y crear su evidencia, crear cada hallazgo, cerrar |
| Firma | firmar la transferencia (entrega y/o recibe) |

## Reglas de procesamiento

- **FIFO, un job a la vez**: se respeta el orden de las etapas.
- **Señal estable**: wifi, datos o ethernet **y** `GET /health` responde en menos de 5 s. Se valida antes de empezar y antes
  de cada job; si la señal se cae a mitad, la pasada se detiene y se reanuda luego.
- **Reanudable**: cada paso completado se guarda (inspección creada, foto subida, evidencia creada, hallazgos enviados). Un
  corte a mitad no duplica nada al reintentar.
- **Errores transitorios** (sin red, 401, 408, 429 y 5xx): el job se conserva con un intento más y la pasada se detiene
  para no romper el orden.
- **Errores no recuperables** (400, 403, 404, 409 en pasos distintos del cierre, 422...): el job pasa a *fallido* y se
  continúa con el siguiente.
- **403 `PIN_CHANGE_REQUIRED` es la excepción**: es el único 403 transitorio. El envío es correcto y vuelve a servir en
  cuanto la persona cambia su PIN, así que se reanuda en vez de morir.
- **409 al cerrar** significa "ya estaba cerrada": cuenta como éxito.
- **Máximo 8 intentos**; al octavo el job pasa a *fallido*.
- **Cuándo se dispara**: al recuperar una señal estable, al iniciar sesión, al abrir la app y a demanda ("Sincronizar").
  Sin sesión iniciada no se procesa (todo fallaría con 401 y gastaría intentos), y **con un PIN temporal tampoco**
  (`EstadoAuth.puedeOperar`): el servidor responde 403 a todo salvo cambiarlo, y como ese 403 no se arregla
  reintentando, una pasada a destiempo marcaría como fallido lo que solo llegó temprano.

## Mejoras sobre la app original

1. **Sello por foto**: cada foto lleva la fecha y la ubicación del momento en que se tomó. Antes todas llevaban las del
   cierre del informe.
2. **Los fallos no se pierden en silencio**: un job *fallido* se conserva y se muestra en "Envíos pendientes" con su
   motivo; el usuario puede **reintentar** (vuelve a la cola con los intentos en cero) o **descartar** (con confirmación;
   borra también sus fotos locales). La barra de sincronización avisa cuántos hay con error.

## Lo que se le dice a quien cierra un informe

Al cerrar, la app encola, intenta enviar y **mira ese job** para anunciar uno de tres desenlaces. Es la única señal que
tiene en el patio de si su trabajo quedó hecho, así que tiene que ser verdad:

| Estado del job | Mensaje |
|---|---|
| Ya no está en la cola | «Informe enviado» |
| Está y quedó *fallido* | «No se pudo enviar», con el motivo que dio el servidor y dónde recuperarlo |
| Está y sigue vivo | «Informe guardado»; se enviará solo cuando haya señal |

Se mira ese job y no el contador de pendientes de la cola: ese contador excluye los fallidos, así que un informe
recién rechazado por el servidor se anunciaba como enviado y cerrado. Lo destapó una prueba en dispositivo con el
400 de `momento` de la S5-HU-24.

## Datos y límites conocidos

- La cola vive en el dispositivo hasta que se envía; si se desinstala la app o se borran sus datos, lo pendiente se pierde.
  Por eso la barra de sincronización siempre muestra cuánto falta enviar.
- Cerrar sesión **conserva** la cola. Los trabajos se envían con la identidad de quien tenga la sesión iniciada cuando haya
  señal (el servidor toma el usuario del token). En un dispositivo compartido conviene dejar la cola vacía antes de
  cambiar de usuario; la app avisa al cerrar sesión si quedan elementos sin enviar. Etiquetar cada job con su usuario y
  procesar solo los de la sesión actual queda como mejora pendiente.
- Si el token vence (401 en una petición autenticada) se cierra la sesión con un aviso y la cola se conserva; se retoma al
  volver a entrar.
- El servidor valida que quien firma sea el usuario autenticado; la app no lo impone en pantalla.
