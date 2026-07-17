#!/bin/sh
# test-fast.sh [$SCOPE] — unit tests only, under 60s.
set -eu
REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$REPO_ROOT"

SCOPE="${1:-}"
PROJ="tests/TreasuryServiceOrchestrator.UnitTests/TreasuryServiceOrchestrator.UnitTests.csproj"

dotnet build "$PROJ" -clp:NoSummary --nologo

if [ -n "$SCOPE" ]; then
    dotnet test "$PROJ" --no-build --filter "$SCOPE"
else
    dotnet test "$PROJ" --no-build
fi
