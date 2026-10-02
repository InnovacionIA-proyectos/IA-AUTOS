#!/bin/sh
# Genera /config.json (configuracion de ejecucion, sin secretos) a partir de variables de entorno.
set -eu

# Escapa comillas y barras invertidas para que el JSON resultante sea siempre valido.
json() { printf '%s' "$1" | sed 's/\\/\\\\/g; s/"/\\"/g'; }

# Solo se aceptan numeros enteros para los minutos de inactividad; otro valor usa 30.
case "${INACTIVIDAD_MINUTOS:-30}" in
  ''|*[!0-9]*) MINUTOS=30 ;;
  *) MINUTOS="${INACTIVIDAD_MINUTOS:-30}" ;;
esac

cat > /usr/share/nginx/html/config.json <<EOF
{
  "apiUrl": "$(json "${API_URL%/}")",
  "apkUrl": "$(json "${APK_URL:-}")",
  "inactividadMinutos": ${MINUTOS},
  "proveedorLogin": "$(json "${PROVEEDOR_LOGIN:-propio}")"
}
EOF
