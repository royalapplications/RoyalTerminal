#!/usr/bin/env bash
# royalterminal-managed-personal-v1
set -euo pipefail
base=$(readlink -f -- "$HOME/.local/share/royalterminal-mux/current")
# Never attach a locally loaded admin credential to an explicitly selected foreign endpoint.
case ${1-} in --endpoint|--socket) exec dotnet "$base/cli/royalterminal-mux.dll" "$@";; esac
: "${XDG_RUNTIME_DIR:?Missing per-user runtime directory}"
file="$HOME/.config/royalterminal-mux/token"
[[ -O "$file" && -f "$file" && ! -L "$file" && $(stat -c %a -- "$file") == 600 ]] || { echo 'Missing or insecure personal credential' >&2; exit 1; }
IFS= read -r ROYALTERMINAL_MUX_TOKEN <"$file"; export ROYALTERMINAL_MUX_TOKEN
exec dotnet "$base/cli/royalterminal-mux.dll" --socket "$XDG_RUNTIME_DIR/royalterminal-mux/host.sock" "$@"
