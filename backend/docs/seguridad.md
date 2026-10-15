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
| Rol `super_admin` | no podia usar carga de VIN, asignaciones ni etapas | incluido en la carga de VIN y en la gestion de viajes |
| Lista de usuarios | todos los autenticados veian correos y estados | los que no gestionan usuarios reciben solo `id`, `nombre` y `rol` de los usuarios activos de su cliente |
| Gestion de usuarios | solo `super_admin` | tambien `admin` y `coordinador_calidad`, cada uno solo sobre los usuarios de su cliente; el nivel `super_admin` lo asigna y modifica unicamente un `super_admin` |
| PIN del personal de campo | no existia | cedula y PIN numerico (4 a 8 digitos, no trivial) con el mismo servicio de hash de las contrasenas; 5 PIN errados seguidos bloquean la cuenta 15 minutos (`423`), cada intento fallido queda en la auditoria y el mensaje es el mismo exista o no la cedula; el PIN asignado o reiniciado es temporal y la API entera responde `403 PIN_CHANGE_REQUIRED` hasta que la persona lo cambie (la marca se lee de la base en cada solicitud) |
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

- Todo controlador exige autenticacion salvo `[AllowAnonymous]` (login, login-conductor, salud, ingesta del ETL con su token y lectura de imagenes).
- Un filtro global rechaza con `403 PIN_CHANGE_REQUIRED` toda accion mientras el PIN sea temporal, salvo `cambiar-pin`, `me` y `logout`.
- El PIN y la contrasena nunca se guardan ni se devuelven en claro: solo el hash (`PinHash`, `PasswordHash`). El PIN generado por la API viaja una unica vez en la respuesta que lo crea.
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
| Inspectores y conductores leen solo lo de sus viajes (VIN, viajes, informes, ordenes Sofasa); el resto de roles lee todo | la visibilidad sale del conductor o inspector del viaje | la visibilidad por cliente (unidades de un cliente y ningun otro) solo esta aplicada a la gestion de usuarios: usuarios y vehiculos ya llevan su cliente (`ClienteId`), pero los listados de vehiculos, viajes, informes y reportes todavia no filtran por el; los reportes son agregados |
| Los intentos de PIN se cuentan en la fila del usuario sin bloqueo de concurrencia | dos intentos simultaneos pueden contar uno solo | el limite por IP de `login-conductor` y el bloqueo por cuenta bastan para el uso previsto; si hiciera falta, pasar el contador a una actualizacion atomica |
| Cola offline de la app: un 4xx distinto de 409 la marca como fallida y puede perder evidencia | del cliente, no del backend | corregirlo en la migracion de la app a Flutter |
| El sello de fecha y ubicacion de las fotos lo envia el cliente y el servidor no puede verificarlo | igual que antes | para valor legal, considerar sellos del lado del servidor |
| La validacion de propiedad puede rechazar jobs de la app si se cambia de usuario en el mismo dispositivo antes de sincronizar | nueva | si ocurre en campo, desactivar `Seguridad__ValidarPropiedadInspeccion` temporalmente |

## Sesion del portal (login-portal, refresh y logout)

- **Verificacion contra TI.** `POST /api/auth/login-portal` recibe el arreglo del usuario del portal pero solo usa el `id` del primer elemento; el nombre y los permisos que envia el navegador se ignoran. El servidor consulta la base de usuarios de TI (solo lectura, `PortalTi__ConnectionString`), nunca lee la contrasena del portal y, si TI no responde, no concede acceso (503). Sin base de TI configurada solo entran los ids ya vinculados a un usuario. Quien no tiene acceso recibe 403 `PORTAL_USER_NOT_LINKED`.
- **Lectura estricta del cuerpo.** Los tipos del portal se verifican (id entero, `permisos` arreglo, ids de permiso e `idOcion` como texto); un tipo distinto responde 400.
- **Refresh token.** Valor aleatorio de 256 bits que viaja en el cuerpo (mismo esquema que EmbarQA). En la base solo se guarda su SHA-256 (`tblRefreshTokens`) junto con familia, vencimiento, revocacion, IP y dispositivo. Cada uso lo rota; un refresh ya rotado que se presenta otra vez (pasada la gracia) revoca toda la familia. Como no va en una cookie HttpOnly, el cliente es responsable de guardarlo de forma segura: un XSS en el modulo podria leerlo.
- **Logout real.** El access token lleva `sid` (la familia): al cerrar sesion o revocarla por reuso, la API lo rechaza en la siguiente solicitud aunque no haya vencido.
- **CORS** con origenes exactos y `Content-Disposition` expuesta; el comodin `*` se ignora y no se piden credenciales (no hay cookies).
- **Limite de intentos** por IP en `login-portal` y `refresh`. Detras de un proxy, active `Proxy__ConfiarEnEncabezadosReenviados` para que el limite use la IP real.
- **JWT con emisor y destinatario** (`Jwt__Issuer`, `Jwt__Audience`) cuando se configuran.

### Limitacion a tener presente

`login-portal` no recibe ninguna credencial: confia en que quien lo llama es el portal. Un id del portal es un numero consecutivo y
no es secreto, asi que **quien pueda alcanzar el endpoint y conozca (o adivine) el id de una persona con rol de IA Autos puede
iniciar sesion como ella**. Las mitigaciones actuales (limite de intentos, CORS exacto, revalidacion contra TI) reducen el abuso desde
navegadores y el sondeo, pero no prueban la identidad. Lo robusto es que el portal entregue una prueba firmada (por ejemplo un token
corto firmado por el portal) que esta API verifique, o que el endpoint solo sea alcanzable desde la red del portal.
