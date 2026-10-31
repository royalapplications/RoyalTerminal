#!/usr/bin/env bash
# GitHub-hosted unprivileged runner. sudo is confined to curl, simulating the trusted root daemon's UID.
set -euo pipefail
umask 077
: "${GATEWAY:?set the absolute gateway executable path}" "${SERVER:?set the absolute server DLL path}"
d=$(mktemp -d); export ROYALTERMINAL_MUX_TOKEN="$(openssl rand -hex 32)"; export Kestrel__Endpoints__Personal__Url="http://unix:$d/broker.sock"
host=build.example.ts.net; login=tester@example.invalid
p=; g=
cleanup(){ [[ -z $g ]] || kill "$g" 2>/dev/null || :; [[ -z $p ]] || kill "$p" 2>/dev/null || :; wait || :; rm -rf "$d"; }; trap cleanup EXIT
dotnet "$SERVER" >"$d/host.log" 2>&1 & p=$!
"$GATEWAY" serve -listen "$d/serve.sock" -backend "$d/broker.sock" -host "$host" -allow "$login" >"$d/gate.log" 2>&1 & g=$!
for i in {1..150}; do if [[ -S $d/serve.sock && -S $d/broker.sock ]] && curl -fsS --unix-socket "$d/broker.sock" http://localhost/healthz >/dev/null; then break; fi; sleep .1; done
if ! [[ -S $d/serve.sock && -S $d/broker.sock ]]; then cat "$d/gate.log" >&2; exit 1; fi
args=(--max-time 4 -sS --unix-socket "$d/serve.sock" -H "Tailscale-User-Login: $login")
code(){ "$@" -o /dev/null -w '%{http_code}' http://localhost/api/context; }
[[ $(code curl "${args[@]}" -H "Authorization: Bearer $ROYALTERMINAL_MUX_TOKEN") == 403 ]]
[[ $(code sudo -n curl "${args[@]}") == 401 ]]
[[ $(code sudo -n curl "${args[@]}" -H 'Origin: https://evil.test' -H "Authorization: Bearer $ROYALTERMINAL_MUX_TOKEN") == 403 ]]
sudo -n curl -f "${args[@]}" -H "Origin: https://$host" -H "Authorization: Bearer $ROYALTERMINAL_MUX_TOKEN" http://localhost/api/context | jq -e '.administrator' >/dev/null
sudo -n curl -f "${args[@]}" http://localhost/.well-known/royalterminal-workspace | jq -e '.service=="royalterminal-workspace" and .auth=="bearer"' >/dev/null
echo 'Verified actual Unix peer UIDs, allowlisted ingress, distinct bearer authorization and origin enforcement.'
