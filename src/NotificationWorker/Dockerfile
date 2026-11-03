# Build from the repo root, because this service also needs src/Contracts:
#   docker build -f src/NotificationWorker/Dockerfile -t notification-worker .
# Migration image (runs once before the service, see the "migrations" stage):
#   docker build -f src/NotificationWorker/Dockerfile --target migrations -t notification-worker-migrations .

# Stage 1: compile with the full SDK
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Project files first: packages are only restored again when these change
COPY src/Contracts/Contracts.csproj src/Contracts/
COPY src/NotificationWorker/NotificationWorker.csproj src/NotificationWorker/
RUN dotnet restore src/NotificationWorker/NotificationWorker.csproj

COPY src/Contracts/ src/Contracts/
COPY src/NotificationWorker/ src/NotificationWorker/
RUN dotnet publish src/NotificationWorker/NotificationWorker.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

# Migration bundle: one program containing all EF Core migrations. Needs no SDK or source code to run.
FROM build AS migrations-build
COPY dotnet-tools.json .
RUN dotnet tool restore
RUN dotnet ef migrations bundle --project src/NotificationWorker/NotificationWorker.csproj --configuration Release --output /app/efbundle

# Migration image. Reads ConnectionStrings__NotificationDb, applies missing migrations, then exits.
FROM mcr.microsoft.com/dotnet/runtime:8.0 AS migrations
WORKDIR /app
COPY --from=migrations-build /app/efbundle .
USER $APP_UID
ENTRYPOINT ["./efbundle"]

# Stage 2: a worker has no web server, so the plain .NET runtime image is enough.
# The last stage is what a plain "docker build" produces, so it must stay the service.
FROM mcr.microsoft.com/dotnet/runtime:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# Run as a normal user instead of root
USER $APP_UID
ENTRYPOINT ["dotnet", "NotificationWorker.dll"]
