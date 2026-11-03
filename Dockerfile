
# MILLY K-POP GO - .NET 10
# Compilar desde la raiz del repositorio

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

# Copiar el proyecto completo
COPY . .

# Restaurar dependencias
RUN dotnet restore "MillyKpopGo.csproj"

# Publicar la aplicacion
RUN dotnet publish "MillyKpopGo.csproj" \
    -c Release \
    -o /out \
    --no-restore \
    /p:UseAppHost=false

# Imagen de ejecucion
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

WORKDIR /app

COPY --from=build /out/ ./

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_FORWARDEDHEADERS_ENABLED=true \
    DOTNET_RUNNING_IN_CONTAINER=true \
    PORT=8080

EXPOSE 8080

ENTRYPOINT ["sh", "-c", "exec dotnet MillyKpopGo.dll --urls http://0.0.0.0:${PORT:-8080}"]
