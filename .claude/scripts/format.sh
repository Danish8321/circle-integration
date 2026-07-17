#!/bin/sh
# format.sh $FILE — dotnet format in place, always exit 0.
set -u
cd "$(dirname "$0")/../.." || exit 0

FILE="${1:-}"

if [ -z "$FILE" ]; then
    dotnet format TreasuryServiceOrchestrator.slnx >/dev/null 2>&1
    exit 0
fi

dotnet format TreasuryServiceOrchestrator.slnx --include "$FILE" >/dev/null 2>&1
exit 0
