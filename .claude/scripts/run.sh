#!/bin/sh
# run.sh — start the full stack locally (API + dependencies).
set -eu
REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$REPO_ROOT"

PROJ="src/TreasuryServiceOrchestrator.Api/TreasuryServiceOrchestrator.Api.csproj"

# No external dependencies (SQL Server, etc.) wired via docker-compose yet;
# this only starts the API. Extend here once Infrastructure needs a real DB.
ASPNETCORE_ENVIRONMENT=Development exec dotnet run --project "$PROJ"
