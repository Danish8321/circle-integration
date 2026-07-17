#!/bin/sh
# test-full.sh — integration + e2e (Testcontainers SQL Server), may be slow.
set -eu
REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$REPO_ROOT"

PROJ="tests/TreasuryServiceOrchestrator.IntegrationTests/TreasuryServiceOrchestrator.IntegrationTests.csproj"

dotnet build "$PROJ" -clp:NoSummary --nologo
dotnet test "$PROJ" --no-build
