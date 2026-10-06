# syntax=docker/dockerfile:1
# Imagen de la API. El contexto de compilacion es la RAIZ del repositorio (no backend/):
#   Dokploy:  Docker File = backend/Dockerfile   Docker Context Path = .
#   Local:    docker build -f backend/Dockerfile .      (docker compose ya lo hace asi)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# 1) Solo los archivos de proyecto: la restauracion de paquetes queda en cache mientras no cambien las dependencias.
#    Se restaura unicamente la API (y sus proyectos referenciados): las pruebas no forman parte de la imagen.
COPY backend/Directory.Build.props backend/Directory.Packages.props ./
COPY backend/src/IA-AUTOS.Dominio/*.csproj src/IA-AUTOS.Dominio/
COPY backend/src/IA-AUTOS.Aplicacion/*.csproj src/IA-AUTOS.Aplicacion/
COPY backend/src/IA-AUTOS.Infraestructura/*.csproj src/IA-AUTOS.Infraestructura/
COPY backend/src/IA-AUTOS.API/*.csproj src/IA-AUTOS.API/
RUN dotnet restore src/IA-AUTOS.API/IA-AUTOS.API.csproj

# 2) Codigo fuente y publicacion.
COPY backend/src src
RUN dotnet publish src/IA-AUTOS.API/IA-AUTOS.API.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_EnableDiagnostics=0
COPY --from=build /app/publish .
# Imagen sin privilegios: usuario no-root predefinido en las imagenes oficiales.
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "IA-AUTOS.API.dll"]
