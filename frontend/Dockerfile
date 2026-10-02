# Frontend Angular: compilacion con Node y servicio con nginx sin privilegios (puerto 8080).
# La configuracion de ejecucion (URL de la API, enlace del APK...) se genera al arrancar desde variables de entorno,
# de modo que la misma imagen sirve para QA y produccion.

FROM node:24-alpine AS compilacion
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY . .
RUN npx ng build --configuration production

FROM nginxinc/nginx-unprivileged:1.29-alpine AS runtime
USER root
COPY --from=compilacion /app/dist/traza-autos-web/browser /usr/share/nginx/html
COPY nginx/default.conf.template /etc/nginx/templates/default.conf.template
COPY nginx/05-normalizar-env.envsh /docker-entrypoint.d/05-normalizar-env.envsh
COPY nginx/40-config-json.sh /docker-entrypoint.d/40-config-json.sh
RUN sed -i 's/\r$//' /docker-entrypoint.d/05-normalizar-env.envsh /docker-entrypoint.d/40-config-json.sh \
    && chmod +x /docker-entrypoint.d/05-normalizar-env.envsh /docker-entrypoint.d/40-config-json.sh \
    && chown -R nginx:nginx /usr/share/nginx/html
USER nginx

# Valores por defecto (sobrescribir al ejecutar). API_URL no lleva barra final.
ENV API_URL=http://localhost:8080 \
    APK_URL= \
    INACTIVIDAD_MINUTOS=30 \
    PROVEEDOR_LOGIN=propio

EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=3s --retries=3 CMD wget -q -O /dev/null http://127.0.0.1:8080/healthz || exit 1
