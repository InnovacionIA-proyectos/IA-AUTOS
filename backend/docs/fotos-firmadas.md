# Fotos con URL firmada

Una foto **no es pública**: solo se ve con una URL **firmada por el sistema y vigente**. La forma de la URL, las respuestas de `GET /api/uploads/{archivo}` y las reglas de vigencia están en [CONTRATO.md](../CONTRATO.md) («URL firmada de una foto»); aquí, cómo funciona y cómo se despliega.

## Cómo funciona

1. Toda foto que devuelve la API (`EvidenciaDto.url`, `ArchivoSubidoDto.url`, el sello de quien valida, la constancia, las fotos de los hallazgos previos…) sale como `/api/uploads/{archivo}?exp=<epoch>&firma=<hmac>`, con su vencimiento en `url_expira_at` (ISO 8601).
2. La firma es un HMAC-SHA256 del nombre del archivo y de `exp` con el secreto del servidor (`Storage__FirmaSecreto`): una firma solo sirve para ese archivo y ese vencimiento.
3. `GET /api/uploads/{archivo}` **no exige `Authorization`** (sirve en un `<img src>`); sin firma, con una firma que no es la del archivo o vencida responde `403 URL_FIRMADA_INVALIDA` (también con un token válido), y `404` si la firma es válida pero el archivo no existe.
4. Cuando la URL vence se vuelve a pedir el recurso (informe, inventario…) para recibir una nueva: no se guarda la URL sino el recurso.

## Configuración

| Dónde | Qué |
|---|---|
| `Storage__FirmaSecreto` | secreto de la firma (mínimo 32 caracteres, solo en la variable de entorno). Si falta, se deriva del secreto de los tokens y se avisa en el registro, pero conviene definirlo |
| Parámetro `url_firmada_minutos` | vigencia de la URL, 15 minutos por defecto; se cambia sin desplegar (ver [parametros.md](parametros.md)) |
| `Storage__LecturaPublica` | **retirada**: ya no hay lectura pública de imágenes (ver [migracion-v1-v2.md](migracion-v1-v2.md)) |

## Qué no cambia

Los PDF (inventario, informe y constancia) incrustan las imágenes leyéndolas del almacén, no por URL. El caché de una foto es privado y dura hasta que vence la URL (máximo una hora).

## Código y pruebas

`clsFirmadorUrls` (firma y validación), `clsUrlsArchivos` (arma las URL de los DTO) y `clsArchivosController`. Pruebas: `UrlsFirmadasApiTests` y `ArchivosApiTests`. Ver también [seguridad.md](seguridad.md) y [evidencia-inmutable.md](evidencia-inmutable.md).
