#!/bin/sh
# contract.sh — emit OpenAPI doc from the API, regenerate Angular client, diff generated dir; non-empty diff = non-zero.
#
# STUBBED (see README below): Angular client generation/diff is a no-op — no
# Angular project exists in this repo yet. Once one exists at
# clients/angular (or wherever), replace the "REGEN CLIENT" block with the
# real `openapi-generator-cli` / `ng-openapi-gen` invocation and remove this
# notice.
set -eu
REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$REPO_ROOT"

PROJ="src/TreasuryServiceOrchestrator.Api/TreasuryServiceOrchestrator.Api.csproj"
OUT_DIR="docs/openapi"
OUT_FILE="$OUT_DIR/openapi.json"
URL="http://localhost:5068/openapi/v1.json"
PORT_WAIT_SECS=30

mkdir -p "$OUT_DIR"

dotnet build "$PROJ" -clp:NoSummary --nologo >&2

ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS="http://localhost:5068" \
    dotnet run --no-build --project "$PROJ" >/tmp/contract-api.log 2>&1 &
API_PID=$!

cleanup() {
    kill "$API_PID" >/dev/null 2>&1 || true
    wait "$API_PID" 2>/dev/null || true
}
trap cleanup EXIT

i=0
until curl -sf "$URL" -o "/tmp/openapi-new.json" 2>/dev/null; do
    i=$((i + 1))
    if [ "$i" -ge "$PORT_WAIT_SECS" ]; then
        echo "contract.sh: API did not respond at $URL within ${PORT_WAIT_SECS}s" >&2
        cat /tmp/contract-api.log >&2
        exit 1
    fi
    sleep 1
done

cp /tmp/openapi-new.json "$OUT_FILE"

cleanup
trap - EXIT

echo "contract.sh: REGEN CLIENT — stubbed, no Angular client project exists yet" >&2

git diff --exit-code -- "$OUT_DIR"
