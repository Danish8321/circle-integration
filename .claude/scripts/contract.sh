#!/bin/sh
# contract.sh — emit OpenAPI doc from the API, regenerate Angular client, diff generated dir; non-empty diff = non-zero.
#
# Emit is build-time and in-process: the Microsoft.Extensions.ApiDescription.Server
# tool (GetDocument.Insider) loads the host and writes the document during
# `dotnet build` — no Kestrel, no bound socket, no loopback. Works identically in
# CI and in sandboxes that block client->Kestrel loopback.
#
# STUBBED (see below): Angular client generation/diff is a no-op — no Angular
# project exists in this repo yet. Once one exists at clients/angular (or
# wherever), replace the "REGEN CLIENT" block with the real
# `openapi-generator-cli` / `ng-openapi-gen` invocation and remove this notice.
set -eu
REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$REPO_ROOT"

PROJ="src/TreasuryServiceOrchestrator.Api/TreasuryServiceOrchestrator.Api.csproj"
EMITTED="src/TreasuryServiceOrchestrator.Api/obj/openapi/TreasuryServiceOrchestrator.Api.json"
OUT_DIR="docs/openapi"
OUT_FILE="$OUT_DIR/openapi.json"

mkdir -p "$OUT_DIR"

# ASPNETCORE_ENVIRONMENT=Development so the host builds the dev DI graph (fake
# gateway) — the document provider needs a resolvable host, not a live provider.
ASPNETCORE_ENVIRONMENT=Development \
    dotnet build "$PROJ" -clp:NoSummary --nologo >&2

if [ ! -f "$EMITTED" ]; then
    echo "contract.sh: expected emitted OpenAPI doc at $EMITTED — build did not produce it" >&2
    exit 1
fi

cp "$EMITTED" "$OUT_FILE"

echo "contract.sh: REGEN CLIENT — stubbed, no Angular client project exists yet" >&2

git diff --exit-code -- "$OUT_DIR"
