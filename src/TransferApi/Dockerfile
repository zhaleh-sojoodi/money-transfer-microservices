# Build from the repo root, because this service also needs src/Contracts:
#   docker build -f src/TransferApi/Dockerfile -t transfer-api .
# Migration image (runs once before the service, see the "migrations" stage):
#   docker build -f src/TransferApi/Dockerfile --target migrations -t transfer-api-migrations .

# Stage 1: compile with the full SDK
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Project files first: packages are only restored again when these change
COPY src/Contracts/Contracts.csproj src/Contracts/
COPY src/TransferApi/TransferApi.csproj src/TransferApi/
RUN dotnet restore src/TransferApi/TransferApi.csproj

COPY src/Contracts/ src/Contracts/
COPY src/TransferApi/ src/TransferApi/
RUN dotnet publish src/TransferApi/TransferApi.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

# Migration bundle: one program containing all EF Core migrations. Needs no SDK or source code to run.
FROM build AS migrations-build
COPY dotnet-tools.json .
RUN dotnet tool restore
RUN dotnet ef migrations bundle --project src/TransferApi/TransferApi.csproj --configuration Release --output /app/efbundle

# Migration image. Reads ConnectionStrings__TransferDb, applies missing migrations, then exits.
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS migrations
WORKDIR /app
COPY --from=migrations-build /app/efbundle .
USER $APP_UID
ENTRYPOINT ["./efbundle"]

# Stage 2: only the compiled output on the smaller runtime image.
# The last stage is what a plain "docker build" produces, so it must stay the service.
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# The image's default port for ASP.NET Core
EXPOSE 8080

# Run as a normal user instead of root
USER $APP_UID
ENTRYPOINT ["dotnet", "TransferApi.dll"]
