# Build from repository root: docker build -t milly-kpop-go .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/MillyKpopGo/MillyKpopGo.csproj src/MillyKpopGo/
RUN dotnet restore src/MillyKpopGo/MillyKpopGo.csproj
COPY src/MillyKpopGo/ src/MillyKpopGo/
RUN dotnet publish src/MillyKpopGo/MillyKpopGo.csproj -c Release -o /out --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /out/ ./
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_FORWARDEDHEADERS_ENABLED=true \
    DOTNET_RUNNING_IN_CONTAINER=true \
    PORT=8080
EXPOSE 8080
# Railway defines PORT; local Docker uses the default 8080.
ENTRYPOINT ["sh", "-c", "exec dotnet MillyKpopGo.dll --urls http://0.0.0.0:${PORT:-8080}"]
