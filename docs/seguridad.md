# Seguridad

## Cambios respecto al sistema anterior

Cambios intencionales que corrigen riesgos detectados en el diagnostico. Ninguno altera rutas, metodos ni la forma de las respuestas.

| Tema | Antes | Ahora |
|---|---|---|
| Ruta `/api/dev/token` | emitia un JWT con cualquier rol si `NODE_ENV` no era `production` | eliminada; no hay autenticacion de desarrollo |
| Rol y estado del usuario | tomados del token; un usuario retirado conservaba el acceso hasta 30 dias | en cada solicitud se consulta la base: un usuario retirado o eliminado pierde el acceso de inmediato y el rol efectivo es el vigente |
| Intentos de login | sin limite | limite por IP (`Seguridad__LoginMaxIntentosPorMinuto`) con respuesta 429; ademas el tiempo de respuesta no revela si el correo existe |
| Token del ETL | comparacion con `!==` | comparacion en tiempo constante; sin token configurado se rechaza todo |
| Contrasenas nuevas | scrypt, minimo 6 caracteres | PBKDF2-SHA256 con 600000 iteraciones, minimo 8 caracteres; los hashes scrypt se aceptan y se regeneran al primer ingreso |
| Acceso a informes ajenos | cualquier usuario autenticado escribia en cualquier inspeccion | solo el creador o un administrador agrega hallazgos y evidencias o cierra (configurable) |
| Firmas | una transferencia firmada podia sobrescribirse | inmutable cuando ambas partes firmaron (repetir los mismos datos es idempotente) |
| Informe nuevo | admitia `estado: cerrada` al crear, saltando las reglas de cierre | siempre nace en borrador; se cierra con `/cerrar` |
| Rol `super_admin` | no podia usar carga de VIN, asignaciones ni etapas | incluido en esas rutas |
| Lista de usuarios | todos los autenticados veian correos y estados | los no administradores reciben solo `id`, `nombre` y `rol` de usuarios activos |
| Subida de fotos | sin limite ni validacion de contenido | tamano maximo, tipo verificado por la firma binaria (JPEG o PNG) y extension decidida por el contenido |
| `storage_path` de evidencias | cualquier texto | solo nombres planos de archivo (sin carpetas ni `..`) |
| CORS | `*` por defecto | solo los origenes configurados; sin configuracion no se habilita |
| Cabeceras | sin cabeceras de seguridad | `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`, CSP restrictiva y `Cache-Control: no-store` |
| Errores | mensajes crudos de la base en lotes | mensajes seguros; el detalle solo en el log del servidor |
| `/health` | devolvia el texto del error de la base | solo conectado o no, sin detalle |
| Inmutabilidad | solo en codigo | tambien en la base de datos (triggers) |
| Auditoria | tabla sin escribir | toda accion relevante se registra |
| Imagen Docker | root, con dependencias de desarrollo | usuario sin privilegios, solo el runtime |

## Controles vigentes

- Todo controlador exige autenticacion salvo `[AllowAnonymous]` (login, salud, ingesta del ETL con su token y lectura de imagenes).
- Todo el acceso a datos es parametrizado (EF Core).
- La API se conecta con un usuario de base de datos de minimo privilegio (`database/01-crear-usuario-app.sql`), no con `sa`.
- El secreto JWT se valida al arrancar (minimo 32 caracteres).
- Los secretos solo llegan por variables de entorno; `.env` esta en `.gitignore` y fuera de la imagen (`.dockerignore`).

## Riesgos aceptados y recomendaciones

| Riesgo | Estado | Recomendacion |
|---|---|---|
| Las imagenes se leen sin token (decision del sistema anterior: las etiquetas de imagen no envian cabeceras; el nombre es un UUID no adivinable) | se mantiene por compatibilidad | cuando web y app puedan enviar el token, active `Storage__LecturaPublica=false` |
| El token dura 30 dias y no hay refresh | se mantiene por compatibilidad con la app instalada | la revocacion es inmediata por baja del usuario; evaluar tokens cortos con refresh al migrar la app a Flutter |
| Una consulta a la base por solicitud para validar al usuario | costo bajo (clave primaria) | si el volumen lo exige, agregar un cache de pocos segundos |
| El limite de intentos es por IP y en memoria de la instancia | basico | con varias instancias, mover el limite a un almacen compartido o al proxy |
| Todo usuario autenticado puede leer inspecciones, inventarios y reportes | igual que antes | si el negocio lo requiere, restringir la lectura por asignacion (la tabla de asignaciones ya existe) |
| Cola offline de la app: un 4xx distinto de 409 la marca como fallida y puede perder evidencia | del cliente, no del backend | corregirlo en la migracion de la app a Flutter |
| El sello de fecha y ubicacion de las fotos lo envia el cliente y el servidor no puede verificarlo | igual que antes | para valor legal, considerar sellos del lado del servidor |
| La validacion de propiedad puede rechazar jobs de la app si se cambia de usuario en el mismo dispositivo antes de sincronizar | nueva | si ocurre en campo, desactivar `Seguridad__ValidarPropiedadInspeccion` temporalmente |
