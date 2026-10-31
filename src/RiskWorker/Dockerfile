# Build from the repo root, because this service also needs src/Contracts:
#   docker build -f src/RiskWorker/Dockerfile -t risk-worker .

# Stage 1: compile with the full SDK
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Project files first: packages are only restored again when these change
COPY src/Contracts/Contracts.csproj src/Contracts/
COPY src/RiskWorker/RiskWorker.csproj src/RiskWorker/
RUN dotnet restore src/RiskWorker/RiskWorker.csproj

COPY src/Contracts/ src/Contracts/
COPY src/RiskWorker/ src/RiskWorker/
RUN dotnet publish src/RiskWorker/RiskWorker.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

# Stage 2: a worker has no web server, so the plain .NET runtime image is enough
FROM mcr.microsoft.com/dotnet/runtime:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# Run as a normal user instead of root
USER $APP_UID
ENTRYPOINT ["dotnet", "RiskWorker.dll"]
