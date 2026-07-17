#!/bin/sh
# check.sh [$FILE] — build (warnings-as-errors + analyzers) the .csproj owning $FILE; non-zero = blocked.
set -eu
REPO_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$REPO_ROOT"

FILE="${1:-}"
START=$(date +%s%N)

if [ -z "$FILE" ]; then
    dotnet build TreasuryServiceOrchestrator.slnx -clp:NoSummary --nologo
    STATUS=$?
else
    if command -v cygpath >/dev/null 2>&1; then
        FILE="$(cygpath -u "$FILE")"
    else
        FILE="$(printf '%s' "$FILE" | tr '\\' '/')"
    fi
    case "$FILE" in
        /*) ABS_FILE="$FILE" ;;
        *) ABS_FILE="$REPO_ROOT/$FILE" ;;
    esac
    DIR="$(dirname "$ABS_FILE")"
    PROJ=""
    while [ "$DIR" != "/" ] && [ "$DIR" != "." ]; do
        CANDIDATE=$(find "$DIR" -maxdepth 1 -name "*.csproj" 2>/dev/null | head -1)
        if [ -n "$CANDIDATE" ]; then
            PROJ="$CANDIDATE"
            break
        fi
        case "$DIR" in
            "$REPO_ROOT") break ;;
        esac
        DIR="$(dirname "$DIR")"
    done

    if [ -z "$PROJ" ]; then
        echo "check.sh: $FILE is not owned by any .csproj, skipping" >&2
        exit 0
    fi

    echo "check.sh: building $PROJ" >&2
    dotnet build "$PROJ" -clp:NoSummary --nologo
    STATUS=$?
fi

END=$(date +%s%N)
MS=$(( (END - START) / 1000000 ))
echo "check.sh: wall-clock ${MS}ms" >&2
exit $STATUS
