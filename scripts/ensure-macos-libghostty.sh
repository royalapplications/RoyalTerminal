#!/usr/bin/env bash
# ensure-macos-libghostty.sh — Materialize libghostty.dylib from Ghostty build cache.
#
# Upstream Ghostty currently builds the macOS embed dylib for app-runtime=none
# but does not install it into zig-out/lib. This helper copies the newest
# built dylib from .zig-cache into zig-out/lib so downstream packaging can
# consume a stable path. Keep this as a small, reversible downstream patch.

set -euo pipefail

if [ $# -ne 1 ]; then
    echo "Usage: $0 <ghostty-dir>" >&2
    exit 1
fi

GHOSTTY_DIR="$1"
OUT_DIR="$GHOSTTY_DIR/zig-out/lib"
TARGET_LIB="$OUT_DIR/libghostty.dylib"

if [ -f "$TARGET_LIB" ]; then
    exit 0
fi

mkdir -p "$OUT_DIR"

resolve_dir() {
    local dir="$1"
    if [ -z "$dir" ]; then
        return 1
    fi

    case "$dir" in
        /*) printf '%s\n' "$dir" ;;
        *) printf '%s\n' "$PWD/$dir" ;;
    esac
}

contains_dir() {
    local needle="$1"
    shift
    local item=""
    for item in "$@"; do
        if [ "$item" = "$needle" ]; then
            return 0
        fi
    done

    return 1
}

CACHE_DIRS=()
for raw_dir in "$GHOSTTY_DIR/.zig-cache" "${ZIG_LOCAL_CACHE_DIR:-}" "${ZIG_GLOBAL_CACHE_DIR:-}"; do
    resolved_dir="$(resolve_dir "$raw_dir" || true)"
    if [ -n "${resolved_dir:-}" ] && [ -d "$resolved_dir" ] && ! contains_dir "$resolved_dir" "${CACHE_DIRS[@]:-}"; then
        CACHE_DIRS+=("$resolved_dir")
    fi
done

CANDIDATES=()
for cache_dir in "${CACHE_DIRS[@]:-}"; do
    while IFS= read -r candidate; do
        if [ -n "$candidate" ]; then
            CANDIDATES+=("$candidate")
        fi
    done < <(
        find "$cache_dir" \
            -type f \
            -name 'libghostty.dylib' \
            -size +1048576c \
            -print 2>/dev/null
    )
done

LATEST_CANDIDATE=""
if [ "${#CANDIDATES[@]}" -gt 0 ]; then
    LATEST_CANDIDATE="$(
        printf '%s\0' "${CANDIDATES[@]}" \
            | xargs -0 ls -t 2>/dev/null \
            | head -n 1 \
            || true
    )"
fi

if [ -z "${LATEST_CANDIDATE:-}" ]; then
    echo "Could not locate a built libghostty.dylib in any known Zig cache directory." >&2
    printf 'Searched cache dirs:\n' >&2
    if [ "${#CACHE_DIRS[@]}" -eq 0 ]; then
        printf '  (none)\n' >&2
    else
        printf '  %s\n' "${CACHE_DIRS[@]}" >&2
    fi
    exit 1
fi

cp -L "$LATEST_CANDIDATE" "$TARGET_LIB"
echo "Materialized $TARGET_LIB from $LATEST_CANDIDATE"
