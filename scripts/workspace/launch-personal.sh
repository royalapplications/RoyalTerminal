#!/usr/bin/env bash
# royalterminal-managed-personal-v1
set -euo pipefail
umask 077
: "${XDG_RUNTIME_DIR:?The personal service requires a user-owned runtime directory}"
root="$XDG_RUNTIME_DIR/royalterminal-mux"; token="$HOME/.config/royalterminal-mux/token"
[[ -d "$root" && ! -L "$root" && -O "$root" && $(stat -c %a -- "$root") == 700 ]] || { echo 'Missing or insecure personal runtime directory' >&2; exit 1; }
[[ -f "$token" && -O "$token" && ! -L "$token" && $(stat -c %a -- "$token") == 600 ]] || { echo 'Missing or insecure personal credential' >&2; exit 1; }
root_release=$(readlink -f -- "$HOME/.local/share/royalterminal-mux/current")
IFS= read -r ROYALTERMINAL_MUX_TOKEN <"$token"; export ROYALTERMINAL_MUX_TOKEN
export ROYALTERMINAL_MUX_CONFIG="$HOME/.config/royalterminal-mux/automation.json"
unset ROYALTERMINAL_MUX_ALLOW_REMOTE ASPNETCORE_URLS
# An explicit Kestrel endpoint takes precedence over the sample's local HTTP URL.
exec dotnet "$root_release/server/RoyalTerminal.Multiplexing.Server.dll" "--Kestrel:Endpoints:Personal:Url=http://unix:$root/host.sock"
