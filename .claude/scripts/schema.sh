#!/bin/sh
# schema.sh new|apply|verify — dotnet ef migrations add / database update / migrations script --idempotent.
#
# STUBBED: wired to EF Core (dotnet-ef via .config/dotnet-tools.json) even
# though no DbContext/entities exist yet. `new` and `apply` will fail with
# "no DbContext was found" until a DbContext is added to Infrastructure —
# that failure is expected, not a bug in this script.
set -eu
REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$REPO_ROOT"

PROJ="src/TreasuryServiceOrchestrator.Infrastructure/TreasuryServiceOrchestrator.Infrastructure.csproj"
STARTUP="src/TreasuryServiceOrchestrator.Api/TreasuryServiceOrchestrator.Api.csproj"

CMD="${1:-}"
shift 2>/dev/null || true

case "$CMD" in
    new)
        NAME="${1:-}"
        if [ -z "$NAME" ]; then
            echo "usage: schema.sh new <MigrationName>" >&2
            exit 1
        fi
        dotnet tool run dotnet-ef migrations add "$NAME" \
            --project "$PROJ" --startup-project "$STARTUP" --output-dir Migrations
        echo "schema.sh: migration generated — READ IT before applying (schema-change skill)" >&2
        ;;
    apply)
        dotnet tool run dotnet-ef database update \
            --project "$PROJ" --startup-project "$STARTUP"
        ;;
    verify)
        dotnet tool run dotnet-ef migrations script --idempotent \
            --project "$PROJ" --startup-project "$STARTUP"
        ;;
    *)
        echo "usage: schema.sh new|apply|verify" >&2
        exit 1
        ;;
esac
