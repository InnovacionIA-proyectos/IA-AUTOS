# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props IA-AUTOS.slnx ./
COPY src/IA-AUTOS.Dominio/*.csproj src/IA-AUTOS.Dominio/
COPY src/IA-AUTOS.Aplicacion/*.csproj src/IA-AUTOS.Aplicacion/
COPY src/IA-AUTOS.Infraestructura/*.csproj src/IA-AUTOS.Infraestructura/
COPY src/IA-AUTOS.API/*.csproj src/IA-AUTOS.API/
COPY tests/IA-AUTOS.Tests.Unitarios/*.csproj tests/IA-AUTOS.Tests.Unitarios/
COPY tests/IA-AUTOS.Tests.Integracion/*.csproj tests/IA-AUTOS.Tests.Integracion/
RUN dotnet restore IA-AUTOS.slnx
COPY . .
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
